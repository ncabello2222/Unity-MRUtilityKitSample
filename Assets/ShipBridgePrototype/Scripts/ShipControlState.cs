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
        public enum TelegraphOrder
        {
            FullAstern = -2,
            HalfAstern = -1,
            Stop = 0,
            HalfAhead = 1,
            FullAhead = 2
        }

        [Header("Live values (read-only in inspector)")]
        [SerializeField] private float rudderAngleDeg;
        [SerializeField] private TelegraphOrder telegraph = TelegraphOrder.Stop;
        [SerializeField] private float bowThruster;
        [SerializeField] private bool hornActive;
        [SerializeField] private bool emergencyStop;
        [SerializeField] private bool anchorPortDown;
        [SerializeField] private bool anchorStarboardDown;

        public UnityEvent<float> RudderChanged = new();
        public UnityEvent<TelegraphOrder> TelegraphChanged = new();
        public UnityEvent<float> BowThrusterChanged = new();
        public UnityEvent<bool> HornChanged = new();
        public UnityEvent<bool> EmergencyStopChanged = new();
        public UnityEvent<bool> AnchorPortChanged = new();
        public UnityEvent<bool> AnchorStarboardChanged = new();

        /// <summary>Commanded rudder angle in degrees. Positive = starboard, negative = port.</summary>
        public float RudderAngleDeg
        {
            get => rudderAngleDeg;
            set
            {
                if (!Mathf.Approximately(rudderAngleDeg, value))
                {
                    rudderAngleDeg = value;
                    RudderChanged.Invoke(value);
                }
            }
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
    }
}
