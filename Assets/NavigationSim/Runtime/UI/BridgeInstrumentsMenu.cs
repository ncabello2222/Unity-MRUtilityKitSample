using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Quest-friendly launcher: left controller X (or keyboard X) opens a world-space
    /// menu to choose which bridge instrument panel to show.
    /// </summary>
    public sealed class BridgeInstrumentsMenu : MonoBehaviour
    {
        private const float Width = 720f;
        private const float Height = 780f;
        private static readonly Vector3 Prewarm = new Vector3(0f, -1800f, 0f);

        private GameObject _root;
        private Canvas _canvas;
        private bool _built;
        private bool _openWhenReady;

        private BridgeConningDisplay _conning;
        private BridgeChartDisplay _chart;
        private BridgeRadarDisplay _radar;
        private BridgeAisDisplay _ais;
        private VisualBearingOverlay _bearing;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            CachePanels();
        }

        private void Start()
        {
            Build();
            _canvas.enabled = false;
            if (_openWhenReady)
            {
                _openWhenReady = false;
                Open();
            }
        }

        private void Update()
        {
            if (!TogglePressed())
            {
                return;
            }

            if (IsOpen || _openWhenReady)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private static bool TogglePressed()
        {
            // Quest Touch: left X = Button.One on LTouch (Y = Button.Two).
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
        }

        public void Open()
        {
            if (!_built)
            {
                _openWhenReady = true;
                return;
            }

            CachePanels();
            BridgeInstrumentCanvas.PlaceInFront(_root.transform, 1.45f, 0f, 0.05f);
            _canvas.enabled = true;
            IsOpen = true;
        }

        public void Close()
        {
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            _openWhenReady = false;
            IsOpen = false;
        }

        private void CachePanels()
        {
            _conning ??= GetComponent<BridgeConningDisplay>();
            _chart ??= GetComponent<BridgeChartDisplay>();
            _radar ??= GetComponent<BridgeRadarDisplay>();
            _ais ??= GetComponent<BridgeAisDisplay>();
            _bearing ??= GetComponent<VisualBearingOverlay>();
        }

        private void Build()
        {
            _root = BridgeInstrumentCanvas.CreateCanvas("BridgeInstrumentsMenu", transform,
                new Vector2(Width, Height), 0.0011f);
            _root.transform.position = Prewarm;
            _canvas = _root.GetComponent<Canvas>();
            var rt = _root.GetComponent<RectTransform>();

            BridgeInstrumentCanvas.Image(rt, "Bg", Vector2.zero, new Vector2(Width, Height),
                BridgeInstrumentCanvas.PanelBg);
            BridgeInstrumentCanvas.Text(rt, "Title", new Vector2(24f, -18f), new Vector2(560f, 40f),
                "INSTRUMENTOS DE PUENTE", 26f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.AccentAmber);
            BridgeInstrumentCanvas.Text(rt, "Hint", new Vector2(24f, -58f), new Vector2(670f, 32f),
                "Mando izq. X abre / cierra · Y = conning · der. B = config", 18f,
                TextAlignmentOptions.Left, BridgeInstrumentCanvas.TextMuted);

            BridgeInstrumentCanvas.Button(rt, new Vector2(Width - 70f, -14f), new Vector2(50f, 40f),
                "X", BridgeInstrumentCanvas.Danger, Close);

            float y = -110f;
            float btnH = 72f;
            float gap = 14f;
            float btnW = Width - 80f;

            AddLaunch(rt, new Vector2(40f, y), new Vector2(btnW, btnH), "CONNING",
                "Rumbo, motor, profundidad, GPS",
                BridgeInstrumentCanvas.AccentCyan, OpenConning);
            y -= btnH + gap;

            AddLaunch(rt, new Vector2(40f, y), new Vector2(btnW, btnH), "CHART-LITE",
                "Carta north-up · own-ship + tráfico",
                BridgeInstrumentCanvas.AccentGreen, OpenChart);
            y -= btnH + gap;

            AddLaunch(rt, new Vector2(40f, y), new Vector2(btnW, btnH), "RADAR",
                "PPI · EBL/VRM · ARPA · guard zone",
                new Color(0.2f, 0.55f, 0.35f, 1f), OpenRadar);
            y -= btnH + gap;

            AddLaunch(rt, new Vector2(40f, y), new Vector2(btnW, btnH), "AIS",
                "Lista de contactos (sintético)",
                BridgeInstrumentCanvas.AccentAmber, OpenAis);
            y -= btnH + gap;

            AddLaunch(rt, new Vector2(40f, y), new Vector2(btnW, btnH), "DEMORA VISUAL",
                "Pelorus / bearing en la vista",
                new Color(0.55f, 0.45f, 0.2f, 1f), OpenBearing);
            y -= btnH + gap + 8f;

            BridgeInstrumentCanvas.Button(rt, new Vector2(40f, y), new Vector2(btnW * 0.48f, 56f),
                "CERRAR TODO", BridgeInstrumentCanvas.TextMuted, CloseAllInstruments);
            BridgeInstrumentCanvas.Button(rt, new Vector2(40f + btnW * 0.52f, y),
                new Vector2(btnW * 0.48f, 56f),
                "CERRAR MENÚ", BridgeInstrumentCanvas.Danger, Close);

            _built = true;
        }

        private static void AddLaunch(RectTransform parent, Vector2 topLeft, Vector2 size,
            string title, string subtitle, Color color, System.Action onClick)
        {
            var btn = BridgeInstrumentCanvas.Button(parent, topLeft, size, "", color, onClick);
            // Replace empty label with two-line title/subtitle.
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = $"{title}\n<size=70%>{subtitle}</size>";
                label.textWrappingMode = TextWrappingModes.Normal;
                label.fontSize = 26f;
                label.alignment = TextAlignmentOptions.Center;
            }
        }

        private void OpenConning()
        {
            CachePanels();
            _conning?.Open();
            Close();
        }

        private void OpenChart()
        {
            CachePanels();
            _chart?.Open();
            Close();
        }

        private void OpenRadar()
        {
            CachePanels();
            _radar?.Open();
            Close();
        }

        private void OpenAis()
        {
            CachePanels();
            _ais?.Open();
            Close();
        }

        private void OpenBearing()
        {
            CachePanels();
            _bearing?.Open();
            Close();
        }

        private void CloseAllInstruments()
        {
            CachePanels();
            _conning?.Close();
            _chart?.Close();
            _radar?.Close();
            _ais?.Close();
            _bearing?.Close();
            Close();
        }
    }
}
