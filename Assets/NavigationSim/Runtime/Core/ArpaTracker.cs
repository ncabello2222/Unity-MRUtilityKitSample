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

        /// <summary>
        /// Track objects, reused across updates. <see cref="_tracks"/> gets sorted and
        /// the pool does not, so the two can hold the same references in different
        /// orders without the pool losing track of which objects are already built.
        /// Callers must read a track within the update that produced it — which every
        /// caller does, since they all render from the current state.
        /// </summary>
        private readonly List<ArpaTrack> _pool = new List<ArpaTrack>();
        private int _poolUsed;

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
            _poolUsed = 0;
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

                ArpaTrack track = Rent();
                track.ContactId = c.Id;
                track.Name = c.Name;
                track.RangeNm = rangeM / 1852.0;
                track.BearingDeg = bearingDeg;
                track.CourseDeg = c.HeadingDeg;
                track.SpeedKn = c.SogKn;
                track.CpaNm = cpaM / 1852.0;
                track.TcpaMin = tcpaS / 60.0;
                track.RelCourseDeg = relCourse;
                track.RelSpeedKn = relSpeed * 1.94384449244;
                // Under 0.5 Nm and 30 min.
                track.Dangerous = cpaM < 926.0 && tcpaS > 0.0 && tcpaS < 1800.0;
                _tracks.Add(track);
            }


            // Closing targets first by time to CPA, then everything opening or past CPA by
            // range. Switching key on the pair being compared (the previous approach) is
            // not a total order — A<C, C<B, B<A is reachable with three ordinary tracks —
            // and List.Sort throws InvalidOperationException when it detects the cycle.
            // One key per track keeps it a genuine ordering.
            _tracks.Sort(CompareByThreat);
        }

        private ArpaTrack Rent()
        {
            if (_poolUsed == _pool.Count)
            {
                _pool.Add(new ArpaTrack());
            }

            return _pool[_poolUsed++];
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
