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

        [Header("Safety")]
        [SerializeField] private bool emergencyStop;
        [SerializeField] private bool engineReady = true;

        [Header("Readouts (debug)")]
        [SerializeField] private ShipControlState.TelegraphOrder telegraphOrder =
            ShipControlState.TelegraphOrder.Stop;
        [SerializeField] private float actualRudderDeg;

        private ShipControlState _state;
        private bool _syncedTimeScaleFromRunner;

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

            actualRudderDeg = _state.ActualRudderAngleDeg;
            telegraphOrder = _state.Telegraph;
        }

        private void ApplyToState()
        {
            if (_state == null)
            {
                return;
            }

            _state.CommandedRudderAngleDeg = steeringWheelDeg;
            _state.BowThruster = bowThruster;
            _state.Telegraph = ResolveTelegraphOrder(avanceRetroceso);
            _state.EmergencyStop = emergencyStop;

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
            ApplyToState();
        }
    }
}
