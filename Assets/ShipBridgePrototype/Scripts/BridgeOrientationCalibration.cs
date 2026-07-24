using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Confirms which long wall is the bow. Camera look is only a first-time proposal;
    /// the confirmed frame is persisted and restored on later loads.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeOrientationCalibration : MonoBehaviour
    {
        public static BridgeOrientationCalibration Instance { get; private set; }

        [SerializeField] private BridgeRoomMapper mapper;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showWorldPanel = true;
        [SerializeField] private float restoreDotThreshold = 0.55f;

        private MRUKRoom _room;
        private bool _needsUserConfirm;
        private GameObject _worldPanel;

        public bool NeedsUserConfirm => _needsUserConfirm;
        public float RestoreDotThreshold => restoreDotThreshold;

        private void Awake()
        {
            Instance = this;
            if (mapper == null)
            {
                mapper = FindAnyObjectByType<BridgeRoomMapper>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DestroyWorldPanel();
        }

        public void Bind(BridgeRoomMapper owner, MRUKRoom room, bool needsConfirm)
        {
            mapper = owner;
            _room = room;
            _needsUserConfirm = needsConfirm;
            if (needsConfirm)
            {
                EnsureWorldPanel();
            }
            else
            {
                DestroyWorldPanel();
            }
        }

        [ContextMenu("Confirm Current Front")]
        public void ConfirmCurrentFront()
        {
            if (mapper == null)
            {
                return;
            }

            mapper.ConfirmFrontCalibration();
            _needsUserConfirm = false;
            DestroyWorldPanel();
        }

        [ContextMenu("Flip Front And Aft")]
        public void FlipFrontAndAft()
        {
            mapper?.FlipFrontAndAft();
            _needsUserConfirm = true;
            EnsureWorldPanel();
        }

        [ContextMenu("Reset Calibration")]
        public void ResetCalibration()
        {
            if (_room != null)
            {
                BridgeCalibrationStore.ClearForRoom(_room);
            }
            else
            {
                BridgeCalibrationStore.ClearAll();
            }

            var frame = BridgeReferenceFrame.Instance;
            frame?.SetCalibrated(false);
            _needsUserConfirm = true;
            EnsureWorldPanel();
            Debug.Log("[BridgeOrientationCalibration] Calibration cleared for room.");
        }

        [ContextMenu("Recalibrate")]
        public void Recalibrate()
        {
            ResetCalibration();
            mapper?.RegenerateWithProposedFront();
        }

        private void OnGUI()
        {
            if (!showOverlay || !_needsUserConfirm)
            {
                return;
            }

            const float w = 420f;
            const float h = 150f;
            var rect = new Rect(16f, 16f, w, h);
            GUI.Box(rect, "Calibración de proa (pared larga)");
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, w - 24f, h - 36f));
            GUILayout.Label("Confirma cuál pared larga es la proa del puente.");
            if (GUILayout.Button("Confirmar esta pared como proa", GUILayout.Height(32f)))
            {
                ConfirmCurrentFront();
            }

            if (GUILayout.Button("Invertir proa y popa", GUILayout.Height(28f)))
            {
                FlipFrontAndAft();
            }

            if (GUILayout.Button("Borrar calibración guardada", GUILayout.Height(24f)))
            {
                ResetCalibration();
            }

            GUILayout.EndArea();
        }

        private void EnsureWorldPanel()
        {
            if (!showWorldPanel)
            {
                return;
            }

            DestroyWorldPanel();

            var frame = BridgeReferenceFrame.Instance;
            var pivot = frame != null ? frame.Pivot : transform;
            var fwd = frame != null ? frame.Forward : Vector3.forward;
            var pos = pivot.position + Vector3.up * 1.4f + fwd * 0.6f;

            _worldPanel = new GameObject("BridgeBowCalibrationPanel");
            _worldPanel.transform.SetParent(transform, false);
            _worldPanel.transform.position = pos;
            if (frame != null)
            {
                _worldPanel.transform.rotation = Quaternion.LookRotation(-frame.Forward, Vector3.up);
            }

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_worldPanel.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800f, 360f);
            canvasGo.transform.localScale = Vector3.one * 0.0015f;

            CreateWorldButton(canvasGo.transform, new Vector2(0f, 60f), "Confirmar proa", ConfirmCurrentFront);
            CreateWorldButton(canvasGo.transform, new Vector2(0f, -20f), "Invertir proa/popa", FlipFrontAndAft);
            CreateWorldButton(canvasGo.transform, new Vector2(0f, -100f), "Borrar calibración", ResetCalibration);
        }

        private static void CreateWorldButton(Transform parent, Vector2 anchoredPos, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(700f, 70f);
            rt.anchoredPosition = anchoredPos;

            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.12f, 0.18f, 0.28f, 0.92f);

            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(action);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<UnityEngine.UI.Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 36;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        private void DestroyWorldPanel()
        {
            if (_worldPanel != null)
            {
                Destroy(_worldPanel);
                _worldPanel = null;
            }
        }
    }
}
