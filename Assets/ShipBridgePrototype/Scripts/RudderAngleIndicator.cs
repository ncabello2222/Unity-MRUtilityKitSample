using TMPro;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Semicircular rudder angle gauge. Needle tracks the actual (following) rudder angle.
    /// </summary>
    public class RudderAngleIndicator : MonoBehaviour
    {
        [SerializeField] private ShipControlState controlState;
        [SerializeField] private Transform needle;
        [SerializeField] private TextMeshPro valueLabel;
        [SerializeField] private float minAngleDeg = -35f;
        [SerializeField] private float maxAngleDeg = 35f;
        [SerializeField] private float needleMinLocalZ = 90f;
        [SerializeField] private float needleMaxLocalZ = -90f;

        private void Awake()
        {
            ResolveControlState();
        }

        private void Update()
        {
            ResolveControlState();
            if (controlState == null)
            {
                return;
            }

            var rudder = Mathf.Clamp(controlState.ActualRudderAngleDeg, minAngleDeg, maxAngleDeg);
            var t = Mathf.InverseLerp(minAngleDeg, maxAngleDeg, rudder);
            var needleZ = Mathf.Lerp(needleMinLocalZ, needleMaxLocalZ, t);

            if (needle != null)
            {
                var e = needle.localEulerAngles;
                needle.localRotation = Quaternion.Euler(e.x, e.y, needleZ);
            }

            if (valueLabel != null)
            {
                var side = rudder > 0.05f ? "STBD" : rudder < -0.05f ? "PORT" : "AMID";
                valueLabel.text = $"{rudder:0.0}° {side}";
            }
        }

        private void ResolveControlState()
        {
            if (controlState == null)
            {
                controlState = ShipControlState.Instance;
            }
        }

#if UNITY_EDITOR
        public void EditorBind(ShipControlState state, Transform needleTransform, TextMeshPro label)
        {
            controlState = state;
            needle = needleTransform;
            valueLabel = label;
        }
#endif
    }
}
