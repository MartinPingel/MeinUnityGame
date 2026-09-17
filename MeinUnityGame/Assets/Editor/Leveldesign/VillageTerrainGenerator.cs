#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Leveldesign
{
    /// <summary>
    /// Editor-only tool window that builds the rolling hill landscape around the existing
    /// village (Dorf 1). Only ever creates new assets/GameObjects under its own output folder
    /// and "Landschaft" root; it never reads or writes any existing GameObject, component,
    /// material, or script. Re-running replaces only its own previous output.
    ///
    /// Dorf position, size and building height are never hard-coded: every run re-reads them
    /// from "Dorf Boden" and every Renderer inside its footprint in the currently active scene,
    /// so the hill height always follows the real village without a manual slider. Meant to be
    /// run against a separate test scene (e.g. Dorf1_Terrain_Test.unity, a plain copy of
    /// SampleScene.unity) that already contains Dorf 1, so the generated hills can be judged
    /// next to the real church/houses/roads. Refuses to run while SampleScene itself is the
    /// active scene, so that scene can never be touched or saved by this tool.
    ///
    /// The village's own ground ("Dorf Boden") is left completely untouched: a terrain hole is
    /// punched out under its exact footprint so the new terrain never renders or collides there.
    /// </summary>
    public sealed class VillageTerrainGenerator : EditorWindow
    {
        private const string OutputFolder = "Assets/Environment/Terrain";
        private const string TerrainDataPath = OutputFolder + "/Dorf1_Umgebung.asset";
        private const string GrassLayerPath = OutputFolder + "/Wiese.terrainlayer";
        private const string RockLayerPath = OutputFolder + "/Fels.terrainlayer";
        private const string GrassTexturePath = OutputFolder + "/Wiese_Diffuse.png";
        private const string RockTexturePath = OutputFolder + "/Fels_Diffuse.png";

        private const string LandscapeRootName = "Landschaft";
        private const string TerrainObjectName = "Dorf1_Huegellandschaft";

        // The tool must never run against the real village scene - only against a separate
        // test scene (copy) that already contains Dorf 1 for visual comparison.
        private const string ProtectedSceneName = "SampleScene";

        // Ground anchor read live from the active scene every run: world position = Dorfposition,
        // world scale on the built-in 10x10 Plane = Dorfgroesse. Never a hard-coded constant.
        private const string VillageGroundName = "Dorf Boden";
        private const float HoleBuffer = 1.5f; // keeps the hole strictly inside the village plate

        private const float FlatMargin = 40f; // flat grass rim right outside the village plate, unchanged
        // Hills only reach full height this far beyond the flat rim: a wide, gentle climb
        // rather than a short ramp, so a taller hill height never steepens the village edge.
        private const float RiseDistance = 320f;

        // Enlarged so hills this tall still have room to ramp up and roll on for a while
        // before reaching the terrain's outer edge (see RiseDistance).
        private const float TerrainSize = 1400f;
        private const float TerrainHeight = 240f; // vertical range of the terrain asset; headroom only

        // The hill height itself is never entered manually: it is this many times the tallest
        // building/roof found inside the village footprint, clamped to a sane hilly range.
        private const float HillHeightFactor = 4f;
        private const float MinHillHeight = 15f;
        private const float MaxHillHeightLimit = 150f;

        private const int HeightmapResolution = 513;
        private const int AlphamapResolution = 512;

        // South of the village stays open for the future road/corridor and river to Dorf 2.
        private const float CorridorHalfWidth = 90f;

        // Rockier region around the existing mine/smelter placeholders (north-west of the village).
        private static readonly Vector2 MineRegionCenter = new Vector2(-78f, 165f);
        private const float MineRegionRadius = 140f;

        private static readonly Color GrassColor = new Color(0.34f, 0.45f, 0.20f);
        private static readonly Color RockColor = new Color(0.27f, 0.28f, 0.26f); // matches MineRock.mat

        [MenuItem("Tools/Leveldesign/Dorf 1 - Huegellandschaft erzeugen")]
        public static void ShowWindow()
        {
            var window = GetWindow<VillageTerrainGenerator>(true, "Dorf 1 - Huegellandschaft", true);
            window.minSize = new Vector2(420f, 200f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Huegellandschaft um Dorf 1", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Dorfposition, Dorfgroesse und Huegelhoehe werden bei jedem Lauf automatisch aus " +
                "'" + VillageGroundName + "' und den Gebaeuden der aktiven Szene ermittelt - keine " +
                "manuelle Hoeheneingabe. Norden/Osten/Westen werden huegelig, Sueden bleibt offen " +
                "fuer den Weg und spaeteren Fluss nach Dorf 2. Laeuft nur in einer separaten " +
                "Testszene, niemals in '" + ProtectedSceneName + "'.", MessageType.None);
            EditorGUILayout.Space();

            if (TryFindVillageFootprint(out Vector2 center, out Vector2 halfSize, out float groundY))
            {
                float tallest = ComputeTallestBuildingHeight(center, halfSize, groundY);
                float hillHeight = Mathf.Clamp(tallest * HillHeightFactor, MinHillHeight, MaxHillHeightLimit);
                EditorGUILayout.LabelField("Erkannte Dorfposition", $"{center.x:0.0}, {center.y:0.0}");
                EditorGUILayout.LabelField("Erkannte Dorfgroesse (Halbmass)", $"{halfSize.x:0.0} x {halfSize.y:0.0}");
                EditorGUILayout.LabelField("Hoechstes Gebaeude ueber Boden", $"{tallest:0.0}");
                EditorGUILayout.LabelField("Daraus berechnete Huegelhoehe", $"{hillHeight:0.0}");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "'" + VillageGroundName + "' wurde in der aktiven Szene nicht gefunden.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Huegellandschaft erzeugen"))
                Generate();
        }

        private static void Generate()
        {
            if (EditorSceneManager.GetActiveScene().name == ProtectedSceneName)
            {
                Debug.LogError("[Leveldesign] Abgebrochen: aktive Szene ist '" + ProtectedSceneName +
                    "'. Dieses Werkzeug darf sie nicht veraendern. Bitte zuerst eine separate " +
                    "Testszene (z. B. Dorf1_Terrain_Test.unity) oeffnen.");
                return;
            }
            if (!TryFindVillageFootprint(out Vector2 villageCenter, out Vector2 villageHalfSize, out float groundY))
            {
                Debug.LogError("[Leveldesign] Abgebrochen: '" + VillageGroundName + "' wurde in der aktiven " +
                    "Szene nicht gefunden.");
                return;
            }
            float tallestBuilding = ComputeTallestBuildingHeight(villageCenter, villageHalfSize, groundY);
            float maxHillHeight = Mathf.Clamp(tallestBuilding * HillHeightFactor, MinHillHeight, MaxHillHeightLimit);

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

            Vector3 origin = new Vector3(villageCenter.x - TerrainSize / 2f, 0f, villageCenter.y - TerrainSize / 2f);
            PaintHeights(terrainData, origin, villageCenter, villageHalfSize, maxHillHeight);
            PaintHoles(terrainData, origin, villageCenter, villageHalfSize);
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
                " um Dorf 1 (" + villageCenter + ", Halbmass " + villageHalfSize + "). Hoechstes Gebaeude " +
                tallestBuilding + " -> Huegelhoehe " + maxHillHeight + " (automatisch, Faktor " + HillHeightFactor +
                "). Die Dorfplatte bleibt als Terrain-Hole komplett frei. Suedkorridor (+/-" +
                CorridorHalfWidth + ") Richtung Dorf 2 bleibt flach. Bitte Szene manuell pruefen und speichern.");
        }

        // Reads the village anchor live from the active scene every call - never a stored
        // constant - so Dorfposition and Dorfgroesse always match whatever scene is open.
        private static bool TryFindVillageFootprint(out Vector2 center, out Vector2 halfSize, out float groundY)
        {
            GameObject ground = GameObject.Find(VillageGroundName);
            if (ground == null)
            {
                center = default;
                halfSize = default;
                groundY = 0f;
                return false;
            }
            Transform t = ground.transform;
            center = new Vector2(t.position.x, t.position.z);
            // The built-in Unity Plane primitive is a 10x10 unit mesh; world half-size follows
            // whatever scale the village ground actually has, on either axis.
            halfSize = new Vector2(5f * t.lossyScale.x, 5f * t.lossyScale.z);
            groundY = t.position.y;
            return true;
        }

        // Tallest roof/wall found among every Renderer whose bounds fall inside the village
        // footprint - this NPC's own previously generated hills (under LandscapeRootName) are
        // excluded so re-running never feeds back into its own output.
        private static float ComputeTallestBuildingHeight(Vector2 villageCenter, Vector2 villageHalfSize, float groundY)
        {
            float tallest = 0f;
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Renderer renderer in renderers)
            {
                if (renderer.transform.root.name == LandscapeRootName) continue;
                Bounds bounds = renderer.bounds;
                float dx = bounds.center.x - villageCenter.x;
                float dz = bounds.center.z - villageCenter.y;
                if (Mathf.Abs(dx) > villageHalfSize.x || Mathf.Abs(dz) > villageHalfSize.y) continue;
                tallest = Mathf.Max(tallest, bounds.max.y - groundY);
            }
            return tallest;
        }

        private static void PaintHeights(TerrainData terrainData, Vector3 origin, Vector2 villageCenter,
            Vector2 villageHalfSize, float maxHillHeight)
        {
            int resolution = terrainData.heightmapResolution;
            var heights = new float[resolution, resolution];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = origin.z + (float)y / (resolution - 1) * TerrainSize;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = origin.x + (float)x / (resolution - 1) * TerrainSize;
                    heights[y, x] = ComputeHeight01(worldX, worldZ, villageCenter, villageHalfSize, maxHillHeight);
                }
            }
            terrainData.SetHeights(0, 0, heights);
        }

        private static void PaintHoles(TerrainData terrainData, Vector3 origin, Vector2 villageCenter, Vector2 villageHalfSize)
        {
            int resolution = terrainData.holesResolution;
            var holes = new bool[resolution, resolution];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = origin.z + (float)y / (resolution - 1) * TerrainSize;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = origin.x + (float)x / (resolution - 1) * TerrainSize;
                    bool underVillage = Mathf.Abs(worldX - villageCenter.x) <= villageHalfSize.x + HoleBuffer &&
                                         Mathf.Abs(worldZ - villageCenter.y) <= villageHalfSize.y + HoleBuffer;
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

        private static float ComputeHeight01(float worldX, float worldZ, Vector2 villageCenter,
            Vector2 villageHalfSize, float maxHillHeight)
        {
            float dx = worldX - villageCenter.x;
            float dz = worldZ - villageCenter.y;
            float boxDistance = BoxDistance(dx, dz, villageHalfSize);

            float ramp = Smooth01((boxDistance - FlatMargin) / RiseDistance);
            if (ramp <= 0f)
                return 0f;

            float broad = Fbm(worldX, worldZ, 2, 1f / 280f, 0f, 0f);
            float fine = Fbm(worldX, worldZ, 3, 1f / 70f, 4000f, 4000f);
            float shape = 0.7f * broad + 0.3f * fine;

            float mineDistance = Vector2.Distance(new Vector2(worldX, worldZ), MineRegionCenter);
            float mineBump = Smooth01(1f - mineDistance / MineRegionRadius) * 0.3f;

            float heightUnits = (shape + mineBump) * maxHillHeight * ramp;

            // South of the village: fade the hills out toward the centreline of the future
            // Dorf-2 corridor and river so nothing ever blocks it, while hills stay full-height
            // north/east/west of the band.
            if (dz < 0f)
            {
                float corridorFactor = Smooth01(1f - Mathf.Abs(dx) / CorridorHalfWidth);
                heightUnits *= 1f - corridorFactor;
            }

            return Mathf.Clamp01(heightUnits / TerrainHeight);
        }

        private static float BoxDistance(float dx, float dz, Vector2 halfSize)
        {
            float outsideX = Mathf.Max(Mathf.Abs(dx) - halfSize.x, 0f);
            float outsideZ = Mathf.Max(Mathf.Abs(dz) - halfSize.y, 0f);
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
