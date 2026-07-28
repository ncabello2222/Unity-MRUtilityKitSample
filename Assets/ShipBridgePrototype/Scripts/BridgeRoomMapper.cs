using System;
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

        [Header("Mapped Room Objects")]
        [Tooltip("Maximum vertical gap allowed between a furniture volume and the mapped floor.")]
        [SerializeField] [Min(0f)] private float furnitureFloorContactTolerance = 0.15f;

        [Header("Wall / Window")]
        [Tooltip("If disabled, every mapped wall is generated as a solid wall without synthetic windows.")]
        [SerializeField] private bool generateBridgeWindows = true;
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

        [Header("Wall Occlusion")]
        [Tooltip("Write depth on solid wall panels so exterior content is hidden behind bulkheads. Window glass/openings are never occluders.")]
        [SerializeField] private bool enableWallOcclusion = true;
        [Tooltip("Depth-only material (Meta InvisibleOccluder). Used on solid panels / bulkheads / pillars.")]
        [SerializeField] private Material wallOcclusionMaterial;
        [Tooltip("If true, keep BridgeWall visuals and add a depth-only occluder twin. If false, solids use only the occluder (passthrough walls).")]
        [SerializeField] private bool keepWallVisuals = true;

        [Header("Exterior")]
        [SerializeField] private bool generateExterior = true;
        [SerializeField] private bool disablePassthroughForExterior = true;
        [SerializeField] private float exteriorTerrainSize = 220f;
        [SerializeField] private float exteriorTerrainHeight = 45f;
        [SerializeField] private float exteriorDistanceFromRoom = 18f;

        [Header("Gizmos")]
        [SerializeField] private bool drawRoleGizmos = true;
        [SerializeField] private float gizmoArrowLength = 0.6f;
        [SerializeField] private float gizmoForwardArrowLength = 1.4f;

        [Header("Bow calibration")]
        [SerializeField] [Range(0.3f, 0.95f)] private float restoreDotThreshold = 0.55f;
        [SerializeField] [Range(-0.95f, -0.3f)] private float oppositeNormalMaxDot = -0.5f;

        private Transform _generatedRoot;
        private ExteriorWorldRoot _exteriorWorldRoot;
        private BridgeReferenceFrame _referenceFrame;
        private BridgeOrientationCalibration _calibration;
        private MRUKRoom _activeRoom;
        private readonly Dictionary<WallRole, MRUKAnchor> _classifiedWalls = new();
        private readonly List<(WallRole role, Vector3 center, Vector3 inward)> _gizmoWalls = new();
        private bool _mrukBound;
        private bool _passthroughWasEnabled;
        private bool _calibrationRestored;
        private Vector3 _bowForwardWorld = Vector3.forward;

        public Transform GeneratedRoot => _generatedRoot;
        public ExteriorWorldRoot ExteriorWorld => _exteriorWorldRoot;
        public BridgeReferenceFrame ReferenceFrame => _referenceFrame;
        public IReadOnlyDictionary<WallRole, MRUKAnchor> ClassifiedWalls => _classifiedWalls;

        /// <summary>
        /// Fired after walls are classified, geometry is built, and
        /// <see cref="BridgeReferenceFrame"/> is published (including Front/Aft flips).
        /// </summary>
        public event Action BridgeGenerated;

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
            GenerateBridgeKeepingFrontChoice(preferRestoredCalibration: true, forcedFront: null);
        }

        private void GenerateBridgeKeepingFrontChoice(bool preferRestoredCalibration, MRUKAnchor forcedFront)
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
            _activeRoom = room;
            _calibrationRestored = false;

            var rootGo = new GameObject("BridgeGeneratedRoot");
            _generatedRoot = rootGo.transform;
            _generatedRoot.SetParent(transform, false);

            if (!ClassifyWalls(room, preferRestoredCalibration, forcedFront))
            {
                Debug.LogError("[BridgeRoomMapper] Wall classification failed; bridge not generated.");
                return;
            }

            CreateFloorAndCeiling(room, _generatedRoot);
            CreateBridgeWalls(room, _generatedRoot);
            CreateRoomObjectProxies(room, _generatedRoot);

            if (generateExterior)
            {
                CreateExteriorEnvironment(room, _generatedRoot);
                ApplyPassthroughForExterior(false);
            }
            else
            {
                EnsureReferenceFrameWithoutExterior(room);
            }

            EnsureCalibrationComponent(room);
            LogBridgeFrame();
            BridgeGenerated?.Invoke();
        }

        /// <summary>Persist current Front as the confirmed bow.</summary>
        public void ConfirmFrontCalibration()
        {
            if (_activeRoom == null ||
                !_classifiedWalls.TryGetValue(WallRole.Front, out var front) ||
                front == null ||
                _referenceFrame == null)
            {
                Debug.LogWarning("[BridgeRoomMapper] Cannot confirm bow: frame not ready.");
                return;
            }

            BridgeCalibrationStore.Save(
                _activeRoom,
                _referenceFrame.Forward,
                GetWallWidth(front),
                GetAnchorWorldCenter(front));
            _referenceFrame.SetCalibrated(true);
            _calibrationRestored = true;
            LogBridgeFrame();
            Debug.Log("[BridgeRoomMapper] Bow calibration confirmed and saved.");
        }

        /// <summary>Swap Front/Aft among the current pair and regenerate.</summary>
        public void FlipFrontAndAft()
        {
            if (!_classifiedWalls.TryGetValue(WallRole.Front, out var front) ||
                !_classifiedWalls.TryGetValue(WallRole.Aft, out var aft) ||
                front == null ||
                aft == null)
            {
                Debug.LogWarning("[BridgeRoomMapper] Cannot flip: Front/Aft not classified.");
                return;
            }

            if (_referenceFrame != null)
            {
                _referenceFrame.SetCalibrated(false);
            }

            // Rebuild with the swapped Front, without trying to restore the old save.
            GenerateBridgeKeepingFrontChoice(preferRestoredCalibration: false, forcedFront: aft);
        }

        /// <summary>
        /// Cycle to the next MRUK wall face as Front (any wall), then regenerate.
        /// </summary>
        public void SelectNextFrontWall() => CycleFrontWall(+1);

        /// <summary>
        /// Cycle to the previous MRUK wall face as Front (any wall), then regenerate.
        /// </summary>
        public void SelectPreviousFrontWall() => CycleFrontWall(-1);

        private void CycleFrontWall(int direction)
        {
            if (_activeRoom == null)
            {
                Debug.LogWarning("[BridgeRoomMapper] Cannot select wall: no active room.");
                return;
            }

            var walls = GetBridgeWallCandidates(_activeRoom);
            if (walls.Count == 0)
            {
                Debug.LogWarning("[BridgeRoomMapper] Cannot select wall: no wall candidates.");
                return;
            }

            var step = direction >= 0 ? 1 : -1;
            var currentIndex = 0;
            if (_classifiedWalls.TryGetValue(WallRole.Front, out var current) && current != null)
            {
                var found = walls.IndexOf(current);
                if (found >= 0)
                {
                    currentIndex = found;
                }
            }

            var nextIndex = (currentIndex + step) % walls.Count;
            if (nextIndex < 0)
            {
                nextIndex += walls.Count;
            }

            var next = walls[nextIndex];
            if (_referenceFrame != null)
            {
                _referenceFrame.SetCalibrated(false);
            }

            GenerateBridgeKeepingFrontChoice(preferRestoredCalibration: false, forcedFront: next);
            Debug.Log(
                $"[BridgeRoomMapper] Selected wall {nextIndex + 1}/{walls.Count} " +
                $"as Front: {next.name} ({GetWallWidth(next):F2}m).");
        }

        public string GetFrontWallDisplayName()
        {
            if (!_classifiedWalls.TryGetValue(WallRole.Front, out var front) || front == null)
            {
                return "(ninguna)";
            }

            return $"{front.name} ({GetWallWidth(front):F2} m)";
        }

        public int GetFrontWallSelectionIndex(out int wallCount)
        {
            wallCount = 0;
            if (_activeRoom == null)
            {
                return -1;
            }

            var walls = GetBridgeWallCandidates(_activeRoom);
            wallCount = walls.Count;
            if (!_classifiedWalls.TryGetValue(WallRole.Front, out var front) || front == null)
            {
                return -1;
            }

            return walls.IndexOf(front);
        }

        /// <summary>Clear saved bow and regenerate with a camera-proposed Front.</summary>
        public void RegenerateWithProposedFront()
        {
            GenerateBridgeKeepingFrontChoice(preferRestoredCalibration: false, forcedFront: null);
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
            _exteriorWorldRoot = null;
            _referenceFrame = null;
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

        private bool ClassifyWalls(MRUKRoom room, bool preferRestoredCalibration, MRUKAnchor forcedFront)
        {
            var walls = GetBridgeWallCandidates(room);
            if (walls.Count == 0)
            {
                Debug.LogWarning("[BridgeRoomMapper] Room has no wall anchors.");
                return false;
            }

            var sorted = new List<MRUKAnchor>(walls);
            sorted.Sort((a, b) => GetWallWidth(b).CompareTo(GetWallWidth(a)));

            if (sorted.Count < 2)
            {
                Debug.LogWarning("[BridgeRoomMapper] Need at least two walls to classify front/aft.");
                return false;
            }

            // Default proposal uses the opposite-facing pair with the greatest mean length.
            // The user can later force ANY wall as Front via SelectNextFrontWall().
            if (!TryPickLongitudinalPair(room, sorted, out var longA, out var longB, out var meanLength))
            {
                Debug.LogWarning(
                    "[BridgeRoomMapper] Irregular room: no opposite long-wall pair found. " +
                    "Falling back to the two widest walls — verify bow manually.");
                longA = sorted[0];
                longB = sorted[1];
                meanLength = 0.5f * (GetWallWidth(longA) + GetWallWidth(longB));
            }

            if (longA == longB)
            {
                Debug.LogError("[BridgeRoomMapper] Front/Aft candidates are the same wall.");
                return false;
            }

            MRUKAnchor front;
            if (forcedFront != null && walls.Contains(forcedFront))
            {
                front = forcedFront;
                _calibrationRestored = false;
            }
            else if (preferRestoredCalibration &&
                     TryRestoreFrontFromCalibration(room, walls, out front))
            {
                _calibrationRestored = true;
            }
            else
            {
                front = ProposeFrontFromCamera(room, longA, longB);
                _calibrationRestored = false;
            }

            var aft = FindBestOppositeWall(room, walls, front);
            if (aft == null)
            {
                aft = front == longA ? longB : longA;
            }

            var fa = Flatten(room.GetFacingDirection(front));
            var fb = Flatten(room.GetFacingDirection(aft));
            if (fa.sqrMagnitude > 1e-6f && fb.sqrMagnitude > 1e-6f)
            {
                fa.Normalize();
                fb.Normalize();
                if (Vector3.Dot(fa, fb) > oppositeNormalMaxDot)
                {
                    Debug.LogWarning(
                        "[BridgeRoomMapper] Selected Front/Aft normals are not clearly opposite. " +
                        $"dot={Vector3.Dot(fa, fb):F2}. Classification may be unreliable.");
                }
            }

            _classifiedWalls[WallRole.Front] = front;
            _classifiedWalls[WallRole.Aft] = aft;

            var roomCenter = room.GetRoomBounds().center;
            _bowForwardWorld = OutwardHorizontal(room, front, roomCenter);
            var starboardDir = Vector3.Cross(Vector3.up, _bowForwardWorld).normalized;

            ClassifyPortStarboard(sorted, front, aft, roomCenter, starboardDir, out var port, out var starboard);

            if (port != null)
            {
                _classifiedWalls[WallRole.Port] = port;
            }

            if (starboard != null && starboard != port)
            {
                _classifiedWalls[WallRole.Starboard] = starboard;
            }
            else if (port == null || starboard == null || starboard == port)
            {
                Debug.LogWarning(
                    "[BridgeRoomMapper] Could not assign distinct Port/Starboard walls. " +
                    "Room may be irregular.");
            }

            if (port != null && starboard != null && starboard != port)
            {
                var portSide = Vector3.Dot(Flatten(GetAnchorWorldCenter(port) - roomCenter), starboardDir);
                var stbdSide = Vector3.Dot(Flatten(GetAnchorWorldCenter(starboard) - roomCenter), starboardDir);
                if (portSide * stbdSide >= 0f)
                {
                    Debug.LogWarning(
                        "[BridgeRoomMapper] Port and Starboard are not on opposite sides of the " +
                        "longitudinal axis. Check room geometry.");
                }
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

            Debug.Log(
                $"[BridgeRoomMapper] Longitudinal pair mean length={meanLength:F2}m. " +
                $"Front={front.name} ({GetWallWidth(front):F2}m), Aft={aft.name} ({GetWallWidth(aft):F2}m).");
            return true;
        }

        private bool TryPickLongitudinalPair(
            MRUKRoom room,
            List<MRUKAnchor> wallsSortedWideFirst,
            out MRUKAnchor wallA,
            out MRUKAnchor wallB,
            out float meanLength)
        {
            wallA = null;
            wallB = null;
            meanLength = 0f;
            var bestMean = float.NegativeInfinity;

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

                    // Opposite-facing (≈ parallel planes): near -1.
                    var opposite = Vector3.Dot(fa, fb);
                    if (opposite > oppositeNormalMaxDot)
                    {
                        continue;
                    }

                    var mean = 0.5f * (GetWallWidth(a) + GetWallWidth(b));
                    // Prefer longer pair; small boost for more opposite normals.
                    var score = mean + 0.05f * (-opposite);
                    if (score > bestMean)
                    {
                        bestMean = score;
                        meanLength = mean;
                        wallA = a;
                        wallB = b;
                    }
                }
            }

            return wallA != null && wallB != null;
        }

        private bool TryRestoreFrontFromCalibration(
            MRUKRoom room,
            List<MRUKAnchor> walls,
            out MRUKAnchor front)
        {
            front = null;
            if (!BridgeCalibrationStore.TryLoad(room, out var record) || walls == null || walls.Count == 0)
            {
                return false;
            }

            var savedForward = Flatten(room.transform.TransformDirection(record.forwardLocalInRoom));
            if (savedForward.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            savedForward.Normalize();
            var roomCenter = room.GetRoomBounds().center;
            var bestDot = float.NegativeInfinity;
            MRUKAnchor bestWall = null;
            foreach (var wall in walls)
            {
                if (wall == null)
                {
                    continue;
                }

                var outward = OutwardHorizontal(room, wall, roomCenter);
                var dot = Vector3.Dot(outward, savedForward);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestWall = wall;
                }
            }

            if (bestWall == null || bestDot < restoreDotThreshold)
            {
                Debug.LogWarning(
                    $"[BridgeRoomMapper] Saved bow vector does not match any wall " +
                    $"(bestDot={bestDot:F2} < {restoreDotThreshold:F2}). Recalibration required.");
                return false;
            }

            front = bestWall;
            return true;
        }

        private static MRUKAnchor FindBestOppositeWall(
            MRUKRoom room,
            List<MRUKAnchor> walls,
            MRUKAnchor front)
        {
            if (room == null || walls == null || front == null)
            {
                return null;
            }

            var roomCenter = room.GetRoomBounds().center;
            var frontOut = OutwardHorizontal(room, front, roomCenter);
            MRUKAnchor best = null;
            var bestScore = float.PositiveInfinity;

            foreach (var wall in walls)
            {
                if (wall == null || wall == front)
                {
                    continue;
                }

                var outward = OutwardHorizontal(room, wall, roomCenter);
                // Prefer most opposite outward; break ties by wall width.
                var score = Vector3.Dot(frontOut, outward) - 0.01f * GetWallWidth(wall);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = wall;
                }
            }

            return best;
        }

        private static MRUKAnchor ProposeFrontFromCamera(MRUKRoom room, MRUKAnchor a, MRUKAnchor b)
        {
            var roomCenter = room.GetRoomBounds().center;
            var cam = Camera.main;
            if (cam == null)
            {
                // Stable editor fallback: prefer wall closer to world +Z.
                var outA = OutwardHorizontal(room, a, roomCenter);
                var outB = OutwardHorizontal(room, b, roomCenter);
                return Vector3.Dot(outA, Vector3.forward) >= Vector3.Dot(outB, Vector3.forward) ? a : b;
            }

            var look = Flatten(cam.transform.forward);
            if (look.sqrMagnitude < 1e-6f)
            {
                look = Vector3.forward;
            }
            else
            {
                look.Normalize();
            }

            var toA = Flatten(GetAnchorWorldCenter(a) - cam.transform.position);
            var toB = Flatten(GetAnchorWorldCenter(b) - cam.transform.position);
            if (toA.sqrMagnitude > 1e-6f)
            {
                toA.Normalize();
            }

            if (toB.sqrMagnitude > 1e-6f)
            {
                toB.Normalize();
            }

            // Proposal only — user must confirm for persistence.
            return Vector3.Dot(look, toA) >= Vector3.Dot(look, toB) ? a : b;
        }

        private static void ClassifyPortStarboard(
            List<MRUKAnchor> walls,
            MRUKAnchor front,
            MRUKAnchor aft,
            Vector3 roomCenter,
            Vector3 starboardDir,
            out MRUKAnchor port,
            out MRUKAnchor starboard)
        {
            port = null;
            starboard = null;
            var bestPort = float.PositiveInfinity;
            var bestStarboard = float.NegativeInfinity;

            foreach (var wall in walls)
            {
                if (wall == front || wall == aft)
                {
                    continue;
                }

                var side = Vector3.Dot(Flatten(GetAnchorWorldCenter(wall) - roomCenter), starboardDir);
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
        }

        private static Vector3 OutwardHorizontal(MRUKRoom room, MRUKAnchor wall, Vector3 roomCenter)
        {
            // The bow axis must be exactly perpendicular to the wall plane so the
            // hull/exterior stay square with the room. The room-center direction is
            // only used to resolve the outward sign (wall anchors are often not
            // centered on the room bounds, so center→wall alone can be diagonal).
            var toWall = Flatten(GetAnchorWorldCenter(wall) - roomCenter);
            var normal = Flatten(room.GetFacingDirection(wall));
            if (normal.sqrMagnitude > 1e-6f)
            {
                normal.Normalize();
                if (toWall.sqrMagnitude > 1e-6f && Vector3.Dot(normal, toWall) > 0f)
                {
                    return normal;
                }

                // MRUK facing direction points into the room; invert for outward/bow.
                return -normal;
            }

            if (toWall.sqrMagnitude > 1e-6f)
            {
                return toWall.normalized;
            }

            return Vector3.forward;
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

                if (!classified && wall.PlaneRect.HasValue)
                {
                    var extra = wall.PlaneRect.Value;
                    CreateSolidWall(wall, wallsRoot, "ExtraWall", extra.xMin, extra.xMax);
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

            ResolveWallSpanX(role, wall, out var xMin, out var xMax);

            if (!generateBridgeWindows)
            {
                CreateSolidWall(wall, parent, role.ToString(), xMin, xMax);
                return;
            }

            switch (role)
            {
                case WallRole.Front:
                    CreateFrontWindowWall(wall, parent, xMin, xMax);
                    break;
                case WallRole.Port:
                case WallRole.Starboard:
                    CreateSideWindowWall(wall, parent, role, xMin, xMax);
                    break;
                case WallRole.Aft:
                case WallRole.SolidExtra:
                default:
                    CreateSolidWall(wall, parent, role.ToString(), xMin, xMax);
                    break;
            }
        }

        /// <summary>
        /// Front spans out to the outer faces of Port/Starboard so the bow wall owns the
        /// corners; side walls are trimmed at the bow so they butt against that front face
        /// instead of running past it and nesting the front wall between them.
        /// </summary>
        private void ResolveWallSpanX(WallRole role, MRUKAnchor wall, out float xMin, out float xMax)
        {
            var rect = wall.PlaneRect.Value;
            xMin = rect.xMin;
            xMax = rect.xMax;

            switch (role)
            {
                case WallRole.Front:
                    ExpandFrontWallToSideOuters(wall, ref xMin, ref xMax);
                    break;
                case WallRole.Port:
                case WallRole.Starboard:
                    TrimSideWallToFrontFace(wall, ref xMin, ref xMax);
                    break;
            }
        }

        private void ExpandFrontWallToSideOuters(MRUKAnchor front, ref float xMin, ref float xMax)
        {
            _classifiedWalls.TryGetValue(WallRole.Port, out var port);
            _classifiedWalls.TryGetValue(WallRole.Starboard, out var starboard);

            if (TryProjectSideOuterOntoFront(front, port, out var portX))
            {
                xMin = Mathf.Min(xMin, portX);
                xMax = Mathf.Max(xMax, portX);
            }

            if (TryProjectSideOuterOntoFront(front, starboard, out var starboardX))
            {
                xMin = Mathf.Min(xMin, starboardX);
                xMax = Mathf.Max(xMax, starboardX);
            }
        }

        private bool TryProjectSideOuterOntoFront(MRUKAnchor front, MRUKAnchor side, out float frontLocalX)
        {
            frontLocalX = 0f;
            if (front == null || side == null || !side.PlaneRect.HasValue)
            {
                return false;
            }

            // Outer face of the side wall (thickness grows along -local Z).
            var sideRect = side.PlaneRect.Value;
            var bowLocalX = GetBowLocalX(side, sideRect.xMin, sideRect.xMax);
            var outerCorner = side.transform.TransformPoint(
                new Vector3(bowLocalX, sideRect.center.y, -wallThickness));
            frontLocalX = front.transform.InverseTransformPoint(outerCorner).x;
            return true;
        }

        private void TrimSideWallToFrontFace(MRUKAnchor side, ref float xMin, ref float xMax)
        {
            if (!_classifiedWalls.TryGetValue(WallRole.Front, out var front) || front == null)
            {
                return;
            }

            if (!TryIntersectWallLocalX(side, front, 0f, out var frontFaceX))
            {
                return;
            }

            // Keep the aft end; pull the bow end back to the front wall's inner face.
            if (Mathf.Abs(frontFaceX - xMin) <= Mathf.Abs(frontFaceX - xMax))
            {
                xMin = frontFaceX;
            }
            else
            {
                xMax = frontFaceX;
            }

            if (xMax < xMin)
            {
                (xMin, xMax) = (xMax, xMin);
            }
        }

        private float GetBowLocalX(MRUKAnchor wall, float xMin, float xMax)
        {
            var leftWorld = wall.transform.TransformPoint(new Vector3(xMin, 0f, 0f));
            var rightWorld = wall.transform.TransformPoint(new Vector3(xMax, 0f, 0f));
            var bow = _bowForwardWorld.sqrMagnitude > 1e-6f ? _bowForwardWorld : Vector3.forward;
            return Vector3.Dot(Flatten(leftWorld), bow) >= Vector3.Dot(Flatten(rightWorld), bow)
                ? xMin
                : xMax;
        }

        /// <summary>
        /// Local-X on <paramref name="wall"/> where it meets the plane of
        /// <paramref name="cutter"/> at the cutter's local Z.
        /// </summary>
        private static bool TryIntersectWallLocalX(
            MRUKAnchor wall,
            MRUKAnchor cutter,
            float cutterLocalZ,
            out float wallLocalX)
        {
            wallLocalX = 0f;
            if (wall == null || cutter == null)
            {
                return false;
            }

            var planePoint = cutter.transform.TransformPoint(new Vector3(0f, 0f, cutterLocalZ));
            var planeNormal = cutter.transform.forward;
            var origin = wall.transform.position;
            var axis = wall.transform.right;
            var denom = Vector3.Dot(planeNormal, axis);
            if (Mathf.Abs(denom) < 1e-5f)
            {
                return false;
            }

            var t = Vector3.Dot(planeNormal, planePoint - origin) / denom;
            wallLocalX = wall.transform.InverseTransformPoint(origin + axis * t).x;
            return true;
        }

        private void CreateSolidWall(
            MRUKAnchor wall,
            Transform parent,
            string label,
            float xMin,
            float xMax)
        {
            var rect = wall.PlaneRect.Value;
            var width = xMax - xMin;
            if (width < 0.01f)
            {
                return;
            }

            var wallRoot = new GameObject($"Wall_{label}").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            CreateSolidWallPanel(
                wallRoot,
                "Solid",
                new Vector3((xMin + xMax) * 0.5f, rect.center.y, -wallThickness * 0.5f),
                new Vector3(width, rect.height, wallThickness));
        }

        private void CreateFrontWindowWall(MRUKAnchor wall, Transform parent, float xMin, float xMax)
        {
            var rect = wall.PlaneRect.Value;
            var wallRoot = new GameObject("Wall_Front").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            var width = xMax - xMin;
            if (width < 0.01f)
            {
                return;
            }

            var halfW = width * 0.5f;
            var centerX = (xMin + xMax) * 0.5f;
            var bottomY = rect.yMin;
            var topY = rect.yMax;

            var windowBottom = Mathf.Clamp(bottomY + windowWaistHeight, bottomY + 0.05f, topY - 0.2f);
            var windowTop = Mathf.Clamp(topY - windowTopClearance, windowBottom + 0.2f, topY - 0.02f);
            var margin = Mathf.Clamp(frontWindowSideMargin, 0f, halfW * 0.45f);
            var windowLeft = xMin + margin;
            var windowRight = xMax - margin;
            var windowWidth = windowRight - windowLeft;
            var windowHeight = windowTop - windowBottom;
            var windowCenterX = (windowLeft + windowRight) * 0.5f;
            var windowCenterY = (windowBottom + windowTop) * 0.5f;
            var z = -wallThickness * 0.5f;

            // Bottom bulkhead under the window.
            CreateSolidWallPanel(
                wallRoot,
                "Bottom",
                new Vector3(centerX, (bottomY + windowBottom) * 0.5f, z),
                new Vector3(width, windowBottom - bottomY, wallThickness));

            // Top strip above the window (near ceiling).
            var topStripHeight = topY - windowTop;
            if (topStripHeight > 0.01f)
            {
                CreateSolidWallPanel(
                    wallRoot,
                    "Top",
                    new Vector3(centerX, (windowTop + topY) * 0.5f, z),
                    new Vector3(width, topStripHeight, wallThickness));
            }

            // Left / right pillars (narrow margins) — keep opening continuous; only if margin > 0.
            if (margin > 0.01f)
            {
                CreateSolidWallPanel(
                    wallRoot,
                    "LeftPillar",
                    new Vector3((xMin + windowLeft) * 0.5f, windowCenterY, z),
                    new Vector3(margin, windowHeight, wallThickness));
                CreateSolidWallPanel(
                    wallRoot,
                    "RightPillar",
                    new Vector3((windowRight + xMax) * 0.5f, windowCenterY, z),
                    new Vector3(margin, windowHeight, wallThickness));
            }

            CreateWindowOpening(
                wallRoot,
                new Vector3(windowCenterX, windowCenterY, z),
                windowWidth,
                windowHeight);
        }

        private void CreateSideWindowWall(
            MRUKAnchor wall,
            Transform parent,
            WallRole role,
            float xMin,
            float xMax)
        {
            var rect = wall.PlaneRect.Value;
            var spanWidth = xMax - xMin;
            if (spanWidth < 0.01f)
            {
                return;
            }

            var wallRoot = new GameObject($"Wall_{role}").transform;
            wallRoot.SetParent(parent, false);
            wallRoot.SetPositionAndRotation(wall.transform.position, wall.transform.rotation);

            var bottomY = rect.yMin;
            var topY = rect.yMax;
            var windowBottom = Mathf.Clamp(bottomY + windowWaistHeight, bottomY + 0.05f, topY - 0.2f);
            var windowTop = Mathf.Clamp(topY - windowTopClearance, windowBottom + 0.2f, topY - 0.02f);
            var windowHeight = windowTop - windowBottom;
            var windowCenterY = (windowBottom + windowTop) * 0.5f;
            var z = -wallThickness * 0.5f;

            // Which local-X end faces the bow — use BridgeReferenceFrame forward, not a
            // separate Front-wall search.
            var frontIsTowardLocalMinX = Mathf.Abs(GetBowLocalX(wall, xMin, xMax) - xMin) < 1e-4f;

            var windowLength = Mathf.Clamp01(sideWindowLengthRatio) * spanWidth;
            float windowMinX;
            float windowMaxX;
            if (frontIsTowardLocalMinX)
            {
                windowMinX = xMin;
                windowMaxX = xMin + windowLength;
            }
            else
            {
                windowMaxX = xMax;
                windowMinX = xMax - windowLength;
            }

            var solidMinX = frontIsTowardLocalMinX ? windowMaxX : xMin;
            var solidMaxX = frontIsTowardLocalMinX ? xMax : windowMinX;
            var solidWidth = solidMaxX - solidMinX;

            // Solid rear half (full height).
            if (solidWidth > 0.01f)
            {
                CreateSolidWallPanel(
                    wallRoot,
                    "SolidAftHalf",
                    new Vector3((solidMinX + solidMaxX) * 0.5f, rect.center.y, z),
                    new Vector3(solidWidth, rect.height, wallThickness));
            }

            // Front half: bottom + top around the window opening (true aperture).
            var winWidth = windowMaxX - windowMinX;
            var winCenterX = (windowMinX + windowMaxX) * 0.5f;

            CreateSolidWallPanel(
                wallRoot,
                "BottomFrontHalf",
                new Vector3(winCenterX, (bottomY + windowBottom) * 0.5f, z),
                new Vector3(winWidth, windowBottom - bottomY, wallThickness));

            var topStripHeight = topY - windowTop;
            if (topStripHeight > 0.01f)
            {
                CreateSolidWallPanel(
                    wallRoot,
                    "TopFrontHalf",
                    new Vector3(winCenterX, (windowTop + topY) * 0.5f, z),
                    new Vector3(winWidth, topStripHeight, wallThickness));
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

        private void CreateSolidWallPanel(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            // Opaque wallMaterial already writes depth and occludes the exterior through bulkheads.
            // Only spawn InvisibleOccluder when visuals are off (passthrough-wall mode). Spawning
            // occluder twins alongside painted walls risks error/magenta shaders covering the bridge
            // on Android if Meta/MRUK/MixedReality/InvisibleOccluder fails to compile.
            var wantsOccluderOnly = enableWallOcclusion &&
                                    !keepWallVisuals &&
                                    wallOcclusionMaterial != null;

            if (wantsOccluderOnly)
            {
                CreateWallPanel(parent, name, localPosition, localScale, wallOcclusionMaterial, removeCollider: false);
                return;
            }

            CreateWallPanel(parent, name, localPosition, localScale, wallMaterial, removeCollider: false);
        }

        private void CreateWallPanel(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool removeCollider = false)
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

            if (removeCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }
            }
        }

        private void CreateRoomObjectProxies(MRUKRoom room, Transform root)
        {
            var objectsRoot = new GameObject("RoomObjects").transform;
            objectsRoot.SetParent(root, false);

            foreach (var anchor in room.Anchors)
            {
                if (!IsFloorStandingFurniture(anchor, room))
                {
                    continue;
                }

                var bounds = anchor.VolumeBounds.Value;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Obj_{anchor.Label}_{anchor.name}";
                go.transform.SetParent(objectsRoot, false);
                go.transform.position = anchor.transform.TransformPoint(bounds.center);
                go.transform.rotation = anchor.transform.rotation;
                go.transform.localScale = bounds.size;
                ApplyMaterial(go, roomObjectMaterial);
            }
        }

        private bool IsFloorStandingFurniture(MRUKAnchor anchor, MRUKRoom room)
        {
            if (anchor == null || room?.FloorAnchor == null || !anchor.VolumeBounds.HasValue)
            {
                return false;
            }

            var furnitureLabels = MRUKAnchor.SceneLabels.TABLE |
                                  MRUKAnchor.SceneLabels.COUCH |
                                  MRUKAnchor.SceneLabels.BED |
                                  MRUKAnchor.SceneLabels.STORAGE;
            if (!anchor.HasAnyLabel(furnitureLabels))
            {
                return false;
            }

            var floorHeight = room.FloorAnchor.transform.position.y;
            return GetVolumeWorldBottom(anchor) <= floorHeight + furnitureFloorContactTolerance;
        }

        private static float GetVolumeWorldBottom(MRUKAnchor anchor)
        {
            var bounds = anchor.VolumeBounds.Value;
            var min = bounds.min;
            var max = bounds.max;
            var bottom = float.PositiveInfinity;

            for (var x = 0; x < 2; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        bottom = Mathf.Min(bottom, anchor.transform.TransformPoint(corner).y);
                    }
                }
            }

            return bottom;
        }

        private void CreateExteriorEnvironment(MRUKRoom room, Transform root)
        {
            // Shell only: scenario content is loaded under ExteriorWorld by ExteriorScenarioLoader.
            var exteriorGo = new GameObject("ExteriorWorld");
            var exteriorRoot = exteriorGo.transform;
            exteriorRoot.SetParent(root, false);
            _exteriorWorldRoot = exteriorGo.AddComponent<ExteriorWorldRoot>();

            var roomCenter = GetRoomFloorCenter(room);
            var pivotGo = new GameObject("ShipMotionPivot");
            pivotGo.transform.SetParent(root, false);
            pivotGo.transform.position = roomCenter;

            _referenceFrame = pivotGo.AddComponent<BridgeReferenceFrame>();
            PublishReferenceFrame(room, calibrated: _calibrationRestored);

            var forward = _referenceFrame.Forward;

            // Scenario prefabs are authored with +Z = out the front windows.
            // ShipMotionPivot.forward must match BridgeReferenceFrame.Forward exactly.
            exteriorRoot.SetPositionAndRotation(roomCenter, _referenceFrame.Rotation);
            pivotGo.transform.rotation = _referenceFrame.Rotation;
            _exteriorWorldRoot.SetMotionPivot(pivotGo.transform);

            EnsureBridgeSystems(out var motion, out var loader);
            motion.SetControlState(EnsureControlState(motion.gameObject));
            loader.NotifyExteriorReady(_exteriorWorldRoot, pivotGo.transform, forward);

            var hullPresenter = systemsHullPresenter(motion.gameObject);
            hullPresenter.BindPivot(pivotGo.transform);
        }

        private void EnsureReferenceFrameWithoutExterior(MRUKRoom room)
        {
            var roomCenter = GetRoomFloorCenter(room);
            var pivotGo = new GameObject("ShipMotionPivot");
            pivotGo.transform.SetParent(_generatedRoot, false);
            pivotGo.transform.position = roomCenter;
            _referenceFrame = pivotGo.AddComponent<BridgeReferenceFrame>();
            PublishReferenceFrame(room, calibrated: _calibrationRestored);
            pivotGo.transform.rotation = _referenceFrame.Rotation;
        }

        private void PublishReferenceFrame(MRUKRoom room, bool calibrated)
        {
            _classifiedWalls.TryGetValue(WallRole.Front, out var front);
            _classifiedWalls.TryGetValue(WallRole.Aft, out var aft);
            _classifiedWalls.TryGetValue(WallRole.Port, out var port);
            _classifiedWalls.TryGetValue(WallRole.Starboard, out var starboard);

            var forward = _bowForwardWorld.sqrMagnitude > 1e-6f
                ? _bowForwardWorld
                : Vector3.forward;

            _referenceFrame.Apply(forward, front, aft, port, starboard, calibrated);
        }

        private void EnsureCalibrationComponent(MRUKRoom room)
        {
            if (_calibration == null)
            {
                _calibration = GetComponent<BridgeOrientationCalibration>();
                if (_calibration == null)
                {
                    _calibration = gameObject.AddComponent<BridgeOrientationCalibration>();
                }
            }

            var needsConfirm = _referenceFrame == null || !_referenceFrame.IsCalibrated;
            _calibration.Bind(this, room, needsConfirm);
        }

        private void LogBridgeFrame()
        {
            var frame = _referenceFrame;
            if (frame == null)
            {
                return;
            }

            Debug.Log(
                "[BridgeRoomMapper] Bridge frame:\n" +
                $"  Front wall: {NameOf(WallRole.Front)}\n" +
                $"  Aft wall: {NameOf(WallRole.Aft)}\n" +
                $"  Port wall: {NameOf(WallRole.Port)}\n" +
                $"  Starboard wall: {NameOf(WallRole.Starboard)}\n" +
                $"  Forward world vector: {frame.Forward}\n" +
                $"  ShipMotionPivot.forward: {(frame.Pivot != null ? frame.Pivot.forward.ToString() : "(null)")}\n" +
                $"  Calibration restored: {_calibrationRestored}\n" +
                $"  IsCalibrated: {frame.IsCalibrated}");
        }

        private static Vector3 GetRoomFloorCenter(MRUKRoom room)
        {
            var roomBounds = room.GetRoomBounds();
            var roomCenter = roomBounds.center;
            roomCenter.y = room.FloorAnchor != null
                ? room.FloorAnchor.transform.position.y
                : roomBounds.min.y;
            return roomCenter;
        }

        private static VesselHullPresenter systemsHullPresenter(GameObject systems)
        {
            var presenter = systems.GetComponent<VesselHullPresenter>();
            if (presenter == null)
            {
                presenter = FindAnyObjectByType<VesselHullPresenter>();
            }

            if (presenter == null)
            {
                presenter = systems.AddComponent<VesselHullPresenter>();
            }

            return presenter;
        }

        private static void EnsureBridgeSystems(
            out ExteriorWorldMotion motion,
            out ExteriorScenarioLoader loader)
        {
            var systems = GameObject.Find("ShipBridgeSystems");
            if (systems == null)
            {
                systems = new GameObject("ShipBridgeSystems");
            }

            motion = systems.GetComponent<ExteriorWorldMotion>();
            if (motion == null)
            {
                motion = systems.AddComponent<ExteriorWorldMotion>();
            }

            loader = systems.GetComponent<ExteriorScenarioLoader>();
            if (loader == null)
            {
                loader = systems.AddComponent<ExteriorScenarioLoader>();
            }
        }

        private static ShipControlState EnsureControlState(GameObject systems)
        {
            var controlState = ShipControlState.Instance;
            if (controlState == null)
            {
                controlState = FindAnyObjectByType<ShipControlState>();
            }

            if (controlState == null)
            {
                controlState = systems.GetComponent<ShipControlState>();
                if (controlState == null)
                {
                    controlState = systems.AddComponent<ShipControlState>();
                }
            }

            return controlState;
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
            if (!drawRoleGizmos)
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

            // Yellow arrow: ShipMotionPivot → bow (+Z / Front).
            var frame = _referenceFrame != null ? _referenceFrame : BridgeReferenceFrame.Instance;
            if (frame != null && frame.Pivot != null)
            {
                Gizmos.color = Color.yellow;
                var origin = frame.Pivot.position + Vector3.up * 0.05f;
                Gizmos.DrawRay(origin, frame.Forward * gizmoForwardArrowLength);
                Gizmos.DrawSphere(origin + frame.Forward * gizmoForwardArrowLength, 0.06f);
#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.yellow;
                UnityEditor.Handles.Label(origin + frame.Forward * gizmoForwardArrowLength, "BOW +Z");
#endif
            }
        }
    }
}
