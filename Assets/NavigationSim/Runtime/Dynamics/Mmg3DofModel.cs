using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Basic (dimensional) MMG parameters. Direct port of ShipMMG's
    /// Mmg3DofBasicParams (shipmmg/mmg_3dof.py). See Yasukawa & Yoshimura (2015).
    /// </summary>
    [Serializable]
    public class MmgBasicParams
    {
        public double Lpp;         // length between perpendiculars [m]
        public double B;           // breadth [m]
        public double d;           // draft [m]
        public double xG;          // longitudinal CoG [m]
        public double Dp;          // propeller diameter [m]
        public double m;           // mass [kg]
        public double IzG;         // yaw inertia about CoG [kg·m²]
        public double AR;          // rudder profile area [m²]
        public double Eta;         // Dp / rudder span
        public double mx;          // added mass x [kg]
        public double my;          // added mass y [kg]
        public double Jz;          // added yaw inertia [kg·m²]
        public double fAlpha;     // rudder lift gradient
        public double Epsilon;     // wake ratio propeller/rudder
        public double tR;          // steering resistance deduction
        public double xR;          // rudder position [m]
        public double aH;          // rudder force increase factor
        public double xH;          // acting point of aH force [m]
        public double GammaRMinus; // flow straightening (betaR < 0)
        public double GammaRPlus;  // flow straightening (betaR > 0)
        public double lR;          // effective rudder position (non-dim)
        public double Kappa;       // uR experimental constant
        public double tP;          // thrust deduction
        public double wP0;         // straight-run wake fraction
        public double xP;          // effective propeller position (non-dim)
    }

    /// <summary>Non-dimensional maneuvering derivatives (Mmg3DofManeuveringParams).</summary>
    [Serializable]
    public class MmgManeuveringParams
    {
        public double k0, k1, k2;  // KT = k0 + k1·J + k2·J²
        public double R0Dash;      // straight-run resistance coefficient
        public double Xvv, Xvr, Xrr, Xvvvv;
        public double Yv, Yr, Yvvv, Yvvr, Yvrr, Yrrr;
        public double Nv, Nr, Nvvv, Nvvr, Nvrr, Nrrr;
    }

    /// <summary>
    /// Port of ShipMMG's mmg_3dof_eom_solve_ivp (shipmmg/mmg_3dof.py:862-955)
    /// with three extensions: uniform current via water-relative velocities,
    /// external force injection (wind/thrusters) and a simple astern-thrust
    /// branch for n &lt; 0 (outside the scope of the original model).
    /// </summary>
    public class Mmg3DofModel : IManeuveringModel
    {
        public MmgBasicParams Basic;
        public MmgManeuveringParams Maneuvering;

        /// <summary>Reverse thrust magnitude relative to ahead bollard KT(0).</summary>
        public double AsternThrustFactor = 0.75;

        public Mmg3DofModel(MmgBasicParams basic, MmgManeuveringParams maneuvering)
        {
            Basic = basic;
            Maneuvering = maneuvering;
        }

        public void Derivatives(double[] x, double rudderRad, double nps,
            ExternalForces ext, EnvironmentState env, double[] dxdt)
        {
            MmgBasicParams bp = Basic;
            MmgManeuveringParams mp = Maneuvering;
            double rho = env.WaterDensity;

            double u = x[0];
            double v = x[1];
            double r = x[2];
            double psi = x[5];
            double delta = rudderRad;

            // Water-relative velocities drive every hydrodynamic force.
            CurrentModel.BodyCurrent(env, psi, out double uc, out double vc);
            double ur = u - uc;
            double vr = v - vc;

            double U = Math.Sqrt(ur * ur + (vr - r * bp.xG) * (vr - r * bp.xG));

            double beta = U == 0.0 ? 0.0 : Math.Asin(Clamp(-(vr - r * bp.xG) / U, -1.0, 1.0));
            double vDash = U == 0.0 ? 0.0 : vr / U;
            double rDash = U == 0.0 ? 0.0 : r * bp.Lpp / U;

            double wP = bp.wP0 * Math.Exp(-4.0 * (beta - bp.xP * rDash) * (beta - bp.xP * rDash));

            double J = nps == 0.0 ? 0.0 : (1.0 - wP) * ur / (nps * bp.Dp);
            J = Clamp(J, -0.8, 1.4); // robustness outside the fitted range
            double KT = mp.k0 + mp.k1 * J + mp.k2 * J * J;

            double betaR = beta - bp.lR * rDash;
            double gammaR = betaR < 0.0 ? bp.GammaRMinus : bp.GammaRPlus;
            double vR = U * gammaR * betaR;

            double d4 = bp.Dp * bp.Dp * bp.Dp * bp.Dp;
            double uR;
            if (nps <= 0.0)
            {
                // No slipstream over the rudder when stopped or reversing.
                uR = Math.Max(0.0, ur) * (1.0 - wP) * bp.Epsilon;
            }
            else if (J == 0.0)
            {
                uR = Math.Sqrt(Math.Max(0.0, bp.Eta)) *
                     (bp.Kappa * bp.Epsilon * 8.0 * mp.k0 * nps * nps * d4 / Math.PI);
            }
            else
            {
                double inner = 1.0 + 8.0 * KT / (Math.PI * J * J);
                double root = inner <= 0.0 ? 0.0 : Math.Sqrt(inner);
                double term = 1.0 + bp.Kappa * (root - 1.0);
                uR = ur * (1.0 - wP) * bp.Epsilon *
                     Math.Sqrt(Math.Max(0.0, bp.Eta * term * term + (1.0 - bp.Eta)));
            }

            double UR = Math.Sqrt(uR * uR + vR * vR);
            double alphaR = delta - Math.Atan2(vR, uR);
            double FN = 0.5 * bp.AR * rho * bp.fAlpha * UR * UR * Math.Sin(alphaR);

            double dyn = 0.5 * rho * bp.Lpp * bp.d * U * U;
            double XH = dyn * (-mp.R0Dash
                        + mp.Xvv * vDash * vDash
                        + mp.Xvr * vDash * rDash
                        + mp.Xrr * rDash * rDash
                        + mp.Xvvvv * vDash * vDash * vDash * vDash);
            double XR = -(1.0 - bp.tR) * FN * Math.Sin(delta);

            double XP;
            if (nps >= 0.0)
            {
                XP = (1.0 - bp.tP) * rho * KT * nps * nps * d4;
            }
            else
            {
                XP = -(1.0 - bp.tP) * rho * (AsternThrustFactor * mp.k0) * nps * nps * d4;
            }

            double YH = dyn * (mp.Yv * vDash
                        + mp.Yr * rDash
                        + mp.Yvvv * vDash * vDash * vDash
                        + mp.Yvvr * vDash * vDash * rDash
                        + mp.Yvrr * vDash * rDash * rDash
                        + mp.Yrrr * rDash * rDash * rDash);
            double YR = -(1.0 + bp.aH) * FN * Math.Cos(delta);

            double NH = 0.5 * rho * bp.Lpp * bp.Lpp * bp.d * U * U * (mp.Nv * vDash
                        + mp.Nr * rDash
                        + mp.Nvvv * vDash * vDash * vDash
                        + mp.Nvvr * vDash * vDash * rDash
                        + mp.Nvrr * vDash * rDash * rDash
                        + mp.Nrrr * rDash * rDash * rDash);
            double NR = -(bp.xR + bp.aH * bp.xH) * FN * Math.Cos(delta);

            double X = XH + XR + XP + ext.X;
            double Y = YH + YR + ext.Y;
            double N = NH + NR + ext.N;

            // Relative accelerations, same coupled sway/yaw solution as ShipMMG.
            double mTot = bp.m;
            double dur = (X + (mTot + bp.my) * vr * r + bp.xG * mTot * r * r) / (mTot + bp.mx);

            double izTerm = bp.IzG + bp.Jz + bp.xG * bp.xG * mTot;
            double denom = izTerm * (mTot + bp.my) - bp.xG * bp.xG * mTot * mTot;
            double dvr = (bp.xG * bp.xG * mTot * mTot * ur * r
                          - N * bp.xG * mTot
                          + (Y - (mTot + bp.mx) * ur * r) * izTerm) / denom;
            double dr = (N - bp.xG * mTot * (dvr + ur * r)) / izTerm;

            // Ground accelerations: add rotation of the body-frame current vector.
            dxdt[0] = dur + r * vc;
            dxdt[1] = dvr - r * uc;
            dxdt[2] = dr;
            dxdt[3] = u * Math.Cos(psi) - v * Math.Sin(psi); // dNorth
            dxdt[4] = u * Math.Sin(psi) + v * Math.Cos(psi); // dEast
            dxdt[5] = r;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
