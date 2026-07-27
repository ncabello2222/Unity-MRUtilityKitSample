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
    /// </summary>
    public sealed class BridgeRadarDisplay : MonoBehaviour
    {
        private const float Width = 1200f;
        private const float Height = 960f;
        private const int PpiSize = 640;
        private static readonly Vector3 Prewarm = new Vector3(0f, -1400f, 0f);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private RawImage _ppi;
        private Texture2D _tex;
        private TMP_Text _dataLabel;
        private TMP_Text _arpaLabel;
        private TMP_Text _modeLabel;
        private float _timer;
        private float _sweepDeg;
        private bool _openWhenReady;
        private bool _built;
        private int _activePi;

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

            if (!IsOpen || _runner?.Sim == null || _runner.Radar == null)
            {
                return;
            }

            _sweepDeg = (_sweepDeg + Time.deltaTime * 72f) % 360f;
            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = 0.12f;
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

            BridgeInstrumentCanvas.PlaceInFront(_root.transform, 1.75f, -0.45f, 0.02f);
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

            _modeLabel = BridgeInstrumentCanvas.Text(rt, "Mode", new Vector2(430f, -55f),
                new Vector2(400f, 34f), "", 20f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextPrimary);

            _tex = new Texture2D(PpiSize, PpiSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var plotGo = new GameObject("PPI", typeof(RectTransform));
            plotGo.transform.SetParent(rt, false);
            var plotRt = plotGo.GetComponent<RectTransform>();
            plotRt.anchorMin = new Vector2(0f, 1f);
            plotRt.anchorMax = new Vector2(0f, 1f);
            plotRt.pivot = new Vector2(0f, 1f);
            plotRt.anchoredPosition = new Vector2(40f, -240f);
            plotRt.sizeDelta = new Vector2(PpiSize, PpiSize);
            _ppi = plotGo.AddComponent<RawImage>();
            _ppi.texture = _tex;
            _ppi.raycastTarget = false;

            _dataLabel = BridgeInstrumentCanvas.Text(rt, "Data", new Vector2(700f, -240f),
                new Vector2(460f, 220f), "", 20f, TextAlignmentOptions.TopLeft,
                BridgeInstrumentCanvas.TextPrimary);
            _dataLabel.textWrappingMode = TextWrappingModes.Normal;

            _arpaLabel = BridgeInstrumentCanvas.Text(rt, "Arpa", new Vector2(700f, -480f),
                new Vector2(460f, 440f), "", 17f, TextAlignmentOptions.TopLeft,
                BridgeInstrumentCanvas.TextMuted);
            _arpaLabel.textWrappingMode = TextWrappingModes.Normal;

            _built = true;
        }

        private void Refresh()
        {
            var radar = _runner.Radar;
            var s = _runner.Sim.State;
            var arpa = _runner.Arpa;

            _modeLabel.text =
                $"{(radar.PowerOn ? "ON" : "OFF")}  {radar.Orientation}  RNG {radar.RangeNm:0.##} Nm" +
                (radar.GuardAlarm ? "  GUARD ALARM" : "") +
                (arpa.TrialManoeuvre ? "  TRIAL" : "");

            DrawPpi(radar, s, arpa);

            radar.CursorBearingDeg = radar.EblBearingDeg;
            radar.CursorRangeNm = radar.VrmNm;

            var sb = new StringBuilder(256);
            sb.AppendLine($"EBL {radar.EblBearingDeg:000.0}°");
            sb.AppendLine($"VRM {radar.VrmNm:0.00} Nm");
            sb.AppendLine($"CUR {radar.CursorRangeNm:0.00} Nm / {radar.CursorBearingDeg:000.0}°");
            sb.AppendLine($"HDG {s.HeadingDeg:000.0}  COG {s.CogDeg:000.0}");
            sb.AppendLine($"SOG {s.SogMs * 1.94384449244:0.0} kn");
            sb.AppendLine($"Echoes {radar.Echoes.Count}");
            sb.AppendLine($"PI sel #{_activePi + 1}");
            if (arpa.TrialManoeuvre)
            {
                sb.AppendLine($"Trial {arpa.TrialCourseDeg:000}° {arpa.TrialSpeedKn:0.0} kn");
            }

            _dataLabel.text = sb.ToString();
            _dataLabel.color = radar.GuardAlarm
                ? BridgeInstrumentCanvas.Danger
                : BridgeInstrumentCanvas.TextPrimary;

            var arpaSb = new StringBuilder(512);
            arpaSb.AppendLine(arpa.Enabled
                ? $"ARPA {(arpa.TrueVectors ? "TRUE" : "REL")} vec {arpa.VectorMinutes:0} min"
                : "ARPA OFF");
            if (arpa.Enabled)
            {
                int shown = 0;
                for (int i = 0; i < arpa.Tracks.Count && shown < 8; i++, shown++)
                {
                    var t = arpa.Tracks[i];
                    string flag = t.Dangerous ? "!" : " ";
                    arpaSb.AppendLine(
                        $"{flag}{t.ContactId:00} {Trim(t.Name, 10)}  " +
                        $"{t.RangeNm:0.00}Nm {t.BearingDeg:000}°  " +
                        $"CPA {t.CpaNm:0.00}  TCPA {t.TcpaMin:+0.0;-0.0;0.0}m");
                }

                if (arpa.Tracks.Count == 0)
                {
                    arpaSb.AppendLine("(no tracks)");
                }
            }

            _arpaLabel.text = arpaSb.ToString();
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "—";
            }

            return s.Length <= n ? s : s.Substring(0, n);
        }

        private void DrawPpi(RadarModel radar, ShipState s, ArpaTracker arpa)
        {
            int w = _tex.width;
            int h = _tex.height;
            int cx = w / 2;
            int cy = h / 2;
            float radius = w * 0.48f;
            var bg = new Color(0.02f, 0.08f, 0.04f, 1f);
            var ring = new Color(0.15f, 0.45f, 0.25f, 1f);
            var echoShip = new Color(0.4f, 1f, 0.45f, 1f);
            var echoBuoy = new Color(1f, 0.85f, 0.2f, 1f);
            var echoLand = new Color(0.55f, 0.55f, 0.35f, 1f);

            var pixels = _tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            _tex.SetPixels(pixels);

            // Circular mask background.
            float r2 = radius * radius;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    if (dx * dx + dy * dy <= r2)
                    {
                        _tex.SetPixel(x, y, bg);
                    }
                }
            }

            if (!radar.PowerOn)
            {
                _tex.Apply(false);
                return;
            }

            // Range rings.
            for (int i = 1; i <= 4; i++)
            {
                DrawCircle(cx, cy, radius * i / 4f, ring);
            }

            // Heading flash (ship heading in screen space).
            double hdgScreen = radar.BearingToScreenDeg(s.HeadingDeg, s.HeadingDeg, s.CogDeg);
            DrawRadial(cx, cy, radius, hdgScreen, new Color(0.9f, 0.9f, 0.5f, 0.8f));

            // Sweep.
            DrawRadial(cx, cy, radius, _sweepDeg, new Color(0.3f, 0.9f, 0.4f, 0.35f));

            // EBL / VRM.
            double eblScreen = radar.BearingToScreenDeg(radar.EblBearingDeg, s.HeadingDeg, s.CogDeg);
            DrawRadial(cx, cy, radius, eblScreen, BridgeInstrumentCanvas.AccentAmber);
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
                Color col = e.IsLand ? echoLand : e.Kind == TrafficKind.Buoy ? echoBuoy : echoShip;
                FillDisk(px, py, e.IsLand ? 2 : 4, col);
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
                    PolarToPixel(px, py, len, vecScreen, out int x2, out int y2);
                    // Vector from echo position: reinterpret polar from px,py as offset.
                    double rad = vecScreen * Math.PI / 180.0;
                    x2 = px + (int)(Math.Sin(rad) * len);
                    y2 = py + (int)(Math.Cos(rad) * len);
                    DrawLine(px, py, x2, y2,
                        t.Dangerous ? BridgeInstrumentCanvas.Danger : BridgeInstrumentCanvas.AccentCyan);
                }
            }

            // Own-ship mark.
            FillDisk(cx, cy, 3, Color.white);
            _tex.Apply(false);
        }

        private void DrawParallelIndex(int cx, int cy, float radius, double bearingScreenDeg,
            float offsetPx, Color c)
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

        private void DrawCircle(int cx, int cy, float r, Color c)
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

        private void DrawRadial(int cx, int cy, float r, double screenDeg, Color c)
        {
            double rad = screenDeg * Math.PI / 180.0;
            int x2 = cx + (int)(Math.Sin(rad) * r);
            int y2 = cy + (int)(Math.Cos(rad) * r);
            DrawLine(cx, cy, x2, y2, c);
        }

        private static void PolarToPixel(int cx, int cy, float r, double screenDeg, out int px, out int py)
        {
            double rad = screenDeg * Math.PI / 180.0;
            px = cx + (int)(Math.Sin(rad) * r);
            py = cy + (int)(Math.Cos(rad) * r);
        }

        private void FillDisk(int cx, int cy, int r, Color c)
        {
            int r2 = r * r;
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r2)
                    {
                        continue;
                    }

                    int px = cx + x;
                    int py = cy + y;
                    if (px >= 0 && px < _tex.width && py >= 0 && py < _tex.height)
                    {
                        _tex.SetPixel(px, py, c);
                    }
                }
            }
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < _tex.width && y0 >= 0 && y0 < _tex.height)
                {
                    float existing = _tex.GetPixel(x0, y0).a;
                    if (existing < 0.05f || c.a > 0.5f)
                    {
                        _tex.SetPixel(x0, y0, c);
                    }
                }

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
