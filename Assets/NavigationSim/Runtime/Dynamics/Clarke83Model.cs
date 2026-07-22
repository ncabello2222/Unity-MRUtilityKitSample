using System;

namespace NavigationSim.Core
{
    /// <summary>Main-dimension parameters for the generic Clarke (1983) linear model.</summary>
    [Serializable]
    public class Clarke83Params
    {
        public double L = 90.0;      // length [m]
        public double B = 15.0;      // beam [m]
        public double T = 5.5;       // draft [m]
        public double Cb = 0.72;     // block coefficient
        public double SurgeTimeConstantFactor = 1.0; // T_surge = factor · L [s]
        public double RudderAspectRatio = 0.7;       // Lambda = b²/AR
        public double ThrustDeduction = 0.1;

        public double R66 => (L > 100.0 ? 0.27 : 0.25) * L;
        public double Mass(double rho) => rho * Cb * L * B * T;
    }

    /// <summary>
    /// Port of PVS shipClarke83.py + lib/models.py clarke83(): linear M/N
    /// matrices regressed from main dimensions, wing-theory rudder forces and
    /// water-relative current handling. Thrust comes from the shared propeller
    /// formulation so the telegraph chain behaves like the MMG mode.
    /// </summary>
    public class Clarke83Model : IManeuveringModel
    {
        public Clarke83Params Params;
        public PropellerParams Propeller;

        public Clarke83Model(Clarke83Params p, PropellerParams prop)
        {
            Params = p;
            Propeller = prop;
        }

        public void Derivatives(double[] x, double rudderRad, double nps,
            ExternalForces ext, EnvironmentState env, double[] dxdt)
        {
            Clarke83Params p = Params;
            double rho = env.WaterDensity;

            double u = x[0];
            double v = x[1];
            double r = x[2];
            double psi = x[5];

            CurrentModel.BodyCurrent(env, psi, out double uc, out double vc);
            double ur = u - uc;
            double vr = v - vc;
            double Ur = Math.Sqrt(ur * ur + vr * vr);

            // Rudder forces and moment (Fossen 2021, Ch. 9.5.1).
            double b = 0.7 * p.T;
            double AR = b * b / p.RudderAspectRatio;
            double CN = 6.13 * p.RudderAspectRatio / (p.RudderAspectRatio + 2.25);
            double tRudder = 1.0 - 0.28 * p.Cb - 0.55;
            double aH = 0.4;
            double xRr = -0.45 * p.L;
            double xHr = -1.0 * p.L;

            double Xdd = -0.5 * (1.0 - tRudder) * rho * Ur * Ur * AR * CN;
            double Yd = -0.25 * (1.0 + aH) * rho * Ur * Ur * AR * CN;
            double Nd = -0.25 * (xRr + aH * xHr) * rho * Ur * Ur * AR * CN;

            double deltaR = -rudderRad; // physical rudder angle convention of PVS

            double thrust = PropellerModel.Thrust(Propeller, ur, nps, rho);
            double tau1 = (1.0 - p.ThrustDeduction) * thrust
                          - Xdd * Math.Sin(deltaR) * Math.Sin(deltaR) + ext.X;
            double tau2 = -Yd * Math.Sin(2.0 * deltaR) + ext.Y;
            double tau6 = -Nd * Math.Sin(2.0 * deltaR) + ext.N;

            // clarke83() system matrices (PVS lib/models.py).
            BuildMatrices(Ur, rho, out double m11,
                out double m22, out double m23, out double m32, out double m33,
                out double n11, out double n22, out double n23, out double n32, out double n33);

            double dur = (tau1 - n11 * ur) / m11;

            // Solve the 2x2 sway/yaw system: M·[dv, dr] = tau - N·[v, r].
            double b1 = tau2 - (n22 * vr + n23 * r);
            double b2 = tau6 - (n32 * vr + n33 * r);
            double det = m22 * m33 - m23 * m32;
            double dvr = (b1 * m33 - b2 * m23) / det;
            double dr = (b2 * m22 - b1 * m32) / det;

            dxdt[0] = dur + r * vc;
            dxdt[1] = dvr - r * uc;
            dxdt[2] = dr;
            dxdt[3] = u * Math.Cos(psi) - v * Math.Sin(psi);
            dxdt[4] = u * Math.Sin(psi) + v * Math.Cos(psi);
            dxdt[5] = r;
        }

        private void BuildMatrices(double U, double rho, out double m11,
            out double m22, out double m23, out double m32, out double m33,
            out double n11, out double n22, out double n23, out double n32, out double n33)
        {
            Clarke83Params p = Params;
            double L = p.L, B = p.B, T = p.T, Cb = p.Cb;

            double mass = p.Mass(rho);
            double R66 = p.R66;
            double xg = 0.0;
            double Iz = mass * R66 * R66 + mass * xg * xg;
            double Tsurge = Math.Max(1.0, p.SurgeTimeConstantFactor * L);

            double XudotDim = -0.1 * mass;
            double Ueff = U + 0.001; // avoid U = 0 singularity, as in PVS
            double Xu = -((mass - XudotDim) / Tsurge) / (0.5 * rho * L * L * Ueff);
            double Xudot = XudotDim / (0.5 * rho * L * L * L);

            double S = Math.PI * (T / L) * (T / L);
            double Yvdot = -S * (1.0 + 0.16 * Cb * B / T - 5.1 * (B / L) * (B / L));
            double Yrdot = -S * (0.67 * B / L - 0.0033 * (B / T) * (B / T));
            double Nvdot = -S * (1.1 * B / L - 0.041 * (B / T));
            double Nrdot = -S * (1.0 / 12.0 + 0.017 * Cb * (B / T) - 0.33 * (B / L));
            double Yv = -S * (1.0 + 0.4 * Cb * (B / T));
            double Yr = -S * (-0.5 + 2.2 * (B / L) - 0.08 * (B / T));
            double Nv = -S * (0.5 + 2.4 * (T / L));
            double Nr = -S * (0.25 + 0.039 * (B / T) - 0.56 * (B / L));

            // Dimensionalization (Fossen 2021, App. D): T = diag(1,1,1/L).
            double halfRhoL3 = 0.5 * rho * L * L * L;
            double halfRhoL2U = 0.5 * rho * L * L * Ueff;

            m11 = mass - halfRhoL3 * Xudot;
            m22 = mass - halfRhoL3 * Yvdot;
            m23 = mass * xg - halfRhoL3 * L * Yrdot;
            m32 = mass * xg - halfRhoL3 * L * Nvdot;
            m33 = Iz - halfRhoL3 * L * L * Nrdot;

            n11 = -halfRhoL2U * Xu;
            n22 = -halfRhoL2U * Yv;
            n23 = -halfRhoL2U * L * Yr;
            n32 = -halfRhoL2U * L * Nv;
            n33 = -halfRhoL2U * L * L * Nr;
        }
    }
}
