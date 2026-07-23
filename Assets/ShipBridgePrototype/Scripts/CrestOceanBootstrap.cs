using UnityEngine;
using WaveHarmonic.Crest;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Spawns Crest with identity rotation (Crest forbids any parent rotation) and
    /// follows <see cref="ExteriorWorldRoot"/> by translation only so coastline height
    /// stays aligned while the ocean stays world-level.
    /// </summary>
    public class CrestOceanBootstrap : MonoBehaviour
    {
        public static CrestOceanBootstrap Instance { get; private set; }

        [Header("Optional prefab")]
        [Tooltip("If set, instantiated instead of building WaterRenderer at runtime.")]
        [SerializeField] private GameObject crestWaterPrefab;

        [Header("Runtime build (used when prefab is null)")]
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material underwaterMaterial;
        [SerializeField] private WaveSpectrum waveSpectrum;

        [Header("Placement")]
        [Tooltip("Sea level relative to ExteriorWorld origin (room floor / ShipMotionPivot). Negative = below bridge.")]
        [SerializeField] private float seaLevelLocalY = -16.5f;

        [Header("Options")]
        [SerializeField] private bool attachWhenExteriorReady = true;
        [SerializeField] private bool destroyPreviousOceanOnAttach = true;
        [SerializeField] private bool addCalmWaves = true;
        [Tooltip("Only enable if you add Clip Surface inputs (hull holes, etc.). Bare ClipLod can fight shoreline visibility.")]
        [SerializeField] private bool enableClipSurface = false;
        [Tooltip("Bridge cameras stay above sea level. Underwater fullscreen wash looks red/orange and can obscure the whole view on Quest if Crest mis-detects water height.")]
        [SerializeField] private bool enableUnderwater = false;

        private WaterRenderer _water;
        private Transform _exteriorRoot;

        public WaterRenderer Water => _water;
        public GameObject CrestWaterPrefab => crestWaterPrefab;

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
            SyncPoseToExterior();
        }

        /// <summary>Called by BridgeRoomMapper after ExteriorWorld exists.</summary>
        public void NotifyExteriorReady(ExteriorWorldRoot root)
        {
            if (!attachWhenExteriorReady || root == null)
            {
                return;
            }

            AttachToExterior(root);
        }

        public bool AttachToExterior(ExteriorWorldRoot root)
        {
            if (root == null)
            {
                Debug.LogWarning("[CrestOceanBootstrap] ExteriorWorld is null.");
                return false;
            }

            return AttachToExterior(root.Root);
        }

        public bool AttachToExterior(Transform exteriorRoot)
        {
            if (exteriorRoot == null)
            {
                Debug.LogWarning("[CrestOceanBootstrap] Exterior root is null.");
                return false;
            }

            // Crest cannot live under ExteriorWorld: that root is yawed toward the bow.
            DetachLegacyOceanUnderExterior(exteriorRoot);

            if (destroyPreviousOceanOnAttach &&
                _water != null &&
                _exteriorRoot != null &&
                _exteriorRoot != exteriorRoot)
            {
                DestroyOceanInstance();
            }

            _exteriorRoot = exteriorRoot;

            if (_water != null)
            {
                EnsureIdentityHierarchy(_water.transform);
                ApplyClipSurface(_water);
                ApplyUnderwaterSetting(_water);
                SyncPoseToExterior();
                return true;
            }

            // Prefer any existing scene-level CrestOcean (e.g. left from a previous attach).
            _water = FindAnyObjectByType<WaterRenderer>();
            if (_water != null)
            {
                EnsureIdentityHierarchy(_water.transform);
                ApplyClipSurface(_water);
                ApplyUnderwaterSetting(_water);
                SyncPoseToExterior();
                Debug.Log("[CrestOceanBootstrap] Reusing existing WaterRenderer (scene root, identity rotation).");
                return true;
            }

            if (crestWaterPrefab != null)
            {
                return InstantiateFromPrefab();
            }

            return BuildRuntimeOcean();
        }

        public void SetPrefab(GameObject prefab)
        {
            crestWaterPrefab = prefab;
        }

        private bool InstantiateFromPrefab()
        {
            var instance = Instantiate(crestWaterPrefab);
            instance.name = "CrestOcean";
            EnsureIdentityHierarchy(instance.transform);

            _water = instance.GetComponentInChildren<WaterRenderer>(true);
            if (_water == null)
            {
                Debug.LogError("[CrestOceanBootstrap] Prefab has no WaterRenderer.", instance);
                return false;
            }

            ApplyClipSurface(_water);
            ApplyUnderwaterSetting(_water);
            SyncPoseToExterior();
            Debug.Log("[CrestOceanBootstrap] Crest ocean spawned at scene root (identity rotation).");
            return true;
        }

        private bool BuildRuntimeOcean()
        {
            if (waterMaterial == null)
            {
                Debug.LogError(
                    "[CrestOceanBootstrap] Assign waterMaterial (Crest Water.mat) or crestWaterPrefab. " +
                    "Menu: Ship Bridge → Setup Crest Ocean.");
                return false;
            }

            var root = new GameObject("CrestOcean");
            EnsureIdentityHierarchy(root.transform);

            _water = root.AddComponent<WaterRenderer>();
            _water.Surface.Material = waterMaterial;
            if (underwaterMaterial != null)
            {
                _water.Underwater.Material = underwaterMaterial;
            }

            ApplyClipSurface(_water);
            ApplyUnderwaterSetting(_water);

            if (addCalmWaves)
            {
                var wavesGo = new GameObject("Waves");
                wavesGo.transform.SetParent(root.transform, false);
                wavesGo.transform.localRotation = Quaternion.identity;
                var fft = wavesGo.AddComponent<ShapeFFT>();
                if (waveSpectrum != null)
                {
                    fft.Spectrum = waveSpectrum;
                }
            }

            SyncPoseToExterior();
            Debug.Log("[CrestOceanBootstrap] Crest ocean built at scene root (identity rotation).");
            return true;
        }

        /// <summary>
        /// Lock Crest sea level to ExteriorWorld vertical motion (world up).
        /// Crest cannot inherit pitch/roll, so we must not use TransformPoint — that
        /// drifts the water vs terrain whenever the exterior tilts. XZ still tracks the
        /// exterior origin; Crest's viewpoint follow handles LOD centering.
        /// </summary>
        private void SyncPoseToExterior()
        {
            if (_water == null || _exteriorRoot == null)
            {
                return;
            }

            var exteriorPos = _exteriorRoot.position;
            var seaPoint = new Vector3(
                exteriorPos.x,
                exteriorPos.y + seaLevelLocalY,
                exteriorPos.z);
            _water.transform.SetPositionAndRotation(seaPoint, Quaternion.identity);
        }

        private static void EnsureIdentityHierarchy(Transform oceanRoot)
        {
            oceanRoot.SetParent(null, true);
            oceanRoot.localScale = Vector3.one;
            oceanRoot.rotation = Quaternion.identity;
        }

        private void ApplyClipSurface(WaterRenderer water)
        {
            if (!enableClipSurface || water == null || water.ClipLod == null)
            {
                return;
            }

            water.ClipLod.Enabled = true;
        }

        private void ApplyUnderwaterSetting(WaterRenderer water)
        {
            if (water == null || water.Underwater == null)
            {
                return;
            }

            water.Underwater.Enabled = enableUnderwater;
        }

        private void DetachLegacyOceanUnderExterior(Transform exteriorRoot)
        {
            var nested = exteriorRoot.GetComponentsInChildren<WaterRenderer>(true);
            for (var i = 0; i < nested.Length; i++)
            {
                var wr = nested[i];
                if (_water == wr)
                {
                    EnsureIdentityHierarchy(wr.transform);
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(wr.gameObject);
                }
                else
                {
                    DestroyImmediate(wr.gameObject);
                }
            }
        }

        private void DestroyOceanInstance()
        {
            if (_water == null)
            {
                return;
            }

            var go = _water.gameObject;
            _water = null;
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }
    }
}
