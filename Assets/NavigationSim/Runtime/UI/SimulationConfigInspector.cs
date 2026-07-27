using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Inspector mirror of the B-button <see cref="SimulationConfigPanel"/> canvas.
    /// Edit Buque / Motor / Hélice / Gobierno / Entorno here during Play Mode;
    /// Instrumentos are live readouts. Changes sync both ways with the runner
    /// (canvas ↔ Inspector).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-35)]
    public class SimulationConfigInspector : MonoBehaviour
    {
        [Header("Sync")]
        [Tooltip("When on, Inspector edits write into the live simulation.")]
        [SerializeField] private bool driveFromInspector = true;

        // ── BUQUE ────────────────────────────────────────────────────────────
        [Header("Buque")]
        [Range(1f, 60f)]
        [SerializeField] private float tiempoAcelerado = 1f;

        [Tooltip("Index into VesselCatalog (Costero, Remolcador, …).")]
        [SerializeField] private int tipoEmbarcacion;

        [SerializeField] private ManeuveringModelType modeloManiobra =
            ManeuveringModelType.Clarke83Generic;

        [SerializeField] private float esloraLM = 90f;
        [SerializeField] private float mangaBM = 15f;
        [SerializeField] private float caladoTM = 5.5f;
        [Range(0.4f, 0.95f)]
        [SerializeField] private float coefBloqueCb = 0.72f;
        [Range(0.3f, 6f)]
        [SerializeField] private float cteSurgeXL = 1f;

        [SerializeField] private string buqueActivo = "";
        [SerializeField] private string masaVelEquilibrio = "";

        [Tooltip("Tick once to call ResetShip().")]
        [SerializeField] private bool reiniciarBuque;

        // ── MOTOR ────────────────────────────────────────────────────────────
        [Header("Motor")]
        [SerializeField] private float rpmNominal = 156f;
        [SerializeField] private float mcrKw = 36000f;
        [SerializeField] private float cteTiempoMotorS = 18f;
        [SerializeField] private float rampaSubidaRpmS = 3.6f;
        [SerializeField] private float rampaBajadaRpmS = 7.2f;
        [SerializeField] private float retardoInversionS = 8f;
        [SerializeField] private FuelType combustible = FuelType.MarineDiesel;
        [SerializeField] private float sfocBaseC = 270f;
        [SerializeField] private bool motorListo = true;
        [SerializeField] private bool paradaEmergencia;
        [SerializeField] private string consumo = "";

        // ── HÉLICE ───────────────────────────────────────────────────────────
        [Header("Hélice")]
        [SerializeField] private float diametroHeliceM = 9.86f;
        [SerializeField] private float ktK0 = 0.2931f;
        [SerializeField] private float ktK1 = -0.2753f;
        [SerializeField] private float ktK2 = -0.1385f;
        [SerializeField] private float kq0 = 0.0454f;
        [SerializeField] private float kq1 = -0.0408f;
        [Range(0f, 0.6f)]
        [SerializeField] private float estelaWP0 = 0.4f;
        [Range(0f, 0.4f)]
        [SerializeField] private float deduccionEmpujeTP = 0.22f;
        [Range(0.3f, 1f)]
        [SerializeField] private float factorMarchaAtras = 0.75f;
        [SerializeField] private float thrusterProaKN = 250f;
        [SerializeField] private float posicionThrusterM = 140f;

        // ── GOBIERNO ─────────────────────────────────────────────────────────
        [Header("Gobierno")]
        [SerializeField] private SteeringMode modoGobierno = SteeringMode.Hand;
        [Tooltip("-1 babor, 0 hold, +1 estribor (NFU).")]
        [Range(-1, 1)]
        [SerializeField] private int nfuTimon;
        [Range(0f, 360f)]
        [SerializeField] private float rumboAutopilotoDeg;
        [Range(10f, 45f)]
        [SerializeField] private float timonMaximoDeg = 35f;
        [Range(0.5f, 12f)]
        [SerializeField] private float velocidadTimonDegS = 3f;
        [SerializeField] private float apKp = 3f;
        [SerializeField] private float apKi = 0.05f;
        [SerializeField] private float apKd = 120f;
        [Range(5f, 35f)]
        [SerializeField] private float apLimiteTimonDeg = 20f;
        [SerializeField] private bool bombaGobierno1 = true;
        [SerializeField] private bool bombaGobierno2 = true;

        // ── ENTORNO ──────────────────────────────────────────────────────────
        [Header("Entorno")]
        [Range(0f, 8f)]
        [SerializeField] private float corrienteKn;
        [Range(0f, 360f)]
        [SerializeField] private float dirCorrienteHaciaDeg;
        [Range(0f, 90f)]
        [SerializeField] private float vientoKn;
        [Range(0f, 360f)]
        [SerializeField] private float dirVientoDesdeDeg;
        [Range(0f, 9f)]
        [SerializeField] private float alturaOlasHsM = 0.5f;
        [Range(2f, 22f)]
        [SerializeField] private float periodoPicoTpS = 8f;
        [Range(0f, 360f)]
        [SerializeField] private float dirOleajeDesdeDeg;
        [Range(4f, 30f)]
        [SerializeField] private float periodoBalanceS = 12f;
        [SerializeField] private float profundidadM = 60f;
        [SerializeField] private float mareaM = 1.2f;
        [SerializeField] private float densidadAgua = 1025f;
        [SerializeField] private bool nmeaUdpOut;

        // ── INSTRUMENTOS (solo lectura) ──────────────────────────────────────
        [Header("Instrumentos (live)")]
        [SerializeField] private float rumboDeg;
        [SerializeField] private float rotDegMin;
        [SerializeField] private float sogKn;
        [SerializeField] private float stwKn;
        [SerializeField] private float cogDeg;
        [SerializeField] private float timonActualDeg;
        [SerializeField] private float timonOrdenDeg;
        [SerializeField] private string telegrafo = "";
        [SerializeField] private float rpmEje;
        [SerializeField] private float rpmOrden;
        [SerializeField] private float empujeKN;
        [SerializeField] private float parKNm;
        [SerializeField] private float potenciaEjeKW;
        [SerializeField] private float cargaMotor;
        [SerializeField] private float heaveM;
        [SerializeField] private float rollDeg;
        [SerializeField] private float pitchDeg;
        [SerializeField] private Vector2 posicionNE_M;

        private NavigationSimRunner _runner;
        private AppliedSnapshot _applied;
        private bool _hasApplied;

        private void OnValidate()
        {
            tiempoAcelerado = Mathf.Clamp(tiempoAcelerado, 1f, 60f);
            tipoEmbarcacion = Mathf.Clamp(tipoEmbarcacion, 0, Mathf.Max(0, VesselCatalog.All.Length - 1));
            if (Application.isPlaying && driveFromInspector)
            {
                TryResolveRunner();
                ApplyInspectorToSim(force: true);
            }
        }

        private void Update()
        {
            if (!TryResolveRunner())
            {
                return;
            }

            if (reiniciarBuque)
            {
                reiniciarBuque = false;
                _runner.ResetShip();
            }

            // First frame: mirror the live sim so defaults don't overwrite it.
            if (!_hasApplied)
            {
                PullSimIntoInspector();
            }
            else if (driveFromInspector && InspectorDiffersFromApplied())
            {
                ApplyInspectorToSim(force: false);
            }
            else
            {
                PullSimIntoInspector();
            }

            RefreshInstrumentReadouts();
        }

        private bool TryResolveRunner()
        {
            if (_runner != null && _runner.Sim != null && _runner.Config != null)
            {
                return true;
            }

            _runner = NavigationSimRunner.Instance;
            if (_runner == null)
            {
                _runner = FindAnyObjectByType<NavigationSimRunner>();
            }

            return _runner != null && _runner.Sim != null && _runner.Config != null;
        }

        private void PullSimIntoInspector()
        {
            var cfg = _runner.Config;
            var eng = cfg.Engine;
            var prop = cfg.Propeller;
            var bow = cfg.BowThruster;
            var rud = cfg.Rudder;
            var ap = cfg.Autopilot;
            var cmd = _runner.Sim.Command;
            var env = _runner.Env;

            tiempoAcelerado = _runner.TimeScale;
            tipoEmbarcacion = _runner.ActiveVesselIndex;
            modeloManiobra = cfg.ModelType;
            esloraLM = (float)cfg.Clarke.L;
            mangaBM = (float)cfg.Clarke.B;
            caladoTM = (float)cfg.Clarke.T;
            coefBloqueCb = (float)cfg.Clarke.Cb;
            cteSurgeXL = (float)cfg.Clarke.SurgeTimeConstantFactor;
            buqueActivo = cfg.Name;
            masaVelEquilibrio = BuildMassSpeedLabel();

            rpmNominal = (float)(eng.RatedRps * 60.0);
            mcrKw = (float)eng.McrKw;
            cteTiempoMotorS = (float)eng.TimeConstantS;
            rampaSubidaRpmS = (float)(eng.MaxRpsPerS * 60.0);
            rampaBajadaRpmS = (float)(eng.MaxRpsPerSDecel * 60.0);
            retardoInversionS = (float)eng.ReversalDelayS;
            combustible = eng.Fuel;
            sfocBaseC = (float)eng.SfocC;
            motorListo = cmd.EngineReady;
            paradaEmergencia = _runner.PanelEmergencyStop;
            consumo =
                $"{_runner.Sim.State.FuelFlowKgPerH:F0} kg/h | acum {_runner.Sim.State.FuelUsedKg:F0} kg";

            diametroHeliceM = (float)prop.Diameter;
            ktK0 = (float)prop.K0;
            ktK1 = (float)prop.K1;
            ktK2 = (float)prop.K2;
            kq0 = (float)prop.Kq0;
            kq1 = (float)prop.Kq1;
            estelaWP0 = (float)prop.WakeFraction;
            deduccionEmpujeTP = (float)prop.ThrustDeduction;
            factorMarchaAtras = (float)prop.AsternThrustFactor;
            thrusterProaKN = (float)(bow.MaxThrustN / 1000.0);
            posicionThrusterM = (float)bow.LongitudinalPositionM;

            modoGobierno = cmd.SteeringMode;
            nfuTimon = _runner.NfuHold;
            rumboAutopilotoDeg = (float)cmd.HeadingSetpointDeg;
            timonMaximoDeg = (float)rud.MaxAngleDeg;
            velocidadTimonDegS = (float)rud.MaxRateDegPerS;
            apKp = (float)ap.Kp;
            apKi = (float)ap.Ki;
            apKd = (float)ap.Kd;
            apLimiteTimonDeg = (float)ap.RudderLimitDeg;
            bombaGobierno1 = cmd.SteeringPump1;
            bombaGobierno2 = cmd.SteeringPump2;

            corrienteKn = (float)(env.CurrentSpeedMs * 1.9438);
            dirCorrienteHaciaDeg = (float)env.CurrentSetToDeg;
            vientoKn = (float)(env.WindSpeedMs * 1.9438);
            dirVientoDesdeDeg = (float)env.WindFromDeg;
            alturaOlasHsM = (float)env.WaveHeightM;
            periodoPicoTpS = (float)env.WavePeriodS;
            dirOleajeDesdeDeg = (float)env.WaveFromDeg;
            periodoBalanceS = (float)env.RollNaturalPeriodS;
            profundidadM = (float)env.WaterDepthM;
            mareaM = (float)env.TideHeightM;
            densidadAgua = (float)env.WaterDensity;
            nmeaUdpOut = _runner.Nmea != null && _runner.Nmea.Enabled;

            CaptureAppliedFromInspector();
        }

        private void ApplyInspectorToSim(bool force)
        {
            if (_runner == null || _runner.Config == null || _runner.Sim == null)
            {
                return;
            }

            int vesselCount = VesselCatalog.All.Length;
            tipoEmbarcacion = Mathf.Clamp(tipoEmbarcacion, 0, Mathf.Max(0, vesselCount - 1));

            bool vesselChanged = !_hasApplied || _applied.TipoEmbarcacion != tipoEmbarcacion;
            if (vesselChanged)
            {
                _runner.ApplyVessel(tipoEmbarcacion);
            }

            _runner.TimeScale = tiempoAcelerado;

            var cfg = _runner.Config;
            cfg.ModelType = modeloManiobra;
            cfg.Clarke.L = esloraLM;
            cfg.Clarke.B = mangaBM;
            cfg.Clarke.T = caladoTM;
            cfg.Clarke.Cb = coefBloqueCb;
            cfg.Clarke.SurgeTimeConstantFactor = cteSurgeXL;

            var eng = cfg.Engine;
            eng.RatedRps = rpmNominal / 60.0;
            eng.McrKw = mcrKw;
            eng.TimeConstantS = cteTiempoMotorS;
            eng.MaxRpsPerS = rampaSubidaRpmS / 60.0;
            eng.MaxRpsPerSDecel = rampaBajadaRpmS / 60.0;
            eng.ReversalDelayS = retardoInversionS;
            eng.Fuel = combustible;
            eng.SfocC = sfocBaseC;

            var cmd = _runner.Sim.Command;
            cmd.EngineReady = motorListo && !paradaEmergencia;
            _runner.PanelEmergencyStop = paradaEmergencia;

            var prop = cfg.Propeller;
            bool propStructural =
                !_hasApplied ||
                !Approx(_applied.DiametroHeliceM, diametroHeliceM) ||
                !Approx(_applied.KtK0, ktK0) ||
                !Approx(_applied.KtK1, ktK1) ||
                !Approx(_applied.KtK2, ktK2) ||
                !Approx(_applied.EstelaWP0, estelaWP0) ||
                !Approx(_applied.DeduccionEmpujeTP, deduccionEmpujeTP) ||
                !Approx(_applied.FactorMarchaAtras, factorMarchaAtras);

            prop.Diameter = diametroHeliceM;
            prop.K0 = ktK0;
            prop.K1 = ktK1;
            prop.K2 = ktK2;
            prop.Kq0 = kq0;
            prop.Kq1 = kq1;
            prop.WakeFraction = estelaWP0;
            prop.ThrustDeduction = deduccionEmpujeTP;
            prop.AsternThrustFactor = factorMarchaAtras;

            cfg.BowThruster.MaxThrustN = thrusterProaKN * 1000.0;
            cfg.BowThruster.LongitudinalPositionM = posicionThrusterM;

            cmd.SteeringMode = modoGobierno;
            _runner.NfuHold = nfuTimon;
            cmd.HeadingSetpointDeg = ShipState.Normalize360(rumboAutopilotoDeg);
            cfg.Rudder.MaxAngleDeg = timonMaximoDeg;
            cfg.Rudder.MaxRateDegPerS = velocidadTimonDegS;
            cfg.Autopilot.Kp = apKp;
            cfg.Autopilot.Ki = apKi;
            cfg.Autopilot.Kd = apKd;
            cfg.Autopilot.RudderLimitDeg = apLimiteTimonDeg;
            cmd.SteeringPump1 = bombaGobierno1;
            cmd.SteeringPump2 = bombaGobierno2;

            var env = _runner.Env;
            env.CurrentSpeedMs = corrienteKn / 1.9438;
            env.CurrentSetToDeg = ShipState.Normalize360(dirCorrienteHaciaDeg);
            env.WindSpeedMs = vientoKn / 1.9438;
            env.WindFromDeg = ShipState.Normalize360(dirVientoDesdeDeg);
            env.WaveHeightM = alturaOlasHsM;
            env.WavePeriodS = periodoPicoTpS;
            env.WaveFromDeg = ShipState.Normalize360(dirOleajeDesdeDeg);
            env.RollNaturalPeriodS = periodoBalanceS;
            env.WaterDepthM = profundidadM;
            env.TideHeightM = mareaM;
            env.WaterDensity = densidadAgua;
            if (_runner.Nmea != null)
            {
                _runner.Nmea.Enabled = nmeaUdpOut;
            }

            if (propStructural || force || vesselChanged)
            {
                _runner.NotifyConfigChanged();
            }

            buqueActivo = cfg.Name;
            masaVelEquilibrio = BuildMassSpeedLabel();
            CaptureAppliedFromInspector();
        }

        private void RefreshInstrumentReadouts()
        {
            var s = _runner.Sim.State;
            var cmd = _runner.Sim.Command;
            var bridge = ShipControlState.Instance;

            rumboDeg = (float)s.HeadingDeg;
            rotDegMin = (float)s.RotDegPerMin;
            sogKn = (float)(s.SogMs * 1.9438);
            stwKn = (float)(s.StwMs * 1.9438);
            cogDeg = (float)s.CogDeg;
            timonActualDeg = (float)s.RudderAngleDeg;
            timonOrdenDeg = (float)_runner.Sim.ResolvedRudderCommandDeg;
            telegrafo = bridge != null ? bridge.Telegraph.ToString() : "n/a";
            rpmEje = (float)(s.ShaftRps * 60.0);
            rpmOrden = (float)(cmd.TelegraphFraction * _runner.Config.Engine.RatedRps * 60.0);
            empujeKN = (float)(s.PropThrustN / 1000.0);
            parKNm = (float)(s.PropTorqueNm / 1000.0);
            potenciaEjeKW = (float)(s.ShaftPowerW / 1000.0);
            cargaMotor = (float)s.EngineLoad;
            heaveM = (float)s.HeaveM;
            rollDeg = (float)s.RollDeg;
            pitchDeg = (float)s.PitchDeg;
            posicionNE_M = new Vector2((float)s.North, (float)s.East);
            consumo =
                $"{s.FuelFlowKgPerH:F0} kg/h | acum {s.FuelUsedKg:F0} kg";
        }

        private string BuildMassSpeedLabel()
        {
            double eq = HullResistanceCalibration.EquilibriumSpeed(
                _runner.Config.MmgBasic, _runner.Config.MmgManeuvering,
                _runner.Config.Propeller, _runner.Env.WaterDensity,
                _runner.Config.Engine.RatedRps);
            double mass = _runner.Config.ModelType == ManeuveringModelType.MmgCalibrated
                ? _runner.Config.MmgBasic.m
                : _runner.Config.Clarke.Mass(_runner.Env.WaterDensity);
            return $"{mass / 1000.0:N0} t | {eq * 1.9438:F1} kn";
        }

        private bool InspectorDiffersFromApplied()
        {
            if (!_hasApplied)
            {
                return true;
            }

            return
                !Approx(_applied.TiempoAcelerado, tiempoAcelerado) ||
                _applied.TipoEmbarcacion != tipoEmbarcacion ||
                _applied.ModeloManiobra != modeloManiobra ||
                !Approx(_applied.EsloraLM, esloraLM) ||
                !Approx(_applied.MangaBM, mangaBM) ||
                !Approx(_applied.CaladoTM, caladoTM) ||
                !Approx(_applied.CoefBloqueCb, coefBloqueCb) ||
                !Approx(_applied.CteSurgeXL, cteSurgeXL) ||
                !Approx(_applied.RpmNominal, rpmNominal) ||
                !Approx(_applied.McrKw, mcrKw) ||
                !Approx(_applied.CteTiempoMotorS, cteTiempoMotorS) ||
                !Approx(_applied.RampaSubidaRpmS, rampaSubidaRpmS) ||
                !Approx(_applied.RampaBajadaRpmS, rampaBajadaRpmS) ||
                !Approx(_applied.RetardoInversionS, retardoInversionS) ||
                _applied.Combustible != combustible ||
                !Approx(_applied.SfocBaseC, sfocBaseC) ||
                _applied.MotorListo != motorListo ||
                _applied.ParadaEmergencia != paradaEmergencia ||
                !Approx(_applied.DiametroHeliceM, diametroHeliceM) ||
                !Approx(_applied.KtK0, ktK0) ||
                !Approx(_applied.KtK1, ktK1) ||
                !Approx(_applied.KtK2, ktK2) ||
                !Approx(_applied.Kq0, kq0) ||
                !Approx(_applied.Kq1, kq1) ||
                !Approx(_applied.EstelaWP0, estelaWP0) ||
                !Approx(_applied.DeduccionEmpujeTP, deduccionEmpujeTP) ||
                !Approx(_applied.FactorMarchaAtras, factorMarchaAtras) ||
                !Approx(_applied.ThrusterProaKN, thrusterProaKN) ||
                !Approx(_applied.PosicionThrusterM, posicionThrusterM) ||
                _applied.ModoGobierno != modoGobierno ||
                _applied.NfuTimon != nfuTimon ||
                !Approx(_applied.RumboAutopilotoDeg, rumboAutopilotoDeg) ||
                !Approx(_applied.TimonMaximoDeg, timonMaximoDeg) ||
                !Approx(_applied.VelocidadTimonDegS, velocidadTimonDegS) ||
                !Approx(_applied.ApKp, apKp) ||
                !Approx(_applied.ApKi, apKi) ||
                !Approx(_applied.ApKd, apKd) ||
                !Approx(_applied.ApLimiteTimonDeg, apLimiteTimonDeg) ||
                _applied.BombaGobierno1 != bombaGobierno1 ||
                _applied.BombaGobierno2 != bombaGobierno2 ||
                !Approx(_applied.CorrienteKn, corrienteKn) ||
                !Approx(_applied.DirCorrienteHaciaDeg, dirCorrienteHaciaDeg) ||
                !Approx(_applied.VientoKn, vientoKn) ||
                !Approx(_applied.DirVientoDesdeDeg, dirVientoDesdeDeg) ||
                !Approx(_applied.AlturaOlasHsM, alturaOlasHsM) ||
                !Approx(_applied.PeriodoPicoTpS, periodoPicoTpS) ||
                !Approx(_applied.DirOleajeDesdeDeg, dirOleajeDesdeDeg) ||
                !Approx(_applied.PeriodoBalanceS, periodoBalanceS) ||
                !Approx(_applied.ProfundidadM, profundidadM) ||
                !Approx(_applied.DensidadAgua, densidadAgua);
        }

        private void CaptureAppliedFromInspector()
        {
            _applied = new AppliedSnapshot
            {
                TiempoAcelerado = tiempoAcelerado,
                TipoEmbarcacion = tipoEmbarcacion,
                ModeloManiobra = modeloManiobra,
                EsloraLM = esloraLM,
                MangaBM = mangaBM,
                CaladoTM = caladoTM,
                CoefBloqueCb = coefBloqueCb,
                CteSurgeXL = cteSurgeXL,
                RpmNominal = rpmNominal,
                McrKw = mcrKw,
                CteTiempoMotorS = cteTiempoMotorS,
                RampaSubidaRpmS = rampaSubidaRpmS,
                RampaBajadaRpmS = rampaBajadaRpmS,
                RetardoInversionS = retardoInversionS,
                Combustible = combustible,
                SfocBaseC = sfocBaseC,
                MotorListo = motorListo,
                ParadaEmergencia = paradaEmergencia,
                DiametroHeliceM = diametroHeliceM,
                KtK0 = ktK0,
                KtK1 = ktK1,
                KtK2 = ktK2,
                Kq0 = kq0,
                Kq1 = kq1,
                EstelaWP0 = estelaWP0,
                DeduccionEmpujeTP = deduccionEmpujeTP,
                FactorMarchaAtras = factorMarchaAtras,
                ThrusterProaKN = thrusterProaKN,
                PosicionThrusterM = posicionThrusterM,
                ModoGobierno = modoGobierno,
                NfuTimon = nfuTimon,
                RumboAutopilotoDeg = rumboAutopilotoDeg,
                TimonMaximoDeg = timonMaximoDeg,
                VelocidadTimonDegS = velocidadTimonDegS,
                ApKp = apKp,
                ApKi = apKi,
                ApKd = apKd,
                ApLimiteTimonDeg = apLimiteTimonDeg,
                BombaGobierno1 = bombaGobierno1,
                BombaGobierno2 = bombaGobierno2,
                CorrienteKn = corrienteKn,
                DirCorrienteHaciaDeg = dirCorrienteHaciaDeg,
                VientoKn = vientoKn,
                DirVientoDesdeDeg = dirVientoDesdeDeg,
                AlturaOlasHsM = alturaOlasHsM,
                PeriodoPicoTpS = periodoPicoTpS,
                DirOleajeDesdeDeg = dirOleajeDesdeDeg,
                PeriodoBalanceS = periodoBalanceS,
                ProfundidadM = profundidadM,
                DensidadAgua = densidadAgua
            };
            _hasApplied = true;
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        private struct AppliedSnapshot
        {
            public float TiempoAcelerado;
            public int TipoEmbarcacion;
            public ManeuveringModelType ModeloManiobra;
            public float EsloraLM, MangaBM, CaladoTM, CoefBloqueCb, CteSurgeXL;
            public float RpmNominal, McrKw, CteTiempoMotorS, RampaSubidaRpmS, RampaBajadaRpmS, RetardoInversionS;
            public FuelType Combustible;
            public float SfocBaseC;
            public bool MotorListo, ParadaEmergencia;
            public float DiametroHeliceM, KtK0, KtK1, KtK2, Kq0, Kq1;
            public float EstelaWP0, DeduccionEmpujeTP, FactorMarchaAtras;
            public float ThrusterProaKN, PosicionThrusterM;
            public SteeringMode ModoGobierno;
            public int NfuTimon;
            public float RumboAutopilotoDeg, TimonMaximoDeg, VelocidadTimonDegS;
            public float ApKp, ApKi, ApKd, ApLimiteTimonDeg;
            public bool BombaGobierno1, BombaGobierno2;
            public float CorrienteKn, DirCorrienteHaciaDeg, VientoKn, DirVientoDesdeDeg;
            public float AlturaOlasHsM, PeriodoPicoTpS, DirOleajeDesdeDeg, PeriodoBalanceS;
            public float ProfundidadM, DensidadAgua;
        }
    }
}
