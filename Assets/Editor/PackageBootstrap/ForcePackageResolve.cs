using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace NavigationSim.EditorBootstrap
{
    /// <summary>
    /// Re-resolves Package Manager after a restart so embedded
    /// com.meta.utilities* packages are imported and compiled.
    /// </summary>
    [InitializeOnLoad]
    internal static class ForcePackageResolve
    {
        private const string PrefKey = "NavigationSim.MetaUtilitiesResolve.v3";

        static ForcePackageResolve()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(PrefKey, false))
                {
                    return;
                }

                EditorPrefs.SetBool(PrefKey, true);
                Debug.Log("[PackageBootstrap] Client.Resolve() for com.meta.utilities* packages.");
                Client.Resolve();
            };
        }

        [MenuItem("Assets/Navigation Sim/Resolve Meta Utilities Packages")]
        private static void ResolveFromMenu()
        {
            EditorPrefs.DeleteKey(PrefKey);
            Debug.Log("[PackageBootstrap] Manual Client.Resolve().");
            Client.Resolve();
        }
    }
}
