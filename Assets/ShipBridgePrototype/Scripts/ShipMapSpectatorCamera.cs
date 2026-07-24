using NavigationSim.UnityLayer;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Picture-in-picture top-down "map" view rendered from the geographic frame.
    /// The room/hull never rotate in world space (the exterior moves inversely),
    /// which reads backwards from any external viewpoint. This camera counter-
    /// rotates with the ship heading so the sea/terrain keep a fixed screen
    /// orientation while the vessel appears to translate and turn like on a chart.
    /// </summary>
    public class ShipMapSpectatorCamera : MonoBehaviour
    {
        public static ShipMapSpectatorCamera Instance { get; private set; }

        [SerializeField] private float height = 250f;
        [SerializeField] private float orthographicSize = 140f;
        [SerializeField] private float minOrthographicSize = 40f;
        [SerializeField] private float maxOrthographicSize = 1500f;
        [SerializeField] private Rect viewport = new Rect(0.56f, 0.5f, 0.43f, 0.49f);

        private Camera _cam;
        private ExteriorWorldMotion _motion;

        public static void SetVisible(bool visible)
        {
            if (Instance == null)
            {
                if (!visible)
                {
                    return;
                }

                var go = new GameObject("ShipMapSpectatorCamera");
                go.AddComponent<ShipMapSpectatorCamera>();
            }

            Instance.gameObject.SetActive(visible);
        }

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                _cam = gameObject.AddComponent<Camera>();
            }

            _cam.orthographic = true;
            _cam.orthographicSize = orthographicSize;
            _cam.nearClipPlane = 1f;
            _cam.farClipPlane = 2000f;
            _cam.depth = 50f; // render on top of the main view
            _cam.rect = viewport;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.1f, 0.16f, 1f);
            _cam.stereoTargetEye = StereoTargetEyeMask.None; // desktop overlay only
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void ZoomIn()
        {
            orthographicSize = Mathf.Clamp(
                orthographicSize * 0.75f, minOrthographicSize, maxOrthographicSize);
        }

        public void ZoomOut()
        {
            orthographicSize = Mathf.Clamp(
                orthographicSize * 1.3333f, minOrthographicSize, maxOrthographicSize);
        }

        private const float ButtonSize = 40f;
        private const float ButtonPad = 8f;

        private void GetButtonRects(out Rect plus, out Rect minus, out Rect label)
        {
            // Camera rect uses a bottom-left origin; GUI/screen-space here uses
            // top-left. Pin the buttons to the PiP's top-left corner.
            var r = _cam.rect;
            var x = r.x * Screen.width;
            var yTop = (1f - r.y - r.height) * Screen.height;
            plus = new Rect(x + ButtonPad, yTop + ButtonPad, ButtonSize, ButtonSize);
            minus = new Rect(plus.xMax + 6f, yTop + ButtonPad, ButtonSize, ButtonSize);
            label = new Rect(minus.xMax + 6f, yTop + ButtonPad, 160f, ButtonSize);
        }

        private void Update()
        {
            if (_cam == null || !_cam.enabled)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            // The project runs Input System only, so IMGUI does not receive input;
            // clicks/keys are resolved here and OnGUI just draws the visuals.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.numpadPlusKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame)
                {
                    ZoomIn();
                }

                if (kb.numpadMinusKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame)
                {
                    ZoomOut();
                }
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var mp = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                GetButtonRects(out var plus, out var minus, out _);
                var guiPos = new Vector2(mp.x, Screen.height - mp.y);
                if (plus.Contains(guiPos))
                {
                    ZoomIn();
                }
                else if (minus.Contains(guiPos))
                {
                    ZoomOut();
                }
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                var vp = new Vector2(mp.x / Screen.width, mp.y / Screen.height);
                if (_cam.rect.Contains(vp))
                {
                    if (scroll > 0f)
                    {
                        ZoomIn();
                    }
                    else
                    {
                        ZoomOut();
                    }
                }
            }
#endif
        }

        private void OnGUI()
        {
            if (_cam == null || !_cam.enabled)
            {
                return;
            }

            GetButtonRects(out var plus, out var minus, out var label);

            // Drawn as boxes with the button style: clicks are handled in Update via
            // the Input System, so interactive GUI.Button would double-trigger when
            // IMGUI input happens to work (e.g. in the editor).
            GUI.Box(plus, "+", GUI.skin.button);
            GUI.Box(minus, "-", GUI.skin.button);
            GUI.Label(label, $"±{orthographicSize:0} m  (rueda/±)");
        }

        private void LateUpdate()
        {
            // Primary source: the bridge reference frame (unique, static after
            // generation). ExteriorWorldMotion is only a fallback — the scene can
            // contain stale instances without an initial pose.
            Vector3 pivotPos;
            Vector3 north0;
            var frame = BridgeReferenceFrame.Instance;
            if (frame != null && frame.Pivot != null)
            {
                pivotPos = frame.Pivot.position;
                north0 = frame.Forward;
            }
            else
            {
                if (_motion == null || !_motion.HasInitialPose)
                {
                    _motion = null;
                    foreach (var m in FindObjectsByType<ExteriorWorldMotion>(FindObjectsSortMode.None))
                    {
                        if (m.HasInitialPose)
                        {
                            _motion = m;
                            break;
                        }
                    }
                }

                if (_motion == null)
                {
                    return;
                }

                pivotPos = _motion.ShipPivot != null
                    ? _motion.ShipPivot.position
                    : _motion.InitialPivotPosition;
                north0 = _motion.ShipForwardBasis * Vector3.forward;
            }

            // Screen-up = geographic North (initial bow) carried through the current
            // heading, so the map stays north-up while the room/hull appear to turn.
            var runner = NavigationSimRunner.Instance;
            var psiDeg = runner != null ? (float)runner.InterpPsiDeg : 0f;
            var upWorld = Quaternion.AngleAxis(-psiDeg, Vector3.up) * north0;

            _cam.orthographicSize = orthographicSize;
            transform.SetPositionAndRotation(
                pivotPos + Vector3.up * height,
                Quaternion.LookRotation(Vector3.down, upWorld));
        }
    }
}
