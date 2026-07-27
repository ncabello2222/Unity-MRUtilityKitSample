using System.Collections.Generic;
using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Derives radar/chart land echoes from the scenery that is actually on screen.
    /// <para>
    /// The land samples used to be a hard-coded pair of point rows with a comment
    /// claiming they matched the coastal scenario; they did not — none of the 48 points
    /// fell on any of the six islands the prefab draws, so the radar painted open water
    /// as land and left the visible coast invisible. Reading the scenario's own renderers
    /// makes the two agree by construction, for any scenario, with no authoring step.
    /// </para>
    /// Coordinates come out geographic (East/North), which is what the sim core, radar,
    /// chart and ARPA all speak. That relies on <see cref="ExteriorWorldMotion"/> parking
    /// the exterior on its scenario datum, so scenario-local (x, z) reads as (East, North).
    /// </summary>
    public static class ScenarioLandmassSampler
    {
        /// <summary>
        /// Samples the outline of every sizeable renderer under <paramref name="scenarioRoot"/>.
        /// </summary>
        /// <param name="exteriorRoot">Frame the samples are expressed in (ExteriorWorld.Root).</param>
        /// <param name="minExtentM">Ignore props smaller than this; they are not radar targets.</param>
        /// <param name="pointsPerBody">Outline samples per landmass. Radar sees the edge.</param>
        public static List<(double north, double east)> Sample(
            Transform scenarioRoot,
            Transform exteriorRoot,
            float minExtentM = 8f,
            int pointsPerBody = 16,
            int maxBodies = 96)
        {
            var samples = new List<(double north, double east)>();
            if (scenarioRoot == null || exteriorRoot == null)
            {
                return samples;
            }

            // Inactive renderers are skipped on purpose: the ocean adapter disables the
            // scenario's own water planes, and a water plane is not land.
            var renderers = scenarioRoot.GetComponentsInChildren<Renderer>(false);
            int bodies = 0;

            for (int i = 0; i < renderers.Length && bodies < maxBodies; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                Bounds b = r.bounds;
                Vector3 centreLocal = exteriorRoot.InverseTransformPoint(b.center);

                // The exterior root is yaw-only, so world extents carry over to the
                // scenario frame unchanged.
                float ex = b.extents.x;
                float ez = b.extents.z;
                float radius = Mathf.Max(ex, ez);
                if (radius < minExtentM)
                {
                    continue;
                }

                bodies++;
                for (int p = 0; p < pointsPerBody; p++)
                {
                    float a = p / (float)pointsPerBody * Mathf.PI * 2f;
                    float east = centreLocal.x + Mathf.Sin(a) * ex;
                    float north = centreLocal.z + Mathf.Cos(a) * ez;
                    samples.Add((north, east));
                }
            }

            return samples;
        }
    }
}
