#if UNITY_EDITOR
using DA_Assets.Extensions;
using DA_Assets.FCU;
using DA_Assets.UCC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NavigationSim.EditorBootstrap
{
    /// <summary>
    /// Recreates the OpenBridge FCU canvas if missing, then configures import defaults.
    /// Menu: Tools/Ship Bridge/Ensure OpenBridge FCU
    /// </summary>
    internal static class EnsureOpenBridgeFcu
    {
        private const string ObjectName = "OpenBridge_FCU";
        private const string ProjectUrl =
            "https://www.figma.com/design/wp9RvYJDlsXg5e7aPQNI6o/OpenBridge-5.0-Cases-beta--Copy-?node-id=6514-23116";
        private const string SessionKey = "NavigationSim.EnsureOpenBridgeFcu.Pending";

        [MenuItem("Tools/Ship Bridge/Ensure OpenBridge FCU", false, 10)]
        private static void EnsureFromMenu()
        {
            Ensure(saveScene: true);
        }

        [InitializeOnLoadMethod]
        private static void AutoEnsureOnce()
        {
            if (!SessionState.GetBool(SessionKey, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (!SessionState.GetBool(SessionKey, false))
                    return;

                SessionState.SetBool(SessionKey, false);
                Ensure(saveScene: true);
            };
        }

        /// <summary>Call from bootstrap or external tools: queue ensure after domain reload.</summary>
        public static void QueueEnsureAfterReload()
        {
            SessionState.SetBool(SessionKey, true);
        }

        private static void Ensure(bool saveScene)
        {
            var existing = Object.FindObjectsByType<FigmaConverterUnity>(FindObjectsSortMode.None);
            FigmaConverterUnity fcu = null;

            foreach (var candidate in existing)
            {
                if (candidate != null && candidate.name == ObjectName)
                {
                    fcu = candidate;
                    break;
                }
            }

            if (fcu == null && existing.Length > 0)
                fcu = existing[0];

            if (fcu == null)
            {
                fcu = AssetTools.CreateConverterOnScene<FigmaConverterUnity>();
                if (fcu == null)
                {
                    Debug.LogError("[OpenBridge FCU] CreateConverterOnScene returned null.");
                    return;
                }

                fcu.gameObject.name = ObjectName;
                Debug.Log($"[OpenBridge FCU] Created '{ObjectName}' in active scene.");
            }
            else
            {
                fcu.gameObject.name = ObjectName;
                Debug.Log($"[OpenBridge FCU] Reusing existing instance as '{ObjectName}'.");
            }

            var main = fcu.Settings.MainSettings;
            main.UIFramework = UIFramework.UGUI;
            main.PositioningMode = PositioningMode.Absolute;
            main.PivotType = PivotType.MiddleCenter;
            main.UseDuplicateFinder = false;
            main.ProjectUrl = ProjectUrl;

            fcu.Settings.TextFontsSettings.TextComponent = TextComponent.TextMeshPro;

            EditorUtility.SetDirty(fcu);
            Selection.activeGameObject = fcu.gameObject;

            if (saveScene)
            {
                var scene = fcu.gameObject.scene;
                if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[OpenBridge FCU] Scene saved: {scene.path}");
                }
            }
        }
    }
}
#endif
