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

        /// <summary>
        /// Phillips energy peaks at k = 1/(L√2) with L = U²/g, so the peak wavelength is
        /// 2π√2·U². Inverting it gives the wind speed whose spectrum peaks at the
        /// instructor's Tp: U = g·Tp / (2π·2^¼).
        /// </summary>
        private const double SpectrumWindPerPeriod = 9.81 / (2.0 * Math.PI * 1.189207115);

        public struct OceanDriveParams
        {
            /// <summary>
            /// Wind speed the <em>spectrum</em> is built from — derived from Tp so the
            /// rendered sea has the ordered period, not the meteorological wind (which
            /// still drives hull wind loads through <see cref="WindLoadModel"/>).
            /// </summary>
            public float WindSpeedMs;
            public float Directionality;
            public float Choppyness;
            public float PatchSizeM;
            public float MinWaveSize;
            public float WindFromDeg;
            public float WaveLengthM;
            /// <summary>Significant wave height the rendered field must actually show [m].</summary>
            public float TargetHsM;
        }

        public static OceanDriveParams FromEnvironment(EnvironmentState env)
        {
            double hs = Math.Max(0.0, env.WaveHeightM);
            double tp = Math.Max(1.0, env.WavePeriodS);

            // Deep-water peak wavelength λ = g · Tp² / (2π).
            double lambda = G * tp * tp / (2.0 * Math.PI);

            // Tp sets the spectrum's shape, Hs its amplitude — the two knobs the
            // instructor actually orders. Deriving the spectrum wind from the measured
            // wind instead (the old behaviour) let the rendered sea contradict both:
            // the panel could announce Hs 5 m over a mirror, or a flat-calm order could
            // render a gale. Amplitude is trimmed separately by the ocean adapter.
            double windMs = Clamp(tp * SpectrumWindPerPeriod, 1.5, 45.0);

            // Patch covers a few peak wavelengths so the FFT can form the ordered sea.
            // It must be an INTEGER multiple of the peak wavelength: the FFT only carries
            // k = 2πn/patch, so a patch of 2.5λ puts the spectral peak between modes and
            // the sea comes out ~9% short in period no matter how well the wind is
            // derived. Snapping to a whole number of wavelengths lands the peak on mode n.
            double patchTarget = Clamp(lambda * 2.5, 64.0, 512.0);
            double nWaves = Math.Max(1.0, Math.Floor(patchTarget / lambda + 0.5));
            // Long swell needs a big patch; the cost is texel size (patch / FFT
            // resolution), so this trades near-field ripple detail for period fidelity.
            double patch = Math.Min(nWaves * lambda, 768.0);

            // Confused / low seas → lower directionality; developed sea → tighter.
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
                WaveLengthM = (float)lambda,
                TargetHsM = (float)hs
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
