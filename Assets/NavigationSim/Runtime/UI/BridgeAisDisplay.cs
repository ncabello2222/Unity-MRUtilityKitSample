using System.Text;
using NavigationSim.Core;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// AIS contact list (synthetic — no radio). Toggle: keyboard I / left stick click.
    /// </summary>
    public sealed class BridgeAisDisplay : MonoBehaviour
    {
        private const float Width = 900f;
        private const float Height = 720f;
        private static readonly Vector3 Prewarm = new Vector3(0f, -1600f, 0f);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private TMP_Text _list;
        private float _timer;
        private bool _openWhenReady;
        private bool _built;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<NavigationSimRunner>();
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

            if (!IsOpen || _runner?.Sim == null)
            {
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = 0.2f;
            Refresh();
        }

        private static bool TogglePressed()
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
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

            BridgeInstrumentCanvas.PlaceInFront(_root.transform, 1.55f, 0.55f, -0.05f);
            _canvas.enabled = true;
            IsOpen = true;
            Refresh();
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

        private void Build()
        {
            _root = BridgeInstrumentCanvas.CreateCanvas("BridgeAisCanvas", transform,
                new Vector2(Width, Height), 0.001f);
            _root.transform.position = Prewarm;
            _canvas = _root.GetComponent<Canvas>();
            var rt = _root.GetComponent<RectTransform>();

            BridgeInstrumentCanvas.Image(rt, "Bg", Vector2.zero, new Vector2(Width, Height),
                BridgeInstrumentCanvas.PanelBg);
            BridgeInstrumentCanvas.Text(rt, "Title", new Vector2(20f, -14f), new Vector2(700f, 36f),
                "AIS — I / stick izq. para cerrar", 24f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.AccentAmber);
            BridgeInstrumentCanvas.Button(rt, new Vector2(Width - 70f, -10f), new Vector2(50f, 36f),
                "X", BridgeInstrumentCanvas.Danger, Close);

            _list = BridgeInstrumentCanvas.Text(rt, "List", new Vector2(24f, -60f),
                new Vector2(Width - 48f, Height - 90f), "", 20f, TextAlignmentOptions.TopLeft,
                BridgeInstrumentCanvas.TextPrimary);
            _list.textWrappingMode = TextWrappingModes.Normal;

            _built = true;
        }

        private void Refresh()
        {
            var traffic = _runner.Traffic;
            var arpa = _runner.Arpa;
            var s = _runner.Sim.State;
            var sb = new StringBuilder(1024);
            sb.AppendLine("MMSI       NAME            RNG    BRG   COG  SOG   CPA   TCPA");
            sb.AppendLine("----------------------------------------------------------------");

            if (traffic == null)
            {
                sb.AppendLine("(no traffic)");
                _list.text = sb.ToString();
                return;
            }

            for (int i = 0; i < traffic.Contacts.Count; i++)
            {
                var c = traffic.Contacts[i];
                if (!c.Visible || c.Kind == TrafficKind.Buoy)
                {
                    // Still show buoys with MMSI 0 as AtoN-like rows.
                }

                c.RangeBearingFrom(s.North, s.East, out double rangeM, out double bearing);
                double rangeNm = rangeM / 1852.0;
                string mmsi = c.Mmsi > 0 ? c.Mmsi.ToString("000000000") : "---------";
                string name = c.Name;
                if (name.Length > 14)
                {
                    name = name.Substring(0, 14);
                }

                double cpa = 0, tcpa = 0;
                if (arpa != null)
                {
                    for (int t = 0; t < arpa.Tracks.Count; t++)
                    {
                        if (arpa.Tracks[t].ContactId == c.Id)
                        {
                            cpa = arpa.Tracks[t].CpaNm;
                            tcpa = arpa.Tracks[t].TcpaMin;
                            break;
                        }
                    }
                }

                sb.AppendLine(
                    $"{mmsi}  {name,-14}  {rangeNm,5:0.00}  {bearing,5:000}  " +
                    $"{c.HeadingDeg,5:000} {c.SogKn,4:0.0}  {cpa,5:0.00} {tcpa,6:+0.0;-0.0;0.0}");
            }

            sb.AppendLine();
            sb.AppendLine("Synthetic AIS (no VHF). Same contacts as radar/chart.");
            _list.text = sb.ToString();
        }
    }
}
