namespace NavigationSim.Core
{
    public delegate void StateDerivative(double[] x, double[] dxdt);

    /// <summary>
    /// Fixed-step classic RK4 over a small state vector. Replaces SciPy's
    /// solve_ivp used by ShipMMG. Buffers are preallocated to stay GC-free.
    /// </summary>
    public class Rk4Integrator
    {
        private readonly double[] _k1;
        private readonly double[] _k2;
        private readonly double[] _k3;
        private readonly double[] _k4;
        private readonly double[] _tmp;

        public Rk4Integrator(int dimension)
        {
            _k1 = new double[dimension];
            _k2 = new double[dimension];
            _k3 = new double[dimension];
            _k4 = new double[dimension];
            _tmp = new double[dimension];
        }

        public void Step(StateDerivative f, double[] x, double dt)
        {
            int n = x.Length;

            f(x, _k1);
            for (int i = 0; i < n; i++)
            {
                _tmp[i] = x[i] + 0.5 * dt * _k1[i];
            }

            f(_tmp, _k2);
            for (int i = 0; i < n; i++)
            {
                _tmp[i] = x[i] + 0.5 * dt * _k2[i];
            }

            f(_tmp, _k3);
            for (int i = 0; i < n; i++)
            {
                _tmp[i] = x[i] + dt * _k3[i];
            }

            f(_tmp, _k4);
            for (int i = 0; i < n; i++)
            {
                x[i] += dt / 6.0 * (_k1[i] + 2.0 * _k2[i] + 2.0 * _k3[i] + _k4[i]);
            }
        }
    }
}
