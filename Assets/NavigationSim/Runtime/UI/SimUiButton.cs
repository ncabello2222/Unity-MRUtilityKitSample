using System;
using UnityEngine;
using UnityEngine.UI;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>
    /// Minimal VR button driven by <see cref="VrUiPointer"/> through physics
    /// raycasts (no EventSystem needed). Supports click, auto-repeat while held
    /// (for +/- rows) and hold notifications (for NFU levers).
    /// </summary>
    public class SimUiButton : MonoBehaviour
    {
        public Action OnClick;
        public Action<bool> OnHoldChanged;
        public bool AutoRepeat;

        private const float RepeatDelay = 0.45f;
        private const float RepeatInterval = 0.12f;

        private Image _background;
        private Color _baseColor;
        private bool _hovered;
        private bool _pressed;
        private float _repeatTimer;

        public void Bind(Image background)
        {
            _background = background;
            _baseColor = background.color;
        }

        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            RefreshColor();

            if (!hovered && _pressed)
            {
                Release();
            }
        }

        public void Press()
        {
            if (_pressed)
            {
                return;
            }

            _pressed = true;
            _repeatTimer = RepeatDelay;
            RefreshColor();
            OnClick?.Invoke();
            OnHoldChanged?.Invoke(true);
        }

        public void Release()
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            RefreshColor();
            OnHoldChanged?.Invoke(false);
        }

        private void Update()
        {
            if (!_pressed || !AutoRepeat)
            {
                return;
            }

            _repeatTimer -= Time.deltaTime;
            if (_repeatTimer <= 0f)
            {
                _repeatTimer = RepeatInterval;
                OnClick?.Invoke();
            }
        }

        private void OnDisable()
        {
            _pressed = false;
            _hovered = false;
            if (_background != null)
            {
                _background.color = _baseColor;
            }
        }

        private void RefreshColor()
        {
            if (_background == null)
            {
                return;
            }

            if (_pressed)
            {
                _background.color = _baseColor * 1.6f;
            }
            else if (_hovered)
            {
                _background.color = _baseColor * 1.3f;
            }
            else
            {
                _background.color = _baseColor;
            }
        }
    }
}
