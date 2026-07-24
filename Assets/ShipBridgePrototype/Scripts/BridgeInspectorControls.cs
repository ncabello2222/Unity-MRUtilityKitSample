using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Debug / desktop bridge controls exposed as Inspector sliders.
    /// Writes into <see cref="ShipControlState"/> the same way as the physical wheel,
    /// telegraph and bow thruster.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public class BridgeInspectorControls : MonoBehaviour
    {
        [Header("Enable")]
        [Tooltip("When off, sliders are ignored (physical bridge keeps ownership).")]
        [SerializeField] private bool driveControls = true;

        [Header("Timón (steering wheel)")]
        [Tooltip("Positive = starboard, negative = port.")]
        [Range(-35f, 35f)]
        [SerializeField] private float steeringWheelDeg;

        [Header("Bow thruster")]
        [Tooltip("Positive pushes the bow to starboard.")]
        [Range(-1f, 1f)]
        [SerializeField] private float bowThruster;

        [Header("Máquina — avance / retroceso")]
        [Tooltip("Signed throttle: −1 Full Astern … 0 Stop … +1 Full Ahead.")]
        [Range(-1f, 1f)]
        [SerializeField] private float avanceRetroceso;

        [Header("Simulación")]
        [Tooltip("Same as B-panel «Tiempo acelerado». Default ×1 (real time).")]
        [Range(1f, 60f)]
        [SerializeField] private float tiempoAcelerado = 1f;

        [Header("Vista mapa (PiP)")]
        [Tooltip("Top-down chart view: the vessel visibly turns/advances over a fixed sea. " +
                 "Use this to judge maneuvers from outside — in world space the room never " +
                 "rotates (the exterior moves inversely).")]
        [SerializeField] private bool vistaMapa;

        [Header("Safety")]
        [SerializeField] private bool emergencyStop;
        [SerializeField] private bool engineReady = true;

        [Header("Bow calibration (Entrega 1)")]
        [SerializeField] private bool confirmBow;
        [SerializeField] private bool flipBow;
        [SerializeField] private bool clearBowCalibration;

        [Header("Readouts (debug)")]
        [SerializeField] private ShipControlState.TelegraphOrder telegraphOrder =
            ShipControlState.TelegraphOrder.Stop;
        [SerializeField] private float actualRudderDeg;
        [Tooltip("Compass heading. 0 = initial bow direction, increases turning to starboard.")]
        [SerializeField] private float headingDeg;
        [Tooltip("Rate of turn, degrees per minute. Positive = turning to starboard.")]
        [SerializeField] private float rateOfTurnDegMin;
        [SerializeField] private float speedKnots;
        [SerializeField] private Vector2 positionEastNorthM;

        private ShipControlState _state;
        private bool _syncedTimeScaleFromRunner;

        // Last values this component applied. Writes happen only on slider change so
        // the physical wheel / panel levers are not stomped every frame.
        private float _appliedRudder = float.NaN;
        private float _appliedBowThruster = float.NaN;
        private float _appliedThrottle = float.NaN;
        private bool _appliedEmergencyStop;
        private bool _hasAppliedEmergencyStop;

        public float SteeringWheelDeg
        {
            get => steeringWheelDeg;
            set => steeringWheelDeg = Mathf.Clamp(value, -35f, 35f);
        }

        public float BowThruster
        {
            get => bowThruster;
            set => bowThruster = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>−1 Full Astern … 0 Stop … +1 Full Ahead.</summary>
        public float AvanceRetroceso
        {
            get => avanceRetroceso;
            set => avanceRetroceso = Mathf.Clamp(value, -1f, 1f);
        }

        public float TiempoAcelerado
        {
            get => tiempoAcelerado;
            set => tiempoAcelerado = Mathf.Clamp(value, 1f, 60f);
        }

        private void Awake()
        {
            ResolveState();
        }

        private void OnValidate()
        {
            telegraphOrder = ResolveTelegraphOrder(avanceRetroceso);
            tiempoAcelerado = Mathf.Clamp(tiempoAcelerado, 1f, 60f);
            if (Application.isPlaying)
            {
                ApplyToState();
            }
        }

        private void Update()
        {
            ResolveState();
            if (_state == null)
            {
                return;
            }

            if (driveControls)
            {
                ApplyToState();
            }

            ShipMapSpectatorCamera.SetVisible(vistaMapa);
            ApplyBowCalibrationButtons();

            actualRudderDeg = _state.ActualRudderAngleDeg;
            telegraphOrder = _state.Telegraph;
            UpdateNavigationReadouts();
        }

        private void UpdateNavigationReadouts()
        {
            var runner = NavigationSim.UnityLayer.NavigationSimRunner.Instance;
            if (runner?.Sim == null)
            {
                return;
            }

            var state = runner.Sim.State;
            headingDeg = (float)state.HeadingDeg;
            rateOfTurnDegMin = (float)(state.R * Mathf.Rad2Deg * 60.0);
            speedKnots = (float)(System.Math.Sqrt(state.U * state.U + state.V * state.V) * 1.9438);
            positionEastNorthM = new Vector2((float)state.East, (float)state.North);
        }

        private void ApplyBowCalibrationButtons()
        {
            var cal = BridgeOrientationCalibration.Instance;
            if (cal == null)
            {
                cal = FindAnyObjectByType<BridgeOrientationCalibration>();
            }

            if (confirmBow)
            {
                confirmBow = false;
                cal?.ConfirmCurrentFront();
            }

            if (flipBow)
            {
                flipBow = false;
                cal?.FlipFrontAndAft();
            }

            if (clearBowCalibration)
            {
                clearBowCalibration = false;
                cal?.ResetCalibration();
            }
        }

        private void ApplyToState()
        {
            if (_state == null)
            {
                return;
            }

            // Write only when the slider itself changed; otherwise reflect the live
            // state so this inspector doubles as a readout and never fights the
            // physical wheel/levers writing the same fields.
            if (!Mathf.Approximately(steeringWheelDeg, _appliedRudder))
            {
                _state.CommandedRudderAngleDeg = steeringWheelDeg;
                _appliedRudder = steeringWheelDeg;
            }
            else if (!Mathf.Approximately(_state.CommandedRudderAngleDeg, steeringWheelDeg))
            {
                steeringWheelDeg = _state.CommandedRudderAngleDeg;
                _appliedRudder = steeringWheelDeg;
            }

            if (!Mathf.Approximately(bowThruster, _appliedBowThruster))
            {
                _state.BowThruster = bowThruster;
                _appliedBowThruster = bowThruster;
            }
            else if (!Mathf.Approximately(_state.BowThruster, bowThruster))
            {
                bowThruster = _state.BowThruster;
                _appliedBowThruster = bowThruster;
            }

            if (!Mathf.Approximately(avanceRetroceso, _appliedThrottle))
            {
                _state.Telegraph = ResolveTelegraphOrder(avanceRetroceso);
                _appliedThrottle = avanceRetroceso;
            }

            if (!_hasAppliedEmergencyStop || emergencyStop != _appliedEmergencyStop)
            {
                _state.EmergencyStop = emergencyStop;
                _appliedEmergencyStop = emergencyStop;
                _hasAppliedEmergencyStop = true;
            }

            var runner = NavigationSim.UnityLayer.NavigationSimRunner.Instance;
            if (runner != null)
            {
                if (!_syncedTimeScaleFromRunner)
                {
                    tiempoAcelerado = runner.TimeScale;
                    _syncedTimeScaleFromRunner = true;
                }

                runner.TimeScale = tiempoAcelerado;
                runner.Sim.Command.EngineReady = engineReady && !emergencyStop;
                runner.Sim.Command.SteeringMode = NavigationSim.Core.SteeringMode.Hand;
                runner.PanelEmergencyStop = emergencyStop;
            }
        }

        private void ResolveState()
        {
            if (_state != null)
            {
                return;
            }

            _state = ShipControlState.Instance;
            if (_state == null)
            {
                _state = FindAnyObjectByType<ShipControlState>();
            }

            if (_state == null)
            {
                var host = GameObject.Find("ShipBridgeSystems");
                if (host == null)
                {
                    host = gameObject;
                }

                _state = host.GetComponent<ShipControlState>();
                if (_state == null)
                {
                    _state = host.AddComponent<ShipControlState>();
                }
            }
        }

        /// <summary>
        /// Maps a signed throttle (−1…+1, 0 = stop) onto the 9-step telegraph table.
        /// </summary>
        public static ShipControlState.TelegraphOrder ResolveTelegraphOrder(float signedThrottle)
        {
            signedThrottle = Mathf.Clamp(signedThrottle, -1f, 1f);
            const float eps = 0.04f;
            if (Mathf.Abs(signedThrottle) < eps)
            {
                return ShipControlState.TelegraphOrder.Stop;
            }

            float mag = Mathf.Abs(signedThrottle);
            if (signedThrottle > 0f)
            {
                if (mag < 0.3f) return ShipControlState.TelegraphOrder.DeadSlowAhead;
                if (mag < 0.55f) return ShipControlState.TelegraphOrder.SlowAhead;
                if (mag < 0.8f) return ShipControlState.TelegraphOrder.HalfAhead;
                return ShipControlState.TelegraphOrder.FullAhead;
            }

            if (mag < 0.3f) return ShipControlState.TelegraphOrder.DeadSlowAstern;
            if (mag < 0.55f) return ShipControlState.TelegraphOrder.SlowAstern;
            if (mag < 0.8f) return ShipControlState.TelegraphOrder.HalfAstern;
            return ShipControlState.TelegraphOrder.FullAstern;
        }

        [ContextMenu("Reset Controls")]
        public void ResetControls()
        {
            steeringWheelDeg = 0f;
            bowThruster = 0f;
            avanceRetroceso = 0f;
            tiempoAcelerado = 1f;
            emergencyStop = false;
            engineReady = true;
            // Invalidate caches so the reset force-writes every field.
            _appliedRudder = float.NaN;
            _appliedBowThruster = float.NaN;
            _appliedThrottle = float.NaN;
            _hasAppliedEmergencyStop = false;
            ApplyToState();
        }
    }
}
