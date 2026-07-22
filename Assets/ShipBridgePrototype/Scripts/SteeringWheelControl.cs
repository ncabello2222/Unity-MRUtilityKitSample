using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Maps physical wheel rotation to commanded rudder angle [-35, +35].
    /// Convention: right / starboard wheel → positive rudder, left / port → negative.
    /// The measured sign depends on which way the wheel's local +Z faces; when it
    /// points toward the helm a left turn reads positive, so <see cref="invertRudder"/>
    /// flips it back to the port(-)/starboard(+) convention shared by the sim and displays.
    /// </summary>
    public class SteeringWheelControl : MonoBehaviour
    {
        [SerializeField] private ShipControlState controlState;
        [SerializeField] private Transform wheelTransform;
        [SerializeField] private float maxWheelAngleDeg = 400f;
        [SerializeField] private float maxRudderAngleDeg = 35f;
        [Tooltip("Invert wheel→rudder sign so a left (port) turn maps to negative rudder. " +
                 "Needed when the wheel's local +Z faces the helm.")]
        [SerializeField] private bool invertRudder = true;
        [Tooltip("Local axis used by OneGrabRotateTransformer (Forward = Z).")]
        [SerializeField] private Vector3 localRotationAxis = Vector3.forward;

        private Quaternion _restLocalRotation;
        private float _accumulatedAngle;
        private float _prevRawAngle;
        private bool _hasPrevRaw;

        public Transform WheelTransform => wheelTransform;
        public float CurrentWheelAngleDeg => _accumulatedAngle;

        private void Awake()
        {
            if (wheelTransform == null)
            {
                wheelTransform = transform;
            }

            ResolveControlState();
            _restLocalRotation = wheelTransform.localRotation;
            localRotationAxis.Normalize();
            _accumulatedAngle = 0f;
            _hasPrevRaw = false;
        }

        private void Update()
        {
            ResolveControlState();
            if (controlState == null)
            {
                return;
            }

            var angle = ReadSignedWheelAngle();
            var t = Mathf.Clamp(angle / maxWheelAngleDeg, -1f, 1f);
            if (invertRudder)
            {
                t = -t;
            }

            controlState.CommandedRudderAngleDeg = t * maxRudderAngleDeg;
        }

        private void ResolveControlState()
        {
            if (controlState == null)
            {
                controlState = ShipControlState.Instance;
            }
        }

        /// <summary>
        /// Quaternions only encode orientation mod 360°, and ToAngleAxis folds to ±180°.
        /// Accumulate frame-to-frame DeltaAngle so multi-turn wheel motion stays monotonic.
        /// </summary>
        private float ReadSignedWheelAngle()
        {
            var raw = ReadRawSignedAngle();
            if (!_hasPrevRaw)
            {
                _prevRawAngle = raw;
                _accumulatedAngle = raw;
                _hasPrevRaw = true;
                return _accumulatedAngle;
            }

            _accumulatedAngle += Mathf.DeltaAngle(_prevRawAngle, raw);
            _prevRawAngle = raw;
            _accumulatedAngle = Mathf.Clamp(_accumulatedAngle, -maxWheelAngleDeg, maxWheelAngleDeg);
            return _accumulatedAngle;
        }

        private float ReadRawSignedAngle()
        {
            var delta = Quaternion.Inverse(_restLocalRotation) * wheelTransform.localRotation;
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            if (axis.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            axis.Normalize();
            return angle * Mathf.Sign(Vector3.Dot(axis, localRotationAxis));
        }

#if UNITY_EDITOR
        public void EditorBind(ShipControlState state, Transform wheel)
        {
            controlState = state;
            wheelTransform = wheel;
        }
#endif
    }
}
