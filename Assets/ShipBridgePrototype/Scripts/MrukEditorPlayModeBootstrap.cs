using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// In the Unity Editor, force MRUK to load a Prefab room so Play Mode does not stall on
    /// Device Scene Capture (unsupported) and never reach Prefab fallback.
    /// On device builds this component is a no-op — scene DataSource stays as authored.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class MrukEditorPlayModeBootstrap : MonoBehaviour
    {
        [SerializeField] private bool forcePrefabInEditor = true;

        private void Awake()
        {
#if UNITY_EDITOR
            if (!forcePrefabInEditor)
            {
                return;
            }

            EnsurePersistentDataFolder();

            var mruk = GetComponent<MRUK>();
            if (mruk == null)
            {
                mruk = FindAnyObjectByType<MRUK>();
            }

            if (mruk == null || mruk.SceneSettings == null)
            {
                Debug.LogWarning("[MrukEditorPlayModeBootstrap] MRUK not found; cannot force Prefab data source.");
                return;
            }

            if (mruk.SceneSettings.DataSource == MRUK.SceneDataSource.Prefab)
            {
                return;
            }

            Debug.Log("[MrukEditorPlayModeBootstrap] Editor Play Mode → MRUK DataSource = Prefab (device builds unchanged).");
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Prefab;
#else
            // Device / player: leave SceneSettings as configured in the scene.
#endif
        }

        private static void EnsurePersistentDataFolder()
        {
            try
            {
                var path = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(path) && !System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MrukEditorPlayModeBootstrap] Could not create persistentDataPath: " + ex.Message);
            }
        }
    }
}
