#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;
using GtaPunta.AI;
using GtaPunta.Atmosphere;
using GtaPunta.Core;
using GtaPunta.Player;
using GtaPunta.UI;
using GtaPunta.Vehicles;
using GtaPunta.Wanted;
using GtaPunta.World;

namespace GtaPunta.Editor
{
    /// <summary>
    /// Deterministic Master Scene Builder for GTA: Punta del Este.
    /// Builds the complete Peninsula of Punta del Este from real OSM data,
    /// terrain heightmap/splatmap, Blender 5.3 FBX models, PBR textures,
    /// custom dual-ocean URP shader, Cinemachine 3, WheelCollider vehicles,
    /// traffic, pedestrians, wanted police, and GTA HUD into Assets/Scenes/PuntaDelEste.unity.
    /// </summary>
    public static class PuntaCityBuilder
    {
        private const string ScenePath = "Assets/Scenes/PuntaDelEste.unity";
        private const string MatDir = "Assets/Art/Materials";
        private const string MeshDir = "Assets/Art/Meshes";
        private const string SettingDir = "Assets/Settings";
        private const string TexDir = "Assets/Art/Textures/PBR";
        private const string ModelDir = "Assets/Art/Models";

        // Simple JSON parser helpers via JsonUtility wrapper? Wait: Unity's JsonUtility
        // does not parse List<float[]> jagged arrays directly! So we parse punta_del_este_map.json
        // using a tiny zero-dependency recursive descent JSON parser right here in Editor!

        [MenuItem("Tools/GTA Punta del Este/Build Complete City Scene")]
        public static void BuildCompleteCityScene()
        {
            Debug.Log("[PuntaCityBuilder] Starting complete Punta del Este world build...");

            EnsureDir(MatDir);
            EnsureDir(MeshDir);
            EnsureDir(SettingDir);
            EnsureDir("Assets/Scenes");

            // 0. Ensure TMP Essential Resources & Quality / URP Baseline
            ConfigureProjectQualityAndUrp();
            ConfigureTextureImporters();

            // 1. Create fresh scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Build Materials Library (all PBR URP Lit + Custom Dual Ocean + Procedural Sky)
            var mats = BuildMaterials();

            // 3. Parse real OpenStreetMap JSON + terrain_meta.json
            var mapRaw = SimpleJson.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Resources/MapData/punta_del_este_map.json"))) as Dictionary<string, object>;
            var tMeta = JsonUtility.FromJson<TerrainMeta>(File.ReadAllText(Path.Combine(Application.dataPath, "Resources/MapData/terrain_meta.json")));

            // 4. Build Native Peninsula Terrain + Splatmap
            Terrain terrain = BuildPeninsulaTerrain(tMeta, mats);

            // 5. Build Dual-Character Ocean (Mansa calm vs Brava surf) + Isla Gorriti on western horizon
            BuildOceanAndIslaGorriti(mats);

            // 6. Create World Containers & RoadNetworkRuntime
            var systemsRoot = new GameObject("--- SYSTEMS ---");
            var roadNetwork = systemsRoot.AddComponent<RoadNetworkRuntime>();

            // 7. Build Real OSM Roads + Sidewalks + Traffic Lane Graph
            BuildRoadsAndGraph(mapRaw, terrain, mats, roadNetwork);

            // 8. Build Real OSM Articulated Buildings + Hero Luxury Oceanfront Towers
            BuildBuildingsAndTowers(mapRaw, terrain, mats);

            // 9. Build Hero Landmarks (Los Dedos, El Faro + Rotating Beam, Iglesia Candelaria, Puerto + Yachts + Sea Lions, Plaza Artigas, Punta de las Salinas Rocks)
            var atmExtras = BuildLandmarksAndPuerto(mapRaw, terrain, mats, roadNetwork);

            // 10. Place Palms, Pines, Streetlamps, Benches, Traffic Lights, Beach Paradores, Lifeguard Towers & Uruguayan Street Signs
            var streetLights = PopulateCoastalPropsAndFlora(mapRaw, terrain, mats, roadNetwork);

            // 11. Build Reusable Vehicle Templates + Spawn Parked Drivable Fleet
            var vehicleTemplates = BuildVehicleTemplatesAndParkedFleet(terrain, mats, out GtaVehicleController starterCar);

            // 12. Build Player (Articulated Blender Humanoid), Cinemachine 3 Cameras, Post-Processing & Atmosphere
            var atmosphere = BuildLightingAtmosphereAndVolume(mats, atmExtras, streetLights);
            var playerFoot = BuildPlayer(terrain, mats, out VehicleInteractionController vehInteract);
            var camRig = BuildCameras(playerFoot, out Camera uiOverlayCam);
            vehInteract.cameraRig = camRig;

            // 13. Build Living City Managers (Traffic, Pedestrians, Wanted Police, Audio, GameManager, HUD)
            var audioMgr = systemsRoot.AddComponent<AudioManager>();

            var trafficGo = new GameObject("TrafficManager");
            trafficGo.transform.SetParent(systemsRoot.transform);
            var trafficMgr = trafficGo.AddComponent<TrafficManager>();
            trafficMgr.roadNetwork = roadNetwork;
            trafficMgr.trafficVehicleTemplates = new List<GameObject>
            {
                vehicleTemplates["CitySedan"],
                vehicleTemplates["BeachSUV"],
                vehicleTemplates["Cabrio"],
                vehicleTemplates["SportsGT"]
            };

            var pedTemplate = BuildPedestrianTemplate();
            var pedGo = new GameObject("PedestrianManager");
            pedGo.transform.SetParent(systemsRoot.transform);
            var pedMgr = pedGo.AddComponent<PedestrianManager>();
            pedMgr.roadNetwork = roadNetwork;
            pedMgr.pedestrianTemplate = pedTemplate;

            var wantedGo = new GameObject("WantedSystem");
            wantedGo.transform.SetParent(systemsRoot.transform);
            var wantedSys = wantedGo.AddComponent<WantedSystem>();
            wantedSys.roadNetwork = roadNetwork;
            wantedSys.policeCruiserTemplate = vehicleTemplates["PoliceCruiser"];

            var hudGo = new GameObject("GTA_HUD_Canvas");
            var hud = hudGo.AddComponent<GtaHudController>();
            hud.uiOverlayCamera = uiOverlayCam;

            var gm = systemsRoot.AddComponent<GameManager>();
            gm.playerFoot = playerFoot;
            gm.vehicleInteraction = vehInteract;
            gm.cameraRig = camRig;
            gm.atmosphere = atmosphere;
            gm.roadNetwork = roadNetwork;
            gm.wantedSystem = wantedSys;
            gm.hud = hud;
            gm.initialPlayerPos = playerFoot.transform.position;
            gm.initialPlayerYaw = playerFoot.transform.eulerAngles.y;

            // 14. Save Scene & Register in Build Settings
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PuntaCityBuilder] SUCCESS! Built {ScenePath} — {roadNetwork.roadNodes.Count} road nodes, {roadNetwork.pedestrianPoints.Count} ped points, {roadNetwork.landmarks.Count} landmarks.");
        }

        private static void EnsureDir(string assetPath)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!Directory.Exists(full)) Directory.CreateDirectory(full);
        }

        private static void ConfigureProjectQualityAndUrp()
        {
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
            var names = QualitySettings.names;
            if (names.Length > 0)
                QualitySettings.SetQualityLevel(names.Length - 1, true);

            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.supportsHDR = true;
                urp.supportsCameraDepthTexture = true;
                urp.supportsCameraOpaqueTexture = true;
                urp.shadowDistance = 240f;
                urp.shadowCascadeCount = 4;
                urp.msaaSampleCount = 4;
                EditorUtility.SetDirty(urp);
            }
        }

        private static void ConfigureTextureImporters()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir, "Assets/Resources/MapData", "Assets/Resources/UI" });
            bool changed = false;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;

                bool dirty = false;
                if (path.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase) || path.Contains("Ocean_Normal_"))
                {
                    if (imp.textureType != TextureImporterType.NormalMap)
                    {
                        imp.textureType = TextureImporterType.NormalMap;
                        dirty = true;
                    }
                }
                if (path.Contains("Palm_Frond_RGBA"))
                {
                    if (!imp.alphaIsTransparency)
                    {
                        imp.alphaIsTransparency = true;
                        dirty = true;
                    }
                }
                if (path.Contains("Peninsula_Splatmap_1024"))
                {
                    if (!imp.isReadable || imp.sRGBTexture)
                    {
                        imp.isReadable = true;
                        imp.sRGBTexture = false;
                        imp.textureCompression = TextureImporterCompression.Uncompressed;
                        dirty = true;
                    }
                }
                if (path.Contains("Minimap_PuntaDelEste"))
                {
                    if (imp.wrapMode != TextureWrapMode.Clamp || imp.maxTextureSize < 1024)
                    {
                        imp.wrapMode = TextureWrapMode.Clamp;
                        imp.maxTextureSize = 1024;
                        imp.mipmapEnabled = false;
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    imp.SaveAndReimport();
                    changed = true;
                }
            }
            if (changed) AssetDatabase.Refresh();
        }

        private static Texture2D LoadTex(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");
        }

        private static Material CreateUrpLitMat(string assetName, Color tint, string albedoTex = null, string normalTex = null, string metallicTex = null, string emissionTex = null, float metallic = 0f, float smoothness = 0.35f, Vector2? tiling = null, bool alphaClip = false)
        {
            string path = $"{MatDir}/{assetName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = lit;
            }

            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);

            if (!string.IsNullOrEmpty(albedoTex))
            {
                var tex = LoadTex(albedoTex);
                if (tex != null) mat.SetTexture("_BaseMap", tex);
            }
            if (!string.IsNullOrEmpty(normalTex))
            {
                var ntex = LoadTex(normalTex);
                if (ntex != null)
                {
                    mat.SetTexture("_BumpMap", ntex);
                    mat.SetFloat("_BumpScale", 1.0f);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }
            if (!string.IsNullOrEmpty(metallicTex))
            {
                var mtex = LoadTex(metallicTex);
                if (mtex != null)
                {
                    mat.SetTexture("_MetallicGlossMap", mtex);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
            if (!string.IsNullOrEmpty(emissionTex))
            {
                var etex = LoadTex(emissionTex);
                if (etex != null)
                {
                    mat.SetTexture("_EmissionMap", etex);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.EnableKeyword("_EMISSION");
                }
            }
            if (tiling.HasValue)
                mat.SetTextureScale("_BaseMap", tiling.Value);

            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.38f);
                mat.SetFloat("_Cull", 0f); // Double-sided palm fronds!
                mat.EnableKeyword("_ALPHATEST_ON");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Dictionary<string, Material> BuildMaterials()
        {
            var m = new Dictionary<string, Material>();

            m["RoadAvenue"]   = CreateUrpLitMat("Mat_RoadAvenue", Color.white, "Road_Avenue_Albedo", "Road_Asphalt_Normal", null, null, 0f, 0.28f);
            m["RoadStreet"]   = CreateUrpLitMat("Mat_RoadStreet", Color.white, "Road_Street_Albedo", "Road_Asphalt_Normal", null, null, 0f, 0.25f);
            m["Sidewalk"]     = CreateUrpLitMat("Mat_Sidewalk", Color.white, "Sidewalk_Rambla_Albedo", "Sidewalk_Rambla_Normal", null, null, 0f, 0.24f);
            m["Seawall"]      = CreateUrpLitMat("Mat_Seawall", Color.white, "Seawall_Granite_Albedo", "Seawall_Granite_Normal", null, null, 0f, 0.20f);
            m["GlassTower"]   = CreateUrpLitMat("Mat_FacadeGlassTower", Color.white, "Facade_GlassTower_Albedo", "Facade_GlassTower_Normal", "Facade_GlassTower_Metallic", "Facade_GlassTower_Emission", 0.45f, 0.85f);
            m["Gorlero"]      = CreateUrpLitMat("Mat_FacadeGorlero", Color.white, "Facade_Gorlero_Albedo", "Facade_Gorlero_Normal", null, "Facade_Gorlero_Emission", 0.05f, 0.32f);
            m["Villa"]        = CreateUrpLitMat("Mat_CoastalVilla", Color.white, "Stucco_Coastal_Albedo", "Stucco_Coastal_Normal", null, "Stucco_Coastal_Emission", 0.02f, 0.25f);
            m["ConcreteRoof"] = CreateUrpLitMat("Mat_ConcreteRoof", Palette.CreamConcrete, "Concrete_Sculpture_Albedo", "Concrete_Sculpture_Normal", null, null, 0f, 0.20f);
            m["Sculpture"]    = CreateUrpLitMat("Mat_LosDedos", Color.white, "Concrete_Sculpture_Albedo", "Concrete_Sculpture_Normal", null, null, 0.02f, 0.22f);
            m["WoodDock"]     = CreateUrpLitMat("Mat_WoodDock", Color.white, "Wood_Dock_Albedo", "Wood_Dock_Normal", null, null, 0f, 0.24f);
            m["RockCoastal"]  = CreateUrpLitMat("Mat_RockCoastal", Color.white, "Rock_Coastal_Albedo", "Rock_Coastal_Normal", null, null, 0.02f, 0.18f);
            m["PalmBark"]     = CreateUrpLitMat("Mat_PalmBark", Color.white, "Palm_Bark_Albedo", "Palm_Bark_Normal", null, null, 0f, 0.15f);
            m["PalmFrond"]    = CreateUrpLitMat("Mat_PalmFrond", Color.white, "Palm_Frond_RGBA", null, null, null, 0f, 0.28f, null, true);
            m["AwningBlue"]   = CreateUrpLitMat("Mat_AwningBlue", Palette.Hex("#1E649B"), null, null, null, null, 0f, 0.22f);
            m["AwningCoral"]  = CreateUrpLitMat("Mat_AwningCoral", Palette.Hex("#C8523B"), null, null, null, null, 0f, 0.22f);
            m["Sand"]         = CreateUrpLitMat("Mat_SandPunta", Color.white, "Sand_Punta_Albedo", "Sand_Punta_Normal", null, null, 0f, 0.14f);
            m["Grass"]        = CreateUrpLitMat("Mat_ParkGrass", Color.white, "Park_Grass_Albedo", "Park_Grass_Normal", null, null, 0f, 0.15f);

            // Signs
            foreach (var sname in new[] { "Sign_Gorlero", "Sign_Rambla", "Sign_Puerto", "Sign_Brava", "Sign_Faro", "Sign_Mansa" })
                m[sname] = CreateUrpLitMat("Mat_" + sname, Color.white, sname, null, null, null, 0.05f, 0.45f);

            // Custom Dual-Character Punta del Este Ocean Material
            string oceanMatPath = $"{MatDir}/Mat_PuntaOcean.mat";
            var oceanMat = AssetDatabase.LoadAssetAtPath<Material>(oceanMatPath);
            Shader oceanShader = Shader.Find("PuntaDelEste/OceanURP");
            if (oceanMat == null)
            {
                oceanMat = new Material(oceanShader);
                AssetDatabase.CreateAsset(oceanMat, oceanMatPath);
            }
            else
            {
                oceanMat.shader = oceanShader;
            }
            oceanMat.SetColor("_MansaShallowColor", Palette.MansaShallow);
            oceanMat.SetColor("_MansaDeepColor", Palette.MansaDeep);
            oceanMat.SetColor("_BravaShallowColor", Palette.BravaShallow);
            oceanMat.SetColor("_BravaDeepColor", Palette.BravaDeep);
            oceanMat.SetColor("_FoamColor", Palette.OceanFoam);
            oceanMat.SetTexture("_NormalMapA", LoadTex("Ocean_Normal_A"));
            oceanMat.SetTexture("_NormalMapB", LoadTex("Ocean_Normal_B"));
            EditorUtility.SetDirty(oceanMat);
            m["Ocean"] = oceanMat;

            // Procedural Coastal Skybox Material
            string skyPath = $"{MatDir}/Mat_PuntaSkybox.mat";
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyMat == null)
            {
                skyMat = new Material(skyShader);
                AssetDatabase.CreateAsset(skyMat, skyPath);
            }
            else
            {
                skyMat.shader = skyShader;
            }
            skyMat.SetFloat("_SunSize", 0.045f);
            skyMat.SetFloat("_SunSizeConvergence", 6f);
            skyMat.SetFloat("_AtmosphereThickness", 0.95f);
            skyMat.SetColor("_SkyTint", Palette.SkyZenithDay);
            skyMat.SetColor("_GroundColor", Palette.SkyHorizonDay);
            skyMat.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(skyMat);
            m["Skybox"] = skyMat;

            return m;
        }

        private static TerrainLayer CreateTerrainLayer(string name, string albedo, string normal, float tileSize)
        {
            string path = $"{MatDir}/TL_{name}.terrainlayer";
            var tl = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (tl == null)
            {
                tl = new TerrainLayer();
                AssetDatabase.CreateAsset(tl, path);
            }
            tl.diffuseTexture = LoadTex(albedo);
            tl.normalMapTexture = LoadTex(normal);
            tl.tileSize = new Vector2(tileSize, tileSize);
            tl.metallic = 0f;
            tl.smoothness = 0.16f;
            EditorUtility.SetDirty(tl);
            return tl;
        }

        private static Terrain BuildPeninsulaTerrain(TerrainMeta meta, Dictionary<string, Material> mats)
        {
            string tdPath = $"{MeshDir}/PeninsulaTerrainData.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null)
            {
                td = new TerrainData();
                AssetDatabase.CreateAsset(td, tdPath);
            }

            int res = meta.heightRes; // 513
            td.heightmapResolution = res;
            td.size = new Vector3(meta.size, meta.maxY - meta.minY, meta.size);

            byte[] rawBytes = File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources/MapData/Peninsula_Heightmap_513.bytes"));
            float[] flat = new float[res * res];
            Buffer.BlockCopy(rawBytes, 0, flat, 0, rawBytes.Length);

            float[,] heights = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z, x] = flat[z * res + x];
            td.SetHeights(0, 0, heights);

            // 4 PBR Terrain Layers: 0=Punta Sand, 1=Rambla Urban Pavement, 2=Park Grass, 3=Coastal Granite Rock
            td.terrainLayers = new[]
            {
                CreateTerrainLayer("SandPunta", "Sand_Punta_Albedo", "Sand_Punta_Normal", 14f),
                CreateTerrainLayer("UrbanPavement", "Sidewalk_Rambla_Albedo", "Sidewalk_Rambla_Normal", 9f),
                CreateTerrainLayer("ParkGrass", "Park_Grass_Albedo", "Park_Grass_Normal", 12f),
                CreateTerrainLayer("CoastalRock", "Rock_Coastal_Albedo", "Rock_Coastal_Normal", 16f),
            };

            // Apply 1024x1024 splatmap cleanly onto alphamapResolution = 512
            var splatTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/MapData/Peninsula_Splatmap_1024.png");
            int aRes = 512;
            td.alphamapResolution = aRes;
            float[,,] alphas = new float[aRes, aRes, 4];
            if (splatTex != null)
            {
                for (int z = 0; z < aRes; z++)
                {
                    float v = z / (float)(aRes - 1);
                    for (int x = 0; x < aRes; x++)
                    {
                        float u = x / (float)(aRes - 1);
                        Color px = splatTex.GetPixelBilinear(u, v);
                        float sum = Mathf.Max(px.r + px.g + px.b + px.a, 0.001f);
                        alphas[z, x, 0] = px.r / sum;
                        alphas[z, x, 1] = px.g / sum;
                        alphas[z, x, 2] = px.b / sum;
                        alphas[z, x, 3] = px.a / sum;
                    }
                }
                td.SetAlphamaps(0, 0, alphas);
            }

            EditorUtility.SetDirty(td);

            var terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Terrain_PuntaDelEste";
            terrainGo.transform.position = new Vector3(meta.minX, meta.minY, meta.minZ);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 1200f;
            var terrainMat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            AssetDatabase.CreateAsset(terrainMat, $"{MatDir}/Mat_TerrainURP.mat");
            terrain.materialTemplate = terrainMat;

            return terrain;
        }

        private static float SampleTerrainY(Terrain t, float x, float z)
        {
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        private static void BuildOceanAndIslaGorriti(Dictionary<string, Material> mats)
        {
            // High-density ocean surface mesh around Punta del Este Peninsula so Gerstner Mansa/Brava waves animate crisply!
            int grid = 150;
            float oceanSpan = 5600f;
            var mesh = new Mesh { name = "PuntaOceanMesh", indexFormat = IndexFormat.UInt32 };
            var verts = new Vector3[(grid + 1) * (grid + 1)];
            var norms = new Vector3[verts.Length];
            var tris = new int[grid * grid * 6];

            for (int z = 0; z <= grid; z++)
            {
                float fz = (z / (float)grid - 0.5f) * oceanSpan;
                for (int x = 0; x <= grid; x++)
                {
                    float fx = (x / (float)grid - 0.5f) * oceanSpan;
                    int idx = z * (grid + 1) + x;
                    verts[idx] = new Vector3(fx, 0f, fz);
                    norms[idx] = Vector3.up;
                }
            }
            int ti = 0;
            for (int z = 0; z < grid; z++)
            {
                for (int x = 0; x < grid; x++)
                {
                    int i0 = z * (grid + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + grid + 1;
                    int i3 = i2 + 1;
                    tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                    tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
                }
            }
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            SaveMeshAsset(mesh, "PuntaOceanMesh");

            var oceanGo = new GameObject("Ocean_MansaBrava");
            oceanGo.layer = LayerMask.NameToLayer("Water");
            oceanGo.transform.position = new Vector3(100f, 0f, 0f);
            oceanGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = oceanGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats["Ocean"];
            mr.shadowCastingMode = ShadowCastingMode.Off;

            // Iconic Isla Gorriti across Playa Mansa Bay (western horizon landmark!)
            var gorriti = new GameObject("Landmark_IslaGorriti");
            gorriti.transform.position = new Vector3(-2050f, -1.5f, 180f);
            var gBeach = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gBeach.name = "Gorriti_SandBase";
            gBeach.transform.SetParent(gorriti.transform, false);
            gBeach.transform.localScale = new Vector3(580f, 18f, 920f);
            gBeach.GetComponent<MeshRenderer>().sharedMaterial = mats["Sand"];

            var gForest = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gForest.name = "Gorriti_PineCrown";
            gForest.transform.SetParent(gorriti.transform, false);
            gForest.transform.localPosition = new Vector3(0f, 5.5f, 0f);
            gForest.transform.localScale = new Vector3(440f, 24f, 760f);
            gForest.GetComponent<MeshRenderer>().sharedMaterial = mats["Grass"];
        }

        private static void BuildRoadsAndGraph(Dictionary<string, object> mapRaw, Terrain terrain, Dictionary<string, Material> mats, RoadNetworkRuntime roadNet)
        {
            var roadsList = mapRaw["roads"] as List<object>;
            if (roadsList == null) return;

            var aveBuilder = new MeshBuilder();
            var strBuilder = new MeshBuilder();
            var swBuilder  = new MeshBuilder();
            var wallBuilder = new MeshBuilder();

            var nodeSnap = new Dictionary<Vector2Int, int>();
            int GetOrAddNode(Vector3 pos, string rname, bool isRambla, bool isAve)
            {
                var key = new Vector2Int(Mathf.RoundToInt(pos.x / 12f), Mathf.RoundToInt(pos.z / 12f));
                if (nodeSnap.TryGetValue(key, out int existing))
                {
                    if (string.IsNullOrEmpty(roadNet.roadNodes[existing].roadName) && !string.IsNullOrEmpty(rname))
                        roadNet.roadNodes[existing].roadName = rname;
                    roadNet.roadNodes[existing].isRambla |= isRambla;
                    return existing;
                }
                int id = roadNet.roadNodes.Count;
                roadNet.roadNodes.Add(new RoadNode
                {
                    position = pos,
                    roadName = rname,
                    isRambla = isRambla,
                    isAvenue = isAve,
                    neighbors = new List<int>()
                });
                nodeSnap[key] = id;
                return id;
            }

            foreach (var item in roadsList)
            {
                var r = item as Dictionary<string, object>;
                if (r == null) continue;
                string rclass = r["class"] as string ?? "street";
                string rname = r["name"] as string ?? "";
                float width = Convert.ToSingle(r["width"]);
                var ptsRaw = r["points"] as List<object>;
                if (ptsRaw == null || ptsRaw.Count < 2) continue;

                bool isRambla = rclass == "rambla";
                bool isAve = rclass == "avenue" || isRambla;
                // Skip unpaved beach service tracks near Los Dedos so golden sand stays clean!
                if (rclass == "service") continue;
                var targetMesh = isAve ? aveBuilder : strBuilder;

                // Convert polyline to world 3D points cleanly draped above terrain
                float yLift = isAve ? 0.24f : 0.16f;
                var pts = new List<Vector3>(ptsRaw.Count);
                foreach (var po in ptsRaw)
                {
                    var pa = po as List<object>;
                    float x = Convert.ToSingle(pa[0]);
                    float z = Convert.ToSingle(pa[1]);
                    if (x < -1020f || x > 1220f || z < -1120f || z > 1120f) continue;
                    float y = Mathf.Max(SampleTerrainY(terrain, x, z), 2.85f) + yLift;
                    pts.Add(new Vector3(x, y, z));
                }
                if (pts.Count < 2) continue;

                // Subdivide every 10m so roads hug terrain contour cleanly with zero clipping!
                var dense = SubdividePolyline(pts, terrain, yLift, 10f);

                // Extrude road ribbon (CLOCKWISE from above -> +Y upward normal!) + raised sidewalks + Rambla stone seawall
                float halfW = width * 0.5f;
                float swW = isRambla ? 3.4f : (isAve ? 2.6f : 1.7f);
                float vDist = 0f;

                int prevNode = -1;
                for (int i = 0; i < dense.Count - 1; i++)
                {
                    Vector3 a = dense[i];
                    Vector3 b = dense[i + 1];
                    Vector3 seg = b - a;
                    float len = seg.magnitude;
                    if (len < 0.4f) continue;

                    Vector3 dir = new Vector3(seg.x, 0f, seg.z).normalized;
                    Vector3 right = new Vector3(dir.z, 0f, -dir.x);

                    float v0 = vDist / 12f;
                    float v1 = (vDist + len) / 12f;
                    vDist += len;

                    // Road asphalt quad (strictly +Y upward facing in Unity left-handed coords!)
                    targetMesh.AddQuadUp(
                        a - right * halfW, b - right * halfW,
                        b + right * halfW, a + right * halfW,
                        new Vector2(0, v0), new Vector2(0, v1), new Vector2(1, v1), new Vector2(1, v0));

                    // Populate connected drivable road graph first so we know intersection nodes cleanly
                    int nA = GetOrAddNode(a, rname, isRambla, isAve);
                    int nB = GetOrAddNode(b, rname, isRambla, isAve);
                    if (nA != nB)
                    {
                        if (!roadNet.roadNodes[nA].neighbors.Contains(nB)) roadNet.roadNodes[nA].neighbors.Add(nB);
                        if (!roadNet.roadNodes[nB].neighbors.Contains(nA)) roadNet.roadNodes[nB].neighbors.Add(nA);
                    }
                    if (prevNode >= 0 && prevNode != nA)
                    {
                        if (!roadNet.roadNodes[prevNode].neighbors.Contains(nA)) roadNet.roadNodes[prevNode].neighbors.Add(nA);
                        if (!roadNet.roadNodes[nA].neighbors.Contains(prevNode)) roadNet.roadNodes[nA].neighbors.Add(prevNode);
                    }
                    prevNode = nB;

                    // Raised sidewalks slightly below asphalt edges so intersections NEVER get barred by sidewalk strips!
                    bool nearIntersection = i == 0 || i == dense.Count - 2 ||
                                            roadNet.roadNodes[nA].neighbors.Count >= 3 ||
                                            roadNet.roadNodes[nB].neighbors.Count >= 3;
                    float swY = nearIntersection ? -0.05f : 0.05f;
                    swBuilder.AddQuadUp(
                        a - right * (halfW + swW) + Vector3.up * swY, b - right * (halfW + swW) + Vector3.up * swY,
                        b - right * halfW + Vector3.up * swY, a - right * halfW + Vector3.up * swY,
                        new Vector2(0, v0 * 2), new Vector2(0, v1 * 2), new Vector2(1, v1 * 2), new Vector2(1, v0 * 2));
                    swBuilder.AddQuadUp(
                        a + right * halfW + Vector3.up * swY, b + right * halfW + Vector3.up * swY,
                        b + right * (halfW + swW) + Vector3.up * swY, a + right * (halfW + swW) + Vector3.up * swY,
                        new Vector2(0, v0 * 2), new Vector2(0, v1 * 2), new Vector2(1, v1 * 2), new Vector2(1, v0 * 2));

                    // Stone curb / Rambla seawall skirts only on clean outer road runs (never across intersections!)
                    if (!nearIntersection)
                    {
                        float drop = isRambla ? 1.85f : 0.55f;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 outerA = a + right * (side * (halfW + swW));
                            Vector3 outerB = b + right * (side * (halfW + swW));
                            wallBuilder.AddQuadDoubleSided(
                                outerA + Vector3.up * swY, outerB + Vector3.up * swY,
                                outerB - Vector3.up * drop, outerA - Vector3.up * drop,
                                new Vector2(v0 * 2, 1), new Vector2(v1 * 2, 1), new Vector2(v1 * 2, 0), new Vector2(v0 * 2, 0));
                        }
                    }

                    // Collect sidewalk pedestrian stroll points along Gorlero, Calle 20 & Rambla
                    if (i % 3 == 0 && (isAve || roadNet.pedestrianPoints.Count < 140))
                    {
                        Vector3 pedPt = a + right * (halfW + swW * 0.55f) + Vector3.up * 0.25f;
                        roadNet.pedestrianPoints.Add(pedPt);
                    }
                }
            }

            var roadsRoot = new GameObject("City_RoadNetwork");
            CreateMeshChild(roadsRoot.transform, "Roads_Avenues_Rambla", aveBuilder.Build("Mesh_RoadsAvenue"), mats["RoadAvenue"], true);
            CreateMeshChild(roadsRoot.transform, "Roads_PeninsulaStreets", strBuilder.Build("Mesh_RoadsStreet"), mats["RoadStreet"], true);
            CreateMeshChild(roadsRoot.transform, "Sidewalks_Promenade", swBuilder.Build("Mesh_Sidewalks"), mats["Sidewalk"], true);
            CreateMeshChild(roadsRoot.transform, "Rambla_Seawalls", wallBuilder.Build("Mesh_Seawalls"), mats["Seawall"], true);
        }

        private static List<Vector3> SubdividePolyline(List<Vector3> pts, Terrain terrain, float yOffset, float maxStep)
        {
            var res = new List<Vector3> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i];
                Vector3 b = pts[i + 1];
                float d = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                int steps = Mathf.Max(1, Mathf.CeilToInt(d / maxStep));
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float x = Mathf.Lerp(a.x, b.x, t);
                    float z = Mathf.Lerp(a.z, b.z, t);
                    float y = Mathf.Max(SampleTerrainY(terrain, x, z), 2.85f) + yOffset;
                    res.Add(new Vector3(x, y, z));
                }
            }
            return res;
        }

        private static void BuildBuildingsAndTowers(Dictionary<string, object> mapRaw, Terrain terrain, Dictionary<string, Material> mats)
        {
            var bList = mapRaw["buildings"] as List<object>;
            if (bList == null) return;

            var glassWalls = new MeshBuilder();
            var gorleroWalls = new MeshBuilder();
            var villaWalls = new MeshBuilder();
            var roofsAndCornices = new MeshBuilder();
            var awningsBlue = new MeshBuilder();
            var awningsCoral = new MeshBuilder();

            int count = 0;
            foreach (var item in bList)
            {
                var b = item as Dictionary<string, object>;
                if (b == null) continue;
                var ptsRaw = b["points"] as List<object>;
                if (ptsRaw == null || ptsRaw.Count < 3) continue;

                float height = Convert.ToSingle(b["height"]);
                string style = b["style"] as string ?? "coastal_villa";
                var centerRaw = b["center"] as List<object>;
                float cx = Convert.ToSingle(centerRaw[0]);
                float cz = Convert.ToSingle(centerRaw[1]);

                if (cx < -1020f || cx > 1220f || cz < -1120f || cz > 1120f) continue;

                var poly = new List<Vector2>(ptsRaw.Count);
                foreach (var po in ptsRaw)
                {
                    var pa = po as List<object>;
                    poly.Add(new Vector2(Convert.ToSingle(pa[0]), Convert.ToSingle(pa[1])));
                }
                // Ensure consistent CCW winding for outward-facing normals
                if (SignedPolygonArea(poly) < 0f) poly.Reverse();

                float baseY = SampleTerrainY(terrain, cx, cz) - 1.2f; // sunk slightly into ground so zero slope gap!
                float topY = baseY + height + 1.2f;

                var wallTarget = style == "glass_tower" ? glassWalls : (style == "coastal_villa" ? villaWalls : gorleroWalls);
                float uScale = style == "glass_tower" ? 0.055f : 0.085f;
                float vScale = style == "glass_tower" ? 0.038f : 0.065f;

                // Extrude perimeter walls
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 p0 = poly[i];
                    Vector2 p1 = poly[(i + 1) % poly.Count];
                    float edgeLen = Vector2.Distance(p0, p1);
                    if (edgeLen < 0.4f) continue;

                    Vector3 b0 = new Vector3(p0.x, baseY, p0.y);
                    Vector3 b1 = new Vector3(p1.x, baseY, p1.y);
                    Vector3 t1 = new Vector3(p1.x, topY, p1.y);
                    Vector3 t0 = new Vector3(p0.x, topY, p0.y);

                    // Extrude perimeter walls with ENFORCED OUTWARD-FACING NORMALS (solid volumetric buildings!)
                    Vector2 ed = (p1 - p0).normalized;
                    Vector3 outward = new Vector3(ed.y, 0f, -ed.x);
                    wallTarget.AddQuadFacing(b0, b1, t1, t0,
                        new Vector2(0, 0), new Vector2(edgeLen * uScale, 0),
                        new Vector2(edgeLen * uScale, height * vScale), new Vector2(0, height * vScale),
                        outward);

                    // Architectural roof cornice overhang rim (solid outward-facing!)
                    float overhang = style == "glass_tower" ? 0.65f : 0.35f;
                    Vector3 c0 = t0 + outward * overhang;
                    Vector3 c1 = t1 + outward * overhang;
                    Vector3 c0u = c0 + Vector3.up * 0.55f;
                    Vector3 c1u = c1 + Vector3.up * 0.55f;
                    roofsAndCornices.AddQuadFacing(c0, c1, c1u, c0u, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, outward);

                    // Striped street-level café/boutique awnings along commercial Av. Gorlero & Calle 20 buildings!
                    if (style == "midrise_commercial" && edgeLen > 5.5f && i == 0 && (count % 2 == 0))
                    {
                        float awnY = baseY + 4.4f;
                        var awTarget = (count % 4 == 0) ? awningsBlue : awningsCoral;
                        Vector3 a0 = new Vector3(p0.x, awnY + 0.5f, p0.y) + outward * 0.1f;
                        Vector3 a1 = new Vector3(p1.x, awnY + 0.5f, p1.y) + outward * 0.1f;
                        Vector3 a0o = new Vector3(p0.x, awnY - 0.15f, p0.y) + outward * 1.9f;
                        Vector3 a1o = new Vector3(p1.x, awnY - 0.15f, p1.y) + outward * 1.9f;
                        awTarget.AddQuad(a0, a1, a1o, a0o, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                        awTarget.AddQuad(a1, a0, a0o, a1o, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                }

                // Roof cap fan (enforced +Y upward facing normal!)
                Vector3 roofCenter = new Vector3(cx, topY + 0.55f, cz);
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 p0 = poly[i];
                    Vector2 p1 = poly[(i + 1) % poly.Count];
                    roofsAndCornices.AddTriUp(
                        roofCenter,
                        new Vector3(p0.x, topY + 0.55f, p0.y),
                        new Vector3(p1.x, topY + 0.55f, p1.y));
                }

                // Rooftop mechanical penthouse on taller towers for authentic skyline detail!
                if (height >= 18f)
                {
                    AddBox(roofsAndCornices, new Vector3(cx, topY + 1.85f, cz), new Vector3(5.2f, 2.6f, 4.4f));
                }

                count++;
            }

            var bRoot = new GameObject("City_Architecture");
            CreateMeshChild(bRoot.transform, "Buildings_GlassTowers", glassWalls.Build("Mesh_BldGlass"), mats["GlassTower"], true);
            CreateMeshChild(bRoot.transform, "Buildings_GorleroCommercial", gorleroWalls.Build("Mesh_BldGorlero"), mats["Gorlero"], true);
            CreateMeshChild(bRoot.transform, "Buildings_CoastalVillas", villaWalls.Build("Mesh_BldVilla"), mats["Villa"], true);
            CreateMeshChild(bRoot.transform, "Buildings_RoofsAndCornices", roofsAndCornices.Build("Mesh_BldRoofs"), mats["ConcreteRoof"], true);
            CreateMeshChild(bRoot.transform, "Buildings_AwningsBlue", awningsBlue.Build("Mesh_AwningsBlue"), mats["AwningBlue"], false);
            CreateMeshChild(bRoot.transform, "Buildings_AwningsCoral", awningsCoral.Build("Mesh_AwningsCoral"), mats["AwningCoral"], false);

            // Place iconic sculpted Blender Luxury Coastal Skyscrapers (Trump/Imperial/Le Parc silhouette!)
            // framing Parada 1-3 Playa Brava and Playa Mansa!
            var curvedTower = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Tower_CurvedGlass.fbx");
            var terraceTower = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Tower_TwinTerrace.fbx");

            Vector4[] heroTowerSpecs =
            {
                // (x, z, yaw, scaleY) — Mansa & Brava iconic beachfront skyline corridor!
                new Vector4( 620f,  730f,  32f, 1.15f),
                new Vector4( 715f,  790f,  48f, 1.05f),
                new Vector4( 825f,  845f,  35f, 1.25f),
                new Vector4( 945f,  910f,  42f, 1.10f),
                new Vector4( 490f,  690f,  15f, 0.95f),
                new Vector4( 395f,  845f, -12f, 1.20f), // Rambla Mansa waterfront towers
                new Vector4( 365f,  960f, -18f, 1.08f),
                new Vector4( 415f, 1065f, -10f, 1.18f),
                new Vector4( 535f,  940f,  20f, 1.02f),
                new Vector4( 680f,  965f,  30f, 1.12f),
                new Vector4( 510f,  420f,  38f, 0.88f), // Near Parada 1 Brava
                new Vector4( 425f,  330f,  35f, 0.82f),
            };

            for (int i = 0; i < heroTowerSpecs.Length; i++)
            {
                var sp = heroTowerSpecs[i];
                var prefab = (i % 2 == 0) ? curvedTower : terraceTower;
                if (prefab == null) continue;
                float y = SampleTerrainY(terrain, sp.x, sp.y) - 0.6f;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = $"HeroTower_Punta_{i:00}";
                inst.transform.SetParent(bRoot.transform);
                inst.transform.position = new Vector3(sp.x, y, sp.y);
                inst.transform.rotation = Quaternion.Euler(0f, sp.z, 0f);
                inst.transform.localScale = new Vector3(1.05f, sp.w, 1.05f);
                UpgradeFbxMaterialsToUrp(inst, mats);
                AddBoxColliderFromBounds(inst);
            }
        }

        private struct AtmosphereExtras
        {
            public Transform faroRotor;
            public Light faroSpot;
            public Renderer faroBeamCone;
        }

        private static AtmosphereExtras BuildLandmarksAndPuerto(Dictionary<string, object> mapRaw, Terrain terrain, Dictionary<string, Material> mats, RoadNetworkRuntime roadNet)
        {
            var extras = new AtmosphereExtras();
            var lmRoot = new GameObject("City_IconicLandmarks");

            var lmList = mapRaw["landmarks"] as List<object>;
            if (lmList != null)
            {
                foreach (var lo in lmList)
                {
                    var d = lo as Dictionary<string, object>;
                    float x = Convert.ToSingle(d["x"]);
                    float z = Convert.ToSingle(d["z"]);
                    float yaw = Convert.ToSingle(d["yaw"]);
                    float y = SampleTerrainY(terrain, x, z);
                    roadNet.landmarks.Add(new LandmarkRuntimeInfo
                    {
                        id = d["id"] as string,
                        displayName = d["name"] as string,
                        district = d["district"] as string,
                        position = new Vector3(x, y, z),
                        teleportYaw = yaw,
                        discovered = false
                    });
                }
            }

            // 1. LOS DEDOS / LA MANO DE PUNTA DEL ESTE (Playa Brava Parada 1 — exact OSM 794.6, 511.3!)
            {
                float x = 794.6f, z = 511.3f;
                float y = Mathf.Max(SampleTerrainY(terrain, x, z), 1.6f);
                var dedos = SpawnModel("LaMano_LosDedos", new Vector3(x, y - 0.05f, z), Quaternion.Euler(0f, 132f, 0f), Vector3.one * 1.35f, lmRoot.transform, mats, true);
                if (dedos != null)
                {
                    foreach (var r in dedos.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (r.name.Contains("Sand")) r.sharedMaterial = mats["Sand"];
                        else r.sharedMaterial = mats["Sculpture"];
                    }
                }
                // Wooden beach boardwalk (pasarela) from Rambla down onto the sand beside Los Dedos
                var pasarela = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pasarela.name = "Pasarela_LosDedos";
                pasarela.transform.SetParent(lmRoot.transform);
                pasarela.transform.position = new Vector3(772f, y + 0.12f, 522f);
                pasarela.transform.rotation = Quaternion.Euler(0f, 118f, 0f);
                pasarela.transform.localScale = new Vector3(3.2f, 0.25f, 34f);
                pasarela.GetComponent<MeshRenderer>().sharedMaterial = mats["WoodDock"];

                // Beach Parador, Lifeguard Tower & Umbrellas framing Playa Brava right near Los Dedos!
                SpawnModel("Beach_LifeguardTower", new Vector3(818f, SampleTerrainY(terrain, 818f, 492f), 492f), Quaternion.Euler(0f, 125f, 0f), Vector3.one, lmRoot.transform, mats, true);
                SpawnModel("Beach_Parador", new Vector3(755f, SampleTerrainY(terrain, 755f, 470f), 470f), Quaternion.Euler(0f, 128f, 0f), Vector3.one, lmRoot.transform, mats, true);
                for (int u = 0; u < 8; u++)
                {
                    float ux = 768f + u * 11f;
                    float uz = 482f + u * 7f - (u % 2) * 14f;
                    float uy = SampleTerrainY(terrain, ux, uz);
                    if (uy > 0.7f)
                        SpawnModel("Beach_UmbrellaSet", new Vector3(ux, uy, uz), Quaternion.Euler(0f, 125f + u * 11f, 0f), Vector3.one, lmRoot.transform, mats, false);
                }
                roadNet.pedestrianPoints.Add(new Vector3(786f, y + 0.2f, 514f));
                roadNet.pedestrianPoints.Add(new Vector3(802f, y + 0.2f, 504f));
            }

            // 2. FARO DE PUNTA DEL ESTE (exact OSM -514.8, -705.6) + Rotating Lighthouse Beam!
            {
                float x = -514.8f, z = -705.6f;
                float y = SampleTerrainY(terrain, x, z);
                var faro = SpawnModel("Faro_PuntaDelEste", new Vector3(x, y - 0.3f, z), Quaternion.Euler(0f, 20f, 0f), Vector3.one, lmRoot.transform, mats, true);

                // Rotating volumetric beam rig inside the lantern room (41.8m up!)
                var rotor = new GameObject("Faro_BeaconRotor");
                rotor.transform.SetParent(lmRoot.transform);
                rotor.transform.position = new Vector3(x, y + 41.5f, z);

                var spotGo = new GameObject("Faro_Spotlight");
                spotGo.transform.SetParent(rotor.transform, false);
                spotGo.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                var spot = spotGo.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = Palette.SunAfternoon;
                spot.intensity = 1600f;
                spot.range = 320f;
                spot.spotAngle = 24f;

                // Translucent volumetric beam cone softly flaring from the lantern at night!
                var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cone.name = "Faro_VolumetricBeam";
                UnityEngine.Object.DestroyImmediate(cone.GetComponent<Collider>());
                cone.transform.SetParent(rotor.transform, false);
                cone.transform.localPosition = new Vector3(0f, -4f, 52f);
                cone.transform.localRotation = Quaternion.Euler(95f, 0f, 0f);
                cone.transform.localScale = new Vector3(4.2f, 52f, 4.2f);
                var beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                beamMat.color = new Color(1.0f, 0.94f, 0.72f, 0.085f);
                beamMat.SetFloat("_Surface", 1f);
                beamMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                beamMat.SetInt("_DstBlend", (int)BlendMode.One);
                beamMat.SetInt("_ZWrite", 0);
                beamMat.renderQueue = 3100;
                AssetDatabase.CreateAsset(beamMat, $"{MatDir}/Mat_FaroBeam.mat");
                var cmr = cone.GetComponent<MeshRenderer>();
                cmr.sharedMaterial = beamMat;
                cmr.shadowCastingMode = ShadowCastingMode.Off;

                extras.faroRotor = rotor.transform;
                extras.faroSpot = spot;
                extras.faroBeamCone = cmr;
            }

            // 3. IGLESIA DE LA CANDELARIA (exact OSM -412.9, -714.6)
            {
                float x = -412.9f, z = -714.6f;
                float y = SampleTerrainY(terrain, x, z);
                SpawnModel("Iglesia_Candelaria", new Vector3(x, y - 0.25f, z), Quaternion.Euler(0f, -42f, 0f), Vector3.one, lmRoot.transform, mats, true);
            }

            // 4. PUERTO DE PUNTA DEL ESTE (Real OSM piers/escollera + Yacht Club + 26 Boats + Sea Lions!)
            {
                var puertoRoot = new GameObject("Landmark_PuertoPuntaDelEste");
                puertoRoot.transform.SetParent(lmRoot.transform);

                // Yacht Club Punta del Este at exact OSM (-403.6, -236.5)
                float ycY = SampleTerrainY(terrain, -403.6f, -236.5f);
                SpawnModel("Puerto_YachtClub", new Vector3(-403.6f, ycY - 0.2f, -236.5f), Quaternion.Euler(0f, -55f, 0f), Vector3.one, puertoRoot.transform, mats, true);

                // Extrude real OSM pier & breakwater lines into solid walkable docks (+2.1m above sea level!)
                var pierMesh = new MeshBuilder();
                var piersList = mapRaw["piers"] as List<object>;
                if (piersList != null)
                {
                    foreach (var po in piersList)
                    {
                        var pd = po as Dictionary<string, object>;
                        var ppts = pd["points"] as List<object>;
                        if (ppts == null || ppts.Count < 2) continue;

                        for (int i = 0; i < ppts.Count - 1; i++)
                        {
                            var a2 = ppts[i] as List<object>;
                            var b2 = ppts[i + 1] as List<object>;
                            Vector3 a = new Vector3(Convert.ToSingle(a2[0]), 2.15f, Convert.ToSingle(a2[1]));
                            Vector3 b = new Vector3(Convert.ToSingle(b2[0]), 2.15f, Convert.ToSingle(b2[1]));
                            if (Vector3.Distance(a, b) < 1.5f) continue;

                            Vector3 dir = (b - a).normalized;
                            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                            float halfW = 4.2f;
                            AddRibbonBox(pierMesh, a, b, right * halfW, 3.8f);
                        }
                    }
                }

                // Also add clean iconic finger piers extending NW into the sheltered harbor bay!
                Vector3[][] fingerPiers =
                {
                    new[] { new Vector3(-430f, 2.0f, -155f), new Vector3(-475f, 2.0f, -25f) },
                    new[] { new Vector3(-378f, 2.0f, -158f), new Vector3(-418f, 2.0f, -38f) },
                    new[] { new Vector3(-326f, 2.0f, -162f), new Vector3(-360f, 2.0f, -55f) },
                    new[] { new Vector3(-495f, 2.4f, -180f), new Vector3(-548f, 2.4f, 185f) }, // Main Escollera Breakwater!
                };
                foreach (var fp in fingerPiers)
                {
                    Vector3 dir = (fp[1] - fp[0]).normalized;
                    Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                    AddRibbonBox(pierMesh, fp[0], fp[1], right * 3.8f, 3.8f);
                    roadNet.pedestrianPoints.Add(Vector3.Lerp(fp[0], fp[1], 0.45f) + Vector3.up * 0.2f);
                }
                CreateMeshChild(puertoRoot.transform, "Puerto_PiersAndEscollera", pierMesh.Build("Mesh_PuertoPiers"), mats["WoodDock"], true);

                // Moored Luxury Yachts, Sailboats & Artisanal Fishing Boats gently bobbing in the harbor & Playa Mansa bay!
                string[] boatModels = { "Boat_LuxuryYacht", "Boat_Sailboat", "Boat_FishingBoat" };
                Vector3[] boatSpots =
                {
                    // Inside Puerto slips
                    new Vector3(-455f, 0.0f, -95f),  new Vector3(-442f, 0.0f, -62f),  new Vector3(-430f, 0.0f, -32f),
                    new Vector3(-396f, 0.0f, -108f), new Vector3(-384f, 0.0f, -72f),  new Vector3(-372f, 0.0f, -40f),
                    new Vector3(-344f, 0.0f, -112f), new Vector3(-332f, 0.0f, -78f),  new Vector3(-496f, 0.0f, -55f),
                    new Vector3(-508f, 0.0f,  15f),  new Vector3(-492f, 0.0f,  68f),  new Vector3(-478f, 0.0f, 120f),
                    // Anchored out across calm Playa Mansa bay facing Isla Gorriti!
                    new Vector3(-620f, 0.0f,  95f),  new Vector3(-590f, 0.0f, 240f),  new Vector3(-460f, 0.0f, 345f),
                    new Vector3(-340f, 0.0f, 430f),  new Vector3(-240f, 0.0f, 540f),  new Vector3(-165f, 0.0f, 680f),
                    new Vector3(-710f, 0.0f, -85f),  new Vector3(-745f, 0.0f, 260f),  new Vector3(-540f, 0.0f, 520f),
                    new Vector3(-390f, 0.0f, 760f),  new Vector3(-210f, 0.0f, 890f),  new Vector3(-660f, 0.0f, 410f),
                };

                for (int i = 0; i < boatSpots.Length; i++)
                {
                    string bModel = boatModels[i % boatModels.Length];
                    float yaw = (i < 12) ? -68f + (i % 3) * 6f : (i * 47f) % 360f;
                    var boat = SpawnModel(bModel, boatSpots[i], Quaternion.Euler(0f, yaw, 0f), Vector3.one * 1.15f, puertoRoot.transform, mats, false);
                    if (boat != null)
                    {
                        var bob = boat.AddComponent<FloatingBoatBob>();
                        bob.bobAmplitude = (i < 12) ? 0.09f : 0.16f;
                        bob.phaseOffset = i * 0.9f;
                    }
                }

                // Lounging Sea Lions (Lobos Marinos) on the wooden Puerto piers!
                Vector3[] sealionSpots =
                {
                    new Vector3(-448f, 2.2f, -104f),
                    new Vector3(-452f, 2.2f, -94f),
                    new Vector3(-395f, 2.2f, -110f),
                    new Vector3(-518f, 2.5f, -45f),
                };
                for (int i = 0; i < sealionSpots.Length; i++)
                    SpawnModel("SeaLion_Puerto", sealionSpots[i], Quaternion.Euler(0f, i * 85f, 0f), Vector3.one * 1.1f, puertoRoot.transform, mats, false);
            }

            // 5. PLAZA ARTIGAS (Feria de Artesanos on Av. Gorlero — exact OSM 207.6, -77.8)
            {
                var plazaRoot = new GameObject("Landmark_PlazaArtigas");
                plazaRoot.transform.SetParent(lmRoot.transform);
                float px = 207.6f, pz = -77.8f;
                for (int row = -1; row <= 1; row++)
                {
                    for (int col = -1; col <= 1; col++)
                    {
                        if (row == 0 && col == 0) continue;
                        float sx = px + col * 11f;
                        float sz = pz + row * 11f;
                        float sy = SampleTerrainY(terrain, sx, sz);
                        SpawnModel("PlazaArtigas_Stall", new Vector3(sx, sy, sz), Quaternion.Euler(0f, 45f, 0f), Vector3.one, plazaRoot.transform, mats, true);
                    }
                }
                roadNet.pedestrianPoints.Add(new Vector3(px, SampleTerrainY(terrain, px, pz) + 0.2f, pz));
            }

            // 6. PUNTA DE LAS SALINAS ROCKY SOUTHERN TIP (Coastal Granite Boulders meeting the ocean!)
            {
                var rockRoot = new GameObject("Landmark_PuntaSalinasRocks");
                rockRoot.transform.SetParent(lmRoot.transform);
                var rng = new System.Random(777);
                for (int i = 0; i < 38; i++)
                {
                    float ang = Mathf.Lerp(-165f, 15f, i / 37f) * Mathf.Deg2Rad;
                    float rad = 95f + (float)rng.NextDouble() * 55f;
                    float rx = -505f + Mathf.Cos(ang) * rad;
                    float rz = -940f + Mathf.Sin(ang) * rad * 0.75f;
                    float ry = SampleTerrainY(terrain, rx, rz);
                    if (ry > -2.4f && ry < 3.6f)
                    {
                        float sc = 0.85f + (float)rng.NextDouble() * 1.45f;
                        var rock = SpawnModel("Prop_CoastalBoulder", new Vector3(rx, ry - 0.35f, rz),
                            Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * sc, rockRoot.transform, mats, true);
                        if (rock != null)
                            foreach (var mr in rock.GetComponentsInChildren<MeshRenderer>()) mr.sharedMaterial = mats["RockCoastal"];
                    }
                }
            }

            return extras;
        }

        private static List<Light> PopulateCoastalPropsAndFlora(Dictionary<string, object> mapRaw, Terrain terrain, Dictionary<string, Material> mats, RoadNetworkRuntime roadNet)
        {
            var propsRoot = new GameObject("City_CoastalFloraAndFurniture");
            var streetLights = new List<Light>();
            var rng = new System.Random(2026);

            // Place Palm Trees, Rambla Curved Streetlamps, Benches & Traffic Lights along real OSM Rambla & Av. Gorlero nodes!
            int palmCount = 0, lampCount = 0, benchCount = 0, tlCount = 0;
            for (int i = 0; i < roadNet.roadNodes.Count; i++)
            {
                var node = roadNet.roadNodes[i];
                if (!node.isRambla && !node.isAvenue) continue;
                if (node.neighbors.Count == 0) continue;

                Vector3 dir = (roadNet.roadNodes[node.neighbors[0]].position - node.position);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.2f) continue;
                dir.Normalize();
                Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

                // Palm trees lining the Rambla & Av. Gorlero every ~2-3 nodes
                if (i % 2 == 0 && palmCount < 230)
                {
                    float side = (i % 4 == 0) ? 1f : -1f;
                    Vector3 pPos = node.position + right * (side * 8.4f);
                    pPos.y = SampleTerrainY(terrain, pPos.x, pPos.z) - 0.15f;
                    if (pPos.y > 1.2f)
                    {
                        string pm = (palmCount % 2 == 0) ? "Palm_Phoenix_A" : "Palm_Phoenix_B";
                        float sc = 0.85f + (float)rng.NextDouble() * 0.35f;
                        var palm = SpawnModel(pm, pPos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * sc, propsRoot.transform, mats, false);
                        ApplyPalmMaterials(palm, mats);
                        palmCount++;
                    }
                }

                // Rambla Streetlamps every 4th avenue/rambla node
                if (i % 4 == 1 && lampCount < 85)
                {
                    Vector3 lPos = node.position + right * 7.4f;
                    lPos.y = SampleTerrainY(terrain, lPos.x, lPos.z);
                    if (lPos.y > 1.8f)
                    {
                        var lamp = SpawnModel("Prop_RamblaLamp", lPos, Quaternion.Euler(0f, yaw - 90f, 0f), Vector3.one, propsRoot.transform, mats, false);
                        // Add real-time point light on every 3rd lamp (warm golden coastal night glow while keeping RTX 3050 fast!)
                        if (lamp != null && lampCount % 3 == 0 && streetLights.Count < 28)
                        {
                            var plGo = new GameObject("LampLight");
                            plGo.transform.SetParent(lamp.transform, false);
                            plGo.transform.localPosition = new Vector3(0f, 7.4f, 1.6f);
                            var pl = plGo.AddComponent<Light>();
                            pl.type = LightType.Point;
                            pl.color = Palette.Hex("#FFD89B");
                            pl.intensity = 28f;
                            pl.range = 24f;
                            pl.shadows = LightShadows.None;
                            pl.enabled = false;
                            streetLights.Add(pl);
                        }
                        lampCount++;
                    }
                }

                // Coastal Benches on Rambla promenade
                if (node.isRambla && i % 6 == 2 && benchCount < 38)
                {
                    Vector3 bPos = node.position - right * 8.2f;
                    bPos.y = SampleTerrainY(terrain, bPos.x, bPos.z);
                    if (bPos.y > 2.2f)
                    {
                        SpawnModel("Prop_Bench", bPos, Quaternion.Euler(0f, yaw + 90f, 0f), Vector3.one, propsRoot.transform, mats, false);
                        benchCount++;
                    }
                }

                // Traffic Lights at major intersections (3+ neighbors)
                if (node.neighbors.Count >= 3 && i % 5 == 0 && tlCount < 16)
                {
                    Vector3 tlPos = node.position + right * 7.2f;
                    tlPos.y = SampleTerrainY(terrain, tlPos.x, tlPos.z);
                    SpawnModel("Prop_TrafficLight", tlPos, Quaternion.Euler(0f, yaw - 90f, 0f), Vector3.one, propsRoot.transform, mats, false);
                    tlCount++;
                }
            }

            // Coastal Maritime Pines around Plaza del Faro, Plaza Artigas & Playa Mansa dunes
            Vector3[] pineCenters =
            {
                new Vector3(-514.8f, 0f, -705.6f), // Plaza del Faro
                new Vector3( 207.6f, 0f,  -77.8f), // Plaza Artigas
                new Vector3( 260.0f, 0f,  720.0f), // Playa Mansa dunes
                new Vector3( 310.0f, 0f,  920.0f), // Mansa Parada 2
            };
            foreach (var c in pineCenters)
            {
                for (int k = 0; k < 12; k++)
                {
                    float ang = k * 30f * Mathf.Deg2Rad;
                    float rad = 18f + (k % 3) * 14f;
                    float px = c.x + Mathf.Cos(ang) * rad;
                    float pz = c.z + Mathf.Sin(ang) * rad;
                    float py = SampleTerrainY(terrain, px, pz);
                    if (py > 2.0f)
                        SpawnModel("Pine_Coastal", new Vector3(px, py - 0.1f, pz), Quaternion.Euler(0f, k * 47f, 0f), Vector3.one * (0.9f + (k % 3) * 0.2f), propsRoot.transform, mats, false);
                }
            }

            // Playa Mansa & Playa Brava Beach Lifeguard Towers + Umbrella clusters!
            Vector3[] beachSpots =
            {
                // Playa Mansa (West/NW)
                new Vector3(110f, 0f, 455f),
                new Vector3(195f, 0f, 645f),
                new Vector3(245f, 0f, 845f),
                new Vector3(-125f, 0f, 165f),
                // Playa Brava & El Emir (East/SE)
                new Vector3(950f, 0f, 670f),
                new Vector3(1110f, 0f, 790f),
                new Vector3(315f, 0f, -155f),
                new Vector3(95f, 0f, -365f),
            };
            for (int b = 0; b < beachSpots.Length; b++)
            {
                Vector3 bp = beachSpots[b];
                float by = SampleTerrainY(terrain, bp.x, bp.z);
                if (by < 0.6f) continue;
                float faceYaw = b < 4 ? -65f : 125f;
                SpawnModel("Beach_LifeguardTower", new Vector3(bp.x, by, bp.z), Quaternion.Euler(0f, faceYaw, 0f), Vector3.one, propsRoot.transform, mats, true);
                for (int u = -1; u <= 1; u++)
                {
                    Vector3 up = new Vector3(bp.x + u * 8f, 0f, bp.z + u * 5f + 6f);
                    up.y = SampleTerrainY(terrain, up.x, up.z);
                    if (up.y > 0.65f)
                        SpawnModel("Beach_UmbrellaSet", up, Quaternion.Euler(0f, faceYaw + u * 12f, 0f), Vector3.one, propsRoot.transform, mats, false);
                }
                roadNet.pedestrianPoints.Add(new Vector3(bp.x, by + 0.2f, bp.z));
            }

            // 6 Crisp Readable Uruguayan Punta del Este Landmark & Street Signs!
            var signSpecs = new (string matKey, float x, float z, float yaw)[]
            {
                ("Sign_Brava",   758f,   525f, 130f),
                ("Sign_Gorlero", 232f,   -62f,  45f),
                ("Sign_Puerto", -382f,  -215f, -55f),
                ("Sign_Faro",   -492f,  -692f,  25f),
                ("Sign_Mansa",   185f,   565f, -65f),
                ("Sign_Rambla",  -455f, -920f, 195f),
            };
            foreach (var sp in signSpecs)
            {
                float sy = SampleTerrainY(terrain, sp.x, sp.z);
                var sign = SpawnModel("Prop_StreetSign", new Vector3(sp.x, sy, sp.z), Quaternion.Euler(0f, sp.yaw, 0f), Vector3.one * 1.25f, propsRoot.transform, mats, false);
                if (sign != null)
                {
                    foreach (var mr in sign.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (mr.name.Contains("Board"))
                            mr.sharedMaterial = mats[sp.matKey];
                    }
                }
            }

            return streetLights;
        }

        private static void ApplyPalmMaterials(GameObject palm, Dictionary<string, Material> mats)
        {
            if (palm == null) return;
            foreach (var mr in palm.GetComponentsInChildren<MeshRenderer>())
            {
                if (mr.name.Contains("Canopy"))
                    mr.sharedMaterial = mats["PalmFrond"];
                else
                    mr.sharedMaterial = mats["PalmBark"];
            }
        }

        private static Dictionary<string, GameObject> BuildVehicleTemplatesAndParkedFleet(Terrain terrain, Dictionary<string, Material> mats, out GtaVehicleController starterCar)
        {
            var templatesRoot = new GameObject("--- VEHICLE_TEMPLATES ---");
            var parkedRoot = new GameObject("City_ParkedVehicles");
            var dict = new Dictionary<string, GameObject>();

            var specs = new (string key, string fbx, string display, VehicleArchetype arch, float torque, float maxKmh, bool police)[]
            {
                ("Cabrio",        "Car_Cabrio_Punta",     "Punta Cabrio GT",       VehicleArchetype.Cabrio,        2500f, 152f, false),
                ("SportsGT",      "Car_SportsGT",         "Atlántico V8 GT",       VehicleArchetype.SportsGT,      3050f, 178f, false),
                ("BeachSUV",      "Car_BeachSUV",         "Mansa 4x4 SUV",         VehicleArchetype.BeachSUV,      2700f, 134f, false),
                ("CitySedan",     "Car_CitySedan",        "Gorlero Sedán",         VehicleArchetype.CitySedan,     2150f, 128f, false),
                ("PoliceCruiser", "Car_PoliciaMaldonado", "Patrullero Maldonado",  VehicleArchetype.PoliceCruiser, 2950f, 165f, true),
            };

            foreach (var sp in specs)
            {
                var tpl = CreateDrivableVehicleObject(sp.key, sp.fbx, sp.display, sp.arch, sp.torque, sp.maxKmh, sp.police, mats);
                tpl.transform.SetParent(templatesRoot.transform);
                tpl.SetActive(false);
                dict[sp.key] = tpl;
            }

            // Spawn 10 parked, ready-to-drive vehicles right at iconic Punta del Este spots!
            var parkedSpots = new (string key, float x, float z, float yaw)[]
            {
                ("Cabrio",        732f,  526f, 222f), // Right next to Player Spawn at Parada 1 / Los Dedos!
                ("SportsGT",      226f,  -46f, 224f), // Av. Gorlero & Plaza Artigas
                ("BeachSUV",     -386f, -206f, -45f), // Puerto de Punta del Este
                ("PoliceCruiser",-485f, -688f,  15f), // Plaza del Faro
                ("Cabrio",       -465f, -908f,  75f), // Punta de las Salinas viewpoint
                ("SportsGT",      222f,  582f,  18f), // Playa Mansa Rambla
                ("BeachSUV",      550f,  460f, 220f), // Parada 1 Brava Rambla
                ("CitySedan",     -55f, -330f, 225f), // Av. Gorlero South
                ("Cabrio",        390f,  145f, 222f), // Centro Calle 20
                ("PoliceCruiser", 660f,  605f, 210f), // Brava Roundabout
            };

            starterCar = null;
            for (int i = 0; i < parkedSpots.Length; i++)
            {
                var ps = parkedSpots[i];
                float y = Mathf.Max(SampleTerrainY(terrain, ps.x, ps.z), 3.1f) + 0.55f;
                var inst = UnityEngine.Object.Instantiate(dict[ps.key], new Vector3(ps.x, y, ps.z), Quaternion.Euler(0f, ps.yaw, 0f), parkedRoot.transform);
                inst.name = $"Parked_{ps.key}_{i:00}";
                inst.SetActive(true);
                if (i == 0) starterCar = inst.GetComponent<GtaVehicleController>();
            }

            return dict;
        }

        private static GameObject CreateDrivableVehicleObject(string key, string fbxName, string displayName, VehicleArchetype arch, float torque, float maxKmh, bool isPolice, Dictionary<string, Material> mats)
        {
            var root = new GameObject("Vehicle_" + key);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1550f;

            // Main chassis BoxCollider (raised cleanly above suspension bottom)
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.88f, 0f);
            box.size = new Vector3(1.86f, 0.92f, 4.35f);

            var fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{fbxName}.fbx");
            Transform visualModel = null;
            if (fbxPrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                UpgradeFbxMaterialsToUrp(inst, mats);
                visualModel = inst.transform;
            }

            var ctrl = root.AddComponent<GtaVehicleController>();
            ctrl.vehicleDisplayName = displayName;
            ctrl.archetype = arch;
            ctrl.motorTorque = torque;
            ctrl.maxSpeedKmh = maxKmh;
            ctrl.isPoliceVehicle = isPolice;

            // Find the 4 separated Blender wheel meshes & verify forward (+Z) alignment!
            Transform wFL = FindDeep(visualModel, "Wheel_FL");
            Transform wFR = FindDeep(visualModel, "Wheel_FR");
            Transform wRL = FindDeep(visualModel, "Wheel_RL");
            Transform wRR = FindDeep(visualModel, "Wheel_RR");

            if (visualModel != null && wFL != null && wFL.localPosition.z < 0f)
            {
                // Flip visual root 180 deg around Y so front wheels are strictly at +Z!
                visualModel.localRotation = Quaternion.Euler(0f, 180f, 0f);
                // Swap L/R references after 180° flip
                (wFL, wFR) = (wFR, wFL);
                (wRL, wRR) = (wRR, wRL);
            }

            float wheelRadius = arch == VehicleArchetype.BeachSUV ? 0.42f : 0.36f;
            ctrl.visualFL = wFL;
            ctrl.visualFR = wFR;
            ctrl.visualRL = wRL;
            ctrl.visualRR = wRR;

            ctrl.wcFL = CreateWheelCollider(root.transform, "WC_FL", new Vector3(-0.82f, wheelRadius + 0.08f,  1.38f), wheelRadius);
            ctrl.wcFR = CreateWheelCollider(root.transform, "WC_FR", new Vector3( 0.82f, wheelRadius + 0.08f,  1.38f), wheelRadius);
            ctrl.wcRL = CreateWheelCollider(root.transform, "WC_RL", new Vector3(-0.82f, wheelRadius + 0.08f, -1.34f), wheelRadius);
            ctrl.wcRR = CreateWheelCollider(root.transform, "WC_RR", new Vector3( 0.82f, wheelRadius + 0.08f, -1.34f), wheelRadius);

            // Real-time headlights (enabled automatically at Sunset / Night!)
            ctrl.headLightL = CreateCarLight(root.transform, "Headlight_L", new Vector3(-0.62f, 0.72f, 2.15f), LightType.Spot, Palette.SunAfternoon, 48f, 38f);
            ctrl.headLightR = CreateCarLight(root.transform, "Headlight_R", new Vector3( 0.62f, 0.72f, 2.15f), LightType.Spot, Palette.SunAfternoon, 48f, 38f);

            if (isPolice)
            {
                ctrl.sirenRedLight  = CreateCarLight(root.transform, "Siren_Red",  new Vector3(-0.36f, 1.78f, -0.08f), LightType.Point, Palette.HudWantedAlert, 16f, 14f);
                ctrl.sirenBlueLight = CreateCarLight(root.transform, "Siren_Blue", new Vector3( 0.36f, 1.78f, -0.08f), LightType.Point, Palette.HudWantedPolice, 16f, 14f);
            }

            // Tire drift smoke particle systems on rear wheels (palette-tinted warm dust/smoke!)
            ctrl.driftSmokeL = CreateDriftSmoke(root.transform, "DriftSmoke_L", new Vector3(-0.88f, 0.18f, -1.34f));
            ctrl.driftSmokeR = CreateDriftSmoke(root.transform, "DriftSmoke_R", new Vector3( 0.88f, 0.18f, -1.34f));

            return root;
        }

        private static WheelCollider CreateWheelCollider(Transform parent, string name, Vector3 localPos, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var wc = go.AddComponent<WheelCollider>();
            wc.radius = radius;
            wc.mass = 24f;
            wc.suspensionDistance = 0.24f;
            wc.forceAppPointDistance = 0.25f;

            var spring = wc.suspensionSpring;
            spring.spring = 34000f;
            spring.damper = 4200f;
            spring.targetPosition = 0.5f;
            wc.suspensionSpring = spring;

            var fwd = wc.forwardFriction;
            fwd.stiffness = 1.45f;
            wc.forwardFriction = fwd;

            var side = wc.sidewaysFriction;
            side.stiffness = 1.35f;
            wc.sidewaysFriction = side;
            return wc;
        }

        private static Light CreateCarLight(Transform parent, string name, Vector3 pos, LightType type, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (type == LightType.Spot) go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = 52f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        private static ParticleSystem CreateDriftSmoke(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.55f;
            main.startSpeed = 1.2f;
            main.startSize = 0.65f;
            main.startColor = new Color(Palette.CreamConcrete.r, Palette.CreamConcrete.g, Palette.CreamConcrete.b, 0.52f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 24f;
            em.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            var pr = go.GetComponent<ParticleSystemRenderer>();
            var pMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            pMat.color = new Color(Palette.CreamConcrete.r, Palette.CreamConcrete.g, Palette.CreamConcrete.b, 0.45f);
            pr.sharedMaterial = pMat;
            return ps;
        }

        private static PlayerFootController BuildPlayer(Terrain terrain, Dictionary<string, Material> mats, out VehicleInteractionController vehInteract)
        {
            // Spawn right on the Rambla Brava promenade beside the shiny red Punta Cabrio GT overlooking Los Dedos (La Mano)!
            float sx = 724f, sz = 532f;
            float sy = Mathf.Max(SampleTerrainY(terrain, sx, sz), 3.3f) + 0.42f;

            var root = new GameObject("Player_GTA");
            root.tag = "Player";
            root.transform.position = new Vector3(sx, sy, sz);
            root.transform.rotation = Quaternion.Euler(0f, 114f, 0f); // Facing Los Dedos, Playa Brava & Atlantic ocean!

            root.AddComponent<CharacterController>();
            var foot = root.AddComponent<PlayerFootController>();
            vehInteract = root.AddComponent<VehicleInteractionController>();
            vehInteract.playerFoot = foot;

            var camPivot = new GameObject("CameraPivot");
            camPivot.transform.SetParent(root.transform, false);
            camPivot.transform.localPosition = new Vector3(0f, 1.48f, 0f);
            foot.cameraTargetPivot = camPivot.transform;

            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Char_Humanoid.fbx");
            if (charPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                model.name = "Visual_Character";
                model.transform.SetParent(root.transform, false);
                UpgradeFbxMaterialsToUrp(model, mats);
                // Orient humanoid facing LOCAL +Z (check sunglasses position in model's local space!)
                var shades = FindDeep(model.transform, "Sunglasses_Bridge");
                if (shades != null && model.transform.InverseTransformPoint(shades.position).z < 0f)
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                foot.visualRoot = model.transform;
                foot.charAnim = model.AddComponent<ProceduralCharacterAnimator>();
            }

            return foot;
        }

        private static GameObject BuildPedestrianTemplate()
        {
            var tpl = new GameObject("Pedestrian_Template");
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Char_Humanoid.fbx");
            if (charPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                model.name = "Visual";
                model.transform.SetParent(tpl.transform, false);
                UpgradeFbxMaterialsToUrp(model, null);
                var shades = FindDeep(model.transform, "Sunglasses_Bridge");
                if (shades != null && model.transform.InverseTransformPoint(shades.position).z < 0f)
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                model.AddComponent<ProceduralCharacterAnimator>();
            }
            tpl.AddComponent<PedestrianAI>();
            tpl.SetActive(false);
            return tpl;
        }

        private static GtaCameraRig BuildCameras(PlayerFootController playerFoot, out Camera uiOverlayCam)
        {
            var camRoot = new GameObject("--- CAMERAS ---");

            // Main Base Camera with URP HDR + PostProcessing + CinemachineBrain
            var mainGo = new GameObject("Main Camera");
            mainGo.tag = "MainCamera";
            mainGo.transform.SetParent(camRoot.transform);
            var mainCam = mainGo.AddComponent<Camera>();
            mainCam.nearClipPlane = 0.25f;
            mainCam.farClipPlane = 3800f;
            mainCam.fieldOfView = 58f;
            mainCam.cullingMask = ~LayerMask.GetMask("UI");
            mainGo.AddComponent<AudioListener>();
            mainGo.AddComponent<CinemachineBrain>();

            var mainUrp = mainGo.AddComponent<UniversalAdditionalCameraData>();
            mainUrp.renderType = CameraRenderType.Base;
            mainUrp.renderPostProcessing = true;
            mainUrp.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            mainUrp.volumeLayerMask = ~0;

            // Dedicated URP Overlay Camera strictly for UI Layer -> crisp HUD in BOTH builds & Pipeline captures!
            var uiCamGo = new GameObject("UI_Overlay_Camera");
            uiCamGo.transform.SetParent(camRoot.transform);
            uiOverlayCam = uiCamGo.AddComponent<Camera>();
            uiOverlayCam.clearFlags = CameraClearFlags.Depth;
            uiOverlayCam.cullingMask = LayerMask.GetMask("UI");
            uiOverlayCam.orthographic = true;
            uiOverlayCam.nearClipPlane = 0.1f;
            uiOverlayCam.farClipPlane = 10f;
            var uiUrp = uiCamGo.AddComponent<UniversalAdditionalCameraData>();
            uiUrp.renderType = CameraRenderType.Overlay;
            uiUrp.renderPostProcessing = false;
            mainUrp.cameraStack.Add(uiOverlayCam);

            // Gameplay Cinemachine 3 Orbital Camera
            var gpGo = new GameObject("CM_GameplayCamera");
            gpGo.transform.SetParent(camRoot.transform);
            var gpCam = gpGo.AddComponent<CinemachineCamera>();
            gpCam.Priority = 10;
            gpCam.Lens.FieldOfView = 58f;
            gpCam.Lens.NearClipPlane = 0.25f;
            gpCam.Lens.FarClipPlane = 3800f;
            var pivot = playerFoot.cameraTargetPivot != null ? playerFoot.cameraTargetPivot : playerFoot.transform;
            gpCam.Target.TrackingTarget = pivot;
            gpCam.Target.LookAtTarget = pivot;

            var orbital = gpGo.AddComponent<CinemachineOrbitalFollow>();
            orbital.Radius = 4.6f;
            orbital.TargetOffset = new Vector3(0.45f, 0.15f, 0f);
            orbital.HorizontalAxis.Value = 118f;
            orbital.VerticalAxis.Value = 16f;
            gpGo.AddComponent<CinemachineRotationComposer>();
            gpGo.AddComponent<CinemachineImpulseListener>();

            // Title Cinematic Drone Camera framing Los Dedos (La Mano) & Playa Brava!
            var titleGo = new GameObject("CM_TitleDroneCamera");
            titleGo.transform.SetParent(camRoot.transform);
            var titleCam = titleGo.AddComponent<CinemachineCamera>();
            titleCam.Priority = 30;
            titleCam.Lens.FieldOfView = 52f;
            titleCam.Lens.NearClipPlane = 0.3f;
            titleCam.Lens.FarClipPlane = 3800f;
            titleGo.transform.position = new Vector3(774f, 14f, 492f);
            titleGo.transform.LookAt(new Vector3(794.6f, 5.2f, 511.3f));

            var rig = camRoot.AddComponent<GtaCameraRig>();
            rig.gameplayCam = gpCam;
            rig.titleCam = titleCam;
            rig.orbitalFollow = orbital;
            rig.impulseSource = camRoot.AddComponent<CinemachineImpulseSource>();
            rig.SetFootTarget(pivot);
            rig.SetTitleMode(true);

            return rig;
        }

        private static CoastalAtmosphereController BuildLightingAtmosphereAndVolume(Dictionary<string, Material> mats, AtmosphereExtras extras, List<Light> streetLights)
        {
            var atmRoot = new GameObject("--- LIGHTING_AND_ATMOSPHERE ---");

            // Directional Sun Light (warm coastal afternoon tint, 4-cascade soft shadows)
            var sunGo = new GameObject("Sun_DirectionalLight");
            sunGo.transform.SetParent(atmRoot.transform);
            sunGo.transform.rotation = Quaternion.Euler(38f, -52f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.SunAfternoon;
            sun.intensity = 1.48f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;

            // Global Reflection Probe for ocean, glass skyscrapers & metallic car paint!
            var probeGo = new GameObject("Global_CoastalReflectionProbe");
            probeGo.transform.SetParent(atmRoot.transform);
            probeGo.transform.position = new Vector3(120f, 35f, 80f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.size = new Vector3(3200f, 400f, 3200f);
            probe.intensity = 0.95f;

            // URP Global Post-Processing Volume (ACES Tonemapping, Bloom, ColorAdjustments, Vignette)
            string profPath = $"{SettingDir}/PuntaPostProcessProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profPath);
            }
            profile.components.Clear();

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.value = TonemappingMode.ACES;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 1.08f;
            bloom.intensity.value = 0.30f;
            bloom.scatter.value = 0.65f;

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.08f;
            ca.contrast.value = 7f;
            ca.saturation.value = 9f;

            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = 0.16f;
            vig.smoothness.value = 0.45f;

            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("Global_PostProcessVolume");
            volGo.transform.SetParent(atmRoot.transform);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var ctrl = atmRoot.AddComponent<CoastalAtmosphereController>();
            ctrl.sunLight = sun;
            ctrl.postVolume = vol;
            ctrl.skyboxMaterial = mats["Skybox"];
            ctrl.oceanMaterial = mats["Ocean"];
            ctrl.glassTowerMaterial = mats["GlassTower"];
            ctrl.gorleroFacadeMaterial = mats["Gorlero"];
            ctrl.villaFacadeMaterial = mats["Villa"];
            ctrl.faroBeaconRotor = extras.faroRotor;
            ctrl.faroSpotLight = extras.faroSpot;
            ctrl.faroBeamConeRenderer = extras.faroBeamCone;
            ctrl.streetPointLights = streetLights;
            ctrl.ApplyMode(TimeOfDayMode.SunnyAfternoon);

            return ctrl;
        }

        // --- UTILITY HELPERS ---

        private static GameObject SpawnModel(string fbxName, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent, Dictionary<string, Material> mats, bool addCollider)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{fbxName}.fbx");
            if (prefab == null)
            {
                Debug.LogWarning($"[PuntaCityBuilder] Missing model FBX: {fbxName}");
                return null;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = fbxName;
            inst.transform.SetParent(parent);
            inst.transform.position = pos;
            inst.transform.rotation = rot;
            inst.transform.localScale = scale;
            UpgradeFbxMaterialsToUrp(inst, mats);
            if (addCollider) AddBoxColliderFromBounds(inst);
            return inst;
        }

        private static void UpgradeFbxMaterialsToUrp(GameObject root, Dictionary<string, Material> mats)
        {
            if (root == null) return;
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var shared = mr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var sm = shared[i];
                    if (sm == null || sm.name == "Default-Material" || sm.shader == null || !sm.shader.name.Contains("Universal Render Pipeline"))
                    {
                        Color col = sm != null && sm.HasProperty("_Color") ? sm.color : Palette.WarmStucco;
                        var matAssetName = $"Mat_FBX_{(sm != null ? sm.name.Replace(" ", "_") : "Fallback")}";
                        string mp = $"{MatDir}/{matAssetName}.mat";
                        var um = AssetDatabase.LoadAssetAtPath<Material>(mp);
                        if (um == null)
                        {
                            um = new Material(urpLit);
                            um.SetColor("_BaseColor", col);
                            if (sm != null && sm.HasProperty("_Metallic")) um.SetFloat("_Metallic", sm.GetFloat("_Metallic"));
                            if (sm != null && sm.HasProperty("_Glossiness")) um.SetFloat("_Smoothness", sm.GetFloat("_Glossiness"));
                            AssetDatabase.CreateAsset(um, mp);
                        }
                        shared[i] = um;
                        changed = true;
                    }
                }
                if (changed) mr.sharedMaterials = shared;
            }
        }

        private static void AddBoxColliderFromBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            Vector3 lossy = go.transform.lossyScale;
            col.size = new Vector3(
                b.size.x / Mathf.Max(0.01f, Mathf.Abs(lossy.x)),
                b.size.y / Mathf.Max(0.01f, Mathf.Abs(lossy.y)),
                b.size.z / Mathf.Max(0.01f, Mathf.Abs(lossy.z)));
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject CreateMeshChild(Transform parent, string name, Mesh mesh, Material mat, bool addMeshCollider)
        {
            SaveMeshAsset(mesh, name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (addMeshCollider && mesh.vertexCount > 0)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
            return go;
        }

        private static void SaveMeshAsset(Mesh mesh, string assetName)
        {
            string path = $"{MeshDir}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        private static float SignedPolygonArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p1 = poly[i];
                Vector2 p2 = poly[(i + 1) % poly.Count];
                a += p1.x * p2.y - p2.x * p1.y;
            }
            return a * 0.5f;
        }

        private static void AddBox(MeshBuilder mb, Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = c + new Vector3( h.x, -h.y, -h.z);
            Vector3 p101 = c + new Vector3( h.x, -h.y,  h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y,  h.z);
            Vector3 p010 = c + new Vector3(-h.x,  h.y, -h.z);
            Vector3 p110 = c + new Vector3( h.x,  h.y, -h.z);
            Vector3 p111 = c + new Vector3( h.x,  h.y,  h.z);
            Vector3 p011 = c + new Vector3(-h.x,  h.y,  h.z);
            mb.AddQuadFacing(p010, p011, p111, p110, Vector2.zero, Vector2.up, Vector2.one, Vector2.right, Vector3.up);
            mb.AddQuadFacing(p000, p100, p110, p010, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.back);
            mb.AddQuadFacing(p100, p101, p111, p110, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.right);
            mb.AddQuadFacing(p101, p001, p011, p111, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.forward);
            mb.AddQuadFacing(p001, p000, p010, p011, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.left);
        }

        private static void AddRibbonBox(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 halfRight, float depth)
        {
            Vector3 al = a - halfRight, ar = a + halfRight;
            Vector3 bl = b - halfRight, br = b + halfRight;
            Vector3 down = Vector3.down * depth;
            float len = Vector3.Distance(a, b) * 0.25f;
            // Top deck (strictly +Y upward normal!)
            mb.AddQuadUp(al, bl, br, ar, new Vector2(0, 0), new Vector2(0, len), new Vector2(1, len), new Vector2(1, 0));
            // Sides & ends (double-sided solid)
            mb.AddQuadDoubleSided(ar, br, br + down, ar + down, Vector2.zero, new Vector2(len, 0), new Vector2(len, 1), Vector2.up);
            mb.AddQuadDoubleSided(bl, al, al + down, bl + down, Vector2.zero, new Vector2(len, 0), new Vector2(len, 1), Vector2.up);
            mb.AddQuadDoubleSided(br, bl, bl + down, br + down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            mb.AddQuadDoubleSided(al, ar, ar + down, al + down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        // Fast 32-bit indexed mesh builder for clean batching of 731 buildings & 236 roads!
        private class MeshBuilder
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _tris = new List<int>();

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = _verts.Count;
                _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
                _uvs.Add(ua); _uvs.Add(ub); _uvs.Add(uc); _uvs.Add(ud);
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
                _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
            }

            public void AddQuadUp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                AddQuadFacing(a, b, c, d, ua, ub, uc, ud, Vector3.up);
            }

            public void AddQuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 desiredNormal)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, desiredNormal) < 0f)
                    AddQuad(a, d, c, b, ua, ud, uc, ub);
                else
                    AddQuad(a, b, c, d, ua, ub, uc, ud);
            }

            public void AddQuadDoubleSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                AddQuad(a, b, c, d, ua, ub, uc, ud);
                AddQuad(a, d, c, b, ua, ud, uc, ub);
            }

            public void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = _verts.Count;
                _verts.Add(a); _verts.Add(b); _verts.Add(c);
                _uvs.Add(new Vector2(a.x * 0.1f, a.z * 0.1f));
                _uvs.Add(new Vector2(b.x * 0.1f, b.z * 0.1f));
                _uvs.Add(new Vector2(c.x * 0.1f, c.z * 0.1f));
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            }

            public void AddTriUp(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.y < 0f) AddTri(a, c, b);
                else AddTri(a, b, c);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_verts);
                m.SetUVs(0, _uvs);
                m.SetTriangles(_tris, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        // Minimal, rock-solid JSON parser for OSM nested arrays/objects (no external NuGet dependency required)
        private static class SimpleJson
        {
            public static object Parse(string s)
            {
                int idx = 0;
                return ParseValue(s, ref idx);
            }

            private static void SkipWs(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private static object ParseValue(string s, ref int i)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{') return ParseObj(s, ref i);
                if (c == '[') return ParseArr(s, ref i);
                if (c == '"') return ParseStr(s, ref i);
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return ParseNum(s, ref i);
            }

            private static Dictionary<string, object> ParseObj(string s, ref int i)
            {
                var d = new Dictionary<string, object>();
                i++; // '{'
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (i < s.Length)
                {
                    SkipWs(s, ref i);
                    string key = ParseStr(s, ref i);
                    SkipWs(s, ref i);
                    i++; // ':'
                    d[key] = ParseValue(s, ref i);
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; break; }
                    break;
                }
                return d;
            }

            private static List<object> ParseArr(string s, ref int i)
            {
                var list = new List<object>();
                i++; // '['
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (i < s.Length)
                {
                    list.Add(ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; break; }
                    break;
                }
                return list;
            }

            private static string ParseStr(string s, ref int i)
            {
                i++; // '"'
                var sb = new System.Text.StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') break;
                    if (c == '\\' && i < s.Length)
                    {
                        char n = s[i++];
                        if (n == 'u' && i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        else sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => n });
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            private static double ParseNum(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                    i++;
                return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
#endif
