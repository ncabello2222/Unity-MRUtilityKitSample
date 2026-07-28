using System;
using System.Collections.Generic;
using NavigationSim.Core;
using ShipBridgePrototype;
using UnityEngine;

namespace NavigationSim.UnityLayer
{
    /// <summary>
    /// Spawns simple hull/buoy visuals for <see cref="TrafficWorld"/> contacts and
    /// keeps them glued to the geographic frame via <see cref="ExteriorWorldMotion"/>,
    /// riding the same iFFT surface own ship does.
    /// </summary>
    public sealed class OtherShipPresenter : MonoBehaviour
    {
        /// <summary>Wall-clock rate at which contacts re-probe the wave field [Hz].</summary>
        private const float SeakeepingSampleHz = 10f;

        /// <summary>
        /// Past this the vertical bob subtends well under a headset pixel, so the contact
        /// stays parked at mean level and costs nothing.
        /// </summary>
        private const double SeakeepingRangeM = 3000.0;

        /// <summary>
        /// Convergence rate toward a fresh probe [1/s]. Also the only inertia a contact
        /// gets — own ship has second-order filters, a contact is a cork with a lag.
        /// </summary>
        private const float SeakeepingSmoothing = 8f;

        /// <summary>Safety rails, matching the ones <see cref="WaveResponseModel"/> keeps.</summary>
        private const float MaxPitchDeg = 18f;
        private const float MaxRollDeg = 25f;

        private sealed class Visual
        {
            public int Id;
            public Transform Root;
            public TrafficKind Kind;

            // Last probe of the surface, and the smoothed values actually rendered.
            public float TargetHeaveM;
            public float TargetPitchDeg;
            public float TargetRollDeg;
            public float HeaveM;
            public float PitchDeg;
            public float RollDeg;
            public bool HasSample;
        }

        private readonly List<Visual> _visuals = new List<Visual>();
        private NavigationSimRunner _runner;
        private ExteriorWorldMotion _motion;
        private Transform _root;
        private float _sampleAccum;

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

        /// <summary>
        /// Roster and surface probes. Both belong in Update, not LateUpdate:
        /// <see cref="NorthStarOceanAdapter"/> (execution order -50) schedules the iFFT
        /// from its own LateUpdate, so a probe taken after that would block the main
        /// thread on the jobs it had just kicked off. Here the textures from the last
        /// frame's render are already resident and SampleHeight is a plain texture read.
        /// </summary>
        private void Update()
        {
            if (_runner == null || _runner.Traffic == null)
            {
                return;
            }

            SyncRoster(_runner.Traffic);

            _sampleAccum += Time.deltaTime;
            if (_sampleAccum >= 1f / SeakeepingSampleHz)
            {
                _sampleAccum = 0f;
                RefreshSeakeeping(_runner.Traffic);
            }
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

        /// <summary>
        /// Re-probes the wave field under every contact that is close enough to be worth
        /// it. Contacts that are hidden, out of range, or that have no surface to read
        /// fall back to mean level, which is what the whole roster used to do.
        /// </summary>
        private void RefreshSeakeeping(TrafficWorld traffic)
        {
            IOceanSurface surface = ResolveSurface();
            ShipState own = _runner.Sim?.State;
            double timeS = own != null ? own.TimeS : 0.0;

            for (int i = 0; i < _visuals.Count; i++)
            {
                Visual v = _visuals[i];
                TrafficContact c = traffic.FindById(v.Id);
                if (c == null)
                {
                    continue;
                }

                if (surface == null || !c.Visible || OutOfSeakeepingRange(c, own))
                {
                    v.TargetHeaveM = 0f;
                    v.TargetPitchDeg = 0f;
                    v.TargetRollDeg = 0f;
                    continue;
                }

                SampleContact(surface, c, timeS, v);

                // First probe lands whole: a contact spawning in the near field would
                // otherwise be seen climbing out of the water as the filter converged.
                if (!v.HasSample)
                {
                    v.HasSample = true;
                    v.HeaveM = v.TargetHeaveM;
                    v.PitchDeg = v.TargetPitchDeg;
                    v.RollDeg = v.TargetRollDeg;
                }
            }
        }

        /// <summary>
        /// Buoys take one probe and stay upright. Hulls take four — bow, stern, port,
        /// starboard — which carries heave plus both attitude angles, using the same
        /// estimators and sign conventions as <see cref="WaveResponseModel"/> so a
        /// contact and own ship can never disagree about which way the sea is tilting.
        /// <para>
        /// No size-response factor is applied here: the probe spacing already supplies
        /// it. A hull long against the wavelength gets bow and stern samples that are
        /// decorrelated, so the fitted slope collapses on its own, and averaging the
        /// four flattens its heave — which is exactly what a long ship does in a short
        /// sea. A launch shorter than the wave follows the slope, as it should.
        /// </para>
        /// </summary>
        private static void SampleContact(IOceanSurface surface, TrafficContact c, double timeS, Visual v)
        {
            if (c.Kind == TrafficKind.Buoy)
            {
                v.TargetHeaveM = (float)surface.SampleHeight(c.East, c.North, timeS);
                v.TargetPitchDeg = 0f;
                v.TargetRollDeg = 0f;
                return;
            }

            double halfL = Math.Max(1.0, c.LengthM * 0.45);
            double halfB = Math.Max(0.5, c.BeamM * 0.45);
            double psi = c.HeadingDeg * Math.PI / 180.0;
            double fE = Math.Sin(psi);
            double fN = Math.Cos(psi);
            // Starboard in east/north: +90 deg off the bow.
            double sE = Math.Cos(psi);
            double sN = -Math.Sin(psi);

            double hBow = surface.SampleHeight(c.East + fE * halfL, c.North + fN * halfL, timeS);
            double hStern = surface.SampleHeight(c.East - fE * halfL, c.North - fN * halfL, timeS);
            double hStbd = surface.SampleHeight(c.East + sE * halfB, c.North + sN * halfB, timeS);
            double hPort = surface.SampleHeight(c.East - sE * halfB, c.North - sN * halfB, timeS);

            // The four probes are symmetric about the centre, so their mean is the centre
            // elevation and a fifth sample would buy nothing.
            v.TargetHeaveM = (float)((hBow + hStern + hStbd + hPort) * 0.25);
            // Pitch > 0 = bow down, roll > 0 = starboard down, as in WaveResponseModel.
            v.TargetPitchDeg = Mathf.Clamp(
                (float)(Math.Atan2(hStern - hBow, 2.0 * halfL) * (180.0 / Math.PI)),
                -MaxPitchDeg, MaxPitchDeg);
            v.TargetRollDeg = Mathf.Clamp(
                (float)(Math.Atan2(hPort - hStbd, 2.0 * halfB) * (180.0 / Math.PI)),
                -MaxRollDeg, MaxRollDeg);
        }

        private static bool OutOfSeakeepingRange(TrafficContact c, ShipState own)
        {
            if (own == null)
            {
                return false;
            }

            c.RangeBearingFrom(own.North, own.East, out double rangeM, out _);
            return rangeM > SeakeepingRangeM;
        }

        private static IOceanSurface ResolveSurface()
        {
            var ocean = NorthStarOceanAdapter.Instance;
            return ocean != null && ocean.IsReady ? ocean : null;
        }

        private void UpdatePoses(TrafficWorld traffic)
        {
            Quaternion basis = Quaternion.identity;
            if (_motion != null && _motion.HasInitialPose)
            {
                basis = _motion.ShipForwardBasis;
            }

            // Probes land at 10 Hz; the render runs at the headset refresh. Converging
            // toward the last probe keeps the motion continuous between them, and at
            // fast time — where the sea evolves faster than the probes can resolve —
            // it turns what would be strobing hulls into a gentle bob.
            float blend = 1f - Mathf.Exp(-SeakeepingSmoothing * Time.deltaTime);

            for (int i = 0; i < _visuals.Count; i++)
            {
                Visual v = _visuals[i];
                TrafficContact c = traffic.FindById(v.Id);
                if (c == null || v.Root == null)
                {
                    continue;
                }

                v.Root.gameObject.SetActive(c.Visible);

                v.HeaveM = Mathf.Lerp(v.HeaveM, v.TargetHeaveM, blend);
                v.PitchDeg = Mathf.Lerp(v.PitchDeg, v.TargetPitchDeg, blend);
                v.RollDeg = Mathf.Lerp(v.RollDeg, v.TargetRollDeg, blend);

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

                // ...and then onto the crest that is actually under them. The line above
                // lands on OceanRoot's mean-level plane exactly, so this offset is the
                // displacement the shader gives that patch of sea. Without it a contact
                // sat at mean level while the swell washed straight through it, and every
                // hull in the scenario inherited own ship's heave instead of its own.
                world.y += v.HeaveM;

                // GeoToWorld already applies inverse own-ship transform; heading in the
                // room frame is geographic heading minus current own-ship yaw. Pitch and
                // roll are body-frame, so they ride through that yaw untouched — same
                // Euler composition ExteriorWorldMotion.ResolveShipPose uses for own ship.
                float ownPsi = NavigationSimRunner.Instance != null
                    ? (float)NavigationSimRunner.Instance.InterpPsiDeg
                    : 0f;
                Quaternion heading = basis * Quaternion.Euler(
                    v.PitchDeg, (float)c.HeadingDeg - ownPsi, -v.RollDeg);
                v.Root.SetPositionAndRotation(world, heading);
            }
        }
    }
}
