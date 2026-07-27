using System;
using System.Collections.Generic;

namespace NavigationSim.Core
{
    public sealed class ArpaTrack
    {
        public int ContactId;
        public string Name = "";
        public double RangeNm;
        public double BearingDeg;
        public double CourseDeg;
        public double SpeedKn;
        public double CpaNm;
        public double TcpaMin;
        public double RelCourseDeg;
        public double RelSpeedKn;
        public bool Dangerous;
    }

    /// <summary>
    /// CPA/TCPA from known traffic kinematics (MVP: no radar acquisition).
    /// Supports a trial own-ship course/speed override for trial manoeuvre.
    /// </summary>
    public sealed class ArpaTracker
    {
        private readonly List<ArpaTrack> _tracks = new List<ArpaTrack>();

        public IReadOnlyList<ArpaTrack> Tracks => _tracks;
        public bool Enabled = true;
        public bool TrueVectors = true;
        public double VectorMinutes = 6.0;

        /// <summary>When true, CPA uses <see cref="TrialCourseDeg"/> / <see cref="TrialSpeedKn"/>.</summary>
        public bool TrialManoeuvre;
        public double TrialCourseDeg;
        public double TrialSpeedKn = 10.0;

        public void Update(ShipState own, TrafficWorld traffic)
        {
            _tracks.Clear();
            if (!Enabled || own == null || traffic == null)
            {
                return;
            }

            double ownCourse = TrialManoeuvre ? TrialCourseDeg : own.CogDeg;
            double ownSog = TrialManoeuvre
                ? TrialSpeedKn / 1.94384449244
                : Math.Max(own.SogMs, 0.0);

            double ownPsi = ownCourse * Math.PI / 180.0;
            double ownVn = ownSog * Math.Cos(ownPsi);
            double ownVe = ownSog * Math.Sin(ownPsi);

            for (int i = 0; i < traffic.Contacts.Count; i++)
            {
                TrafficContact c = traffic.Contacts[i];
                if (!c.IsTrackable)
                {
                    continue;
                }

                c.RangeBearingFrom(own.North, own.East, out double rangeM, out double bearingDeg);

                double tgtPsi = c.HeadingDeg * Math.PI / 180.0;
                double tgtVn = c.SogMs * Math.Cos(tgtPsi);
                double tgtVe = c.SogMs * Math.Sin(tgtPsi);

                double relVn = tgtVn - ownVn;
                double relVe = tgtVe - ownVe;
                double relSpeed = Math.Sqrt(relVn * relVn + relVe * relVe);

                double dn = c.North - own.North;
                double de = c.East - own.East;

                double tcpaS = 0.0;
                double cpaM = rangeM;
                if (relSpeed > 1e-3)
                {
                    // Time to CPA along relative velocity (negative = opening / past).
                    tcpaS = -(dn * relVn + de * relVe) / (relSpeed * relSpeed);
                    double cpaN = dn + relVn * tcpaS;
                    double cpaE = de + relVe * tcpaS;
                    cpaM = Math.Sqrt(cpaN * cpaN + cpaE * cpaE);
                }

                double relCourse = ShipState.Normalize360(Math.Atan2(relVe, relVn) * 180.0 / Math.PI);

                var track = new ArpaTrack
                {
                    ContactId = c.Id,
                    Name = c.Name,
                    RangeNm = rangeM / 1852.0,
                    BearingDeg = bearingDeg,
                    CourseDeg = c.HeadingDeg,
                    SpeedKn = c.SogKn,
                    CpaNm = cpaM / 1852.0,
                    TcpaMin = tcpaS / 60.0,
                    RelCourseDeg = relCourse,
                    RelSpeedKn = relSpeed * 1.94384449244,
                    Dangerous = cpaM < 926.0 && tcpaS > 0.0 && tcpaS < 1800.0 // under 0.5 Nm and 30 min
                };
                _tracks.Add(track);
            }


            // Closing targets first by time to CPA, then everything opening or past CPA by
            // range. Switching key on the pair being compared (the previous approach) is
            // not a total order — A<C, C<B, B<A is reachable with three ordinary tracks —
            // and List.Sort throws InvalidOperationException when it detects the cycle.
            // One key per track keeps it a genuine ordering.
            _tracks.Sort(CompareByThreat);
        }

        /// <summary>
        /// Total order: approaching tracks (TCPA &gt;= 0) ahead of opening ones, closest in
        /// time first; opening tracks after them, nearest first. Ties fall back to contact
        /// id so the list does not shuffle between frames.
        /// </summary>
        private static int CompareByThreat(ArpaTrack a, ArpaTrack b)
        {
            bool aClosing = a.TcpaMin >= 0.0;
            bool bClosing = b.TcpaMin >= 0.0;
            if (aClosing != bClosing)
            {
                return aClosing ? -1 : 1;
            }

            int primary = aClosing
                ? a.TcpaMin.CompareTo(b.TcpaMin)
                : a.RangeNm.CompareTo(b.RangeNm);
            return primary != 0 ? primary : a.ContactId.CompareTo(b.ContactId);
        }
    }
}
