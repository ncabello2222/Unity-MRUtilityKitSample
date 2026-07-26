using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Uniform sea current. It is never added to the ground velocity by hand: it
    /// changes the water-relative velocity used for hull, propeller and rudder
    /// forces (pattern from PVS shipClarke83), and the resulting set and drift fall
    /// out of the integration of u, v — so SOG/COG do carry it.
    /// </summary>
    public static class CurrentModel
    {
        /// <summary>Body-frame components of the current for a ship heading psi.</summary>
        public static void BodyCurrent(EnvironmentState env, double psiRad, out double uc, out double vc)
        {
            double set = env.CurrentSetToDeg * Math.PI / 180.0;
            uc = env.CurrentSpeedMs * Math.Cos(set - psiRad);
            vc = env.CurrentSpeedMs * Math.Sin(set - psiRad);
        }
    }
}
