using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Laser pointer for sim world-space canvases: raycasts from the right
    /// controller (or camera/mouse in the editor) against SimUiButton colliders
    /// and drives hover/press/release with the index trigger or mouse button.
    /// Active while any interactive sim panel (config, instruments menu, etc.) is open.
    /// </summary>
    public class VrUiPointer : MonoBehaviour
    {
        private const float MaxDistance = 6f;

        private SimulationConfigPanel _configPanel;
        private BridgeConningDisplay _conningDisplay;
        private BridgeInstrumentsMenu _instrumentsMenu;
        private BridgeChartDisplay _chartDisplay;
        private BridgeRadarDisplay _radarDisplay;
        private BridgeAisDisplay _aisDisplay;
        private OVRCameraRig _rig;
        private LineRenderer _line;
        private SimUiButton _hovered;
        private bool _triggerHeld;

        private void Awake()
        {
            CachePanels();

            var lineGo = new GameObject("SimUiPointerLine");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.startWidth = 0.004f;
            _line.endWidth = 0.002f;
            _line.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _line.material = new Material(shader);
            }

            _line.startColor = new Color(0.4f, 0.85f, 1f, 0.9f);
            _line.endColor = new Color(0.4f, 0.85f, 1f, 0.25f);
            _line.enabled = false;
        }

        private void CachePanels()
        {
            _configPanel ??= GetComponent<SimulationConfigPanel>();
            _conningDisplay ??= GetComponent<BridgeConningDisplay>();
            _instrumentsMenu ??= GetComponent<BridgeInstrumentsMenu>();
            _chartDisplay ??= GetComponent<BridgeChartDisplay>();
            _radarDisplay ??= GetComponent<BridgeRadarDisplay>();
            _aisDisplay ??= GetComponent<BridgeAisDisplay>();
        }

        private bool AnyPanelOpen
        {
            get
            {
                CachePanels();
                return IsOpen(_configPanel) ||
                       IsOpen(_conningDisplay) ||
                       IsOpen(_instrumentsMenu) ||
                       IsOpen(_chartDisplay) ||
                       IsOpen(_radarDisplay) ||
                       IsOpen(_aisDisplay);
            }
        }

        private static bool IsOpen(SimulationConfigPanel p) => p != null && p.IsOpen;
        private static bool IsOpen(BridgeConningDisplay p) => p != null && p.IsOpen;
        private static bool IsOpen(BridgeInstrumentsMenu p) => p != null && p.IsOpen;
        private static bool IsOpen(BridgeChartDisplay p) => p != null && p.IsOpen;
        private static bool IsOpen(BridgeRadarDisplay p) => p != null && p.IsOpen;
        private static bool IsOpen(BridgeAisDisplay p) => p != null && p.IsOpen;

        private void Update()
        {
            if (!AnyPanelOpen)
            {
                SetHovered(null);
                _line.enabled = false;
                _triggerHeld = false;
                return;
            }

            if (!TryGetRay(out Ray ray))
            {
                SetHovered(null);
                _line.enabled = false;
                return;
            }

            SimUiButton target = null;
            Vector3 end = ray.origin + ray.direction * MaxDistance;
            if (Physics.Raycast(ray, out RaycastHit hit, MaxDistance))
            {
                target = hit.collider.GetComponentInParent<SimUiButton>();
                end = hit.point;
            }

            SetHovered(target);

            _line.enabled = true;
            _line.SetPosition(0, ray.origin + ray.direction * 0.05f);
            _line.SetPosition(1, end);

            bool trigger = ReadTrigger();
            if (trigger && !_triggerHeld)
            {
                _hovered?.Press();
            }
            else if (!trigger && _triggerHeld)
            {
                _hovered?.Release();
            }

            _triggerHeld = trigger;
        }

        private void SetHovered(SimUiButton button)
        {
            if (_hovered == button)
            {
                return;
            }

            if (_hovered != null)
            {
                _hovered.SetHovered(false);
            }

            _hovered = button;
            if (_hovered != null)
            {
                _hovered.SetHovered(true);
            }
        }

        private bool TryGetRay(out Ray ray)
        {
            if (_rig == null)
            {
                _rig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (_rig != null && _rig.rightControllerAnchor != null &&
                OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            {
                var anchor = _rig.rightControllerAnchor;
                ray = new Ray(anchor.position, anchor.forward);
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            var cam = Camera.main;
            if (cam != null && Mouse.current != null)
            {
                ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                return true;
            }
#endif
            var gazeCam = Camera.main;
            if (gazeCam != null)
            {
                ray = new Ray(gazeCam.transform.position, gazeCam.transform.forward);
                return true;
            }

            ray = default;
            return false;
        }

        private bool ReadTrigger()
        {
            if (OVRInput.Get(OVRInput.RawButton.RIndexTrigger))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                return true;
            }
#endif
            return false;
        }
    }
}
