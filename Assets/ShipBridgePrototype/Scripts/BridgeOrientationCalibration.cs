using System.Collections;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.UI;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Lets the user pick any room wall as the bow (Front), then confirm.
    /// Desktop uses OnGUI; headset uses a world-space canvas with Interaction SDK ray.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeOrientationCalibration : MonoBehaviour
    {
        public static BridgeOrientationCalibration Instance { get; private set; }

        [SerializeField] private BridgeRoomMapper mapper;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showWorldPanel = true;
        [SerializeField] private float restoreDotThreshold = 0.55f;
        [SerializeField] private float panelDistance = 1.1f;
        [SerializeField] [Range(0f, 1f)] private float panelHeightBetweenHandsAndHead = 0.55f;

        private MRUKRoom _room;
        private bool _needsUserConfirm;
        private GameObject _worldPanel;
        private Text _statusLabel;
        private OVRCameraRig _cameraRig;
        private Coroutine _panelPoseRoutine;
        private bool _panelPoseSettled;

        public bool NeedsUserConfirm => _needsUserConfirm;
        public float RestoreDotThreshold => restoreDotThreshold;

        private void Awake()
        {
            Instance = this;
            if (mapper == null)
            {
                mapper = FindAnyObjectByType<BridgeRoomMapper>();
            }

            _cameraRig = FindAnyObjectByType<OVRCameraRig>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            StopPanelPoseRoutine();
            DestroyWorldPanel();
        }

        public void Bind(BridgeRoomMapper owner, MRUKRoom room, bool needsConfirm)
        {
            mapper = owner;
            _room = room;
            _needsUserConfirm = needsConfirm;
            if (needsConfirm)
            {
                // Defer until head tracking is valid — first-frame pose is often at
                // the tracking origin so the panel spawns off-screen / in the floor.
                EnsureWorldPanelWhenReady();
            }
            else
            {
                StopPanelPoseRoutine();
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

        [ContextMenu("Select Next Wall As Front")]
        public void SelectNextWall() => CycleWall(+1);

        [ContextMenu("Select Previous Wall As Front")]
        public void SelectPreviousWall() => CycleWall(-1);

        /// <summary>Kept for inspector/context-menu compatibility; cycles any wall.</summary>
        [ContextMenu("Flip Front And Aft")]
        public void FlipFrontAndAft()
        {
            SelectNextWall();
        }

        private void CycleWall(int direction)
        {
            // Defer so we do not destroy the PointableCanvas mid-ray click.
            if (isActiveAndEnabled)
            {
                StartCoroutine(CycleWallRoutine(direction));
            }
            else
            {
                ApplyCycleWall(direction);
            }
        }

        private IEnumerator CycleWallRoutine(int direction)
        {
            yield return null;
            ApplyCycleWall(direction);
        }

        private void ApplyCycleWall(int direction)
        {
            if (direction < 0)
            {
                mapper?.SelectPreviousFrontWall();
            }
            else
            {
                mapper?.SelectNextFrontWall();
            }

            _needsUserConfirm = true;
            EnsureWorldPanelWhenReady();
            RefreshStatusLabel();
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
            EnsureWorldPanelWhenReady();
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

            const float w = 360f;
            const float h = 170f;
            var rect = new Rect(16f, 16f, w, h);
            GUI.Box(rect, "Select forward");
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 28f, w - 24f, h - 36f));
            GUILayout.Label(GetStatusText());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Height(36f), GUILayout.Width(70f)))
            {
                SelectPreviousWall();
            }

            if (GUILayout.Button("▶", GUILayout.Height(36f), GUILayout.Width(70f)))
            {
                SelectNextWall();
            }

            GUILayout.EndHorizontal();
            if (GUILayout.Button("Confirm", GUILayout.Height(36f)))
            {
                ConfirmCurrentFront();
            }

            GUILayout.EndArea();
        }

        private void EnsureWorldPanelWhenReady()
        {
            if (!showWorldPanel || !_needsUserConfirm)
            {
                return;
            }

            StopPanelPoseRoutine();
            if (isActiveAndEnabled)
            {
                _panelPoseRoutine = StartCoroutine(EnsureWorldPanelRoutine());
            }
            else
            {
                EnsureWorldPanel();
            }
        }

        private IEnumerator EnsureWorldPanelRoutine()
        {
            // Wait a couple of frames for OVRCameraRig / XR tracking to settle.
            yield return null;
            yield return null;

            const float timeoutSec = 3f;
            var elapsed = 0f;
            while (elapsed < timeoutSec && !TryGetReliableUserFacingPose(out _, out _))
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_needsUserConfirm)
            {
                EnsureWorldPanel();
            }

            _panelPoseRoutine = null;
        }

        private void StopPanelPoseRoutine()
        {
            if (_panelPoseRoutine != null)
            {
                StopCoroutine(_panelPoseRoutine);
                _panelPoseRoutine = null;
            }
        }

        private void LateUpdate()
        {
            if (!_needsUserConfirm || _worldPanel == null || _panelPoseSettled)
            {
                return;
            }

            if (TryGetReliableUserFacingPose(out var pos, out var rot))
            {
                _worldPanel.transform.SetPositionAndRotation(pos, rot);
                _panelPoseSettled = true;
            }
        }

        private void EnsureWorldPanel()
        {
            if (!showWorldPanel)
            {
                return;
            }

            DestroyWorldPanel();
            _panelPoseSettled = false;

            Vector3 pos;
            Quaternion rot;
            if (!TryGetReliableUserFacingPose(out pos, out rot))
            {
                // Prefer a visible bridge-relative pose over an untracked head at the
                // origin. LateUpdate snaps to the user once tracking is reliable.
                var frame = BridgeReferenceFrame.Instance;
                var pivot = frame != null ? frame.Pivot : transform;
                var fwd = frame != null ? frame.Forward : Vector3.forward;
                pos = pivot.position + Vector3.up * 1.4f + fwd * panelDistance;
                rot = Quaternion.LookRotation(fwd, Vector3.up);
            }

            _worldPanel = new GameObject("BridgeBowCalibrationPanel");
            _worldPanel.transform.SetParent(transform, false);
            _worldPanel.transform.SetPositionAndRotation(pos, rot);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_worldPanel.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(720f, 400f);
            // Match NavigationPanel scale order of magnitude so ray distance feels the same.
            canvasGo.transform.localScale = Vector3.one * 0.0012f;
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;

            // Background so the panel reads as a solid UI surface.
            CreateWorldImage(canvasGo.transform, Vector2.zero, new Vector2(720f, 400f),
                new Color(0.05f, 0.08f, 0.12f, 0.92f));

            CreateWorldLabel(canvasGo.transform, new Vector2(0f, 140f), "Select forward", 40, out _);
            CreateWorldLabel(canvasGo.transform, new Vector2(0f, 70f), GetCompactStatusText(), 26, out _statusLabel);

            CreateWorldButton(
                canvasGo.transform,
                new Vector2(-160f, -10f),
                "◀",
                SelectPreviousWall,
                new Vector2(140f, 110f),
                64);
            CreateWorldButton(
                canvasGo.transform,
                new Vector2(160f, -10f),
                "▶",
                SelectNextWall,
                new Vector2(140f, 110f),
                64);
            CreateWorldButton(
                canvasGo.transform,
                new Vector2(0f, -140f),
                "Confirm",
                ConfirmCurrentFront,
                new Vector2(420f, 80f),
                40);

            AttachRayCanvasInteraction(canvas, rt);
        }

        /// <summary>
        /// Same pose convention as <see cref="NavigationPanelSpawner"/>:
        /// panel sits in front of the head; +Z points away from the user so Unity UI (-Z) faces them.
        /// </summary>
        private bool TryGetUserFacingPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            if (!TryGetHead(out var head))
            {
                return false;
            }

            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = head.forward;
            }

            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            var height = head.position.y - 0.15f;
            if (_cameraRig != null &&
                _cameraRig.leftHandAnchor != null &&
                _cameraRig.rightHandAnchor != null)
            {
                var handY = (_cameraRig.leftHandAnchor.position.y +
                             _cameraRig.rightHandAnchor.position.y) * 0.5f;
                height = Mathf.Lerp(handY, head.position.y, panelHeightBetweenHandsAndHead);
            }

            position = head.position + forward * panelDistance;
            position.y = height;
            // +Z = away from user → GraphicRaycaster / PlaneSurface Backward face the user.
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        private bool TryGetReliableUserFacingPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!TryGetHead(out var head) || !IsHeadTrackingReliable(head))
            {
                return false;
            }

            return TryGetUserFacingPose(out position, out rotation);
        }

        private bool TryGetHead(out Transform head)
        {
            head = null;
            if (_cameraRig == null)
            {
                _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (_cameraRig != null)
            {
                head = _cameraRig.centerEyeAnchor != null
                    ? _cameraRig.centerEyeAnchor
                    : _cameraRig.transform;
            }
            else if (Camera.main != null)
            {
                head = Camera.main.transform;
            }

            return head != null;
        }

        private static bool IsHeadTrackingReliable(Transform head)
        {
            if (head == null)
            {
                return false;
            }

            // Untracked / pre-recenter poses usually sit near the floor at the origin.
            if (head.position.y < 0.5f)
            {
                return false;
            }

            try
            {
                if (OVRPlugin.initialized &&
                    !OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter) &&
                    !OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head))
                {
                    return false;
                }
            }
            catch
            {
                // OVRPlugin may throw offline; fall through to the height check above.
            }

            return true;
        }

        private void AttachRayCanvasInteraction(Canvas canvas, RectTransform canvasRt)
        {
            // Same hierarchy as ISDK Template_RayInteraction / NavigationPanel.
            var rayGo = new GameObject("ISDK_RayCanvasInteraction");
            rayGo.SetActive(false);
            var rayRt = rayGo.AddComponent<RectTransform>();
            rayRt.SetParent(canvasRt, false);
            rayRt.anchorMin = Vector2.zero;
            rayRt.anchorMax = Vector2.one;
            rayRt.pivot = new Vector2(0.5f, 0.5f);
            rayRt.offsetMin = Vector2.zero;
            rayRt.offsetMax = Vector2.zero;
            rayRt.localScale = Vector3.one;
            rayRt.localRotation = Quaternion.identity;
            rayRt.localPosition = Vector3.zero;

            var pointable = rayGo.AddComponent<PointableCanvas>();
            pointable.InjectCanvas(canvas);

            var surfaceGo = new GameObject("Surface");
            surfaceGo.SetActive(false);
            var surfaceRt = surfaceGo.AddComponent<RectTransform>();
            surfaceRt.SetParent(rayRt, false);
            surfaceRt.anchorMin = Vector2.zero;
            surfaceRt.anchorMax = Vector2.one;
            surfaceRt.pivot = new Vector2(0.5f, 0.5f);
            surfaceRt.offsetMin = Vector2.zero;
            surfaceRt.offsetMax = Vector2.zero;
            surfaceRt.localScale = Vector3.one;
            surfaceRt.localRotation = Quaternion.identity;
            surfaceRt.localPosition = Vector3.zero;

            var plane = surfaceGo.AddComponent<PlaneSurface>();
            plane.Facing = PlaneSurface.NormalFacing.Backward;
            plane.DoubleSided = false;

            var clipper = surfaceGo.AddComponent<BoundsClipper>();
            // Stretch fill → rect matches canvas sizeDelta after parenting.
            Canvas.ForceUpdateCanvases();
            var rect = surfaceRt.rect;
            var width = rect.width > 1f ? rect.width : canvasRt.sizeDelta.x;
            var height = rect.height > 1f ? rect.height : canvasRt.sizeDelta.y;
            clipper.Size = new Vector3(width, height, 0.01f);

            var clipped = surfaceGo.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });

            // Keep clipper bounds in sync with the RectTransform (ISDK template does this).
            var driver = surfaceGo.AddComponent<RectTransformBoundsClipperDriver>();
            SetBoundsClipperDriver(driver, clipper);

            surfaceGo.SetActive(true);

            var rayInteractable = rayGo.AddComponent<RayInteractable>();
            rayInteractable.InjectAllRayInteractable(clipped);
            rayInteractable.InjectOptionalPointableElement(pointable);
            rayInteractable.InjectOptionalSelectSurface(plane);
            rayGo.SetActive(true);
        }

        private static void SetBoundsClipperDriver(
            RectTransformBoundsClipperDriver driver,
            BoundsClipper clipper)
        {
            // No public Inject API on the driver; wire the serialized field at runtime.
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var field = typeof(RectTransformBoundsClipperDriver).GetField("_boundsClipper", flags);
            field?.SetValue(driver, clipper);
            var resize = typeof(RectTransformBoundsClipperDriver).GetMethod("Resize", flags);
            resize?.Invoke(driver, null);
        }

        private string GetStatusText()
        {
            if (mapper == null)
            {
                return "Wall: (none)";
            }

            var index = mapper.GetFrontWallSelectionIndex(out var count);
            var name = mapper.GetFrontWallDisplayName();
            if (index < 0 || count <= 0)
            {
                return $"Wall: {name}";
            }

            return $"Wall {index + 1}/{count}: {name}";
        }

        private string GetCompactStatusText()
        {
            if (mapper == null)
            {
                return "—";
            }

            var index = mapper.GetFrontWallSelectionIndex(out var count);
            if (index < 0 || count <= 0)
            {
                return "—";
            }

            return $"{index + 1} / {count}";
        }

        private void RefreshStatusLabel()
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = GetCompactStatusText();
            }
        }

        private static void CreateWorldImage(
            Transform parent,
            Vector2 anchoredPos,
            Vector2 size,
            Color color)
        {
            var go = new GameObject("Background");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void CreateWorldLabel(
            Transform parent,
            Vector2 anchoredPos,
            string label,
            int fontSize,
            out Text text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(740f, 50f);
            rt.anchoredPosition = anchoredPos;

            text = go.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.raycastTarget = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        private static void CreateWorldButton(
            Transform parent,
            Vector2 anchoredPos,
            string label,
            UnityEngine.Events.UnityAction action,
            Vector2 size,
            int fontSize)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.12f, 0.18f, 0.28f, 0.92f);
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.raycastTarget = false;
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
                _statusLabel = null;
            }
        }
    }
}
