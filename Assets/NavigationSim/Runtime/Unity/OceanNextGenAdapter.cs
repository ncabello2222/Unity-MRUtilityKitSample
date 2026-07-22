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
        [SerializeField] private bool hideScenarioWaterPlanes = true;
        [SerializeField] private bool useUrpFallbackMaterial = true;
        [SerializeField] private float waterLevelOffsetY = -1.5f;
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
            if (_ocean == null)
            {
                return;
            }

            if (!_hasPivot)
            {
                BindPivotFromExterior();
            }

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

            HideScenarioWaterIfNeeded();

            if (!useUrpFallbackMaterial || _ocean == null)
            {
                return;
            }

            if (_urpReapplyFramesLeft > 0)
            {
                _urpReapplyFramesLeft--;
                ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
            }
            else if (continuousUrpRepair)
            {
                // Cheap pass: only rewrite renderers that fell back to Built-in Ocean / error shader.
                ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
            }
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

            if (useUrpFallbackMaterial)
            {
                ApplyUrpMaterialsToOcean(_ocean, forceRebuild: false);
            }

            Debug.Log("[OceanNextGenAdapter] Ocean CNG spawned under OceanMotionRoot (URP fallback="
                      + useUrpFallbackMaterial + ").");
        }

        private void ConfigureOcean(Ocean ocean)
        {
            ocean.followMainCamera = false;
            ocean.player = _phaseProxy;
            ocean.renderReflection = false;
            ocean.renderRefraction = false;
            ocean.mistEnabled = false;
            ocean.forceDepth = false;

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

            urpWaterMaterial = new Material(shader)
            {
                name = "OceanUrpFallback",
                color = new Color(0.05f, 0.25f, 0.35f, 0.85f)
            };
            if (urpWaterMaterial.HasProperty("_BaseColor"))
            {
                urpWaterMaterial.SetColor("_BaseColor", new Color(0.05f, 0.25f, 0.35f, 0.85f));
            }

            if (urpWaterMaterial.HasProperty("_Surface"))
            {
                urpWaterMaterial.SetFloat("_Surface", 1f);
            }

            urpWaterMaterial.renderQueue = (int)RenderQueue.Transparent;
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
                if (forceRebuild || IsBuiltInOceanShader(r.sharedMaterial))
                {
                    r.sharedMaterial = _urpMat0;
                }
            }
        }

        private static bool IsBuiltInOceanShader(Material mat)
        {
            if (mat == null || mat.shader == null)
            {
                return true;
            }

            string n = mat.shader.name;
            return n == "Hidden/InternalErrorShader"
                   || n.StartsWith("Mobile/Ocean", StringComparison.Ordinal)
                   || n.Contains("OceanL");
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

                if (t.name == "Water" || t.name.StartsWith("Water"))
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
