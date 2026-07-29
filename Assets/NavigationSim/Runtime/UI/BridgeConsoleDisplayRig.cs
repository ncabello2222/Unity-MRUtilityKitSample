using System.Collections;
using System.Collections.Generic;
using ShipBridgePrototype;
using TMPro;
using UnityEngine;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Docks two instrument canvases on the upper console surface, side by side.
    /// Left bank cycles OpenBridge-style panels (conning / chart); right bank cycles
    /// sensor panels (radar / AIS). Banks never share the same instrument.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BridgeConsoleDisplayRig : MonoBehaviour
    {
        public static BridgeConsoleDisplayRig Instance { get; private set; }

        private const float GapMeters = 0.04f;
        private const float SideMarginMeters = 0.08f;
        private const float ButtonStripMeters = 0.07f;
        private const float ScreenTiltDeg = 18f;
        private const float LiftAboveSurfaceMeters = 0.02f;
        private const float MaxSlotHeightMeters = 0.48f;
        private const float MinSlotHeightMeters = 0.28f;
        private const float MaxSlotWidthMeters = 1.55f;
        private const float ChromeHeightPx = 64f;
        private const float ChromeScale = 0.001f;

        private BridgeConsoleController _console;
        private Transform _rigRoot;
        private Slot _left;
        private Slot _right;
        private bool _attached;

        private readonly List<IBridgeDockableInstrument> _leftBank = new();
        private readonly List<IBridgeDockableInstrument> _rightBank = new();

        private sealed class Slot
        {
            public Transform Root;
            public Transform Content;
            public RectTransform Chrome;
            public TMP_Text Title;
            public float WidthM;
            public float HeightM;
            public int Index;
            public List<IBridgeDockableInstrument> Bank;
            public IBridgeDockableInstrument Active;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void LateUpdate()
        {
            if (!EnsureAttached())
            {
                return;
            }

            // Parent is the console root (wall-stretched). Counter that scale so
            // slot children keep uniform world meters and correct aspect.
            var parentScale = _console.transform.lossyScale;
            _rigRoot.localScale = new Vector3(
                SafeInv(parentScale.x),
                SafeInv(parentScale.y),
                SafeInv(parentScale.z));

            LayoutSlots();
            FitActive(_left);
            FitActive(_right);
        }

        /// <summary>Show an instrument in its bank slot (used by the floating menu).</summary>
        public bool TryShow(string instrumentId)
        {
            if (!_attached)
            {
                return false;
            }

            EnsureBanks();
            return TryShowInSlot(_left, instrumentId) || TryShowInSlot(_right, instrumentId);
        }

        public void CycleLeft(int direction) => CycleSlot(_left, direction);

        public void CycleRight(int direction) => CycleSlot(_right, direction);

        /// <summary>
        /// Keeps trying after room/console regen. A one-shot 12s attach was leaving
        /// instruments undocked (PlaceInFront at ~2 m wide) when the console arrived late.
        /// </summary>
        private bool EnsureAttached()
        {
            if (_attached)
            {
                if (_console != null && _console.IsPlaced && _rigRoot != null)
                {
                    return true;
                }

                TearDownRig();
                _attached = false;
                _console = null;
            }

            if (!TryBindConsole())
            {
                return false;
            }

            EnsureBanks();
            if (_leftBank.Count == 0 && _rightBank.Count == 0)
            {
                return false;
            }

            foreach (var instrument in _leftBank)
            {
                instrument.SetDocked(true);
            }

            foreach (var instrument in _rightBank)
            {
                instrument.SetDocked(true);
            }

            BuildRig();
            LayoutSlots();
            _attached = true;
            ShowDefaults();
            Debug.Log(
                "[BridgeConsoleDisplayRig] Docked left=" +
                (_left.Active?.DisplayName ?? "?") +
                " right=" + (_right.Active?.DisplayName ?? "?"));
            return true;
        }

        private bool TryBindConsole()
        {
            if (_console != null && _console.IsPlaced)
            {
                return true;
            }

            var placer = FindAnyObjectByType<BridgeConsolePlacer>();
            if (placer != null && placer.ActiveConsole != null && placer.ActiveConsole.IsPlaced)
            {
                _console = placer.ActiveConsole;
                return true;
            }

            var consoles = FindObjectsByType<BridgeConsoleController>(FindObjectsSortMode.None);
            foreach (var c in consoles)
            {
                if (c != null && c.IsPlaced)
                {
                    _console = c;
                    return true;
                }
            }

            return false;
        }

        private void EnsureBanks()
        {
            var runner = NavigationSimRunner.Instance != null
                ? NavigationSimRunner.Instance
                : FindAnyObjectByType<NavigationSimRunner>();
            if (runner == null)
            {
                return;
            }

            if (_leftBank.Count == 0)
            {
                // Left: OpenBridge / nav instruments.
                AddBank(_leftBank, runner.GetComponent<BridgeConningDisplay>());
                AddBank(_leftBank, runner.GetComponent<BridgeChartDisplay>());
            }

            if (_rightBank.Count == 0)
            {
                // Right: radar + traffic. No overlap with the left bank.
                AddBank(_rightBank, runner.GetComponent<BridgeRadarDisplay>());
                AddBank(_rightBank, runner.GetComponent<BridgeAisDisplay>());
            }
        }

        private static void AddBank(List<IBridgeDockableInstrument> bank, IBridgeDockableInstrument instrument)
        {
            if (instrument != null)
            {
                bank.Add(instrument);
            }
        }

        private void BuildRig()
        {
            TearDownRig();

            // Same trick as the height handle: hang off the console root so wall
            // stretch is countered explicitly, instead of inheriting Upper scale.
            var go = new GameObject("ConsoleDisplays");
            _rigRoot = go.transform;
            _rigRoot.SetParent(_console.transform, false);
            _rigRoot.localPosition = Vector3.zero;
            _rigRoot.localRotation = Quaternion.identity;
            _rigRoot.localScale = Vector3.one;

            _left = CreateSlot("LeftSlot", _leftBank);
            _right = CreateSlot("RightSlot", _rightBank);
        }

        private void TearDownRig()
        {
            if (_rigRoot != null)
            {
                Destroy(_rigRoot.gameObject);
                _rigRoot = null;
            }

            _left = null;
            _right = null;
        }

        private Slot CreateSlot(string name, List<IBridgeDockableInstrument> bank)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_rigRoot, false);

            var content = new GameObject("Content").transform;
            content.SetParent(root, false);

            var chromeGo = BridgeInstrumentCanvas.CreateCanvas(name + "Chrome", root,
                new Vector2(400f, ChromeHeightPx), ChromeScale);
            var chrome = chromeGo.GetComponent<RectTransform>();
            chrome.localPosition = Vector3.zero;
            chrome.localRotation = Quaternion.identity;

            var title = BridgeInstrumentCanvas.Text(chrome, "Title",
                new Vector2(70f, -10f), new Vector2(260f, 44f),
                "—", 22f, TextAlignmentOptions.Center, BridgeInstrumentCanvas.AccentAmber);

            var slot = new Slot
            {
                Root = root,
                Content = content,
                Chrome = chrome,
                Title = title,
                Bank = bank,
                Index = 0
            };

            BridgeInstrumentCanvas.Button(chrome, new Vector2(8f, -10f), new Vector2(56f, 44f),
                "◀", BridgeInstrumentCanvas.AccentCyan,
                () => CycleSlot(slot, -1));
            BridgeInstrumentCanvas.Button(chrome, new Vector2(336f, -10f), new Vector2(56f, 44f),
                "▶", BridgeInstrumentCanvas.AccentCyan,
                () => CycleSlot(slot, +1));
            return slot;
        }

        private void LayoutSlots()
        {
            if (!TryMeasureSurface(out var origin, out var right, out var up, out var forward,
                    out var usableWidth, out var usableHeight))
            {
                return;
            }

            var slotWidth = Mathf.Min(MaxSlotWidthMeters,
                (usableWidth - GapMeters) * 0.5f);
            var slotHeight = Mathf.Clamp(usableHeight, MinSlotHeightMeters, MaxSlotHeightMeters);
            // Keep a comfortable landscape aspect for OpenBridge / radar.
            slotHeight = Mathf.Min(slotHeight, slotWidth / 1.35f);
            slotHeight = Mathf.Max(slotHeight, MinSlotHeightMeters);

            var totalWidth = slotWidth * 2f + GapMeters;
            var start = origin - right * (totalWidth * 0.5f) + right * (slotWidth * 0.5f);
            // World-space Canvas draws on the -Z face (same as PlaceInFront).
            // Console.forward points into the room / at the user, so the slot's +Z
            // must point at the wall (-forward) or the UI reads mirrored.
            // Negative pitch: top of the screen leans toward the wall (reclined).
            var tilt = Quaternion.AngleAxis(-ScreenTiltDeg, right);
            var facing = tilt * Quaternion.LookRotation(-forward, up);

            PlaceSlot(_left, start, facing, slotWidth, slotHeight);
            PlaceSlot(_right, start + right * (slotWidth + GapMeters), facing, slotWidth, slotHeight);
        }

        private void PlaceSlot(
            Slot slot,
            Vector3 center,
            Quaternion facing,
            float widthM,
            float heightM)
        {
            if (slot?.Root == null)
            {
                return;
            }

            slot.WidthM = widthM;
            slot.HeightM = heightM;
            slot.Root.SetPositionAndRotation(center, facing);
            // +Z faces the wall; tiny room nudge so the plane sits just above the mesh.
            slot.Content.localPosition = new Vector3(0f, 0f, -0.01f);
            slot.Content.localRotation = Quaternion.identity;
            slot.Content.localScale = Vector3.one;

            if (slot.Chrome != null)
            {
                // Buttons sit under the screen, toward the desk lip.
                var chromeWidthPx = Mathf.Max(280f, widthM / ChromeScale);
                slot.Chrome.sizeDelta = new Vector2(chromeWidthPx, ChromeHeightPx);
                slot.Chrome.localScale = Vector3.one * ChromeScale;
                slot.Chrome.localRotation = Quaternion.identity;
                slot.Chrome.localPosition = new Vector3(
                    0f,
                    -(heightM * 0.5f + ButtonStripMeters * 0.55f),
                    -0.01f);

                // Keep ◀ ▶ near the chrome edges as width changes.
                RelayoutChromeButtons(slot.Chrome, chromeWidthPx);
                if (slot.Title != null)
                {
                    var titleRt = slot.Title.rectTransform;
                    titleRt.anchoredPosition = new Vector2(70f, -10f);
                    titleRt.sizeDelta = new Vector2(Mathf.Max(120f, chromeWidthPx - 140f), 44f);
                }
            }
        }

        private static void RelayoutChromeButtons(RectTransform chrome, float widthPx)
        {
            for (var i = 0; i < chrome.childCount; i++)
            {
                var child = chrome.GetChild(i) as RectTransform;
                if (child == null)
                {
                    continue;
                }

                if (child.name.StartsWith("Btn_◀"))
                {
                    child.anchoredPosition = new Vector2(8f, -10f);
                }
                else if (child.name.StartsWith("Btn_▶"))
                {
                    child.anchoredPosition = new Vector2(widthPx - 64f, -10f);
                }
            }
        }

        private bool TryMeasureSurface(
            out Vector3 origin,
            out Vector3 right,
            out Vector3 up,
            out Vector3 forward,
            out float usableWidth,
            out float usableHeight)
        {
            origin = default;
            right = _console.transform.right;
            up = Vector3.up;
            forward = _console.transform.forward;
            usableWidth = 1.6f;
            usableHeight = 0.4f;

            var surface = _console.ConsoleInclinado != null
                ? _console.ConsoleInclinado
                : _console.ConsoleMeson;
            if (surface == null)
            {
                return false;
            }

            var bounds = GetWorldRendererBounds(surface);
            if (bounds.size.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            // Project extents onto console axes.
            usableWidth = Mathf.Max(0.6f,
                Vector3.Dot(bounds.size, Abs(right)) - SideMarginMeters * 2f);
            var vertical = Mathf.Max(bounds.size.y, Vector3.Dot(bounds.size, Abs(up)));
            usableHeight = Mathf.Clamp(vertical * 0.85f, MinSlotHeightMeters, MaxSlotHeightMeters);

            // Sit on the inclinada face: slight lift only, little room offset so
            // the reclined plane rests on the mesh instead of floating ahead.
            var depth = Vector3.Dot(bounds.extents, Abs(forward));
            origin = bounds.center
                     + forward * (depth * 0.12f)
                     + Vector3.up * (bounds.extents.y * 0.45f + LiftAboveSurfaceMeters);
            return true;
        }

        private void ShowDefaults()
        {
            if (_leftBank.Count > 0)
            {
                ShowInSlot(_left, 0);
            }

            if (_rightBank.Count > 0)
            {
                ShowInSlot(_right, 0);
            }
        }

        private void CycleSlot(Slot slot, int direction)
        {
            if (slot?.Bank == null || slot.Bank.Count == 0)
            {
                return;
            }

            var next = (slot.Index + direction) % slot.Bank.Count;
            if (next < 0)
            {
                next += slot.Bank.Count;
            }

            ShowInSlot(slot, next);
        }

        private bool TryShowInSlot(Slot slot, string instrumentId)
        {
            if (slot?.Bank == null)
            {
                return false;
            }

            for (var i = 0; i < slot.Bank.Count; i++)
            {
                if (slot.Bank[i] != null &&
                    string.Equals(slot.Bank[i].InstrumentId, instrumentId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowInSlot(slot, i);
                    return true;
                }
            }

            return false;
        }

        private void ShowInSlot(Slot slot, int index)
        {
            if (slot?.Bank == null || slot.Bank.Count == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, slot.Bank.Count - 1);
            slot.Index = index;

            for (var i = 0; i < slot.Bank.Count; i++)
            {
                var instrument = slot.Bank[i];
                if (instrument == null)
                {
                    continue;
                }

                instrument.SetDocked(true);
                if (i == index)
                {
                    continue;
                }

                instrument.Close();
                Park(instrument);
            }

            var active = slot.Bank[index];
            slot.Active = active;
            if (slot.Title != null)
            {
                slot.Title.text = active != null
                    ? $"{active.DisplayName}  ({index + 1}/{slot.Bank.Count})"
                    : "—";
            }

            if (active == null)
            {
                return;
            }

            if (!active.IsReady)
            {
                // Open queues until built; parent when ready next frames.
                active.Open();
                StartCoroutine(DockWhenReady(slot, active));
                return;
            }

            DockNow(slot, active);
        }

        private IEnumerator DockWhenReady(Slot slot, IBridgeDockableInstrument instrument)
        {
            const float timeout = 8f;
            var elapsed = 0f;
            while (elapsed < timeout && instrument != null && !instrument.IsReady)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (slot.Active == instrument && instrument.IsReady)
            {
                DockNow(slot, instrument);
            }
        }

        private void DockNow(Slot slot, IBridgeDockableInstrument instrument)
        {
            var root = instrument.CanvasRoot;
            if (root == null || slot.Content == null)
            {
                return;
            }

            // If LayoutSlots has not measured yet, never leave the authored 0.001
            // canvas scale (that reads as a ~2 m floating slab).
            if (slot.WidthM < 0.05f || slot.HeightM < 0.05f)
            {
                slot.WidthM = 0.72f;
                slot.HeightM = 0.42f;
            }

            root.SetParent(slot.Content, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            FitInstrument(instrument, slot.WidthM, slot.HeightM);
            instrument.Open();
        }

        private static void FitActive(Slot slot)
        {
            if (slot?.Active == null || !slot.Active.IsReady || slot.Active.CanvasRoot == null)
            {
                return;
            }

            if (slot.Active.CanvasRoot.parent != slot.Content)
            {
                return;
            }

            FitInstrument(slot.Active, slot.WidthM, slot.HeightM);
        }

        private static void FitInstrument(IBridgeDockableInstrument instrument, float widthM, float heightM)
        {
            var root = instrument.CanvasRoot;
            if (root == null || widthM < 0.05f || heightM < 0.05f)
            {
                return;
            }

            var native = instrument.NativeSizePx;
            if (native.x < 1f || native.y < 1f)
            {
                return;
            }

            // Match slot meters while preserving aspect ratio (letterbox inside slot).
            var scale = Mathf.Min(widthM / native.x, heightM / native.y);
            root.localScale = Vector3.one * scale;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            var rt = root as RectTransform;
            if (rt != null)
            {
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
        }

        private static void Park(IBridgeDockableInstrument instrument)
        {
            var root = instrument?.CanvasRoot;
            if (root == null)
            {
                return;
            }

            // Keep under the runner while hidden so destroy/reload stays simple.
            var runner = NavigationSimRunner.Instance;
            if (runner != null)
            {
                root.SetParent(runner.transform, false);
            }

            root.localPosition = new Vector3(0f, -2000f, 0f);
            root.localScale = Vector3.one * 0.001f;
        }

        private static Bounds GetWorldRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(root.position, new Vector3(1.2f, 0.35f, 0.4f));
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static float SafeInv(float v) => Mathf.Abs(v) < 1e-4f ? 1f : 1f / v;
    }
}
