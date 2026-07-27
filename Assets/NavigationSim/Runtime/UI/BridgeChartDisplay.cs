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
    /// Chart-lite world-space panel (ECDIS-lite): north-up plot of own-ship + traffic.
    /// Open from <see cref="BridgeInstrumentsMenu"/> (Quest X) or keyboard C in editor.
    /// <para>
    /// Rasterised the same way as <see cref="BridgeRadarDisplay"/>: one reused
    /// <see cref="Color32"/> buffer, a baked static backdrop, one upload per refresh.
    /// </para>
    /// </summary>
    public sealed class BridgeChartDisplay : MonoBehaviour
    {
        private const float Width = 1100f;
        private const float Height = 900f;

        /// <summary>Plot edge in panel units. Unchanged — this is what the user sees.</summary>
        private const float PlotUi = 720f;

        /// <summary>
        /// Raster resolution. The plot subtends ~24° at 1.7 m and the headset resolves
        /// ~20 px/°, so 512 is already above what it can show and costs half the pixels
        /// of the 720 this started at.
        /// </summary>
        private const int PlotPixels = 512;

        /// <summary>
        /// Own-ship barely moves a pixel in half a second at chart scale, and the plot
        /// is the expensive part of a refresh.
        /// </summary>
        private const float RefreshSeconds = 0.5f;

        private static readonly Vector3 Prewarm = new Vector3(0f, -1200f, 0f);
        private static readonly Color32 SeaColor = new Color32(13, 31, 51, 255);
        private static readonly Color32 LandColor = new Color32(64, 89, 56, 255);
        private static readonly Color32 GridColor = new Color32(38, 64, 82, 255);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private RawImage _plot;
        private Texture2D _tex;

        /// <summary>Scratch frame, reused every refresh.</summary>
        private Color32[] _frame;

        /// <summary>Sea fill and grid, baked once — neither depends on own-ship position.</summary>
        private Color32[] _backdrop;

        private TMP_Text _scaleLabel;
        private TMP_Text _posLabel;
        private TMP_Text _statusLabel;

        private readonly StringBuilder _scaleSb = new StringBuilder(64);
        private readonly StringBuilder _posSb = new StringBuilder(128);
        private readonly StringBuilder _statusSb = new StringBuilder(96);

        private float _rangeM = 800f;
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
            // X is reserved for BridgeInstrumentsMenu on Quest. Editor shortcut: C.
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
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

            BridgeInstrumentCanvas.PlaceInFront(_root.transform, 1.7f, 0.35f, 0.05f);
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
            _root = BridgeInstrumentCanvas.CreateCanvas("BridgeChartCanvas", transform,
                new Vector2(Width, Height), 0.001f);
            _root.transform.position = Prewarm;
            _canvas = _root.GetComponent<Canvas>();
            var rt = _root.GetComponent<RectTransform>();

            BridgeInstrumentCanvas.Image(rt, "Bg", Vector2.zero, new Vector2(Width, Height),
                BridgeInstrumentCanvas.PanelBg);
            BridgeInstrumentCanvas.Text(rt, "Title", new Vector2(20f, -14f), new Vector2(700f, 36f),
                "CHART-LITE — botón X del panel / menú (X)", 24f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.AccentAmber);
            BridgeInstrumentCanvas.Button(rt, new Vector2(Width - 70f, -10f), new Vector2(50f, 36f),
                "X", BridgeInstrumentCanvas.Danger, Close);

            BridgeInstrumentCanvas.Button(rt, new Vector2(20f, -60f), new Vector2(90f, 36f),
                "ZOOM+", BridgeInstrumentCanvas.AccentCyan, () => _rangeM = Mathf.Max(200f, _rangeM * 0.7f), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(120f, -60f), new Vector2(90f, 36f),
                "ZOOM-", BridgeInstrumentCanvas.AccentCyan, () => _rangeM = Mathf.Min(4000f, _rangeM * 1.4f), true);
            BridgeInstrumentCanvas.Button(rt, new Vector2(220f, -60f), new Vector2(120f, 36f),
                "CENTER", BridgeInstrumentCanvas.AccentGreen, () => { });

            // Readouts on their own canvas so retyping them does not re-batch the
            // background and the buttons.
            var readouts = BridgeInstrumentCanvas.SubCanvas(rt, "Readouts", new Vector2(Width, Height));

            _scaleLabel = BridgeInstrumentCanvas.Text(readouts, "Scale", new Vector2(360f, -60f),
                new Vector2(400f, 36f), "SCALE", 20f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextMuted);
            _posLabel = BridgeInstrumentCanvas.Text(readouts, "Pos", new Vector2(20f, -820f),
                new Vector2(900f, 60f), "", 22f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextPrimary);
            _statusLabel = BridgeInstrumentCanvas.Text(readouts, "Status", new Vector2(20f, -870f),
                new Vector2(900f, 28f), "NORTH UP", 18f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextMuted);

            _tex = new Texture2D(PlotPixels, PlotPixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _frame = new Color32[PlotPixels * PlotPixels];
            _backdrop = new Color32[PlotPixels * PlotPixels];
            BakeBackdrop();

            var plotGo = new GameObject("Plot", typeof(RectTransform));
            plotGo.transform.SetParent(rt, false);
            var plotRt = plotGo.GetComponent<RectTransform>();
            plotRt.anchorMin = new Vector2(0f, 1f);
            plotRt.anchorMax = new Vector2(0f, 1f);
            plotRt.pivot = new Vector2(0f, 1f);
            plotRt.anchoredPosition = new Vector2(190f, -110f);
            plotRt.sizeDelta = new Vector2(PlotUi, PlotUi);
            _plot = plotGo.AddComponent<RawImage>();
            _plot.texture = _tex;
            _plot.raycastTarget = false;

            _built = true;
        }

        /// <summary>Sea fill plus the quarter-grid, neither of which depends on own-ship.</summary>
        private void BakeBackdrop()
        {
            for (int i = 0; i < _backdrop.Length; i++)
            {
                _backdrop[i] = SeaColor;
            }

            for (int i = 1; i < 4; i++)
            {
                int g = PlotPixels * i / 4;
                for (int y = 0; y < PlotPixels; y++)
                {
                    _backdrop[y * PlotPixels + g] = GridColor;
                }

                for (int x = 0; x < PlotPixels; x++)
                {
                    _backdrop[g * PlotPixels + x] = GridColor;
                }
            }
        }

        private void Refresh()
        {
            var s = _runner.Sim.State;
            var geo = _runner.Geo;
            geo.ToLatLon(s.North, s.East, out double lat, out double lon);

            double half = _rangeM;
            Array.Copy(_backdrop, _frame, _frame.Length);

            // Land samples.
            var lands = _runner.LandSamples;
            if (lands != null)
            {
                for (int i = 0; i < lands.Count; i++)
                {
                    if (TryMap(lands[i].east, lands[i].north, s.East, s.North, half, out int px, out int py))
                    {
                        FillDisk(px, py, 3, LandColor);
                    }
                }
            }

            // Traffic.
            var traffic = _runner.Traffic;
            if (traffic != null)
            {
                for (int i = 0; i < traffic.Contacts.Count; i++)
                {
                    var c = traffic.Contacts[i];
                    if (!c.Visible)
                    {
                        continue;
                    }

                    if (!TryMap(c.East, c.North, s.East, s.North, half, out int px, out int py))
                    {
                        continue;
                    }

                    Color32 col = c.Kind == TrafficKind.Buoy
                        ? BridgeInstrumentCanvas.AccentAmber
                        : BridgeInstrumentCanvas.Danger;
                    FillDisk(px, py, c.Kind == TrafficKind.Buoy ? 2 : 4, col);
                    DrawHeadingTick(px, py, c.HeadingDeg, col);
                }
            }

            // Own ship at centre.
            const int cx = PlotPixels / 2;
            const int cy = PlotPixels / 2;
            FillTriangle(cx, cy, (float)s.HeadingDeg, BridgeInstrumentCanvas.AccentCyan);
            DrawHeadingTick(cx, cy, s.CogDeg, BridgeInstrumentCanvas.AccentGreen);

            _tex.SetPixelData(_frame, 0);
            _tex.Apply(false);

            double rangeNm = _rangeM / 1852.0;
            _scaleSb.Clear();
            _scaleSb.AppendFormat("RANGE ±{0:0.00} Nm  ({1:0} m)", rangeNm, _rangeM);
            _scaleLabel.SetText(_scaleSb);

            _posSb.Clear();
            _posSb.Append(GeoDatum.FormatLat(lat)).Append("   ").Append(GeoDatum.FormatLon(lon));
            _posSb.AppendFormat("   HDG {0:000.0}°   SOG {1:0.0} kn",
                s.HeadingDeg, s.SogMs * 1.94384449244);
            _posLabel.SetText(_posSb);

            int n = traffic?.Contacts.Count ?? 0;
            _statusSb.Clear();
            _statusSb.AppendFormat("NORTH UP · {0} contacts · chart-lite (no ENC)", n);
            _statusLabel.SetText(_statusSb);
        }

        private static bool TryMap(double east, double north, double ownE, double ownN,
            double half, out int px, out int py)
        {
            double dx = (east - ownE) / half;
            double dy = (north - ownN) / half;
            px = (int)((0.5 + dx * 0.5) * (PlotPixels - 1));
            py = (int)((0.5 + dy * 0.5) * (PlotPixels - 1));
            return px >= 0 && px < PlotPixels && py >= 0 && py < PlotPixels;
        }

        private void Plot(int x, int y, Color32 c)
        {
            if ((uint)x >= (uint)PlotPixels || (uint)y >= (uint)PlotPixels)
            {
                return;
            }

            _frame[y * PlotPixels + x] = c;
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

        private void DrawHeadingTick(int cx, int cy, double headingDeg, Color32 c)
        {
            float rad = (float)headingDeg * Mathf.Deg2Rad;
            // Texture Y up = north.
            int x2 = cx + (int)(Mathf.Sin(rad) * 10f);
            int y2 = cy + (int)(Mathf.Cos(rad) * 10f);
            DrawLine(cx, cy, x2, y2, c);
        }

        private void FillTriangle(int cx, int cy, float headingDeg, Color32 c)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            Vector2 tip = new Vector2(cx, cy) + fwd * 9f;
            Vector2 bl = new Vector2(cx, cy) - fwd * 6f - right * 5f;
            Vector2 br = new Vector2(cx, cy) - fwd * 6f + right * 5f;
            FillDisk((int)tip.x, (int)tip.y, 2, c);
            DrawLine((int)tip.x, (int)tip.y, (int)bl.x, (int)bl.y, c);
            DrawLine((int)tip.x, (int)tip.y, (int)br.x, (int)br.y, c);
            DrawLine((int)bl.x, (int)bl.y, (int)br.x, (int)br.y, c);
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
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
