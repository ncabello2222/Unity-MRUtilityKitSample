using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Channel B: visual heave/roll/pitch. Never alters the 3DOF trajectory.
    /// <list type="bullet">
    /// <item>Parametric mode — encounter-frequency sinusoids (fallback).</item>
    /// <item>Surface mode — five-point samples from <see cref="IOceanSurface"/>
    /// filtered with ship length/beam response (Phase 3).</item>
    /// </list>
    /// </summary>
    public class WaveResponseModel
    {
        private const double G = 9.81;

        private double _phaseHeave;
        private double _phasePitch;
        private double _phaseRoll;

        private double _heave;
        private double _rollDeg;
        private double _pitchDeg;
        private double _heaveVel;
        private double _rollVel;
        private double _pitchVel;

        /// <summary>When set, seakeeping follows the shared ocean surface.</summary>
        public IOceanSurface Surface { get; set; }

        public bool UseSurfaceSampling { get; set; } = true;

        public double HeaveM => _heave;
        public double RollDeg => _rollDeg;
        public double PitchDeg => _pitchDeg;

        public void Update(EnvironmentState env, ShipState state, double shipLength, double shipBeam, double dt)
        {
            if (UseSurfaceSampling && Surface != null)
            {
                UpdateFromSurface(env, state, shipLength, shipBeam, dt);
                return;
            }

            UpdateParametric(env, state.PsiRad, state.StwMs, shipLength, shipBeam, dt);
        }

        private void UpdateFromSurface(EnvironmentState env, ShipState state,
            double shipLength, double shipBeam, double dt)
        {
            if (env.WaveHeightM < 0.01 || env.WavePeriodS < 1.0)
            {
                Decay(dt);
                return;
            }

            double halfL = Math.Max(1.0, shipLength * 0.45);
            double halfB = Math.Max(0.5, shipBeam * 0.45);
            double yaw = state.PsiRad;
            double fE = Math.Sin(yaw);
            double fN = Math.Cos(yaw);
            // Starboard in NED/east-north: +90° from bow.
            double sE = Math.Cos(yaw);
            double sN = -Math.Sin(yaw);

            double e = state.East;
            double n = state.North;
            double t = state.TimeS;

            double hC = Surface.SampleHeight(e, n, t);
            double hBow = Surface.SampleHeight(e + fE * halfL, n + fN * halfL, t);
            double hStern = Surface.SampleHeight(e - fE * halfL, n - fN * halfL, t);
            double hStbd = Surface.SampleHeight(e + sE * halfB, n + sN * halfB, t);
            double hPort = Surface.SampleHeight(e - sE * halfB, n - sN * halfB, t);

            double lambda = G * env.WavePeriodS * env.WavePeriodS / (2.0 * Math.PI);
            double sizeFactor = Math.Exp(-1.2 * shipLength / Math.Max(1.0, lambda));
            double beamSizeFactor = Math.Exp(-0.35 * shipBeam / Math.Max(1.0, lambda));

            // Mean-subtract so absolute waterline offsets do not lift the ship.
            double heaveTarget = hC * Math.Max(0.08, sizeFactor);

            // PitchDeg > 0 = bow down; RollDeg > 0 = starboard down.
            double pitchTarget = Math.Atan2(hStern - hBow, 2.0 * halfL) * (180.0 / Math.PI)
                                 * Math.Max(0.08, sizeFactor);
            double rollTarget = Math.Atan2(hPort - hStbd, 2.0 * halfB) * (180.0 / Math.PI)
                                * Math.Max(0.1, beamSizeFactor);

            // Mild resonance boost for roll near natural period (visual only).
            double w0 = 2.0 * Math.PI / env.WavePeriodS;
            double fromRel = env.WaveFromDeg * Math.PI / 180.0 - yaw;
            double betaProp = fromRel + Math.PI;
            double we = Math.Abs(w0 - w0 * w0 * state.StwMs * Math.Cos(betaProp) / G);
            we = Math.Max(0.05, we);
            double wRoll = 2.0 * Math.PI / Math.Max(2.0, env.RollNaturalPeriodS);
            double ratio = we / wRoll;
            double xi = 0.12;
            double amplification = 1.0 / Math.Sqrt(Math.Pow(1.0 - ratio * ratio, 2)
                                   + Math.Pow(2.0 * xi * ratio, 2));
            amplification = Math.Min(2.2, amplification);
            rollTarget = Math.Min(22.0, rollTarget * amplification);

            // Second-order filters: inertia + damping (tankers should not slap every ripple).
            double heaveWn = Clamp(1.4 * sizeFactor + 0.25, 0.35, 1.6);
            double pitchWn = Clamp(1.2 * sizeFactor + 0.2, 0.3, 1.4);
            double rollWn = Clamp(wRoll * 0.85, 0.25, 1.2);

            StepMassSpring(ref _heave, ref _heaveVel, heaveTarget, heaveWn, 0.55, dt);
            StepMassSpring(ref _pitchDeg, ref _pitchVel, pitchTarget, pitchWn, 0.50, dt);
            StepMassSpring(ref _rollDeg, ref _rollVel, rollTarget, rollWn, 0.35, dt);

            _heave = Clamp(_heave, -env.WaveHeightM, env.WaveHeightM);
            _pitchDeg = Clamp(_pitchDeg, -18.0, 18.0);
            _rollDeg = Clamp(_rollDeg, -25.0, 25.0);
        }

        private void UpdateParametric(EnvironmentState env, double psiRad, double speedMs,
            double shipLength, double shipBeam, double dt)
        {
            if (env.WaveHeightM < 0.01 || env.WavePeriodS < 1.0)
            {
                Decay(dt);
                return;
            }

            double w0 = 2.0 * Math.PI / env.WavePeriodS;
            double lambda = 2.0 * Math.PI * G / (w0 * w0);
            double k = 2.0 * Math.PI / lambda;

            double fromRel = env.WaveFromDeg * Math.PI / 180.0 - psiRad;
            double betaProp = fromRel + Math.PI;

            double we = Math.Abs(w0 - w0 * w0 * speedMs * Math.Cos(betaProp) / G);
            we = Math.Max(0.05, we);

            double amp = 0.5 * env.WaveHeightM;
            double slopeRad = Math.Atan(k * amp);

            double sizeFactor = Math.Exp(-1.2 * shipLength / Math.Max(1.0, lambda));
            double headFactor = Math.Abs(Math.Cos(fromRel));
            double beamFactor = Math.Abs(Math.Sin(fromRel));

            double heaveAmp = amp * Math.Max(0.05, sizeFactor);
            double pitchAmpDeg = slopeRad * 180.0 / Math.PI * sizeFactor * headFactor;

            double wRoll = 2.0 * Math.PI / Math.Max(2.0, env.RollNaturalPeriodS);
            double ratio = we / wRoll;
            double xi = 0.10;
            double amplification = 1.0 / Math.Sqrt(Math.Pow(1.0 - ratio * ratio, 2)
                                   + Math.Pow(2.0 * xi * ratio, 2));
            amplification = Math.Min(3.5, amplification);
            double beamSizeFactor = Math.Exp(-0.35 * shipBeam / Math.Max(1.0, lambda));
            double rollAmpDeg = Math.Min(25.0,
                slopeRad * 180.0 / Math.PI * beamFactor * amplification * Math.Max(0.1, beamSizeFactor));

            _phaseHeave += we * dt;
            _phasePitch += we * dt;
            _phaseRoll += we * dt;

            _heave = heaveAmp * Math.Sin(_phaseHeave);
            _pitchDeg = pitchAmpDeg * Math.Sin(_phasePitch + 0.6);
            _rollDeg = rollAmpDeg * Math.Sin(_phaseRoll + 1.1);
            _heaveVel = _rollVel = _pitchVel = 0.0;
        }

        private void Decay(double dt)
        {
            double k = Math.Pow(0.98, Math.Max(1.0, dt / 0.02));
            _heave *= k;
            _rollDeg *= k;
            _pitchDeg *= k;
            _heaveVel *= k;
            _rollVel *= k;
            _pitchVel *= k;
        }

        private static void StepMassSpring(ref double x, ref double v, double target,
            double wn, double zeta, double dt)
        {
            // ẍ + 2ζωẋ + ω²(x - target) = 0
            double acc = -2.0 * zeta * wn * v - wn * wn * (x - target);
            v += acc * dt;
            x += v * dt;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public void Reset()
        {
            _phaseHeave = _phasePitch = _phaseRoll = 0.0;
            _heave = _rollDeg = _pitchDeg = 0.0;
            _heaveVel = _rollVel = _pitchVel = 0.0;
        }
    }
}
