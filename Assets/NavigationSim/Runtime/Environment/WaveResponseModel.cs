using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Channel B of the master plan: parametric heave/roll/pitch for the visual
    /// motion of the horizon. It never alters the 3DOF trajectory. Encounter
    /// frequency follows MSS encounter.m; amplitudes are closed-form estimates
    /// in the spirit of Vessel.js WaveMotion (small-parameter visual response).
    /// </summary>
    public class WaveResponseModel
    {
        private const double G = 9.81;

        private double _phaseHeave;
        private double _phasePitch;
        private double _phaseRoll;

        public double HeaveM { get; private set; }
        public double RollDeg { get; private set; }
        public double PitchDeg { get; private set; }

        public void Update(EnvironmentState env, double psiRad, double speedMs,
            double shipLength, double shipBeam, double dt)
        {
            if (env.WaveHeightM < 0.01 || env.WavePeriodS < 1.0)
            {
                HeaveM *= 0.98;
                RollDeg *= 0.98;
                PitchDeg *= 0.98;
                return;
            }

            double w0 = 2.0 * Math.PI / env.WavePeriodS;
            double lambda = 2.0 * Math.PI * G / (w0 * w0);
            double k = 2.0 * Math.PI / lambda;

            // Direction of wave propagation relative to the bow (pi = head seas).
            double fromRel = env.WaveFromDeg * Math.PI / 180.0 - psiRad;
            double betaProp = fromRel + Math.PI;

            // Encounter frequency (MSS encounter.m): we = w0 - w0²·U·cos(beta)/g.
            double we = Math.Abs(w0 - w0 * w0 * speedMs * Math.Cos(betaProp) / G);
            we = Math.Max(0.05, we);

            double amp = 0.5 * env.WaveHeightM;
            double slopeRad = Math.Atan(k * amp);

            // Long hulls barely respond to short waves.
            double sizeFactor = Math.Exp(-1.2 * shipLength / Math.Max(1.0, lambda));
            double headFactor = Math.Abs(Math.Cos(fromRel));
            double beamFactor = Math.Abs(Math.Sin(fromRel));

            double heaveAmp = amp * Math.Max(0.05, sizeFactor);
            double pitchAmpDeg = slopeRad * 180.0 / Math.PI * sizeFactor * headFactor;

            // Roll with resonance around the natural period, damped cap.
            double wRoll = 2.0 * Math.PI / Math.Max(2.0, env.RollNaturalPeriodS);
            double ratio = we / wRoll;
            double xi = 0.10; // effective damping
            double amplification = 1.0 / Math.Sqrt(Math.Pow(1.0 - ratio * ratio, 2)
                                   + Math.Pow(2.0 * xi * ratio, 2));
            amplification = Math.Min(3.5, amplification);
            double beamSizeFactor = Math.Exp(-0.35 * shipBeam / Math.Max(1.0, lambda));
            double rollAmpDeg = Math.Min(25.0,
                slopeRad * 180.0 / Math.PI * beamFactor * amplification * Math.Max(0.1, beamSizeFactor));

            _phaseHeave += we * dt;
            _phasePitch += we * dt;
            _phaseRoll += we * dt;

            HeaveM = heaveAmp * Math.Sin(_phaseHeave);
            PitchDeg = pitchAmpDeg * Math.Sin(_phasePitch + 0.6);
            RollDeg = rollAmpDeg * Math.Sin(_phaseRoll + 1.1);
        }

        public void Reset()
        {
            _phaseHeave = _phasePitch = _phaseRoll = 0.0;
            HeaveM = RollDeg = PitchDeg = 0.0;
        }
    }
}
