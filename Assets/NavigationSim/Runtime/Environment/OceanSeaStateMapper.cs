using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Converts instructor sea state (Hs / Tp / wind) into North Star iFFT
    /// <c>OceanSettings</c> knobs. Pure C# so the navigation core stays testable.
    /// </summary>
    public static class OceanSeaStateMapper
    {
        private const double G = 9.81;

        public struct OceanDriveParams
        {
            public float WindSpeedMs;
            public float Directionality;
            public float Choppyness;
            public float PatchSizeM;
            public float MinWaveSize;
            public float WindFromDeg;
            public float WaveLengthM;
        }

        public static OceanDriveParams FromEnvironment(EnvironmentState env)
        {
            double hs = Math.Max(0.0, env.WaveHeightM);
            double tp = Math.Max(1.0, env.WavePeriodS);

            // Deep-water peak wavelength λ = g · Tp² / (2π).
            double lambda = G * tp * tp / (2.0 * Math.PI);

            // Prefer measured wind; otherwise invert a compact Hs≈a·U² relation.
            double windMs = env.WindSpeedMs > 0.5
                ? env.WindSpeedMs
                : Math.Sqrt(Math.Max(0.25, hs) / 0.0246);
            windMs = Clamp(windMs, 1.5, 28.0);

            // Patch covers several wavelengths so the FFT can form the peak sea.
            double patch = Clamp(lambda * 3.5, 48.0, 256.0);

            // Confused / low seas → lower directionality; wind sea → tighter.
            double dir = Clamp(0.55 + 0.40 * (windMs / 20.0), 0.45, 0.95);

            double chop = Clamp(0.35 + 0.12 * hs, 0.35, 1.0);

            // Fade ripples that alias at coarse resolutions when seas are large.
            double minWave = hs < 0.8 ? 0.001 : Clamp(0.002 * hs, 0.001, 0.05);

            // Spectrum direction follows waves; fall back to wind-from.
            double fromDeg = Math.Abs(env.WaveHeightM) > 0.05 ? env.WaveFromDeg : env.WindFromDeg;

            return new OceanDriveParams
            {
                WindSpeedMs = (float)windMs,
                Directionality = (float)dir,
                Choppyness = (float)chop,
                PatchSizeM = (float)patch,
                MinWaveSize = (float)minWave,
                WindFromDeg = (float)fromDeg,
                WaveLengthM = (float)lambda
            };
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
