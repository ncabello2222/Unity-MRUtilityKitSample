using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Offline calibration helpers (§5 of the master plan). The MMG X_H term
    /// already contains straight-run resistance via R0'; these utilities only
    /// predict equilibrium so the instructor panel can display sanity numbers.
    /// Never add an extra resistance force per frame.
    /// </summary>
    public static class HullResistanceCalibration
    {
        /// <summary>Straight-run hull resistance R(U) = ½ρ·Lpp·d·U²·R0' [N].</summary>
        public static double Resistance(MmgBasicParams bp, MmgManeuveringParams mp, double rho, double speedMs)
        {
            return 0.5 * rho * bp.Lpp * bp.d * speedMs * speedMs * mp.R0Dash;
        }

        /// <summary>
        /// Steady speed where (1 - tP)·T(n, U) balances R(U), by bisection.
        /// </summary>
        public static double EquilibriumSpeed(MmgBasicParams bp, MmgManeuveringParams mp,
            PropellerParams prop, double rho, double rps)
        {
            if (rps <= 1e-6)
            {
                return 0.0;
            }

            double lo = 0.0;
            double hi = 25.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                double thrust = (1.0 - bp.tP) * PropellerModel.Thrust(prop, mid, rps, rho);
                double resistance = Resistance(bp, mp, rho, mid);
                if (thrust > resistance)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return 0.5 * (lo + hi);
        }
    }
}
