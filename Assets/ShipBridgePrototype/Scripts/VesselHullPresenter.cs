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
        [Tooltip("Yaw applied to Blender hulls so bow points with the bridge windows (+Z). Catalog FBX hulls have the bow along model -Z (origin at stern), so 180 aligns bow with the pivot forward.")]
        [SerializeField] private float hullYawOffsetDeg = 180f;

        private GameObject _activeHull;
        private string _activeId;
        private readonly System.Collections.Generic.Dictionary<string, GameObject> _cache =
            new System.Collections.Generic.Dictionary<string, GameObject>();

        public string ActiveVesselId => _activeId;
        public VesselDefinition ActiveDefinition =>
            string.IsNullOrEmpty(_activeId) ? VesselCatalog.All[0] : VesselCatalog.Get(VesselCatalog.IndexOf(_activeId));

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
            // ClearGenerated destroys ShipMotionPivot + VesselHullRoot; Unity fake-nulls
            // the dead hullParent / _activeHull references.
            if (hullParent == null && pivot != null)
            {
                EnsureHullParent();
            }
            else if (hullParent != null && pivot != null && hullParent.parent != pivot)
            {
                // The hull parent may have been created before the bridge pivot existed
                // (fallback under this component). Rebind so the hull follows the
                // bridge reference frame (bow = pivot +Z).
                hullParent.SetParent(pivot, false);
                hullParent.localPosition = Vector3.zero;
                hullParent.localRotation = Quaternion.identity;
                hullParent.localScale = Vector3.one;
            }

            if (string.IsNullOrEmpty(_activeId))
            {
                return;
            }

            // ApplyPose alone is a no-op when regenerate destroyed the hull instance.
            // ShowVessel recreates from cache (or Resources) under the new pivot.
            if (_activeHull == null)
            {
                ShowVessel(_activeId);
            }
            else
            {
                ApplyPose(VesselCatalog.Get(VesselCatalog.IndexOf(_activeId)));
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
            // Place deck below the bridge by BridgeHeightAboveDeckM and push the attach
            // point slightly forward so the bow runs out toward the ocean windows.
            _activeHull.transform.SetParent(hullParent, false);
            _activeHull.transform.localRotation = Quaternion.Euler(0f, hullYawOffsetDeg, 0f);
            _activeHull.transform.localScale = Vector3.one * Mathf.Max(0.01f, def.VisualScale);
            _activeHull.transform.localPosition = new Vector3(
                fineTuneOffset.x,
                -def.BridgeHeightAboveDeckM + fineTuneOffset.y,
                def.HullForwardFromPivotM + fineTuneOffset.z);
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
            if (hullParent != null)
            {
                return;
            }

            if (shipPivot == null)
            {
                ResolvePivot();
            }

            var parent = shipPivot != null ? shipPivot : transform;
            var existing = parent.Find("VesselHullRoot");
            if (existing != null)
            {
                hullParent = existing;
                return;
            }

            var go = new GameObject("VesselHullRoot");
            go.transform.SetParent(parent, false);
            hullParent = go.transform;
        }
    }
}
