using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Creates the simulation runner, config panel, conning display and VR
    /// pointer without any scene wiring, so the prototype scene keeps working untouched.
    /// Only runs on bridge/sim scenes — never on Crest samples or unrelated scenes.
    /// </summary>
    public static class NavigationSimBootstrap
    {
        static readonly string[] AllowedSceneNames =
        {
            "BridgeRoomPrototype",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryEnsure(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryEnsure(scene);
        }

        private static bool IsAllowedScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var name = scene.name;
            for (int i = 0; i < AllowedSceneNames.Length; i++)
            {
                if (string.Equals(name, AllowedSceneNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryEnsure(Scene scene)
        {
            if (!IsAllowedScene(scene))
            {
                return;
            }

            Ensure();
        }

        private static void Ensure()
        {
            NavigationSimRunner runner = NavigationSimRunner.EnsureInstance();
            if (runner.GetComponent<UI.SimulationConfigPanel>() == null)
            {
                runner.gameObject.AddComponent<UI.SimulationConfigPanel>();
            }

            if (runner.GetComponent<UI.BridgeConningDisplay>() == null)
            {
                runner.gameObject.AddComponent<UI.BridgeConningDisplay>();
            }

            if (runner.GetComponent<UI.VrUiPointer>() == null)
            {
                runner.gameObject.AddComponent<UI.VrUiPointer>();
            }

            if (UnityEngine.Object.FindAnyObjectByType<ShipBridgePrototype.VesselHullPresenter>() == null)
            {
                var systems = GameObject.Find("ShipBridgeSystems");
                if (systems == null)
                {
                    systems = runner.gameObject;
                }

                systems.AddComponent<ShipBridgePrototype.VesselHullPresenter>();
            }
        }
    }
}
