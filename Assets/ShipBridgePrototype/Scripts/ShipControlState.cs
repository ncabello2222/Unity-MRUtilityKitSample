using UnityEngine;
using UnityEngine.Events;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Central state written by the bridge control panel and read later by the ship simulation.
    /// Convention: the ship's forward direction is toward the front (wide window) wall.
    /// Positive rudder/thruster values mean starboard, negative mean port.
    /// </summary>
    public class ShipControlState : MonoBehaviour
    {
        public static ShipControlState Instance { get; private set; }

        public enum TelegraphOrder
        {
            FullAstern = -4,
            HalfAstern = -3,
            SlowAstern = -2,
            DeadSlowAstern = -1,
            Stop = 0,
            DeadSlowAhead = 1,
            SlowAhead = 2,
            HalfAhead = 3,
            FullAhead = 4
        }

        [Header("Rudder")]
        [SerializeField] private float commandedRudderAngleDeg;
        [SerializeField] private float actualRudderAngleDeg;
        [SerializeField] private float rudderFollowSpeedDegPerSec = 12f;

        [Header("Propulsion / thrusters")]
        [SerializeField] private TelegraphOrder telegraph = TelegraphOrder.Stop;
        [SerializeField] private float bowThruster;

        [Header("Other")]
        [SerializeField] private bool hornActive;
        [SerializeField] private bool emergencyStop;
        [SerializeField] private bool anchorPortDown;
        [SerializeField] private bool anchorStarboardDown;

        private bool _externallyDrivenRudder;

        public UnityEvent<float> CommandedRudderChanged = new();
        public UnityEvent<float> ActualRudderChanged = new();
        public UnityEvent<TelegraphOrder> TelegraphChanged = new();
        public UnityEvent<float> BowThrusterChanged = new();
        public UnityEvent<bool> HornChanged = new();
        public UnityEvent<bool> EmergencyStopChanged = new();
        public UnityEvent<bool> AnchorPortChanged = new();
        public UnityEvent<bool> AnchorStarboardChanged = new();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Requested rudder angle from the wheel. Positive = starboard, negative = port.</summary>
        public float CommandedRudderAngleDeg
        {
            get => commandedRudderAngleDeg;
            set
            {
                value = Mathf.Clamp(value, -35f, 35f);
                if (!Mathf.Approximately(commandedRudderAngleDeg, value))
                {
                    commandedRudderAngleDeg = value;
                    CommandedRudderChanged.Invoke(value);
                }
            }
        }

        /// <summary>Actual rudder angle that follows the command progressively.</summary>
        public float ActualRudderAngleDeg
        {
            get => actualRudderAngleDeg;
            private set
            {
                if (!Mathf.Approximately(actualRudderAngleDeg, value))
                {
                    actualRudderAngleDeg = value;
                    ActualRudderChanged.Invoke(value);
                }
            }
        }

        /// <summary>Legacy alias for commanded rudder angle.</summary>
        public float RudderAngleDeg
        {
            get => CommandedRudderAngleDeg;
            set => CommandedRudderAngleDeg = value;
        }

        public TelegraphOrder Telegraph
        {
            get => telegraph;
            set
            {
                if (telegraph != value)
                {
                    telegraph = value;
                    TelegraphChanged.Invoke(value);
                }
            }
        }

        /// <summary>Bow thruster command in [-1, 1]. Positive pushes the bow to starboard.</summary>
        public float BowThruster
        {
            get => bowThruster;
            set
            {
                value = Mathf.Clamp(value, -1f, 1f);
                if (!Mathf.Approximately(bowThruster, value))
                {
                    bowThruster = value;
                    BowThrusterChanged.Invoke(value);
                }
            }
        }

        public bool HornActive
        {
            get => hornActive;
            set
            {
                if (hornActive != value)
                {
                    hornActive = value;
                    HornChanged.Invoke(value);
                }
            }
        }

        public bool EmergencyStop
        {
            get => emergencyStop;
            set
            {
                if (emergencyStop != value)
                {
                    emergencyStop = value;
                    EmergencyStopChanged.Invoke(value);
                }
            }
        }

        public bool AnchorPortDown
        {
            get => anchorPortDown;
            set
            {
                if (anchorPortDown != value)
                {
                    anchorPortDown = value;
                    AnchorPortChanged.Invoke(value);
                }
            }
        }

        public bool AnchorStarboardDown
        {
            get => anchorStarboardDown;
            set
            {
                if (anchorStarboardDown != value)
                {
                    anchorStarboardDown = value;
                    AnchorStarboardChanged.Invoke(value);
                }
            }
        }

        /// <summary>
        /// The navigation core owns the steering-gear dynamics; it pushes the
        /// actual rudder angle here every frame so indicators stay authoritative.
        /// </summary>
        public void DriveActualRudder(float angleDeg)
        {
            _externallyDrivenRudder = true;
            ActualRudderAngleDeg = angleDeg;
        }

        private void Update()
        {
            if (_externallyDrivenRudder)
            {
                return; // simulation core drives the actual angle
            }

            if (Mathf.Approximately(actualRudderAngleDeg, commandedRudderAngleDeg))
            {
                return;
            }

            ActualRudderAngleDeg = Mathf.MoveTowards(
                actualRudderAngleDeg,
                commandedRudderAngleDeg,
                rudderFollowSpeedDegPerSec * Time.deltaTime);
        }
    }
}
