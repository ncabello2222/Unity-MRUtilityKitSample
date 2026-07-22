using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Navigation control panel: grabbable frame, lock toggle, and a ControlsAnchor for widgets.
    /// Lock only freezes panel repositioning — instrument grabs stay available.
    /// </summary>
    public class NavigationPanel : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Transform controlsAnchor;
        [SerializeField] private Transform frameRoot;

        [Header("Lock UI")]
        [SerializeField] private Button lockButton;
        [SerializeField] private TextMeshProUGUI lockButtonLabel;
        [SerializeField] private Image lockButtonBackground;
        [SerializeField] private Color unlockedColor = new Color(0.15f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color lockedColor = new Color(0.85f, 0.25f, 0.2f, 1f);

        [SerializeField] private bool startLocked;

        private readonly List<IInteractable> _frameGrabInteractables = new();
        private bool _isLocked;

        public Transform ControlsAnchor => controlsAnchor;
        public bool IsLocked => _isLocked;

        private void Awake()
        {
            CacheFrameGrabInteractables();
            if (lockButton != null)
            {
                lockButton.onClick.AddListener(ToggleLock);
            }

            SetLocked(startLocked, force: true);
            EnsureProximityFade();
        }

        private void EnsureProximityFade()
        {
            var fade = GetComponent<PanelProximityFade>();
            if (fade == null)
            {
                fade = gameObject.AddComponent<PanelProximityFade>();
            }

            fade.AutoBindSurface();
        }

        private void OnDestroy()
        {
            if (lockButton != null)
            {
                lockButton.onClick.RemoveListener(ToggleLock);
            }
        }

        public void ToggleLock()
        {
            SetLocked(!_isLocked);
        }

        public void SetLocked(bool locked)
        {
            SetLocked(locked, force: false);
        }

        private void SetLocked(bool locked, bool force)
        {
            if (!force && _isLocked == locked)
            {
                return;
            }

            _isLocked = locked;

            for (var i = 0; i < _frameGrabInteractables.Count; i++)
            {
                var interactable = _frameGrabInteractables[i];
                if (interactable == null)
                {
                    continue;
                }

                if (_isLocked)
                {
                    interactable.Disable();
                }
                else
                {
                    interactable.Enable();
                }
            }

            RefreshLockVisuals();
        }

        private void CacheFrameGrabInteractables()
        {
            _frameGrabInteractables.Clear();

            void Consider(MonoBehaviour behaviour)
            {
                if (behaviour == null)
                {
                    return;
                }

                // Instrument grabs live under ControlsAnchor and must stay usable while the panel is locked.
                if (controlsAnchor != null && behaviour.transform.IsChildOf(controlsAnchor))
                {
                    return;
                }

                if (behaviour is IInteractable interactable)
                {
                    _frameGrabInteractables.Add(interactable);
                }
            }

            foreach (var c in GetComponentsInChildren<GrabInteractable>(true))
            {
                Consider(c);
            }

            foreach (var c in GetComponentsInChildren<HandGrabInteractable>(true))
            {
                Consider(c);
            }

            foreach (var c in GetComponentsInChildren<DistanceGrabInteractable>(true))
            {
                Consider(c);
            }

            foreach (var c in GetComponentsInChildren<DistanceHandGrabInteractable>(true))
            {
                Consider(c);
            }
        }

        private void RefreshLockVisuals()
        {
            if (lockButtonLabel != null)
            {
                lockButtonLabel.text = _isLocked ? "Unlock" : "Lock";
            }

            if (lockButtonBackground != null)
            {
                lockButtonBackground.color = _isLocked ? lockedColor : unlockedColor;
            }
        }

#if UNITY_EDITOR
        public void EditorBind(
            Transform controls,
            Transform frame,
            Button button,
            TextMeshProUGUI label,
            Image background)
        {
            controlsAnchor = controls;
            frameRoot = frame;
            lockButton = button;
            lockButtonLabel = label;
            lockButtonBackground = background;
        }
#endif
    }
}
