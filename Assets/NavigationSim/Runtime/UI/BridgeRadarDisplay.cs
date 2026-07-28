using System;
using System.Text;
using NavigationSim.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Synthetic marine radar PPI with EBL/VRM, ARPA list, guard zone and PIs.
    /// Toggle: keyboard R, or open from BridgeInstrumentsMenu (Quest left X).
    /// <para>
    /// The PPI is rasterised on the CPU, so how it is rasterised decides whether the
    /// headset holds frame rate. Everything here writes into one reused
    /// <see cref="Color32"/> buffer and uploads it once — no <c>GetPixels</c>,
    /// no <c>SetPixel</c>, no per-pixel readback.
    /// </para>
    /// </summary>
    public sealed class BridgeRadarDisplay : MonoBehaviour, IBridgeDockableInstrument
    {
        private const float Width = 1200f;
        private const float Height = 960f;

        /// <summary>PPI edge in panel units. This is the size the user actually sees.</summary>
        private const float PpiUi = 640f;

        /// <summary>
        /// Raster resolution, deliberately below the panel size. The plot subtends about
        /// 21° of arc at the 1.75 m viewing distance and the headset resolves roughly
        /// 20 px per degree, so ~420 px is all it can show: 512 keeps a margin while
        /// costing 36% fewer pixels than the 640 this started at.
        /// </summary>
        private const int PpiPixels = 512;

        /// <summary>
        /// Antenna period. The echo raster is the expensive half of a refresh and a real
        /// X-band scanner turns at 24 rpm, so repainting it eight times a second bought
        /// nothing but dropped frames. Sweep, heading flash and EBL are rotating quads
        /// now, so they stay smooth at frame rate regardless of this.
        /// </summary>
        private const float RefreshSeconds = 0.4f;

        private static readonly Vector3 Prewarm = new Vector3(0f, -1400f, 0f);

        private static readonly Color32 EchoShip = new Color32(102, 255, 115, 255);
        private static readonly Color32 EchoBuoy = new Color32(255, 217, 51, 255);
        private static readonly Color32 EchoLand = new Color32(140, 140, 89, 255);
        private static readonly Color32 ScreenBg = new Color32(5, 20, 10, 255);
        private static readonly Color32 RingColor = new Color32(38, 115, 64, 255);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private RawImage _ppi;
        private Texture2D _tex;

        /// <summary>Scratch frame. Reused every refresh; never reallocated.</summary>
        private Color32[] _frame;

        /// <summary>
        /// The circular screen mask, baked once. Restoring it is a 1 MB memcpy; deriving
        /// it was a quarter-million distance tests and as many texture writes.
        /// </summary>
        private Color32[] _screenMask;

        private Image _sweep;
        private Image _headingFlash;
        private Image _ebl;
        private bool _radialsVisible = true;

        private TMP_Text _dataLabel;
        private TMP_Text _arpaLabel;
        private TMP_Text _modeLabel;

        private readonly StringBuilder _modeSb = new StringBuilder(128);
        private readonly StringBuilder _dataSb = new StringBuilder(256);
        private readonly StringBuilder _arpaSb = new StringBuilder(512);

        private float _timer;
        private float _sweepDeg;
        private bool _openWhenReady;
        private bool _built;
        private bool _docked;
        private int _activePi;

        public string InstrumentId => "radar";
        public string DisplayName => "RADAR";
        public Vector2 NativeSizePx => new Vector2(Width, Height);
        public bool IsReady => _built && _root != null;
        public bool IsOpen { get; private set; }
        public bool IsDocked => _docked;
        public Transform CanvasRoot => _root != null ? _root.transform : null;

        public void SetDocked(bool docked) => _docked = docked;

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
            if (!_docked && TogglePressed())
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

            if (!IsOpen || _runner?.Sim == null || _runner.Radar == null)
            {
                return;
            }

            _sweepDeg = (_sweepDeg + Time.deltaTime * 72f) % 360f;
            AimRadials();

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
            // Quest: open from instruments menu (left X). Editor: R.
            // Right A is reserved for the physical navigation panel spawner.
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
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

            if (!_docked)
            {
                BridgeInstrumentCanvas.PlaceInFront(_root.transform, 1.75f, -0.45f, 0.02f);
            }

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
            _root = BridgeInstrumentCanvas.CreateCanvas("BridgeRadarCanvas", transform,
                new Vector2(Width, Height), 0.001f);
            _root.transform.position = Prewarm;
            _canvas = _root.GetComponent<Canvas>();
            var rt = _root.GetComponent<RectTransform>();
            var radar = _runner.Radar;

            BridgeInstrumentCanvas.Image(rt, "Bg", Vector2.zero, new Vector2(Width, Height),
                BridgeInstrumentCanvas.PanelBg);
            BridgeInstrumentCanvas.Text(rt, "Title", new Vector2(20f, -12f), new Vector2(800f, 34f),
                "RADAR — R / mando der. X para cerrar", 22f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.AccentAmber);
            BridgeInstrumentCanvas.Button(rt, new Vector2(Width - 70f, -8f), new Vector2(50f, 34f),
                "X", BridgeInstrumentCanvas.Danger, Close);

            float y = -55f;
            BridgeInstrumentCanvas.Button(rt, new Vector2(20f, y), new Vector2(80f, 34f), "PWR",
                BridgeInstrumentCanvas.AccentGreen, () => radar.PowerOn = !radar.PowerOn);
            BridgeInstrumentCanvas.Button(rt, new Vector2(110f, y), new Vector2(80f, 34f), "RNG+",
                BridgeInstrumentCanvas.AccentCyan, () => radar.IncreaseRange(), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(200f, y), new Vector2(80f, 34f), "RNG-",
                BridgeInstrumentCanvas.AccentCyan, () => radar.DecreaseRange(), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(290f, y), new Vector2(100f, 34f), "MODE",
                BridgeInstrumentCanvas.AccentCyan, () => radar.CycleOrientation());

            y = -100f;
            BridgeInstrumentCanvas.Button(rt, new Vector2(20f, y), new Vector2(70f, 34f), "EBL+",
                BridgeInstrumentCanvas.AccentAmber,
                () => radar.EblBearingDeg = ShipState.Normalize360(radar.EblBearingDeg + 1.0), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(100f, y), new Vector2(70f, 34f), "EBL-",
                BridgeInstrumentCanvas.AccentAmber,
                () => radar.EblBearingDeg = ShipState.Normalize360(radar.EblBearingDeg - 1.0), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(180f, y), new Vector2(70f, 34f), "VRM+",
                BridgeInstrumentCanvas.AccentAmber,
                () => radar.VrmNm = Math.Min(radar.RangeNm, radar.VrmNm + 0.05), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(260f, y), new Vector2(70f, 34f), "VRM-",
                BridgeInstrumentCanvas.AccentAmber,
                () => radar.VrmNm = Math.Max(0.05, radar.VrmNm - 0.05), true);

            y = -145f;
            BridgeInstrumentCanvas.Button(rt, new Vector2(20f, y), new Vector2(90f, 34f), "ARPA",
                BridgeInstrumentCanvas.AccentGreen,
                () => _runner.Arpa.Enabled = !_runner.Arpa.Enabled);
            BridgeInstrumentCanvas.Button(rt, new Vector2(120f, y), new Vector2(90f, 34f), "T/R",
                BridgeInstrumentCanvas.AccentCyan,
                () => _runner.Arpa.TrueVectors = !_runner.Arpa.TrueVectors);
            BridgeInstrumentCanvas.Button(rt, new Vector2(220f, y), new Vector2(90f, 34f), "GUARD",
                BridgeInstrumentCanvas.Danger,
                () => radar.GuardZoneOn = !radar.GuardZoneOn);
            BridgeInstrumentCanvas.Button(rt, new Vector2(320f, y), new Vector2(90f, 34f), "TRIAL",
                BridgeInstrumentCanvas.AccentAmber, () =>
                {
                    var a = _runner.Arpa;
                    a.TrialManoeuvre = !a.TrialManoeuvre;
                    if (a.TrialManoeuvre && _runner.Sim != null)
                    {
                        a.TrialCourseDeg = _runner.Sim.State.CogDeg;
                        a.TrialSpeedKn = Math.Max(1.0, _runner.Sim.State.SogMs * 1.94384449244);
                    }
                });

            y = -190f;
            BridgeInstrumentCanvas.Button(rt, new Vector2(20f, y), new Vector2(70f, 34f), "PI",
                BridgeInstrumentCanvas.AccentCyan, () =>
                {
                    _activePi = (_activePi + 1) % 10;
                    var pi = radar.ParallelIndexes[_activePi];
                    if (!pi.Active)
                    {
                        radar.SetParallelIndex(_activePi, true, radar.VrmNm, radar.EblBearingDeg);
                    }
                });
            BridgeInstrumentCanvas.Button(rt, new Vector2(100f, y), new Vector2(90f, 34f), "PI CLR",
                BridgeInstrumentCanvas.TextMuted, () =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        radar.SetParallelIndex(i, false, 0, 0);
                    }
                });

            // Readouts live on their own canvas: they are retyped every refresh and
            // would otherwise dirty the batch holding the background and all the buttons.
            var readouts = BridgeInstrumentCanvas.SubCanvas(rt, "Readouts", new Vector2(Width, Height));

            _modeLabel = BridgeInstrumentCanvas.Text(readouts, "Mode", new Vector2(430f, -55f),
                new Vector2(400f, 34f), "", 20f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextPrimary);

            _tex = new Texture2D(PpiPixels, PpiPixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _frame = new Color32[PpiPixels * PpiPixels];
            _screenMask = new Color32[PpiPixels * PpiPixels];
            BakeScreenMask();

            var plotGo = new GameObject("PPI", typeof(RectTransform));
            plotGo.transform.SetParent(rt, false);
            var plotRt = plotGo.GetComponent<RectTransform>();
            plotRt.anchorMin = new Vector2(0f, 1f);
            plotRt.anchorMax = new Vector2(0f, 1f);
            plotRt.pivot = new Vector2(0f, 1f);
            plotRt.anchoredPosition = new Vector2(40f, -240f);
            plotRt.sizeDelta = new Vector2(PpiUi, PpiUi);
            _ppi = plotGo.AddComponent<RawImage>();
            _ppi.texture = _tex;
            _ppi.raycastTarget = false;

            // Sweep, heading flash and EBL are quads over the plot. They move every frame,
            // which is exactly what a rasterised line cannot afford to do.
            float radialLen = PpiUi * 0.48f;
            _sweep = BridgeInstrumentCanvas.Radial(plotRt, "Sweep", radialLen, 3f,
                new Color(0.3f, 0.9f, 0.4f, 0.35f));
            _headingFlash = BridgeInstrumentCanvas.Radial(plotRt, "HeadingFlash", radialLen, 2f,
                new Color(0.9f, 0.9f, 0.5f, 0.8f));
            _ebl = BridgeInstrumentCanvas.Radial(plotRt, "Ebl", radialLen, 2f,
                BridgeInstrumentCanvas.AccentAmber);

            _dataLabel = BridgeInstrumentCanvas.Text(readouts, "Data", new Vector2(700f, -240f),
                new Vector2(460f, 220f), "", 20f, TextAlignmentOptions.TopLeft,
                BridgeInstrumentCanvas.TextPrimary);
            _dataLabel.textWrappingMode = TextWrappingModes.Normal;

            _arpaLabel = BridgeInstrumentCanvas.Text(readouts, "Arpa", new Vector2(700f, -480f),
                new Vector2(460f, 440f), "", 17f, TextAlignmentOptions.TopLeft,
                BridgeInstrumentCanvas.TextMuted);
            _arpaLabel.textWrappingMode = TextWrappingModes.Normal;

            _built = true;
        }

        /// <summary>
        /// The transparent surround and the dark screen disc, computed once. Range rings
        /// are not baked in: they must vanish when the set is switched off, and four
        /// circles is a few thousand writes — nothing next to the mask.
        /// </summary>
        private void BakeScreenMask()
        {
            const int c = PpiPixels / 2;
            float radius = PpiPixels * 0.48f;
            float r2 = radius * radius;

            Array.Clear(_screenMask, 0, _screenMask.Length);
            for (int y = 0; y < PpiPixels; y++)
            {
                int dy = y - c;
                int row = y * PpiPixels;
                float rowTerm = r2 - dy * dy;
                if (rowTerm < 0f)
                {
                    continue;
                }

                int halfSpan = (int)Mathf.Sqrt(rowTerm);
                int x0 = Mathf.Max(0, c - halfSpan);
                int x1 = Mathf.Min(PpiPixels - 1, c + halfSpan);
                for (int x = x0; x <= x1; x++)
                {
                    _screenMask[row + x] = ScreenBg;
                }
            }
        }

        /// <summary>Aims the three rotating quads. Runs every frame; costs three quaternions.</summary>
        private void AimRadials()
        {
            var radar = _runner.Radar;
            var s = _runner.Sim.State;

            if (_radialsVisible != radar.PowerOn)
            {
                _radialsVisible = radar.PowerOn;
                _sweep.enabled = _radialsVisible;
                _headingFlash.enabled = _radialsVisible;
                _ebl.enabled = _radialsVisible;
            }

            if (!_radialsVisible)
            {
                return;
            }

            BridgeInstrumentCanvas.PointRadial(_sweep, _sweepDeg);
            BridgeInstrumentCanvas.PointRadial(_headingFlash,
                radar.BearingToScreenDeg(s.HeadingDeg, s.HeadingDeg, s.CogDeg));
            BridgeInstrumentCanvas.PointRadial(_ebl,
                radar.BearingToScreenDeg(radar.EblBearingDeg, s.HeadingDeg, s.CogDeg));
        }

        private void Refresh()
        {
            var radar = _runner.Radar;
            var s = _runner.Sim.State;
            var arpa = _runner.Arpa;

            _modeSb.Clear();
            _modeSb.Append(radar.PowerOn ? "ON" : "OFF").Append("  ")
                .Append(OrientationName(radar.Orientation)).Append("  RNG ")
                .AppendFormat("{0:0.##}", radar.RangeNm).Append(" Nm");
            if (radar.GuardAlarm)
            {
                _modeSb.Append("  GUARD ALARM");
            }

            if (arpa.TrialManoeuvre)
            {
                _modeSb.Append("  TRIAL");
            }

            _modeLabel.SetText(_modeSb);

            DrawPpi(radar, s, arpa);

            radar.CursorBearingDeg = radar.EblBearingDeg;
            radar.CursorRangeNm = radar.VrmNm;

            _dataSb.Clear();
            _dataSb.AppendFormat("EBL {0:000.0}°\n", radar.EblBearingDeg);
            _dataSb.AppendFormat("VRM {0:0.00} Nm\n", radar.VrmNm);
            _dataSb.AppendFormat("CUR {0:0.00} Nm / {1:000.0}°\n", radar.CursorRangeNm, radar.CursorBearingDeg);
            _dataSb.AppendFormat("HDG {0:000.0}  COG {1:000.0}\n", s.HeadingDeg, s.CogDeg);
            _dataSb.AppendFormat("SOG {0:0.0} kn\n", s.SogMs * 1.94384449244);
            _dataSb.AppendFormat("Echoes {0}\n", radar.Echoes.Count);
            _dataSb.AppendFormat("PI sel #{0}\n", _activePi + 1);
            if (arpa.TrialManoeuvre)
            {
                _dataSb.AppendFormat("Trial {0:000}° {1:0.0} kn\n", arpa.TrialCourseDeg, arpa.TrialSpeedKn);
            }

            _dataLabel.SetText(_dataSb);
            _dataLabel.color = radar.GuardAlarm
                ? BridgeInstrumentCanvas.Danger
                : BridgeInstrumentCanvas.TextPrimary;

            _arpaSb.Clear();
            if (arpa.Enabled)
            {
                _arpaSb.AppendFormat("ARPA {0} vec {1:0} min\n",
                    arpa.TrueVectors ? "TRUE" : "REL", arpa.VectorMinutes);

                int shown = 0;
                for (int i = 0; i < arpa.Tracks.Count && shown < 8; i++, shown++)
                {
                    var t = arpa.Tracks[i];
                    _arpaSb.Append(t.Dangerous ? '!' : ' ');
                    _arpaSb.AppendFormat("{0:00} ", t.ContactId);
                    AppendTrimmed(_arpaSb, t.Name, 10);
                    _arpaSb.AppendFormat("  {0:0.00}Nm {1:000}°  CPA {2:0.00}  TCPA {3:+0.0;-0.0;0.0}m\n",
                        t.RangeNm, t.BearingDeg, t.CpaNm, t.TcpaMin);
                }

                if (arpa.Tracks.Count == 0)
                {
                    _arpaSb.Append("(no tracks)\n");
                }
            }
            else
            {
                _arpaSb.Append("ARPA OFF\n");
            }

            _arpaLabel.SetText(_arpaSb);
        }

        /// <summary>Enum.ToString() allocates and does a reflection lookup; these do not.</summary>
        private static string OrientationName(RadarOrientation o) => o switch
        {
            RadarOrientation.HeadUp => "HEAD UP",
            RadarOrientation.CourseUp => "COURSE UP",
            _ => "NORTH UP"
        };

        private static void AppendTrimmed(StringBuilder sb, string s, int n)
        {
            if (string.IsNullOrEmpty(s))
            {
                sb.Append('—');
                return;
            }

            int count = Math.Min(s.Length, n);
            for (int i = 0; i < count; i++)
            {
                sb.Append(s[i]);
            }
        }

        private void DrawPpi(RadarModel radar, ShipState s, ArpaTracker arpa)
        {
            const int cx = PpiPixels / 2;
            const int cy = PpiPixels / 2;
            float radius = PpiPixels * 0.48f;

            Array.Copy(_screenMask, _frame, _frame.Length);

            if (!radar.PowerOn)
            {
                Upload();
                return;
            }

            // Range rings.
            for (int i = 1; i <= 4; i++)
            {
                DrawCircle(cx, cy, radius * i / 4f, RingColor);
            }

            // VRM.
            float vrmR = (float)(radar.VrmNm / Math.Max(0.01, radar.RangeNm)) * radius;
            DrawCircle(cx, cy, vrmR, BridgeInstrumentCanvas.AccentAmber);

            // Guard zone arc hints.
            if (radar.GuardZoneOn)
            {
                float rIn = (float)(radar.GuardInnerNm / radar.RangeNm) * radius;
                float rOut = (float)(radar.GuardOuterNm / radar.RangeNm) * radius;
                DrawCircle(cx, cy, rIn, BridgeInstrumentCanvas.Danger);
                DrawCircle(cx, cy, rOut, BridgeInstrumentCanvas.Danger);
            }

            // Parallel indexes as parallel lines through range/bearing.
            for (int i = 0; i < radar.ParallelIndexes.Length; i++)
            {
                var pi = radar.ParallelIndexes[i];
                if (!pi.Active)
                {
                    continue;
                }

                double brg = radar.BearingToScreenDeg(pi.BearingDeg, s.HeadingDeg, s.CogDeg);
                float dist = (float)(pi.RangeNm / radar.RangeNm) * radius;
                DrawParallelIndex(cx, cy, radius, brg, dist, BridgeInstrumentCanvas.AccentCyan);
            }

            // Echoes.
            for (int i = 0; i < radar.Echoes.Count; i++)
            {
                var e = radar.Echoes[i];
                double screenBrg = radar.BearingToScreenDeg(e.BearingDeg, s.HeadingDeg, s.CogDeg);
                float rr = (float)(e.RangeNm / radar.RangeNm) * radius;
                if (rr > radius)
                {
                    continue;
                }

                PolarToPixel(cx, cy, rr, screenBrg, out int px, out int py);
                Color32 col = e.IsLand ? EchoLand : e.Kind == TrafficKind.Buoy ? EchoBuoy : EchoShip;
                FillDisk(px, py, e.IsLand ? 2 : 3, col);
            }

            // ARPA vectors.
            if (arpa.Enabled)
            {
                for (int i = 0; i < arpa.Tracks.Count; i++)
                {
                    var t = arpa.Tracks[i];
                    double screenBrg = radar.BearingToScreenDeg(t.BearingDeg, s.HeadingDeg, s.CogDeg);
                    float rr = (float)(t.RangeNm / radar.RangeNm) * radius;
                    if (rr > radius)
                    {
                        continue;
                    }

                    PolarToPixel(cx, cy, rr, screenBrg, out int px, out int py);
                    double course = arpa.TrueVectors ? t.CourseDeg : t.RelCourseDeg;
                    double speedKn = arpa.TrueVectors ? t.SpeedKn : t.RelSpeedKn;
                    double vecNm = speedKn * (arpa.VectorMinutes / 60.0);
                    double vecScreen = radar.BearingToScreenDeg(course, s.HeadingDeg, s.CogDeg);
                    float len = (float)(vecNm / radar.RangeNm) * radius;
                    // Vector from the echo position: polar offset, not a second plot origin.
                    double rad = vecScreen * Math.PI / 180.0;
                    int x2 = px + (int)(Math.Sin(rad) * len);
                    int y2 = py + (int)(Math.Cos(rad) * len);
                    DrawLine(px, py, x2, y2,
                        t.Dangerous ? BridgeInstrumentCanvas.Danger : BridgeInstrumentCanvas.AccentCyan);
                }
            }

            // Own-ship mark.
            FillDisk(cx, cy, 3, Color.white);
            Upload();
        }

        /// <summary>One memcpy into the texture and one upload — no per-pixel API calls.</summary>
        private void Upload()
        {
            _tex.SetPixelData(_frame, 0);
            _tex.Apply(false);
        }

        private void Plot(int x, int y, Color32 c)
        {
            if ((uint)x >= (uint)PpiPixels || (uint)y >= (uint)PpiPixels)
            {
                return;
            }

            _frame[y * PpiPixels + x] = c;
        }

        private void DrawParallelIndex(int cx, int cy, float radius, double bearingScreenDeg,
            float offsetPx, Color32 c)
        {
            double rad = bearingScreenDeg * Math.PI / 180.0;
            // Line perpendicular to bearing, offset by range along bearing.
            float ox = (float)(Math.Sin(rad) * offsetPx);
            float oy = (float)(Math.Cos(rad) * offsetPx);
            float px = (float)Math.Cos(rad);
            float py = (float)-Math.Sin(rad);
            int x0 = (int)(cx + ox - px * radius);
            int y0 = (int)(cy + oy - py * radius);
            int x1 = (int)(cx + ox + px * radius);
            int y1 = (int)(cy + oy + py * radius);
            DrawLine(x0, y0, x1, y1, c);
        }

        private void DrawCircle(int cx, int cy, float r, Color32 c)
        {
            if (r < 1f)
            {
                return;
            }

            int steps = Math.Max(32, (int)(r * 2f));
            int prevX = 0, prevY = 0;
            for (int i = 0; i <= steps; i++)
            {
                double a = i * Math.PI * 2.0 / steps;
                int x = cx + (int)(Math.Sin(a) * r);
                int y = cy + (int)(Math.Cos(a) * r);
                if (i > 0)
                {
                    DrawLine(prevX, prevY, x, y, c);
                }

                prevX = x;
                prevY = y;
            }
        }

        private static void PolarToPixel(int cx, int cy, float r, double screenDeg, out int px, out int py)
        {
            double rad = screenDeg * Math.PI / 180.0;
            px = cx + (int)(Math.Sin(rad) * r);
            py = cy + (int)(Math.Cos(rad) * r);
        }

        private void FillDisk(int cx, int cy, int r, Color32 c)
        {
            int r2 = r * r;
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y <= r2)
                    {
                        Plot(cx + x, cy + y, c);
                    }
                }
            }
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Plot(x0, y0, c);

                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
