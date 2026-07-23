#if UNITY_EDITOR
using System.IO;
using ShipBridgePrototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WaveHarmonic.Crest;

namespace ShipBridgePrototype.Editor
{
    /// <summary>
    /// Builds a reusable Crest water prefab and wires <see cref="CrestOceanBootstrap"/>
    /// in BridgeRoomPrototype. Crest lives under ExteriorWorld at runtime — not inside
    /// scenario prefabs (those are swapped).
    /// </summary>
    public static class CrestOceanSetup
    {
        private const string PrefabsFolder = "Assets/ShipBridgePrototype/Prefabs";
        private const string CrestPrefabPath = PrefabsFolder + "/CrestOcean.prefab";
        private const string WaterMaterialPath = "Packages/com.waveharmonic.crest/Runtime/Materials/Water.mat";
        private const string VolumeMaterialPath = "Packages/com.waveharmonic.crest/Runtime/Materials/Water Volume.mat";
        private const string CalmSpectrumPath = "Packages/com.waveharmonic.crest/Runtime/Data/WaveSpectra/WavesCalm.asset";
        private const string ScenePath = "Assets/ShipBridgePrototype/BridgeRoomPrototype.unity";
        private const string CoastalPrefabPath = PrefabsFolder + "/Scenarios/Scenario_CoastalMountains.prefab";

        [MenuItem("Ship Bridge/Setup Crest Ocean")]
        public static void SetupMenu()
        {
            var msg = Setup(showDialog: true);
            Debug.Log(msg);
        }

        /// <summary>Batchmode entry: Unity -executeMethod ShipBridgePrototype.Editor.CrestOceanSetup.SetupBatch</summary>
        public static void SetupBatch()
        {
            Debug.Log(Setup(showDialog: false));
        }

        public static string Setup(bool showDialog = false)
        {
            EnsureFolder(PrefabsFolder);

            var prefab = BuildCrestPrefab();
            StripBrokenOceanFromCoastalScenario();
            WireBootstrapInScene(prefab);

            var msg =
                $"Crest ocean ready.\nPrefab: {CrestPrefabPath}\n" +
                "Placement: scene root + sync translation to ExteriorWorld (Crest forbids parent rotation).\n" +
                "Waves/math link: deferred — calm spectrum only for a visible surface.";

            if (showDialog)
            {
                EditorUtility.DisplayDialog("Crest Ocean", msg, "OK");
            }

            return msg.Replace('\n', ' ');
        }

        private static GameObject BuildCrestPrefab()
        {
            var root = new GameObject("CrestOcean");
            try
            {
                var water = root.AddComponent<WaterRenderer>();

                var waterMat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
                var volumeMat = AssetDatabase.LoadAssetAtPath<Material>(VolumeMaterialPath);
                if (waterMat != null)
                {
                    water.Surface.Material = waterMat;
                }

                if (volumeMat != null)
                {
                    water.Underwater.Material = volumeMat;
                }

                // Minimal waves so the surface reads as ocean; presets/math later.
                var wavesGo = new GameObject("Waves");
                wavesGo.transform.SetParent(root.transform, false);
                var fft = wavesGo.AddComponent<ShapeFFT>();
                var calm = AssetDatabase.LoadAssetAtPath<WaveSpectrum>(CalmSpectrumPath);
                if (calm != null)
                {
                    fft.Spectrum = calm;
                }

                root.transform.localPosition = new Vector3(0f, -16.5f, 0f);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, CrestPrefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void WireBootstrapInScene(GameObject crestPrefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var systems = GameObject.Find("ShipBridgeSystems");
            if (systems == null)
            {
                systems = new GameObject("ShipBridgeSystems");
                Undo.RegisterCreatedObjectUndo(systems, "ShipBridgeSystems");
            }

            var bootstrap = systems.GetComponent<CrestOceanBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = Undo.AddComponent<CrestOceanBootstrap>(systems);
            }

            var waterMat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            var volumeMat = AssetDatabase.LoadAssetAtPath<Material>(VolumeMaterialPath);
            var calm = AssetDatabase.LoadAssetAtPath<WaveSpectrum>(CalmSpectrumPath);

            var so = new SerializedObject(bootstrap);
            so.FindProperty("crestWaterPrefab").objectReferenceValue = crestPrefab;
            so.FindProperty("waterMaterial").objectReferenceValue = waterMat;
            so.FindProperty("underwaterMaterial").objectReferenceValue = volumeMat;
            so.FindProperty("waveSpectrum").objectReferenceValue = calm;
            so.FindProperty("seaLevelLocalY").floatValue = -16.5f;
            so.FindProperty("attachWhenExteriorReady").boolValue = true;
            so.FindProperty("addCalmWaves").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(systems);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void StripBrokenOceanFromCoastalScenario()
        {
            var coastal = AssetDatabase.LoadAssetAtPath<GameObject>(CoastalPrefabPath);
            if (coastal == null)
            {
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(CoastalPrefabPath);
            try
            {
                var removed = 0;
                for (var i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i).gameObject;
                    var isLegacyWater = child.name is "Ocean" or "Water" or "CrestOcean" ||
                                        child.GetComponentInChildren<WaterRenderer>(true) != null;
                    if (!isLegacyWater)
                    {
                        continue;
                    }

                    Object.DestroyImmediate(child);
                    removed++;
                }

                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, CoastalPrefabPath);
                    Debug.Log($"[CrestOceanSetup] Removed {removed} legacy water object(s) from coastal scenario.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var name = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
