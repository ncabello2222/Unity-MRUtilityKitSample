using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Removes unnecessary runtime geometry and physics components from the generated bridge.
    /// The exterior hierarchy is intentionally left untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BridgeGeneratedGeometryOptimizer : MonoBehaviour
    {
        private BridgeRoomMapper _mapper;
        private Transform _lastProcessedRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var mappers = FindObjectsByType<BridgeRoomMapper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var mapper in mappers)
            {
                if (mapper != null && mapper.GetComponent<BridgeGeneratedGeometryOptimizer>() == null)
                {
                    mapper.gameObject.AddComponent<BridgeGeneratedGeometryOptimizer>();
                }
            }
        }

        private void Awake()
        {
            _mapper = GetComponent<BridgeRoomMapper>();
        }

        private void LateUpdate()
        {
            if (_mapper == null)
            {
                return;
            }

            var generatedRoot = _mapper.GeneratedRoot;
            if (generatedRoot == null || generatedRoot == _lastProcessedRoot)
            {
                return;
            }

            Optimize(generatedRoot, _mapper.ExteriorWorld != null ? _mapper.ExteriorWorld.transform : null);
            _lastProcessedRoot = generatedRoot;
        }

        private static void Optimize(Transform generatedRoot, Transform exteriorRoot)
        {
            RemoveWindowGeometry(generatedRoot, exteriorRoot);
            RemoveBridgeColliders(generatedRoot, exteriorRoot);
        }

        private static void RemoveWindowGeometry(Transform generatedRoot, Transform exteriorRoot)
        {
            var transforms = generatedRoot.GetComponentsInChildren<Transform>(true);
            for (var i = transforms.Length - 1; i >= 0; i--)
            {
                var current = transforms[i];
                if (current == null || current == generatedRoot || IsInsideExterior(current, exteriorRoot))
                {
                    continue;
                }

                // The surrounding wall panels already define the real aperture.
                // WindowOpening only contains synthetic glass and four decorative frame cubes.
                if (current.name == "WindowOpening")
                {
                    Destroy(current.gameObject);
                }
            }
        }

        private static void RemoveBridgeColliders(Transform generatedRoot, Transform exteriorRoot)
        {
            var colliders = generatedRoot.GetComponentsInChildren<Collider>(true);
            foreach (var bridgeCollider in colliders)
            {
                if (bridgeCollider == null || IsInsideExterior(bridgeCollider.transform, exteriorRoot))
                {
                    continue;
                }

                Destroy(bridgeCollider);
            }
        }

        private static bool IsInsideExterior(Transform candidate, Transform exteriorRoot)
        {
            return exteriorRoot != null &&
                   (candidate == exteriorRoot || candidate.IsChildOf(exteriorRoot));
        }
    }
}
