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
        [Tooltip("Virtual button One maps to A on RTouch (Meta OVRInput docs).")]
        [SerializeField] private OVRInput.Button spawnButton = OVRInput.Button.One;

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
            if (!WasSpawnPressed())
            {
                return;
            }

            SpawnOrReposition();
        }

        private bool WasSpawnPressed()
        {
            // Query RTouch / Touch explicitly. Controller.Active often sticks on Hands while
            // the comprehensive ISDK rig is running, so RawButton.A with Active never fires.
            if (OVRInput.GetDown(spawnButton, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.RawButton.A, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(spawnButton, OVRInput.Controller.Touch))
            {
                return true;
            }

#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            return false;
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
                Debug.LogWarning("[NavigationPanelSpawner] Could not resolve spawn pose.", this);
                return;
            }

            if (_activePanel == null)
            {
                _activePanel = Instantiate(panelPrefab, position, rotation);
                _activePanel.name = "NavigationPanel";
                Debug.Log($"[NavigationPanelSpawner] Spawned panel at {position}.", _activePanel);
            }
            else
            {
                _activePanel.transform.SetPositionAndRotation(position, rotation);
                if (!_activePanel.gameObject.activeSelf)
                {
                    _activePanel.gameObject.SetActive(true);
                }

                Debug.Log($"[NavigationPanelSpawner] Repositioned panel at {position}.", _activePanel);
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
            // +Z away from user so panel front (-Z / Unity UI) faces them.
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }
    }
}
