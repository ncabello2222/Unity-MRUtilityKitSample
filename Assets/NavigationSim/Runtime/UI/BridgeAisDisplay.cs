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

        /// <summary>
        /// A class A transponder reports every 2–10 s. Retyping the whole table five
        /// times a second was both fictional and the most expensive thing this panel did.
        /// </summary>
        private const float RefreshSeconds = 1f;

        private static readonly Vector3 Prewarm = new Vector3(0f, -1600f, 0f);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private TMP_Text _list;
        private readonly StringBuilder _sb = new StringBuilder(1024);
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

            _timer = RefreshSeconds;
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

            // The table is the only thing that changes; keep its rebuilds off the
            // canvas holding the background and the close button.
            var readouts = BridgeInstrumentCanvas.SubCanvas(rt, "Readouts", new Vector2(Width, Height));
            _list = BridgeInstrumentCanvas.Text(readouts, "List", new Vector2(24f, -60f),
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
            _sb.Clear();
            _sb.Append("MMSI       NAME            RNG    BRG   COG  SOG   CPA   TCPA\n");
            _sb.Append("----------------------------------------------------------------\n");

            if (traffic == null)
            {
                _sb.Append("(no traffic)\n");
                _list.SetText(_sb);
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

                if (c.Mmsi > 0)
                {
                    _sb.AppendFormat("{0:000000000}", c.Mmsi);
                }
                else
                {
                    _sb.Append("---------");
                }

                _sb.Append("  ");
                AppendPadded(_sb, c.Name, 14);
                _sb.AppendFormat("  {0,5:0.00}  {1,5:000}  {2,5:000} {3,4:0.0}  {4,5:0.00} {5,6:+0.0;-0.0;0.0}\n",
                    rangeNm, bearing, c.HeadingDeg, c.SogKn, cpa, tcpa);
            }

            _sb.Append("\nSynthetic AIS (no VHF). Same contacts as radar/chart.\n");
            _list.SetText(_sb);
        }

        /// <summary>Left-aligned fixed-width name without cutting a substring out of it.</summary>
        private static void AppendPadded(StringBuilder sb, string s, int width)
        {
            int count = s == null ? 0 : System.Math.Min(s.Length, width);
            for (int i = 0; i < count; i++)
            {
                sb.Append(s[i]);
            }

            for (int i = count; i < width; i++)
            {
                sb.Append(' ');
            }
        }
    }
}
