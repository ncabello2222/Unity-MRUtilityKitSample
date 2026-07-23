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

        /// <summary>Rudder command currently driving the gear (after mode resolution).</summary>
        public double ResolvedRudderCommandDeg { get; private set; }

        public ShipSimulator(VesselConfig config, EnvironmentState environment)
        {
            Config = config;
            Environment = environment;
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

            double rudderRad = Rudder.AngleDeg * Math.PI / 180.0;
            double rps = Engine.Rps;
            IManeuveringModel model = Config.ModelType == ManeuveringModelType.MmgCalibrated
                ? (IManeuveringModel)_mmg
                : _clarke;

            _rk4.Step((x, dxdt) => model.Derivatives(x, rudderRad, rps, _externalForces, env, dxdt),
                _x, dt);

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
