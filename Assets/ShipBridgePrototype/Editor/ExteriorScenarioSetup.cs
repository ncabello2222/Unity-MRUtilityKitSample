#if UNITY_EDITOR
using System.IO;
using ShipBridgePrototype;
using UnityEditor;
using UnityEngine;

namespace ShipBridgePrototype.Editor
{
    public static class ExteriorScenarioSetup
    {
        private const string PrefabsFolder = "Assets/ShipBridgePrototype/Prefabs/Scenarios";
        private const string CatalogPath = "Assets/ShipBridgePrototype/ExteriorScenarioCatalog.asset";
        private const string CoastalPrefabPath = PrefabsFolder + "/Scenario_CoastalMountains.prefab";

        [MenuItem("Ship Bridge/Setup Exterior Scenario Catalog")]
        public static void SetupCatalogMenu()
        {
            var result = SetupCatalog(showDialog: true);
            Debug.Log(result);
        }

        public static string SetupCatalog(bool showDialog = false)
        {
            EnsureFolder(PrefabsFolder);

            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/ShipBridgePrototype/Materials/BridgeExteriorWater.mat");
            var mountain = AssetDatabase.LoadAssetAtPath<Material>("Assets/ShipBridgePrototype/Materials/BridgeExteriorMountain.mat");

            var tempRoot = new GameObject("_ScenarioBakeRoot");
            try
            {
                var built = CoastalExteriorScenarioBuilder.Build(
                    tempRoot.transform,
                    water,
                    mountain,
                    terrainSize: 220f,
                    terrainHeight: 45f,
                    distanceFromBridge: 18f);

                PersistTerrainData(built.transform, PrefabsFolder + "/Scenario_CoastalMountains_TerrainData.asset");

                var prefab = PrefabUtility.SaveAsPrefabAsset(built, CoastalPrefabPath);

                var catalog = AssetDatabase.LoadAssetAtPath<ExteriorScenarioCatalog>(CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<ExteriorScenarioCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }

                var so = new SerializedObject(catalog);
                var scenarios = so.FindProperty("scenarios");
                scenarios.arraySize = Mathf.Max(1, scenarios.arraySize);
                var entry0 = scenarios.GetArrayElementAtIndex(0);
                entry0.FindPropertyRelative("id").stringValue = "coastal_mountains";
                entry0.FindPropertyRelative("displayName").stringValue = "Coastal Mountains";
                entry0.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);

                WireLoaderInScene(catalog);
                AssetDatabase.SaveAssets();

                var msg = $"Catalog ready. Default=coastal_mountains prefab={CoastalPrefabPath}";
                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Exterior Scenarios",
                        "Catalog ready.\nDefault: coastal_mountains\n\nAdd more entries in the catalog asset, then call:\nExteriorScenarioLoader.Instance.LoadById(\"...\")",
                        "OK");
                }

                return msg;
            }
            finally
            {
                Object.DestroyImmediate(tempRoot);
            }
        }

        private static void PersistTerrainData(Transform scenarioRoot, string terrainDataPath)
        {
            var terrains = scenarioRoot.GetComponentsInChildren<Terrain>(true);
            for (var i = 0; i < terrains.Length; i++)
            {
                var data = terrains[i].terrainData;
                if (data == null)
                {
                    continue;
                }

                var path = terrains.Length == 1 ? terrainDataPath : terrainDataPath.Replace(".asset", $"_{i}.asset");
                var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (existing != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }

                AssetDatabase.CreateAsset(Object.Instantiate(data), path);
                terrains[i].terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                var collider = terrains[i].GetComponent<TerrainCollider>();
                if (collider != null)
                {
                    collider.terrainData = terrains[i].terrainData;
                }
            }
        }

        private static void WireLoaderInScene(ExteriorScenarioCatalog catalog)
        {
            var systems = GameObject.Find("ShipBridgeSystems");
            if (systems == null)
            {
                systems = new GameObject("ShipBridgeSystems");
                Undo.RegisterCreatedObjectUndo(systems, "ShipBridgeSystems");
            }

            var loader = systems.GetComponent<ExteriorScenarioLoader>();
            if (loader == null)
            {
                loader = Undo.AddComponent<ExteriorScenarioLoader>(systems);
            }

            if (systems.GetComponent<ShipControlState>() == null)
            {
                Undo.AddComponent<ShipControlState>(systems);
            }

            if (systems.GetComponent<ExteriorWorldMotion>() == null)
            {
                Undo.AddComponent<ExteriorWorldMotion>(systems);
            }

            var so = new SerializedObject(loader);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("defaultScenarioIndex").intValue = 0;
            so.FindProperty("loadDefaultWhenExteriorReady").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(systems);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
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
