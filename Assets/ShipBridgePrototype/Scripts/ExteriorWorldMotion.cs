using NavigationSim.Core;
using NavigationSim.UnityLayer;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// World-motion driver (§9 of the master plan). The bridge/room stays fixed;
    /// this reads the interpolated ship pose from the navigation core and moves
    /// the exterior world inversely, including the visual heave/roll/pitch
    /// channel. The old arcade integrator was replaced by the MMG/Clarke core in
    /// <see cref="NavigationSimRunner"/>.
    /// </summary>
    public class ExteriorWorldMotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ShipControlState controlState;
        [SerializeField] private ExteriorWorldRoot exteriorWorld;
        [Tooltip("Bridge anchor (room center). The ship's maneuvering origin sits ahead of this by the active vessel's SimOriginForwardFromBridgeM.")]
        [SerializeField] private Transform shipPivot;

        [Header("Visual response")]
        [Tooltip("Scales the wave-induced heave/roll/pitch applied to the horizon.")]
        [SerializeField] private float seakeepingVisualGain = 1f;

        [Tooltip(
            "If off (default), only heave + yaw move ExteriorWorld. Pitch/roll stay off so the " +
            "NorthStar ocean stays locked to terrain vertically. Enable only if the exterior " +
            "should physically tilt.")]
        [SerializeField] private bool applySeakeepingAttitudeToExterior = false;

        private Vector3 _initialExteriorPosition;
        private Quaternion _initialExteriorRotation = Quaternion.identity;
        private bool _hasInitialPose;
        private Vector3 _initialPivotPosition;
        private Quaternion _shipForwardBasis = Quaternion.identity;
        private float _simOriginForwardM;

        public ExteriorWorldRoot ExteriorWorld => exteriorWorld;
        public Transform ShipPivot => shipPivot;
        public Quaternion ShipForwardBasis => _shipForwardBasis;
        public Vector3 InitialPivotPosition => _initialPivotPosition;
        public bool HasInitialPose => _hasInitialPose;

        /// <summary>
        /// Unity-world anchor of the ship's maneuvering origin at psi = 0. The bridge
        /// sits at InitialPivotPosition; the sim state (East/North/psi) tracks a point
        /// SimOriginForwardFromBridgeM ahead of it, so the exterior yaws around this
        /// point and the bridge sweeps laterally during turns like a real aft bridge.
        /// </summary>
        public Vector3 SimOriginInitialPosition =>
            _initialPivotPosition + _shipForwardBasis * new Vector3(0f, 0f, _simOriginForwardM);

        private void Awake()
        {
            ResolveReferences();
            NavigationSimRunner.EnsureInstance();
        }

        private void Start()
        {
            ResolveReferences();
            TryCaptureInitialPose();
        }

        private void Update()
        {
            ResolveReferences();
            if (exteriorWorld == null)
            {
                return;
            }

            if (!_hasInitialPose)
            {
                TryCaptureInitialPose();
                if (!_hasInitialPose)
                {
                    return;
                }
            }

            RefreshSimOriginOffset();
            ApplyInverseExteriorPose();
        }

        private void RefreshSimOriginOffset()
        {
            var runner = NavigationSimRunner.Instance;
            if (runner == null)
            {
                return;
            }

            // Vessel switches call ResetShip(), so the anchor jump happens while the
            // relative transform is identity and the exterior never pops.
            _simOriginForwardM = VesselCatalog.Get(runner.ActiveVesselIndex).SimOriginForwardFromBridgeM;
        }

        /// <summary>Called by BridgeRoomMapper after exterior generation.</summary>
        public void BindExterior(ExteriorWorldRoot root, Transform pivot = null, Vector3 shipForward = default)
        {
            exteriorWorld = root;
            if (pivot != null)
            {
                shipPivot = pivot;
                root.SetMotionPivot(pivot);
            }

            if (shipForward.sqrMagnitude > 1e-6f)
            {
                shipForward.y = 0f;
                _shipForwardBasis = Quaternion.LookRotation(shipForward.normalized, Vector3.up);
            }
            else if (pivot != null)
            {
                var f = pivot.forward;
                f.y = 0f;
                _shipForwardBasis = f.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(f.normalized, Vector3.up)
                    : Quaternion.identity;
            }
            else
            {
                _shipForwardBasis = Quaternion.identity;
            }

            CaptureInitialPose();
        }

        public void SetControlState(ShipControlState state)
        {
            controlState = state;
        }

        /// <summary>
        /// Re-read the exterior pose after swapping scenario content under ExteriorWorld.
        /// </summary>
        public void RecaptureExteriorPose(bool resetShipState = false)
        {
            if (exteriorWorld == null)
            {
                return;
            }

            if (resetShipState || !_hasInitialPose)
            {
                CaptureInitialPose();
                return;
            }

            // Keep virtual ship progress; bake E0 so S0*S^-1*E0 == current exterior.
            // E0 = S * S0^-1 * E_current.
            ResolveShipPose(out var shipPos, out var shipRot);
            var root = exteriorWorld.Root;
            var invShip0 = Quaternion.Inverse(_shipForwardBasis);
            _initialExteriorPosition =
                shipPos + shipRot * (invShip0 * (root.position - SimOriginInitialPosition));
            _initialExteriorRotation = shipRot * invShip0 * root.rotation;
        }

        private void ResolveReferences()
        {
            if (controlState == null)
            {
                controlState = ShipControlState.Instance;
            }

            if (exteriorWorld == null)
            {
                exteriorWorld = ExteriorWorldRoot.Instance;
            }
        }

        private void TryCaptureInitialPose()
        {
            if (exteriorWorld != null)
            {
                CaptureInitialPose();
            }
        }

        private void CaptureInitialPose()
        {
            var pivotPos = shipPivot != null ? shipPivot.position : exteriorWorld.MotionPivot.position;
            _initialPivotPosition = pivotPos;
            _initialExteriorPosition = exteriorWorld.Root.position;
            _initialExteriorRotation = exteriorWorld.Root.rotation;
            _hasInitialPose = true;

            var runner = NavigationSimRunner.EnsureInstance();
            runner.ResetShip();
            RefreshSimOriginOffset();
        }

        /// <summary>
        /// Virtual ship pose in Unity world space. Sim North/East/psi with psi = 0
        /// along the initial forward basis: North → basis +Z, East → basis +X.
        /// The pose is anchored at the maneuvering origin, not the bridge.
        /// </summary>
        private void ResolveShipPose(out Vector3 shipPos, out Quaternion shipRot)
        {
            var runner = NavigationSimRunner.Instance;
            if (runner == null)
            {
                shipPos = SimOriginInitialPosition;
                shipRot = _shipForwardBasis;
                return;
            }

            float gain = seakeepingVisualGain;
            var localOffset = new Vector3(
                (float)runner.InterpEast,
                (float)runner.InterpHeave * gain,
                (float)runner.InterpNorth);

            // Yaw always. Pitch/roll only if explicitly enabled — tilting ExteriorWorld can
            // make the shoreline slide vs the NorthStar ocean surface.
            float pitch = applySeakeepingAttitudeToExterior ? (float)runner.InterpPitchDeg * gain : 0f;
            float roll = applySeakeepingAttitudeToExterior ? -(float)runner.InterpRollDeg * gain : 0f;
            // Sim ψ is compass heading (0=bow/N, + = starboard). Applied as Unity yaw
            // with the sign that matches port/starboard on mesh exteriors (Terrain could
            // not rotate, so an earlier negate looked "correct" for the wrong reason).
            var attitude = Quaternion.Euler(pitch, (float)runner.InterpPsiDeg, roll);

            shipPos = SimOriginInitialPosition + _shipForwardBasis * localOffset;
            shipRot = _shipForwardBasis * attitude;
        }

        private void ApplyInverseExteriorPose()
        {
            // Room stays fixed. Geographic G maps to Unity as:
            //   X' = shipPos0 + R0 * R^-1 * (G - shipPos)
            // so the CURRENT sim position (East/North) stays locked under shipPos0.
            // shipPos0 is the maneuvering origin (midships), SimOriginForwardFromBridgeM
            // ahead of the bridge — NOT the hull/bridge pivot. From a Scene top-down on
            // the stern hull this looks like scenery "orbiting" a point ahead of the ship;
            // from the bridge it is the correct stern sweep in a turn.
            ResolveShipPose(out var shipPos, out var shipRot);

            var shipPos0 = SimOriginInitialPosition;
            var shipRot0 = _shipForwardBasis;
            var invShip = Quaternion.Inverse(shipRot);

            var newPos = shipPos0 + shipRot0 * (invShip * (_initialExteriorPosition - shipPos));
            var newRot = shipRot0 * invShip * _initialExteriorRotation;

            exteriorWorld.Root.SetPositionAndRotation(newPos, newRot);
        }

        /// <summary>
        /// Unity world position where the geographic point (east, north) renders under
        /// the current inverse-exterior transform. Used by the ocean adapter to sample
        /// the wave field in the exact frame the terrain is drawn in.
        /// </summary>
        public Vector3 GeoToWorld(double east, double north)
        {
            var virtualPos = SimOriginInitialPosition +
                             _shipForwardBasis * new Vector3((float)east, 0f, (float)north);
            ResolveShipPose(out var shipPos, out var shipRot);
            return SimOriginInitialPosition +
                   _shipForwardBasis * (Quaternion.Inverse(shipRot) * (virtualPos - shipPos));
        }

        /// <summary>
        /// How a geographic displacement (east, north) renders in the room frame:
        /// GeoToWorld(P + D) - GeoToWorld(P), independent of P. Content that drifts
        /// geographically instead of staying put — the water mass under a current —
        /// adds this on top of the rigid shift the terrain receives.
        /// </summary>
        public Vector3 GeoVectorToWorld(double east, double north)
        {
            ResolveShipPose(out _, out var shipRot);
            var geo = _shipForwardBasis * new Vector3((float)east, 0f, (float)north);
            return _shipForwardBasis * (Quaternion.Inverse(shipRot) * geo);
        }

        /// <summary>
        /// How far the exterior content that started at worldPoint has moved under the
        /// current inverse transform (mapped - original). Zero while the ship is at rest.
        /// </summary>
        public Vector3 ComputeWorldShiftAt(Vector3 worldPoint)
        {
            ResolveShipPose(out var shipPos, out var shipRot);
            var mapped = SimOriginInitialPosition +
                         _shipForwardBasis * (Quaternion.Inverse(shipRot) * (worldPoint - shipPos));
            return mapped - worldPoint;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_hasInitialPose)
            {
                return;
            }

            // Yellow = bridge / hull pivot (fixed in Unity).
            // Red = midships lock: exterior yaw/translate keeps the CURRENT sim
            // (East/North) under this point. Do NOT draw the pre-inverse "ghost"
            // shipPos — that drifts away in world space and looks like a bug.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_initialPivotPosition, 1.5f);
            var lockPos = SimOriginInitialPosition;
            Gizmos.DrawLine(_initialPivotPosition, lockPos);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(lockPos, 2f);

            if (!Application.isPlaying || NavigationSimRunner.Instance == null)
            {
                return;
            }

            // Green ray on the lock: sim heading in the FIXED room frame (same yaw sign
            // as ResolveShipPose).
            var psiDeg = (float)NavigationSimRunner.Instance.InterpPsiDeg;
            var headingRoom = _shipForwardBasis * Quaternion.Euler(0f, psiDeg, 0f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(lockPos, headingRoom * Vector3.forward * 25f);
        }
#endif
    }
}
