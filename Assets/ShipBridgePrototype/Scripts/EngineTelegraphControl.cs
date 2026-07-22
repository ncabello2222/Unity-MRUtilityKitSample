using System;
using TMPro;
using UnityEngine;
using Oculus.Interaction;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Engine-order telegraph lever with mechanical detents for each order.
    /// </summary>
    public class EngineTelegraphControl : MonoBehaviour
    {
        [Serializable]
        public struct Detent
        {
            public ShipControlState.TelegraphOrder order;
            public float angleDeg;
            public string label;
        }

        [SerializeField] private ShipControlState controlState;
        [SerializeField] private Transform leverTransform;
        [SerializeField] private Transform orderNeedle;
        [SerializeField] private TextMeshPro orderLabel;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private float snapSpeedDegPerSec = 180f;
        [SerializeField] private float detentCaptureDegrees = 6f;
        [SerializeField] private Vector3 localRotationAxis = Vector3.right;

        // 0° = STOP, lever perpendicular to the panel face; ±90° = Full Ahead/Astern folded toward the face.
        [SerializeField] private Detent[] detents =
        {
            new Detent { order = ShipControlState.TelegraphOrder.FullAhead, angleDeg = 90f, label = "FULL AHEAD" },
            new Detent { order = ShipControlState.TelegraphOrder.HalfAhead, angleDeg = 67.5f, label = "HALF AHEAD" },
            new Detent { order = ShipControlState.TelegraphOrder.SlowAhead, angleDeg = 45f, label = "SLOW AHEAD" },
            new Detent { order = ShipControlState.TelegraphOrder.DeadSlowAhead, angleDeg = 22.5f, label = "DEAD SLOW AHEAD" },
            new Detent { order = ShipControlState.TelegraphOrder.Stop, angleDeg = 0f, label = "STOP" },
            new Detent { order = ShipControlState.TelegraphOrder.DeadSlowAstern, angleDeg = -22.5f, label = "DEAD SLOW ASTERN" },
            new Detent { order = ShipControlState.TelegraphOrder.SlowAstern, angleDeg = -45f, label = "SLOW ASTERN" },
            new Detent { order = ShipControlState.TelegraphOrder.HalfAstern, angleDeg = -67.5f, label = "HALF ASTERN" },
            new Detent { order = ShipControlState.TelegraphOrder.FullAstern, angleDeg = -90f, label = "FULL ASTERN" },
        };

        private Quaternion _restLocalRotation;
        private bool _isGrabbed;
        private int _currentDetentIndex = 4;
        private int _lastHapticDetent = -1;

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
            ApplyDetent(_currentDetentIndex, instant: true);
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
            var angle = ReadSignedAngle();

            if (_isGrabbed)
            {
                var min = detents[detents.Length - 1].angleDeg;
                var max = detents[0].angleDeg;
                if (angle < min || angle > max)
                {
                    angle = Mathf.Clamp(angle, min, max);
                    SetLeverAngle(angle);
                }

                var nearest = FindNearestDetent(angle);
                if (nearest != _lastHapticDetent &&
                    Mathf.Abs(angle - detents[nearest].angleDeg) <= detentCaptureDegrees)
                {
                    _lastHapticDetent = nearest;
                    _currentDetentIndex = nearest;
                    PublishOrder(nearest);
                    TryHaptic();
                }
                else
                {
                    // Live preview of closest order while dragging.
                    PublishOrder(FindNearestDetent(angle));
                }
            }
            else
            {
                var target = detents[_currentDetentIndex].angleDeg;
                if (!Mathf.Approximately(angle, target))
                {
                    var next = Mathf.MoveTowards(angle, target, snapSpeedDegPerSec * Time.deltaTime);
                    SetLeverAngle(next);
                }

                PublishOrder(_currentDetentIndex);
            }

            if (orderNeedle != null)
            {
                orderNeedle.localRotation = Quaternion.AngleAxis(detents[_currentDetentIndex].angleDeg, localRotationAxis);
            }
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    _isGrabbed = true;
                    _lastHapticDetent = -1;
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _isGrabbed = false;
                    _currentDetentIndex = FindNearestDetent(ReadSignedAngle());
                    ApplyDetent(_currentDetentIndex, instant: false);
                    PublishOrder(_currentDetentIndex);
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

        private void PublishOrder(int index)
        {
            ResolveControlState();
            if (controlState == null || index < 0 || index >= detents.Length)
            {
                return;
            }

            controlState.Telegraph = detents[index].order;
            if (orderLabel != null)
            {
                orderLabel.text = detents[index].label;
            }
        }

        private void ApplyDetent(int index, bool instant)
        {
            _currentDetentIndex = Mathf.Clamp(index, 0, detents.Length - 1);
            if (instant)
            {
                SetLeverAngle(detents[_currentDetentIndex].angleDeg);
            }
        }

        private int FindNearestDetent(float angle)
        {
            var best = 0;
            var bestDist = float.MaxValue;
            for (var i = 0; i < detents.Length; i++)
            {
                var d = Mathf.Abs(angle - detents[i].angleDeg);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
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

        private void TryHaptic()
        {
            StartCoroutine(PulseHaptics());
        }

        private System.Collections.IEnumerator PulseHaptics()
        {
            OVRInput.SetControllerVibration(0.25f, 0.4f, OVRInput.Controller.RTouch);
            OVRInput.SetControllerVibration(0.25f, 0.4f, OVRInput.Controller.LTouch);
            yield return new WaitForSeconds(0.05f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        }

#if UNITY_EDITOR
        public void EditorBind(ShipControlState state, Transform lever, Transform needle, TextMeshPro label, Grabbable grab)
        {
            controlState = state;
            leverTransform = lever;
            orderNeedle = needle;
            orderLabel = label;
            grabbable = grab;
        }
#endif
    }
}
