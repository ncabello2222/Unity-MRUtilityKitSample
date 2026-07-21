using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Converts a loaded MRUK room into a 1:1 ship-bridge interior built from Unity primitives.
    /// </summary>
    public class BridgeRoomMapper : MonoBehaviour
    {
        public enum WallRole
        {
            Front,
            Aft,
            Port,
            Starboard,
            SolidExtra
        }

        [Header("Generation")]
        [SerializeField] private bool generateOnSceneLoaded = true;
        [SerializeField] private bool clearExistingBeforeGenerate = true;

        [Header("Wall / Window")]
        [SerializeField] private float wallThickness = 0.06f;
        [SerializeField] private float windowWaistHeight = 0.9f;
        [SerializeField] private float windowTopClearance = 0.12f;
        [SerializeField] private float frontWindowSideMargin = 0.08f;
        [SerializeField] private float windowFrameThickness = 0.04f;
        [SerializeField] private float windowFrameDepth = 0.08f;
        [SerializeField] [Range(0.05f, 0.95f)] private float sideWindowLengthRatio = 0.5f;

        [Header("Materials")]
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material ceilingMaterial;
        [SerializeField] private Material windowFrameMaterial;
        [SerializeField] private Material windowGlassMaterial;
        [SerializeField] private Material roomObjectMaterial;
        [SerializeField] private Material exteriorGroundMaterial;
        [SerializeField] private Material exteriorMountainMaterial;
        [SerializeField] private Material exteriorWaterMaterial;

        [Header("Exterior")]
        [SerializeField] private bool generateExterior = true;
        [SerializeField] private bool disablePassthroughForExterior = true;
        [SerializeField] private float exteriorTerrainSize = 220f;
        [SerializeField] private float exteriorTerrainHeight = 45f;
        [SerializeField] private float exteriorDistanceFromRoom = 18f;

        [Header("Gizmos")]
        [SerializeField] private bool drawRoleGizmos = true;
        [SerializeField] private float gizmoArrowLength = 0.6f;

        private Transform _generatedRoot;
        private readonly Dictionary<WallRole, MRUKAnchor> _classifiedWalls = new();
        private readonly List<(WallRole role, Vector3 center, Vector3 inward)> _gizmoWalls = new();
        private bool _mrukBound;
        private bool _passthroughWasEnabled;

        public Transform GeneratedRoot => _generatedRoot;
        public IReadOnlyDictionary<WallRole, MRUKAnchor> ClassifiedWalls => _classifiedWalls;

        private void OnEnable()
        {
            BindMruk();
        }

        private void Start()
        {
            // MRUK may spawn/initialize after this component enables.
            BindMruk();
            if (generateOnSceneLoaded && IsRoomReady())
            {
                GenerateBridge();
            }
        }

        private void OnDisable()
        {
            UnbindMruk();
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
            if (generateOnSceneLoaded)
            {
                GenerateBridge();
            }
        }

        private static bool IsRoomReady()
        {
            return MRUK.Instance != null &&
                   MRUK.Instance.IsInitialized &&
                   MRUK.Instance.Rooms != null &&
                   MRUK.Instance.Rooms.Count > 0;
        }

        [ContextMenu("Generate Bridge")]
        public void GenerateBridge()
        {
            if (MRUK.Instance == null)
            {
                Debug.LogWarning("[BridgeRoomMapper] MRUK.Instance is null.");
                return;
            }

            var room = MRUK.Instance.GetCurrentRoom();
            if (room == null)
            {
                Debug.LogWarning("[BridgeRoomMapper] No current MRUK room available yet.");
                return;
            }

            if (clearExistingBeforeGenerate)
            {
                ClearGenerated();
            }

            _classifiedWalls.Clear();
            _gizmoWalls.Clear();

            var rootGo = new GameObject("BridgeGeneratedRoot");
            _generatedRoot = rootGo.transform;
            _generatedRoot.SetParent(transform, false);

            ClassifyWalls(room);
            CreateFloorAndCeiling(room, _generatedRoot);
            CreateBridgeWalls(room, _generatedRoot);
            CreateRoomObjectProxies(room, _generatedRoot);
            CreateRoleMarkers(_generatedRoot);

            if (generateExterior)
            {
                CreateExteriorEnvironment(room, _generatedRoot);
                ApplyPassthroughForExterior(false);
            }

            Debug.Log(
                $"[BridgeRoomMapper] Bridge generated. Front={NameOf(WallRole.Front)}, Aft={NameOf(WallRole.Aft)}, " +
                $"Port={NameOf(WallRole.Port)}, Starboard={NameOf(WallRole.Starboard)}");
        }

        [ContextMenu("Clear Generated Bridge")]
        public void ClearGenerated()
        {
            // DestroyImmediate so regeneration in the same frame cannot leave duplicates.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name == "BridgeGeneratedRoot")
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            _generatedRoot = null;
            _classifiedWalls.Clear();
            _gizmoWalls.Clear();
            ApplyPassthroughForExterior(true);
        }

        private string NameOf(WallRole role)
        {
            return _classifiedWalls.TryGetValue(role, out var anchor) && anchor != null
                ? anchor.name
                : "(none)";
        }

        private void ClassifyWalls(MRUKRoom room)
        {
            var walls = GetBridgeWallCandidates(room);
            if (walls.Count == 0)
            {
                Debug.LogWarning("[BridgeRoomMapper] Room has no wall anchors.");
                return;
            }

            var sorted = new List<MRUKAnchor>(walls);
            // MRUK SortWallsByWidth sorts shortest→longest; we only need a mutable copy + our own ranking.
            sorted = MRUKRoom.SortWallsByWidth(sorted);
            sorted.Sort((a, b) => GetWallWidth(b).CompareTo(GetWallWidth(a)));

            if (sorted.Count < 2)
            {
                Debug.LogWarning("[BridgeRoomMapper] Need at least two walls to classify front/aft.");
                return;
            }

            // Prefer the widest opposite-facing pair (true fore/aft on rectangular rooms;
            // more stable than raw top-2 widths on irregular/L-shaped rooms).
            if (!TryPickForeAftPair(room, sorted, out var longA, out var longB))
            {
                longA = sorted[0];
                longB = sorted[1];
            }

            var front = ChooseFrontWall(longA, longB);
            var aft = front == longA ? longB : longA;

            _classifiedWalls[WallRole.Front] = front;
            _classifiedWalls[WallRole.Aft] = aft;

            var roomCenter = room.GetRoomBounds().center;
            var toFront = Flatten(GetAnchorWorldCenter(front) - roomCenter);
            if (toFront.sqrMagnitude < 1e-6f)
            {
                toFront = Flatten(-room.GetFacingDirection(front));
            }

            toFront.Normalize();
            var right = Vector3.Cross(Vector3.up, toFront).normalized;

            MRUKAnchor port = null;
            MRUKAnchor starboard = null;
            var bestPort = float.PositiveInfinity;
            var bestStarboard = float.NegativeInfinity;

            foreach (var wall in sorted)
            {
                if (wall == front || wall == aft)
                {
                    continue;
                }

                var side = Vector3.Dot(Flatten(GetAnchorWorldCenter(wall) - roomCenter), right);
                if (side < bestPort)
                {
                    bestPort = side;
                    port = wall;
                }

                if (side > bestStarboard)
                {
                    bestStarboard = side;
                    starboard = wall;
                }
            }

            if (port != null)
            {
                _classifiedWalls[WallRole.Port] = port;
            }

            if (starboard != null && starboard != port)
            {
                _classifiedWalls[WallRole.Starboard] = starboard;
            }

            foreach (var wall in walls)
            {
                if (wall == front || wall == aft || wall == port || wall == starboard)
                {
                    continue;
                }

                _gizmoWalls.Add((WallRole.SolidExtra, GetAnchorWorldCenter(wall), room.GetFacingDirection(wall)));
            }

            CacheGizmo(WallRole.Front, front, room);
            CacheGizmo(WallRole.Aft, aft, room);
            if (port != null)
            {
                CacheGizmo(WallRole.Port, port, room);
            }

            if (starboard != null && starboard != port)
            {
                CacheGizmo(WallRole.Starboard, starboard, room);
            }
        }

        private static bool TryPickForeAftPair(
            MRUKRoom room,
            List<MRUKAnchor> wallsSortedWideFirst,
            out MRUKAnchor wallA,
            out MRUKAnchor wallB)
        {
            wallA = null;
            wallB = null;
            var bestScore = float.NegativeInfinity;

            for (var i = 0; i < wallsSortedWideFirst.Count; i++)
            {
                var a = wallsSortedWideFirst[i];
                var fa = Flatten(room.GetFacingDirection(a));
                if (fa.sqrMagnitude < 1e-6f)
                {
                    continue;
                }

                fa.Normalize();

                for (var j = i + 1; j < wallsSortedWideFirst.Count; j++)
                {
                    var b = wallsSortedWideFirst[j];
                    var fb = Flatten(room.GetFacingDirection(b));
                    if (fb.sqrMagnitude < 1e-6f)
                    {
                        continue;
                    }

                    fb.Normalize();

                    // Opposite-facing walls have near -1 forward dot.
                    var opposite = Vector3.Dot(fa, fb);
                    if (opposite > -0.5f)
                    {
                        continue;
                    }

                    var score = GetWallWidth(a) + GetWallWidth(b) + (-opposite);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        wallA = a;
                        wallB = b;
                    }
                }
            }

            return wallA != null && wallB != null;
        }

        private static MRUKAnchor ChooseFrontWall(MRUKAnchor a, MRUKAnchor b)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                // Fallback: wall whose inward normal most opposes world +Z (arbitrary but stable).
                var scoreA = Vector3.Dot(Flatten(a.transform.forward), Vector3.forward);
                var scoreB = Vector3.Dot(Flatten(b.transform.forward), Vector3.forward);
                return scoreA <= scoreB ? a : b;
            }

            var origin = cam.transform.position;
            var look = Flatten(cam.transform.forward);
            if (look.sqrMagnitude < 1e-6f)
            {
                look = Vector3.forward;
            }
            else
            {
                look.Normalize();
            }

            var toA = Flatten(GetAnchorWorldCenter(a) - origin);
            var toB = Flatten(GetAnchorWorldCenter(b) - origin);
            if (toA.sqrMagnitude > 1e-6f)
            {
                toA.Normalize();
            }

            if (toB.sqrMagnitude > 1e-6f)
            {
                toB.Normalize();
            }

            // The long wall the user is looking toward when load completes.
            return Vector3.Dot(look, toA) >= Vector3.Dot(look, toB) ? a : b;
        }

        private void CacheGizmo(WallRole role, MRUKAnchor wall, MRUKRoom room)
        {
            if (wall == null)
            {
                return;
            }

            var inward = room.GetFacingDirection(wall);
            if (inward.sqrMagnitude < 1e-6f)
            {
                inward = -wall.transform.forward;
            }

            _gizmoWalls.Add((role, GetAnchorWorldCenter(wall), inward.normalized));
        }

        private void CreateFloorAndCeiling(MRUKRoom room, Transform root)
        {
            var structure = new GameObject("Structure").transform;
            structure.SetParent(root, false);

            CreatePlaneSurface("Floor", room.FloorAnchor, structure, floorMaterial, true);
            CreatePlaneSurface("Ceiling", room.CeilingAnchor, structure, ceilingMaterial, false);
        }

        private void CreatePlaneSurface(string name, MRUKAnchor anchor, Transform parent, Material material, bool isFloor)
        {
            if (anchor == null || !anchor.PlaneRect.HasValue)
            {
                return;
            }

            var rect = anchor.PlaneRect.Value;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);

            // Plane is local XY; give a thin slab along local Z.
            const float thickness = 0.04f;
            var localCenter = new Vector3(rect.center.x, rect.center.y, isFloor ? -thickness * 0.5f : thickness * 0.5f);
            go.transform.position = anchor.transform.TransformPoint(localCenter);
            go.transform.rotation = anchor.transform.rotation;
            go.transform.localScale = new Vector3(rect.width, rect.height, thickness);
            ApplyMaterial(go, material);
        }

        private void CreateBridgeWalls(MRUKRoom room, Transform root)
        {
            var wallsRoot = new GameObject("Walls").transform;
            wallsRoot.SetParent(root, false);

            foreach (var pair in _classifiedWalls)
            {
                CreateWallGeometry(pair.Key, pair.Value, wallsRoot);
            }

            // Any unclassified bridge-wall candidates (extra) as full solids.
            foreach (var wall in GetBridgeWallCandidates(room))
            {
                var classified = false;
                foreach (var known in _classifiedWalls.Values)
                {
                    if (known == wall)
                    {
                        classified = true;
                        break;
                    }
                }

                if (!classified)
                {
                    CreateSolidWall(wall, wallsRoot, "ExtraWall");
                }
            }
        }

        private static List<MRUKAnchor> GetBridgeWallCandidates(MRUKRoom room)
        {
            var result = new List<MRUKAnchor>();
            if (room?.WallAnchors == null)
            {
                return result;
            }

            // Prefer real wall faces. Ignore invisible/opening walls so mapped doors/windows
            // do not drive bridge layout; the bridge creates its own window band.
            foreach (var wall in room.WallAnchors)
            {
                if (wall == null || !wall.PlaneRect.HasValue)
                {
                    continue;
                }

                if (wall.Label.HasFlag(MRUKAnchor.SceneLabels.WALL_FACE))
                {
                    result.Add(wall);
                }
            }

            if (result.Count >= 4)
            {
                return result;
            }

            // Fallback for sparse rooms: allow invisible wall faces only if needed.
            foreach (var wall in room.WallAnchors)
            {
                if (wall == null || !wall.PlaneRect.HasValue || result.Contains(wall))
                {
                    continue;
                }

                if (wall.Label.HasFlag(MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE) ||
                    wall.Label.HasFlag(MRUKAnchor.SceneLabels.INNER_WALL_FACE))
                {
                    result.Add(wall);
                }
            }

            return result;
        }

        private void CreateWallGeometry(WallRole role, MRUKAnchor wall, Transform parent)
        {
            if (wall == null || !wall.PlaneRect.HasValue)
            {
                return;
            }

            switch (role)
            {
                case WallRole.Front:
                    CreateFrontWindowWall(wall, parent);
                    break;
                case WallRole.Port:
                case WallRole.Starboard:
                    CreateSideWindowWall(wall, parent, role);
                    break;
                case WallRole.Aft:
                case WallRole.SolidExtra:
                default:
                    CreateSolidWall(wall, parent, role.ToString());
                    break;
            }
        }

        private void CreateSolidWall(MRUKAnchor wall, Transform parent, string label)
        {
            var rect = wall.PlaneRect.Value;
            var wallRoot = new GameObject($"Wall_{label}").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            CreateWallPanel(
                wallRoot,
                "Solid",
                new Vector3(rect.center.x, rect.center.y, -wallThickness * 0.5f),
                new Vector3(rect.width, rect.height, wallThickness),
                wallMaterial);
        }

        private void CreateFrontWindowWall(MRUKAnchor wall, Transform parent)
        {
            var rect = wall.PlaneRect.Value;
            var wallRoot = new GameObject("Wall_Front").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            var halfW = rect.width * 0.5f;
            var bottomY = rect.yMin;
            var topY = rect.yMax;

            var windowBottom = Mathf.Clamp(bottomY + windowWaistHeight, bottomY + 0.05f, topY - 0.2f);
            var windowTop = Mathf.Clamp(topY - windowTopClearance, windowBottom + 0.2f, topY - 0.02f);
            var margin = Mathf.Clamp(frontWindowSideMargin, 0f, halfW * 0.45f);
            var windowLeft = rect.xMin + margin;
            var windowRight = rect.xMax - margin;
            var windowWidth = windowRight - windowLeft;
            var windowHeight = windowTop - windowBottom;
            var windowCenterX = (windowLeft + windowRight) * 0.5f;
            var windowCenterY = (windowBottom + windowTop) * 0.5f;
            var z = -wallThickness * 0.5f;

            // Bottom bulkhead under the window.
            CreateWallPanel(
                wallRoot,
                "Bottom",
                new Vector3(rect.center.x, (bottomY + windowBottom) * 0.5f, z),
                new Vector3(rect.width, windowBottom - bottomY, wallThickness),
                wallMaterial);

            // Top strip above the window (near ceiling).
            var topStripHeight = topY - windowTop;
            if (topStripHeight > 0.01f)
            {
                CreateWallPanel(
                    wallRoot,
                    "Top",
                    new Vector3(rect.center.x, (windowTop + topY) * 0.5f, z),
                    new Vector3(rect.width, topStripHeight, wallThickness),
                    wallMaterial);
            }

            // Left / right pillars (narrow margins) — keep opening continuous; only if margin > 0.
            if (margin > 0.01f)
            {
                CreateWallPanel(
                    wallRoot,
                    "LeftPillar",
                    new Vector3((rect.xMin + windowLeft) * 0.5f, windowCenterY, z),
                    new Vector3(margin, windowHeight, wallThickness),
                    wallMaterial);
                CreateWallPanel(
                    wallRoot,
                    "RightPillar",
                    new Vector3((windowRight + rect.xMax) * 0.5f, windowCenterY, z),
                    new Vector3(margin, windowHeight, wallThickness),
                    wallMaterial);
            }

            CreateWindowOpening(
                wallRoot,
                new Vector3(windowCenterX, windowCenterY, z),
                windowWidth,
                windowHeight);
        }

        private void CreateSideWindowWall(MRUKAnchor wall, Transform parent, WallRole role)
        {
            var rect = wall.PlaneRect.Value;
            var wallRoot = new GameObject($"Wall_{role}").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            if (!_classifiedWalls.TryGetValue(WallRole.Front, out var front) || front == null)
            {
                CreateWallPanel(
                    wallRoot,
                    "Solid",
                    new Vector3(rect.center.x, rect.center.y, -wallThickness * 0.5f),
                    new Vector3(rect.width, rect.height, wallThickness),
                    wallMaterial);
                return;
            }

            var bottomY = rect.yMin;
            var topY = rect.yMax;
            var windowBottom = Mathf.Clamp(bottomY + windowWaistHeight, bottomY + 0.05f, topY - 0.2f);
            var windowTop = Mathf.Clamp(topY - windowTopClearance, windowBottom + 0.2f, topY - 0.02f);
            var windowHeight = windowTop - windowBottom;
            var windowCenterY = (windowBottom + windowTop) * 0.5f;
            var z = -wallThickness * 0.5f;

            // Which local-X end faces the front wall?
            var leftWorld = wall.transform.TransformPoint(new Vector3(rect.xMin, rect.center.y, 0f));
            var rightWorld = wall.transform.TransformPoint(new Vector3(rect.xMax, rect.center.y, 0f));
            var frontCenter = GetAnchorWorldCenter(front);
            var frontIsTowardLocalMinX = (leftWorld - frontCenter).sqrMagnitude <= (rightWorld - frontCenter).sqrMagnitude;

            var windowLength = Mathf.Clamp01(sideWindowLengthRatio) * rect.width;
            float windowMinX;
            float windowMaxX;
            if (frontIsTowardLocalMinX)
            {
                windowMinX = rect.xMin;
                windowMaxX = rect.xMin + windowLength;
            }
            else
            {
                windowMaxX = rect.xMax;
                windowMinX = rect.xMax - windowLength;
            }

            var solidMinX = frontIsTowardLocalMinX ? windowMaxX : rect.xMin;
            var solidMaxX = frontIsTowardLocalMinX ? rect.xMax : windowMinX;
            var solidWidth = solidMaxX - solidMinX;

            // Solid rear half (full height).
            if (solidWidth > 0.01f)
            {
                CreateWallPanel(
                    wallRoot,
                    "SolidAftHalf",
                    new Vector3((solidMinX + solidMaxX) * 0.5f, rect.center.y, z),
                    new Vector3(solidWidth, rect.height, wallThickness),
                    wallMaterial);
            }

            // Front half: bottom + top around the window opening (true aperture).
            var winWidth = windowMaxX - windowMinX;
            var winCenterX = (windowMinX + windowMaxX) * 0.5f;

            CreateWallPanel(
                wallRoot,
                "BottomFrontHalf",
                new Vector3(winCenterX, (bottomY + windowBottom) * 0.5f, z),
                new Vector3(winWidth, windowBottom - bottomY, wallThickness),
                wallMaterial);

            var topStripHeight = topY - windowTop;
            if (topStripHeight > 0.01f)
            {
                CreateWallPanel(
                    wallRoot,
                    "TopFrontHalf",
                    new Vector3(winCenterX, (windowTop + topY) * 0.5f, z),
                    new Vector3(winWidth, topStripHeight, wallThickness),
                    wallMaterial);
            }

            CreateWindowOpening(
                wallRoot,
                new Vector3(winCenterX, windowCenterY, z),
                winWidth,
                windowHeight);
        }

        private void CreateWindowOpening(Transform wallRoot, Vector3 centerLocal, float width, float height)
        {
            var openingRoot = new GameObject("WindowOpening").transform;
            openingRoot.SetParent(wallRoot, false);
            openingRoot.localPosition = centerLocal;
            openingRoot.localRotation = Quaternion.identity;

            var t = Mathf.Max(0.01f, windowFrameThickness);
            var d = Mathf.Max(wallThickness + 0.02f, windowFrameDepth);
            var hw = width * 0.5f;
            var hh = height * 0.5f;

            // Transparent glass fills the aperture so the exterior remains visible.
            var glassThickness = Mathf.Max(0.012f, wallThickness * 0.35f);
            CreateWallPanel(
                openingRoot,
                "Glass",
                Vector3.zero,
                new Vector3(Mathf.Max(0.05f, width - t * 0.5f), Mathf.Max(0.05f, height - t * 0.5f), glassThickness),
                windowGlassMaterial);

            // Frame around the glass.
            CreateWallPanel(openingRoot, "Sill", new Vector3(0f, -hh + t * 0.5f, 0f), new Vector3(width + t, t, d), windowFrameMaterial);
            CreateWallPanel(openingRoot, "Header", new Vector3(0f, hh - t * 0.5f, 0f), new Vector3(width + t, t, d), windowFrameMaterial);
            CreateWallPanel(openingRoot, "Left", new Vector3(-hw + t * 0.5f, 0f, 0f), new Vector3(t, height, d), windowFrameMaterial);
            CreateWallPanel(openingRoot, "Right", new Vector3(hw - t * 0.5f, 0f, 0f), new Vector3(t, height, d), windowFrameMaterial);
        }

        private void CreateWallPanel(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            if (localScale.x <= 0.001f || localScale.y <= 0.001f || localScale.z <= 0.001f)
            {
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            ApplyMaterial(go, material);
        }

        private void CreateRoomObjectProxies(MRUKRoom room, Transform root)
        {
            var objectsRoot = new GameObject("RoomObjects").transform;
            objectsRoot.SetParent(root, false);

            foreach (var anchor in room.Anchors)
            {
                if (anchor == null || IsStructuralLabel(anchor.Label))
                {
                    continue;
                }

                if (anchor.VolumeBounds.HasValue)
                {
                    var bounds = anchor.VolumeBounds.Value;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Obj_{anchor.Label}_{anchor.name}";
                    go.transform.SetParent(objectsRoot, false);
                    go.transform.position = anchor.transform.TransformPoint(bounds.center);
                    go.transform.rotation = anchor.transform.rotation;
                    go.transform.localScale = bounds.size;
                    ApplyMaterial(go, roomObjectMaterial);
                    continue;
                }

                if (anchor.PlaneRect.HasValue)
                {
                    // Plane-only scene elements (e.g. wall art / screens) as thin boxes.
                    var rect = anchor.PlaneRect.Value;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Obj_{anchor.Label}_{anchor.name}";
                    go.transform.SetParent(objectsRoot, false);
                    go.transform.position = anchor.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, -0.02f));
                    go.transform.rotation = anchor.transform.rotation;
                    go.transform.localScale = new Vector3(rect.width, rect.height, 0.04f);
                    ApplyMaterial(go, roomObjectMaterial);
                }
            }
        }

        private void CreateRoleMarkers(Transform root)
        {
            var markers = new GameObject("RoleMarkers").transform;
            markers.SetParent(root, false);

            foreach (var entry in _gizmoWalls)
            {
                if (entry.role == WallRole.SolidExtra)
                {
                    continue;
                }

                var marker = new GameObject($"Marker_{entry.role}");
                marker.transform.SetParent(markers, false);
                marker.transform.position = entry.center + Vector3.up * 0.05f;

                // Small colored cube as an in-scene reference (complements OnDrawGizmos).
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Badge";
                cube.transform.SetParent(marker.transform, false);
                cube.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
                var renderer = cube.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = wallMaterial;
                    renderer.material.color = RoleColor(entry.role);
                }

                var collider = cube.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(collider);
                    }
                    else
                    {
                        DestroyImmediate(collider);
                    }
                }
            }
        }

        private static bool IsStructuralLabel(MRUKAnchor.SceneLabels label)
        {
            return label.HasFlag(MRUKAnchor.SceneLabels.FLOOR) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.CEILING) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.WALL_FACE) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.INNER_WALL_FACE) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.GLOBAL_MESH) ||
                   // Bridge windows/doors are generated by this component; ignore mapped openings.
                   label.HasFlag(MRUKAnchor.SceneLabels.WINDOW_FRAME) ||
                   label.HasFlag(MRUKAnchor.SceneLabels.DOOR_FRAME);
        }

        private void CreateExteriorEnvironment(MRUKRoom room, Transform root)
        {
            var exteriorRoot = new GameObject("Exterior").transform;
            exteriorRoot.SetParent(root, false);

            var roomBounds = room.GetRoomBounds();
            var roomCenter = roomBounds.center;
            roomCenter.y = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : roomBounds.min.y;

            var forward = Vector3.forward;
            if (_classifiedWalls.TryGetValue(WallRole.Front, out var front) && front != null)
            {
                // Exterior should sit outside the front windows (opposite of inward wall normal).
                var inward = Flatten(room.GetFacingDirection(front));
                if (inward.sqrMagnitude > 1e-6f)
                {
                    forward = -inward.normalized;
                }
                else
                {
                    forward = Flatten(GetAnchorWorldCenter(front) - roomCenter).normalized;
                }
            }

            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var exteriorCenter = roomCenter + forward * (roomBounds.extents.magnitude + exteriorDistanceFromRoom);

            CreateWaterPlane(exteriorRoot, exteriorCenter, forward);
            CreateTerrainMountains(exteriorRoot, exteriorCenter, forward, right);
            CreatePrimitiveMountainRing(exteriorRoot, exteriorCenter, forward, right);
        }

        private void CreateWaterPlane(Transform parent, Vector3 exteriorCenter, Vector3 forward)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.SetParent(parent, false);
            // Unity plane is 10x10; scale to a large sea in front of the bridge.
            water.transform.position = exteriorCenter + Vector3.down * 1.5f - forward * 8f;
            water.transform.localScale = new Vector3(exteriorTerrainSize * 0.12f, 1f, exteriorTerrainSize * 0.12f);
            ApplyMaterial(water, exteriorWaterMaterial != null ? exteriorWaterMaterial : exteriorGroundMaterial);
        }

        private void CreateTerrainMountains(Transform parent, Vector3 exteriorCenter, Vector3 forward, Vector3 right)
        {
            var terrainData = new TerrainData
            {
                heightmapResolution = 129,
                size = new Vector3(exteriorTerrainSize, exteriorTerrainHeight, exteriorTerrainSize)
            };

            var res = terrainData.heightmapResolution;
            var heights = new float[res, res];
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var nx = x / (float)(res - 1);
                    var nz = z / (float)(res - 1);
                    // Several soft peaks toward the far side of the terrain.
                    var h = 0f;
                    h += Peak(nx, nz, 0.22f, 0.70f, 0.18f, 0.85f);
                    h += Peak(nx, nz, 0.48f, 0.78f, 0.22f, 1.0f);
                    h += Peak(nx, nz, 0.72f, 0.66f, 0.16f, 0.75f);
                    h += Peak(nx, nz, 0.38f, 0.55f, 0.28f, 0.35f);
                    h += Mathf.PerlinNoise(nx * 4.1f, nz * 4.1f) * 0.08f;
                    heights[z, x] = Mathf.Clamp01(h);
                }
            }

            terrainData.SetHeights(0, 0, heights);

            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "ExteriorTerrain";
            terrainGo.transform.SetParent(parent, false);
            terrainGo.transform.position = exteriorCenter
                                          + forward * (exteriorTerrainSize * 0.15f)
                                          - right * (exteriorTerrainSize * 0.5f)
                                          + Vector3.down * 2f;

            var terrain = terrainGo.GetComponent<Terrain>();
            if (terrain != null && exteriorMountainMaterial != null)
            {
                terrain.materialTemplate = exteriorMountainMaterial;
            }
        }

        private void CreatePrimitiveMountainRing(Transform parent, Vector3 exteriorCenter, Vector3 forward, Vector3 right)
        {
            var mountains = new GameObject("MountainPrimitives").transform;
            mountains.SetParent(parent, false);

            // Extra bold silhouettes near the windows, complementary to the terrain.
            PlaceMountain(mountains, exteriorCenter + forward * 28f + right * -18f, new Vector3(22f, 18f, 22f));
            PlaceMountain(mountains, exteriorCenter + forward * 14f + right * 12f, new Vector3(16f, 12f, 16f));
            PlaceMountain(mountains, exteriorCenter + forward * 20f + right * 26f, new Vector3(20f, 16f, 18f));
            PlaceMountain(mountains, exteriorCenter + forward * 34f + right * 4f, new Vector3(28f, 24f, 24f));
            PlaceMountain(mountains, exteriorCenter + forward * 10f + right * -30f, new Vector3(14f, 10f, 14f));
        }

        private void PlaceMountain(Transform parent, Vector3 position, Vector3 scale)
        {
            var mountain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mountain.name = "Mountain";
            mountain.transform.SetParent(parent, false);
            mountain.transform.position = position;
            mountain.transform.localScale = scale;
            ApplyMaterial(mountain, exteriorMountainMaterial != null ? exteriorMountainMaterial : wallMaterial);

            var collider = mountain.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }
        }

        private static float Peak(float x, float z, float cx, float cz, float radius, float amplitude)
        {
            var dx = (x - cx) / radius;
            var dz = (z - cz) / radius;
            var d = dx * dx + dz * dz;
            if (d > 1f)
            {
                return 0f;
            }

            return (1f - d) * (1f - d) * amplitude;
        }

        private void ApplyPassthroughForExterior(bool restore)
        {
            if (!disablePassthroughForExterior)
            {
                return;
            }

            var layer = FindAnyObjectByType<OVRPassthroughLayer>();
            if (layer == null)
            {
                return;
            }

            if (!restore)
            {
                _passthroughWasEnabled = layer.enabled;
                layer.enabled = false;
            }
            else if (_passthroughWasEnabled)
            {
                layer.enabled = true;
            }
        }

        private static float GetWallWidth(MRUKAnchor wall)
        {
            if (wall != null && wall.PlaneRect.HasValue)
            {
                return wall.PlaneRect.Value.width;
            }

            return 0f;
        }

        private static Vector3 GetAnchorWorldCenter(MRUKAnchor anchor)
        {
            if (anchor == null)
            {
                return Vector3.zero;
            }

            return anchor.GetAnchorCenter();
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null)
            {
                return;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Color RoleColor(WallRole role)
        {
            switch (role)
            {
                case WallRole.Front: return new Color(0.2f, 0.85f, 1f);
                case WallRole.Aft: return new Color(0.25f, 0.35f, 1f);
                case WallRole.Port: return new Color(0.3f, 0.9f, 0.35f);
                case WallRole.Starboard: return new Color(1f, 0.35f, 0.3f);
                default: return Color.gray;
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawRoleGizmos || _gizmoWalls.Count == 0)
            {
                return;
            }

            foreach (var entry in _gizmoWalls)
            {
                if (entry.role == WallRole.SolidExtra)
                {
                    continue;
                }

                Gizmos.color = RoleColor(entry.role);
                Gizmos.DrawSphere(entry.center, 0.08f);
                Gizmos.DrawRay(entry.center, entry.inward.normalized * gizmoArrowLength);
#if UNITY_EDITOR
                UnityEditor.Handles.color = RoleColor(entry.role);
                UnityEditor.Handles.Label(entry.center + Vector3.up * 0.2f, entry.role.ToString());
#endif
            }
        }
    }
}
