using NavigationSim.UnityLayer;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Loads exterior scenario prefabs under <see cref="ExteriorWorldRoot"/>.
    /// Default = catalog index 0. Other scenarios via LoadByIndex / LoadById.
    /// </summary>
    public class ExteriorScenarioLoader : MonoBehaviour
    {
        public static ExteriorScenarioLoader Instance { get; private set; }

        [Header("Catalog")]
        [SerializeField] private ExteriorScenarioCatalog catalog;
        [SerializeField] private int defaultScenarioIndex;

        [Header("Runtime refs (auto if null)")]
        [SerializeField] private ExteriorWorldRoot exteriorWorld;
        [SerializeField] private ExteriorWorldMotion worldMotion;

        [Header("Options")]
        [SerializeField] private bool loadDefaultWhenExteriorReady = true;
        [SerializeField] private bool resetShipMotionOnSwap = true;

        private GameObject _currentInstance;
        private int _currentIndex = -1;
        private bool _exteriorReady;

        public ExteriorScenarioCatalog Catalog => catalog;
        public int CurrentIndex => _currentIndex;
        public string CurrentId =>
            catalog != null && catalog.Get(_currentIndex) != null
                ? catalog.Get(_currentIndex).id
                : null;

        public GameObject CurrentInstance => _currentInstance;

        private void Awake()
        {
            Instance = this;
            ResolveRefs();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Called by BridgeRoomMapper after ExteriorWorld + pivot exist.</summary>
        public void NotifyExteriorReady(ExteriorWorldRoot root, Transform shipPivot, Vector3 shipForward)
        {
            exteriorWorld = root;
            ResolveRefs();

            if (worldMotion != null)
            {
                worldMotion.BindExterior(root, shipPivot, shipForward);
            }

            _exteriorReady = true;

            if (loadDefaultWhenExteriorReady)
            {
                LoadDefault();
            }

            OceanNextGenAdapter.EnsureInstance().NotifyExteriorChanged();
        }

        public bool LoadDefault()
        {
            return LoadByIndex(defaultScenarioIndex);
        }

        public bool LoadByIndex(int index)
        {
            ResolveRefs();
            if (!_exteriorReady && exteriorWorld == null)
            {
                Debug.LogWarning("[ExteriorScenarioLoader] ExteriorWorld not ready yet.");
                return false;
            }

            if (catalog == null || catalog.Count == 0)
            {
                Debug.LogWarning("[ExteriorScenarioLoader] Catalog empty.");
                return false;
            }

            index = Mathf.Clamp(index, 0, catalog.Count - 1);
            var entry = catalog.Get(index);
            if (entry == null || entry.prefab == null)
            {
                Debug.LogError($"[ExteriorScenarioLoader] Scenario index {index} has no prefab.");
                return false;
            }

            return SwapTo(entry.prefab, index);
        }

        public bool LoadById(string id)
        {
            if (catalog == null)
            {
                return false;
            }

            var index = catalog.IndexOfId(id);
            if (index < 0)
            {
                Debug.LogError($"[ExteriorScenarioLoader] Unknown scenario id '{id}'.");
                return false;
            }

            return LoadByIndex(index);
        }

        public void ClearCurrent()
        {
            if (_currentInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_currentInstance);
                }
                else
                {
                    DestroyImmediate(_currentInstance);
                }

                _currentInstance = null;
                _currentIndex = -1;
            }
        }

        private bool SwapTo(GameObject prefab, int index)
        {
            ClearCurrent();

            _currentInstance = Instantiate(prefab, exteriorWorld.Root);
            _currentInstance.name = prefab.name;
            _currentInstance.transform.localPosition = Vector3.zero;
            _currentInstance.transform.localRotation = Quaternion.identity;
            _currentInstance.transform.localScale = Vector3.one;
            _currentIndex = index;

            if (worldMotion != null)
            {
                worldMotion.RecaptureExteriorPose(resetShipState: resetShipMotionOnSwap);
            }

            OceanNextGenAdapter.EnsureInstance().NotifyExteriorChanged();

            Debug.Log($"[ExteriorScenarioLoader] Loaded scenario [{index}] '{CurrentId}'.");
            return true;
        }

        private void ResolveRefs()
        {
            if (exteriorWorld == null)
            {
                exteriorWorld = ExteriorWorldRoot.Instance;
            }

            if (worldMotion == null)
            {
                worldMotion = FindAnyObjectByType<ExteriorWorldMotion>();
            }
        }
    }
}
