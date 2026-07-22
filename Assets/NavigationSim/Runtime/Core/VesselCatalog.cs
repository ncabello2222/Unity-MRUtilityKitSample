using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Visual + simulation identity for each Blender-authored hull shown from the bridge.
    /// </summary>
    [Serializable]
    public class VesselDefinition
    {
        public string Id;
        public string DisplayName;
        public string HullResourcePath;
        /// <summary>Bridge floor height above the visual deck [m].</summary>
        public float BridgeHeightAboveDeckM;
        /// <summary>How far ahead of the room pivot the hull attach point sits [m].</summary>
        public float HullForwardFromPivotM;
        /// <summary>
        /// Yaw applied to the FBX. Blender hulls in this project extend along -Z from a
        /// stern-near origin; 180° puts the bow out the front bridge windows.
        /// </summary>
        public float HullYawDeg = 180f;
        /// <summary>Optional uniform scale if the mesh needs a final nudge.</summary>
        public float VisualScale = 1f;
        public Func<VesselConfig> CreateConfig;
    }

    /// <summary>
    /// Presets that pair Blender hulls with fuel / power / dimensions for the B panel.
    /// </summary>
    public static class VesselCatalog
    {
        public static readonly VesselDefinition[] All =
        {
            new VesselDefinition
            {
                Id = "coaster",
                DisplayName = "Costero (proa original)",
                HullResourcePath = "Vessels/Vessel_Original",
                BridgeHeightAboveDeckM = 12f,
                HullForwardFromPivotM = 2f,
                CreateConfig = VesselConfig.CreateGenericCoaster
            },
            new VesselDefinition
            {
                Id = "tug",
                DisplayName = "Remolcador 12×30 m",
                HullResourcePath = "Vessels/Vessel_Tug",
                BridgeHeightAboveDeckM = 4.5f,
                HullForwardFromPivotM = 1.5f,
                CreateConfig = CreateTug
            },
            new VesselDefinition
            {
                Id = "fishing",
                DisplayName = "Pesquero 9×42 m",
                HullResourcePath = "Vessels/Vessel_Fishing",
                BridgeHeightAboveDeckM = 5.5f,
                HullForwardFromPivotM = 1.5f,
                CreateConfig = CreateFishing
            },
            new VesselDefinition
            {
                Id = "ferry",
                DisplayName = "Ferry RoPax 20×100 m",
                HullResourcePath = "Vessels/Vessel_Ferry",
                BridgeHeightAboveDeckM = 9f,
                HullForwardFromPivotM = 2f,
                CreateConfig = CreateFerry
            },
            new VesselDefinition
            {
                Id = "bulk",
                DisplayName = "Granelero 32×225 m",
                HullResourcePath = "Vessels/Vessel_Bulk",
                BridgeHeightAboveDeckM = 14f,
                HullForwardFromPivotM = 3f,
                CreateConfig = CreateBulk
            },
            new VesselDefinition
            {
                Id = "container",
                DisplayName = "Portacontenedores 40×280 m",
                HullResourcePath = "Vessels/Vessel_Container",
                BridgeHeightAboveDeckM = 20f,
                HullForwardFromPivotM = 3f,
                CreateConfig = CreateContainer
            },
            new VesselDefinition
            {
                Id = "kvlcc2",
                DisplayName = "KVLCC2 petrolero (MMG)",
                HullResourcePath = "Vessels/Vessel_Bulk",
                BridgeHeightAboveDeckM = 16f,
                HullForwardFromPivotM = 3f,
                CreateConfig = VesselConfig.CreateKvlcc2
            }
        };

        public static int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        public static VesselDefinition Get(int index)
        {
            if (index < 0 || index >= All.Length)
            {
                return All[0];
            }

            return All[index];
        }

        public static string[] DisplayNames()
        {
            var names = new string[All.Length];
            for (int i = 0; i < All.Length; i++)
            {
                names[i] = All[i].DisplayName;
            }

            return names;
        }

        // ── Physics presets aligned with Blender hull sizes ──────────────────

        public static VesselConfig CreateTug()
        {
            var cfg = VesselConfig.CreateGenericCoaster();
            cfg.Name = "Remolcador 12×30 m";
            cfg.ModelType = ManeuveringModelType.Clarke83Generic;
            cfg.Clarke.L = 30.0;
            cfg.Clarke.B = 12.0;
            cfg.Clarke.T = 4.2;
            cfg.Clarke.Cb = 0.65;
            cfg.Clarke.SurgeTimeConstantFactor = 1.2;
            cfg.Engine.RatedRps = 3.2;
            cfg.Engine.McrKw = 4500.0;
            cfg.Engine.TimeConstantS = 4.0;
            cfg.Engine.MaxRpsPerS = 0.25;
            cfg.Engine.MaxRpsPerSDecel = 0.4;
            cfg.Engine.ReversalDelayS = 3.0;
            cfg.Engine.Fuel = FuelType.MarineDiesel;
            cfg.Propeller.Diameter = 2.8;
            cfg.Propeller.WakeFraction = 0.20;
            cfg.Rudder.MaxRateDegPerS = 6.0;
            cfg.Windage.LoaM = 32.0;
            cfg.Windage.FrontalAreaM2 = 90.0;
            cfg.Windage.LateralAreaM2 = 280.0;
            cfg.Windage.CentroidAboveWaterM = 3.5;
            cfg.Windage.VesselType = BlendermannVesselType.OffshoreSupply;
            cfg.BowThruster.MaxThrustN = 80000.0;
            cfg.BowThruster.LongitudinalPositionM = 12.0;
            return cfg;
        }

        public static VesselConfig CreateFishing()
        {
            var cfg = VesselConfig.CreateGenericCoaster();
            cfg.Name = "Pesquero 9×42 m";
            cfg.ModelType = ManeuveringModelType.Clarke83Generic;
            cfg.Clarke.L = 42.0;
            cfg.Clarke.B = 9.0;
            cfg.Clarke.T = 3.6;
            cfg.Clarke.Cb = 0.60;
            cfg.Clarke.SurgeTimeConstantFactor = 1.4;
            cfg.Engine.RatedRps = 3.0;
            cfg.Engine.McrKw = 1800.0;
            cfg.Engine.TimeConstantS = 5.0;
            cfg.Engine.Fuel = FuelType.MarineDiesel;
            cfg.Propeller.Diameter = 2.4;
            cfg.Rudder.MaxRateDegPerS = 5.0;
            cfg.Windage.LoaM = 45.0;
            cfg.Windage.FrontalAreaM2 = 70.0;
            cfg.Windage.LateralAreaM2 = 320.0;
            cfg.Windage.CentroidAboveWaterM = 4.0;
            cfg.Windage.VesselType = BlendermannVesselType.FishingVessel;
            cfg.BowThruster.MaxThrustN = 25000.0;
            cfg.BowThruster.LongitudinalPositionM = 16.0;
            return cfg;
        }

        public static VesselConfig CreateFerry()
        {
            var cfg = VesselConfig.CreateGenericCoaster();
            cfg.Name = "Ferry RoPax 20×100 m";
            cfg.ModelType = ManeuveringModelType.Clarke83Generic;
            cfg.Clarke.L = 100.0;
            cfg.Clarke.B = 20.0;
            cfg.Clarke.T = 5.5;
            cfg.Clarke.Cb = 0.68;
            cfg.Clarke.SurgeTimeConstantFactor = 1.8;
            cfg.Engine.RatedRps = 2.4;
            cfg.Engine.McrKw = 12000.0;
            cfg.Engine.TimeConstantS = 10.0;
            cfg.Engine.Fuel = FuelType.MarineDiesel;
            cfg.Propeller.Diameter = 4.2;
            cfg.Rudder.MaxRateDegPerS = 3.5;
            cfg.Windage.LoaM = 108.0;
            cfg.Windage.FrontalAreaM2 = 380.0;
            cfg.Windage.LateralAreaM2 = 1400.0;
            cfg.Windage.CentroidAboveWaterM = 8.0;
            cfg.Windage.VesselType = BlendermannVesselType.Ferry;
            cfg.BowThruster.MaxThrustN = 160000.0;
            cfg.BowThruster.LongitudinalPositionM = 42.0;
            return cfg;
        }

        public static VesselConfig CreateBulk()
        {
            var cfg = VesselConfig.CreateGenericCoaster();
            cfg.Name = "Granelero 32×225 m";
            cfg.ModelType = ManeuveringModelType.Clarke83Generic;
            cfg.Clarke.L = 225.0;
            cfg.Clarke.B = 32.0;
            cfg.Clarke.T = 12.5;
            cfg.Clarke.Cb = 0.82;
            cfg.Clarke.SurgeTimeConstantFactor = 2.4;
            cfg.Engine.RatedRps = 1.6;
            cfg.Engine.McrKw = 14000.0;
            cfg.Engine.TimeConstantS = 18.0;
            cfg.Engine.MaxRpsPerS = 0.04;
            cfg.Engine.Fuel = FuelType.HeavyFuelOil;
            cfg.Propeller.Diameter = 6.5;
            cfg.Propeller.WakeFraction = 0.35;
            cfg.Rudder.MaxRateDegPerS = 2.8;
            cfg.Windage.LoaM = 230.0;
            cfg.Windage.FrontalAreaM2 = 700.0;
            cfg.Windage.LateralAreaM2 = 2800.0;
            cfg.Windage.CentroidAboveWaterM = 10.0;
            cfg.Windage.VesselType = BlendermannVesselType.CargoLoaded;
            cfg.BowThruster.MaxThrustN = 200000.0;
            cfg.BowThruster.LongitudinalPositionM = 95.0;
            return cfg;
        }

        public static VesselConfig CreateContainer()
        {
            var cfg = VesselConfig.CreateGenericCoaster();
            cfg.Name = "Portacontenedores 40×280 m";
            cfg.ModelType = ManeuveringModelType.Clarke83Generic;
            cfg.Clarke.L = 280.0;
            cfg.Clarke.B = 40.0;
            cfg.Clarke.T = 14.5;
            cfg.Clarke.Cb = 0.70;
            cfg.Clarke.SurgeTimeConstantFactor = 2.6;
            cfg.Engine.RatedRps = 1.5;
            cfg.Engine.McrKw = 45000.0;
            cfg.Engine.TimeConstantS = 22.0;
            cfg.Engine.MaxRpsPerS = 0.035;
            cfg.Engine.Fuel = FuelType.HeavyFuelOil;
            cfg.Propeller.Diameter = 8.5;
            cfg.Propeller.WakeFraction = 0.32;
            cfg.Rudder.MaxRateDegPerS = 2.5;
            cfg.Windage.LoaM = 300.0;
            cfg.Windage.FrontalAreaM2 = 1400.0;
            cfg.Windage.LateralAreaM2 = 5200.0;
            cfg.Windage.CentroidAboveWaterM = 14.0;
            cfg.Windage.VesselType = BlendermannVesselType.ContainerShipLoaded;
            cfg.BowThruster.MaxThrustN = 280000.0;
            cfg.BowThruster.LongitudinalPositionM = 120.0;
            return cfg;
        }
    }
}
