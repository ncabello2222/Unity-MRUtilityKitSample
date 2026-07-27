using System;
using System.Collections.Generic;

namespace NavigationSim.Core
{
    public enum RadarOrientation
    {
        NorthUp = 0,
        HeadUp = 1,
        CourseUp = 2
    }

    public struct RadarEcho
    {
        public int ContactId;
        public string Name;
        public double RangeNm;
        public double BearingDeg;
        public TrafficKind Kind;
        public bool IsLand;
    }

    public struct ParallelIndex
    {
        public bool Active;
        public double RangeNm;
        public double BearingDeg;
    }

    /// <summary>
    /// Synthetic marine radar state: range, orientation, EBL/VRM, guard zone, PIs.
    /// Echoes come from <see cref="TrafficWorld"/> plus optional land samples.
    /// </summary>
    public sealed class RadarModel
    {
        private static readonly double[] RangeStepsNm =
        {
            0.25, 0.5, 0.75, 1.5, 3.0, 6.0, 12.0, 24.0
        };

        private readonly List<RadarEcho> _echoes = new List<RadarEcho>();
        private readonly ParallelIndex[] _parallelIndexes = new ParallelIndex[10];

        public bool PowerOn = true;
        public int RangeIndex = 4; // 3 Nm default
        public RadarOrientation Orientation = RadarOrientation.NorthUp;

        public double EblBearingDeg;
        public double VrmNm = 1.0;
        public double CursorBearingDeg;
        public double CursorRangeNm = 1.0;

        public bool GuardZoneOn;
        public double GuardInnerNm = 0.5;
        public double GuardOuterNm = 1.5;
        public double GuardStartDeg;
        public double GuardEndDeg = 90.0;
        public bool GuardAlarm;

        public IReadOnlyList<RadarEcho> Echoes => _echoes;
        public ParallelIndex[] ParallelIndexes => _parallelIndexes;

        public double RangeNm => RangeStepsNm[Math.Clamp(RangeIndex, 0, RangeStepsNm.Length - 1)];

        public void IncreaseRange()
        {
            if (RangeIndex < RangeStepsNm.Length - 1)
            {
                RangeIndex++;
            }
        }

        public void DecreaseRange()
        {
            if (RangeIndex > 0)
            {
                RangeIndex--;
            }
        }

        public void CycleOrientation()
        {
            Orientation = (RadarOrientation)(((int)Orientation + 1) % 3);
        }

        /// <summary>
        /// Screen angle for PPI drawing: 0 = up, clockwise positive (degrees).
        /// </summary>
        public double BearingToScreenDeg(double trueBearingDeg, double ownHeadingDeg, double ownCogDeg)
        {
            double refDeg = Orientation switch
            {
                RadarOrientation.HeadUp => ownHeadingDeg,
                RadarOrientation.CourseUp => ownCogDeg,
                _ => 0.0
            };
            return ShipState.Normalize360(trueBearingDeg - refDeg);
        }

        public void UpdateEchoes(ShipState own, TrafficWorld traffic, IReadOnlyList<(double north, double east)> landSamples)
        {
            _echoes.Clear();
            GuardAlarm = false;
            if (!PowerOn || own == null)
            {
                return;
            }

            double maxM = RangeNm * 1852.0;

            if (traffic != null)
            {
                for (int i = 0; i < traffic.Contacts.Count; i++)
                {
                    TrafficContact c = traffic.Contacts[i];
                    if (!c.Visible)
                    {
                        continue;
                    }

                    c.RangeBearingFrom(own.North, own.East, out double rangeM, out double bearingDeg);
                    if (rangeM > maxM * 1.05)
                    {
                        continue;
                    }

                    double rangeNm = rangeM / 1852.0;
                    _echoes.Add(new RadarEcho
                    {
                        ContactId = c.Id,
                        Name = c.Name,
                        RangeNm = rangeNm,
                        BearingDeg = bearingDeg,
                        Kind = c.Kind,
                        IsLand = false
                    });

                    if (GuardZoneOn && IsInGuardZone(rangeNm, bearingDeg))
                    {
                        GuardAlarm = true;
                    }
                }
            }

            if (landSamples != null)
            {
                for (int i = 0; i < landSamples.Count; i++)
                {
                    var (n, e) = landSamples[i];
                    double dn = n - own.North;
                    double de = e - own.East;
                    double rangeM = Math.Sqrt(dn * dn + de * de);
                    if (rangeM > maxM || rangeM < 1.0)
                    {
                        continue;
                    }

                    double bearing = ShipState.Normalize360(Math.Atan2(de, dn) * 180.0 / Math.PI);
                    _echoes.Add(new RadarEcho
                    {
                        ContactId = -1,
                        Name = "LAND",
                        RangeNm = rangeM / 1852.0,
                        BearingDeg = bearing,
                        Kind = TrafficKind.Buoy,
                        IsLand = true
                    });
                }
            }
        }

        public bool IsInGuardZone(double rangeNm, double bearingDeg)
        {
            if (rangeNm < GuardInnerNm || rangeNm > GuardOuterNm)
            {
                return false;
            }

            double start = ShipState.Normalize360(GuardStartDeg);
            double end = ShipState.Normalize360(GuardEndDeg);
            double b = ShipState.Normalize360(bearingDeg);
            if (start <= end)
            {
                return b >= start && b <= end;
            }

            return b >= start || b <= end;
        }

        public void SetParallelIndex(int index, bool active, double rangeNm, double bearingDeg)
        {
            if (index < 0 || index >= _parallelIndexes.Length)
            {
                return;
            }

            _parallelIndexes[index] = new ParallelIndex
            {
                Active = active,
                RangeNm = rangeNm,
                BearingDeg = bearingDeg
            };
        }
    }
}
