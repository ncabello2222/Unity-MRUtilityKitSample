using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Single source of truth for ship bow / port / starboard orientation in the
    /// physical bridge room. Local convention: +Z forward (bow), +X starboard, +Y up.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeReferenceFrame : MonoBehaviour
    {
        public static BridgeReferenceFrame Instance { get; private set; }

        [SerializeField] private bool isCalibrated;
        [SerializeField] private Vector3 forward = Vector3.forward;
        [SerializeField] private Vector3 starboard = Vector3.right;

        public Transform Pivot => transform;
        public Vector3 Forward => forward;
        public Vector3 Aft => -forward;
        public Vector3 Starboard => starboard;
        public Vector3 Port => -starboard;
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public MRUKAnchor FrontWall { get; private set; }
        public MRUKAnchor AftWall { get; private set; }
        public MRUKAnchor PortWall { get; private set; }
        public MRUKAnchor StarboardWall { get; private set; }
        public bool IsCalibrated => isCalibrated;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Apply(
            Vector3 forwardWorld,
            MRUKAnchor front,
            MRUKAnchor aft,
            MRUKAnchor port,
            MRUKAnchor starboardWall,
            bool calibrated)
        {
            forward = Flatten(forwardWorld);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            starboard = Vector3.Cross(Vector3.up, forward).normalized;
            if (starboard.sqrMagnitude < 1e-6f)
            {
                starboard = Vector3.right;
            }

            Rotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.rotation = Rotation;

            FrontWall = front;
            AftWall = aft;
            PortWall = port;
            StarboardWall = starboardWall;
            isCalibrated = calibrated;
        }

        public void SetCalibrated(bool calibrated)
        {
            isCalibrated = calibrated;
        }

        public static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
