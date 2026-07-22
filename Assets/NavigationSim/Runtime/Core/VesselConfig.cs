using System;

namespace NavigationSim.Core
{
    public enum ManeuveringModelType
    {
        MmgCalibrated = 0,
        Clarke83Generic = 1
    }

    [Serializable]
    public class BowThrusterParams
    {
        public double MaxThrustN = 250000.0;      // ~25 t bow thruster
        public double LongitudinalPositionM = 140.0;
        public double FadeOutSpeedMs = 4.0;       // effectiveness lost with headway
    }

    /// <summary>
    /// Everything that defines one vessel. All values are editable live from the
    /// configuration canvas; the simulator holds a reference, so edits apply on
    /// the next physics tick.
    /// </summary>
    public class VesselConfig
    {
        public string Name = "Ship";
        public ManeuveringModelType ModelType = ManeuveringModelType.MmgCalibrated;

        public MmgBasicParams MmgBasic = new MmgBasicParams();
        public MmgManeuveringParams MmgManeuvering = new MmgManeuveringParams();
        public Clarke83Params Clarke = new Clarke83Params();

        public RudderParams Rudder = new RudderParams();
        public EngineParams Engine = new EngineParams();
        public PropellerParams Propeller = new PropellerParams();
        public WindageParams Windage = new WindageParams();
        public BowThrusterParams BowThruster = new BowThrusterParams();
        public AutopilotParams Autopilot = new AutopilotParams();

        /// <summary>Telegraph detents Full Astern..Full Ahead → signed rpm fraction.</summary>
        public double[] TelegraphFractions =
        {
            -0.75, -0.55, -0.35, -0.20, 0.0, 0.25, 0.45, 0.70, 1.00
        };

        public double TelegraphFraction(int order)
        {
            int index = order + 4;
            if (index < 0 || index >= TelegraphFractions.Length)
            {
                return 0.0;
            }

            return TelegraphFractions[index];
        }

        /// <summary>Keep the MMG propeller entries and PropellerParams coherent.</summary>
        public void SyncPropellerIntoMmg()
        {
            MmgBasic.Dp = Propeller.Diameter;
            MmgBasic.tP = Propeller.ThrustDeduction;
            MmgBasic.wP0 = Propeller.WakeFraction;
            MmgManeuvering.k0 = Propeller.K0;
            MmgManeuvering.k1 = Propeller.K1;
            MmgManeuvering.k2 = Propeller.K2;
        }

        /// <summary>
        /// KVLCC2 scaled to full size from the L7 calibrated set shipped with
        /// ShipMMG (tests/test_mmg_3dof.py). Non-dimensional derivatives are
        /// scale-invariant; dimensional entries use lambda = 320/7. R0' is
        /// reduced to a full-scale value per §5 (model-scale friction is higher).
        /// </summary>
        public static VesselConfig CreateKvlcc2()
        {
            const double rho = 1025.0;
            const double lambda = 320.0 / 7.0;

            double lpp = 7.00 * lambda;          // 320 m
            double breadth = 1.27 * lambda;      // 58.06 m
            double draft = 0.46 * lambda;        // 21.03 m
            double nabla = 3.27 * lambda * lambda * lambda;  // ≈312,000 m³
            double xg = 0.25 * lambda;           // 11.43 m
            double dp = 0.216 * lambda;          // 9.87 m
            double hr = 0.345 * lambda;          // rudder span
            double ar = 0.0539 * lambda * lambda; // 112.6 m²

            var cfg = new VesselConfig
            {
                Name = "KVLCC2 (MMG calibrado)",
                ModelType = ManeuveringModelType.MmgCalibrated
            };

            cfg.MmgBasic = new MmgBasicParams
            {
                Lpp = lpp,
                B = breadth,
                d = draft,
                xG = xg,
                Dp = dp,
                m = rho * nabla,
                IzG = rho * nabla * Math.Pow(0.25 * lpp, 2),
                AR = ar,
                Eta = dp / hr,
                mx = 0.5 * rho * lpp * lpp * draft * 0.022,
                my = 0.5 * rho * lpp * lpp * draft * 0.223,
                Jz = 0.5 * rho * Math.Pow(lpp, 4) * draft * 0.011,
                fAlpha = 2.747,
                Epsilon = 1.09,
                tR = 0.387,
                xR = -0.500 * lpp,
                aH = 0.312,
                xH = -0.464 * lpp,
                GammaRMinus = 0.395,
                GammaRPlus = 0.640,
                lR = -0.710,
                Kappa = 0.50,
                tP = 0.220,
                wP0 = 0.40,
                xP = -0.690
            };

            cfg.MmgManeuvering = new MmgManeuveringParams
            {
                k0 = 0.2931,
                k1 = -0.2753,
                k2 = -0.1385,
                R0Dash = 0.0125, // full-scale calibration (L7 model value is 0.022)
                Xvv = -0.040,
                Xvr = 0.002,
                Xrr = 0.011,
                Xvvvv = 0.771,
                Yv = -0.315,
                Yr = 0.083,
                Yvvv = -1.607,
                Yvvr = 0.379,
                Yvrr = -0.391,
                Yrrr = 0.008,
                Nv = -0.137,
                Nr = -0.049,
                Nvvv = -0.030,
                Nvvr = -0.294,
                Nvrr = 0.055,
                Nrrr = -0.013
            };

            cfg.Rudder = new RudderParams
            {
                MaxAngleDeg = 35.0,
                MaxRateDegPerS = 2.5,
                TimeConstantS = 1.5
            };

            cfg.Engine = new EngineParams
            {
                RatedRps = 1.25,       // ≈75 rpm
                TimeConstantS = 25.0,
                MaxRpsPerS = 0.02,
                MaxRpsPerSDecel = 0.05,
                ReversalDelayS = 15.0,
                McrKw = 36000.0,
                Fuel = FuelType.HeavyFuelOil
            };

            cfg.Propeller = new PropellerParams
            {
                Diameter = dp,
                K0 = 0.2931,
                K1 = -0.2753,
                K2 = -0.1385,
                Kq0 = 0.0454,
                Kq1 = -0.0408,
                WakeFraction = 0.40,
                ThrustDeduction = 0.220,
                AsternThrustFactor = 0.75
            };

            cfg.Windage = new WindageParams
            {
                FrontalAreaM2 = 1200.0,
                LateralAreaM2 = 3900.0,
                CentroidAboveWaterM = 12.0,
                CentroidFromMidshipM = 8.0,
                LoaM = 333.0,
                VesselType = BlendermannVesselType.TankerLoaded
            };

            cfg.BowThruster = new BowThrusterParams
            {
                MaxThrustN = 300000.0,
                LongitudinalPositionM = 0.45 * lpp,
                FadeOutSpeedMs = 4.0
            };

            cfg.Clarke = new Clarke83Params
            {
                L = lpp,
                B = breadth,
                T = draft,
                Cb = 0.81
            };

            return cfg;
        }

        /// <summary>Generic 90 m coaster using the Clarke 83 model (no MMG data needed).</summary>
        public static VesselConfig CreateGenericCoaster()
        {
            var cfg = new VesselConfig
            {
                Name = "Costero genérico (Clarke 83)",
                ModelType = ManeuveringModelType.Clarke83Generic
            };

            cfg.Clarke = new Clarke83Params
            {
                L = 90.0,
                B = 15.0,
                T = 5.5,
                Cb = 0.72,
                // The linear Xu damping of clarke83() is calibrated around cruise;
                // a longer surge time constant yields a realistic ~10 kn coaster.
                SurgeTimeConstantFactor = 2.0
            };

            cfg.Rudder = new RudderParams
            {
                MaxAngleDeg = 35.0,
                MaxRateDegPerS = 4.0,
                TimeConstantS = 1.0
            };

            cfg.Engine = new EngineParams
            {
                RatedRps = 2.6,        // ≈156 rpm
                TimeConstantS = 8.0,
                MaxRpsPerS = 0.12,
                MaxRpsPerSDecel = 0.25,
                ReversalDelayS = 5.0,
                McrKw = 3000.0,
                Fuel = FuelType.MarineDiesel
            };

            cfg.Propeller = new PropellerParams
            {
                Diameter = 3.2,
                K0 = 0.35,
                K1 = -0.28,
                K2 = -0.16,
                Kq0 = 0.052,
                Kq1 = -0.041,
                WakeFraction = 0.25,
                ThrustDeduction = 0.18,
                AsternThrustFactor = 0.8
            };

            cfg.Windage = new WindageParams
            {
                FrontalAreaM2 = 120.0,
                LateralAreaM2 = 520.0,
                CentroidAboveWaterM = 5.0,
                CentroidFromMidshipM = 4.0,
                LoaM = 96.0,
                VesselType = BlendermannVesselType.CargoLoaded
            };

            cfg.BowThruster = new BowThrusterParams
            {
                MaxThrustN = 45000.0,
                LongitudinalPositionM = 38.0,
                FadeOutSpeedMs = 3.0
            };

            // Populate MMG entries too so switching model type never divides by zero.
            var kvlcc = CreateKvlcc2();
            cfg.MmgBasic = kvlcc.MmgBasic;
            cfg.MmgManeuvering = kvlcc.MmgManeuvering;

            return cfg;
        }
    }
}
