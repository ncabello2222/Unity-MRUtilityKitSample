using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Marks the parent transform that owns all exterior environment visuals.
    /// The bridge/room stays fixed; <see cref="ExteriorWorldMotion"/> moves this root inversely.
    /// </summary>
    public class ExteriorWorldRoot : MonoBehaviour
    {
        public static ExteriorWorldRoot Instance { get; private set; }

        [Tooltip("Optional pivot used for yaw (defaults to this transform's position at bind time, or room center).")]
        [SerializeField] private Transform motionPivot;

        public Transform Root => transform;
        public Transform MotionPivot => motionPivot != null ? motionPivot : transform;

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

        public void SetMotionPivot(Transform pivot)
        {
            motionPivot = pivot;
        }
    }
}
