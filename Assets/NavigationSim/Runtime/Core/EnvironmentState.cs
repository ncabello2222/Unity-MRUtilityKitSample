namespace NavigationSim.Core
{
    /// <summary>
    /// Instructor-controlled environment. Angles are compass degrees.
    /// Current uses the direction it flows TOWARD (set); wind and waves use the
    /// direction they come FROM (maritime convention).
    /// </summary>
    public class EnvironmentState
    {
        public double CurrentSpeedMs;
        public double CurrentSetToDeg;

        public double WindSpeedMs;
        public double WindFromDeg;

        /// <summary>Significant wave height Hs [m].</summary>
        public double WaveHeightM = 0.5;

        /// <summary>Peak period Tp [s].</summary>
        public double WavePeriodS = 8.0;

        public double WaveFromDeg;

        public double WaterDensity = 1025.0;
        public double AirDensity = 1.224;
        public double WaterDepthM = 60.0;

        /// <summary>Natural roll period of the vessel [s], used by the visual wave response.</summary>
        public double RollNaturalPeriodS = 12.0;
    }
}
