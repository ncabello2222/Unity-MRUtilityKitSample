using System;

namespace NavigationSim.Core
{
    [Serializable]
    public class PropellerParams
    {
        public double Diameter = 9.86;   // [m]

        // KT(J) = k0 + k1·J + k2·J²  (ShipMMG form)
        public double K0 = 0.2931;
        public double K1 = -0.2753;
        public double K2 = -0.1385;

        // KQ(J) = Kq0 + Kq1·J  (linear curve, Vessel.js PropellerInteraction form)
        public double Kq0 = 0.0454;
        public double Kq1 = -0.0408;

        public double WakeFraction = 0.40;      // w_P0
        public double ThrustDeduction = 0.220;  // t_P
        public double AsternThrustFactor = 0.75;
    }

    /// <summary>
    /// Open-water propeller: J → KT/KQ → thrust, torque and shaft power.
    /// Formulas: T = ρ n² D⁴ KT,  Q = ρ n² D⁵ KQ,  P = 2π |n·Q|.
    /// </summary>
    public static class PropellerModel
    {
        /// <summary>Thrust [N] including the simplified astern branch (n &lt; 0).</summary>
        public static double Thrust(PropellerParams p, double uWater, double rps, double rho)
        {
            if (Math.Abs(rps) < 1e-6)
            {
                return 0.0;
            }

            double d4 = Math.Pow(p.Diameter, 4);
            if (rps >= 0.0)
            {
                double kt = Kt(p, AdvanceRatio(p, uWater, rps));
                return rho * kt * rps * rps * d4;
            }

            return -rho * (p.AsternThrustFactor * p.K0) * rps * rps * d4;
        }

        public static double AdvanceRatio(PropellerParams p, double uWater, double rps)
        {
            if (Math.Abs(rps) < 1e-6)
            {
                return 0.0;
            }

            double j = (1.0 - p.WakeFraction) * uWater / (rps * p.Diameter);
            return Clamp(j, -0.8, 1.4);
        }

        public static double Kt(PropellerParams p, double j)
        {
            return p.K0 + p.K1 * j + p.K2 * j * j;
        }

        public static double Kq(PropellerParams p, double j)
        {
            return Math.Max(0.004, p.Kq0 + p.Kq1 * j);
        }

        /// <summary>Full diagnostics for the instruments and the fuel chain.</summary>
        public static void Compute(PropellerParams p, double uWater, double rps, double rho,
            out double j, out double thrustN, out double torqueNm, out double shaftPowerW)
        {
            if (Math.Abs(rps) < 1e-6)
            {
                j = 0.0;
                thrustN = 0.0;
                torqueNm = 0.0;
                shaftPowerW = 0.0;
                return;
            }

            j = rps > 0.0 ? AdvanceRatio(p, uWater, rps) : 0.0;
            thrustN = Thrust(p, uWater, rps, rho);

            double d5 = Math.Pow(p.Diameter, 5);
            double kq = rps > 0.0 ? Kq(p, j) : Kq(p, 0.0);
            torqueNm = rho * kq * rps * rps * d5;
            shaftPowerW = 2.0 * Math.PI * Math.Abs(rps) * torqueNm;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
