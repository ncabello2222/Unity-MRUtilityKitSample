#if UNITY_EDITOR
using System;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using ShipBridgePrototype;
using UnityEditor;
using UnityEngine;

namespace ShipBridgePrototype.Editor
{
    /// <summary>
    /// Builds ShipBridgePrototype/Prefabs/BridgeConsole.prefab from the FBX under
    /// NavigationSim/Resources/ship_bridge_vr, with hierarchy grouping and ISDK grab
    /// on the meson height handle.
    /// </summary>
    public static class BridgeConsolePrefabBuilder
    {
        private const string FbxPath = "Assets/NavigationSim/Resources/ship_bridge_vr/Console_Bridge.fbx";
        private const string PrefabPath = "Assets/ShipBridgePrototype/Prefabs/BridgeConsole.prefab";
        private const string ResourcesPrefabPath =
            "Assets/NavigationSim/Resources/ship_bridge_vr/BridgeConsole.prefab";

        [MenuItem("Ship Bridge/Build Bridge Console Prefab")]
        public static void BuildMenu()
        {
            var result = Build();
            Debug.Log(result);
            EditorUtility.DisplayDialog("Bridge Console", result, "OK");
        }

        public static string Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (model == null)
            {
                return $"FBX not found at {FbxPath}";
            }

            var instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(model);
            }

            instance.name = "BridgeConsole";

            try
            {
                var controller = instance.GetComponent<BridgeConsoleController>() ??
                                 instance.AddComponent<BridgeConsoleController>();
                controller.EnsureHierarchy();
                controller.CaptureStartPose();

                if (controller.HeightHandle != null)
                {
                    EnsureRigidbody(controller.HeightHandle.gameObject);
                    // Prefer the Interaction SDK GrabWizard when available so the
                    // interactable matches NavigationPanel wiring (templates + rig interactors).
                    RunGrabWizard(controller.HeightHandle.gameObject);
                }

                var height = instance.GetComponent<BridgeConsoleHeightHandle>() ??
                             instance.AddComponent<BridgeConsoleHeightHandle>();
                height.EnsureInteractable();
                RetargetGrabbable(controller);

                EnsureFolder("Assets/ShipBridgePrototype/Prefabs");
                EnsureFolder("Assets/NavigationSim/Resources/ship_bridge_vr");

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(instance, ResourcesPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return $"Built:\n- {PrefabPath}\n- {ResourcesPrefabPath}\n" +
                       $"Length axis: {controller.ResolvedLengthAxis}";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RetargetGrabbable(BridgeConsoleController controller)
        {
            var handle = controller.HeightHandle;
            if (handle == null)
            {
                return;
            }

            var grabbable = handle.GetComponent<Grabbable>() ??
                            handle.GetComponentInChildren<Grabbable>();
            if (grabbable == null)
            {
                return;
            }

            // Keep the handle as the grabbable host; height is driven by
            // BridgeConsoleHeightHandle (no-op transformer).
            grabbable.InjectOptionalTargetTransform(handle);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.MaxGrabPoints = 1;

            var noop = handle.GetComponent<BridgeConsoleNoOpTransformer>() ??
                       handle.gameObject.AddComponent<BridgeConsoleNoOpTransformer>();
            grabbable.InjectOptionalOneGrabTransformer(noop);

            var so = new SerializedObject(grabbable);
            var targetProp = so.FindProperty("_targetTransform");
            if (targetProp != null)
            {
                targetProp.objectReferenceValue = handle;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var oneGrab = so.FindProperty("_oneGrabTransformer");
            if (oneGrab != null)
            {
                oneGrab.objectReferenceValue = noop;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void RunGrabWizard(GameObject target)
        {
            if (target.GetComponentInChildren<GrabInteractable>(true) != null &&
                target.GetComponentInChildren<HandGrabInteractable>(true) != null)
            {
                return;
            }

            EnsureRigidbody(target);

            var wizardBase = Type.GetType(
                "Oculus.Interaction.Editor.QuickActions.QuickActionsWizard, Oculus.Interaction.Editor");
            var grabWizard = Type.GetType(
                "Oculus.Interaction.Editor.QuickActions.GrabWizard, Oculus.Interaction.Editor");
            if (wizardBase == null || grabWizard == null)
            {
                Debug.LogWarning(
                    "[BridgeConsolePrefabBuilder] GrabWizard not found — runtime EnsureInteractable will be used.");
                return;
            }

            var create = wizardBase.GetMethod(
                "CreateWithDefaults",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (create == null)
            {
                return;
            }

            create.MakeGenericMethod(grabWizard).Invoke(null, new object[] { target, false, null });

            var handGrab = target.GetComponentInChildren<HandGrabInteractable>(true);
            if (handGrab != null && handGrab.GetComponent<MoveFromTargetProvider>() == null)
            {
                handGrab.gameObject.AddComponent<MoveFromTargetProvider>();
            }
        }

        private static void EnsureRigidbody(GameObject target)
        {
            var body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = target.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
#endif
