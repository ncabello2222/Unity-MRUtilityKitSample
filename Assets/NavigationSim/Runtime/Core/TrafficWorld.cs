using System;
using System.Collections.Generic;

namespace NavigationSim.Core
{
    /// <summary>
    /// Deterministic traffic set consumed by radar, chart, AIS and presenters.
    /// </summary>
    public sealed class TrafficWorld
    {
        private readonly List<TrafficContact> _contacts = new List<TrafficContact>();
        private int _nextId = 1;

        public IReadOnlyList<TrafficContact> Contacts => _contacts;

        public TrafficContact Add(TrafficContact contact)
        {
            if (contact.Id <= 0)
            {
                contact.Id = _nextId++;
            }
            else if (contact.Id >= _nextId)
            {
                _nextId = contact.Id + 1;
            }

            _contacts.Add(contact);
            return contact;
        }

        public void Clear()
        {
            _contacts.Clear();
            _nextId = 1;
        }

        public void Step(double dt)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                _contacts[i].Integrate(dt);
            }
        }

        /// <summary>
        /// Coastal demo: two crossing ships, one overtaking, one buoy ahead of bow.
        /// Positions are relative to own-ship start (0,0) facing north.
        /// </summary>
        public void LoadCoastalDemo()
        {
            Clear();

            Add(new TrafficContact
            {
                Name = "COASTER ALPHA",
                Mmsi = 224123456,
                Kind = TrafficKind.Ship,
                North = 420.0,
                East = 180.0,
                HeadingDeg = 220.0,
                SogMs = 5.5,
                LengthM = 75.0,
                BeamM = 12.0
            });

            Add(new TrafficContact
            {
                Name = "TUG BRAVO",
                Mmsi = 224654321,
                Kind = TrafficKind.Ship,
                North = 280.0,
                East = -220.0,
                HeadingDeg = 90.0,
                SogMs = 3.2,
                LengthM = 28.0,
                BeamM = 9.0
            });

            Add(new TrafficContact
            {
                Name = "FERRY CHARLIE",
                Mmsi = 224111222,
                Kind = TrafficKind.Ship,
                North = -150.0,
                East = 90.0,
                HeadingDeg = 350.0,
                SogMs = 7.0,
                LengthM = 95.0,
                BeamM = 18.0
            });

            Add(new TrafficContact
            {
                Name = "N CARDINAL",
                Mmsi = 0,
                Kind = TrafficKind.Buoy,
                North = 200.0,
                East = 40.0,
                HeadingDeg = 0.0,
                SogMs = 0.0,
                LengthM = 2.0,
                BeamM = 2.0
            });

            Add(new TrafficContact
            {
                Name = "PORT HAND",
                Mmsi = 0,
                Kind = TrafficKind.Buoy,
                North = 120.0,
                East = -60.0,
                HeadingDeg = 0.0,
                SogMs = 0.0,
                LengthM = 2.0,
                BeamM = 2.0
            });
        }

        public TrafficContact FindById(int id)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].Id == id)
                {
                    return _contacts[i];
                }
            }

            return null;
        }
    }
}
