#if UNITY_EDITOR
using System;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using ShipBridgePrototype;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ShipBridgePrototype.Editor
{
    public static class NavigationControlsBuilder
    {
        private const string PrefabPath = "Assets/ShipBridgePrototype/Prefabs/NavigationPanel.prefab";

        [MenuItem("Ship Bridge/Rebuild Navigation Controls")]
        public static void RebuildMenu()
        {
            var result = Rebuild();
            Debug.Log(result);
            EditorUtility.DisplayDialog("Navigation Controls", result, "OK");
        }

        public static string Rebuild()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = root.GetComponent<NavigationPanel>();
                if (panel == null)
                {
                    return "NavigationPanel component missing.";
                }

                var soPanel = new SerializedObject(panel);
                var controlsAnchor = soPanel.FindProperty("controlsAnchor").objectReferenceValue as Transform;
                if (controlsAnchor == null)
                {
                    return "ControlsAnchor missing.";
                }

                // Clear previous instruments.
                for (var i = controlsAnchor.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(controlsAnchor.GetChild(i).gameObject);
                }

                // ShipControlState lives in the scene (ShipBridgeSystems), not on the panel prefab.
                var stateOnPanel = root.GetComponent<ShipControlState>();
                if (stateOnPanel != null)
                {
                    UnityEngine.Object.DestroyImmediate(stateOnPanel);
                }

                var brass = LoadOrCreateMat("Assets/ShipBridgePrototype/Materials/NavControlBrass.mat", new Color(0.69f, 0.55f, 0.34f));
                var dark = LoadOrCreateMat("Assets/ShipBridgePrototype/Materials/NavControlDark.mat", new Color(0.12f, 0.13f, 0.15f));
                var accent = LoadOrCreateMat("Assets/ShipBridgePrototype/Materials/NavControlAccent.mat", new Color(0.85f, 0.2f, 0.15f));
                var gauge = LoadOrCreateMat("Assets/ShipBridgePrototype/Materials/NavControlGauge.mat", new Color(0.9f, 0.9f, 0.85f));

                BuildRudderGauge(controlsAnchor, null, gauge, accent, dark);
                BuildSteeringWheel(controlsAnchor, null, brass, dark, accent);
                BuildTelegraph(controlsAnchor, null, brass, dark, accent);
                BuildBowThruster(controlsAnchor, null, brass, dark);

                FixPanelFrameGrab(root, soPanel);
                EnsurePanelProximityFade(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                EnlargeControllerGrabVolumes();
                return "Navigation controls rebuilt on prefab (labels enlarged, proximity fade wired).";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Ship Bridge/Fix Grab Interactions")]
        public static void FixGrabMenu()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = root.GetComponent<NavigationPanel>();
                var soPanel = new SerializedObject(panel);
                FixPanelFrameGrab(root, soPanel);

                // Near grab only for instruments (no distance grab).
                var rotateRoots = new[]
                {
                    root.transform.Find("ControlsAnchor/SteeringWheel/Wheel"),
                    root.transform.Find("ControlsAnchor/EngineTelegraph/LeverPivot"),
                    root.transform.Find("ControlsAnchor/BowThruster/LeverPivot"),
                };
                for (var i = 0; i < rotateRoots.Length; i++)
                {
                    if (rotateRoots[i] != null)
                    {
                        RemoveDistanceGrab(rotateRoots[i].gameObject);
                        EnlargeChildGrabColliders(rotateRoots[i]);
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            EnlargeControllerGrabVolumes();
            EditorUtility.DisplayDialog("Grab Fix", "Panel/control near-grab repaired (no distance grab on instruments).", "OK");
        }

        private static Material LoadOrCreateMat(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                mat.color = color;
                return mat;
            }

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void BuildRudderGauge(Transform parent, ShipControlState state, Material faceMat, Material needleMat, Material darkMat)
        {
            var root = new GameObject("RudderAngleIndicator");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(-0.22f, 0.16f, -0.02f);
            root.transform.localRotation = Quaternion.identity;

            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "GaugeFace";
            face.transform.SetParent(root.transform, false);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(0.28f, 0.005f, 0.28f);
            UnityEngine.Object.DestroyImmediate(face.GetComponent<Collider>());
            face.GetComponent<MeshRenderer>().sharedMaterial = faceMat;

            // Mask lower half with a cover so it reads as a semicircle.
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = "LowerCover";
            cover.transform.SetParent(root.transform, false);
            cover.transform.localPosition = new Vector3(0f, -0.07f, -0.001f);
            cover.transform.localScale = new Vector3(0.3f, 0.14f, 0.01f);
            UnityEngine.Object.DestroyImmediate(cover.GetComponent<Collider>());
            cover.GetComponent<MeshRenderer>().sharedMaterial = darkMat;

            var ticks = new GameObject("Ticks");
            ticks.transform.SetParent(root.transform, false);
            float[] marks = { -35, -30, -20, -10, 0, 10, 20, 30, 35 };
            for (var i = 0; i < marks.Length; i++)
            {
                var mark = marks[i];
                // Map -35..35 to 180..0 degrees on upper semicircle.
                var deg = Mathf.Lerp(180f, 0f, Mathf.InverseLerp(-35f, 35f, mark));
                var rad = deg * Mathf.Deg2Rad;
                var tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tick.name = "Tick_" + mark;
                tick.transform.SetParent(ticks.transform, false);
                var r = 0.11f;
                tick.transform.localPosition = new Vector3(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r, -0.006f);
                tick.transform.localRotation = Quaternion.Euler(0f, 0f, deg - 90f);
                tick.transform.localScale = mark == 0f ? new Vector3(0.004f, 0.02f, 0.004f) : new Vector3(0.003f, 0.014f, 0.003f);
                UnityEngine.Object.DestroyImmediate(tick.GetComponent<Collider>());
                tick.GetComponent<MeshRenderer>().sharedMaterial = darkMat;
            }

            CreateLabel(root.transform, "PortLabel", "PORT", new Vector3(-0.11f, 0.02f, -0.01f), 2.2f, TextAlignmentOptions.Center);
            CreateLabel(root.transform, "StbdLabel", "STARBOARD", new Vector3(0.11f, 0.02f, -0.01f), 2.2f, TextAlignmentOptions.Center);
            CreateLabel(root.transform, "Title", "RUDDER ANGLE", new Vector3(0f, 0.14f, -0.01f), 2.6f, TextAlignmentOptions.Center);

            var needle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            needle.name = "Needle";
            needle.transform.SetParent(root.transform, false);
            needle.transform.localPosition = new Vector3(0f, 0.05f, -0.008f);
            needle.transform.localScale = new Vector3(0.004f, 0.1f, 0.004f);
            UnityEngine.Object.DestroyImmediate(needle.GetComponent<Collider>());
            needle.GetComponent<MeshRenderer>().sharedMaterial = needleMat;

            var value = CreateLabel(root.transform, "Value", "0.0° AMID", new Vector3(0f, -0.02f, -0.01f), 3f, TextAlignmentOptions.Center);

            var indicator = root.AddComponent<RudderAngleIndicator>();
            indicator.EditorBind(state, needle.transform, value);
        }

        private static void BuildSteeringWheel(Transform parent, ShipControlState state, Material brass, Material dark, Material accent)
        {
            var mount = new GameObject("SteeringWheel");
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = new Vector3(-0.25f, -0.05f, -0.05f);

            var hubBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hubBase.name = "Pedestal";
            hubBase.transform.SetParent(mount.transform, false);
            hubBase.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            hubBase.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hubBase.transform.localScale = new Vector3(0.05f, 0.03f, 0.05f);
            UnityEngine.Object.DestroyImmediate(hubBase.GetComponent<Collider>());
            hubBase.GetComponent<MeshRenderer>().sharedMaterial = dark;

            var wheel = new GameObject("Wheel");
            wheel.transform.SetParent(mount.transform, false);
            wheel.transform.localPosition = Vector3.zero;

            const float diameter = 0.38f;
            const float radius = diameter * 0.5f;
            const int spokes = 8;

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Rim";
            rim.transform.SetParent(wheel.transform, false);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rim.transform.localScale = new Vector3(diameter, 0.012f, diameter);
            rim.GetComponent<MeshRenderer>().sharedMaterial = brass;
            // Hollow look: keep collider on rim for grabbing.
            // Remove solid collider from non-uniformly scaled rim; GrabVolume handles grabs.
            var rimCol = rim.GetComponent<Collider>();
            if (rimCol != null)
            {
                UnityEngine.Object.DestroyImmediate(rimCol);
            }

            for (var i = 0; i < spokes; i++)
            {
                var angle = i * (360f / spokes);
                var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spoke.name = "Spoke_" + i;
                spoke.transform.SetParent(wheel.transform, false);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                spoke.transform.localPosition = Vector3.zero;
                spoke.transform.localScale = new Vector3(0.012f, radius * 1.7f, 0.012f);
                UnityEngine.Object.DestroyImmediate(spoke.GetComponent<Collider>());
                spoke.GetComponent<MeshRenderer>().sharedMaterial = brass;
            }

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            hub.transform.SetParent(wheel.transform, false);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hub.transform.localScale = new Vector3(0.06f, 0.02f, 0.06f);
            UnityEngine.Object.DestroyImmediate(hub.GetComponent<Collider>());
            hub.GetComponent<MeshRenderer>().sharedMaterial = dark;

            var topMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topMark.name = "TopMark";
            topMark.transform.SetParent(wheel.transform, false);
            topMark.transform.localPosition = new Vector3(0f, radius * 0.92f, -0.01f);
            topMark.transform.localScale = new Vector3(0.03f, 0.02f, 0.015f);
            UnityEngine.Object.DestroyImmediate(topMark.GetComponent<Collider>());
            topMark.GetComponent<MeshRenderer>().sharedMaterial = accent;

            CreateLabel(mount.transform, "Label", "HELM", new Vector3(0f, -0.22f, 0f), 3f, TextAlignmentOptions.Center);

            // Uniform grab volume (rim mesh scale is non-uniform and breaks sphere colliders).
            var grabVolume = new GameObject("GrabVolume");
            grabVolume.transform.SetParent(wheel.transform, false);
            grabVolume.transform.localScale = Vector3.one;
            var grabSphere = grabVolume.AddComponent<SphereCollider>();
            grabSphere.radius = 0.22f;

            SetupRotateGrab(wheel, OneGrabRotateTransformer.Axis.Forward, -400f, 400f);

            var control = mount.AddComponent<SteeringWheelControl>();
            control.EditorBind(state, wheel.transform);
        }

        private static void BuildTelegraph(Transform parent, ShipControlState state, Material brass, Material dark, Material accent)
        {
            var root = new GameObject("EngineTelegraph");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0.12f, -0.02f, -0.04f);

            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            basePlate.transform.SetParent(root.transform, false);
            basePlate.transform.localPosition = new Vector3(0f, -0.02f, 0.02f);
            basePlate.transform.localScale = new Vector3(0.16f, 0.04f, 0.12f);
            UnityEngine.Object.DestroyImmediate(basePlate.GetComponent<Collider>());
            basePlate.GetComponent<MeshRenderer>().sharedMaterial = dark;

            var sector = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sector.name = "Sector";
            sector.transform.SetParent(root.transform, false);
            sector.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            sector.transform.localScale = new Vector3(0.14f, 0.01f, 0.14f);
            UnityEngine.Object.DestroyImmediate(sector.GetComponent<Collider>());
            sector.GetComponent<MeshRenderer>().sharedMaterial = brass;

            var pivot = new GameObject("LeverPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = Vector3.zero;

            // STOP / zero: lever sticks out toward panel front (-Z), perpendicular to the face.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            shaft.transform.SetParent(pivot.transform, false);
            shaft.transform.localPosition = new Vector3(0f, 0f, -0.12f);
            shaft.transform.localScale = new Vector3(0.02f, 0.02f, 0.24f);
            shaft.GetComponent<MeshRenderer>().sharedMaterial = brass;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grip.name = "Grip";
            grip.transform.SetParent(pivot.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            grip.transform.localScale = Vector3.one * 0.05f;
            grip.GetComponent<MeshRenderer>().sharedMaterial = accent;

            var needle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            needle.name = "OrderNeedle";
            needle.transform.SetParent(root.transform, false);
            needle.transform.localPosition = new Vector3(0.06f, 0f, -0.04f);
            needle.transform.localScale = new Vector3(0.008f, 0.008f, 0.08f);
            UnityEngine.Object.DestroyImmediate(needle.GetComponent<Collider>());
            needle.GetComponent<MeshRenderer>().sharedMaterial = accent;

            var label = CreateLabel(root.transform, "OrderLabel", "STOP", new Vector3(0f, -0.12f, -0.02f), 2.8f, TextAlignmentOptions.Center);
            CreateLabel(root.transform, "Title", "ENGINE TELEGRAPH", new Vector3(0f, 0.2f, -0.02f), 2.4f, TextAlignmentOptions.Center);

            SetupRotateGrab(pivot, OneGrabRotateTransformer.Axis.Right, -90f, 90f);

            var grab = pivot.GetComponent<Grabbable>();
            var control = root.AddComponent<EngineTelegraphControl>();
            control.EditorBind(state, pivot.transform, needle.transform, label, grab);
        }

        private static void BuildBowThruster(Transform parent, ShipControlState state, Material brass, Material dark)
        {
            var root = new GameObject("BowThruster");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0.38f, -0.08f, -0.04f);

            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            basePlate.transform.SetParent(root.transform, false);
            basePlate.transform.localScale = new Vector3(0.14f, 0.03f, 0.08f);
            UnityEngine.Object.DestroyImmediate(basePlate.GetComponent<Collider>());
            basePlate.GetComponent<MeshRenderer>().sharedMaterial = dark;

            var pivot = new GameObject("LeverPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.02f, 0f);

            // Zero / spring-return: lever sticks out toward panel front (-Z), perpendicular to the face.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            shaft.transform.SetParent(pivot.transform, false);
            shaft.transform.localPosition = new Vector3(0f, 0.03f, -0.07f);
            shaft.transform.localScale = new Vector3(0.018f, 0.018f, 0.14f);
            shaft.GetComponent<MeshRenderer>().sharedMaterial = brass;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grip.name = "Grip";
            grip.transform.SetParent(pivot.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0.03f, -0.14f);
            grip.transform.localScale = Vector3.one * 0.04f;
            grip.GetComponent<MeshRenderer>().sharedMaterial = brass;

            var label = CreateLabel(root.transform, "Value", "BOW THRUSTER\n0", new Vector3(0f, -0.08f, -0.02f), 2.4f, TextAlignmentOptions.Center);
            CreateLabel(root.transform, "Port", "PORT", new Vector3(-0.08f, 0.06f, -0.02f), 2f, TextAlignmentOptions.Center);
            CreateLabel(root.transform, "Stbd", "STBD", new Vector3(0.08f, 0.06f, -0.02f), 2f, TextAlignmentOptions.Center);

            // ±90° around Up: swing port/stbd; zero stays perpendicular to the panel face.
            SetupRotateGrab(pivot, OneGrabRotateTransformer.Axis.Up, -90f, 90f);

            var grab = pivot.GetComponent<Grabbable>();
            var control = root.AddComponent<BowThrusterControl>();
            control.EditorBind(state, pivot.transform, label, grab);
        }

        private static TextMeshPro CreateLabel(Transform parent, string name, string text, Vector3 localPos, float fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            // scale 0.01 + fontSize~24-36 ≈ 2–3 cm glyphs, readable at ~0.9 m without leaning in
            go.transform.localScale = Vector3.one * 0.01f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = Mathf.Max(18f, fontSize * 12f);
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(80f, 24f);
            return tmp;
        }

        private static void EnsurePanelProximityFade(GameObject panelRoot)
        {
            var surface = panelRoot.transform.Find("FrameGrab/Surface")
                          ?? panelRoot.transform.Find("Frame/Surface")
                          ?? panelRoot.transform.Find("Surface");
            if (surface == null)
            {
                Debug.LogWarning("[NavigationControlsBuilder] Surface not found; proximity fade skipped.");
                return;
            }

            var fade = panelRoot.GetComponent<PanelProximityFade>();
            if (fade == null)
            {
                fade = panelRoot.AddComponent<PanelProximityFade>();
            }

            var surfaceRenderer = surface.GetComponent<Renderer>();
            if (surfaceRenderer == null)
            {
                Debug.LogWarning("[NavigationControlsBuilder] Surface renderer missing; proximity fade skipped.");
                return;
            }

            fade.EditorBind(surfaceRenderer);
        }

        private static void FixPanelFrameGrab(GameObject panelRoot, SerializedObject soPanel)
        {
            // Remove broken root-level grab that collected every child collider (wheel/levers).
            StripGrabComponents(panelRoot, destroyIsdkChildren: true);

            var frameRoot = soPanel.FindProperty("frameRoot").objectReferenceValue as Transform;
            if (frameRoot == null)
            {
                frameRoot = panelRoot.transform.Find("Frame");
            }

            if (frameRoot == null)
            {
                Debug.LogError("[NavigationControlsBuilder] Frame root missing; cannot fix panel grab.");
                return;
            }

            var frameGrab = panelRoot.transform.Find("FrameGrab");
            if (frameGrab == null)
            {
                var go = new GameObject("FrameGrab");
                go.transform.SetParent(panelRoot.transform, false);
                frameGrab = go.transform;
            }

            // Re-parent frame pieces under FrameGrab so only those colliders belong to its Rigidbody.
            while (frameRoot.childCount > 0)
            {
                frameRoot.GetChild(0).SetParent(frameGrab, true);
            }

            // Keep empty Frame transform as marker for NavigationPanel.frameRoot.
            soPanel.FindProperty("frameRoot").objectReferenceValue = frameGrab;
            soPanel.ApplyModifiedPropertiesWithoutUndo();

            // Thicker frame colliders for easier proximity grab.
            EnlargeChildGrabColliders(frameGrab);

            SetupFreeGrab(frameGrab.gameObject, panelRoot.transform);
            RemoveDistanceGrab(frameGrab.gameObject);
        }

        private static void SetupRotateGrab(GameObject target, OneGrabRotateTransformer.Axis axis, float minAngle, float maxAngle)
        {
            EnsureRigidbody(target);
            RunGrabWizard(target);
            RemoveDistanceGrab(target);
            EnlargeChildGrabColliders(target.transform);

            var grabbable = target.GetComponent<Grabbable>() ?? target.GetComponentInChildren<Grabbable>();
            var transformer = target.GetComponent<OneGrabRotateTransformer>();
            if (transformer == null)
            {
                transformer = target.AddComponent<OneGrabRotateTransformer>();
            }

            var tso = new SerializedObject(transformer);
            tso.FindProperty("_rotationAxis").enumValueIndex = (int)axis;
            var constraints = tso.FindProperty("_constraints");
            var min = constraints.FindPropertyRelative("MinAngle");
            var max = constraints.FindPropertyRelative("MaxAngle");
            min.FindPropertyRelative("Constrain").boolValue = true;
            min.FindPropertyRelative("Value").floatValue = minAngle;
            max.FindPropertyRelative("Constrain").boolValue = true;
            max.FindPropertyRelative("Value").floatValue = maxAngle;
            tso.ApplyModifiedPropertiesWithoutUndo();

            if (grabbable != null)
            {
                grabbable.InjectOptionalOneGrabTransformer(transformer);
                var gso = new SerializedObject(grabbable);
                var oneGrabProp = gso.FindProperty("_oneGrabTransformer");
                if (oneGrabProp != null)
                {
                    oneGrabProp.objectReferenceValue = transformer;
                    gso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EnsureMoveFromTarget(target);
        }

        private static void SetupFreeGrab(GameObject target, Transform moveTarget)
        {
            EnsureRigidbody(target);
            RunGrabWizard(target);

            var grabbable = target.GetComponent<Grabbable>() ?? target.GetComponentInChildren<Grabbable>();
            if (grabbable != null && moveTarget != null)
            {
                grabbable.InjectOptionalTargetTransform(moveTarget);
                var gso = new SerializedObject(grabbable);
                var targetProp = gso.FindProperty("_targetTransform");
                if (targetProp != null)
                {
                    targetProp.objectReferenceValue = moveTarget;
                    gso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EnsureMoveFromTarget(target);
        }

        private static void EnsureDistanceGrab(GameObject target)
        {
            if (target.GetComponentInChildren<DistanceGrabInteractable>(true) != null &&
                target.GetComponentInChildren<DistanceHandGrabInteractable>(true) != null)
            {
                return;
            }

            var wizardBase = Type.GetType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard, Oculus.Interaction.Editor");
            var distanceWizard = Type.GetType("Oculus.Interaction.Editor.QuickActions.DistanceGrabWizard, Oculus.Interaction.Editor");
            if (wizardBase == null || distanceWizard == null)
            {
                Debug.LogWarning("[NavigationControlsBuilder] DistanceGrabWizard not found.");
                return;
            }

            var create = wizardBase.GetMethod(
                "CreateWithDefaults",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            create.MakeGenericMethod(distanceWizard).Invoke(null, new object[] { target, false, null });
        }

        private static void RemoveDistanceGrab(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            foreach (var c in target.GetComponentsInChildren<DistanceGrabInteractable>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in target.GetComponentsInChildren<DistanceHandGrabInteractable>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            var transforms = target.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                if (transforms[i] != null &&
                    transforms[i].name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(transforms[i].gameObject);
                }
            }
        }

        private static void RunGrabWizard(GameObject target)
        {
            if (target.GetComponentInChildren<GrabInteractable>(true) != null &&
                target.GetComponentInChildren<HandGrabInteractable>(true) != null)
            {
                return;
            }

            var wizardBase = Type.GetType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard, Oculus.Interaction.Editor");
            var grabWizard = Type.GetType("Oculus.Interaction.Editor.QuickActions.GrabWizard, Oculus.Interaction.Editor");
            var create = wizardBase.GetMethod(
                "CreateWithDefaults",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            create.MakeGenericMethod(grabWizard).Invoke(null, new object[] { target, false, null });
        }

        private static void EnsureMoveFromTarget(GameObject target)
        {
            var handGrab = target.GetComponentInChildren<HandGrabInteractable>(true);
            if (handGrab == null)
            {
                return;
            }

            var move = handGrab.GetComponent<MoveFromTargetProvider>();
            if (move == null)
            {
                move = handGrab.gameObject.AddComponent<MoveFromTargetProvider>();
            }

            var hso = new SerializedObject(handGrab);
            var moveProp = hso.FindProperty("_movementProvider");
            if (moveProp != null)
            {
                moveProp.objectReferenceValue = move;
                hso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureRigidbody(GameObject target)
        {
            var rb = target.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = target.AddComponent<Rigidbody>();
            }

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private static void StripGrabComponents(GameObject host, bool destroyIsdkChildren)
        {
            foreach (var c in host.GetComponents<GrabInteractable>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in host.GetComponents<HandGrabInteractable>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in host.GetComponents<DistanceGrabInteractable>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in host.GetComponents<DistanceHandGrabInteractable>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in host.GetComponents<Grabbable>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            foreach (var c in host.GetComponents<OneGrabRotateTransformer>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            var rb = host.GetComponent<Rigidbody>();
            if (rb != null)
            {
                UnityEngine.Object.DestroyImmediate(rb);
            }

            if (!destroyIsdkChildren)
            {
                return;
            }

            for (var i = host.transform.childCount - 1; i >= 0; i--)
            {
                var child = host.transform.GetChild(i);
                if (child.name.StartsWith("ISDK_", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static void EnlargeChildGrabColliders(Transform root)
        {
            var spheres = root.GetComponentsInChildren<SphereCollider>(true);
            for (var i = 0; i < spheres.Length; i++)
            {
                spheres[i].radius = Mathf.Max(spheres[i].radius, 0.55f);
            }

            var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            for (var i = 0; i < boxes.Length; i++)
            {
                var size = boxes[i].size;
                // Pad thin frame edges so the 5cm controller grab sphere can hit them.
                size.x = Mathf.Max(size.x, 0.08f);
                size.y = Mathf.Max(size.y, 0.08f);
                size.z = Mathf.Max(size.z, 0.08f);
                boxes[i].size = size;
            }
        }

        private static void EnlargeControllerGrabVolumes()
        {
            var cameraRig = GameObject.Find("OVRCameraRig");
            if (cameraRig == null)
            {
                return;
            }

            var grabbers = cameraRig.GetComponentsInChildren<GrabInteractor>(true);
            for (var i = 0; i < grabbers.Length; i++)
            {
                var spheres = grabbers[i].GetComponentsInChildren<SphereCollider>(true);
                for (var s = 0; s < spheres.Length; s++)
                {
                    spheres[s].radius = Mathf.Max(spheres[s].radius, 0.12f);
                }
            }

            // Hand/controller-hand grab spheres.
            var handGrabbers = cameraRig.GetComponentsInChildren<HandGrabInteractor>(true);
            for (var i = 0; i < handGrabbers.Length; i++)
            {
                var spheres = handGrabbers[i].GetComponentsInChildren<SphereCollider>(true);
                for (var s = 0; s < spheres.Length; s++)
                {
                    if (spheres[s].gameObject.layer == 2)
                    {
                        continue; // ignore Ignore Raycast pinch helpers
                    }

                    spheres[s].radius = Mathf.Max(spheres[s].radius, 0.1f);
                }
            }

            EditorUtility.SetDirty(cameraRig);
        }
    }
}
#endif
