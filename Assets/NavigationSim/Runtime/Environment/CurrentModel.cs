using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Uniform sea current. The current never moves the ship visually by itself:
    /// it changes the water-relative velocity used for hull, propeller and rudder
    /// forces (pattern from PVS shipClarke83), while ground kinematics use u, v.
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
