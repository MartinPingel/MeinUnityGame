#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Leveldesign
{
    /// <summary>
    /// Editor-only tool that builds the rolling hill landscape around the existing village
    /// (Dorf 1). Only ever creates new assets/GameObjects under its own output folder and
    /// "Landschaft" root; it never reads or writes any existing GameObject, component,
    /// material, or script. Re-running replaces only its own previous output.
    ///
    /// The village's own ground ("Dorf Boden", a 300x300 plane centred at VillageCenter)
    /// is left completely untouched: a terrain hole is punched out under its exact footprint
    /// so the new terrain never renders or collides there.
    /// </summary>
    public static class VillageTerrainGenerator
    {
        private const string OutputFolder = "Assets/Environment/Terrain";
        private const string TerrainDataPath = OutputFolder + "/Dorf1_Umgebung.asset";
        private const string GrassLayerPath = OutputFolder + "/Wiese.terrainlayer";
        private const string RockLayerPath = OutputFolder + "/Fels.terrainlayer";
        private const string GrassTexturePath = OutputFolder + "/Wiese_Diffuse.png";
        private const string RockTexturePath = OutputFolder + "/Fels_Diffuse.png";

        private const string LandscapeRootName = "Landschaft";
        private const string TerrainObjectName = "Dorf1_Huegellandschaft";

        // "Dorf Boden" in SampleScene.unity: local position (41.73209, 0, 63.01184),
        // scale (30, 1, 30) on the built-in 10x10 Plane -> a 300x300 square, half-size 150.
        // Taken from the existing scene file only for reference; never written back to it.
        private static readonly Vector2 VillageCenter = new Vector2(41.73209f, 63.01184f);
        private const float VillageHalfSize = 150f;
        private const float HoleBuffer = 1.5f; // keeps the hole strictly inside the village plate

        private const float FlatMargin = 40f;    // flat grass rim right outside the village plate
        private const float ShoulderWidth = 60f; // gentle ramp before hills reach full height

        private const float TerrainSize = 1000f;
        private const float TerrainHeight = 60f;
        private const float MaxHillHeight = 22f; // wide wavelength, low amplitude -> gentle slopes
        private const int HeightmapResolution = 513;
        private const int AlphamapResolution = 512;

        // East of the village stays open for the future road/corridor to Dorf 2.
        private const float CorridorHalfWidth = 90f;

        // Rockier region around the existing mine/smelter placeholders (north-west of the village).
        private static readonly Vector2 MineRegionCenter = new Vector2(-78f, 165f);
        private const float MineRegionRadius = 140f;

        private static readonly Color GrassColor = new Color(0.34f, 0.45f, 0.20f);
        private static readonly Color RockColor = new Color(0.27f, 0.28f, 0.26f); // matches MineRock.mat

        [MenuItem("Tools/Leveldesign/Dorf 1 - Huegellandschaft erzeugen")]
        public static void Generate()
        {
            EnsureFolder(OutputFolder);

            TerrainLayer grassLayer = CreateOrReplaceTerrainLayer(GrassLayerPath, GrassTexturePath, GrassColor, new Vector2(40f, 40f), 1);
            TerrainLayer rockLayer = CreateOrReplaceTerrainLayer(RockLayerPath, RockTexturePath, RockColor, new Vector2(25f, 25f), 2);

            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath) != null)
                AssetDatabase.DeleteAsset(TerrainDataPath);

            var terrainData = new TerrainData
            {
                heightmapResolution = HeightmapResolution,
                alphamapResolution = AlphamapResolution
            };
            terrainData.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            terrainData.terrainLayers = new[] { grassLayer, rockLayer };
            AssetDatabase.CreateAsset(terrainData, TerrainDataPath);

            Vector3 origin = new Vector3(VillageCenter.x - TerrainSize / 2f, 0f, VillageCenter.y - TerrainSize / 2f);
            PaintHeights(terrainData, origin);
            PaintHoles(terrainData, origin);
            PaintAlphamap(terrainData, origin);
            EditorUtility.SetDirty(terrainData);

            GameObject landscapeRoot = GameObject.Find(LandscapeRootName);
            if (landscapeRoot == null)
            {
                landscapeRoot = new GameObject(LandscapeRootName);
                Undo.RegisterCreatedObjectUndo(landscapeRoot, "Generate Dorf 1 Umgebung");
            }

            Transform existing = landscapeRoot.transform.Find(TerrainObjectName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = TerrainObjectName;
            terrainObject.transform.SetParent(landscapeRoot.transform, worldPositionStays: false);
            terrainObject.transform.position = origin;
            Undo.RegisterCreatedObjectUndo(terrainObject, "Generate Dorf 1 Umgebung");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = terrainObject;

            Debug.Log("[Leveldesign] Huegellandschaft erzeugt: " + TerrainSize + "x" + TerrainSize +
                " um Dorf 1. Die Dorfplatte (+/-" + VillageHalfSize + " um " + VillageCenter +
                ") bleibt als Terrain-Hole komplett frei. Ostkorridor (+/-" + CorridorHalfWidth +
                ") Richtung Dorf 2 bleibt flach. Bitte Szene manuell pruefen und speichern.");
        }

        private static void PaintHeights(TerrainData terrainData, Vector3 origin)
        {
            int resolution = terrainData.heightmapResolution;
            var heights = new float[resolution, resolution];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = origin.z + (float)y / (resolution - 1) * TerrainSize;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = origin.x + (float)x / (resolution - 1) * TerrainSize;
                    heights[y, x] = ComputeHeight01(worldX, worldZ);
                }
            }
            terrainData.SetHeights(0, 0, heights);
        }

        private static void PaintHoles(TerrainData terrainData, Vector3 origin)
        {
            int resolution = terrainData.holesResolution;
            var holes = new bool[resolution, resolution];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = origin.z + (float)y / (resolution - 1) * TerrainSize;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = origin.x + (float)x / (resolution - 1) * TerrainSize;
                    bool underVillage = Mathf.Abs(worldX - VillageCenter.x) <= VillageHalfSize + HoleBuffer &&
                                         Mathf.Abs(worldZ - VillageCenter.y) <= VillageHalfSize + HoleBuffer;
                    holes[y, x] = !underVillage; // true = terrain solid; false = hole under the existing plate
                }
            }
            terrainData.SetHoles(0, 0, holes);
        }

        private static void PaintAlphamap(TerrainData terrainData, Vector3 origin)
        {
            int resolution = terrainData.alphamapResolution;
            var map = new float[resolution, resolution, 2];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = origin.z + (float)y / (resolution - 1) * TerrainSize;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = origin.x + (float)x / (resolution - 1) * TerrainSize;
                    float mineDistance = Vector2.Distance(new Vector2(worldX, worldZ), MineRegionCenter);
                    float rockWeight = Smooth01(1f - mineDistance / MineRegionRadius);
                    map[y, x, 1] = rockWeight;
                    map[y, x, 0] = 1f - rockWeight;
                }
            }
            terrainData.SetAlphamaps(0, 0, map);
        }

        private static float ComputeHeight01(float worldX, float worldZ)
        {
            float dx = worldX - VillageCenter.x;
            float dz = worldZ - VillageCenter.y;
            float boxDistance = BoxDistance(dx, dz, VillageHalfSize);

            float ramp = Smooth01((boxDistance - FlatMargin) / ShoulderWidth);
            if (ramp <= 0f)
                return 0f;

            float broad = Fbm(worldX, worldZ, 2, 1f / 280f, 0f, 0f);
            float fine = Fbm(worldX, worldZ, 3, 1f / 70f, 4000f, 4000f);
            float shape = 0.7f * broad + 0.3f * fine;

            float mineDistance = Vector2.Distance(new Vector2(worldX, worldZ), MineRegionCenter);
            float mineBump = Smooth01(1f - mineDistance / MineRegionRadius) * 0.3f;

            float heightUnits = (shape + mineBump) * MaxHillHeight * ramp;

            // East of the village: fade the hills out toward the centreline of the future
            // Dorf-2 corridor so no ridge ever blocks it, while hills stay full-height
            // north/south of the band.
            if (dx > 0f)
            {
                float corridorFactor = Smooth01(1f - Mathf.Abs(dz) / CorridorHalfWidth);
                heightUnits *= 1f - corridorFactor;
            }

            return Mathf.Clamp01(heightUnits / TerrainHeight);
        }

        private static float BoxDistance(float dx, float dz, float halfSize)
        {
            float outsideX = Mathf.Max(Mathf.Abs(dx) - halfSize, 0f);
            float outsideZ = Mathf.Max(Mathf.Abs(dz) - halfSize, 0f);
            return Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
        }

        private static float Fbm(float x, float z, int octaves, float baseFrequency, float offsetX, float offsetZ)
        {
            float sum = 0f, amplitude = 1f, frequency = baseFrequency, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise((x + offsetX) * frequency, (z + offsetZ) * frequency);
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / norm;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static TerrainLayer CreateOrReplaceTerrainLayer(string layerPath, string texturePath, Color color, Vector2 tileSize, int seed)
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath) != null)
                AssetDatabase.DeleteAsset(layerPath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) != null)
                AssetDatabase.DeleteAsset(texturePath);

            Texture2D texture = CreateFlatTexture(color, 64, 0.04f, seed);
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath);
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var layer = new TerrainLayer { diffuseTexture = imported, tileSize = tileSize };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        private static Texture2D CreateFlatTexture(Color baseColor, int size, float jitter, int seed)
        {
            var random = new System.Random(seed);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float noise = ((float)random.NextDouble() - 0.5f) * jitter;
                pixels[i] = new Color(
                    Mathf.Clamp01(baseColor.r + noise),
                    Mathf.Clamp01(baseColor.g + noise),
                    Mathf.Clamp01(baseColor.b + noise), 1f);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
