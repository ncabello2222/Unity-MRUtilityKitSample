using System;
using System.Collections.Generic;
using NavigationSim.Core;
using ShipBridgePrototype;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Binds the FCU-imported OpenBridge conning case to the live simulation.
    /// The imported hierarchy remains the visual source of truth; this component
    /// only resolves stable labels/sections and updates their values.
    /// </summary>
    public sealed class OpenBridgeConningBinder : MonoBehaviour
    {
        private const double MsToKnots = 1.94384449244;

        private NavigationSimRunner _runner;
        private Transform _caseRoot;
        private Transform _conningCompass;

        private TMP_Text _hdg;
        private TMP_Text _cog;
        private TMP_Text _rot;
        private TMP_Text _wind;
        private TMP_Text _current;
        private TMP_Text _depth;
        private TMP_Text _pitch;
        private TMP_Text _roll;
        private TMP_Text _heave;
        private TMP_Text _windSpeed;
        private TMP_Text _windDirection;
        private TMP_Text _northPosition;
        private TMP_Text _eastPosition;
        private TMP_Text _clock;
        private TMP_Text _date;
        private TMP_Text _steeringMode;
        private TMP_Text _legCourse;

        private TMP_Text _engineLoad;
        private TMP_Text _engineRpm;
        private TMP_Text _thrusterOrder;
        private TMP_Text _thrusterActual;
        private TMP_Text _rudderOrder;
        private TMP_Text _rudderActual;

        private readonly List<TMP_Text> _speedVectorValues = new();

        private Transform _headingGraphic;
        private Transform _cogGraphic;
        private Transform _windGraphic;
        private Transform _currentGraphic;
        private Transform _setpointGraphic;

        public void Initialize(NavigationSimRunner runner)
        {
            _runner = runner;
            _caseRoot = transform;
            _conningCompass = FindDescendant(_caseRoot, "Conning Compass");

            RepairImportArtifacts(_caseRoot);
            ConfigureRelevantEquipment();
            CacheBindings();
            Refresh();
        }

        private static readonly HashSet<string> StretchedSpriteNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "north-arrow",
            "BoldLine",
            "Ship",
            "Arrow-5",
            "arrow-medium",
            "Arrow"
        };

        /// <summary>
        /// Fixes FCU import artifacts on a conning hierarchy (scene workspace or runtime clone).
        /// </summary>
        public static int RepairImportArtifacts(Transform root)
        {
            if (root == null)
            {
                return 0;
            }

            int fixedCount = 0;
            fixedCount += RepairStretchedSprites(root);
            fixedCount += RepairStretchedArrowContainers(root);
            fixedCount += DisableBrokenOutlineEffects(root);
            return fixedCount;
        }

        public void Refresh()
        {
            if (_runner == null || _runner.Sim == null)
            {
                return;
            }

            ShipState state = _runner.Sim.State;
            EnvironmentState env = _runner.Env;
            ShipControlState bridge = ShipControlState.Instance;

            double sogKn = state.SogMs * MsToKnots;
            double stwKn = state.StwMs * MsToKnots;
            double currentKn = env.CurrentSpeedMs * MsToKnots;
            double windKn = env.WindSpeedMs * MsToKnots;
            double rpm = state.ShaftRps * 60.0;

            Set(_hdg, $"{state.HeadingDeg:000}");
            Set(_cog, $"{state.CogDeg:000}");
            Set(_rot, $"{state.RotDegPerMin:+0.0;-0.0;0.0}");
            Set(_wind, $"{windKn:0.0}");
            Set(_current, $"{currentKn:0.0}");
            Set(_depth, $"{env.WaterDepthM:0.0}");

            Set(_pitch, $"{state.PitchDeg:+0.0;-0.0;0.0}");
            Set(_roll, $"{state.RollDeg:+0.0;-0.0;0.0}");
            Set(_heave, $"{state.HeaveM:+0.00;-0.00;0.00}");
            Set(_windSpeed, $"{windKn:0.0}");
            Set(_windDirection, $"{env.WindFromDeg:000}");

            Set(_northPosition, $"N {state.North:+0;-0;0} m");
            Set(_eastPosition, $"E {state.East:+0;-0;0} m");

            TimeSpan simTime = TimeSpan.FromSeconds(Math.Max(0.0, state.TimeS));
            Set(_clock, $"{(int)simTime.TotalHours:00}:{simTime.Minutes:00}:{simTime.Seconds:00}");
            Set(_date, "SIM TIME");
            Set(_steeringMode, SteeringModeLabel(_runner.Sim.Command.SteeringMode));
            Set(_legCourse, $"{_runner.Sim.Command.HeadingSetpointDeg:000}");

            Set(_engineLoad, $"{state.EngineLoad * 100.0:0}");
            Set(_engineRpm, $"{rpm:0}");

            float thruster = bridge != null ? bridge.BowThruster : (float)_runner.Sim.Command.BowThruster;
            Set(_thrusterOrder, $"{thruster * 100f:+0;-0;0}");
            Set(_thrusterActual, $"{thruster * 100f:+0;-0;0}");

            double rudderOrder = _runner.Sim.ResolvedRudderCommandDeg;
            Set(_rudderOrder, $"{rudderOrder:+0.0;-0.0;0.0}");
            Set(_rudderActual, $"{state.RudderAngleDeg:+0.0;-0.0;0.0}");

            if (_speedVectorValues.Count > 0)
            {
                Set(_speedVectorValues[0], $"{stwKn:0.0}");
            }
            if (_speedVectorValues.Count > 1)
            {
                Set(_speedVectorValues[1], $"{sogKn:0.0}");
            }
            if (_speedVectorValues.Count > 2)
            {
                Set(_speedVectorValues[2], $"{Math.Abs(sogKn - stwKn):0.0}");
            }

            RotateCompassGraphic(_headingGraphic, state.HeadingDeg);
            RotateCompassGraphic(_cogGraphic, state.CogDeg);
            RotateCompassGraphic(_windGraphic, env.WindFromDeg);
            RotateCompassGraphic(_currentGraphic, env.CurrentSetToDeg);
            RotateCompassGraphic(_setpointGraphic, _runner.Sim.Command.HeadingSetpointDeg);
        }

        private void CacheBindings()
        {
            _hdg = FindInstrumentOutput(_conningCompass, "HDG");
            _cog = FindInstrumentOutput(_conningCompass, "COG");
            _rot = FindInstrumentOutput(_conningCompass, "ROT");
            _wind = FindInstrumentOutput(_conningCompass, "Wind");
            _current = FindInstrumentOutput(_conningCompass, "Current");

            Transform motion = FindDescendant(_caseRoot, "Frame 333");
            _pitch = FindInstrumentOutput(motion, "Pitch");
            _roll = FindInstrumentOutput(motion, "Roll");
            _heave = FindInstrumentOutput(motion, "Heave");

            Transform windCard = FindDescendant(_caseRoot, "Group 27");
            _windSpeed = FindInstrumentOutput(windCard, "Speed");
            _windDirection = FindInstrumentOutput(windCard, "Direction");

            Transform depthValue = FindDescendant(_caseRoot, "Vertical-S");
            _depth = FindTextNamed(depthValue, "000");

            TMP_Text[] texts = _caseRoot.GetComponentsInChildren<TMP_Text>(true);
            _northPosition = FindTextByInitialValue(texts, "41°03.441");
            _eastPosition = FindTextByInitialValue(texts, "071°16.676");
            _clock = FindTextByInitialValue(texts, "14:34:32");
            _date = FindTextByInitialValue(texts, "12-08-2021");

            Transform steering = FindDescendant(_caseRoot, "Frame 303");
            _steeringMode = FindTextByInitialValue(
                steering != null ? steering.GetComponentsInChildren<TMP_Text>(true) : Array.Empty<TMP_Text>(),
                "Track");

            Transform currentLeg = FindDescendant(_caseRoot, "Frame 300");
            _legCourse = FindInstrumentOutput(currentLeg, "Course");

            Transform compass = FindDescendant(_caseRoot, "Conning compass L");
            _headingGraphic = FindDirectChild(FindDirectChild(compass, "Compass"), "Heading");
            _cogGraphic = FindDirectChild(FindDirectChild(compass, "Compass"), "COG");
            _windGraphic = FindDirectChild(FindDirectChild(compass, "Compass"), "Wind");
            _currentGraphic = FindDirectChild(FindDirectChild(compass, "Compass"), "Current");
            _setpointGraphic = FindDirectChild(FindDirectChild(compass, "Compass"), "Setpoint");

            Transform vectors = FindDescendant(_caseRoot, "Frame 351");
            if (vectors != null)
            {
                foreach (TMP_Text text in vectors.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "2.3")
                    {
                        _speedVectorValues.Add(text);
                    }
                }
            }
        }

        private void ConfigureRelevantEquipment()
        {
            List<Transform> engines = FindAllDescendants(_caseRoot, "Main engine Labeled");
            if (engines.Count > 0)
            {
                Transform engine = engines[0];
                SetSectionTitle(engine, "MAIN ENGINE");
                TMP_Text loadLabel = FindTextByExactValue(engine, "Pitch");
                if (loadLabel != null)
                {
                    loadLabel.text = "Load";
                }
                _engineLoad = FindInstrumentOutput(engine, "Load");

                TMP_Text rpmLabel = FindTextByExactValue(engine, "Power");
                if (rpmLabel != null)
                {
                    rpmLabel.text = "RPM";
                }
                _engineRpm = FindInstrumentOutput(engine, "RPM");
            }
            DisableFromIndex(engines, 1);

            List<Transform> azimuths = FindAllDescendants(_caseRoot, "Azimuth Labeled");
            DisableFromIndex(azimuths, 0);

            List<Transform> thrusters = FindAllDescendants(_caseRoot, "Tunnel thruster Labeled");
            if (thrusters.Count > 0)
            {
                Transform thruster = thrusters[0];
                SetSectionTitle(thruster, "BOW THRUSTER");
                _thrusterOrder = FindInstrumentInput(thruster);
                _thrusterActual = FindInstrumentOutput(thruster, "Power");
            }

            List<Transform> rudders = FindAllDescendants(_caseRoot, "Rudder labeled");
            if (rudders.Count > 0)
            {
                Transform rudder = rudders[0];
                SetSectionTitle(rudder, "RUDDER");
                _rudderOrder = FindInstrumentInput(rudder);
                _rudderActual = FindInstrumentOutput(rudder, "Angle");
            }
            DisableFromIndex(rudders, 1);
        }

        /// <summary>
        /// FCU often imports thin compass sprites (BoldLine, Ship, north-arrow,
        /// Arrow) with stretch anchors outside [0,1]. That turns fine needles into
        /// fat blue/black rectangles. Restore export size (sprites are 4x).
        /// </summary>
        private static int RepairStretchedSprites(Transform root)
        {
            int fixedCount = 0;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.sprite.texture == null)
                {
                    continue;
                }

                bool nameMatch = StretchedSpriteNames.Contains(image.name)
                                 || StretchedSpriteNames.Contains(image.sprite.name);
                if (!nameMatch)
                {
                    continue;
                }

                if (TryRepairStretchedRect(
                        image.rectTransform,
                        image.sprite.texture.width * 0.25f,
                        image.sprite.texture.height * 0.25f))
                {
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        /// <summary>
        /// Some Arrow nodes are empty containers with stretch anchors; size them
        /// from their child Icon frame / Image so tips stop becoming rectangles.
        /// </summary>
        private static int RepairStretchedArrowContainers(Transform root)
        {
            int fixedCount = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "Arrow" || child is not RectTransform rect)
                {
                    continue;
                }

                if (!HasOutOfRangeAnchors(rect))
                {
                    continue;
                }

                float width = 36f;
                float height = 46f;
                Image childImage = child.GetComponentInChildren<Image>(true);
                if (childImage != null && childImage.sprite != null && childImage.sprite.texture != null)
                {
                    width = childImage.sprite.texture.width * 0.25f;
                    height = childImage.sprite.texture.height * 0.25f;
                }
                else
                {
                    foreach (Transform nested in child)
                    {
                        if (nested is RectTransform nestedRect && nestedRect.sizeDelta.sqrMagnitude > 1f)
                        {
                            width = nestedRect.sizeDelta.x;
                            height = nestedRect.sizeDelta.y;
                            break;
                        }
                    }
                }

                if (TryRepairStretchedRect(rect, width, height))
                {
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        private static bool HasOutOfRangeAnchors(RectTransform rect)
        {
            return rect.anchorMin.x < -0.01f || rect.anchorMin.y < -0.01f
                   || rect.anchorMax.x > 1.01f || rect.anchorMax.y > 1.01f;
        }

        private static bool TryRepairStretchedRect(RectTransform rect, float width, float height)
        {
            bool stretched = HasOutOfRangeAnchors(rect)
                             || !Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x)
                             || !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y)
                             || rect.rect.width > width * 1.5f
                             || rect.rect.height > height * 1.5f;
            if (!stretched)
            {
                return false;
            }

            // Keep world placement: FCU used out-of-range anchors for tip offsets.
            Vector3 worldPosition = rect.position;
            Quaternion worldRotation = rect.rotation;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.position = worldPosition;
            rect.rotation = worldRotation;
            return true;
        }

        /// <summary>
        /// Imported DAOutlineEffect paths can throw FormatException during mesh
        /// rebuild (and player builds). Disable them under OpenBridge so the
        /// sprites remain while outlines are skipped.
        /// </summary>
        private static int DisableBrokenOutlineEffects(Transform root)
        {
            int disabled = 0;
            foreach (var outline in root.GetComponentsInChildren<DA_Assets.DAO.DAOutlineEffect>(true))
            {
                if (outline == null || !outline.enabled)
                {
                    continue;
                }

                outline.enabled = false;
                disabled++;
            }

            return disabled;
        }

        private static TMP_Text FindInstrumentOutput(Transform scope, string label)
        {
            if (scope == null)
            {
                return null;
            }

            TMP_Text labelText = FindTextByExactValue(scope, label);
            if (labelText == null)
            {
                return null;
            }

            Transform instrument = labelText.transform;
            while (instrument != null && !instrument.name.StartsWith("Instrument field", StringComparison.OrdinalIgnoreCase))
            {
                instrument = instrument.parent;
            }

            if (instrument == null)
            {
                return null;
            }

            foreach (TMP_Text text in instrument.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "value-actual" && !HasAncestorNamed(text.transform, instrument, "container-input"))
                {
                    return text;
                }
            }

            return null;
        }

        private static TMP_Text FindInstrumentInput(Transform scope)
        {
            if (scope == null)
            {
                return null;
            }

            foreach (TMP_Text text in scope.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "value-actual" && HasAncestorNamed(text.transform, scope, "container-input"))
                {
                    return text;
                }
            }

            return null;
        }

        private static bool HasAncestorNamed(Transform child, Transform stop, string name)
        {
            Transform current = child.parent;
            while (current != null && current != stop)
            {
                if (current.name == name)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static TMP_Text FindTextNamed(Transform scope, string name)
        {
            if (scope == null)
            {
                return null;
            }

            foreach (TMP_Text text in scope.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == name)
                {
                    return text;
                }
            }
            return null;
        }

        private static TMP_Text FindTextByExactValue(Transform scope, string value)
        {
            if (scope == null)
            {
                return null;
            }
            return FindTextByExactValue(scope.GetComponentsInChildren<TMP_Text>(true), value);
        }

        private static TMP_Text FindTextByExactValue(TMP_Text[] texts, string value)
        {
            foreach (TMP_Text text in texts)
            {
                if (string.Equals(text.text.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
            return null;
        }

        private static TMP_Text FindTextByInitialValue(TMP_Text[] texts, string prefix)
        {
            foreach (TMP_Text text in texts)
            {
                if (text.text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return text;
                }
            }
            return null;
        }

        private static Transform FindDescendant(Transform scope, string name)
        {
            if (scope == null)
            {
                return null;
            }

            foreach (Transform child in scope.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        private static List<Transform> FindAllDescendants(Transform scope, string name)
        {
            var matches = new List<Transform>();
            if (scope == null)
            {
                return matches;
            }

            foreach (Transform child in scope.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    matches.Add(child);
                }
            }
            return matches;
        }

        private static void DisableFromIndex(List<Transform> transforms, int firstIndex)
        {
            for (int i = firstIndex; i < transforms.Count; i++)
            {
                transforms[i].gameObject.SetActive(false);
            }
        }

        private static void SetSectionTitle(Transform section, string value)
        {
            foreach (TMP_Text text in section.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "title")
                {
                    text.text = value;
                    return;
                }
            }
        }

        private static void RotateCompassGraphic(Transform graphic, double compassDeg)
        {
            if (graphic != null)
            {
                graphic.localRotation = Quaternion.Euler(0f, 0f, (float)-compassDeg);
            }
        }

        private static string SteeringModeLabel(SteeringMode mode)
        {
            return mode switch
            {
                SteeringMode.Hand => "Hand",
                SteeringMode.Nfu => "NFU",
                SteeringMode.Auto => "Auto",
                _ => mode.ToString()
            };
        }

        private static void Set(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }
}
