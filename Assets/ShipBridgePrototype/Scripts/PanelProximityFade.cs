using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Fades panel surface materials when the headset gets close, so an opaque slab
    /// does not fill the FOV / clip through the near plane.
    /// </summary>
    public class PanelProximityFade : MonoBehaviour
    {
        [SerializeField] private Renderer[] fadeRenderers;
        [SerializeField] private Transform eyeAnchor;
        [SerializeField] private float fadeStartDistance = 0.55f;
        [SerializeField] private float fadeEndDistance = 0.22f;
        [SerializeField] [Range(0f, 1f)] private float farAlpha = 0.55f;
        [SerializeField] [Range(0f, 1f)] private float nearAlpha = 0.08f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;
        private Color[] _baseColors;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (fadeRenderers == null || fadeRenderers.Length == 0)
            {
                AutoBindSurface();
            }

            CacheBaseColors();
            ResolveEyeAnchor();
        }

        /// <summary>Finds FrameGrab/Surface (or Frame/Surface) when nothing was wired in the Inspector.</summary>
        public void AutoBindSurface()
        {
            var surface = transform.Find("FrameGrab/Surface")
                          ?? transform.Find("Frame/Surface")
                          ?? transform.Find("Surface");
            if (surface == null)
            {
                return;
            }

            var renderer = surface.GetComponent<Renderer>();
            if (renderer != null)
            {
                fadeRenderers = new[] { renderer };
            }
        }

        private void LateUpdate()
        {
            if (fadeRenderers == null || fadeRenderers.Length == 0)
            {
                return;
            }

            if (eyeAnchor == null)
            {
                ResolveEyeAnchor();
                if (eyeAnchor == null)
                {
                    return;
                }
            }

            var distance = Vector3.Distance(eyeAnchor.position, transform.position);
            var t = Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, distance);
            var alpha = Mathf.Lerp(farAlpha, nearAlpha, t);

            for (var i = 0; i < fadeRenderers.Length; i++)
            {
                var renderer = fadeRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var color = _baseColors != null && i < _baseColors.Length
                    ? _baseColors[i]
                    : Color.white;
                color.a = alpha;
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                _block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void CacheBaseColors()
        {
            if (fadeRenderers == null)
            {
                _baseColors = System.Array.Empty<Color>();
                return;
            }

            _baseColors = new Color[fadeRenderers.Length];
            for (var i = 0; i < fadeRenderers.Length; i++)
            {
                var renderer = fadeRenderers[i];
                if (renderer == null || renderer.sharedMaterial == null)
                {
                    _baseColors[i] = new Color(0.12f, 0.14f, 0.18f, farAlpha);
                    continue;
                }

                var mat = renderer.sharedMaterial;
                _baseColors[i] = mat.HasProperty(BaseColorId)
                    ? mat.GetColor(BaseColorId)
                    : mat.color;
            }
        }

        private void ResolveEyeAnchor()
        {
            if (eyeAnchor != null)
            {
                return;
            }

            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                eyeAnchor = rig.centerEyeAnchor;
                return;
            }

            if (Camera.main != null)
            {
                eyeAnchor = Camera.main.transform;
            }
        }

#if UNITY_EDITOR
        public void EditorBind(params Renderer[] renderers)
        {
            fadeRenderers = renderers;
        }
#endif
    }
}
