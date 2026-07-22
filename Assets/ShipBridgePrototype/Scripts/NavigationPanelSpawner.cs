using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace ShipBridgePrototype
{
    /// <summary>
    /// Spawns / repositions the navigation panel in front of the player when button A is pressed.
    /// </summary>
    public class NavigationPanelSpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private NavigationPanel panelPrefab;

        [Header("Placement")]
        [SerializeField] private float spawnDistance = 0.9f;
        [SerializeField] [Range(0f, 1f)] private float heightBetweenHandsAndHead = 0.55f;

        [Header("Input")]
        [SerializeField] private OVRInput.RawButton spawnButton = OVRInput.RawButton.A;

        [Header("References")]
        [SerializeField] private OVRCameraRig cameraRig;

        private NavigationPanel _activePanel;

        private void Awake()
        {
            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }
        }

        private void Update()
        {
            // Primary: Quest controller (A by default). Keyboard only in Editor via Input System.
            var pressed = OVRInput.GetDown(spawnButton);
#if UNITY_EDITOR
            if (!pressed && Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
            {
                pressed = true;
            }
#endif

            if (!pressed)
            {
                return;
            }

            SpawnOrReposition();
        }

        public void SpawnOrReposition()
        {
            if (panelPrefab == null)
            {
                Debug.LogError("[NavigationPanelSpawner] Missing panel prefab.", this);
                return;
            }

            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
                if (cameraRig == null)
                {
                    Debug.LogError("[NavigationPanelSpawner] No OVRCameraRig found.", this);
                    return;
                }
            }

            if (!TryGetSpawnPose(out var position, out var rotation))
            {
                return;
            }

            if (_activePanel == null)
            {
                _activePanel = Instantiate(panelPrefab, position, rotation);
                _activePanel.name = "NavigationPanel";
            }
            else
            {
                _activePanel.transform.SetPositionAndRotation(position, rotation);
                if (!_activePanel.gameObject.activeSelf)
                {
                    _activePanel.gameObject.SetActive(true);
                }
            }
        }

        private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            var head = cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor
                : cameraRig.transform;

            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = cameraRig.transform.forward;
                forward.y = 0f;
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            var left = cameraRig.leftHandAnchor;
            var right = cameraRig.rightHandAnchor;
            var handY = head.position.y - 0.35f;
            if (left != null && right != null)
            {
                handY = (left.position.y + right.position.y) * 0.5f;
            }

            var height = Mathf.Lerp(handY, head.position.y, heightBetweenHandsAndHead);
            position = head.position + forward * spawnDistance;
            position.y = height;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }
    }
}
