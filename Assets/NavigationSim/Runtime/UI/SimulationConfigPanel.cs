using System;
using System.Collections.Generic;
using NavigationSim.Core;
using ShipBridgePrototype;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// World-space configuration canvas toggled with the B button. Exposes every
    /// editable parameter of the simulation (vessel, engine, propeller, steering,
    /// environment) plus a live instrument page. Built entirely from code so the
    /// prototype scene needs no wiring.
    /// </summary>
    public class SimulationConfigPanel : MonoBehaviour
    {
        private const float CanvasWidth = 960f;
        private const float CanvasHeight = 800f;
        private const float RowHeight = 52f;
        private const float ContentTop = 128f;

        private static readonly Color PanelColor = new Color(0.075f, 0.095f, 0.125f, 0.97f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.42f, 0.62f, 1f);
        private static readonly Color AccentColor = new Color(0.95f, 0.65f, 0.15f, 1f);
        private static readonly Color DangerColor = new Color(0.75f, 0.2f, 0.15f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.95f, 0.98f, 1f);

        private readonly string[] _tabs = { "BUQUE", "MOTOR", "HÉLICE", "GOBIERNO", "ENTORNO", "INSTRUM." };

        private NavigationSimRunner _runner;
        private BridgeRoomMapper _bridgeMapper;
        private GameObject _canvasRoot;
        private RectTransform _contentRoot;
        private readonly List<Image> _tabImages = new List<Image>();
        private readonly List<(TMP_Text text, Func<string> supplier)> _liveTexts =
            new List<(TMP_Text, Func<string>)>();
        private float _liveRefreshTimer;
        private float _rowY;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            _runner = GetComponent<NavigationSimRunner>();
            CacheBridgeMapper();
        }

        private void CacheBridgeMapper()
        {
            if (_bridgeMapper == null)
            {
                _bridgeMapper = FindAnyObjectByType<BridgeRoomMapper>();
            }
        }

        private void Update()
        {
            if (TogglePressed())
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }

            if (!IsOpen)
            {
                return;
            }

            _liveRefreshTimer -= Time.deltaTime;
            if (_liveRefreshTimer <= 0f)
            {
                _liveRefreshTimer = 0.15f;
                foreach (var (text, supplier) in _liveTexts)
                {
                    if (text != null)
                    {
                        text.text = supplier();
                    }
                }
            }
        }

        private static bool TogglePressed()
        {
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.Two))
            {
                return true;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
        }

        public void Open()
        {
            if (_canvasRoot == null)
            {
                BuildCanvas();
                ShowTab(0);
            }

            PlaceInFrontOfCamera();
            _canvasRoot.SetActive(true);
            IsOpen = true;
        }

        public void Close()
        {
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(false);
            }

            IsOpen = false;
            _runner.NfuHold = 0;
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            _canvasRoot.transform.position = cam.transform.position + forward * 1.05f + Vector3.down * 0.05f;
            _canvasRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Canvas construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            _canvasRoot = new GameObject("SimConfigCanvas");
            _canvasRoot.transform.SetParent(transform, false);

            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            var rt = _canvasRoot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            _canvasRoot.transform.localScale = Vector3.one * 0.0011f;

            var bg = CreateImage(rt, "Background", Vector2.zero, new Vector2(CanvasWidth, CanvasHeight), PanelColor);
            bg.rectTransform.anchoredPosition = Vector2.zero;

            CreateText(rt, "Title", new Vector2(28f, -14f), new Vector2(720f, 40f),
                "SIMULADOR DE NAVEGACIÓN — CONFIGURACIÓN (B para cerrar)", 26f,
                TextAlignmentOptions.Left, AccentColor);

            var closeBtn = CreateButton(rt, new Vector2(CanvasWidth - 76f, -12f), new Vector2(56f, 44f),
                "X", DangerColor, Close, false);
            closeBtn.name = "CloseButton";

            // Tab bar.
            float tabWidth = (CanvasWidth - 56f) / _tabs.Length;
            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                var tabBtn = CreateButton(rt,
                    new Vector2(28f + i * tabWidth, -62f), new Vector2(tabWidth - 8f, 46f),
                    _tabs[i], ButtonColor, () => ShowTab(index), false);
                _tabImages.Add(tabBtn.GetComponent<Image>());
            }

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(rt, false);
            _contentRoot = contentGo.GetComponent<RectTransform>();
            _contentRoot.anchorMin = new Vector2(0f, 1f);
            _contentRoot.anchorMax = new Vector2(0f, 1f);
            _contentRoot.pivot = new Vector2(0f, 1f);
            _contentRoot.anchoredPosition = new Vector2(0f, -ContentTop);
            _contentRoot.sizeDelta = new Vector2(CanvasWidth, CanvasHeight - ContentTop);
        }

        private void ShowTab(int index)
        {
            for (int i = 0; i < _tabImages.Count; i++)
            {
                _tabImages[i].color = i == index ? AccentColor : ButtonColor;
                var btn = _tabImages[i].GetComponent<SimUiButton>();
                btn.Bind(_tabImages[i]); // refresh base color for hover logic
            }

            foreach (Transform child in _contentRoot)
            {
                Destroy(child.gameObject);
            }

            _liveTexts.Clear();
            _rowY = 0f;

            switch (index)
            {
                case 0: BuildShipTab(); break;
                case 1: BuildEngineTab(); break;
                case 2: BuildPropellerTab(); break;
                case 3: BuildSteeringTab(); break;
                case 4: BuildEnvironmentTab(); break;
                case 5: BuildInstrumentsTab(); break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tabs
        // ─────────────────────────────────────────────────────────────────────

        private void BuildShipTab()
        {
            var cfg = _runner.Config;

            AddValueRow("Tiempo acelerado (×)",
                () => _runner.TimeScale, v => _runner.TimeScale = (float)v, 2.0, "F0", 1.0, 60.0);

            AddCycleRow("Tipo de embarcación",
                VesselCatalog.DisplayNames(),
                () => _runner.ActiveVesselIndex,
                i =>
                {
                    _runner.ApplyVessel(i);
                    ShowTab(0);
                });

            CacheBridgeMapper();
            if (_bridgeMapper != null)
            {
                AddCycleRow("Pared proa (ventanas)",
                    _bridgeMapper.GetFrontWallOptionLabels(),
                    () =>
                    {
                        var index = _bridgeMapper.GetFrontWallSelectionIndex(out var count);
                        if (count <= 0)
                        {
                            return 0;
                        }

                        return Mathf.Clamp(index, 0, count - 1);
                    },
                    i =>
                    {
                        _bridgeMapper.SelectFrontWallAtIndex(i);
                        BridgeOrientationCalibration.Instance?.OnExternalFrontWallChanged();
                        ShowTab(0);
                    });

                AddLiveRow("Proa / calibración", () =>
                {
                    var frame = BridgeReferenceFrame.Instance;
                    var calibrated = frame != null && frame.IsCalibrated;
                    var name = _bridgeMapper.GetFrontWallDisplayName();
                    return calibrated ? $"OK — {name}" : $"Pendiente — {name}";
                });

                AddActionRow("Confirmar proa", "CONFIRMAR VENTANAS", () =>
                {
                    var cal = BridgeOrientationCalibration.Instance;
                    if (cal != null)
                    {
                        cal.ConfirmCurrentFront();
                    }
                    else
                    {
                        _bridgeMapper.ConfirmFrontCalibration();
                    }

                    ShowTab(0);
                }, AccentColor);
            }

            AddLiveRow("Casco visual", () =>
            {
                var d = VesselCatalog.Get(_runner.ActiveVesselIndex);
                return $"{d.DisplayName}  |  puente +{d.BridgeHeightAboveDeckM:0.#} m";
            });
            AddLiveRow("Dimensiones", () =>
                $"L {cfg.Clarke.L:F0} m   B {cfg.Clarke.B:F1} m   T {cfg.Clarke.T:F1} m   Cb {cfg.Clarke.Cb:F2}");
            AddLiveRow("Propulsión", () =>
                $"MCR {cfg.Engine.McrKw:N0} kW   RPM {cfg.Engine.RatedRps * 60.0:F0}   {FuelLabel(cfg.Engine.Fuel)}");
            AddLiveRow("Hélice / thruster", () =>
                $"Ø {cfg.Propeller.Diameter:F1} m   bow {cfg.BowThruster.MaxThrustN / 1000.0:F0} kN");

            AddCycleRow("Modelo de maniobra",
                new[] { "MMG calibrado", "Clarke 83 genérico" },
                () => (int)cfg.ModelType,
                i => cfg.ModelType = (ManeuveringModelType)i);

            AddValueRow("Eslora L [m] (Clarke)", () => cfg.Clarke.L, v => cfg.Clarke.L = v, 5.0, "F0", 20.0, 400.0);
            AddValueRow("Manga B [m] (Clarke)", () => cfg.Clarke.B, v => cfg.Clarke.B = v, 1.0, "F1", 4.0, 65.0);
            AddValueRow("Calado T [m] (Clarke)", () => cfg.Clarke.T, v => cfg.Clarke.T = v, 0.5, "F1", 1.0, 25.0);
            AddValueRow("Coef. bloque Cb (Clarke)", () => cfg.Clarke.Cb, v => cfg.Clarke.Cb = v, 0.01, "F2", 0.40, 0.95);
            AddValueRow("Cte. surge (×L) (Clarke)", () => cfg.Clarke.SurgeTimeConstantFactor,
                v => cfg.Clarke.SurgeTimeConstantFactor = v, 0.1, "F1", 0.3, 6.0);

            AddLiveRow("Buque activo", () => _runner.Config.Name);
            AddLiveRow("Masa / Vel. equilibrio", () =>
            {
                double eq = HullResistanceCalibration.EquilibriumSpeed(
                    _runner.Config.MmgBasic, _runner.Config.MmgManeuvering,
                    _runner.Config.Propeller, _runner.Env.WaterDensity,
                    _runner.Config.Engine.RatedRps);
                double mass = _runner.Config.ModelType == ManeuveringModelType.MmgCalibrated
                    ? _runner.Config.MmgBasic.m
                    : _runner.Config.Clarke.Mass(_runner.Env.WaterDensity);
                return $"{mass / 1000.0:N0} t   |   {eq * 1.9438:F1} kn";
            });

            AddActionRow("Estado del buque", "REINICIAR BUQUE", () => _runner.ResetShip(), DangerColor);
        }

        private static string FuelLabel(FuelType fuel)
        {
            switch (fuel)
            {
                case FuelType.HeavyFuelOil: return "HFO";
                case FuelType.Lng: return "LNG";
                default: return "MDO";
            }
        }

        private void BuildEngineTab()
        {
            var eng = _runner.Config.Engine;

            AddValueRow("RPM nominal", () => eng.RatedRps * 60.0, v => eng.RatedRps = v / 60.0, 5.0, "F0", 10.0, 400.0);
            AddValueRow("MCR [kW]", () => eng.McrKw, v => eng.McrKw = v, 500.0, "N0", 100.0, 120000.0);
            AddValueRow("Cte. tiempo motor [s]", () => eng.TimeConstantS, v => eng.TimeConstantS = v, 1.0, "F0", 1.0, 90.0);
            AddValueRow("Rampa subida [rpm/s]", () => eng.MaxRpsPerS * 60.0, v => eng.MaxRpsPerS = v / 60.0, 0.3, "F1", 0.3, 60.0);
            AddValueRow("Rampa bajada [rpm/s]", () => eng.MaxRpsPerSDecel * 60.0, v => eng.MaxRpsPerSDecel = v / 60.0, 0.3, "F1", 0.3, 90.0);
            AddValueRow("Retardo inversión [s]", () => eng.ReversalDelayS, v => eng.ReversalDelayS = v, 1.0, "F0", 0.0, 90.0);

            AddCycleRow("Combustible",
                new[] { "Diésel marino (MDO)", "Fuel pesado (HFO)", "Gas natural (LNG)" },
                () => (int)eng.Fuel,
                i => eng.Fuel = (FuelType)i);

            AddValueRow("SFOC base C [g/kWh]", () => eng.SfocC, v => eng.SfocC = v, 5.0, "F0", 150.0, 400.0);

            AddToggleRow("Motor listo (Engine Ready)",
                () => _runner.Sim.Command.EngineReady,
                v => _runner.Sim.Command.EngineReady = v);

            AddToggleRow("PARADA DE EMERGENCIA",
                () => _runner.PanelEmergencyStop,
                v => _runner.PanelEmergencyStop = v, DangerColor);

            AddLiveRow("Consumo", () =>
                $"{_runner.Sim.State.FuelFlowKgPerH:F0} kg/h   acumulado {_runner.Sim.State.FuelUsedKg:F0} kg");
        }

        private void BuildPropellerTab()
        {
            var prop = _runner.Config.Propeller;
            var bow = _runner.Config.BowThruster;
            Action sync = () => _runner.NotifyConfigChanged();

            AddValueRow("Diámetro hélice [m]", () => prop.Diameter, v => prop.Diameter = v, 0.1, "F2", 0.5, 12.0, sync);
            AddValueRow("KT: k0", () => prop.K0, v => prop.K0 = v, 0.005, "F3", 0.05, 0.6, sync);
            AddValueRow("KT: k1", () => prop.K1, v => prop.K1 = v, 0.005, "F3", -0.6, 0.2, sync);
            AddValueRow("KT: k2", () => prop.K2, v => prop.K2 = v, 0.005, "F3", -0.5, 0.2, sync);
            AddValueRow("KQ: kq0", () => prop.Kq0, v => prop.Kq0 = v, 0.001, "F3", 0.01, 0.12);
            AddValueRow("KQ: kq1", () => prop.Kq1, v => prop.Kq1 = v, 0.001, "F3", -0.10, 0.0);
            AddValueRow("Estela wP0", () => prop.WakeFraction, v => prop.WakeFraction = v, 0.01, "F2", 0.0, 0.6, sync);
            AddValueRow("Deducción empuje tP", () => prop.ThrustDeduction, v => prop.ThrustDeduction = v, 0.01, "F2", 0.0, 0.4, sync);
            AddValueRow("Factor marcha atrás", () => prop.AsternThrustFactor, v => prop.AsternThrustFactor = v, 0.05, "F2", 0.3, 1.0, sync);
            AddValueRow("Thruster proa [kN]", () => bow.MaxThrustN / 1000.0, v => bow.MaxThrustN = v * 1000.0, 10.0, "F0", 0.0, 800.0);
            AddValueRow("Posición thruster [m]", () => bow.LongitudinalPositionM, v => bow.LongitudinalPositionM = v, 5.0, "F0", 0.0, 200.0);
        }

        private void BuildSteeringTab()
        {
            var cmd = _runner.Sim.Command;
            var rud = _runner.Config.Rudder;
            var ap = _runner.Config.Autopilot;

            AddCycleRow("Modo de gobierno",
                new[] { "HAND (rueda)", "NFU (palanca)", "AUTO (piloto)" },
                () => (int)cmd.SteeringMode,
                i => cmd.SteeringMode = (SteeringMode)i);

            AddHoldRow("NFU timón directo", "◄ BABOR", "ESTRIBOR ►", dir => _runner.NfuHold = dir);

            AddValueRow("Rumbo autopiloto [°]",
                () => cmd.HeadingSetpointDeg,
                v => cmd.HeadingSetpointDeg = ShipState.Normalize360(v),
                1.0, "F0", -1.0, 360.0);

            AddValueRow("Timón máximo [°]", () => rud.MaxAngleDeg, v => rud.MaxAngleDeg = v, 1.0, "F0", 10.0, 45.0);
            AddValueRow("Velocidad timón [°/s]", () => rud.MaxRateDegPerS, v => rud.MaxRateDegPerS = v, 0.5, "F1", 0.5, 12.0);
            AddValueRow("AP ganancia Kp", () => ap.Kp, v => ap.Kp = v, 0.2, "F1", 0.1, 12.0);
            AddValueRow("AP ganancia Ki", () => ap.Ki, v => ap.Ki = v, 0.01, "F2", 0.0, 0.5);
            AddValueRow("AP ganancia Kd", () => ap.Kd, v => ap.Kd = v, 10.0, "F0", 0.0, 500.0);
            AddValueRow("AP límite timón [°]", () => ap.RudderLimitDeg, v => ap.RudderLimitDeg = v, 1.0, "F0", 5.0, 35.0);

            AddToggleRow("Bomba de gobierno 1", () => cmd.SteeringPump1, v => cmd.SteeringPump1 = v);
            AddToggleRow("Bomba de gobierno 2", () => cmd.SteeringPump2, v => cmd.SteeringPump2 = v);
        }

        private void BuildEnvironmentTab()
        {
            var env = _runner.Env;

            AddValueRow("Corriente [kn]", () => env.CurrentSpeedMs * 1.9438,
                v => env.CurrentSpeedMs = v / 1.9438, 0.1, "F1", 0.0, 8.0);
            AddValueRow("Dir. corriente (hacia) [°]", () => env.CurrentSetToDeg,
                v => env.CurrentSetToDeg = ShipState.Normalize360(v), 5.0, "F0", -1.0, 360.0);
            AddValueRow("Viento [kn]", () => env.WindSpeedMs * 1.9438,
                v => env.WindSpeedMs = v / 1.9438, 2.0, "F0", 0.0, 90.0);
            AddValueRow("Dir. viento (desde) [°]", () => env.WindFromDeg,
                v => env.WindFromDeg = ShipState.Normalize360(v), 5.0, "F0", -1.0, 360.0);
            AddValueRow("Altura olas Hs [m]", () => env.WaveHeightM, v => env.WaveHeightM = v, 0.25, "F2", 0.0, 9.0);
            AddValueRow("Periodo pico Tp [s]", () => env.WavePeriodS, v => env.WavePeriodS = v, 0.5, "F1", 2.0, 22.0);
            AddValueRow("Dir. oleaje (desde) [°]", () => env.WaveFromDeg,
                v => env.WaveFromDeg = ShipState.Normalize360(v), 5.0, "F0", -1.0, 360.0);
            AddValueRow("Periodo balance [s]", () => env.RollNaturalPeriodS,
                v => env.RollNaturalPeriodS = v, 0.5, "F1", 4.0, 30.0);
            AddValueRow("Profundidad [m]", () => env.WaterDepthM, v => env.WaterDepthM = v, 5.0, "F0", 5.0, 2000.0);
            AddValueRow("Marea [m] (chart datum)", () => env.TideHeightM, v => env.TideHeightM = v, 0.1, "F2", -2.0, 8.0);
            AddValueRow("Densidad agua [kg/m³]", () => env.WaterDensity, v => env.WaterDensity = v, 5.0, "F0", 995.0, 1035.0);
            AddToggleRow("NMEA UDP → plotter (OpenCPN)",
                () => _runner.Nmea != null && _runner.Nmea.Enabled,
                v =>
                {
                    if (_runner.Nmea != null)
                    {
                        _runner.Nmea.Enabled = v;
                    }
                });
            AddActionRow("Tráfico", "RECARGAR DEMO", () => _runner.Traffic?.LoadCoastalDemo());
        }

        private void BuildInstrumentsTab()
        {
            var rowRt = CreateRowBackground(560f);
            var text = CreateText(rowRt, "Instruments", new Vector2(24f, -12f),
                new Vector2(CanvasWidth - 96f, 540f), "", 24f, TextAlignmentOptions.TopLeft, TextColor);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            _liveTexts.Add((text, BuildInstrumentReadout));
        }

        private string BuildInstrumentReadout()
        {
            var s = _runner.Sim.State;
            var cmd = _runner.Sim.Command;
            var env = _runner.Env;
            var bridge = ShipControlState.Instance;

            string telegraph = bridge != null ? bridge.Telegraph.ToString() : "n/a";
            double orderRpm = cmd.TelegraphFraction * _runner.Config.Engine.RatedRps * 60.0;

            string gps = "—";
            if (_runner.Geo != null)
            {
                _runner.Geo.ToLatLon(s.North, s.East, out double lat, out double lon);
                gps = $"{GeoDatum.FormatLat(lat)}  {GeoDatum.FormatLon(lon)}";
            }

            int contacts = _runner.Traffic?.Contacts.Count ?? 0;
            string nmea = _runner.Nmea != null && _runner.Nmea.Enabled ? "ON" : "off";

            return
                $"RUMBO {s.HeadingDeg,6:F1}°     ROT {s.RotDegPerMin,6:F1} °/min\n" +
                $"SOG {s.SogMs * 1.9438,5:F1} kn   STW {s.StwMs * 1.9438,5:F1} kn   COG {s.CogDeg,5:F0}°\n" +
                $"TIMÓN {s.RudderAngleDeg,6:F1}°   (orden {_runner.Sim.ResolvedRudderCommandDeg,6:F1}°)   MODO {cmd.SteeringMode}\n" +
                $"TELÉGRAFO {telegraph}\n" +
                $"RPM {s.ShaftRps * 60.0,6:F1}   (orden {orderRpm,6:F1})\n" +
                $"EMPUJE {s.PropThrustN / 1000.0,8:F0} kN    PAR {s.PropTorqueNm / 1000.0,8:F0} kN·m\n" +
                $"POTENCIA EJE {s.ShaftPowerW / 1000.0,8:F0} kW    CARGA {s.EngineLoad,5:P0}\n" +
                $"COMBUSTIBLE {s.FuelFlowKgPerH,6:F0} kg/h    ACUM. {s.FuelUsedKg,7:F0} kg\n" +
                $"J {s.AdvanceRatioJ,5:F2}\n" +
                $"VIENTO {env.WindSpeedMs * 1.9438,4:F0} kn @ {env.WindFromDeg,3:F0}°   CORRIENTE {env.CurrentSpeedMs * 1.9438,4:F1} kn @ {env.CurrentSetToDeg,3:F0}°\n" +
                $"OLAS Hs {env.WaveHeightM,4:F1} m  Tp {env.WavePeriodS,4:F1} s @ {env.WaveFromDeg,3:F0}°\n" +
                $"HEAVE {s.HeaveM,5:F2} m   ROLL {s.RollDeg,5:F1}°   PITCH {s.PitchDeg,5:F1}°\n" +
                $"GPS {gps}\n" +
                $"POS N {s.North,8:F0} m   E {s.East,8:F0} m    t {s.TimeS,7:F0} s\n" +
                $"TIDE {env.TideHeightM:F2} m   PROF {env.WaterDepthM:F0} m   ×{_runner.TimeScale:F0}   NMEA {nmea}\n" +
                $"TRÁFICO {contacts}   Quest: izq.X menú · izq.Y conning · der.B config · der.A panel";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Row builders
        // ─────────────────────────────────────────────────────────────────────

        private RectTransform CreateRowBackground(float height = RowHeight)
        {
            var img = CreateImage(_contentRoot, "Row", new Vector2(28f, -_rowY),
                new Vector2(CanvasWidth - 56f, height - 6f), RowColor);
            _rowY += height;
            return img.rectTransform;
        }

        private void AddValueRow(string label, Func<double> getter, Action<double> setter,
            double step, string format, double min, double max, Action onChanged = null)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(420f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);

            var valueText = CreateText(row, "Value", new Vector2(560f, 0f), new Vector2(200f, RowHeight - 10f),
                "", 23f, TextAlignmentOptions.Center, Color.white);

            Action apply = () =>
            {
                double v = Math.Max(min, Math.Min(max, getter()));
                setter(v);
                onChanged?.Invoke();
            };

            CreateButton(row, new Vector2(488f, -4f), new Vector2(64f, RowHeight - 14f), "−", ButtonColor, () =>
            {
                setter(Math.Max(min, getter() - step));
                onChanged?.Invoke();
            }, true);

            CreateButton(row, new Vector2(766f, -4f), new Vector2(64f, RowHeight - 14f), "+", ButtonColor, () =>
            {
                setter(Math.Min(max, getter() + step));
                onChanged?.Invoke();
            }, true);

            _liveTexts.Add((valueText, () => getter().ToString(format)));
            apply();
        }

        private void AddCycleRow(string label, string[] options, Func<int> getIndex, Action<int> setIndex)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(420f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);

            var valueText = CreateText(row, "Value", new Vector2(548f, 0f), new Vector2(230f, RowHeight - 10f),
                "", 21f, TextAlignmentOptions.Center, Color.white);

            CreateButton(row, new Vector2(488f, -4f), new Vector2(52f, RowHeight - 14f), "<", ButtonColor, () =>
            {
                int i = (getIndex() - 1 + options.Length) % options.Length;
                setIndex(i);
            }, false);

            CreateButton(row, new Vector2(782f, -4f), new Vector2(52f, RowHeight - 14f), ">", ButtonColor, () =>
            {
                int i = (getIndex() + 1) % options.Length;
                setIndex(i);
            }, false);

            _liveTexts.Add((valueText, () =>
            {
                int i = Mathf.Clamp(getIndex(), 0, options.Length - 1);
                return options[i];
            }));
        }

        private void AddToggleRow(string label, Func<bool> getter, Action<bool> setter, Color? onColor = null)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(520f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);

            SimUiButton btn = null;
            TMP_Text btnLabel = null;
            btn = CreateButton(row, new Vector2(560f, -4f), new Vector2(270f, RowHeight - 14f),
                "", ButtonColor, () => setter(!getter()), false);
            btnLabel = btn.GetComponentInChildren<TMP_Text>();

            var activeColor = onColor ?? new Color(0.15f, 0.55f, 0.3f, 1f);
            _liveTexts.Add((btnLabel, () =>
            {
                bool on = getter();
                var img = btn.GetComponent<Image>();
                img.color = on ? activeColor : new Color(0.25f, 0.28f, 0.32f, 1f);
                btn.Bind(img);
                return on ? "ACTIVADO" : "DESACTIVADO";
            }));
        }

        private void AddActionRow(string label, string buttonText, Action action, Color? color = null)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(420f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);
            CreateButton(row, new Vector2(488f, -4f), new Vector2(342f, RowHeight - 14f),
                buttonText, color ?? ButtonColor, action, false);
        }

        private void AddDualActionRow(string label,
            string leftText, Action leftAction, string rightText, Action rightAction)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(300f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);
            CreateButton(row, new Vector2(330f, -4f), new Vector2(240f, RowHeight - 14f),
                leftText, ButtonColor, leftAction, false);
            CreateButton(row, new Vector2(590f, -4f), new Vector2(240f, RowHeight - 14f),
                rightText, ButtonColor, rightAction, false);
        }

        private void AddHoldRow(string label, string leftText, string rightText, Action<int> onHold)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(300f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);

            var left = CreateButton(row, new Vector2(330f, -4f), new Vector2(240f, RowHeight - 14f),
                leftText, AccentColor, null, false);
            left.OnHoldChanged = held => onHold(held ? -1 : 0);

            var right = CreateButton(row, new Vector2(590f, -4f), new Vector2(240f, RowHeight - 14f),
                rightText, AccentColor, null, false);
            right.OnHoldChanged = held => onHold(held ? 1 : 0);
        }

        private void AddLiveRow(string label, Func<string> supplier)
        {
            var row = CreateRowBackground();
            CreateText(row, "Label", new Vector2(18f, 0f), new Vector2(340f, RowHeight - 10f),
                label, 22f, TextAlignmentOptions.Left, TextColor);
            var value = CreateText(row, "Value", new Vector2(360f, 0f), new Vector2(540f, RowHeight - 10f),
                "", 21f, TextAlignmentOptions.Right, Color.white);
            _liveTexts.Add((value, supplier));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Primitive widget helpers
        // ─────────────────────────────────────────────────────────────────────

        private static Image CreateImage(RectTransform parent, string name,
            Vector2 topLeft, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text CreateText(RectTransform parent, string name,
            Vector2 topLeft, Vector2 size, string content, float fontSize,
            TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static SimUiButton CreateButton(RectTransform parent, Vector2 topLeft, Vector2 size,
            string label, Color color, Action onClick, bool autoRepeat)
        {
            var img = CreateImage(parent, $"Button_{label}", topLeft, size, color);
            var go = img.gameObject;

            CreateText(img.rectTransform, "Label", new Vector2(0f, 0f), size, label, 22f,
                TextAlignmentOptions.Center, Color.white);

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);
            collider.center = new Vector3(size.x * 0.5f, -size.y * 0.5f, 0f);

            var btn = go.AddComponent<SimUiButton>();
            btn.Bind(img);
            btn.OnClick = onClick;
            btn.AutoRepeat = autoRepeat;
            return btn;
        }
    }
}
