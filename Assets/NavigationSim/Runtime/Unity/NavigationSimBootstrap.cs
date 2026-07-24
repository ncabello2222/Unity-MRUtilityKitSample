using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Creates the simulation runner, config panel, conning display and VR
    /// pointer without any scene wiring, so the prototype scene keeps working untouched.
    /// </summary>
    public static class NavigationSimBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
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

            if (runner.GetComponent<NorthStarOceanAdapter>() == null)
            {
                runner.gameObject.AddComponent<NorthStarOceanAdapter>();
            }

            var systems = GameObject.Find("ShipBridgeSystems");
            if (systems == null)
            {
                systems = runner.gameObject;
            }

            if (Object.FindAnyObjectByType<ShipBridgePrototype.VesselHullPresenter>() == null)
            {
                systems.AddComponent<ShipBridgePrototype.VesselHullPresenter>();
            }

            if (Object.FindAnyObjectByType<ShipBridgePrototype.ShipControlState>() == null)
            {
                systems.AddComponent<ShipBridgePrototype.ShipControlState>();
            }

            if (Object.FindAnyObjectByType<ShipBridgePrototype.BridgeInspectorControls>() == null)
            {
                var go = new GameObject("BridgeInspectorControls");
                go.transform.SetParent(systems.transform, false);
                go.AddComponent<ShipBridgePrototype.BridgeInspectorControls>();
            }

            if (Object.FindAnyObjectByType<UI.SimulationConfigInspector>() == null)
            {
                var go = new GameObject("SimulationConfigInspector");
                go.transform.SetParent(systems.transform, false);
                go.AddComponent<UI.SimulationConfigInspector>();
            }
        }
    }
}
