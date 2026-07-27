using NavigationSim.Core;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Pelorus / visual bearing line in the main view. Toggle: keyboard V.
    /// Bearing follows look direction (yaw) relative to north.
    /// </summary>
    public sealed class VisualBearingOverlay : MonoBehaviour
    {
        private NavigationSimRunner _runner;
        private GameObject _hudRoot;
        private Canvas _canvas;
        private RectTransform _line;
        private TMP_Text _label;
        private bool _binoculars;
        private float _defaultFov = 60f;
        private bool _capturedFov;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<NavigationSimRunner>();
        }

        private void Start()
        {
            Build();
            SetVisible(false);
        }

        private void Update()
        {
            if (TogglePressed())
            {
                SetVisible(!IsOpen);
            }

            if (BinocularPressed())
            {
                _binoculars = !_binoculars;
                ApplyBinoculars();
            }

            if (!IsOpen)
            {
                return;
            }

            RefreshBearing();
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
        }

        private static bool BinocularPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                // 'B' is used by config panel — use N for binoculars instead.
            }

            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
        }

        public void Open() => SetVisible(true);

        public void Close() => SetVisible(false);

        private void SetVisible(bool open)
        {
            IsOpen = open;
            if (_canvas != null)
            {
                _canvas.enabled = open;
            }

            if (!open)
            {
                _binoculars = false;
                ApplyBinoculars();
            }
        }

        private void Build()
        {
            _hudRoot = new GameObject("VisualBearingHud", typeof(RectTransform), typeof(Canvas));
            _hudRoot.transform.SetParent(transform, false);
            _canvas = _hudRoot.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            var rt = _hudRoot.GetComponent<RectTransform>();
            rt.sizeDelta = Vector2.zero;

            var lineGo = new GameObject("BearingLine", typeof(RectTransform));
            lineGo.transform.SetParent(rt, false);
            _line = lineGo.GetComponent<RectTransform>();
            _line.anchorMin = new Vector2(0.5f, 0.5f);
            _line.anchorMax = new Vector2(0.5f, 0.5f);
            _line.pivot = new Vector2(0.5f, 0f);
            _line.sizeDelta = new Vector2(4f, 400f);
            var img = lineGo.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 0.85f, 0.2f, 0.85f);
            img.raycastTarget = false;

            var labelGo = new GameObject("BearingLabel", typeof(RectTransform));
            labelGo.transform.SetParent(rt, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = new Vector2(0f, 220f);
            labelRt.sizeDelta = new Vector2(360f, 48f);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 28f;
            _label.color = Color.white;
            _label.fontStyle = FontStyles.Bold;
            _label.raycastTarget = false;
        }

        private void RefreshBearing()
        {
            var cam = Camera.main;
            if (cam == null || _runner?.Sim == null)
            {
                return;
            }

            // Look yaw in room frame ≈ geographic if ship-forward basis aligns with north at start.
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
            {
                return;
            }

            fwd.Normalize();
            // Compass bearing from camera forward relative to world +Z as north proxy,
            // then offset by own-ship heading so "ahead" reads as HDG when looking bow-on.
            double camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            double bearing = ShipState.Normalize360(_runner.InterpPsiDeg + camYaw);
            _label.text = _binoculars
                ? $"BRG {bearing:000.0}°  BINOS×2"
                : $"BRG {bearing:000.0}°  (V)  N=binos";

            // Nearest traffic bearing hint.
            var traffic = _runner.Traffic;
            if (traffic != null && traffic.Contacts.Count > 0)
            {
                var s = _runner.Sim.State;
                double bestDelta = 999;
                string nearest = null;
                for (int i = 0; i < traffic.Contacts.Count; i++)
                {
                    var c = traffic.Contacts[i];
                    if (!c.Visible)
                    {
                        continue;
                    }

                    c.RangeBearingFrom(s.North, s.East, out _, out double brg);
                    double d = Mathf.Abs(Mathf.DeltaAngle((float)bearing, (float)brg));
                    if (d < bestDelta)
                    {
                        bestDelta = d;
                        nearest = $"{c.Name} Δ{d:0.0}°";
                    }
                }

                if (nearest != null && bestDelta < 15.0)
                {
                    _label.text += $"\n→ {nearest}";
                }
            }
        }

        private void ApplyBinoculars()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            if (!_capturedFov)
            {
                _defaultFov = cam.fieldOfView;
                _capturedFov = true;
            }

            // Desktop / single-pass: nudge FOV. On Quest stereo this is approximate.
            cam.fieldOfView = _binoculars && IsOpen ? Mathf.Min(_defaultFov, 35f) : _defaultFov;
        }
    }
}
