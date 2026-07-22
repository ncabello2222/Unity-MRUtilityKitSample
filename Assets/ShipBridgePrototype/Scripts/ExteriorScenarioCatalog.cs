using System;
using UnityEngine;

namespace ShipBridgePrototype
{
    [CreateAssetMenu(
        fileName = "ExteriorScenarioCatalog",
        menuName = "Ship Bridge/Exterior Scenario Catalog")]
    public class ExteriorScenarioCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Stable id used by LoadById.")]
            public string id = "scenario";

            public string displayName = "Scenario";

            [Tooltip("Prefab root instantiated under ExteriorWorld.")]
            public GameObject prefab;
        }

        [SerializeField] private Entry[] scenarios = Array.Empty<Entry>();

        public int Count => scenarios != null ? scenarios.Length : 0;

        public Entry Get(int index)
        {
            if (scenarios == null || index < 0 || index >= scenarios.Length)
            {
                return null;
            }

            return scenarios[index];
        }

        public Entry GetById(string id)
        {
            if (scenarios == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < scenarios.Length; i++)
            {
                if (scenarios[i] != null &&
                    string.Equals(scenarios[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return scenarios[i];
                }
            }

            return null;
        }

        public int IndexOfId(string id)
        {
            if (scenarios == null || string.IsNullOrEmpty(id))
            {
                return -1;
            }

            for (var i = 0; i < scenarios.Length; i++)
            {
                if (scenarios[i] != null &&
                    string.Equals(scenarios[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
