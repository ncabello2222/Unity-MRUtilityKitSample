using System;
using System.Collections;
using NavigationSim.Core;
using ShipBridgePrototype;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// World-space OpenBridge-inspired conning display. Separate from the
    /// instructor <see cref="SimulationConfigPanel"/> (toggle B). Open/close with
    /// Y on the left controller, or the Y key in the editor.
    /// </summary>
    public class BridgeConningDisplay : MonoBehaviour
    {
        private const float CanvasWidth = 1480f;
        private const float CanvasHeight = 900f;
        private const float RefreshInterval = 0.12f;

        /// <summary>Where the panel waits while it is built, well clear of the room.</summary>
        private static readonly Vector3 PrewarmPosition = new Vector3(0f, -1000f, 0f);

        private const float RotCautionDpm = 30f;
        private const float RotAlarmDpm = 60f;
        private const float DepthCautionM = 15f;
        private const float DepthAlarmM = 8f;
        private const float LoadCaution = 0.9f;
        private const float LoadAlarm = 1.05f;

        // OpenBridge-lite tokens.
        private static readonly Color PanelBg = new Color(0.14f, 0.16f, 0.18f, 0.97f);
        private static readonly Color CardBg = new Color(0.18f, 0.20f, 0.22f, 1f);
        private static readonly Color CardBorder = new Color(0.32f, 0.35f, 0.38f, 1f);
        private static readonly Color TextPrimary = new Color(0.93f, 0.95f, 0.97f, 1f);
        private static readonly Color TextMuted = new Color(0.62f, 0.66f, 0.70f, 1f);
        private static readonly Color AccentAmber = new Color(0.95f, 0.62f, 0.18f, 1f);
        private static readonly Color AccentCyan = new Color(0.35f, 0.78f, 0.92f, 1f);
        private static readonly Color AccentGreen = new Color(0.35f, 0.78f, 0.45f, 1f);
        private static readonly Color CautionColor = new Color(0.95f, 0.72f, 0.20f, 1f);
        private static readonly Color AlarmColor = new Color(0.88f, 0.28f, 0.22f, 1f);
        private static readonly Color NormalValue = new Color(0.96f, 0.97f, 0.98f, 1f);
        private static readonly Color CompassFace = new Color(0.12f, 0.14f, 0.16f, 1f);
        private static readonly Color CompassRing = new Color(0.55f, 0.58f, 0.62f, 1f);
        private static readonly Color HullColor = new Color(0.42f, 0.46f, 0.50f, 1f);
        private static readonly Color DangerColor = new Color(0.75f, 0.22f, 0.18f, 1f);

        private NavigationSimRunner _runner;
        private GameObject _canvasRoot;
        private Canvas _canvas;
        private OpenBridgeConningBinder _openBridgeBinder;
        private float _refreshTimer;
        private bool _openWhenReady;

        private TMP_Text _hdgValue;
        private TMP_Text _rotValue;
        private TMP_Text _sogValue;
        private TMP_Text _stwValue;
        private TMP_Text _cogValue;
        private TMP_Text _windValue;
        private TMP_Text _currentValue;
        private TMP_Text _statusValue;

        private TMP_Text _engineRpm;
        private TMP_Text _engineLoad;
        private TMP_Text _enginePower;
        private TMP_Text _telegraph;
        private TMP_Text _rudderActual;
        private TMP_Text _rudderOrder;
        private RectTransform _rudderActualMarker;
        private RectTransform _rudderOrderMarker;
        private Image _rudderFill;

        private RectTransform _hdgNeedle;
        private RectTransform _cogNeedle;
        private RectTransform _windNeedle;
        private RectTransform _rotArc;
        private TMP_Text _compassHdg;
        private TMP_Text _compassStw;
        private TMP_Text _compassSog;

        private TMP_Text _depthValue;
        private TMP_Text _heaveValue;
        private TMP_Text _rollValue;
        private TMP_Text _pitchValue;
        private RectTransform _rollBubble;
        private RectTransform _pitchBubble;
        private Image _depthBarFill;

        private RectTransform _thrusterPortFill;
        private RectTransform _thrusterStbdFill;
        private RectTransform _shipRudder;
        private TMP_Text _thrustLabel;
        private Image _statusStripe;

        private static Sprite _whiteSprite;
        private static Sprite _circleSprite;
        private static Sprite _ringSprite;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<NavigationSimRunner>();
        }

        private void Start()
        {
            StartCoroutine(PrewarmPanel());
        }

        private void Update()
        {
            if (TogglePressed())
            {
                if (IsOpen || _openWhenReady)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }

            if (!IsOpen || _runner == null || _runner.Sim == null)
            {
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f)
            {
                return;
            }

            _refreshTimer = RefreshInterval;
            RefreshInstruments();
        }

        private static bool TogglePressed()
        {
            // Y on left Touch controller (B stays reserved for the config panel).
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
        }

        public void Open()
        {
            if (_canvas == null)
            {
                // Still being built; it opens as soon as the prewarm finishes.
                _openWhenReady = true;
                return;
            }

            PlaceInFrontOfCamera();
            _canvas.enabled = true;
            IsOpen = true;
            _refreshTimer = RefreshInterval;

            if (_runner != null && _runner.Sim != null)
            {
                RefreshInstruments();
            }
        }

        public void Close()
        {
            // Only the Canvas goes off. Deactivating the root would re-run OnEnable
            // across the whole panel and rebuild its layout on the way back in.
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            _openWhenReady = false;
            IsOpen = false;
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            // Slightly to the left of the config panel placement so both can coexist.
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            _canvasRoot.transform.position =
                cam.transform.position + forward * 1.65f - right * 0.15f + Vector3.up * 0.05f;
            _canvasRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Building the panel costs far more than a frame, so it is built while
        /// the room scan and the bridge generation still have the user's
        /// attention, one step per frame, and parked off to the side with its
        /// Canvas switched off. Pressing Y then only has to place it and switch
        /// the Canvas back on.
        /// </summary>
        private IEnumerator PrewarmPanel()
        {
            HideSceneImport();

            ResourceRequest request = Resources.LoadAsync<GameObject>(OpenBridgeConningBinder.RuntimePrefabResource);
            yield return request;

            // The baked prefab is the import already repaired and stripped of the
            // converter's bookkeeping; the scene import is the fallback.
            var prebaked = request.asset as GameObject;
            GameObject source = prebaked != null ? prebaked : FindImportedCase();

            GameObject runtimeCase = null;
            if (source != null)
            {
                runtimeCase = CloneOpenBridgeCase(source);
                yield return null;

                _openBridgeBinder = runtimeCase.AddComponent<OpenBridgeConningBinder>();
                _openBridgeBinder.Initialize(_runner, prebaked != null);

                Debug.Log(prebaked != null
                    ? "[BridgeConningDisplay] Using the baked OpenBridge conning panel with live simulation bindings."
                    : "[BridgeConningDisplay] Using the FCU scene import with live simulation bindings. Bake a runtime panel from Tools > NavigationSim.");
            }
            else
            {
                EnsureSprites();
                BuildCanvas();
                _canvas = _canvasRoot.GetComponent<Canvas>();
                _canvasRoot.transform.position = PrewarmPosition;
            }

            yield return null;

            WarmCanvas();
            _canvas.enabled = false;

            if (_openWhenReady)
            {
                _openWhenReady = false;
                Open();
            }
        }

        private GameObject CloneOpenBridgeCase(GameObject source)
        {
            _canvasRoot = OpenBridgeConningBinder.CreateWorldCanvas("BridgeConningCanvas_OpenBridge", transform);
            _canvas = _canvasRoot.GetComponent<Canvas>();

            // Out of sight until the first Open() places it: the Canvas has to
            // stay on for these few frames so its geometry actually gets built.
            _canvasRoot.transform.position = PrewarmPosition;

            GameObject runtimeCase = Instantiate(source, _canvasRoot.transform, false);
            runtimeCase.name = "cases_conning_5.0_Runtime";
            runtimeCase.SetActive(true);
            OpenBridgeConningBinder.FitToCanvas(runtimeCase.transform as RectTransform);
            return runtimeCase;
        }

        /// <summary>
        /// Pays for the first batch build and for every TMP mesh now, so that the
        /// frame the panel opens on does not have to.
        /// </summary>
        private void WarmCanvas()
        {
            Canvas.ForceUpdateCanvases();

            // Only what will actually draw: the import leaves plenty of text
            // parked in inactive branches.
            foreach (TMP_Text text in _canvasRoot.GetComponentsInChildren<TMP_Text>(false))
            {
                text.ForceMeshUpdate();
            }

            Canvas.ForceUpdateCanvases();
        }

        /// <summary>The FCU import is a workspace, not the runtime panel.</summary>
        private static void HideSceneImport()
        {
            GameObject importedRoot = FindSceneObject("OpenBridge_FCU");
            if (importedRoot != null)
            {
                importedRoot.SetActive(false);
            }
        }

        private static GameObject FindImportedCase()
        {
            GameObject importedRoot = FindSceneObject("OpenBridge_FCU");
            if (importedRoot == null)
            {
                return null;
            }

            foreach (Transform child in importedRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "cases_conning_5.0")
                {
                    return child.gameObject;
                }
            }

            Debug.LogWarning("[BridgeConningDisplay] OpenBridge_FCU exists, but cases_conning_5.0 was not found. Using fallback UI.");
            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.name == objectName && candidate.scene.IsValid())
                {
                    return candidate;
                }
            }
            return null;
        }

        private void BuildCanvas()
        {
            _canvasRoot = new GameObject("BridgeConningCanvas");
            _canvasRoot.transform.SetParent(transform, false);

            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            var rt = _canvasRoot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            _canvasRoot.transform.localScale = Vector3.one * 0.00105f;

            CreateImage(rt, "Background", Vector2.zero, new Vector2(CanvasWidth, CanvasHeight), PanelBg);
            _statusStripe = CreateImage(rt, "StatusStripe", new Vector2(0f, 0f),
                new Vector2(CanvasWidth, 6f), AccentGreen);

            CreateText(rt, "Title", new Vector2(24f, -14f), new Vector2(900f, 40f),
                "CONNING — INSTRUMENTOS DE PUENTE (Y para cerrar)", 26f,
                TextAlignmentOptions.Left, AccentAmber);

            var closeBtn = CreateButton(rt, new Vector2(CanvasWidth - 72f, -10f),
                new Vector2(52f, 40f), "X", DangerColor, Close);
            closeBtn.name = "CloseButton";

            BuildReadoutColumn(rt, new Vector2(20f, -64f));
            BuildCompassCard(rt, new Vector2(300f, -64f));
            BuildShipOverview(rt, new Vector2(820f, -64f));
            BuildEngineCard(rt, new Vector2(1160f, -64f));
            BuildBottomStrip(rt, new Vector2(20f, -640f));
        }

        private void BuildReadoutColumn(RectTransform parent, Vector2 origin)
        {
            var card = CreateCard(parent, "Readouts", origin, new Vector2(260f, 560f));
            CreateText(card, "Hdr", new Vector2(16f, -12f), new Vector2(220f, 28f),
                "NAV", 18f, TextAlignmentOptions.Left, TextMuted);

            _hdgValue = CreateReadout(card, "HDG", "°", new Vector2(16f, -48f), 42f, out _);
            _rotValue = CreateReadout(card, "ROT", "°/min", new Vector2(16f, -130f), 34f, out _);
            _sogValue = CreateReadout(card, "SOG", "kn", new Vector2(16f, -210f), 30f, out _);
            _stwValue = CreateReadout(card, "STW", "kn", new Vector2(16f, -280f), 30f, out _);
            _cogValue = CreateReadout(card, "COG", "°", new Vector2(16f, -350f), 30f, out _);
            _windValue = CreateReadout(card, "WIND", "", new Vector2(16f, -420f), 22f, out _);
            _currentValue = CreateReadout(card, "CURRENT", "", new Vector2(16f, -480f), 22f, out _);
        }

        private void BuildCompassCard(RectTransform parent, Vector2 origin)
        {
            var card = CreateCard(parent, "CompassCard", origin, new Vector2(500f, 560f));
            CreateText(card, "Hdr", new Vector2(16f, -12f), new Vector2(300f, 28f),
                "CONNING COMPASS", 18f, TextAlignmentOptions.Left, TextMuted);

            const float faceSize = 400f;
            var facePos = new Vector2(50f, -70f);

            var face = CreateImage(card, "Face", facePos, new Vector2(faceSize, faceSize), CompassFace);
            face.sprite = _circleSprite;
            face.type = Image.Type.Simple;
            face.preserveAspect = true;

            var ring = CreateImage(card, "Ring", facePos, new Vector2(faceSize, faceSize), CompassRing);
            ring.sprite = _ringSprite;
            ring.type = Image.Type.Simple;
            ring.preserveAspect = true;

            // Cardinal labels.
            float cx = facePos.x + faceSize * 0.5f;
            float cy = facePos.y - faceSize * 0.5f;
            CreateText(card, "N", new Vector2(cx - 14f, facePos.y - 8f), new Vector2(28f, 28f),
                "N", 20f, TextAlignmentOptions.Center, AccentAmber);
            CreateText(card, "E", new Vector2(facePos.x + faceSize - 36f, cy - 14f), new Vector2(28f, 28f),
                "E", 18f, TextAlignmentOptions.Center, TextMuted);
            CreateText(card, "S", new Vector2(cx - 14f, facePos.y - faceSize + 8f), new Vector2(28f, 28f),
                "S", 18f, TextAlignmentOptions.Center, TextMuted);
            CreateText(card, "W", new Vector2(facePos.x + 8f, cy - 14f), new Vector2(28f, 28f),
                "W", 18f, TextAlignmentOptions.Center, TextMuted);

            // ROT arc (radial fill image, rotated so 0 is at top).
            _rotArc = CreateImage(card, "RotArc",
                new Vector2(facePos.x + 30f, facePos.y - 30f),
                new Vector2(faceSize - 60f, faceSize - 60f),
                new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.55f)).rectTransform;
            var rotImg = _rotArc.GetComponent<Image>();
            rotImg.sprite = _circleSprite;
            rotImg.type = Image.Type.Filled;
            rotImg.fillMethod = Image.FillMethod.Radial360;
            rotImg.fillOrigin = (int)Image.Origin360.Top;
            rotImg.fillClockwise = true;
            rotImg.fillAmount = 0f;

            // Needles pivot at compass center.
            var pivot = new Vector2(cx, cy);
            _cogNeedle = CreateNeedle(card, "CogNeedle", pivot, 150f, 5f, AccentCyan);
            _hdgNeedle = CreateNeedle(card, "HdgNeedle", pivot, 170f, 7f, AccentAmber);
            _windNeedle = CreateNeedle(card, "WindNeedle", pivot, 110f, 4f,
                new Color(0.55f, 0.85f, 1f, 0.9f));

            // Center ship mark.
            var shipMark = CreateImage(card, "ShipMark",
                new Vector2(cx - 8f, cy + 28f), new Vector2(16f, 56f), TextPrimary);
            shipMark.color = new Color(0.85f, 0.88f, 0.9f, 0.85f);

            _compassHdg = CreateText(card, "CompassHdg",
                new Vector2(cx - 90f, cy - 10f), new Vector2(180f, 48f),
                "000°", 40f, TextAlignmentOptions.Center, TextPrimary);
            _compassStw = CreateText(card, "CompassStw",
                new Vector2(16f, -490f), new Vector2(220f, 36f),
                "STW 0.0 kn", 22f, TextAlignmentOptions.Left, TextPrimary);
            _compassSog = CreateText(card, "CompassSog",
                new Vector2(260f, -490f), new Vector2(220f, 36f),
                "SOG 0.0 kn", 22f, TextAlignmentOptions.Right, TextPrimary);

            CreateText(card, "Legend", new Vector2(16f, -528f), new Vector2(460f, 24f),
                "HDG ámbar · COG cian · VIENTO claro · arco = ROT", 16f,
                TextAlignmentOptions.Left, TextMuted);
        }

        private void BuildShipOverview(RectTransform parent, Vector2 origin)
        {
            var card = CreateCard(parent, "ShipOverview", origin, new Vector2(320f, 560f));
            CreateText(card, "Hdr", new Vector2(16f, -12f), new Vector2(280f, 28f),
                "PROPULSIÓN / TIMÓN", 18f, TextAlignmentOptions.Left, TextMuted);

            // Hull silhouette (simplified bow-up).
            CreateImage(card, "Hull", new Vector2(90f, -70f), new Vector2(140f, 360f), HullColor);
            CreateImage(card, "Bow", new Vector2(105f, -55f), new Vector2(110f, 40f),
                new Color(HullColor.r * 1.1f, HullColor.g * 1.1f, HullColor.b * 1.1f, 1f));

            // Bow thruster bar (horizontal, mid-bow).
            CreateImage(card, "ThrusterTrack", new Vector2(70f, -150f), new Vector2(180f, 18f),
                new Color(0.1f, 0.12f, 0.14f, 1f));
            _thrusterPortFill = CreateImage(card, "ThrusterPort", new Vector2(70f, -150f),
                new Vector2(0f, 18f), AccentCyan).rectTransform;
            _thrusterStbdFill = CreateImage(card, "ThrusterStbd", new Vector2(160f, -150f),
                new Vector2(0f, 18f), AccentCyan).rectTransform;

            // Stern thruster placeholder track.
            CreateImage(card, "SternTrack", new Vector2(70f, -380f), new Vector2(180f, 14f),
                new Color(0.1f, 0.12f, 0.14f, 1f));

            // Rudder under stern.
            _shipRudder = CreateImage(card, "ShipRudder", new Vector2(148f, -430f),
                new Vector2(10f, 70f), AccentAmber).rectTransform;
            _shipRudder.pivot = new Vector2(0.5f, 1f);

            _thrustLabel = CreateText(card, "Thrust", new Vector2(16f, -480f), new Vector2(288f, 56f),
                "THRUST\n0 kN", 20f, TextAlignmentOptions.Center, TextPrimary);
        }

        private void BuildEngineCard(RectTransform parent, Vector2 origin)
        {
            var card = CreateCard(parent, "EngineCard", origin, new Vector2(300f, 560f));
            CreateText(card, "Hdr", new Vector2(16f, -12f), new Vector2(260f, 28f),
                "MOTOR / TIMÓN", 18f, TextAlignmentOptions.Left, TextMuted);

            _telegraph = CreateReadout(card, "TELÉGRAFO", "", new Vector2(16f, -48f), 26f, out _);
            _engineRpm = CreateReadout(card, "RPM", "", new Vector2(16f, -128f), 34f, out _);
            _engineLoad = CreateReadout(card, "CARGA", "%", new Vector2(16f, -208f), 30f, out _);
            _enginePower = CreateReadout(card, "POTENCIA", "kW", new Vector2(16f, -278f), 26f, out _);

            CreateText(card, "RudderHdr", new Vector2(16f, -350f), new Vector2(260f, 24f),
                "TIMÓN", 18f, TextAlignmentOptions.Left, TextMuted);

            // Rudder bar track: port(-) left, stbd(+) right.
            var track = CreateImage(card, "RudderTrack", new Vector2(24f, -390f),
                new Vector2(252f, 36f), new Color(0.1f, 0.12f, 0.14f, 1f));
            CreateImage(card, "RudderCenter", new Vector2(148f, -388f),
                new Vector2(4f, 40f), TextMuted);

            _rudderFill = CreateImage(card, "RudderFill", new Vector2(150f, -394f),
                new Vector2(0f, 28f), new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.45f));

            _rudderOrderMarker = CreateImage(card, "OrderMarker", new Vector2(146f, -384f),
                new Vector2(8f, 48f), AccentCyan).rectTransform;
            _rudderActualMarker = CreateImage(card, "ActualMarker", new Vector2(146f, -384f),
                new Vector2(10f, 48f), AccentAmber).rectTransform;

            _rudderOrder = CreateText(card, "RudderOrder", new Vector2(16f, -450f),
                new Vector2(130f, 40f), "ORD 0.0°", 20f, TextAlignmentOptions.Left, AccentCyan);
            _rudderActual = CreateText(card, "RudderActual", new Vector2(150f, -450f),
                new Vector2(130f, 40f), "ACT 0.0°", 20f, TextAlignmentOptions.Right, AccentAmber);

            _statusValue = CreateText(card, "Status", new Vector2(16f, -510f),
                new Vector2(268f, 36f), "NORMAL", 22f, TextAlignmentOptions.Center, AccentGreen);
        }

        private void BuildBottomStrip(RectTransform parent, Vector2 origin)
        {
            // Depth card.
            var depthCard = CreateCard(parent, "DepthCard", origin, new Vector2(360f, 240f));
            CreateText(depthCard, "Hdr", new Vector2(16f, -12f), new Vector2(200f, 28f),
                "DEPTH", 18f, TextAlignmentOptions.Left, TextMuted);
            _depthValue = CreateText(depthCard, "DepthVal", new Vector2(16f, -50f),
                new Vector2(320f, 56f), "0 m", 42f, TextAlignmentOptions.Left, TextPrimary);

            CreateImage(depthCard, "DepthTrack", new Vector2(24f, -130f),
                new Vector2(312f, 28f), new Color(0.1f, 0.12f, 0.14f, 1f));
            _depthBarFill = CreateImage(depthCard, "DepthFill", new Vector2(24f, -130f),
                new Vector2(0f, 28f), AccentCyan);

            CreateText(depthCard, "DepthHint", new Vector2(16f, -180f), new Vector2(320f, 36f),
                "Barra relativa (0-100 m). Caution bajo 15 m · Alarm bajo 8 m", 15f,
                TextAlignmentOptions.Left, TextMuted);

            // Motion card.
            var motionCard = CreateCard(parent, "MotionCard",
                new Vector2(origin.x + 380f, origin.y), new Vector2(520f, 240f));
            CreateText(motionCard, "Hdr", new Vector2(16f, -12f), new Vector2(300f, 28f),
                "PITCH / ROLL / HEAVE", 18f, TextAlignmentOptions.Left, TextMuted);

            _pitchValue = CreateText(motionCard, "Pitch", new Vector2(16f, -50f),
                new Vector2(160f, 40f), "P 0.0°", 24f, TextAlignmentOptions.Left, TextPrimary);
            _rollValue = CreateText(motionCard, "Roll", new Vector2(180f, -50f),
                new Vector2(160f, 40f), "R 0.0°", 24f, TextAlignmentOptions.Left, TextPrimary);
            _heaveValue = CreateText(motionCard, "Heave", new Vector2(340f, -50f),
                new Vector2(160f, 40f), "H 0.00 m", 24f, TextAlignmentOptions.Left, TextPrimary);

            // Roll inclinometer.
            CreateText(motionCard, "RollLbl", new Vector2(16f, -100f), new Vector2(220f, 24f),
                "ROLL", 16f, TextAlignmentOptions.Left, TextMuted);
            CreateImage(motionCard, "RollTrack", new Vector2(16f, -130f),
                new Vector2(220f, 20f), new Color(0.1f, 0.12f, 0.14f, 1f));
            CreateImage(motionCard, "RollZero", new Vector2(124f, -128f),
                new Vector2(4f, 24f), TextMuted);
            _rollBubble = CreateImage(motionCard, "RollBubble", new Vector2(116f, -126f),
                new Vector2(20f, 28f), AccentAmber).rectTransform;

            // Pitch inclinometer.
            CreateText(motionCard, "PitchLbl", new Vector2(270f, -100f), new Vector2(220f, 24f),
                "PITCH", 16f, TextAlignmentOptions.Left, TextMuted);
            CreateImage(motionCard, "PitchTrack", new Vector2(270f, -130f),
                new Vector2(220f, 20f), new Color(0.1f, 0.12f, 0.14f, 1f));
            CreateImage(motionCard, "PitchZero", new Vector2(378f, -128f),
                new Vector2(4f, 24f), TextMuted);
            _pitchBubble = CreateImage(motionCard, "PitchBubble", new Vector2(370f, -126f),
                new Vector2(20f, 28f), AccentCyan).rectTransform;

            CreateText(motionCard, "MotionHint", new Vector2(16f, -180f), new Vector2(480f, 36f),
                "Canal visual de oleaje (no altera la trayectoria MMG)", 15f,
                TextAlignmentOptions.Left, TextMuted);

            // Env / mode strip.
            var envCard = CreateCard(parent, "EnvCard",
                new Vector2(origin.x + 920f, origin.y), new Vector2(540f, 240f));
            CreateText(envCard, "Hdr", new Vector2(16f, -12f), new Vector2(300f, 28f),
                "ESTADO", 18f, TextAlignmentOptions.Left, TextMuted);
            CreateText(envCard, "Hint", new Vector2(16f, -56f), new Vector2(500f, 160f),
                "Canvas de instrumentos de puente.\nConfiguración del simulador: botón B.\nConning: botón Y (mando izquierdo).\n\nEstilo inspirado en OpenBridge Conning.",
                20f, TextAlignmentOptions.Left, TextMuted).textWrappingMode = TextWrappingModes.Normal;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Refresh
        // ─────────────────────────────────────────────────────────────────────

        private void RefreshInstruments()
        {
            if (_openBridgeBinder != null)
            {
                _openBridgeBinder.Refresh();
                return;
            }

            var s = _runner.Sim.State;
            var env = _runner.Env;
            var bridge = ShipControlState.Instance;
            double orderRudder = _runner.Sim.ResolvedRudderCommandDeg;
            double sogKn = s.SogMs * 1.94384449244;
            double stwKn = s.StwMs * 1.94384449244;
            double rpm = s.ShaftRps * 60.0;
            double loadPct = s.EngineLoad * 100.0;
            double powerKw = s.ShaftPowerW / 1000.0;

            SetText(_hdgValue, $"{s.HeadingDeg:000.0}");
            SetText(_rotValue, $"{s.RotDegPerMin:+0.0;-0.0;0.0}");
            SetText(_sogValue, $"{sogKn:0.0}");
            SetText(_stwValue, $"{stwKn:0.0}");
            SetText(_cogValue, $"{s.CogDeg:000.0}");
            SetText(_windValue, $"{env.WindSpeedMs * 1.94384449244:0.0} kn  {env.WindFromDeg:000}°");
            SetText(_currentValue, $"{env.CurrentSpeedMs * 1.94384449244:0.0} kn  {env.CurrentSetToDeg:000}°");

            SetText(_compassHdg, $"{s.HeadingDeg:000.0}°");
            SetText(_compassStw, $"STW {stwKn:0.0} kn");
            SetText(_compassSog, $"SOG {sogKn:0.0} kn");

            // Compass needles: UI 0° = up; heading clockwise from north.
            SetNeedleAngle(_hdgNeedle, (float)s.HeadingDeg);
            SetNeedleAngle(_cogNeedle, (float)s.CogDeg);
            SetNeedleAngle(_windNeedle, (float)env.WindFromDeg);

            float rotNorm = Mathf.Clamp01((float)Math.Abs(s.RotDegPerMin) / 90f);
            var rotImg = _rotArc != null ? _rotArc.GetComponent<Image>() : null;
            if (rotImg != null)
            {
                rotImg.fillAmount = rotNorm * 0.35f;
                rotImg.fillClockwise = s.RotDegPerMin >= 0.0;
                rotImg.color = SeverityColor(RotSeverity(s.RotDegPerMin),
                    new Color(AccentAmber.r, AccentAmber.g, AccentAmber.b, 0.55f));
            }

            string telegraph = bridge != null ? bridge.Telegraph.ToString() : "n/a";
            SetText(_telegraph, telegraph);
            SetText(_engineRpm, $"{rpm:0.0}");
            SetText(_engineLoad, $"{loadPct:0}");
            SetText(_enginePower, $"{powerKw:0}");

            SetText(_rudderOrder, $"ORD {orderRudder:+0.0;-0.0;0.0}°");
            SetText(_rudderActual, $"ACT {s.RudderAngleDeg:+0.0;-0.0;0.0}°");
            UpdateRudderBar((float)orderRudder, (float)s.RudderAngleDeg);

            // Depth.
            SetText(_depthValue, $"{env.WaterDepthM:0.0} m");
            if (_depthBarFill != null)
            {
                float depthFrac = Mathf.Clamp01((float)env.WaterDepthM / 100f);
                _depthBarFill.rectTransform.sizeDelta = new Vector2(312f * depthFrac, 28f);
                _depthBarFill.color = SeverityColor(DepthSeverity(env.WaterDepthM), AccentCyan);
            }

            SetText(_heaveValue, $"H {s.HeaveM:+0.00;-0.00;0.00} m");
            SetText(_rollValue, $"R {s.RollDeg:+0.0;-0.0;0.0}°");
            SetText(_pitchValue, $"P {s.PitchDeg:+0.0;-0.0;0.0}°");
            SetInclinometer(_rollBubble, 16f, 220f, (float)s.RollDeg, 25f);
            SetInclinometer(_pitchBubble, 270f, 220f, (float)s.PitchDeg, 15f);

            // Thruster overview.
            float thruster = bridge != null ? bridge.BowThruster : 0f;
            UpdateThrusterBar(thruster);
            if (_shipRudder != null)
            {
                _shipRudder.localRotation = Quaternion.Euler(0f, 0f, (float)(-s.RudderAngleDeg));
            }

            SetText(_thrustLabel, $"THRUST\n{s.PropThrustN / 1000.0:0} kN");

            ApplySeverityColors(s, env, bridge);
        }

        private void ApplySeverityColors(ShipState s, EnvironmentState env, ShipControlState bridge)
        {
            int worst = 0;
            worst = Math.Max(worst, RotSeverity(s.RotDegPerMin));
            worst = Math.Max(worst, DepthSeverity(env.WaterDepthM));
            worst = Math.Max(worst, LoadSeverity(s.EngineLoad));
            bool eStop = (bridge != null && bridge.EmergencyStop) || _runner.PanelEmergencyStop;
            if (eStop)
            {
                worst = 2;
            }

            Color statusColor = worst >= 2 ? AlarmColor : worst == 1 ? CautionColor : AccentGreen;
            string statusText = eStop ? "E-STOP" : worst >= 2 ? "ALARM" : worst == 1 ? "CAUTION" : "NORMAL";
            if (_statusValue != null)
            {
                _statusValue.text = statusText;
                _statusValue.color = statusColor;
            }

            if (_statusStripe != null)
            {
                _statusStripe.color = statusColor;
            }

            if (_rotValue != null)
            {
                _rotValue.color = SeverityColor(RotSeverity(s.RotDegPerMin), NormalValue);
            }

            if (_depthValue != null)
            {
                _depthValue.color = SeverityColor(DepthSeverity(env.WaterDepthM), NormalValue);
            }

            if (_engineLoad != null)
            {
                _engineLoad.color = SeverityColor(LoadSeverity(s.EngineLoad), NormalValue);
            }
        }

        private static int RotSeverity(double rotDpm)
        {
            double a = Math.Abs(rotDpm);
            if (a >= RotAlarmDpm)
            {
                return 2;
            }

            if (a >= RotCautionDpm)
            {
                return 1;
            }

            return 0;
        }

        private static int DepthSeverity(double depthM)
        {
            if (depthM <= DepthAlarmM)
            {
                return 2;
            }

            if (depthM <= DepthCautionM)
            {
                return 1;
            }

            return 0;
        }

        private static int LoadSeverity(double load)
        {
            if (load >= LoadAlarm)
            {
                return 2;
            }

            if (load >= LoadCaution)
            {
                return 1;
            }

            return 0;
        }

        private static Color SeverityColor(int severity, Color normal)
        {
            return severity >= 2 ? AlarmColor : severity == 1 ? CautionColor : normal;
        }

        private void UpdateRudderBar(float orderDeg, float actualDeg)
        {
            const float maxDeg = 35f;
            const float trackWidth = 252f;
            const float trackX = 24f;
            const float centerX = trackX + trackWidth * 0.5f;

            float Place(float deg) => centerX + Mathf.Clamp(deg / maxDeg, -1f, 1f) * (trackWidth * 0.5f);

            if (_rudderOrderMarker != null)
            {
                _rudderOrderMarker.anchoredPosition = new Vector2(Place(orderDeg) - 4f, -384f);
            }

            if (_rudderActualMarker != null)
            {
                _rudderActualMarker.anchoredPosition = new Vector2(Place(actualDeg) - 5f, -384f);
            }

            if (_rudderFill != null)
            {
                float frac = Mathf.Clamp(actualDeg / maxDeg, -1f, 1f);
                float w = Math.Abs(frac) * (trackWidth * 0.5f);
                if (frac >= 0f)
                {
                    _rudderFill.rectTransform.anchoredPosition = new Vector2(centerX, -394f);
                    _rudderFill.rectTransform.sizeDelta = new Vector2(w, 28f);
                }
                else
                {
                    _rudderFill.rectTransform.anchoredPosition = new Vector2(centerX - w, -394f);
                    _rudderFill.rectTransform.sizeDelta = new Vector2(w, 28f);
                }
            }
        }

        private void UpdateThrusterBar(float thruster)
        {
            const float half = 90f;
            if (_thrusterPortFill != null)
            {
                float w = thruster < 0f ? Mathf.Clamp01(-thruster) * half : 0f;
                _thrusterPortFill.sizeDelta = new Vector2(w, 18f);
                _thrusterPortFill.anchoredPosition = new Vector2(160f - w, -150f);
            }

            if (_thrusterStbdFill != null)
            {
                float w = thruster > 0f ? Mathf.Clamp01(thruster) * half : 0f;
                _thrusterStbdFill.sizeDelta = new Vector2(w, 18f);
                _thrusterStbdFill.anchoredPosition = new Vector2(160f, -150f);
            }
        }

        private static void SetInclinometer(RectTransform bubble, float trackX, float trackW,
            float valueDeg, float maxDeg)
        {
            if (bubble == null)
            {
                return;
            }

            float t = Mathf.Clamp(valueDeg / maxDeg, -1f, 1f);
            float x = trackX + trackW * 0.5f + t * (trackW * 0.5f - 12f) - 10f;
            bubble.anchoredPosition = new Vector2(x, bubble.anchoredPosition.y);
        }

        private static void SetNeedleAngle(RectTransform needle, float headingDeg)
        {
            if (needle == null)
            {
                return;
            }

            // Unity UI: positive Z rotates counter-clockwise; compass heading is clockwise.
            needle.localRotation = Quaternion.Euler(0f, 0f, -headingDeg);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Widget helpers
        // ─────────────────────────────────────────────────────────────────────

        private RectTransform CreateCard(RectTransform parent, string name, Vector2 topLeft, Vector2 size)
        {
            var border = CreateImage(parent, name + "Border", topLeft,
                size, CardBorder);
            var inner = CreateImage(border.rectTransform, name,
                new Vector2(2f, -2f), new Vector2(size.x - 4f, size.y - 4f), CardBg);
            return inner.rectTransform;
        }

        private TMP_Text CreateReadout(RectTransform parent, string label, string unit,
            Vector2 topLeft, float valueSize, out TMP_Text labelText)
        {
            labelText = CreateText(parent, label + "Lbl", topLeft, new Vector2(220f, 22f),
                string.IsNullOrEmpty(unit) ? label : $"{label} ({unit})",
                16f, TextAlignmentOptions.Left, TextMuted);
            return CreateText(parent, label + "Val",
                new Vector2(topLeft.x, topLeft.y - 24f), new Vector2(220f, valueSize + 4f),
                "—", valueSize, TextAlignmentOptions.Left, NormalValue);
        }

        private static RectTransform CreateNeedle(RectTransform parent, string name,
            Vector2 center, float length, float width, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, length);
            rt.anchoredPosition = center;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = _whiteSprite;
            img.raycastTarget = false;
            return rt;
        }

        private static Image CreateImage(RectTransform parent, string name,
            Vector2 topLeft, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = _whiteSprite;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text CreateText(RectTransform parent, string name,
            Vector2 topLeft, Vector2 size, string content, float fontSize,
            TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        private static SimUiButton CreateButton(RectTransform parent, Vector2 topLeft, Vector2 size,
            string label, Color color, Action onClick)
        {
            var img = CreateImage(parent, $"Button_{label}", topLeft, size, color);
            img.raycastTarget = true;
            CreateText(img.rectTransform, "Label", Vector2.zero, size, label, 22f,
                TextAlignmentOptions.Center, Color.white);

            var collider = img.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);
            collider.center = new Vector3(size.x * 0.5f, -size.y * 0.5f, 0f);

            var btn = img.gameObject.AddComponent<SimUiButton>();
            btn.Bind(img);
            btn.OnClick = onClick;
            return btn;
        }

        private static void EnsureSprites()
        {
            if (_whiteSprite == null)
            {
                var tex = Texture2D.whiteTexture;
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }

            if (_circleSprite == null)
            {
                _circleSprite = BuildCircleSprite(256, 0f, 1f);
            }

            if (_ringSprite == null)
            {
                _ringSprite = BuildCircleSprite(256, 0.90f, 1f);
            }
        }

        private static Sprite BuildCircleSprite(int size, float innerFrac, float outerFrac)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = (size - 1) * 0.5f;
            float outer = outerFrac * cx;
            float inner = innerFrac * cx;
            float outerSq = outer * outer;
            float innerSq = inner * inner;

            var clear = new Color(0f, 0f, 0f, 0f);
            var solid = Color.white;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cx;
                    float r2 = dx * dx + dy * dy;
                    tex.SetPixel(x, y, r2 <= outerSq && r2 >= innerSq ? solid : clear);
                }
            }

            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
