using System;
using System.Collections.Generic;
using System.Reflection;
using Meta.Utilities.Environment;
using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;
using UnityEngine.Rendering;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Bridge to the North Star / Meta Utilities iFFT ocean.
    /// Keeps the ocean mesh centered on the bridge, drives wind/spectrum from
    /// <see cref="EnvironmentState"/>, translates the wave field rigidly with the
    /// same inverse-exterior transform the terrain uses (spectral phase shift),
    /// and feeds <see cref="WaveResponseModel"/> height samples for seakeeping.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class NorthStarOceanAdapter : MonoBehaviour, IOceanSurface
    {
        private const string OceanMaterialResourcePath = "NorthStarCalmOcean";

        private static readonly int OceanRcpScaleId = Shader.PropertyToID("_OceanRcpScale");
        private static readonly int OceanChoppynessId = Shader.PropertyToID("_OceanChoppyness");
        private static readonly int OceanDisplacementId = Shader.PropertyToID("_OceanDisplacement");
        private static readonly int OceanNormalId = Shader.PropertyToID("_OceanNormal");
        private static readonly int OceanVisAlbedoId = Shader.PropertyToID("_OceanVisAlbedo");
        private static readonly int OceanAlbedoColorId = Shader.PropertyToID("_OceanAlbedoColor");
        private static readonly int GiantWaveOffsetId = Shader.PropertyToID("_GiantWaveOffset");
        private static readonly int SmoothnessCloseId = Shader.PropertyToID("_Smoothness_Close");

        [SerializeField] private Material oceanMaterial;
        [SerializeField] private bool hideScenarioWaterPlanes = true;
        [Tooltip("Ocean surface Y relative to ShipMotionPivot. Bridge floor≈0; hull deck≈-BridgeHeight; waterline a few meters below deck.")]
        [SerializeField] private float waterLevelOffsetY = -14f;
        [Tooltip("When true, waterline tracks the active vessel (deck height + freeboard).")]
        [SerializeField] private bool autoWaterlineFromHull = true;
        [SerializeField] private float freeboardBelowDeckM = 2.5f;
        [SerializeField] private int simulationResolution = 128;
        [SerializeField] private float oceanSize = 1024f;
        [Tooltip("When true, translates the FFT field with the exterior-world transform so crests stay glued to the terrain while the bridge stays fixed.")]
        [SerializeField] private bool scrollWavesWithVirtualPosition = true;
        [Tooltip("Drive WaveResponseModel from iFFT height samples (Phase 3).")]
        [SerializeField] private bool bindSeakeepingToSurface = true;

        private Transform _oceanRoot;
        private OceanSimulation _oceanSimulation;
        private QuadtreeRenderer _quadtreeRenderer;
        private ExteriorWorldMotion _motion;
        private EnvironmentProfile _profile;
        private MaterialPropertyBlock _propertyBlock;
        private Vector3 _pivotPosition;
        private Quaternion _shipForwardBasis = Quaternion.identity;
        private bool _hasPivot;
        private double _east;
        private double _north;
        private double _headingDeg;
        private float _patchSize = 64f;
        private float _driveWindSpeed = 8f;
        private float _driveFromDeg;
        private bool _renderHooked;
        private bool _jobsCompletedThisFrame;

        public static NorthStarOceanAdapter Instance { get; private set; }

        public Transform OceanRoot => _oceanRoot;
        public OceanSimulation OceanSimulation => _oceanSimulation;
        public bool IsReady => _oceanSimulation != null
                              && _oceanSimulation.DisplacementMap != null
                              && _profile != null
                              && _profile.OceanMaterial != null;

        public static NorthStarOceanAdapter EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindAnyObjectByType<NorthStarOceanAdapter>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var runner = NavigationSimRunner.EnsureInstance();
            return runner.gameObject.AddComponent<NorthStarOceanAdapter>();
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
            BindSeakeeping();
        }

        private void OnEnable()
        {
            if (!_renderHooked)
            {
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                _renderHooked = true;
            }
        }

        private void OnDisable()
        {
            if (_renderHooked)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                _renderHooked = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }

        private void Update()
        {
            // Keep the sampling origin on the live sim pose so bow/stern probes
            // stay coherent while NavigationSimRunner sub-steps inside this frame.
            var runner = NavigationSimRunner.Instance;
            if (runner?.Sim == null)
            {
                return;
            }

            _east = runner.Sim.State.East;
            _north = runner.Sim.State.North;
            _headingDeg = runner.Sim.State.HeadingDeg;
        }

        private void LateUpdate()
        {
            if (_oceanSimulation == null)
            {
                return;
            }

            _jobsCompletedThisFrame = false;

            if (!_hasPivot)
            {
                BindPivotFromExterior();
            }

            var runner = NavigationSimRunner.Instance;
            if (runner != null)
            {
                SetVirtualShipPosition(runner.InterpEast, runner.InterpNorth, runner.InterpPsiDeg);
                SyncSeaState(runner.Env);
                BindSeakeeping();
            }

            UpdateOceanPose();
            TickSimulation();
        }

        public void SetVirtualShipPosition(double east, double north, double headingDeg)
        {
            _east = east;
            _north = north;
            _headingDeg = headingDeg;

            if (_oceanSimulation == null || !scrollWavesWithVirtualPosition)
            {
                return;
            }

            // Exact rigid translation: shift the FFT field by the same world-space
            // offset the terrain received at the bridge (spectral phase e^(iK·D)).
            // Heading is carried by BuildWindVector, whose deterministic per-cell
            // phases morph the spectrum smoothly instead of re-randomizing it.
            Vector3 shift;
            if (_motion != null && _motion.HasInitialPose)
            {
                shift = _motion.ComputeWorldShiftAt(_pivotPosition);
            }
            else
            {
                var yaw = Quaternion.AngleAxis(-(float)headingDeg, Vector3.up);
                shift = -(_shipForwardBasis * (yaw * new Vector3((float)east, 0f, (float)north)));
            }

            // T(u) = f(u + D): features move by -D, so D = -shift tracks the terrain.
            _oceanSimulation.FieldOffset = new Vector2(-shift.x, -shift.z);
            Shader.SetGlobalVector(GiantWaveOffsetId, new Vector4(shift.x, 0f, shift.z, 0f));
        }

        public double SampleHeight(double east, double north, double timeS)
        {
            if (!IsReady)
            {
                return 0.0;
            }

            EnsureDisplacementReady();

            // Sample in the frame the mesh is drawn: geo → current Unity world via
            // the inverse-exterior transform, then world XZ / patch as UV, exactly
            // like the shader. The field translation is baked into the texture.
            Vector3 world;
            if (_motion != null && _motion.HasInitialPose)
            {
                world = _motion.GeoToWorld(east, north);
            }
            else
            {
                float localE = (float)(east - _east);
                float localN = (float)(north - _north);
                world = _pivotPosition + _shipForwardBasis * new Vector3(localE, 0f, localN);
            }

            return SampleHeightIterative(new Vector3(world.x, 0f, world.z), 4);
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

        public void NotifyExteriorChanged()
        {
            EnsureOceanSpawned();
            BindPivotFromExterior();
            HideScenarioWaterIfNeeded();
        }

        private void EnsureOceanSpawned()
        {
            if (_oceanSimulation != null)
            {
                return;
            }

            EnsureOceanMaterial();
            if (oceanMaterial == null)
            {
                Debug.LogError("[NorthStarOceanAdapter] Missing NorthStar ocean material (Resources/NorthStarCalmOcean).");
                return;
            }

            var rootGo = new GameObject("OceanRoot");
            _oceanRoot = rootGo.transform;
            float y0 = ResolveWaterLevelOffsetY();
            if (_hasPivot)
            {
                _oceanRoot.position = _pivotPosition + Vector3.up * y0;
            }
            else
            {
                _oceanRoot.position = Vector3.up * y0;
            }

            _profile = ScriptableObject.CreateInstance<EnvironmentProfile>();
            _profile.name = "RuntimeNorthStarOceanProfile";
            SetAutoProperty(_profile, "OceanMaterial", oceanMaterial);
            SetAutoProperty(_profile, "OceanSettings", new OceanSettings());

            var simGo = new GameObject("OceanSimulation");
            simGo.transform.SetParent(_oceanRoot, false);
            simGo.SetActive(false);
            _oceanSimulation = simGo.AddComponent<OceanSimulation>();
            SetPrivateField(_oceanSimulation, "m_resolution", Mathf.ClosestPowerOfTwo(Mathf.Clamp(simulationResolution, 32, 512)));
            _oceanSimulation.Profile = _profile;
            simGo.SetActive(true);

            var qtGo = new GameObject("OceanQuadtree");
            qtGo.transform.SetParent(_oceanRoot, false);
            qtGo.SetActive(false);
            _quadtreeRenderer = qtGo.AddComponent<QuadtreeRenderer>();
            _quadtreeRenderer.Material = oceanMaterial;
            SetPrivateField(_quadtreeRenderer, "m_size", oceanSize);
            _quadtreeRenderer.Version++;
            qtGo.SetActive(true);

            _propertyBlock = new MaterialPropertyBlock();
            _patchSize = Mathf.Max(1f, _profile.OceanSettings.PatchSize);

            Debug.Log("[NorthStarOceanAdapter] North Star iFFT ocean spawned under OceanRoot.");
        }

        private void EnsureOceanMaterial()
        {
            if (oceanMaterial != null && oceanMaterial.shader != null
                && oceanMaterial.shader.name != "Hidden/InternalErrorShader")
            {
                return;
            }

            oceanMaterial = Resources.Load<Material>(OceanMaterialResourcePath);

#if UNITY_EDITOR
            if (oceanMaterial == null)
            {
                oceanMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Packages/com.meta.utilities.environment/Runtime/Data/Environment/Ocean/Calm Ocean Day.mat");
            }
#endif

            if (oceanMaterial != null)
            {
                // Instance so runtime property tweaks do not dirty the shared asset.
                oceanMaterial = new Material(oceanMaterial) { name = "NorthStarOceanRuntime" };
            }
        }

        private void TickSimulation()
        {
            if (_oceanSimulation == null || _profile == null)
            {
                return;
            }

            _oceanSimulation.Profile = _profile;
            _oceanSimulation.UpdateSimulation(BuildWindVector());
        }

        private Vector3 BuildWindVector()
        {
            // Relative to bridge heading so geographic wave direction reads correctly
            // while ExteriorWorldRoot rotates inversely.
            float fromRel = _driveFromDeg - (float)_headingDeg;
            float rad = fromRel * Mathf.Deg2Rad;
            // Wind-from → blow-toward vector for the spectrum job.
            var local = new Vector3(Mathf.Sin(rad + Mathf.PI), 0f, Mathf.Cos(rad + Mathf.PI)) * _driveWindSpeed;
            return _shipForwardBasis * local;
        }

        private void SyncSeaState(EnvironmentState env)
        {
            if (env == null || _profile?.OceanSettings == null || oceanMaterial == null)
            {
                return;
            }

            var drive = OceanSeaStateMapper.FromEnvironment(env);
            _driveWindSpeed = drive.WindSpeedMs;
            _driveFromDeg = drive.WindFromDeg;

            var settings = _profile.OceanSettings;
            bool changed = !Mathf.Approximately(settings.WindSpeed, drive.WindSpeedMs)
                           || !Mathf.Approximately(settings.Directionality, drive.Directionality)
                           || !Mathf.Approximately(settings.Choppyness, drive.Choppyness)
                           || !Mathf.Approximately(settings.PatchSize, drive.PatchSizeM)
                           || !Mathf.Approximately(settings.MinWaveSize, drive.MinWaveSize);

            if (changed)
            {
                settings.Apply(
                    drive.WindSpeedMs,
                    drive.Directionality,
                    drive.Choppyness,
                    drive.PatchSizeM,
                    drive.MinWaveSize);
                _profile.Version++;
            }

            SetAutoProperty(_profile, "WindYaw", Mathf.Repeat(drive.WindFromDeg, 360f));
            SetAutoProperty(_profile, "WindPitch", 90f);

            float hs = Mathf.Clamp01((float)(env.WaveHeightM / 6.0));
            if (oceanMaterial.HasProperty("_Foam_Crest_Offset"))
            {
                oceanMaterial.SetFloat("_Foam_Crest_Offset", Mathf.Lerp(0.08f, 0.02f, hs));
            }

            _patchSize = Mathf.Max(1f, settings.PatchSize);
            if (_profile.OceanMaterial != oceanMaterial)
            {
                SetAutoProperty(_profile, "OceanMaterial", oceanMaterial);
            }
        }

        private void BindSeakeeping()
        {
            if (!bindSeakeepingToSurface)
            {
                return;
            }

            var runner = NavigationSimRunner.Instance;
            if (runner?.Sim?.Waves == null)
            {
                return;
            }

            runner.Sim.Waves.Surface = this;
            runner.Sim.Waves.UseSurfaceSampling = IsReady;
        }

        private void UpdateOceanPose()
        {
            if (_oceanRoot == null || !_hasPivot)
            {
                return;
            }

            float yOffset = ResolveWaterLevelOffsetY();
            // Mesh stays centered on the bridge; only waterline height is applied.
            _oceanRoot.position = _pivotPosition + Vector3.up * yOffset;
            _oceanRoot.rotation = Quaternion.identity;
        }

        private float ResolveWaterLevelOffsetY()
        {
            if (!autoWaterlineFromHull)
            {
                return waterLevelOffsetY;
            }

            var presenter = VesselHullPresenter.Instance;
            if (presenter == null)
            {
                presenter = FindAnyObjectByType<VesselHullPresenter>();
            }

            if (presenter == null)
            {
                return waterLevelOffsetY;
            }

            var def = presenter.ActiveDefinition;
            if (def == null)
            {
                return waterLevelOffsetY;
            }

            // Deck sits at -BridgeHeightAboveDeckM; put mean sea level a bit below deck.
            return -def.BridgeHeightAboveDeckM - Mathf.Max(0.5f, freeboardBelowDeckM);
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!IsReady || _quadtreeRenderer == null || !_quadtreeRenderer.isActiveAndEnabled)
            {
                return;
            }

            _oceanSimulation.BeginContextRendering();

            _propertyBlock.SetFloat(OceanRcpScaleId, 1.0f / _patchSize);
            _propertyBlock.SetFloat(OceanChoppynessId, _profile.OceanSettings.Choppyness);
            _propertyBlock.SetTexture(OceanDisplacementId, _oceanSimulation.DisplacementMap);
            _propertyBlock.SetTexture(OceanNormalId, _oceanSimulation.NormalMap);

            if (oceanMaterial.HasProperty(OceanVisAlbedoId))
            {
                Shader.SetGlobalColor(OceanAlbedoColorId, oceanMaterial.GetColor(OceanVisAlbedoId));
            }

            // Quiet unused-property warning path used by OceanSimulation smoothness sampling.
            if (!oceanMaterial.HasProperty(SmoothnessCloseId))
            {
                // Material from NorthStar includes it; nothing to do when absent.
            }

            float oceanTexelsPerMeter = _oceanSimulation.Resolution / _patchSize;
            _quadtreeRenderer.Material = oceanMaterial;
            _quadtreeRenderer.BeginContextRendering(cameras, _propertyBlock, oceanTexelsPerMeter);
        }

        private void EnsureDisplacementReady()
        {
            if (_jobsCompletedThisFrame || _oceanSimulation == null)
            {
                return;
            }

            _oceanSimulation.BeginContextRendering();
            _jobsCompletedThisFrame = true;
        }

        private float SampleHeightIterative(Vector3 position, int depth)
        {
            var map = _oceanSimulation.DisplacementMap;
            if (map == null)
            {
                return 0f;
            }

            // Wave elevation relative to mean sea level (not the visual waterline offset).
            float height = 0f;
            for (int i = 0; i < depth; i++)
            {
                var uv = new Vector2(
                    Frac(position.x / _patchSize),
                    Frac(position.z / _patchSize));
                var displacement = map.GetPixelBilinear(uv.x, uv.y);
                position.x -= displacement.r / (i + 1);
                position.z -= displacement.b / (i + 1);
                height = displacement.g;
            }

            return height;
        }

        private void BindPivotFromExterior()
        {
            var motion = FindAnyObjectByType<ExteriorWorldMotion>();
            _motion = motion;
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

        /// <summary>
        /// One-shot hierarchy walk used from Start / NotifyExteriorChanged only.
        /// Must not run every frame: GetComponentsInChildren allocates and the water
        /// planes stay disabled once found.
        /// </summary>
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

                if (t.name == "Water" || t.name.StartsWith("Water", StringComparison.Ordinal))
                {
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static float Frac(float v)
        {
            return v - Mathf.Floor(v);
        }

        private static void SetAutoProperty(object target, string propertyName, object value)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = target.GetType().GetField($"<{propertyName}>k__BackingField", flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            prop?.SetValue(target, value);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogWarning($"[NorthStarOceanAdapter] Missing field {fieldName} on {target.GetType().Name}");
                return;
            }

            field.SetValue(target, value);
        }
    }
}
