using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NavigationSim.UnityLayer.UI
{
    /// <summary>Shared builders for world-space bridge instrument canvases.</summary>
    public static class BridgeInstrumentCanvas
    {
        public static readonly Color PanelBg = new Color(0.10f, 0.12f, 0.14f, 0.97f);
        public static readonly Color CardBg = new Color(0.16f, 0.18f, 0.20f, 1f);
        public static readonly Color TextPrimary = new Color(0.93f, 0.95f, 0.97f, 1f);
        public static readonly Color TextMuted = new Color(0.62f, 0.66f, 0.70f, 1f);
        public static readonly Color AccentAmber = new Color(0.95f, 0.62f, 0.18f, 1f);
        public static readonly Color AccentCyan = new Color(0.35f, 0.78f, 0.92f, 1f);
        public static readonly Color AccentGreen = new Color(0.35f, 0.78f, 0.45f, 1f);
        public static readonly Color Danger = new Color(0.88f, 0.28f, 0.22f, 1f);

        private static Sprite _white;

        public static Sprite WhiteSprite
        {
            get
            {
                if (_white == null)
                {
                    var tex = Texture2D.whiteTexture;
                    _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                }

                return _white;
            }
        }

        public static GameObject CreateCanvas(string name, Transform parent, Vector2 size, float scale)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            root.transform.localScale = Vector3.one * scale;
            return root;
        }

        public static void PlaceInFront(Transform canvasRoot, float distance, float lateral, float lift)
        {
            var cam = Camera.main;
            if (cam == null || canvasRoot == null)
            {
                return;
            }

            var forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            canvasRoot.position = cam.transform.position + forward * distance + right * lateral + Vector3.up * lift;
            canvasRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public static Image Image(RectTransform parent, string name, Vector2 topLeft, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = WhiteSprite;
            img.raycastTarget = false;
            return img;
        }

        public static TMP_Text Text(RectTransform parent, string name, Vector2 topLeft, Vector2 size,
            string content, float fontSize, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        public static SimUiButton Button(RectTransform parent, Vector2 topLeft, Vector2 size,
            string label, Color color, Action onClick, bool autoRepeat = false)
        {
            var img = Image(parent, $"Btn_{label}", topLeft, size, color);
            img.raycastTarget = true;
            Text(img.rectTransform, "Label", Vector2.zero, size, label, 18f,
                TextAlignmentOptions.Center, Color.white);

            var collider = img.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 12f);
            collider.center = new Vector3(size.x * 0.5f, -size.y * 0.5f, 0f);

            var btn = img.gameObject.AddComponent<SimUiButton>();
            btn.Bind(img);
            btn.OnClick = onClick;
            btn.AutoRepeat = autoRepeat;
            return btn;
        }
    }
}
