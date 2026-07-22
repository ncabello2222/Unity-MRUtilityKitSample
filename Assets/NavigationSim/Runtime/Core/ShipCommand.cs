namespace NavigationSim.Core
{
    public enum SteeringMode
    {
        Hand = 0,
        Nfu = 1,
        Auto = 2
    }

    /// <summary>
    /// Orders coming from the bridge (wheel, telegraph, panel). The simulation core
    /// never reads Unity objects directly; the runner fills this once per tick.
    /// </summary>
    public class ShipCommand
    {
        /// <summary>Requested rudder angle in HAND mode. Positive = starboard.</summary>
        public double RudderCommandDeg;

        /// <summary>NFU lever: -1 move rudder to port, +1 to starboard, 0 hold.</summary>
        public int NfuDirection;

        public SteeringMode SteeringMode = SteeringMode.Hand;

        /// <summary>Autopilot heading setpoint, compass degrees.</summary>
        public double HeadingSetpointDeg;

        /// <summary>Signed shaft rpm fraction from the telegraph table, -1..+1.</summary>
        public double TelegraphFraction;

        /// <summary>Bow thruster command, -1 (port) .. +1 (starboard).</summary>
        public double BowThruster;

        public bool EmergencyStop;
        public bool EngineReady = true;

        /// <summary>Steering gear pumps; with no pump the rudder is frozen.</summary>
        public bool SteeringPump1 = true;
        public bool SteeringPump2 = true;

        public bool AnySteeringPump => SteeringPump1 || SteeringPump2;
    }
}
