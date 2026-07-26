using System.IO;
using NavigationSim.UnityLayer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavigationSim.EditorTools
{
    /// <summary>
    /// Bakes the FCU conning import into the panel the game loads. The geometry
    /// repair and the strip of the converter's bookkeeping run here, once, so
    /// opening the panel on the headset only clones and binds.
    /// </summary>
    internal static class OpenBridgeConningBaker
    {
        private const string ImportRootName = "OpenBridge_FCU";
        private const string CaseName = "cases_conning_5.0";
        private const string PrefabPath =
            "Assets/NavigationSim/Resources/" + OpenBridgeConningBinder.RuntimePrefabResource + ".prefab";

        /// <summary>
        /// The Figma import lives outside the game scene so that its ~980 objects
        /// are not loaded on the headset. It is pulled in only to bake.
        /// </summary>
        private const string ImportScenePath = "Assets/NavigationSim/FigmaImport/OpenBridgeFigmaImport.unity";

        [MenuItem("Tools/NavigationSim/Bake OpenBridge Conning Panel")]
        private static void Bake()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[OpenBridge] Leave play mode before baking the conning panel.");
                return;
            }

            Scene opened = default;
            GameObject importedCase = FindImportedCase();
            if (importedCase == null && File.Exists(ImportScenePath))
            {
                opened = EditorSceneManager.OpenScene(ImportScenePath, OpenSceneMode.Additive);
                importedCase = FindImportedCase();
            }

            GameObject canvas = null;
            try
            {
                if (importedCase == null)
                {
                    Debug.LogWarning($"[OpenBridge] No {ImportRootName}/.../{CaseName} in {ImportScenePath} or the open scenes, nothing to bake.");
                    return;
                }

                canvas = OpenBridgeConningBinder.CreateWorldCanvas("ConningBakeCanvas", null);
                GameObject panel = Object.Instantiate(importedCase, canvas.transform, false);
                panel.name = CaseName;
                panel.SetActive(true);
                OpenBridgeConningBinder.FitToCanvas(panel.transform as RectTransform);

                // Let the layout components the import still relies on settle
                // before the repair measures anything against them.
                Canvas.ForceUpdateCanvases();

                int repaired = OpenBridgeConningBinder.RepairImportArtifacts(panel.transform);
                int stripped = OpenBridgeConningBinder.StripImportComponents(panel.transform);

                PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath);
                AssetDatabase.Refresh();

                Debug.Log($"[OpenBridge] Baked {PrefabPath}: {repaired} nodes repaired, {stripped} import components removed.");
            }
            finally
            {
                if (canvas != null)
                {
                    Object.DestroyImmediate(canvas);
                }

                if (opened.IsValid())
                {
                    EditorSceneManager.CloseScene(opened, true);
                }
            }
        }

        private static GameObject FindImportedCase()
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.name != ImportRootName || !candidate.scene.IsValid())
                {
                    continue;
                }

                foreach (Transform child in candidate.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == CaseName)
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }
    }
}
