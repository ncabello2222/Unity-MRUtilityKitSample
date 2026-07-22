using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Specific fuel oil consumption chain (Vessel.js FuelConsumption pattern,
    /// single main engine): load → SFOC polynomial → kg/s. Consumption is a
    /// consequence of shaft power; it never feeds back into the dynamics.
    /// </summary>
    public class FuelConsumptionModel
    {
        public double FuelFlowKgPerS { get; private set; }
        public double FuelUsedKg { get; private set; }

        /// <summary>Fuel-type correction over the diesel SFOC curve and density [kg/m³].</summary>
        public static void FuelProperties(FuelType type, out double sfocFactor, out double densityKgM3)
        {
            switch (type)
            {
                case FuelType.HeavyFuelOil:
                    sfocFactor = 1.05;
                    densityKgM3 = 991.0;
                    break;
                case FuelType.Lng:
                    sfocFactor = 0.82;
                    densityKgM3 = 450.0;
                    break;
                default:
                    sfocFactor = 1.0;
                    densityKgM3 = 890.0;
                    break;
            }
        }

        public void Update(EngineParams engine, double shaftPowerW, double dt)
        {
            double powerKw = shaftPowerW / 1000.0;
            double load = engine.McrKw > 1.0 ? powerKw / engine.McrKw : 0.0;

            if (load < 0.01)
            {
                FuelFlowKgPerS = 0.0;
                return;
            }

            load = Math.Min(load, 1.1);
            double sfoc = engine.SfocA * load * load + engine.SfocB * load + engine.SfocC; // g/kWh
            FuelProperties(engine.Fuel, out double factor, out _);
            sfoc = Math.Max(120.0, sfoc * factor);

            FuelFlowKgPerS = sfoc * powerKw / 3.6e6; // g/kWh · kW → kg/s
            FuelUsedKg += FuelFlowKgPerS * dt;
        }

        public void Reset()
        {
            FuelFlowKgPerS = 0.0;
            FuelUsedKg = 0.0;
        }
    }
}
