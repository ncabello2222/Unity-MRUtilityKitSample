using System;
using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;
using UnityEngine.Rendering;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Sole bridge to Ocean Community Next Gen (plan section 9.2). Spawns the ocean
    /// under a dedicated OceanMotionRoot, drives modular phase + inverse heading/seakeeping,
    /// and exposes height sampling without letting BoatController/Boyant touch the bridge.
    /// </summary>
    public class OceanNextGenAdapter : MonoBehaviour, IOceanSurface
    {
        private const string PrefabResourcePath = "OceanCng";
        private const string UrpWaterMaterialResourcePath = "OceanUrpWater";
        private const string UrpWaterMaterialEditorPath = "Assets/ShipBridgePrototype/Materials/BridgeExteriorWater.mat";

        [SerializeField] private GameObject oceanPrefab;
        [SerializeField] private Material urpWaterMaterial;
        [Tooltip("Legacy flat Water planes from scenario builders. Leave off so ExteriorWorld/Ocean stays visible.")]
        [SerializeField] private bool hideScenarioWaterPlanes = false;
        [Tooltip("Ocean CNG Built-in Mobile/Ocean* shaders are magenta under URP. Keep false and use ExteriorWorld/Ocean for look.")]
        [SerializeField] private bool renderCngVisuals = false;
        [SerializeField] private bool useUrpFallbackMaterial = true;
        [SerializeField] private float waterLevelOffsetY = -15f;
        [Tooltip("When true, VesselHullPresenter overwrites waterLevelOffsetY to match hull draft.")]
        [SerializeField] private bool acceptHullWaterlineDrive = true;
        [Tooltip("Pin ExteriorWorld Ocean/Water mesh to this world Y (visual sea level).")]
        [SerializeField] private bool pinExteriorOceanWorldY = true;
        [SerializeField] private float exteriorOceanWorldY = -15f;
        [SerializeField] private bool applyExteriorWaterMaterial = true;
        [SerializeField] private float seakeepingVisualGain = 1f;
        [Tooltip("Frames of eager URP reapply after spawn. After that, only repair Built-in/magenta tiles.")]
        [SerializeField] private int reapplyUrpMaterialFrames = 90;
        [Tooltip("Keep policing tile shaders every frame so Ocean.matSetVars cannot restore Mobile/Ocean.")]
        [SerializeField] private bool continuousUrpRepair = true;

        private Transform _motionRoot;
        private Transform _phaseProxy;
        private Ocean _ocean;
        private Quaternion _shipForwardBasis = Quaternion.identity;
        private Vector3 _pivotPosition;
        private bool _hasPivot;
        private double _east;
        private double _north;
        private double _headingDeg;
        private float _tileSizeX = 150f;
        private float _tileSizeZ = 150f;
        private int _urpReapplyFramesLeft;
        private Material _urpMat0;
        private Material _urpMat1;
        private Material _urpMat2;

        public static OceanNextGenAdapter Instance { get; private set; }

        public Ocean OceanComponent => _ocean;
        public Transform OceanMotionRoot => _motionRoot;
        public bool IsReady => _ocean != null && _ocean.canCheckBuoyancyNow != null
                              && _ocean.canCheckBuoyancyNow.Length > 0
                              && _ocean.canCheckBuoyancyNow[0] == 1;

        public static OceanNextGenAdapter EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindAnyObjectByType<OceanNextGenAdapter>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var runner = NavigationSimRunner.EnsureInstance();
            return runner.gameObject.AddComponent<OceanNextGenAdapter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            EnsureOceanSpawned();
            BindPivotFromExterior();
            HideScenarioWaterIfNeeded();
            ApplyCngVisualPolicy();
            SyncExteriorOceanVisual();
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
            if (!_hasPivot)
            {
                BindPivotFromExterior();
            }

            if (_ocean != null)
            {
                var runner = NavigationSimRunner.Instance;
                if (runner != null)
                {
                    SetVirtualShipPosition(runner.InterpEast, runner.InterpNorth, runner.InterpPsiDeg);
                    SyncSeaState(runner.Env, runner.InterpHeave, runner.InterpRollDeg, runner.InterpPitchDeg);
                }
                else
                {
                    ApplyOceanPose(0.0, 0.0, 0.0);
                }

                // Only fight Mobile/Ocean → URP when CNG tiles are actually drawn.
                if (renderCngVisuals && useUrpFallbackMaterial)
                {
                    if (_urpReapplyFramesLeft > 0)
                    {
                        _urpReapplyFramesLeft--;
                        ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
                    }
                    else if (continuousUrpRepair)
                    {
                        ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
                    }
                }
            }

            HideScenarioWaterIfNeeded();
            ApplyCngVisualPolicy();
            SyncExteriorOceanVisual();
        }

        public void SetVirtualShipPosition(double east, double north, double headingDeg)
        {
            _east = east;
            _north = north;
            _headingDeg = headingDeg;

            var runner = NavigationSimRunner.Instance;
            double heave = runner != null ? runner.InterpHeave : 0.0;
            double roll = runner != null ? runner.InterpRollDeg : 0.0;
            double pitch = runner != null ? runner.InterpPitchDeg : 0.0;
            ApplyOceanPose(heave, roll, pitch);
        }

        public double SampleHeight(double east, double north, double timeS)
        {
            if (!IsReady)
            {
                return 0.0;
            }

            float x = (float)east;
            float z = (float)north;
            float chop = _ocean.GetChoppyAtLocation2(x, z);
            return _ocean.GetWaterHeightAtLocation2(x - chop, z);
        }

        public Vector3 SampleNormal(double east, double north, double eps = 1.0)
        {
            double h = SampleHeight(east, north, 0.0);
            double hx = SampleHeight(east + eps, north, 0.0);
            double hz = SampleHeight(east, north + eps, 0.0);
            var n = Vector3.Cross(
                new Vector3((float)eps, (float)(hx - h), 0f),
                new Vector3(0f, (float)(hz - h), (float)eps));
            return n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
        }

        /// <summary>Call after ExteriorWorld / scenario swaps so water planes stay hidden.</summary>
        public void NotifyExteriorChanged()
        {
            EnsureOceanSpawned();
            BindPivotFromExterior();
            HideScenarioWaterIfNeeded();
            TrySyncWaterlineFromHull();
            ApplyCngVisualPolicy();
            SyncExteriorOceanVisual();
        }

        /// <summary>
        /// Sets the calm-water plane relative to the ship pivot (meters). Negative = below floor.
        /// </summary>
        public void SetWaterLevelOffsetY(float offsetFromPivotY)
        {
            if (!acceptHullWaterlineDrive)
            {
                return;
            }

            waterLevelOffsetY = offsetFromPivotY;
            if (_hasPivot)
            {
                var runner = NavigationSimRunner.Instance;
                double heave = runner != null ? runner.InterpHeave : 0.0;
                double roll = runner != null ? runner.InterpRollDeg : 0.0;
                double pitch = runner != null ? runner.InterpPitchDeg : 0.0;
                ApplyOceanPose(heave, roll, pitch);
            }
        }

        private void TrySyncWaterlineFromHull()
        {
            var hull = VesselHullPresenter.Instance;
            if (hull == null)
            {
                hull = FindAnyObjectByType<VesselHullPresenter>();
            }

            if (hull != null && !string.IsNullOrEmpty(hull.ActiveVesselId))
            {
                SetWaterLevelOffsetY(hull.DesignWaterLevelOffsetFromPivot);
            }
        }

        private void EnsureOceanSpawned()
        {
            if (_ocean != null)
            {
                return;
            }

            if (oceanPrefab == null)
            {
                oceanPrefab = Resources.Load<GameObject>(PrefabResourcePath);
            }

            if (oceanPrefab == null)
            {
                Debug.LogError("[OceanNextGenAdapter] Missing Ocean prefab (Resources/OceanCng).");
                return;
            }

            var rootGo = new GameObject("OceanMotionRoot");
            _motionRoot = rootGo.transform;

            _phaseProxy = new GameObject("OceanPhaseProxy").transform;
            _phaseProxy.SetParent(_motionRoot, false);

            var instance = Instantiate(oceanPrefab);
            instance.name = "OceanCNG";
            // Keep inactive until URP materials are wired. Ocean.Start/Initialize otherwise
            // bakes Built-in Mobile/Ocean into every tile (magenta under URP on Quest).
            instance.SetActive(false);
            instance.transform.SetParent(_motionRoot, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            _ocean = instance.GetComponent<Ocean>();
            if (_ocean == null)
            {
                Debug.LogError("[OceanNextGenAdapter] Prefab has no Ocean component.");
                Destroy(instance);
                return;
            }

            ConfigureOcean(_ocean);

            if (useUrpFallbackMaterial)
            {
                ApplyUrpFallbackMaterials();
            }

            instance.SetActive(true);

            _tileSizeX = Mathf.Max(1f, _ocean.size.x);
            _tileSizeZ = Mathf.Max(1f, _ocean.size.z);
            _urpReapplyFramesLeft = Mathf.Max(0, reapplyUrpMaterialFrames);

            if (renderCngVisuals && useUrpFallbackMaterial)
            {
                ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
            }

            ApplyCngVisualPolicy();

            Debug.Log("[OceanNextGenAdapter] Ocean CNG spawned under OceanMotionRoot (visuals="
                      + renderCngVisuals + ", URP fallback=" + useUrpFallbackMaterial + ").");
        }

        /// <summary>
        /// Ocean CNG still runs CPU wave/height logic for the sim. Its Mobile/Ocean LOD
        /// materials (L0 foam … L2 blue sea) are Built-in-only and look flat/wrong under URP,
        /// so visuals default off in favor of ExteriorWorld/Ocean.
        /// </summary>
        private void ApplyCngVisualPolicy()
        {
            if (_ocean == null)
            {
                return;
            }

            var renderers = _ocean.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.enabled != renderCngVisuals)
                {
                    r.enabled = renderCngVisuals;
                }
            }
        }

        private void SyncExteriorOceanVisual()
        {
            var exterior = ExteriorWorldRoot.Instance;
            if (exterior == null)
            {
                exterior = FindAnyObjectByType<ExteriorWorldRoot>();
            }

            if (exterior == null)
            {
                return;
            }

            Material waterMat = null;
            if (applyExteriorWaterMaterial)
            {
                EnsureUrpWaterMaterial();
                waterMat = urpWaterMaterial;
            }

            var transforms = exterior.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                {
                    continue;
                }

                // Visual sea surface in scenario prefabs is named "Ocean".
                // Do not treat every "Water*" placeholder the same unless pinning height.
                bool isOceanVisual = t.name == "Ocean";
                bool isWaterPlaceholder = t.name == "Water" || t.name.StartsWith("Water_");
                if (!isOceanVisual && !isWaterPlaceholder)
                {
                    continue;
                }

                if (isOceanVisual && !t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                }

                if (pinExteriorOceanWorldY && (isOceanVisual || !hideScenarioWaterPlanes))
                {
                    var p = t.position;
                    if (!Mathf.Approximately(p.y, exteriorOceanWorldY))
                    {
                        p.y = exteriorOceanWorldY;
                        t.position = p;
                    }
                }

                if (!isOceanVisual || waterMat == null)
                {
                    continue;
                }

                var mr = t.GetComponent<MeshRenderer>();
                if (mr == null)
                {
                    continue;
                }

                // Nested prefab often has a grey Lit instance — replace with the blue water mat.
                if (mr.sharedMaterial != waterMat)
                {
                    mr.sharedMaterial = waterMat;
                }
            }
        }

        private void ConfigureOcean(Ocean ocean)
        {
            ocean.followMainCamera = false;
            ocean.player = _phaseProxy;
            ocean.renderReflection = false;
            ocean.renderRefraction = false;
            ocean.mistEnabled = false;
            ocean.forceDepth = false;
            // Keep Built-in ocean fields aligned with the URP fallback look.
            ocean.waterColor = new Color(0.08f, 0.42f, 0.62f, 1f);
            ocean.surfaceColor = new Color(0.35f, 0.65f, 0.85f, 1f);
            ocean.renderQueue = (int)RenderQueue.Geometry;
            ocean.shaderAlpha = 1f;

            if (ocean.sun == null)
            {
                var light = FindAnyObjectByType<Light>();
                if (light != null && light.type == LightType.Directional)
                {
                    ocean.sun = light.transform;
                }
            }
        }

        private void ApplyUrpFallbackMaterials()
        {
            if (_ocean == null)
            {
                return;
            }

            EnsureUrpWaterMaterial();
            if (urpWaterMaterial == null || urpWaterMaterial.shader == null
                || urpWaterMaterial.shader.name == "Hidden/InternalErrorShader")
            {
                Debug.LogError("[OceanNextGenAdapter] No valid URP water material; ocean will stay magenta under URP.");
                return;
            }

            if (_urpMat0 == null)
            {
                _urpMat0 = new Material(urpWaterMaterial) { name = "OceanURP_L0" };
                _urpMat1 = new Material(urpWaterMaterial) { name = "OceanURP_L1" };
                _urpMat2 = new Material(urpWaterMaterial) { name = "OceanURP_L2" };
            }

            ApplyUrpMaterialsToOcean(_ocean, forceRebuild: true);
        }

        private void EnsureUrpWaterMaterial()
        {
            if (urpWaterMaterial != null && urpWaterMaterial.shader != null
                && urpWaterMaterial.shader.name != "Hidden/InternalErrorShader")
            {
                ConfigureUrpWaterMaterial(urpWaterMaterial);
                return;
            }

            urpWaterMaterial = Resources.Load<Material>(UrpWaterMaterialResourcePath);

#if UNITY_EDITOR
            if (urpWaterMaterial == null)
            {
                urpWaterMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UrpWaterMaterialEditorPath);
            }
#endif

            if (urpWaterMaterial != null)
            {
                // Clone so we never mutate the shared Resources asset.
                urpWaterMaterial = new Material(urpWaterMaterial) { name = "OceanUrpWaterRuntime" };
                ConfigureUrpWaterMaterial(urpWaterMaterial);
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                // Force the URP Lit shader into the build via a known project material.
                var bridgeMat = Resources.Load<Material>("OceanUrpWater");
                if (bridgeMat != null)
                {
                    shader = bridgeMat.shader;
                }
            }

            if (shader == null)
            {
                Debug.LogError("[OceanNextGenAdapter] Shader.Find URP/Lit failed in player.");
                return;
            }

            urpWaterMaterial = new Material(shader) { name = "OceanUrpFallback" };
            ConfigureUrpWaterMaterial(urpWaterMaterial);
        }

        /// <summary>
        /// Opaque URP Lit water. Transparent Mobile→URP ports often draw as a black void
        /// (no depth write + bad queue) when viewed from the bridge windows.
        /// </summary>
        private static void ConfigureUrpWaterMaterial(Material mat)
        {
            if (mat == null)
            {
                return;
            }

            var water = new Color(0.08f, 0.42f, 0.62f, 1f);
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", water);
            }

            mat.color = water;

            if (mat.HasProperty("_Metallic"))
            {
                mat.SetFloat("_Metallic", 0.05f);
            }

            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 0.75f);
            }

            // Force opaque surface so tiles write depth and light correctly under URP.
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 0f);
            }

            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;

            if (mat.HasProperty("_SrcBlend"))
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            }

            if (mat.HasProperty("_DstBlend"))
            {
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 1f);
            }

            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", new Color(0.02f, 0.08f, 0.12f, 1f));
            }
        }

        private void ApplyUrpMaterialsToOcean(Ocean ocean, bool forceRebuild)
        {
            if (ocean == null || _urpMat0 == null)
            {
                return;
            }

            // Ocean.matSetVars does material.shader = oceanShader every init; keep it URP.
            ocean.oceanShader = _urpMat0.shader;
            ocean.useShaderLods = false;
            ocean.material = _urpMat0;
            ocean.material1 = _urpMat1;
            ocean.material2 = _urpMat2;

            if (ocean.mat != null && ocean.mat.Length >= 3)
            {
                ocean.mat[0] = _urpMat0;
                ocean.mat[1] = _urpMat1;
                ocean.mat[2] = _urpMat2;
            }

            var renderers = ocean.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                // Prefer sharedMaterial so we don't spawn per-tile instances that keep Mobile/Ocean.
                if (forceRebuild || NeedsUrpRepair(r.sharedMaterial))
                {
                    r.sharedMaterial = _urpMat0;
                }
            }
        }

        private bool NeedsUrpRepair(Material mat)
        {
            if (mat == null || mat.shader == null)
            {
                return true;
            }

            // Already one of our authored URP fallbacks.
            if (mat == _urpMat0 || mat == _urpMat1 || mat == _urpMat2)
            {
                return false;
            }

            string n = mat.shader.name;
            if (n == "Hidden/InternalErrorShader"
                || n.StartsWith("Mobile/Ocean", StringComparison.Ordinal)
                || n.Contains("OceanL"))
            {
                return true;
            }

            // Ocean.GenerateTiles / mesh paths sometimes leave default grey URP Lit instances.
            if (n.IndexOf("Universal Render Pipeline", StringComparison.Ordinal) >= 0)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    var c = mat.GetColor("_BaseColor");
                    // Default Lit grey, or any leftover transparent surface.
                    if (c.r > 0.45f && c.r < 0.55f && c.g > 0.45f && c.g < 0.55f && c.b > 0.45f && c.b < 0.55f)
                    {
                        return true;
                    }
                }

                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") || mat.renderQueue >= 2500)
                {
                    return true;
                }
            }

            return false;
        }

        private void BindPivotFromExterior()
        {
            var motion = FindAnyObjectByType<ExteriorWorldMotion>();
            if (motion != null && motion.HasInitialPose)
            {
                _pivotPosition = motion.InitialPivotPosition;
                _shipForwardBasis = motion.ShipForwardBasis;
                _hasPivot = true;
                return;
            }

            var exterior = ExteriorWorldRoot.Instance;
            Transform pivot = null;
            if (motion != null && motion.ShipPivot != null)
            {
                pivot = motion.ShipPivot;
            }
            else if (exterior != null)
            {
                pivot = exterior.MotionPivot;
            }
            else if (Camera.main != null)
            {
                pivot = Camera.main.transform;
            }

            if (pivot == null)
            {
                return;
            }

            _pivotPosition = pivot.position;
            var forward = pivot.forward;
            forward.y = 0f;
            _shipForwardBasis = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            _hasPivot = true;
        }

        private void SyncSeaState(EnvironmentState env, double heave, double rollDeg, double pitchDeg)
        {
            if (_ocean == null || env == null)
            {
                return;
            }

            float strength = Mathf.Clamp((float)(env.WaveHeightM * 8.0 + env.WindSpeedMs), 2f, 40f);
            float toRad = (float)((env.WaveFromDeg + 180.0) * Mathf.Deg2Rad);
            _ocean.pWindx = strength * Mathf.Sin(toRad);
            _ocean.pWindy = strength * Mathf.Cos(toRad);
            _ocean.choppy_scale = Mathf.Clamp((float)(env.WaveHeightM * 1.2), 0.2f, 4f);
            _ocean.scale = Mathf.Clamp((float)(0.1 + env.WaveHeightM * 0.15), 0.05f, 1.5f);

            ApplyOceanPose(heave, rollDeg, pitchDeg);
        }

        private void ApplyOceanPose(double heaveM, double rollDeg, double pitchDeg)
        {
            if (_motionRoot == null || !_hasPivot)
            {
                return;
            }

            float gain = seakeepingVisualGain;
            double phaseEast = PositiveMod(_east, _tileSizeX);
            double phaseNorth = PositiveMod(_north, _tileSizeZ);

            var localOffset = new Vector3(
                (float)phaseEast,
                (float)heaveM * gain,
                (float)phaseNorth);

            var attitude = Quaternion.Euler(
                (float)pitchDeg * gain,
                (float)_headingDeg,
                -(float)rollDeg * gain);

            var initialShip = Matrix4x4.TRS(_pivotPosition, _shipForwardBasis, Vector3.one);
            var currentShip = Matrix4x4.TRS(
                _pivotPosition + _shipForwardBasis * localOffset,
                _shipForwardBasis * attitude,
                Vector3.one);

            var initialOcean = Matrix4x4.TRS(
                _pivotPosition + Vector3.up * waterLevelOffsetY,
                _shipForwardBasis,
                Vector3.one);

            var shipDelta = currentShip * initialShip.inverse;
            var oceanMatrix = shipDelta.inverse * initialOcean;
            _motionRoot.SetPositionAndRotation(oceanMatrix.GetColumn(3), oceanMatrix.rotation);

            if (_phaseProxy != null)
            {
                _phaseProxy.position = _pivotPosition;
                _phaseProxy.rotation = _shipForwardBasis;
            }

            if (_ocean != null)
            {
                _ocean.followMainCamera = false;
            }
        }

        private void HideScenarioWaterIfNeeded()
        {
            if (!hideScenarioWaterPlanes)
            {
                return;
            }

            var exterior = ExteriorWorldRoot.Instance;
            if (exterior == null)
            {
                return;
            }

            var transforms = exterior.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                {
                    continue;
                }

                // Only hide generic scenario "Water" placeholders — never ExteriorWorld/Ocean.
                if (t.name == "Water" || t.name.StartsWith("Water_"))
                {
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static double PositiveMod(double value, double modulus)
        {
            if (modulus <= 1e-6)
            {
                return 0.0;
            }

            double r = value % modulus;
            if (r < 0.0)
            {
                r += modulus;
            }

            return r;
        }
    }
}
