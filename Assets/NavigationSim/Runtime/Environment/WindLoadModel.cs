using System;

namespace NavigationSim.Core
{
    public enum BlendermannVesselType
    {
        CarCarrier = 0,
        CargoLoaded = 1,
        CargoContainerOnDeck = 2,
        ContainerShipLoaded = 3,
        Destroyer = 4,
        DivingSupport = 5,
        DrillingVessel = 6,
        Ferry = 7,
        FishingVessel = 8,
        LngTanker = 9,
        OffshoreSupply = 10,
        PassengerLiner = 11,
        ResearchVessel = 12,
        SpeedBoat = 13,
        TankerLoaded = 14,
        TankerBallast = 15,
        Tender = 16
    }

    [Serializable]
    public class WindageParams
    {
        public double FrontalAreaM2 = 1200.0;   // AFw
        public double LateralAreaM2 = 3900.0;   // ALw
        public double CentroidAboveWaterM = 12.0;   // sL (vertical centroid of ALw)
        public double CentroidFromMidshipM = 8.0;   // sH (horizontal centroid of ALw)
        public double LoaM = 333.0;
        public BlendermannVesselType VesselType = BlendermannVesselType.TankerLoaded;
    }

    /// <summary>
    /// Port of MSS LIBRARY/environment/blendermann94.m (Blendermann 1994,
    /// Fossen 2021 Ch. 10.1.2). Returns wind loads in body axes.
    /// </summary>
    public static class WindLoadModel
    {
        // BDATA rows: CDt, CDl_AF(0), CDl_AF(pi), delta, kappa
        private static readonly double[,] Bdata =
        {
            { 0.95, 0.55, 0.60, 0.80, 1.2 },
            { 0.85, 0.65, 0.55, 0.40, 1.7 },
            { 0.85, 0.55, 0.50, 0.40, 1.4 },
            { 0.90, 0.55, 0.55, 0.40, 1.4 },
            { 0.85, 0.60, 0.65, 0.65, 1.1 },
            { 0.90, 0.60, 0.80, 0.55, 1.7 },
            { 1.00, 0.85, 0.925, 0.10, 1.7 },
            { 0.90, 0.45, 0.50, 0.80, 1.1 },
            { 0.95, 0.70, 0.70, 0.40, 1.1 },
            { 0.70, 0.60, 0.65, 0.50, 1.1 },
            { 0.90, 0.55, 0.80, 0.55, 1.2 },
            { 0.90, 0.40, 0.40, 0.80, 1.2 },
            { 0.85, 0.55, 0.65, 0.60, 1.4 },
            { 0.90, 0.55, 0.60, 0.60, 1.1 },
            { 0.70, 0.90, 0.55, 0.40, 3.1 },
            { 0.70, 0.75, 0.55, 0.40, 2.2 },
            { 0.85, 0.55, 0.55, 0.65, 1.1 }
        };

        /// <summary>
        /// Wind loads for the current motion state. Uses the relative (apparent)
        /// wind computed from the ship velocity, per Fossen 2021 eq. 10.2.
        /// </summary>
        public static ExternalForces Compute(WindageParams w, EnvironmentState env,
            double psiRad, double u, double v)
        {
            ExternalForces outF = default;
            if (env.WindSpeedMs < 0.05 || w.LateralAreaM2 < 1.0 || w.FrontalAreaM2 < 1.0)
            {
                return outF;
            }

            // Wind FROM direction → going-to direction.
            double betaGoingTo = (env.WindFromDeg + 180.0) * Math.PI / 180.0;

            double uw = env.WindSpeedMs * Math.Cos(betaGoingTo - psiRad);
            double vw = env.WindSpeedMs * Math.Sin(betaGoingTo - psiRad);

            double urw = u - uw;
            double vrw = v - vw;
            double Vr = Math.Sqrt(urw * urw + vrw * vrw);
            if (Vr < 0.05)
            {
                return outF;
            }

            double gammaR = -Math.Atan2(vrw, urw); // 0 = head wind

            int row = (int)w.VesselType;
            double cdt = Bdata[row, 0];
            double cdlAf = Math.Abs(gammaR) <= Math.PI / 2.0 ? Bdata[row, 1] : Bdata[row, 2];
            double delta = Bdata[row, 3];
            double kappa = Bdata[row, 4];

            double cdl = cdlAf * w.FrontalAreaM2 / w.LateralAreaM2;
            double s2 = Math.Sin(2.0 * gammaR);
            double den = 1.0 - 0.5 * delta * (1.0 - cdl / cdt) * s2 * s2;

            double cx = -cdlAf * Math.Cos(gammaR) / den;
            double cy = cdt * Math.Sin(gammaR) / den;
            // Yaw moment uses the longitudinal centroid of ALw (Blendermann 1994).
            // Symmetric extension of the bracket: |gamma| instead of gamma.
            double cn = (w.CentroidFromMidshipM / w.LoaM
                         - 0.18 * (Math.Abs(gammaR) - Math.PI / 2.0)) * cy;

            double q = 0.5 * env.AirDensity * Vr * Vr;
            outF.X = q * cx * w.FrontalAreaM2;
            outF.Y = q * cy * w.LateralAreaM2;
            outF.N = q * cn * w.LateralAreaM2 * w.LoaM;
            return outF;
        }
    }
}
