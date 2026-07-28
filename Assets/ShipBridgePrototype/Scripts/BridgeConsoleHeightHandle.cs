using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// VR grab on the console height handle. Uses the same ISDK layout as the
    /// GrabWizard template (Grab + HandGrab on the Rigidbody host) plus distance
    /// grab, and a controller-grip fallback so height always works on Quest.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeConsoleHeightHandle : MonoBehaviour
    {
        [SerializeField] private BridgeConsoleController console;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private float metersPerHandMeter = 1f;
        [SerializeField] private float fallbackGrabRadius = 0.18f;
        [SerializeField] private bool enableControllerFallback = true;

        private bool _grabbed;
        private bool _fallbackGrab;
        private float _grabHandY;
        private float _grabHeight;
        private Transform _handle;
        private OVRCameraRig _cameraRig;

        public Grabbable Grabbable => grabbable;

        private void Awake()
        {
            if (console == null)
            {
                console = GetComponentInParent<BridgeConsoleController>();
            }

            EnsureInteractable();
        }

        private void OnEnable()
        {
            EnsureInteractable();
            BindGrabbableEvents();
        }

        private void OnDisable()
        {
            UnbindGrabbableEvents();
            _grabbed = false;
            if (console != null)
            {
                console.SetExternalUpperDrive(false);
            }
        }

        private void Update()
        {
            if (console == null)
            {
                return;
            }

            if (_grabbed)
            {
                // Prefer the live grab point; fall back to constrained Upper Y.
                if (grabbable != null && grabbable.GrabPoints != null && grabbable.GrabPoints.Count > 0)
                {
                    var handY = grabbable.GrabPoints[0].position.y;
                    var delta = (handY - _grabHandY) * metersPerHandMeter;
                    console.SetTargetBaseHeight(_grabHeight + delta);
                }
                else if (console.ConsoleUpper != null)
                {
                    console.SyncHeightFromUpperPosition();
                }

                return;
            }

            if (enableControllerFallback)
            {
                UpdateControllerFallback();
            }
        }

        /// <summary>
        /// Wires Rigidbody + Grabbable + controller/hand/distance grab on the
        /// HeightHandle host (same GameObject), matching the ISDK GrabWizard layout.
        /// </summary>
        public void EnsureInteractable()
        {
            if (console == null)
            {
                console = GetComponentInParent<BridgeConsoleController>();
            }

            if (console != null)
            {
                console.EnsureHierarchy();
                console.RefreshHeightHandlePose();
            }

            _handle = console != null && console.HeightHandle != null
                ? console.HeightHandle
                : transform;

            var handleGo = _handle.gameObject;

            // Tear down broken child hosts from the previous wiring attempt.
            DestroyOrphanInteractableHosts(handleGo);

            var box = handleGo.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = handleGo.AddComponent<BoxCollider>();
            }

            // World-ish grab volume, compensated for non-uniform root width scale.
            var lossy = handleGo.transform.lossyScale;
            box.center = Vector3.zero;
            box.size = new Vector3(
                SafeDiv(0.28f, Mathf.Abs(lossy.x)),
                SafeDiv(0.14f, Mathf.Abs(lossy.y)),
                SafeDiv(0.22f, Mathf.Abs(lossy.z)));
            box.isTrigger = false;

            var body = handleGo.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = handleGo.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            grabbable = handleGo.GetComponent<Grabbable>();
            if (grabbable == null)
            {
                grabbable = handleGo.AddComponent<Grabbable>();
            }

            // Drive height ourselves from grab Y — keep the handle from flying away.
            var noop = handleGo.GetComponent<BridgeConsoleNoOpTransformer>();
            if (noop == null)
            {
                noop = handleGo.AddComponent<BridgeConsoleNoOpTransformer>();
            }

            grabbable.InjectOptionalTargetTransform(handleGo.transform);
            grabbable.InjectOptionalRigidbody(body);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.InjectOptionalOneGrabTransformer(noop);
            grabbable.MaxGrabPoints = 1;

            // Same host as Rigidbody — required so GetComponentsInChildren<Collider>() works.
            EnsureComponentInteractables(handleGo, body, grabbable);
            BindGrabbableEvents();
        }

        private void EnsureComponentInteractables(GameObject host, Rigidbody body, Grabbable grab)
        {
            var handGrab = host.GetComponent<HandGrabInteractable>();
            if (handGrab == null)
            {
                handGrab = host.AddComponent<HandGrabInteractable>();
            }

            handGrab.InjectAllHandGrabInteractable(
                GrabTypeFlags.All,
                body,
                GrabbingRule.DefaultPinchRule,
                GrabbingRule.DefaultPalmRule);
            handGrab.InjectOptionalPointableElement(grab);

            var move = host.GetComponent<MoveFromTargetProvider>();
            if (move == null)
            {
                move = host.AddComponent<MoveFromTargetProvider>();
            }

            handGrab.InjectOptionalMovementProvider(move);

            var grabInteractable = host.GetComponent<GrabInteractable>();
            if (grabInteractable == null)
            {
                grabInteractable = host.AddComponent<GrabInteractable>();
            }

            grabInteractable.InjectAllGrabInteractable(body);
            grabInteractable.InjectOptionalPointableElement(grab);

            // Distance grab: console sits on a wall; proximity-only is often unreachable.
            var distanceHand = host.GetComponent<DistanceHandGrabInteractable>();
            if (distanceHand == null)
            {
                distanceHand = host.AddComponent<DistanceHandGrabInteractable>();
            }

            distanceHand.InjectAllDistanceHandGrabInteractable(
                GrabTypeFlags.All,
                body,
                GrabbingRule.DefaultPinchRule,
                GrabbingRule.DefaultPalmRule);
            distanceHand.InjectOptionalPointableElement(grab);
            distanceHand.InjectOptionalMovementProvider(move);

            var distanceGrab = host.GetComponent<DistanceGrabInteractable>();
            if (distanceGrab == null)
            {
                distanceGrab = host.AddComponent<DistanceGrabInteractable>();
            }

            distanceGrab.InjectAllGrabInteractable(body);
            distanceGrab.InjectOptionalPointableElement(grab);
            distanceGrab.InjectOptionalMovementProvider(move);
        }

        private static void DestroyOrphanInteractableHosts(GameObject handleGo)
        {
            for (var i = handleGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = handleGo.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (child.name == "GrabInteractable" ||
                    child.name == "HandGrabInteractable" ||
                    child.name == "ISDK_HandGrabInteraction")
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void BindGrabbableEvents()
        {
            if (grabbable == null)
            {
                return;
            }

            grabbable.WhenPointerEventRaised -= OnPointerEvent;
            grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        private void UnbindGrabbableEvents()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= OnPointerEvent;
            }
        }

        private void OnPointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    BeginGrab(evt.Pose.position.y);
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    EndGrab();
                    break;
            }
        }

        private void BeginGrab(float handY, bool fromFallback = false)
        {
            _grabbed = true;
            _fallbackGrab = fromFallback;
            _grabHandY = handY;
            _grabHeight = console != null ? console.TargetBaseHeight : 0f;
            if (console != null)
            {
                console.SetExternalUpperDrive(true);
            }

            Debug.Log(
                $"[BridgeConsole] Height grab start ({(fromFallback ? "controller grip" : "ISDK")}) " +
                $"y={handY:F2} height={_grabHeight:F2}m",
                this);
        }

        private void EndGrab()
        {
            _grabbed = false;
            _fallbackGrab = false;
            if (console != null)
            {
                console.SetExternalUpperDrive(false);
            }
        }

        private void UpdateControllerFallback()
        {
            if (_handle == null || console == null)
            {
                return;
            }

            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (_cameraRig == null)
            {
                return;
            }

            // If ISDK already owns the grab, do not fight it with the fallback.
            if (_grabbed && !_fallbackGrab)
            {
                return;
            }

            if (TryFallbackAnchor(_cameraRig.rightControllerAnchor, OVRInput.Controller.RTouch))
            {
                return;
            }

            TryFallbackAnchor(_cameraRig.leftControllerAnchor, OVRInput.Controller.LTouch);
        }

        private bool TryFallbackAnchor(Transform anchor, OVRInput.Controller controller)
        {
            if (anchor == null)
            {
                return false;
            }

            var gripping = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > 0.7f ||
                           OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller);
            var near = Vector3.Distance(anchor.position, _handle.position) <= fallbackGrabRadius;

            if (gripping && near)
            {
                if (!_grabbed)
                {
                    BeginGrab(anchor.position.y, fromFallback: true);
                }

                if (_fallbackGrab)
                {
                    var delta = (anchor.position.y - _grabHandY) * metersPerHandMeter;
                    console.SetTargetBaseHeight(_grabHeight + delta);
                }

                return true;
            }

            if (_fallbackGrab && _grabbed && !gripping)
            {
                EndGrab();
            }

            return false;
        }

        private static float SafeDiv(float value, float divisor)
        {
            return value / Mathf.Max(0.01f, divisor);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var t = _handle != null ? _handle : (console != null ? console.HeightHandle : null);
            if (t == null)
            {
                return;
            }

            var box = t.GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            var prev = Gizmos.matrix;
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = prev;
        }
#endif
    }

    /// <summary>
    /// Keeps the Interaction SDK happy without moving the handle mesh — height
    /// is applied by <see cref="BridgeConsoleHeightHandle"/> instead.
    /// </summary>
    public class BridgeConsoleNoOpTransformer : MonoBehaviour, ITransformer
    {
        public void Initialize(IGrabbable grabbable) { }
        public void BeginTransform() { }
        public void UpdateTransform() { }
        public void EndTransform() { }
    }
}
