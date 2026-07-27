using System;

namespace NavigationSim.Core
{
    public enum TrafficKind
    {
        Ship = 0,
        Buoy = 1,
        Mob = 2
    }

    /// <summary>
    /// One traffic / nav-aid contact in the local NED frame. Pure data — no Unity.
    /// </summary>
    public sealed class TrafficContact
    {
        public int Id;
        public string Name = "";
        public int Mmsi;
        public TrafficKind Kind = TrafficKind.Ship;

        public double North;
        public double East;
        public double HeadingDeg;
        public double SogMs;
        public double LengthM = 40.0;
        public double BeamM = 8.0;
        public bool Visible = true;

        /// <summary>True when ARPA / AIS should treat this as a moving vessel.</summary>
        public bool IsTrackable => Kind == TrafficKind.Ship && Visible;

        /// <summary>
        /// Contacts steer straight lines with no leeway or set, so course over ground is
        /// heading. Kept as a distinct member because radar, ARPA and AIS all ask for
        /// course, and the day contacts get drift the change belongs here alone.
        /// </summary>
        public double CogDeg => HeadingDeg;

        public double SogKn => SogMs * 1.94384449244;

        public void Integrate(double dt)
        {
            if (Kind == TrafficKind.Buoy || !Visible || SogMs < 1e-4)
            {
                return;
            }

            double psi = HeadingDeg * Math.PI / 180.0;
            North += SogMs * Math.Cos(psi) * dt;
            East += SogMs * Math.Sin(psi) * dt;
        }

        public void RangeBearingFrom(double ownNorth, double ownEast, out double rangeM, out double bearingDeg)
        {
            double dn = North - ownNorth;
            double de = East - ownEast;
            rangeM = Math.Sqrt(dn * dn + de * de);
            bearingDeg = ShipState.Normalize360(Math.Atan2(de, dn) * 180.0 / Math.PI);
        }
    }
}
