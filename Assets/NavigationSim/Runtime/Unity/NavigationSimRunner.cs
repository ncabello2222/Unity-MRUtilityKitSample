using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Unity host of the deterministic navigation core. Reads the physical
    /// bridge controls (ShipControlState), steps the ShipSimulator at a fixed
    /// 50 Hz and exposes an interpolated pose for the world-motion driver.
    /// The ship never moves in Unity: consumers apply the inverse transform.
    /// Visual ocean phase is driven by an <see cref="IOceanSurface"/> adapter when present.
    /// </summary>
    public class NavigationSimRunner : MonoBehaviour
    {
        public const double FixedStep = ShipSimulator.DefaultDt;

        public static NavigationSimRunner Instance { get; private set; }

        public ShipSimulator Sim { get; private set; }
        public VesselConfig Config { get; private set; }
        public EnvironmentState Env { get; private set; }
        public string ActiveVesselId { get; private set; } = VesselCatalog.All[0].Id;
        public int ActiveVesselIndex => VesselCatalog.IndexOf(ActiveVesselId);

        /// <summary>Panel-driven flags merged into the command every tick.</summary>
        [HideInInspector] public int NfuHold;
        [HideInInspector] public bool PanelEmergencyStop;

        /// <summary>
        /// Fast-time factor. A laden VLCC genuinely takes ~12 min to reach service
        /// speed and ~4 min to turn 90°, which is imperceptible in VR. Running the
        /// deterministic core faster preserves all the physics (ratios stay exact)
        /// while making the maneuver watchable — standard in bridge simulators.
        /// </summary>
        [SerializeField] private float timeScale = 1f;

        public float TimeScale
        {
            get => timeScale;
            set => timeScale = Mathf.Clamp(value, 1f, 60f);
        }

        private double _accumulator;

        // Snapshots for render interpolation.
        private double _prevNorth, _prevEast, _prevPsiDeg, _prevHeave, _prevRoll, _prevPitch;
        private double _currNorth, _currEast, _currPsiDeg, _currHeave, _currRoll, _currPitch;

        public static NavigationSimRunner EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindAnyObjectByType<NavigationSimRunner>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("NavigationSimSystems");
            return go.AddComponent<NavigationSimRunner>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            Env = new EnvironmentState();
            // Default to the nimble coaster so pushing the telegraph and turning the
            // wheel produce immediately perceptible world motion. Other Blender hulls
            // (and the calibrated KVLCC2) are selectable from the B panel.
            var def = VesselCatalog.All[0];
            ActiveVesselId = def.Id;
            Config = def.CreateConfig();
            Sim = new ShipSimulator(Config, Env);
            SnapshotState();
            _prevNorth = _currNorth;
            _prevEast = _currEast;
            _prevPsiDeg = _currPsiDeg;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            BuildCommandFromBridge();

            _accumulator += Mathf.Min(Time.deltaTime, 0.1f) * Mathf.Max(1f, timeScale);
            int maxSubSteps = Mathf.Clamp(Mathf.CeilToInt(timeScale * 6f), 8, 512);
            int safety = 0;
            while (_accumulator >= FixedStep && safety++ < maxSubSteps)
            {
                _prevNorth = _currNorth;
                _prevEast = _currEast;
                _prevPsiDeg = _currPsiDeg;
                _prevHeave = _currHeave;
                _prevRoll = _currRoll;
                _prevPitch = _currPitch;

                Sim.Step(FixedStep);
                SnapshotState();
                _accumulator -= FixedStep;
            }

            PushActualRudderToBridge();
        }

        private void SnapshotState()
        {
            _currNorth = Sim.State.North;
            _currEast = Sim.State.East;
            _currPsiDeg = Sim.State.PsiRad * Mathf.Rad2Deg;
            _currHeave = Sim.State.HeaveM;
            _currRoll = Sim.State.RollDeg;
            _currPitch = Sim.State.PitchDeg;
        }

        private void BuildCommandFromBridge()
        {
            ShipCommand cmd = Sim.Command;
            ShipControlState bridge = ShipControlState.Instance;

            if (bridge != null)
            {
                cmd.RudderCommandDeg = bridge.CommandedRudderAngleDeg;
                cmd.TelegraphFraction = Config.TelegraphFraction((int)bridge.Telegraph);
                cmd.BowThruster = bridge.BowThruster;
                cmd.EmergencyStop = bridge.EmergencyStop || PanelEmergencyStop;
            }
            else
            {
                cmd.EmergencyStop = PanelEmergencyStop;
            }

            cmd.NfuDirection = NfuHold;
            // SteeringMode, HeadingSetpointDeg, pumps and EngineReady are written
            // directly on Sim.Command by the configuration panel.
        }

        private void PushActualRudderToBridge()
        {
            ShipControlState bridge = ShipControlState.Instance;
            if (bridge != null)
            {
                bridge.DriveActualRudder((float)Sim.State.RudderAngleDeg);
            }
        }

        // ── Interpolated pose for the render layer ─────────────────────────

        private float Alpha => Mathf.Clamp01((float)(_accumulator / FixedStep));

        public double InterpNorth => _prevNorth + (_currNorth - _prevNorth) * Alpha;
        public double InterpEast => _prevEast + (_currEast - _prevEast) * Alpha;
        public double InterpHeave => _prevHeave + (_currHeave - _prevHeave) * Alpha;
        public double InterpRollDeg => _prevRoll + (_currRoll - _prevRoll) * Alpha;
        public double InterpPitchDeg => _prevPitch + (_currPitch - _prevPitch) * Alpha;

        public double InterpPsiDeg
        {
            get
            {
                double delta = ShipState.WrapDeg(_currPsiDeg - _prevPsiDeg);
                return _prevPsiDeg + delta * Alpha;
            }
        }

        // ── Panel API ──────────────────────────────────────────────────────

        public void ApplyPreset(int index)
        {
            // Legacy dual-button API kept for older UI calls.
            ApplyVessel(index == 1 ? "coaster" : "kvlcc2");
        }

        /// <summary>
        /// Switches simulation parameters and the visible Blender hull.
        /// </summary>
        public void ApplyVessel(string vesselId)
        {
            var def = VesselCatalog.Get(VesselCatalog.IndexOf(vesselId));
            ActiveVesselId = def.Id;
            VesselConfig fresh = def.CreateConfig();
            Config = fresh;
            Sim.Config = fresh;
            Sim.RebuildModels();
            ResetShip();

            var presenter = ShipBridgePrototype.VesselHullPresenter.Instance;
            if (presenter == null)
            {
                presenter = FindAnyObjectByType<ShipBridgePrototype.VesselHullPresenter>();
            }

            if (presenter != null)
            {
                presenter.ShowVessel(def.Id);
            }
        }

        public void ApplyVessel(int catalogIndex)
        {
            ApplyVessel(VesselCatalog.Get(catalogIndex).Id);
        }

        public void ResetShip()
        {
            Sim.Reset();
            SnapshotState();
            _prevNorth = _currNorth;
            _prevEast = _currEast;
            _prevPsiDeg = _currPsiDeg;
            _prevHeave = _prevRoll = _prevPitch = 0.0;
            _accumulator = 0.0;
            // The world-motion driver recomputes delta = identity next frame,
            // so the exterior snaps back to its bound pose automatically.
        }

        /// <summary>Config edits that change model structure call this.</summary>
        public void NotifyConfigChanged()
        {
            Config.SyncPropellerIntoMmg();
            Sim.RebuildModels();
        }
    }
}
