using System;

namespace NavigationSim.Core
{
    /// <summary>
    /// Full state of the own ship. Kinematics follow the MMG/NED convention:
    /// North/East position, psi = heading clockwise from north, body x forward,
    /// body y to starboard, yaw rate r positive turning to starboard.
    /// </summary>
    public class ShipState
    {
        // Maneuvering state (ground-referenced body velocities).
        public double U;              // surge velocity [m/s]
        public double V;              // sway velocity [m/s]
        public double R;              // yaw rate [rad/s]
        public double North;          // [m]
        public double East;           // [m]
        public double PsiRad;         // heading [rad]

        // Actuators.
        public double RudderAngleDeg; // actual rudder angle
        public double ShaftRps;       // actual propeller revolutions [1/s]

        // Water-relative velocities (updated each tick).
        public double Ur;
        public double Vr;

        // Displacement of the water mass over ground since the last reset [m].
        // The trajectory never reads it: the render layer needs it to keep the wave
        // field glued to the water instead of to the seabed.
        public double WaterDriftNorth;
        public double WaterDriftEast;

        // Apparent (relative) wind — the quantity an anemometer reads, as opposed to
        // the true wind the instructor sets. Direction is the bearing it blows FROM
        // relative to the bow: 0 = ahead, 90 = starboard beam, 180 = astern.
        public double ApparentWindSpeedMs;
        public double ApparentWindFromRelDeg;

        // Fraction of bow-thruster command actually delivered: tunnel thrusters lose
        // authority with headway, so the order and the effect are not the same number.
        public double BowThrusterEffective;

        // Propulsion diagnostics.
        public double AdvanceRatioJ;
        public double PropThrustN;
        public double PropTorqueNm;
        public double ShaftPowerW;
        public double EngineLoad;        // fraction of MCR
        public double FuelFlowKgPerH;
        public double FuelUsedKg;

        // Visual seakeeping channel (does not affect the trajectory).
        public double HeaveM;
        public double RollDeg;   // positive = starboard down
        public double PitchDeg;  // positive = bow down

        public double TimeS;

        public double HeadingDeg => Normalize360(PsiRad * 180.0 / Math.PI);
        public double SogMs => Math.Sqrt(U * U + V * V);
        public double StwMs => Math.Sqrt(Ur * Ur + Vr * Vr);
        public double RotDegPerMin => R * 180.0 / Math.PI * 60.0;

        /// <summary>Apparent wind as the compass bearing it blows from.</summary>
        public double ApparentWindFromDeg => Normalize360(HeadingDeg + ApparentWindFromRelDeg);

        public double CogDeg
        {
            get
            {
                if (SogMs < 0.01)
                {
                    return HeadingDeg;
                }

                double vn = U * Math.Cos(PsiRad) - V * Math.Sin(PsiRad);
                double ve = U * Math.Sin(PsiRad) + V * Math.Cos(PsiRad);
                return Normalize360(Math.Atan2(ve, vn) * 180.0 / Math.PI);
            }
        }

        public static double Normalize360(double deg)
        {
            deg %= 360.0;
            if (deg < 0.0)
            {
                deg += 360.0;
            }

            return deg;
        }

        /// <summary>Wrap to (-180, 180].</summary>
        public static double WrapDeg(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0)
            {
                deg -= 360.0;
            }
            else if (deg <= -180.0)
            {
                deg += 360.0;
            }

            return deg;
        }

        public void Reset()
        {
            U = V = R = North = East = PsiRad = 0.0;
            RudderAngleDeg = 0.0;
            ShaftRps = 0.0;
            Ur = Vr = 0.0;
            WaterDriftNorth = WaterDriftEast = 0.0;
            ApparentWindSpeedMs = ApparentWindFromRelDeg = 0.0;
            BowThrusterEffective = 0.0;
            AdvanceRatioJ = PropThrustN = PropTorqueNm = ShaftPowerW = EngineLoad = 0.0;
            FuelFlowKgPerH = FuelUsedKg = 0.0;
            HeaveM = RollDeg = PitchDeg = 0.0;
            TimeS = 0.0;
        }
    }
}
