using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Instantiates / places <see cref="BridgeConsoleController"/> against the
    /// bridge Front wall (or MRUK key wall) once the room and reference frame exist.
    /// Verified against MRUK GetKeyWall placement pattern in Meta docs.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeConsolePlacer : MonoBehaviour
    {
        public const string ResourcesModelPath = "ship_bridge_vr/Console_Bridge";
        public const string PrefabResourcesPath = "ship_bridge_vr/BridgeConsole";

        [Header("Source")]
        [Tooltip("Preferred prefab. Falls back to Resources FBX model if null.")]
        [SerializeField] private BridgeConsoleController consolePrefab;
        [SerializeField] private bool preferFrontWallFromReferenceFrame = true;
        [SerializeField] private bool placeOnBridgeGenerated = true;
        [SerializeField] private bool destroyPreviousOnReplace = true;

        [Header("Runtime")]
        [SerializeField] private BridgeConsoleController activeConsole;

        private BridgeRoomMapper _mapper;
        private bool _mrukBound;
        private Coroutine _placeRetryRoutine;
        private bool _placedThisSession;

        public BridgeConsoleController ActiveConsole => activeConsole;

        private void OnEnable()
        {
            BindMapper();
            BindMruk();
            // Bootstrap runs AfterSceneLoad — BridgeGenerated may have already fired.
            TryPlaceCatchUp();
        }

        private void Start()
        {
            BindMapper();
            BindMruk();
            TryPlaceCatchUp();
        }

        private void OnDisable()
        {
            if (_placeRetryRoutine != null)
            {
                StopCoroutine(_placeRetryRoutine);
                _placeRetryRoutine = null;
            }

            UnbindMapper();
            UnbindMruk();
        }

        private void BindMapper()
        {
            if (_mapper == null)
            {
                _mapper = FindAnyObjectByType<BridgeRoomMapper>();
            }

            if (_mapper != null)
            {
                _mapper.BridgeGenerated -= OnBridgeGenerated;
                _mapper.BridgeGenerated += OnBridgeGenerated;
            }
        }

        private void UnbindMapper()
        {
            if (_mapper != null)
            {
                _mapper.BridgeGenerated -= OnBridgeGenerated;
            }
        }

        private void BindMruk()
        {
            if (_mrukBound || MRUK.Instance == null)
            {
                return;
            }

            MRUK.Instance.RegisterSceneLoadedCallback(OnMrukSceneLoaded);
            _mrukBound = true;
        }

        private void UnbindMruk()
        {
            if (!_mrukBound || MRUK.Instance == null)
            {
                _mrukBound = false;
                return;
            }

            MRUK.Instance.SceneLoadedEvent.RemoveListener(OnMrukSceneLoaded);
            _mrukBound = false;
        }

        private void OnMrukSceneLoaded()
        {
            // BridgeRoomMapper may place after this callback; BridgeGenerated is the
            // authoritative hook. Still try once in case mapper is disabled.
            if (placeOnBridgeGenerated && _mapper != null && _mapper.isActiveAndEnabled)
            {
                return;
            }

            PlaceOrRebuild();
        }

        private void OnBridgeGenerated()
        {
            if (placeOnBridgeGenerated)
            {
                PlaceOrRebuild();
            }
        }

        private void TryPlaceCatchUp()
        {
            if (!placeOnBridgeGenerated)
            {
                return;
            }

            if (IsRoomReady() && BridgeReferenceFrame.Instance != null)
            {
                PlaceOrRebuild();
                return;
            }

            SchedulePlaceRetry();
        }

        private void SchedulePlaceRetry()
        {
            if (!isActiveAndEnabled || _placeRetryRoutine != null || _placedThisSession)
            {
                return;
            }

            _placeRetryRoutine = StartCoroutine(PlaceWhenReadyRoutine());
        }

        private IEnumerator PlaceWhenReadyRoutine()
        {
            const float timeoutSec = 5f;
            var elapsed = 0f;
            while (elapsed < timeoutSec && !_placedThisSession)
            {
                if (IsRoomReady() && BridgeReferenceFrame.Instance != null)
                {
                    PlaceOrRebuild();
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _placeRetryRoutine = null;
        }

        [ContextMenu("Place Or Rebuild Console")]
        public void PlaceOrRebuild()
        {
            if (!IsRoomReady())
            {
                Debug.LogWarning("[BridgeConsolePlacer] MRUK room not ready yet.");
                SchedulePlaceRetry();
                return;
            }

            var room = MRUK.Instance.GetCurrentRoom();
            if (room == null)
            {
                Debug.LogWarning("[BridgeConsolePlacer] No current MRUK room.");
                SchedulePlaceRetry();
                return;
            }

            if (!TryResolveWall(room, out var wall, out var wallWidth, out var inward))
            {
                Debug.LogWarning("[BridgeConsolePlacer] Could not resolve a main wall.");
                SchedulePlaceRetry();
                return;
            }

            var console = EnsureConsoleInstance();
            if (console == null)
            {
                SchedulePlaceRetry();
                return;
            }

            console.EnsureHierarchy();

            var height = console.GetComponent<BridgeConsoleHeightHandle>();
            if (height == null)
            {
                height = console.gameObject.AddComponent<BridgeConsoleHeightHandle>();
            }

            var floorY = ResolveFloorY(room);
            var wallCenter = wall.GetAnchorCenter();
            console.PlaceAgainstWall(wallCenter, inward, floorY, wallWidth);

            // Wire grab AFTER width scale / placement so the collider matches
            // Console_Handle (or the desk lip proxy) at the final pose.
            height.EnsureInteractable();
            _placedThisSession = true;

            Debug.Log(
                $"[BridgeConsolePlacer] Placed console on '{wall.name}' " +
                $"width={wallWidth:F2}m floorY={floorY:F2} inward={inward}. " +
                "Height handle on Console_Handle (proximity + distance grab + grip fallback).",
                console);
        }

        private BridgeConsoleController EnsureConsoleInstance()
        {
            if (activeConsole != null)
            {
                if (destroyPreviousOnReplace)
                {
                    // Reuse the live instance — just re-place it.
                    return activeConsole;
                }

                return activeConsole;
            }

            BridgeConsoleController instance = null;

            if (consolePrefab != null)
            {
                instance = Instantiate(consolePrefab);
            }
            else
            {
                var prefab = Resources.Load<GameObject>(PrefabResourcesPath);
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    instance = go.GetComponent<BridgeConsoleController>() ??
                               go.AddComponent<BridgeConsoleController>();
                }
                else
                {
                    var model = Resources.Load<GameObject>(ResourcesModelPath);
                    if (model == null)
                    {
                        Debug.LogError(
                            "[BridgeConsolePlacer] Missing Resources/" +
                            ResourcesModelPath + " and no consolePrefab assigned.",
                            this);
                        return null;
                    }

                    var go = Instantiate(model);
                    // FBX root may already be named Console_Bridge.
                    go.name = "Console_Bridge";
                    instance = go.GetComponent<BridgeConsoleController>() ??
                               go.AddComponent<BridgeConsoleController>();
                }
            }

            instance.name = "Console_Bridge";
            activeConsole = instance;
            return instance;
        }

        private bool TryResolveWall(
            MRUKRoom room,
            out MRUKAnchor wall,
            out float wallWidth,
            out Vector3 inward)
        {
            wall = null;
            wallWidth = 0f;
            inward = Vector3.forward;

            var frame = BridgeReferenceFrame.Instance;
            if (preferFrontWallFromReferenceFrame &&
                frame != null &&
                frame.FrontWall != null &&
                frame.FrontWall.PlaneRect.HasValue &&
                frame.FrontWall.PlaneRect.Value.width > 0.05f)
            {
                wall = frame.FrontWall;
                wallWidth = wall.PlaneRect.Value.width;
                // Facing direction points into the room (see BridgeRoomMapper).
                inward = Flatten(room.GetFacingDirection(wall));
                if (inward.sqrMagnitude < 1e-6f)
                {
                    inward = -frame.Forward;
                }

                inward.Normalize();
                return true;
            }

            // Meta MRUK pattern: longest unobstructed wall.
            // Source: developers.meta.com — MR Utility Kit manage scene data.
            Vector2 wallScale;
            wall = room.GetKeyWall(out wallScale);
            if (wall == null)
            {
                return false;
            }

            wallWidth = wallScale.x > 0.05f ? wallScale.x : wall.PlaneRect?.width ?? 0f;
            inward = Flatten(room.GetFacingDirection(wall));
            if (inward.sqrMagnitude < 1e-6f)
            {
                inward = Flatten(-wall.transform.forward);
            }

            if (inward.sqrMagnitude < 1e-6f)
            {
                inward = Vector3.forward;
            }

            inward.Normalize();
            return wallWidth > 0.05f;
        }

        private static float ResolveFloorY(MRUKRoom room)
        {
            if (room.FloorAnchor != null)
            {
                return room.FloorAnchor.transform.position.y;
            }

            return room.GetRoomBounds().min.y;
        }

        private static bool IsRoomReady()
        {
            return MRUK.Instance != null &&
                   MRUK.Instance.IsInitialized &&
                   MRUK.Instance.Rooms != null &&
                   MRUK.Instance.Rooms.Count > 0;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var mapper = FindAnyObjectByType<BridgeRoomMapper>();
            if (mapper == null)
            {
                return;
            }

            var systems = GameObject.Find("ShipBridgeSystems");
            if (systems == null)
            {
                systems = mapper.gameObject;
            }

            if (systems.GetComponent<BridgeConsolePlacer>() == null)
            {
                systems.AddComponent<BridgeConsolePlacer>();
            }
        }
    }
}
