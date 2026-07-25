using System;
using System.Collections.Generic;
using System.Globalization;
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

        private static readonly Color AccentBlue = new Color32(0x00, 0x70, 0xD6, 0xFF);

        private enum Facing
        {
            /// <summary>Leave the imported rotation alone: needle roots are turned by their bearing.</summary>
            Keep,

            /// <summary>
            /// Sprite is exported already turned to the bearing the design was captured
            /// at, so it has to be held level with the compass. Turning its needle root
            /// from there still reads as the live bearing.
            /// </summary>
            Compass
        }

        /// <summary>
        /// Placement of a compass node, taken from the Figma source
        /// (file wp9RvYJDlsXg5e7aPQNI6o, node 6514:23155 "Conning compass L").
        /// Offsets are in compass units from its centre, y up.
        /// </summary>
        private readonly struct CompassNode
        {
            public CompassNode(string path, float offsetX, float offsetY, float width, float height,
                Facing facing = Facing.Keep)
            {
                Path = path;
                Offset = new Vector2(offsetX, offsetY);
                Size = new Vector2(width, height);
                Facing = facing;
            }

            public string Path { get; }
            public Vector2 Offset { get; }
            public Vector2 Size { get; }
            public Facing Facing { get; }
        }

        // Parents first: each entry is placed by world position, so a later parent
        // move would drag already-placed children out of position.
        private static readonly CompassNode[] CompassLayout =
        {
            new CompassNode("Compass/Compass watchface/Watchface/labels/45°", 0f, -0.4f, 491.16f, 491.16f),

            new CompassNode("Compass/Current", 0f, 0f, 63.38f, 507f),
            new CompassNode("Compass/Wind", 0f, 0f, 63.38f, 507f),

            new CompassNode("Compass/Setpoint", 0f, 0f, 63.38f, 507f),
            new CompassNode("Compass/Setpoint/Arrow", 91.1f, 157.8f, 64.94f, 64.94f, Facing.Compass),

            new CompassNode("Compass/Heading", 0f, 0f, 63.38f, 507f),
            new CompassNode("Compass/Heading/Ship", 0f, 0f, 264.25f, 489f, Facing.Compass),
            new CompassNode("Compass/Heading/HDG", 0f, 0f, 63.38f, 507f),

            new CompassNode("Compass/COG", 0f, 0f, 63.38f, 507f),
            new CompassNode("Compass/COG/BoldLine", -11.4f, -15f, 189.83f, 248.63f, Facing.Compass),
            new CompassNode("Compass/COG/Center", 0f, 0f, 15.84f, 15.84f, Facing.Compass)
        };

        /// <summary>
        /// A Figma boolean operation exports no sprite through FCU, so it leaves an
        /// Image with a null sprite that Unity draws as an opaque block over the
        /// compass. Each one is re-rendered from the design at 4x into Resources;
        /// the children the operation consumed are hidden behind it.
        /// </summary>
        private readonly struct BooleanShape
        {
            public BooleanShape(string path, string render, float offsetX, float offsetY, float width, float height)
            {
                Path = path;
                Render = render;
                Offset = new Vector2(offsetX, offsetY);
                Size = new Vector2(width, height);
            }

            public string Path { get; }
            public string Render { get; }
            public Vector2 Offset { get; }
            public Vector2 Size { get; }
        }

        private const string RenderFolder = "OpenBridgeConning/";

        // Sizes are the render bounds Figma reports, which is exactly the pixel size
        // of the export divided by its 4x scale.
        private static readonly BooleanShape[] BooleanShapes =
        {
            new BooleanShape("Compass/Current/Circle compass arrow HDG - Large/Menu icon/Icon frame",
                "conning-current-icon", -104f, 204.7f, 30.75f, 46f),
            new BooleanShape("Compass/Wind/wind-arrow/Icon/Icon frame",
                "conning-wind-icon", -171.4f, 158.4f, 40.25f, 45.25f),
            new BooleanShape("Compass/Heading/HDG/HDG",
                "conning-hdg-needle", 31.5f, 76.3f, 81f, 170.75f),
            // Figma reports render bounds for this one that sit outside its own
            // bounding box, so the arrow is centred on the box instead.
            new BooleanShape("Compass/COG/COG",
                "conning-cog-arrow", 91.4f, 120.6f, 41.75f, 47f)
        };

        private static Sprite _dotSprite;

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
            fixedCount += ConformToDesign(root);
            fixedCount += OverrideDesignQuirks(root);
            fixedCount += RepairConningCompass(root);
            fixedCount += DisableBrokenOutlineEffects(root);
            return fixedCount;
        }

        /// <summary>
        /// Two places where the source file is followed rather than reproduced. Its
        /// inner topbar was left at the 1220 width of a narrower case and never
        /// stretched to this one, which leaves a gap down the right of the screen;
        /// and every value carries a placeholder of faint leading zeros that the
        /// design never clears, which only clutters a live reading.
        /// </summary>
        private static int OverrideDesignQuirks(Transform root)
        {
            if (!(FindDescendant(root, CaseRootName) is RectTransform panel))
            {
                return 0;
            }

            int changed = 0;
            float missing = panel.rect.width - DesignedTopbarWidth;

            if (FindByPath(panel, "Topbar/Topbar") is RectTransform bar)
            {
                bar.sizeDelta = new Vector2(bar.rect.width + missing, bar.rect.height);
                bar.localPosition += new Vector3(missing * 0.5f, 0f, 0f);
                changed++;

                // The modules sit against the bar's right edge, so they travel with it.
                if (FindByPath(bar, "Topbar modules") is RectTransform modules)
                {
                    modules.localPosition += new Vector3(missing * 0.5f, 0f, 0f);
                    changed++;
                }
            }

            foreach (TMP_Text placeholder in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                bool isPlaceholder = placeholder.name == LeadingZeroes || placeholder.name == LeadingDigits;
                if (isPlaceholder && placeholder.gameObject.activeSelf)
                {
                    placeholder.gameObject.SetActive(false);
                    changed++;
                }
            }

            return changed;
        }

        private const string LayoutTable = RenderFolder + "conning-layout";
        private const string CaseRootName = "cases_conning_5.0";
        private const string LeadingZeroes = "value-zeros";
        private const string LeadingDigits = "value-digits";
        private const string LabelUnitGroup = "container-label-unit";
        private const float DesignedTopbarWidth = 1220f;

        private readonly struct DesignRect
        {
            public DesignRect(Vector2 centre, Vector2 size)
            {
                Centre = centre;
                Size = size;
            }

            /// <summary>From the case's top-left corner, y down, as Figma reports it.</summary>
            public Vector2 Centre { get; }

            public Vector2 Size { get; }
        }

        /// <summary>
        /// FCU rebuilds Figma auto-layout out of Unity layout groups and the two
        /// disagree, so containers come out stretched and drag their contents with
        /// them. Pin every node the table names to the rect the design gives it and
        /// switch off the layout components that would push it back.
        /// </summary>
        private static int ConformToDesign(Transform root)
        {
            if (!(FindDescendant(root, CaseRootName) is RectTransform panel))
            {
                return 0;
            }

            Dictionary<string, DesignRect> table = LoadDesignRects();
            if (table.Count == 0)
            {
                return 0;
            }

            int pinned = 0;
            var pending = new Queue<KeyValuePair<Transform, string>>();
            pending.Enqueue(new KeyValuePair<Transform, string>(panel, string.Empty));
            var occurrences = new Dictionary<string, int>();

            while (pending.Count > 0)
            {
                KeyValuePair<Transform, string> step = pending.Dequeue();
                occurrences.Clear();

                for (int i = 0; i < step.Key.childCount; i++)
                {
                    Transform child = step.Key.GetChild(i);
                    occurrences.TryGetValue(child.name, out int seen);
                    occurrences[child.name] = seen + 1;

                    string path = $"{step.Value}/{child.name}#{seen}";
                    if (child is RectTransform rect && table.TryGetValue(path, out DesignRect design))
                    {
                        Pin(panel, rect, design);
                        pinned++;
                    }

                    pending.Enqueue(new KeyValuePair<Transform, string>(child, path));
                }
            }

            return pinned;
        }

        private static Dictionary<string, DesignRect> LoadDesignRects()
        {
            var table = new Dictionary<string, DesignRect>();
            var asset = Resources.Load<TextAsset>(LayoutTable);
            if (asset == null)
            {
                return table;
            }

            foreach (string line in asset.text.Split('\n'))
            {
                string[] fields = line.Trim().Split('\t');
                if (fields.Length != 5)
                {
                    continue;
                }

                table[fields[0]] = new DesignRect(
                    new Vector2(Parse(fields[1]), Parse(fields[2])),
                    new Vector2(Parse(fields[3]), Parse(fields[4])));
            }

            return table;
        }

        private static float Parse(string value)
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static void Pin(RectTransform panel, RectTransform target, DesignRect design)
        {
            foreach (Component component in target.GetComponents<Component>())
            {
                switch (component)
                {
                    case LayoutGroup group:
                        group.enabled = false;
                        break;
                    case ContentSizeFitter fitter:
                        fitter.enabled = false;
                        break;
                    case AspectRatioFitter aspect:
                        aspect.enabled = false;
                        break;
                    case LayoutElement element:
                        element.enabled = false;
                        break;
                }
            }

            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.sizeDelta = design.Size;

            Rect area = panel.rect;
            Vector3 world = panel.TransformPoint(new Vector3(
                area.xMin + design.Centre.x,
                area.yMax - design.Centre.y,
                0f));
            Vector3 local = target.parent.InverseTransformPoint(world);
            target.localPosition = new Vector3(local.x, local.y, target.localPosition.z);
        }

        /// <summary>
        /// FCU derives anchors for rotated Figma frames from their rotated bounding
        /// box, which scatters the compass needles, the diagonal labels and the
        /// arrow heads. Re-place them from the design geometry instead.
        /// </summary>
        private static int RepairConningCompass(Transform root)
        {
            if (!(FindDescendant(root, "Conning compass L") is RectTransform compass))
            {
                return 0;
            }

            int fixedCount = 0;

            foreach (CompassNode node in CompassLayout)
            {
                if (FindByPath(compass, node.Path) is RectTransform target)
                {
                    Place(compass, target, node);
                    fixedCount++;
                }
            }

            fixedCount += PlaceDiagonalLabels(compass);
            fixedCount += RestoreBooleanShapes(compass);

            Image centre = FindImage(compass, "Compass/COG/Center");
            if (centre != null)
            {
                if (centre.sprite == null)
                {
                    centre.sprite = GetDotSprite();
                }

                centre.color = AccentBlue;
                fixedCount++;
            }

            return fixedCount;
        }

        private static int RestoreBooleanShapes(RectTransform compass)
        {
            int restored = 0;

            foreach (BooleanShape shape in BooleanShapes)
            {
                if (!(FindByPath(compass, shape.Path) is RectTransform target))
                {
                    continue;
                }

                Image image = target.GetComponent<Image>();
                if (image == null)
                {
                    continue;
                }

                Sprite render = Resources.Load<Sprite>(RenderFolder + shape.Render);
                if (render == null)
                {
                    image.enabled = false;
                    continue;
                }

                foreach (Image consumed in target.GetComponentsInChildren<Image>(true))
                {
                    if (consumed != image)
                    {
                        consumed.enabled = false;
                    }
                }

                image.sprite = render;
                image.color = Color.white;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.enabled = true;

                Place(compass, target, new CompassNode(shape.Path, shape.Offset.x, shape.Offset.y,
                    shape.Size.x, shape.Size.y, Facing.Compass));
                restored++;
            }

            return restored;
        }

        /// <summary>
        /// NE/SW/SE/NW live in a 45°-rotated frame whose own anchors are wrong, so
        /// their inherited placement is off. Pin them by their design offsets.
        /// </summary>
        private static int PlaceDiagonalLabels(RectTransform compass)
        {
            RectTransform group = FindByPath(compass, "Compass/Compass watchface/Watchface/labels/45°");
            if (group == null)
            {
                return 0;
            }

            var offsets = new (string Label, float X, float Y)[]
            {
                ("NE", 157.4f, 156.9f),
                ("SW", -157.4f, -157.8f),
                ("SE", 157.4f, -157.8f),
                ("NW", -157.4f, 156.9f)
            };

            int placed = 0;
            foreach ((string label, float x, float y) in offsets)
            {
                TMP_Text text = FindTextByExactValue(group, label);
                if (text == null || !(text.transform.parent is RectTransform frame))
                {
                    continue;
                }

                Place(compass, frame, new CompassNode(label, x, y, 46.05f, 46.05f));
                placed++;
            }

            return placed;
        }

        private static void Place(RectTransform compass, RectTransform target, CompassNode node)
        {
            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.sizeDelta = node.Size;

            if (node.Facing == Facing.Compass)
            {
                target.rotation = compass.rotation;
            }

            target.position = compass.TransformPoint(new Vector3(node.Offset.x, node.Offset.y, 0f));
        }

        private static RectTransform FindByPath(Transform root, string path)
        {
            Transform current = root;
            foreach (string step in path.Split('/'))
            {
                current = FindDirectChild(current, step);
                if (current == null)
                {
                    return null;
                }
            }

            return current as RectTransform;
        }

        private static Image FindImage(Transform root, string path)
        {
            RectTransform target = FindByPath(root, path);
            return target != null ? target.GetComponent<Image>() : null;
        }

        private static Sprite GetDotSprite()
        {
            if (_dotSprite != null)
            {
                return _dotSprite;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float alpha = Mathf.Clamp01(centre - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);
            _dotSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _dotSprite;
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
            // Heading and course inherited the rate-of-turn unit from the template,
            // and both read in degrees, which their values already carry.
            ClearInstrumentUnit(_conningCompass, "HDG");
            ClearInstrumentUnit(_conningCompass, "COG");

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

        /// <summary>
        /// Drops the unit suffix next to an instrument's label, for readings whose
        /// only unit is the degree sign already drawn beside the value.
        /// </summary>
        private static void ClearInstrumentUnit(Transform scope, string label)
        {
            TMP_Text labelText = FindTextByExactValue(scope, label);
            if (labelText == null || labelText.transform.parent == null)
            {
                return;
            }

            Transform labelUnit = labelText.transform.parent.parent;
            if (labelUnit == null || labelUnit.name != LabelUnitGroup)
            {
                return;
            }

            TMP_Text unit = FindDirectChild(labelUnit, "value-actual")?.GetComponent<TMP_Text>();
            if (unit != null)
            {
                unit.gameObject.SetActive(false);
            }
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
