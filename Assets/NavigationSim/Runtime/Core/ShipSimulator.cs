using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Deterministic navigation core: command resolution → actuators → forces →
    /// RK4 → diagnostics. Pure C# (no Unity types) so it can be regression-tested
    /// outside the engine. Runs at a fixed step (50 Hz recommended).
    /// </summary>
    public class ShipSimulator
    {
        public const double DefaultDt = 0.02;

        public VesselConfig Config;
        public EnvironmentState Environment;
        public ShipState State = new ShipState();
        public ShipCommand Command = new ShipCommand();

        public RudderActuator Rudder = new RudderActuator();
        public EngineShaftModel Engine = new EngineShaftModel();
        public FuelConsumptionModel Fuel = new FuelConsumptionModel();
        public WaveResponseModel Waves = new WaveResponseModel();
        public HeadingAutopilot Autopilot = new HeadingAutopilot();

        private Mmg3DofModel _mmg;
        private Clarke83Model _clarke;
        private readonly Rk4Integrator _rk4 = new Rk4Integrator(6);
        private readonly double[] _x = new double[6];
        private ExternalForces _externalForces;

        // Parameters the derivative needs but the integrator does not know about,
        // held constant over the step. See EvaluateDerivatives.
        private readonly StateDerivative _derivatives;
        private IManeuveringModel _stepModel;
        private double _stepRudderRad;
        private double _stepRps;

        /// <summary>Rudder command currently driving the gear (after mode resolution).</summary>
        public double ResolvedRudderCommandDeg { get; private set; }

        public ShipSimulator(VesselConfig config, EnvironmentState environment)
        {
            Config = config;
            Environment = environment;
            _derivatives = EvaluateDerivatives;
            RebuildModels();
        }

        /// <summary>Call after replacing Config (e.g. vessel preset change).</summary>
        public void RebuildModels()
        {
            _mmg = new Mmg3DofModel(Config.MmgBasic, Config.MmgManeuvering)
            {
                AsternThrustFactor = Config.Propeller.AsternThrustFactor
            };
            _clarke = new Clarke83Model(Config.Clarke, Config.Propeller);
        }

        public double ShipLength => Config.ModelType == ManeuveringModelType.MmgCalibrated
            ? Config.MmgBasic.Lpp
            : Config.Clarke.L;

        public double ShipBeam => Config.ModelType == ManeuveringModelType.MmgCalibrated
            ? Config.MmgBasic.B
            : Config.Clarke.B;

        public void Step(double dt)
        {
            ShipCommand cmd = Command;
            EnvironmentState env = Environment;

            // ── Steering mode resolution ────────────────────────────────────
            bool pumps = cmd.AnySteeringPump;
            switch (cmd.SteeringMode)
            {
                case SteeringMode.Nfu:
                    Rudder.UpdateNfu(cmd.NfuDirection, pumps, Config.Rudder, dt);
                    ResolvedRudderCommandDeg = Rudder.AngleDeg;
                    break;

                case SteeringMode.Auto:
                    ResolvedRudderCommandDeg = Autopilot.Update(Config.Autopilot,
                        State.HeadingDeg, cmd.HeadingSetpointDeg,
                        State.R * 180.0 / Math.PI, dt);
                    Rudder.Update(ResolvedRudderCommandDeg, pumps, Config.Rudder, dt);
                    break;

                default:
                    ResolvedRudderCommandDeg = cmd.RudderCommandDeg;
                    Rudder.Update(ResolvedRudderCommandDeg, pumps, Config.Rudder, dt);
                    break;
            }

            // ── Engine order ────────────────────────────────────────────────
            double targetRps = 0.0;
            if (!cmd.EmergencyStop && cmd.EngineReady)
            {
                targetRps = cmd.TelegraphFraction * Config.Engine.RatedRps;
            }

            Engine.Update(targetRps, Config.Engine, dt);

            // ── External loads held constant over the step ──────────────────
            _externalForces = WindLoadModel.Compute(Config.Windage, env,
                State.PsiRad, State.U, State.V);

            CurrentModel.BodyCurrent(env, State.PsiRad, out double uc0, out double vc0);
            double urNow = State.U - uc0;
            double thrusterFade = 1.0 - Math.Min(1.0,
                Math.Abs(urNow) / Math.Max(0.5, Config.BowThruster.FadeOutSpeedMs));
            State.BowThrusterEffective = cmd.BowThruster * thrusterFade;
            double yBow = cmd.BowThruster * Config.BowThruster.MaxThrustN * thrusterFade;
            _externalForces.Y += yBow;
            _externalForces.N += yBow * Config.BowThruster.LongitudinalPositionM;

            // ── Integrate the 6-state maneuvering model ─────────────────────
            _x[0] = State.U;
            _x[1] = State.V;
            _x[2] = State.R;
            _x[3] = State.North;
            _x[4] = State.East;
            _x[5] = State.PsiRad;

            double rps = Engine.Rps;
            _stepRudderRad = Rudder.AngleDeg * Math.PI / 180.0;
            _stepRps = rps;
            _stepModel = Config.ModelType == ManeuveringModelType.MmgCalibrated
                ? (IManeuveringModel)_mmg
                : _clarke;

            _rk4.Step(_derivatives, _x, dt);

            State.U = _x[0];
            State.V = _x[1];
            State.R = _x[2];
            State.North = _x[3];
            State.East = _x[4];
            State.PsiRad = WrapRad(_x[5]);
            State.RudderAngleDeg = Rudder.AngleDeg;
            State.ShaftRps = rps;

            // ── Diagnostics: propulsion, fuel, water-relative speeds ────────
            CurrentModel.BodyCurrent(env, State.PsiRad, out double uc, out double vc);
            State.Ur = State.U - uc;
            State.Vr = State.V - vc;

            // The water mass itself travels with the current. Integrated here so the
            // visual wave field can ride on it at the same (possibly fast-time) rate
            // the trajectory advances.
            double setRad = env.CurrentSetToDeg * Math.PI / 180.0;
            State.WaterDriftNorth += env.CurrentSpeedMs * Math.Cos(setRad) * dt;
            State.WaterDriftEast += env.CurrentSpeedMs * Math.Sin(setRad) * dt;

            // Anemometer channel: same relative wind the load model integrates, kept
            // as a reading so the conning panel need not re-derive it.
            WindLoadModel.ApparentWind(env, State.PsiRad, State.U, State.V,
                out double appWindMs, out double appFromRelRad);
            State.ApparentWindSpeedMs = appWindMs;
            State.ApparentWindFromRelDeg = ShipState.Normalize360(appFromRelRad * 180.0 / Math.PI);

            PropellerModel.Compute(Config.Propeller, State.Ur, rps, env.WaterDensity,
                out double j, out double thrustN, out double torqueNm, out double powerW);
            State.AdvanceRatioJ = j;
            State.PropThrustN = thrustN;
            State.PropTorqueNm = torqueNm;
            State.ShaftPowerW = powerW;
            State.EngineLoad = Config.Engine.McrKw > 1.0
                ? powerW / 1000.0 / Config.Engine.McrKw
                : 0.0;

            Fuel.Update(Config.Engine, powerW, dt);
            State.FuelFlowKgPerH = Fuel.FuelFlowKgPerS * 3600.0;
            State.FuelUsedKg = Fuel.FuelUsedKg;

            // ── Visual seakeeping channel ───────────────────────────────────
            Waves.Update(env, State, ShipLength, ShipBeam, dt);
            State.HeaveM = Waves.HeaveM;
            State.RollDeg = Waves.RollDeg;
            State.PitchDeg = Waves.PitchDeg;

            State.TimeS += dt;
        }

        /// <summary>
        /// The function RK4 evaluates four times per step. It reads the model, the
        /// rudder angle, the shaft rate and the external loads off fields instead of
        /// a closure: written as a lambda at the call site it captured four locals
        /// plus <c>this</c>, so the compiler built a display class and a delegate on
        /// every step — two short-lived objects per step, and the runner takes 3000
        /// steps a second at x60 fast time. The delegate is now made once, in the
        /// constructor. Anything this reads must be held constant across the step,
        /// which is exactly what the four evaluation points of RK4 assume anyway.
        /// </summary>
        private void EvaluateDerivatives(double[] x, double[] dxdt)
        {
            _stepModel.Derivatives(x, _stepRudderRad, _stepRps, _externalForces, Environment, dxdt);
        }

        public void Reset()
        {
            State.Reset();
            Rudder.Reset();
            Engine.Reset();
            Fuel.Reset();
            Waves.Reset();
            Autopilot.Reset();
        }

        private static double WrapRad(double a)
        {
            while (a > Math.PI)
            {
                a -= 2.0 * Math.PI;
            }

            while (a < -Math.PI)
            {
                a += 2.0 * Math.PI;
            }

            return a;
        }
    }
}
