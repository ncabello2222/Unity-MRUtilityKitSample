using TMPro;
using UnityEngine;
using Oculus.Interaction;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Horizontal bow-thruster lever with automatic spring return to center.
    /// Negative = bow to port, positive = bow to starboard.
    /// </summary>
    public class BowThrusterControl : MonoBehaviour
    {
        [SerializeField] private ShipControlState controlState;
        [SerializeField] private Transform leverTransform;
        [SerializeField] private TextMeshPro valueLabel;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private float maxAngleDeg = 90f;
        [SerializeField] private float returnSpeedDegPerSec = 220f;
        [SerializeField] private Vector3 localRotationAxis = Vector3.up;

        private Quaternion _restLocalRotation;
        private bool _isGrabbed;

        private void Awake()
        {
            if (leverTransform == null)
            {
                leverTransform = transform;
            }

            if (grabbable == null)
            {
                grabbable = GetComponentInChildren<Grabbable>();
            }

            ResolveControlState();
            _restLocalRotation = leverTransform.localRotation;
            localRotationAxis.Normalize();
        }

        private void OnEnable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += OnPointerEvent;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= OnPointerEvent;
            }
        }

        private void Update()
        {
            ResolveControlState();
            var angle = ReadSignedAngle();

            if (_isGrabbed)
            {
                // Soft clamp if the grab pose slips past the hinge limits.
                if (angle < -maxAngleDeg || angle > maxAngleDeg)
                {
                    angle = Mathf.Clamp(angle, -maxAngleDeg, maxAngleDeg);
                    SetLeverAngle(angle);
                }
            }
            else if (!Mathf.Approximately(angle, 0f))
            {
                var next = Mathf.MoveTowards(angle, 0f, returnSpeedDegPerSec * Time.deltaTime);
                SetLeverAngle(next);
                angle = next;
            }

            var command = Mathf.Clamp(angle / maxAngleDeg, -1f, 1f);
            if (controlState != null)
            {
                controlState.BowThruster = command;
            }

            if (valueLabel != null)
            {
                if (Mathf.Abs(command) < 0.05f)
                {
                    valueLabel.text = "BOW THRUSTER\n0";
                }
                else
                {
                    var side = command < 0f ? "PORT" : "STBD";
                    valueLabel.text = $"BOW THRUSTER\n{side} {Mathf.Abs(command):0.00}";
                }
            }
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _isGrabbed = true;
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _isGrabbed = false;
                    break;
            }
        }

        private void ResolveControlState()
        {
            if (controlState == null)
            {
                controlState = ShipControlState.Instance;
            }
        }

        private float ReadSignedAngle()
        {
            var delta = Quaternion.Inverse(_restLocalRotation) * leverTransform.localRotation;
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

        private void SetLeverAngle(float angleDeg)
        {
            leverTransform.localRotation = _restLocalRotation * Quaternion.AngleAxis(angleDeg, localRotationAxis);
        }

#if UNITY_EDITOR
        public void EditorBind(ShipControlState state, Transform lever, TextMeshPro label, Grabbable grab)
        {
            controlState = state;
            leverTransform = lever;
            valueLabel = label;
            grabbable = grab;
        }
#endif
    }
}
