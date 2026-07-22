namespace NavigationSim.Core
{
    /// <summary>Wind + thruster (and any future) loads in body axes, held constant during one RK4 step.</summary>
    public struct ExternalForces
    {
        public double X; // surge [N]
        public double Y; // sway [N]
        public double N; // yaw moment [N·m]
    }

    /// <summary>
    /// Maneuvering model over the state vector x = [u, v, r, North, East, psi].
    /// Implementations must compute hull/propeller/rudder forces with
    /// water-relative velocities (current handled inside) and integrate the
    /// ground-referenced kinematics.
    /// </summary>
    public interface IManeuveringModel
    {
        void Derivatives(double[] x, double rudderRad, double shaftRps,
            ExternalForces external, EnvironmentState env, double[] dxdt);
    }
}
