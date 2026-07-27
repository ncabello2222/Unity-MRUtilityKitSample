using System.Collections.Generic;
using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Spawns simple hull/buoy visuals for <see cref="TrafficWorld"/> contacts and
    /// keeps them glued to the geographic frame via <see cref="ExteriorWorldMotion"/>.
    /// </summary>
    public sealed class OtherShipPresenter : MonoBehaviour
    {
        private sealed class Visual
        {
            public int Id;
            public Transform Root;
            public TrafficKind Kind;
        }

        private readonly List<Visual> _visuals = new List<Visual>();
        private NavigationSimRunner _runner;
        private ExteriorWorldMotion _motion;
        private Transform _root;

        public static OtherShipPresenter EnsureInstance()
        {
            var existing = FindAnyObjectByType<OtherShipPresenter>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("OtherShipPresenter");
            return go.AddComponent<OtherShipPresenter>();
        }

        private void Awake()
        {
            _runner = NavigationSimRunner.EnsureInstance();
            _root = new GameObject("TrafficVisuals").transform;
            _root.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_runner == null || _runner.Traffic == null)
            {
                return;
            }

            if (_motion == null)
            {
                _motion = FindAnyObjectByType<ExteriorWorldMotion>();
            }

            SyncRoster(_runner.Traffic);
            UpdatePoses(_runner.Traffic);
        }

        private void SyncRoster(TrafficWorld traffic)
        {
            for (int i = 0; i < traffic.Contacts.Count; i++)
            {
                TrafficContact c = traffic.Contacts[i];
                if (FindVisual(c.Id) == null)
                {
                    _visuals.Add(CreateVisual(c));
                }
            }

            for (int i = _visuals.Count - 1; i >= 0; i--)
            {
                if (traffic.FindById(_visuals[i].Id) == null)
                {
                    if (_visuals[i].Root != null)
                    {
                        // The material is a per-contact instance, so it leaks unless it
                        // goes with the object.
                        var rend = _visuals[i].Root.GetComponent<Renderer>();
                        if (rend != null && rend.material != null)
                        {
                            Destroy(rend.material);
                        }

                        Destroy(_visuals[i].Root.gameObject);
                    }

                    _visuals.RemoveAt(i);
                }
            }
        }

        private Visual FindVisual(int id)
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i].Id == id)
                {
                    return _visuals[i];
                }
            }

            return null;
        }

        private Visual CreateVisual(TrafficContact c)
        {
            var go = GameObject.CreatePrimitive(
                c.Kind == TrafficKind.Buoy ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = $"Traffic_{c.Id}_{c.Name}";
            go.transform.SetParent(_root, false);

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    rend.material = new Material(shader)
                    {
                        color = c.Kind == TrafficKind.Buoy
                            ? new Color(0.9f, 0.75f, 0.15f)
                            : new Color(0.55f, 0.2f, 0.15f)
                    };
                }
            }

            float len = Mathf.Max(4f, (float)c.LengthM);
            float beam = Mathf.Max(2f, (float)c.BeamM);
            if (c.Kind == TrafficKind.Buoy)
            {
                go.transform.localScale = new Vector3(2.5f, 4f, 2.5f);
            }
            else
            {
                go.transform.localScale = new Vector3(beam, Mathf.Max(3f, beam * 0.4f), len);
            }

            return new Visual { Id = c.Id, Root = go.transform, Kind = c.Kind };
        }

        private void UpdatePoses(TrafficWorld traffic)
        {
            Quaternion basis = Quaternion.identity;
            if (_motion != null && _motion.HasInitialPose)
            {
                basis = _motion.ShipForwardBasis;
            }

            for (int i = 0; i < _visuals.Count; i++)
            {
                Visual v = _visuals[i];
                TrafficContact c = traffic.FindById(v.Id);
                if (c == null || v.Root == null)
                {
                    continue;
                }

                v.Root.gameObject.SetActive(c.Visible);

                Vector3 world;
                if (_motion != null && _motion.HasInitialPose)
                {
                    world = _motion.GeoToWorld(c.East, c.North);

                    // Float them on the waterline, the same datum the ocean plane and the
                    // coastline hang from. GeoToWorld's Y already carries own-ship heave,
                    // so dropping the waterline out of it keeps the traffic riding with
                    // the sea. Overwriting that Y outright (the previous behaviour) parked
                    // the hulls 12 m above the water and froze them while the whole
                    // exterior rose and fell.
                    world.y -= _motion.BridgeAboveSeaM;
                }
                else
                {
                    world = new Vector3((float)c.East, 0f, (float)c.North);
                }

                // GeoToWorld already applies inverse own-ship transform; heading in the
                // room frame is geographic heading minus current own-ship yaw.
                float ownPsi = NavigationSimRunner.Instance != null
                    ? (float)NavigationSimRunner.Instance.InterpPsiDeg
                    : 0f;
                Quaternion heading = basis * Quaternion.Euler(0f, (float)c.HeadingDeg - ownPsi, 0f);
                v.Root.SetPositionAndRotation(world, heading);
            }
        }
    }
}
