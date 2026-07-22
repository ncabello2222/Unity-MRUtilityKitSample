using UnityEngine;

namespace ShipBridgePrototype
{
    /// <summary>
    /// Builds the default coastal exterior in local space:
    /// origin = bridge/room reference, +Z = out the front windows.
    /// </summary>
    public static class CoastalExteriorScenarioBuilder
    {
        public static GameObject Build(
            Transform parent,
            Material waterMaterial,
            Material mountainMaterial,
            float terrainSize = 220f,
            float terrainHeight = 45f,
            float distanceFromBridge = 18f)
        {
            var root = new GameObject("CoastalMountains");
            root.transform.SetParent(parent, false);

            var exteriorCenter = Vector3.forward * distanceFromBridge;
            var forward = Vector3.forward;
            var right = Vector3.right;

            CreateWaterPlane(root.transform, exteriorCenter, forward, waterMaterial, terrainSize);
            CreateTerrainMountains(root.transform, exteriorCenter, forward, right, mountainMaterial, terrainSize, terrainHeight);
            CreatePrimitiveMountainRing(root.transform, exteriorCenter, forward, right, mountainMaterial);

            return root;
        }

        private static void CreateWaterPlane(
            Transform parent,
            Vector3 exteriorCenter,
            Vector3 forward,
            Material waterMaterial,
            float terrainSize)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.SetParent(parent, false);
            water.transform.localPosition = exteriorCenter + Vector3.down * 1.5f - forward * 8f;
            water.transform.localRotation = Quaternion.identity;
            water.transform.localScale = new Vector3(terrainSize * 0.12f, 1f, terrainSize * 0.12f);
            if (waterMaterial != null)
            {
                water.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
            }
        }

        private static void CreateTerrainMountains(
            Transform parent,
            Vector3 exteriorCenter,
            Vector3 forward,
            Vector3 right,
            Material mountainMaterial,
            float terrainSize,
            float terrainHeight)
        {
            var terrainData = new TerrainData
            {
                heightmapResolution = 129,
                size = new Vector3(terrainSize, terrainHeight, terrainSize)
            };

            var res = terrainData.heightmapResolution;
            var heights = new float[res, res];
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var nx = x / (float)(res - 1);
                    var nz = z / (float)(res - 1);
                    var h = 0f;
                    h += Peak(nx, nz, 0.22f, 0.70f, 0.18f, 0.85f);
                    h += Peak(nx, nz, 0.48f, 0.78f, 0.22f, 1.0f);
                    h += Peak(nx, nz, 0.72f, 0.66f, 0.16f, 0.75f);
                    h += Peak(nx, nz, 0.38f, 0.55f, 0.28f, 0.35f);
                    h += Mathf.PerlinNoise(nx * 4.1f, nz * 4.1f) * 0.08f;
                    heights[z, x] = Mathf.Clamp01(h);
                }
            }

            terrainData.SetHeights(0, 0, heights);

            var terrainGo = Terrain.CreateTerrainGameObject(terrainData);
            terrainGo.name = "ExteriorTerrain";
            terrainGo.transform.SetParent(parent, false);
            terrainGo.transform.localPosition = exteriorCenter
                                               + forward * (terrainSize * 0.15f)
                                               - right * (terrainSize * 0.5f)
                                               + Vector3.down * 2f;
            terrainGo.transform.localRotation = Quaternion.identity;

            var terrain = terrainGo.GetComponent<Terrain>();
            if (terrain != null && mountainMaterial != null)
            {
                terrain.materialTemplate = mountainMaterial;
            }
        }

        private static void CreatePrimitiveMountainRing(
            Transform parent,
            Vector3 exteriorCenter,
            Vector3 forward,
            Vector3 right,
            Material mountainMaterial)
        {
            var mountains = new GameObject("MountainPrimitives").transform;
            mountains.SetParent(parent, false);

            PlaceMountain(mountains, exteriorCenter + forward * 28f + right * -18f, new Vector3(22f, 18f, 22f), mountainMaterial);
            PlaceMountain(mountains, exteriorCenter + forward * 14f + right * 12f, new Vector3(16f, 12f, 16f), mountainMaterial);
            PlaceMountain(mountains, exteriorCenter + forward * 20f + right * 26f, new Vector3(20f, 16f, 18f), mountainMaterial);
            PlaceMountain(mountains, exteriorCenter + forward * 34f + right * 4f, new Vector3(28f, 24f, 24f), mountainMaterial);
            PlaceMountain(mountains, exteriorCenter + forward * 10f + right * -30f, new Vector3(14f, 10f, 14f), mountainMaterial);
        }

        private static void PlaceMountain(Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var mountain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mountain.name = "Mountain";
            mountain.transform.SetParent(parent, false);
            mountain.transform.localPosition = localPosition;
            mountain.transform.localScale = scale;
            if (material != null)
            {
                mountain.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            var collider = mountain.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(collider);
                }
                else
                {
                    Object.DestroyImmediate(collider);
                }
            }
        }

        private static float Peak(float x, float z, float cx, float cz, float radius, float amplitude)
        {
            var dx = (x - cx) / radius;
            var dz = (z - cz) / radius;
            var d = dx * dx + dz * dz;
            if (d > 1f)
            {
                return 0f;
            }

            return (1f - d) * (1f - d) * amplitude;
        }
    }
}
