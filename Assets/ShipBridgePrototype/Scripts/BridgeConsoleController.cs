using System;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Owns the Console_Bridge hierarchy: wall-width stretch, floor-fixed base
    /// column scale, and upper-assembly raise. Does not place itself in the room —
    /// <see cref="BridgeConsolePlacer"/> does that after MRUK is ready.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeConsoleController : MonoBehaviour
    {
        public enum LengthAxis
        {
            Auto,
            X,
            Z
        }

        [Header("Hierarchy")]
        [SerializeField] private Transform consoleBase;
        [SerializeField] private Transform consoleUpper;
        [SerializeField] private Transform consoleMeson;
        [SerializeField] private Transform consoleTeclado;
        [SerializeField] private Transform consoleInclinado;
        [SerializeField] private Transform consoleHandle;
        [SerializeField] private Transform heightHandle;

        [Header("Width (wall length)")]
        [Tooltip("Authored length of the FBX along the wall, in meters.")]
        [SerializeField] private float defaultLengthMeters = 3.6f;
        [SerializeField] private LengthAxis lengthAxis = LengthAxis.Auto;
        [SerializeField] private float wallClearanceMeters = 0.02f;

        [Header("Height")]
        [Tooltip("Default Console_Base height in meters (authored).")]
        [SerializeField] private float defaultBaseHeightMeters = 0.67f;
        [SerializeField] private float minBaseHeightMeters = 0.67f;
        [SerializeField] private float maxBaseHeightMeters = 1.25f;
        [Tooltip("If true, Base pivot is assumed at the floor. If false, compensates a center pivot.")]
        [SerializeField] private bool basePivotAtFloor = true;

        private LengthAxis _resolvedLengthAxis = LengthAxis.X;
        private float _targetBaseHeight;
        private Vector3 _upperStartLocal;
        private Vector3 _baseStartLocal;
        private Vector3 _baseStartScale;
        private bool _hierarchyReady;
        private bool _placed;

        public Transform ConsoleBase => consoleBase;
        public Transform ConsoleUpper => consoleUpper;
        public Transform ConsoleMeson => consoleMeson;
        public Transform ConsoleInclinado => consoleInclinado;
        public Transform ConsoleTeclado => consoleTeclado;
        public Transform ConsoleHandle => consoleHandle;
        public Transform HeightHandle => heightHandle;
        public float TargetBaseHeight => _targetBaseHeight;
        public float DefaultBaseHeight => defaultBaseHeightMeters;
        public float HeightDelta => Mathf.Max(0f, _targetBaseHeight - defaultBaseHeightMeters);
        public float MaxHeightDelta => Mathf.Max(0f, maxBaseHeightMeters - defaultBaseHeightMeters);
        public bool IsPlaced => _placed;
        public LengthAxis ResolvedLengthAxis => _resolvedLengthAxis;

        public event Action<float> HeightChanged;

        private void Awake()
        {
            EnsureHierarchy();
            CaptureStartPose();
            _targetBaseHeight = defaultBaseHeightMeters;
            ApplyHeightInternal();
        }

        /// <summary>
        /// Resolves child names from the FBX, groups the upper desk pieces (including
        /// <c>Console_Handle</c>) under Console_Upper, and wires the height grab host.
        /// </summary>
        public void EnsureHierarchy()
        {
            if (_hierarchyReady && consoleBase != null && consoleUpper != null)
            {
                return;
            }

            if (consoleBase == null)
            {
                consoleBase = FindChildTransform("Console_Base");
            }

            if (consoleMeson == null)
            {
                consoleMeson = FindChildTransform("Console_Meson");
            }

            if (consoleTeclado == null)
            {
                consoleTeclado = FindChildTransform("Console_Teclado");
            }

            if (consoleInclinado == null)
            {
                consoleInclinado = FindChildTransform("Console_Inclinado")
                                  ?? FindChildTransform("Console_Incline");
            }

            if (consoleHandle == null)
            {
                consoleHandle = FindChildTransform("Console_Handle");
            }

            if (consoleBase == null || consoleMeson == null)
            {
                Debug.LogError(
                    "[BridgeConsole] Missing Console_Base / Console_Meson under " + name,
                    this);
                return;
            }

            if (consoleUpper == null)
            {
                var existing = transform.Find("Console_Upper");
                if (existing != null)
                {
                    consoleUpper = existing;
                }
                else
                {
                    var upperGo = new GameObject("Console_Upper");
                    consoleUpper = upperGo.transform;
                    consoleUpper.SetParent(transform, false);
                    consoleUpper.localPosition = Vector3.zero;
                    consoleUpper.localRotation = Quaternion.identity;
                    consoleUpper.localScale = Vector3.one;
                }
            }

            // Everything above the pedestal must ride with Console_Upper when height changes.
            ReparentUnderUpper(consoleMeson);
            ReparentUnderUpper(consoleTeclado);
            ReparentUnderUpper(consoleInclinado);
            ReparentUnderUpper(consoleHandle);
            ReparentUnderUpper(FindChildTransform("Console_Desk"));
            ReparentUnderUpper(FindChildTransform("Console_Body"));
            ReparentUnderUpper(FindChildTransform("Console_Keys"));
            ReparentUnderUpper(FindChildTransform("Console_Incline"));

            EnsureHeightHandle();
            ResolveLengthAxis();
            MeasureDefaultBaseHeightIfNeeded();

            _hierarchyReady = true;
        }

        public void CaptureStartPose()
        {
            if (consoleUpper != null)
            {
                _upperStartLocal = consoleUpper.localPosition;
            }

            if (consoleBase != null)
            {
                _baseStartLocal = consoleBase.localPosition;
                _baseStartScale = consoleBase.localScale;
                if (_baseStartScale.y < 1e-4f)
                {
                    _baseStartScale.y = 1f;
                }
            }
        }

        /// <summary>
        /// Places the root against a wall plane: centered on wall width, on the floor,
        /// facing into the room, stretched to <paramref name="wallWidthMeters"/>.
        /// </summary>
        public void PlaceAgainstWall(
            Vector3 wallCenterWorld,
            Vector3 inwardHorizontal,
            float floorY,
            float wallWidthMeters)
        {
            EnsureHierarchy();
            CaptureStartPose();

            inwardHorizontal.y = 0f;
            if (inwardHorizontal.sqrMagnitude < 1e-6f)
            {
                inwardHorizontal = Vector3.forward;
            }
            else
            {
                inwardHorizontal.Normalize();
            }

            ApplyWidthScale(wallWidthMeters);

            transform.rotation = Quaternion.LookRotation(inwardHorizontal, Vector3.up);

            var pivot = new Vector3(wallCenterWorld.x, floorY, wallCenterWorld.z);
            var backExtent = MeasureBackExtentAlongLocalForward();
            transform.position = pivot + inwardHorizontal * (backExtent + wallClearanceMeters);

            // Re-apply height after scale/pose so base pivot compensation stays correct.
            ApplyHeightInternal();
            RefreshHeightHandlePose();
            _placed = true;
        }

        public void ApplyWidthScale(float wallWidthMeters)
        {
            EnsureHierarchy();
            ResolveLengthAxis();

            var width = Mathf.Max(0.1f, wallWidthMeters);
            var scale = Mathf.Max(0.01f, width / Mathf.Max(0.01f, defaultLengthMeters));

            switch (_resolvedLengthAxis)
            {
                case LengthAxis.Z:
                    transform.localScale = new Vector3(1f, 1f, scale);
                    break;
                default:
                    transform.localScale = new Vector3(scale, 1f, 1f);
                    break;
            }
        }

        public void SetTargetBaseHeight(float heightMeters)
        {
            var clamped = Mathf.Clamp(heightMeters, minBaseHeightMeters, maxBaseHeightMeters);
            // Only raise above the authored default (no sinking below min).
            clamped = Mathf.Max(clamped, minBaseHeightMeters);
            if (Mathf.Abs(clamped - _targetBaseHeight) < 1e-5f)
            {
                return;
            }

            _targetBaseHeight = clamped;
            ApplyHeightInternal();
            HeightChanged?.Invoke(_targetBaseHeight);
        }

        public void SetHeightDelta(float heightDeltaMeters)
        {
            SetTargetBaseHeight(defaultBaseHeightMeters + Mathf.Max(0f, heightDeltaMeters));
        }

        /// <summary>
        /// Kept for callers that previously paused Upper drive during grab. Height is
        /// now always applied to Base + Upper together (grab uses a no-op transformer),
        /// so this is a no-op.
        /// </summary>
        public void SetExternalUpperDrive(bool _)
        {
            // Intentionally empty: grab uses a no-op transformer and drives
            // SetTargetBaseHeight, which always moves Base + Upper together.
        }

        /// <summary>
        /// Reads Console_Upper.local Y as the height delta and applies Base + Upper.
        /// </summary>
        public void SyncHeightFromUpperPosition()
        {
            if (consoleUpper == null)
            {
                return;
            }

            var delta = consoleUpper.localPosition.y - _upperStartLocal.y;
            SetTargetBaseHeight(defaultBaseHeightMeters + delta);
        }

        /// <summary>
        /// Keeps the grab collider on the authored <c>Console_Handle</c> mesh when present.
        /// Synthetic proxies snap to that mesh (or the meson lip) and stay under Upper so
        /// they rise with the desk.
        /// </summary>
        public void RefreshHeightHandlePose()
        {
            EnsureHeightHandleObject();
            if (heightHandle == null)
            {
                return;
            }

            // Authored FBX handle: leave the mesh where the artist put it.
            if (IsAuthoredHandleMesh(heightHandle))
            {
                if (consoleUpper != null && heightHandle.parent != consoleUpper)
                {
                    heightHandle.SetParent(consoleUpper, worldPositionStays: true);
                }

                return;
            }

            // Synthetic proxy: sit on the visual handle, else the room-facing meson lip.
            Transform anchor = consoleHandle != null ? consoleHandle : consoleMeson;
            if (anchor == null)
            {
                return;
            }

            var worldBounds = GetWorldRendererBounds(anchor);
            Vector3 pose;
            if (consoleHandle != null && anchor == consoleHandle)
            {
                pose = worldBounds.center;
            }
            else
            {
                pose = worldBounds.ClosestPoint(worldBounds.center + transform.forward * 10f);
                pose.y = Mathf.Lerp(worldBounds.min.y, worldBounds.max.y, 0.35f);
            }

            if (consoleUpper != null && heightHandle.parent != consoleUpper)
            {
                heightHandle.SetParent(consoleUpper, worldPositionStays: true);
            }

            heightHandle.position = pose;
            heightHandle.rotation = transform.rotation;
            heightHandle.localScale = Vector3.one;
        }

        private void ApplyHeightInternal()
        {
            if (consoleBase == null || consoleUpper == null)
            {
                return;
            }

            var heightScale = _targetBaseHeight / Mathf.Max(1e-4f, defaultBaseHeightMeters);
            var baseScale = _baseStartScale;
            baseScale.y = _baseStartScale.y * heightScale;
            consoleBase.localScale = baseScale;

            if (basePivotAtFloor)
            {
                consoleBase.localPosition = _baseStartLocal;
            }
            else
            {
                // Center-pivot compensation: keep the bottom face on the floor.
                var p = _baseStartLocal;
                p.y = _targetBaseHeight * 0.5f;
                consoleBase.localPosition = p;
            }

            // Always raise the whole upper assembly (desk, screens, handle) with Base.
            var upper = _upperStartLocal;
            upper.y = _upperStartLocal.y + HeightDelta;
            consoleUpper.localPosition = upper;

            // Keep a synthetic grab proxy glued to the handle/lip after the raise.
            if (heightHandle != null && !IsAuthoredHandleMesh(heightHandle))
            {
                RefreshHeightHandlePose();
            }
        }

        private void EnsureHeightHandle()
        {
            EnsureHeightHandleObject();
            RefreshHeightHandlePose();
        }

        private void EnsureHeightHandleObject()
        {
            if (consoleHandle == null)
            {
                consoleHandle = FindChildTransform("Console_Handle");
            }

            // Prefer the authored mango mesh as the grab host.
            if (heightHandle == null && consoleHandle != null)
            {
                heightHandle = consoleHandle;
            }

            if (heightHandle == null)
            {
                var existing = transform.Find("HeightHandle");
                if (existing == null && consoleUpper != null)
                {
                    existing = consoleUpper.Find("HeightHandle");
                }

                if (existing == null && consoleMeson != null)
                {
                    existing = consoleMeson.Find("HeightHandle");
                }

                if (existing != null)
                {
                    heightHandle = existing;
                }
                else if (consoleMeson != null)
                {
                    var handleGo = new GameObject("HeightHandle");
                    heightHandle = handleGo.transform;
                }
            }

            if (heightHandle == null)
            {
                return;
            }

            if (consoleUpper != null && heightHandle.parent != consoleUpper)
            {
                heightHandle.SetParent(consoleUpper, worldPositionStays: true);
            }

            if (heightHandle.GetComponent<BoxCollider>() == null)
            {
                heightHandle.gameObject.AddComponent<BoxCollider>();
            }
        }

        private static bool IsAuthoredHandleMesh(Transform handle)
        {
            return handle != null && handle.name == "Console_Handle";
        }

        private static Bounds GetWorldRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(root.position, Vector3.one * 0.2f);
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private void ReparentUnderUpper(Transform child)
        {
            if (child == null || child.parent == consoleUpper)
            {
                return;
            }

            child.SetParent(consoleUpper, true);
        }

        private void ResolveLengthAxis()
        {
            if (lengthAxis != LengthAxis.Auto)
            {
                _resolvedLengthAxis = lengthAxis;
                return;
            }

            var bounds = GetCombinedLocalBounds();
            _resolvedLengthAxis = bounds.size.x >= bounds.size.z ? LengthAxis.X : LengthAxis.Z;
        }

        private void MeasureDefaultBaseHeightIfNeeded()
        {
            if (consoleBase == null)
            {
                return;
            }

            // Keep the inspector default (≈0.67 m) unless it was left unset.
            if (defaultBaseHeightMeters <= 0.05f)
            {
                var bounds = GetRendererLocalBounds(consoleBase);
                if (bounds.size.y > 0.05f)
                {
                    defaultBaseHeightMeters = bounds.size.y;
                    minBaseHeightMeters = defaultBaseHeightMeters;
                }
            }

            DetectBasePivot();
        }

        private void DetectBasePivot()
        {
            if (consoleBase == null)
            {
                return;
            }

            var bounds = GetRendererLocalBounds(consoleBase);
            // If the mesh sits mostly above the local origin, pivot is at the floor.
            basePivotAtFloor = bounds.min.y >= -0.05f;
        }

        private float MeasureBackExtentAlongLocalForward()
        {
            var bounds = GetCombinedLocalBounds();
            // Local +Z is forward into the room after LookRotation(inward).
            // Back face is the minimum local Z; distance from pivot to that face.
            return Mathf.Max(0f, -bounds.min.z);
        }

        private Bounds GetCombinedLocalBounds()
        {
            var parts = new[]
            {
                consoleBase, consoleMeson, consoleTeclado, consoleInclinado, consoleHandle
            };
            var has = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                var b = GetRendererLocalBounds(part);
                // Convert part-local bounds into root-local space (approx via lossy positions).
                var worldCenter = part.TransformPoint(b.center);
                var localCenter = transform.InverseTransformPoint(worldCenter);
                var worldSize = Vector3.Scale(b.size, part.lossyScale);
                var rootScale = transform.lossyScale;
                var localSize = new Vector3(
                    safeDiv(worldSize.x, rootScale.x),
                    safeDiv(worldSize.y, rootScale.y),
                    safeDiv(worldSize.z, rootScale.z));

                var partBounds = new Bounds(localCenter, localSize);
                if (!has)
                {
                    bounds = partBounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(partBounds);
                }
            }

            if (!has)
            {
                bounds = new Bounds(Vector3.zero, new Vector3(defaultLengthMeters, defaultBaseHeightMeters, 0.6f));
            }

            return bounds;

            static float safeDiv(float a, float b) => Mathf.Abs(b) < 1e-5f ? a : a / b;
        }

        private static Bounds GetRendererLocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.2f);
            }

            var bounds = renderers[0].localBounds;
            var first = true;
            foreach (var r in renderers)
            {
                // localBounds is in the renderer's local space; convert into root local.
                var b = r.localBounds;
                var worldCorners = new Vector3[8];
                var e = b.extents;
                var c = b.center;
                var i = 0;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    worldCorners[i++] = r.transform.TransformPoint(c + Vector3.Scale(e, new Vector3(x, y, z)));
                }

                foreach (var corner in worldCorners)
                {
                    var local = root.InverseTransformPoint(corner);
                    if (first)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return bounds;
        }

        private Transform FindChildTransform(string objectName)
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t != null && t.name == objectName)
                {
                    return t;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minBaseHeightMeters = Mathf.Max(0.05f, minBaseHeightMeters);
            maxBaseHeightMeters = Mathf.Max(minBaseHeightMeters, maxBaseHeightMeters);
            defaultBaseHeightMeters = Mathf.Clamp(defaultBaseHeightMeters, minBaseHeightMeters, maxBaseHeightMeters);
            defaultLengthMeters = Mathf.Max(0.1f, defaultLengthMeters);
        }
#endif
    }
}
