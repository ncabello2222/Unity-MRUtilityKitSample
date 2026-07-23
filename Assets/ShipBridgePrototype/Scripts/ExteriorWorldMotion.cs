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
        [Tooltip("World-space pivot for yaw (typically room/bridge center).")]
        [SerializeField] private Transform shipPivot;

        [Header("Visual response")]
        [Tooltip("Scales the wave-induced heave/roll/pitch applied to the horizon.")]
        [SerializeField] private float seakeepingVisualGain = 1f;

        [Tooltip(
            "If off (default), only heave + yaw move ExteriorWorld. Pitch/roll stay off so the " +
            "NorthStar ocean stays locked to terrain vertically. Enable only if the exterior " +
            "should physically tilt.")]
        [SerializeField] private bool applySeakeepingAttitudeToExterior = false;

        private Matrix4x4 _initialExteriorWorld;
        private Matrix4x4 _initialShipWorld;
        private bool _hasInitialPose;
        private Vector3 _initialPivotPosition;
        private Quaternion _shipForwardBasis = Quaternion.identity;

        public ExteriorWorldRoot ExteriorWorld => exteriorWorld;
        public Transform ShipPivot => shipPivot;
        public Quaternion ShipForwardBasis => _shipForwardBasis;
        public Vector3 InitialPivotPosition => _initialPivotPosition;
        public bool HasInitialPose => _hasInitialPose;

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

            ApplyInverseExteriorPose();
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

            // Keep virtual ship progress; only refresh the exterior baseline so the new
            // scenario appears at the current relative ship pose.
            var shipDelta = CurrentShipWorld() * _initialShipWorld.inverse;
            _initialExteriorWorld = shipDelta * exteriorWorld.Root.localToWorldMatrix;
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
            _initialShipWorld = Matrix4x4.TRS(pivotPos, _shipForwardBasis, Vector3.one);
            _initialExteriorWorld = exteriorWorld.Root.localToWorldMatrix;
            _hasInitialPose = true;

            var runner = NavigationSimRunner.EnsureInstance();
            runner.ResetShip();
        }

        /// <summary>
        /// Ship pose in Unity world space built from the core state. The sim uses
        /// North/East/psi with psi = 0 along the initial forward basis:
        /// sim North → basis forward (+Z), sim East → basis right (+X).
        /// </summary>
        private Matrix4x4 CurrentShipWorld()
        {
            var runner = NavigationSimRunner.Instance;
            if (runner == null)
            {
                return _initialShipWorld;
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
            var attitude = Quaternion.Euler(pitch, (float)runner.InterpPsiDeg, roll);

            var position = _initialPivotPosition + _shipForwardBasis * localOffset;
            return Matrix4x4.TRS(position, _shipForwardBasis * attitude, Vector3.one);
        }

        private void ApplyInverseExteriorPose()
        {
            // Room stays fixed. Exterior receives Inverse(shipDelta) so the view matches a moving ship.
            var shipDelta = CurrentShipWorld() * _initialShipWorld.inverse;
            var exteriorMatrix = shipDelta.inverse * _initialExteriorWorld;

            exteriorWorld.Root.SetPositionAndRotation(exteriorMatrix.GetColumn(3), exteriorMatrix.rotation);
        }
    }
}
