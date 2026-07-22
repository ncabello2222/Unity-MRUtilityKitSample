using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Abstraction over the visual ocean (§9.2 of the master plan). Any render
    /// asset (Ocean Community Next Gen or a simple plane) is hidden behind this.
    /// The Unity adapter (<c>OceanNextGenAdapter</c>) is the only place that
    /// may reference the Ocean CNG asset directly.
    /// </summary>
    public interface IOceanSurface
    {
        double SampleHeight(double east, double north, double timeS);

        /// <summary>
        /// Drive the visual ocean's modular phase / inverse heading so waves
        /// scroll under the fixed bridge. No-op for analytic surfaces.
        /// </summary>
        void SetVirtualShipPosition(double east, double north, double headingDeg);
    }

    /// <summary>
    /// Minimal analytic surface: three directional sinusoids derived from the
    /// instructor's Hs/Tp/direction. Enough for wake/heave sampling until a
    /// full ocean asset is adapted.
    /// </summary>
    public class SimpleOceanSurface : IOceanSurface
    {
        private const double G = 9.81;
        private readonly EnvironmentState _env;

        public SimpleOceanSurface(EnvironmentState env)
        {
            _env = env;
        }

        public double SampleHeight(double east, double north, double timeS)
        {
            if (_env.WaveHeightM < 0.01 || _env.WavePeriodS < 1.0)
            {
                return 0.0;
            }

            double height = 0.0;
            // Component fractions of Hs and relative periods for a small spread.
            double[] ampFrac = { 0.50, 0.30, 0.20 };
            double[] periodFrac = { 1.0, 0.72, 1.31 };
            double[] dirOffsetDeg = { 0.0, 22.0, -28.0 };

            for (int i = 0; i < 3; i++)
            {
                double tp = _env.WavePeriodS * periodFrac[i];
                double w = 2.0 * Math.PI / tp;
                double k = w * w / G;
                double dir = (_env.WaveFromDeg + dirOffsetDeg[i] + 180.0) * Math.PI / 180.0;
                double kx = k * Math.Sin(dir); // east component of the wave vector
                double ky = k * Math.Cos(dir); // north component
                double amp = 0.5 * _env.WaveHeightM * ampFrac[i];
                height += amp * Math.Sin(kx * east + ky * north - w * timeS + i * 1.7);
            }

            return height;
        }

        public void SetVirtualShipPosition(double east, double north, double headingDeg)
        {
            // Analytic surface has no visual root to drive.
        }
    }
}
