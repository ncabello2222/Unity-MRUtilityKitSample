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
    /// </summary>
    public sealed class BridgeChartDisplay : MonoBehaviour
    {
        private const float Width = 1100f;
        private const float Height = 900f;
        private static readonly Vector3 Prewarm = new Vector3(0f, -1200f, 0f);

        private NavigationSimRunner _runner;
        private GameObject _root;
        private Canvas _canvas;
        private RawImage _plot;
        private Texture2D _tex;
        private TMP_Text _scaleLabel;
        private TMP_Text _posLabel;
        private TMP_Text _statusLabel;
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

            _timer = 0.15f;
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

            _scaleLabel = BridgeInstrumentCanvas.Text(rt, "Scale", new Vector2(360f, -60f),
                new Vector2(400f, 36f), "SCALE", 20f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextMuted);
            _posLabel = BridgeInstrumentCanvas.Text(rt, "Pos", new Vector2(20f, -820f),
                new Vector2(900f, 60f), "", 22f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextPrimary);
            _statusLabel = BridgeInstrumentCanvas.Text(rt, "Status", new Vector2(20f, -870f),
                new Vector2(900f, 28f), "NORTH UP", 18f, TextAlignmentOptions.Left,
                BridgeInstrumentCanvas.TextMuted);

            const int plotSize = 720;
            _tex = new Texture2D(plotSize, plotSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var plotGo = new GameObject("Plot", typeof(RectTransform));
            plotGo.transform.SetParent(rt, false);
            var plotRt = plotGo.GetComponent<RectTransform>();
            plotRt.anchorMin = new Vector2(0f, 1f);
            plotRt.anchorMax = new Vector2(0f, 1f);
            plotRt.pivot = new Vector2(0f, 1f);
            plotRt.anchoredPosition = new Vector2(190f, -110f);
            plotRt.sizeDelta = new Vector2(720f, 720f);
            _plot = plotGo.AddComponent<RawImage>();
            _plot.texture = _tex;
            _plot.raycastTarget = false;

            _built = true;
        }

        private void Refresh()
        {
            var s = _runner.Sim.State;
            var geo = _runner.Geo;
            geo.ToLatLon(s.North, s.East, out double lat, out double lon);

            double half = _rangeM;
            int w = _tex.width;
            int h = _tex.height;
            var sea = new Color(0.05f, 0.12f, 0.20f, 1f);
            var land = new Color(0.25f, 0.35f, 0.22f, 1f);
            var grid = new Color(0.15f, 0.25f, 0.32f, 1f);
            ClearTex(sea);

            // Grid.
            for (int i = 1; i < 4; i++)
            {
                int g = w * i / 4;
                DrawVLine(g, grid);
                DrawHLine(g, grid);
            }

            // Land samples.
            var lands = _runner.LandSamples;
            if (lands != null)
            {
                for (int i = 0; i < lands.Count; i++)
                {
                    if (TryMap(lands[i].east, lands[i].north, s.East, s.North, half, w, h, out int px, out int py))
                    {
                        FillDisk(px, py, 4, land);
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

                    if (!TryMap(c.East, c.North, s.East, s.North, half, w, h, out int px, out int py))
                    {
                        continue;
                    }

                    Color col = c.Kind == TrafficKind.Buoy
                        ? BridgeInstrumentCanvas.AccentAmber
                        : BridgeInstrumentCanvas.Danger;
                    FillDisk(px, py, c.Kind == TrafficKind.Buoy ? 3 : 5, col);
                    DrawHeadingTick(px, py, c.HeadingDeg, col);
                }
            }

            // Own ship at centre.
            int cx = w / 2;
            int cy = h / 2;
            FillTriangle(cx, cy, (float)s.HeadingDeg, BridgeInstrumentCanvas.AccentCyan);
            DrawHeadingTick(cx, cy, s.CogDeg, BridgeInstrumentCanvas.AccentGreen);

            _tex.Apply(false);

            double rangeNm = _rangeM / 1852.0;
            _scaleLabel.text = $"RANGE ±{rangeNm:0.00} Nm  ({_rangeM:0} m)";
            _posLabel.text =
                $"{GeoDatum.FormatLat(lat)}   {GeoDatum.FormatLon(lon)}   HDG {s.HeadingDeg:000.0}°   SOG {s.SogMs * 1.94384449244:0.0} kn";
            int n = traffic?.Contacts.Count ?? 0;
            _statusLabel.text = $"NORTH UP · {n} contacts · chart-lite (no ENC)";
        }

        private static bool TryMap(double east, double north, double ownE, double ownN,
            double half, int w, int h, out int px, out int py)
        {
            double dx = (east - ownE) / half;
            double dy = (north - ownN) / half;
            px = (int)((0.5 + dx * 0.5) * (w - 1));
            py = (int)((0.5 + dy * 0.5) * (h - 1));
            return px >= 0 && px < w && py >= 0 && py < h;
        }

        private void ClearTex(Color c)
        {
            var pixels = _tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = c;
            }

            _tex.SetPixels(pixels);
        }

        private void DrawVLine(int x, Color c)
        {
            for (int y = 0; y < _tex.height; y++)
            {
                _tex.SetPixel(x, y, c);
            }
        }

        private void DrawHLine(int y, Color c)
        {
            for (int x = 0; x < _tex.width; x++)
            {
                _tex.SetPixel(x, y, c);
            }
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

        private void DrawHeadingTick(int cx, int cy, double headingDeg, Color c)
        {
            double rad = headingDeg * Mathf.Deg2Rad;
            // Texture Y up = north.
            int x2 = cx + (int)(Mathf.Sin((float)rad) * 14f);
            int y2 = cy + (int)(Mathf.Cos((float)rad) * 14f);
            DrawLine(cx, cy, x2, y2, c);
        }

        private void FillTriangle(int cx, int cy, float headingDeg, Color c)
        {
            float rad = headingDeg * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            Vector2 tip = new Vector2(cx, cy) + fwd * 12f;
            Vector2 bl = new Vector2(cx, cy) - fwd * 8f - right * 7f;
            Vector2 br = new Vector2(cx, cy) - fwd * 8f + right * 7f;
            FillDisk((int)tip.x, (int)tip.y, 2, c);
            DrawLine((int)tip.x, (int)tip.y, (int)bl.x, (int)bl.y, c);
            DrawLine((int)tip.x, (int)tip.y, (int)br.x, (int)br.y, c);
            DrawLine((int)bl.x, (int)bl.y, (int)br.x, (int)br.y, c);
        }

        private void DrawLine(int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < _tex.width && y0 >= 0 && y0 < _tex.height)
                {
                    _tex.SetPixel(x0, y0, c);
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
