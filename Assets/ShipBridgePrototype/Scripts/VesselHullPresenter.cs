using NavigationSim.Core;
using NavigationSim.UnityLayer;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Instantiates the selected Blender hull under the fixed ship pivot so it
    /// stays with the bridge while the exterior world moves. The hull sits
    /// below the bridge floor and extends forward toward the ocean.
    /// </summary>
    public class VesselHullPresenter : MonoBehaviour
    {
        public static VesselHullPresenter Instance { get; private set; }

        [SerializeField] private Transform shipPivot;
        [SerializeField] private Transform hullParent;
        [Tooltip("Extra lateral/vertical nudge after catalog offsets [m].")]
        [SerializeField] private Vector3 fineTuneOffset;

        private GameObject _activeHull;
        private string _activeId;
        private readonly System.Collections.Generic.Dictionary<string, GameObject> _cache =
            new System.Collections.Generic.Dictionary<string, GameObject>();

        public string ActiveVesselId => _activeId;
        public VesselDefinition ActiveDefinition =>
            string.IsNullOrEmpty(_activeId) ? VesselCatalog.All[0] : VesselCatalog.Get(VesselCatalog.IndexOf(_activeId));

        /// <summary>
        /// World-Y offset from the ship pivot where the ocean surface should sit so the
        /// active hull floats with a believable freeboard (deck above water).
        /// </summary>
        public float DesignWaterLevelOffsetFromPivot
        {
            get
            {
                var def = ActiveDefinition;
                float draft = GetDesignDraftM(def);
                // Deck at local Y≈0 → world Y = pivotY - BridgeHeightAboveDeck.
                // Waterline is one draft above the keel; keel ≈ mesh bounds min Y.
                float keelLocalY = GetMeshKeelLocalY();
                float waterLocalY = keelLocalY + draft;
                return -def.BridgeHeightAboveDeckM + waterLocalY;
            }
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

        private void Start()
        {
            ResolvePivot();
            var runner = NavigationSimRunner.Instance;
            if (runner != null && !string.IsNullOrEmpty(runner.ActiveVesselId))
            {
                ShowVessel(runner.ActiveVesselId);
            }
            else
            {
                ShowVessel(VesselCatalog.All[0].Id);
            }
        }

        public void BindPivot(Transform pivot)
        {
            shipPivot = pivot;
            // Always attach under the oriented ship pivot so hull +Z follows the
            // front-window forward (not the unrotated ShipBridgeSystems root).
            EnsureHullParent();

            if (!string.IsNullOrEmpty(_activeId))
            {
                ApplyPose(VesselCatalog.Get(VesselCatalog.IndexOf(_activeId)));
            }
            else
            {
                NotifyOceanWaterline();
            }
        }

        public void ShowVessel(string vesselId)
        {
            var def = VesselCatalog.Get(VesselCatalog.IndexOf(vesselId));
            ResolvePivot();
            EnsureHullParent();

            if (_activeHull != null)
            {
                _activeHull.SetActive(false);
            }

            if (!_cache.TryGetValue(def.Id, out var hull) || hull == null)
            {
                hull = CreateHullInstance(def);
                if (hull == null)
                {
                    Debug.LogWarning($"[VesselHullPresenter] Missing hull resource '{def.HullResourcePath}'.");
                    return;
                }

                _cache[def.Id] = hull;
            }

            _activeHull = hull;
            _activeId = def.Id;
            _activeHull.SetActive(true);
            ApplyPose(def);
        }

        private GameObject CreateHullInstance(VesselDefinition def)
        {
            var prefab = Resources.Load<GameObject>(def.HullResourcePath);
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, hullParent, false);
            instance.name = $"Hull_{def.Id}";
            OptimizeForUnity(instance);
            return instance;
        }

        private void NotifyOceanWaterline()
        {
            var ocean = OceanNextGenAdapter.EnsureInstance();
            if (ocean != null)
            {
                ocean.SetWaterLevelOffsetY(DesignWaterLevelOffsetFromPivot);
            }
        }

        private static float GetDesignDraftM(VesselDefinition def)
        {
            try
            {
                var cfg = def.CreateConfig != null ? def.CreateConfig() : null;
                if (cfg == null)
                {
                    return 5.5f;
                }

                if (cfg.Clarke.T > 0.5)
                {
                    return (float)cfg.Clarke.T;
                }

                if (cfg.MmgBasic.d > 0.5)
                {
                    return (float)cfg.MmgBasic.d;
                }
            }
            catch
            {
                // Fall through to default estimate.
            }

            return 5.5f;
        }

        private float GetMeshKeelLocalY()
        {
            if (_activeHull == null)
            {
                return -6f;
            }

            var filters = _activeHull.GetComponentsInChildren<MeshFilter>(true);
            var minY = float.PositiveInfinity;
            for (int i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i] != null ? filters[i].sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                minY = Mathf.Min(minY, mesh.bounds.min.y);
            }

            return float.IsInfinity(minY) ? -6f : minY;
        }

        private static void OptimizeForUnity(GameObject root)
        {
            // Strip colliders — visual-only from the bridge.
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                Destroy(col);
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            // Prefer URP Lit if available; otherwise keep imported materials.
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null)
                    {
                        continue;
                    }

                    var mat = new Material(urpLit);
                    mat.name = src.name + "_URP";
                    if (src.HasProperty("_Color"))
                    {
                        mat.SetColor("_BaseColor", src.color);
                    }
                    else if (src.HasProperty("_BaseColor"))
                    {
                        mat.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                    }
                    else
                    {
                        mat.SetColor("_BaseColor", new Color(0.35f, 0.38f, 0.42f, 1f));
                    }

                    mat.SetFloat("_Smoothness", 0.25f);
                    mat.SetFloat("_Metallic", 0.15f);
                    mats[i] = mat;
                }

                renderer.sharedMaterials = mats;
            }
        }

        private void ApplyPose(VesselDefinition def)
        {
            if (_activeHull == null || hullParent == null)
            {
                return;
            }

            // Deck at local Y≈0 in the FBX. Bridge floor is at the pivot (room floor).
            // HullYawDeg (default 180) flips Blender -Z hulls so the bow/deck runs out
            // the front windows; then nudge slightly forward of the room center.
            _activeHull.transform.SetParent(hullParent, false);
            _activeHull.transform.localRotation = Quaternion.Euler(0f, def.HullYawDeg, 0f);
            _activeHull.transform.localScale = Vector3.one * Mathf.Max(0.01f, def.VisualScale);
            _activeHull.transform.localPosition = new Vector3(
                fineTuneOffset.x,
                -def.BridgeHeightAboveDeckM + fineTuneOffset.y,
                def.HullForwardFromPivotM + fineTuneOffset.z);

            NotifyOceanWaterline();
        }

        private void ResolvePivot()
        {
            if (shipPivot != null)
            {
                return;
            }

            var motion = FindAnyObjectByType<ExteriorWorldMotion>();
            if (motion != null && motion.ShipPivot != null)
            {
                shipPivot = motion.ShipPivot;
                return;
            }

            var named = GameObject.Find("ShipMotionPivot");
            if (named != null)
            {
                shipPivot = named.transform;
            }
        }

        private void EnsureHullParent()
        {
            if (shipPivot == null)
            {
                ResolvePivot();
            }

            var parent = shipPivot != null ? shipPivot : transform;

            if (hullParent == null)
            {
                var existing = parent.Find("VesselHullRoot");
                if (existing == null)
                {
                    // Also reclaim a hull root created early under this component.
                    var orphan = transform.Find("VesselHullRoot");
                    existing = orphan != null ? orphan : GameObject.Find("VesselHullRoot")?.transform;
                }

                if (existing != null)
                {
                    hullParent = existing;
                }
                else
                {
                    var go = new GameObject("VesselHullRoot");
                    hullParent = go.transform;
                }
            }

            if (hullParent.parent != parent)
            {
                hullParent.SetParent(parent, false);
            }

            hullParent.localPosition = Vector3.zero;
            hullParent.localRotation = Quaternion.identity;
            hullParent.localScale = Vector3.one;
        }
    }
}
