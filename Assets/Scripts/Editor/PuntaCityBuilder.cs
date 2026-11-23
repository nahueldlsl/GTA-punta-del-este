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
                vehicleTemplates["SportsGT"],
                vehicleTemplates["Taxi"]
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
            string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { TexDir, "Assets/Art/HDRI", "Assets/Resources/MapData", "Assets/Resources/UI" });
            bool changed = false;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;

                bool dirty = false;
                if (path.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
                {
                    if (imp.textureShape != TextureImporterShape.Texture2D || imp.sRGBTexture || imp.maxTextureSize < 2048 || imp.wrapMode != TextureWrapMode.Clamp)
                    {
                        imp.textureShape = TextureImporterShape.Texture2D;
                        imp.sRGBTexture = false;
                        imp.maxTextureSize = 2048;
                        imp.wrapMode = TextureWrapMode.Clamp;
                        imp.mipmapEnabled = true;
                        dirty = true;
                    }
                }
                if (path.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase) || path.Contains("Ocean_Normal_"))
                {
                    if (imp.textureType != TextureImporterType.NormalMap)
                    {
                        imp.textureType = TextureImporterType.NormalMap;
                        dirty = true;
                    }
                }
                if (path.Contains("Palm_Frond_RGBA") || path.Contains("Pine_Needles_RGBA") || path.Contains("Dune_Grass_RGBA"))
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

        private static Material CreatePanoramicSkyMat(string assetName, string hdrFilename, float rotation, float exposure)
        {
            string path = $"{MatDir}/{assetName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture>($"Assets/Art/HDRI/{hdrFilename}");
            if (tex != null) mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Rotation", rotation);
            mat.SetFloat("_Exposure", exposure);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            mat.SetFloat("_Mapping", 1f); // Latitude Longitude Layout
            mat.SetFloat("_ImageType", 0f); // 360 Degrees
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Dictionary<string, Material> BuildMaterials()
        {
            var m = new Dictionary<string, Material>();

            // Photographic Poly Haven Roads, Sidewalks, Plazas & Coastal Surfaces
            m["RoadAvenue"]    = CreateUrpLitMat("Mat_RoadAvenue", Color.white, "Road_Avenue_Albedo", "Road_Asphalt_Normal", null, null, 0f, 0.28f);
            m["RoadStreet"]    = CreateUrpLitMat("Mat_RoadStreet", Color.white, "Road_Street_Albedo", "Road_Asphalt_Normal", null, null, 0f, 0.24f);
            m["RoadCrosswalk"] = CreateUrpLitMat("Mat_RoadCrosswalk", Color.white, "Road_Crosswalk_Albedo", "Road_Asphalt_Normal", null, null, 0f, 0.26f);
            m["Sidewalk"]      = CreateUrpLitMat("Mat_Sidewalk", Color.white, "PH_Cobble_Albedo", "PH_Cobble_Normal", null, null, 0f, 0.24f);
            m["BrickPlaza"]    = CreateUrpLitMat("Mat_BrickPlaza", Color.white, "PH_BrickPave_Albedo", "PH_BrickPave_Normal", null, null, 0f, 0.22f);
            m["ConcreteQuay"]  = CreateUrpLitMat("Mat_ConcreteQuay", new Color(0.92f, 0.91f, 0.88f), "PH_Concrete_Albedo", "PH_Concrete_Normal", null, null, 0f, 0.24f);
            m["Seawall"]       = CreateUrpLitMat("Mat_Seawall", Color.white, "Seawall_Granite_Albedo", "Seawall_Granite_Normal", null, null, 0f, 0.20f);

            // 5 Distinct Punta del Este Architectural Facades (breaks ALL v1 wallpaper repetition!)
            m["GlassTower"]    = CreateUrpLitMat("Mat_FacadeLuxuryWhite", Color.white, "Facade_LuxuryWhiteGlass_Albedo", "Facade_LuxuryWhiteGlass_Normal", "Facade_LuxuryWhiteGlass_Metallic", "Facade_LuxuryWhiteGlass_Emission", 0.35f, 0.80f);
            m["BronzeTower"]   = CreateUrpLitMat("Mat_FacadeBronzeTower", Color.white, "Facade_BronzeTower_Albedo", "Facade_BronzeTower_Normal", "Facade_BronzeTower_Metallic", "Facade_BronzeTower_Emission", 0.45f, 0.82f);
            m["TerracottaRes"] = CreateUrpLitMat("Mat_FacadeTerracotta", Color.white, "Facade_TerracottaResort_Albedo", "Facade_TerracottaResort_Normal", "Facade_TerracottaResort_Metallic", "Facade_TerracottaResort_Emission", 0.15f, 0.42f);
            m["Gorlero"]       = CreateUrpLitMat("Mat_FacadeGorleroArcade", Color.white, "Facade_GorleroArcade_Albedo", "Facade_GorleroArcade_Normal", "Facade_GorleroArcade_Metallic", "Facade_GorleroArcade_Emission", 0.12f, 0.45f);
            m["Villa"]         = CreateUrpLitMat("Mat_FacadePeninsulaVilla", Color.white, "Facade_PeninsulaVilla_Albedo", "Facade_PeninsulaVilla_Normal", null, "Facade_PeninsulaVilla_Emission", 0.02f, 0.28f);

            m["ConcreteRoof"]  = CreateUrpLitMat("Mat_ConcreteRoof", Palette.CreamConcrete, "PH_Concrete_Albedo", "PH_Concrete_Normal", null, null, 0f, 0.20f);
            m["TerracottaRoof"]= CreateUrpLitMat("Mat_TerracottaRoof", new Color(0.72f, 0.33f, 0.20f), "PH_BrickPave_Albedo", "PH_BrickPave_Normal", null, null, 0f, 0.26f);
            m["DarkPlinth"]    = CreateUrpLitMat("Mat_DarkPlinth", new Color(0.24f, 0.27f, 0.31f), "PH_Plaster_Albedo", "PH_Plaster_Normal", null, null, 0.05f, 0.32f);
            m["BalconyLedge"]  = CreateUrpLitMat("Mat_BalconyLedge", new Color(0.95f, 0.94f, 0.91f), "PH_Concrete_Albedo", "PH_Concrete_Normal", null, null, 0.02f, 0.35f);
            m["Sculpture"]     = CreateUrpLitMat("Mat_LosDedos", new Color(0.95f, 0.92f, 0.86f), "PH_Plaster_Albedo", "PH_Concrete_Normal", null, null, 0.02f, 0.24f);
            m["WoodDock"]      = CreateUrpLitMat("Mat_WoodDock", Color.white, "PH_WoodDeck_Albedo", "PH_WoodDeck_Normal", null, null, 0f, 0.26f);

            // Poly Haven Photogrammetry Rock & Bench Materials
            m["RockCoastal"]   = CreateUrpLitMat("Mat_RockCoastal", Color.white, "PH_coast_land_rocks_02_Albedo", "PH_coast_land_rocks_02_Normal", null, null, 0.02f, 0.20f);
            m["PH_Rock02"]     = CreateUrpLitMat("Mat_PH_Rock02", Color.white, "PH_coast_land_rocks_02_Albedo", "PH_coast_land_rocks_02_Normal", null, null, 0.02f, 0.22f);
            m["PH_Rock03"]     = CreateUrpLitMat("Mat_PH_Rock03", Color.white, "PH_coast_land_rocks_03_Albedo", "PH_coast_land_rocks_03_Normal", null, null, 0.02f, 0.22f);
            m["PH_Rock04"]     = CreateUrpLitMat("Mat_PH_Rock04", Color.white, "PH_coast_land_rocks_04_Albedo", "PH_coast_land_rocks_04_Normal", null, null, 0.02f, 0.22f);
            m["PH_Boulder"]    = CreateUrpLitMat("Mat_PH_Boulder", Color.white, "PH_boulder_01_Albedo", "PH_boulder_01_Normal", null, null, 0.02f, 0.20f);
            m["PH_Bench"]      = CreateUrpLitMat("Mat_PH_Bench", Color.white, "PH_painted_wooden_bench_Albedo", "PH_painted_wooden_bench_Normal", null, null, 0.02f, 0.30f);

            // Vegetation (Alpha-Cutout Double-Sided Botanical Foliage)
            m["PalmBark"]      = CreateUrpLitMat("Mat_PalmBark", Color.white, "Palm_Bark_Albedo", "Palm_Bark_Normal", null, null, 0f, 0.16f);
            m["PalmFrond"]     = CreateUrpLitMat("Mat_PalmFrond", Color.white, "Palm_Frond_RGBA", null, null, null, 0f, 0.28f, null, true);
            m["PineNeedles"]   = CreateUrpLitMat("Mat_PineNeedles", Color.white, "Pine_Needles_RGBA", null, null, null, 0f, 0.22f, null, true);
            m["DuneGrass"]     = CreateUrpLitMat("Mat_DuneGrass", Color.white, "Dune_Grass_RGBA", null, null, null, 0f, 0.18f, null, true);
            m["AwningBlue"]    = CreateUrpLitMat("Mat_AwningBlue", Palette.Hex("#1E649B"), null, null, null, null, 0f, 0.22f);
            m["AwningCoral"]   = CreateUrpLitMat("Mat_AwningCoral", Palette.Hex("#C8523B"), null, null, null, null, 0f, 0.22f);
            m["Sand"]          = CreateUrpLitMat("Mat_SandPunta", Color.white, "PH_Sand_Albedo", "PH_Sand_Normal", null, null, 0f, 0.16f);
            m["Grass"]         = CreateUrpLitMat("Mat_ParkGrass", Color.white, "PH_Grass_Albedo", "PH_Grass_Normal", null, null, 0f, 0.16f);

            // Street & Landmark Signs + Illuminated Iconic Punta del Este Storefront Signs!
            foreach (var sname in new[] { "Sign_Gorlero", "Sign_Rambla", "Sign_Puerto", "Sign_Brava", "Sign_Faro", "Sign_Mansa" })
                m[sname] = CreateUrpLitMat("Mat_" + sname, Color.white, sname, null, null, null, 0.05f, 0.45f);

            foreach (var shopSign in new[] { "Sign_Enjoy", "Sign_ChurrosManolo", "Sign_CafeGorlero", "Sign_Heladeria", "Sign_CasinoNogaro", "Sign_Boutique20", "Sign_ParrillaPuerto", "Sign_YachtClub" })
                m[shopSign] = CreateUrpLitMat("Mat_" + shopSign, Color.white, shopSign, null, null, shopSign, 0.05f, 0.42f);

            // Tire Skidmark material
            string skidPath = $"{MatDir}/Mat_TireSkidmark.mat";
            var skidMat = AssetDatabase.LoadAssetAtPath<Material>(skidPath);
            if (skidMat == null)
            {
                skidMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(skidMat, skidPath);
            }
            skidMat.color = new Color(0.06f, 0.06f, 0.07f, 0.62f);
            skidMat.SetFloat("_Surface", 1f);
            skidMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            skidMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            skidMat.SetInt("_ZWrite", 0);
            skidMat.renderQueue = 2950;
            EditorUtility.SetDirty(skidMat);
            m["Skidmark"] = skidMat;

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

            // 3 Real Photographic Poly Haven PureSky 2K HDRI Panoramic Skyboxes (Day, Mansa Sunset, Coastal Night)!
            m["SkyboxDay"]    = CreatePanoramicSkyMat("Mat_Sky_Day_Punta",     "Sky_Day_Punta.hdr",     145f, 1.05f);
            m["SkyboxSunset"] = CreatePanoramicSkyMat("Mat_Sky_Sunset_Mansa",  "Sky_Sunset_Mansa.hdr",  245f, 1.15f);
            m["SkyboxNight"]  = CreatePanoramicSkyMat("Mat_Sky_Night_Coastal", "Sky_Night_Coastal.hdr",  60f, 0.85f);
            m["Skybox"]       = m["SkyboxDay"];

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

            // 4 Real Photographic Poly Haven PBR Terrain Layers: 0=Punta Sand, 1=Cobble Urban Plaza, 2=Park Grass, 3=Coastal Granite Rock
            td.terrainLayers = new[]
            {
                CreateTerrainLayer("SandPunta", "PH_Sand_Albedo", "PH_Sand_Normal", 12f),
                CreateTerrainLayer("UrbanPavement", "PH_Cobble_Albedo", "PH_Cobble_Normal", 8f),
                CreateTerrainLayer("ParkGrass", "PH_Grass_Albedo", "PH_Grass_Normal", 10f),
                CreateTerrainLayer("CoastalRock", "PH_coast_land_rocks_02_Albedo", "PH_coast_land_rocks_02_Normal", 12f),
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

        private struct ProcessedRoad
        {
            public List<Vector3> dense;
            public List<int> nodeIds;
            public float width;
            public bool isRambla;
            public bool isAve;
        }

        private static void BuildRoadsAndGraph(Dictionary<string, object> mapRaw, Terrain terrain, Dictionary<string, Material> mats, RoadNetworkRuntime roadNet)
        {
            var roadsList = mapRaw["roads"] as List<object>;
            if (roadsList == null) return;

            var aveBuilder = new MeshBuilder();
            var strBuilder = new MeshBuilder();
            var swBuilder  = new MeshBuilder();
            var wallBuilder = new MeshBuilder();
            var xwalkBuilder = new MeshBuilder();

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

            // PASS 1: Build complete road connectivity graph FIRST so we know every single intersection accurately!
            var processed = new List<ProcessedRoad>();
            foreach (var item in roadsList)
            {
                var r = item as Dictionary<string, object>;
                if (r == null) continue;
                string rclass = r["class"] as string ?? "street";
                string rname = r["name"] as string ?? "";
                float width = Convert.ToSingle(r["width"]);
                var ptsRaw = r["points"] as List<object>;
                if (ptsRaw == null || ptsRaw.Count < 2) continue;
                if (rclass == "service") continue;

                bool isRambla = rclass == "rambla";
                bool isAve = rclass == "avenue" || isRambla;
                float yLift = isAve ? 0.25f : 0.16f;

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

                var dense = SubdividePolyline(pts, terrain, yLift, 10f);
                var nIds = new List<int>(dense.Count);
                int prevNode = -1;
                for (int i = 0; i < dense.Count; i++)
                {
                    int n = GetOrAddNode(dense[i], rname, isRambla, isAve);
                    nIds.Add(n);
                    if (prevNode >= 0 && prevNode != n)
                    {
                        if (!roadNet.roadNodes[prevNode].neighbors.Contains(n)) roadNet.roadNodes[prevNode].neighbors.Add(n);
                        if (!roadNet.roadNodes[n].neighbors.Contains(prevNode)) roadNet.roadNodes[n].neighbors.Add(prevNode);
                    }
                    prevNode = n;
                }
                processed.Add(new ProcessedRoad { dense = dense, nodeIds = nIds, width = width, isRambla = isRambla, isAve = isAve });
            }

            // Collect intersection centers so sidewalks NEVER slice across any cross-street!
            var junctions = new List<Vector2>();
            for (int i = 0; i < roadNet.roadNodes.Count; i++)
            {
                if (roadNet.roadNodes[i].neighbors.Count >= 3)
                    junctions.Add(new Vector2(roadNet.roadNodes[i].position.x, roadNet.roadNodes[i].position.z));
            }

            bool NearAnyJunction(Vector3 p, float radius)
            {
                float r2 = radius * radius;
                for (int j = 0; j < junctions.Count; j++)
                {
                    float dx = p.x - junctions[j].x;
                    float dz = p.z - junctions[j].y;
                    if (dx * dx + dz * dz < r2) return true;
                }
                return false;
            }

            // PASS 2: Extrude clean asphalt, cleanly clipped sidewalks, stone seawalls & zebra crosswalks!
            int xwalkCount = 0;
            foreach (var pr in processed)
            {
                var targetMesh = pr.isAve ? aveBuilder : strBuilder;
                float halfW = pr.width * 0.5f;
                float swW = pr.isRambla ? 3.5f : (pr.isAve ? 2.7f : 1.75f);
                float vDist = 0f;

                for (int i = 0; i < pr.dense.Count - 1; i++)
                {
                    Vector3 a = pr.dense[i];
                    Vector3 b = pr.dense[i + 1];
                    Vector3 seg = b - a;
                    float len = seg.magnitude;
                    if (len < 0.4f) continue;

                    Vector3 dir = new Vector3(seg.x, 0f, seg.z).normalized;
                    Vector3 right = new Vector3(dir.z, 0f, -dir.x);

                    float v0 = vDist / 12f;
                    float v1 = (vDist + len) / 12f;
                    vDist += len;

                    // Crisp asphalt ribbon
                    targetMesh.AddQuadUp(
                        a - right * halfW, b - right * halfW,
                        b + right * halfW, a + right * halfW,
                        new Vector2(0, v0), new Vector2(0, v1), new Vector2(1, v1), new Vector2(1, v0));

                    Vector3 mid = (a + b) * 0.5f;
                    bool blockedByJunction = i == 0 || i == pr.dense.Count - 2 ||
                                             NearAnyJunction(a, halfW + 5.5f) ||
                                             NearAnyJunction(b, halfW + 5.5f) ||
                                             NearAnyJunction(mid, halfW + 5.0f);

                    if (!blockedByJunction)
                    {
                        float swY = 0.09f;
                        swBuilder.AddQuadUp(
                            a - right * (halfW + swW) + Vector3.up * swY, b - right * (halfW + swW) + Vector3.up * swY,
                            b - right * halfW + Vector3.up * swY, a - right * halfW + Vector3.up * swY,
                            new Vector2(0, v0 * 2.2f), new Vector2(0, v1 * 2.2f), new Vector2(1, v1 * 2.2f), new Vector2(1, v0 * 2.2f));
                        swBuilder.AddQuadUp(
                            a + right * halfW + Vector3.up * swY, b + right * halfW + Vector3.up * swY,
                            b + right * (halfW + swW) + Vector3.up * swY, a + right * (halfW + swW) + Vector3.up * swY,
                            new Vector2(0, v0 * 2.2f), new Vector2(0, v1 * 2.2f), new Vector2(1, v1 * 2.2f), new Vector2(1, v0 * 2.2f));

                        // Curb / Rambla stone seawall drops cleanly along both sidewalk edges
                        float drop = pr.isRambla ? 1.85f : 0.55f;
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
                    else if (pr.isAve && len >= 4.5f && xwalkCount < 55 && (i % 2 == 1))
                    {
                        // Painted white Uruguayan zebra crosswalk strip right before major intersections!
                        Vector3 cwA = mid - dir * 2.2f + Vector3.up * 0.035f;
                        Vector3 cwB = mid + dir * 2.2f + Vector3.up * 0.035f;
                        xwalkBuilder.AddQuadUp(
                            cwA - right * (halfW * 0.94f), cwB - right * (halfW * 0.94f),
                            cwB + right * (halfW * 0.94f), cwA + right * (halfW * 0.94f),
                            new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                        xwalkCount++;
                    }

                    if (i % 3 == 0 && (pr.isAve || roadNet.pedestrianPoints.Count < 160))
                    {
                        Vector3 pedPt = a + right * (halfW + swW * 0.55f) + Vector3.up * 0.25f;
                        roadNet.pedestrianPoints.Add(pedPt);
                    }
                }
            }

            var roadsRoot = new GameObject("City_RoadNetwork");
            CreateMeshChild(roadsRoot.transform, "Roads_Avenues_Rambla", aveBuilder.Build("Mesh_RoadsAvenue"), mats["RoadAvenue"], true);
            CreateMeshChild(roadsRoot.transform, "Roads_PeninsulaStreets", strBuilder.Build("Mesh_RoadsStreet"), mats["RoadStreet"], true);
            CreateMeshChild(roadsRoot.transform, "Roads_ZebraCrosswalks", xwalkBuilder.Build("Mesh_Crosswalks"), mats["RoadCrosswalk"], false);
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

            var luxuryWhiteWalls = new MeshBuilder();
            var bronzeWalls      = new MeshBuilder();
            var terracottaWalls  = new MeshBuilder();
            var gorleroWalls     = new MeshBuilder();
            var villaWalls       = new MeshBuilder();
            var plinthWalls      = new MeshBuilder();
            var balconyLedges    = new MeshBuilder();
            var roofsAndCornices = new MeshBuilder();
            var tileRoofs        = new MeshBuilder();
            var awningsBlue      = new MeshBuilder();
            var awningsCoral     = new MeshBuilder();

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
                if (SignedPolygonArea(poly) < 0f) poly.Reverse();

                float baseY = SampleTerrainY(terrain, cx, cz) - 1.2f;
                float topY = baseY + height + 1.2f;

                // Deterministic hash breaks ALL architectural uniformity across the 731 real OSM lots!
                int lotHash = Mathf.Abs(Mathf.RoundToInt(cx * 13.7f + cz * 7.3f) + count);
                MeshBuilder wallTarget;
                bool isTower = style == "glass_tower" || height >= 19f;
                bool isVilla = style == "coastal_villa" && height < 12.5f;

                if (isTower)
                {
                    int mod = lotHash % 3;
                    wallTarget = mod == 0 ? luxuryWhiteWalls : (mod == 1 ? bronzeWalls : terracottaWalls);
                }
                else if (isVilla)
                {
                    wallTarget = (lotHash % 3 == 0) ? terracottaWalls : villaWalls;
                }
                else
                {
                    int mod = lotHash % 4;
                    wallTarget = mod == 0 ? gorleroWalls : (mod == 1 ? terracottaWalls : (mod == 2 ? luxuryWhiteWalls : bronzeWalls));
                }

                float uScale = isTower ? 0.048f : 0.072f;
                float vScale = isTower ? 0.036f : 0.056f;
                float plinthH = (!isVilla && height >= 10f) ? 3.8f : 0f;
                float wallStartY = baseY + plinthH;

                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 p0 = poly[i];
                    Vector2 p1 = poly[(i + 1) % poly.Count];
                    float edgeLen = Vector2.Distance(p0, p1);
                    if (edgeLen < 0.4f) continue;

                    Vector2 ed = (p1 - p0).normalized;
                    Vector3 outward = new Vector3(ed.y, 0f, -ed.x);

                    // Ground-floor commercial plinth band for Gorlero/Rambla buildings
                    if (plinthH > 0.1f)
                    {
                        Vector3 pb0 = new Vector3(p0.x, baseY, p0.y) + outward * 0.15f;
                        Vector3 pb1 = new Vector3(p1.x, baseY, p1.y) + outward * 0.15f;
                        Vector3 pt1 = new Vector3(p1.x, wallStartY, p1.y) + outward * 0.15f;
                        Vector3 pt0 = new Vector3(p0.x, wallStartY, p0.y) + outward * 0.15f;
                        plinthWalls.AddQuadFacing(pb0, pb1, pt1, pt0,
                            Vector2.zero, new Vector2(edgeLen * 0.15f, 0),
                            new Vector2(edgeLen * 0.15f, 1), Vector2.up, outward);
                    }

                    Vector3 b0 = new Vector3(p0.x, wallStartY, p0.y);
                    Vector3 b1 = new Vector3(p1.x, wallStartY, p1.y);
                    Vector3 t1 = new Vector3(p1.x, topY, p1.y);
                    Vector3 t0 = new Vector3(p0.x, topY, p0.y);

                    wallTarget.AddQuadFacing(b0, b1, t1, t0,
                        new Vector2(0, 0), new Vector2(edgeLen * uScale, 0),
                        new Vector2(edgeLen * uScale, (topY - wallStartY) * vScale), new Vector2(0, (topY - wallStartY) * vScale),
                        outward);

                    // Real 3D projecting coastal balcony slabs on towers & mid-rises (casts crisp shadows + SSAO!)
                    if (height >= 14f && edgeLen > 4.5f)
                    {
                        float balcDepth = isTower ? 0.95f : 0.65f;
                        float stepY = 4.2f;
                        for (float by = wallStartY + 3.2f; by < topY - 1.5f; by += stepY)
                        {
                            Vector3 bl0 = new Vector3(p0.x, by, p0.y) + outward * balcDepth;
                            Vector3 bl1 = new Vector3(p1.x, by, p1.y) + outward * balcDepth;
                            Vector3 bl0u = bl0 + Vector3.up * 0.36f;
                            Vector3 bl1u = bl1 + Vector3.up * 0.36f;
                            Vector3 bi0u = new Vector3(p0.x, by + 0.36f, p0.y);
                            Vector3 bi1u = new Vector3(p1.x, by + 0.36f, p1.y);
                            balconyLedges.AddQuadFacing(bl0, bl1, bl1u, bl0u, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, outward);
                            balconyLedges.AddQuadUp(bi0u, bi1u, bl1u, bl0u, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                        }
                    }

                    // Architectural roof cornice overhang rim
                    float overhang = isTower ? 0.72f : 0.42f;
                    Vector3 c0 = t0 + outward * overhang;
                    Vector3 c1 = t1 + outward * overhang;
                    Vector3 c0u = c0 + Vector3.up * 0.60f;
                    Vector3 c1u = c1 + Vector3.up * 0.60f;
                    roofsAndCornices.AddQuadFacing(c0, c1, c1u, c0u, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, outward);

                    // Striped street-level cafe/boutique awnings along Av. Gorlero & Calle 20!
                    if (!isVilla && edgeLen > 5.0f && i == 0 && (count % 2 == 0))
                    {
                        float awnY = baseY + 4.3f;
                        var awTarget = (count % 4 == 0) ? awningsBlue : awningsCoral;
                        Vector3 a0 = new Vector3(p0.x, awnY + 0.55f, p0.y) + outward * 0.15f;
                        Vector3 a1 = new Vector3(p1.x, awnY + 0.55f, p1.y) + outward * 0.15f;
                        Vector3 a0o = new Vector3(p0.x, awnY - 0.12f, p0.y) + outward * 2.05f;
                        Vector3 a1o = new Vector3(p1.x, awnY - 0.12f, p1.y) + outward * 2.05f;
                        awTarget.AddQuadDoubleSided(a0, a1, a1o, a0o, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                }

                // Pitched hipped terracotta tile roofs on Punta del Este coastal villas vs. flat deck + penthouse on towers!
                if (isVilla && poly.Count <= 10)
                {
                    Vector3 apex = new Vector3(cx, topY + 2.65f, cz);
                    for (int i = 0; i < poly.Count; i++)
                    {
                        Vector2 p0 = poly[i];
                        Vector2 p1 = poly[(i + 1) % poly.Count];
                        tileRoofs.AddTriUp(apex, new Vector3(p0.x, topY + 0.35f, p0.y), new Vector3(p1.x, topY + 0.35f, p1.y));
                    }
                }
                else
                {
                    Vector3 roofCenter = new Vector3(cx, topY + 0.60f, cz);
                    for (int i = 0; i < poly.Count; i++)
                    {
                        Vector2 p0 = poly[i];
                        Vector2 p1 = poly[(i + 1) % poly.Count];
                        roofsAndCornices.AddTriUp(roofCenter, new Vector3(p0.x, topY + 0.60f, p0.y), new Vector3(p1.x, topY + 0.60f, p1.y));
                    }
                    if (height >= 17f)
                        AddBox(roofsAndCornices, new Vector3(cx, topY + 2.0f, cz), new Vector3(5.6f, 2.8f, 4.8f));
                }

                count++;
            }

            var bRoot = new GameObject("City_Architecture");
            CreateMeshChild(bRoot.transform, "Buildings_LuxuryWhiteTowers", luxuryWhiteWalls.Build("Mesh_BldLuxWhite"), mats["GlassTower"], true);
            CreateMeshChild(bRoot.transform, "Buildings_BronzeTowers",      bronzeWalls.Build("Mesh_BldBronze"), mats["BronzeTower"], true);
            CreateMeshChild(bRoot.transform, "Buildings_TerracottaResort",  terracottaWalls.Build("Mesh_BldTerra"), mats["TerracottaRes"], true);
            CreateMeshChild(bRoot.transform, "Buildings_GorleroCommercial", gorleroWalls.Build("Mesh_BldGorlero"), mats["Gorlero"], true);
            CreateMeshChild(bRoot.transform, "Buildings_CoastalVillas",     villaWalls.Build("Mesh_BldVilla"), mats["Villa"], true);
            CreateMeshChild(bRoot.transform, "Buildings_CommercialPlinths", plinthWalls.Build("Mesh_BldPlinths"), mats["DarkPlinth"], true);
            CreateMeshChild(bRoot.transform, "Buildings_3DBalconies",       balconyLedges.Build("Mesh_BldBalconies"), mats["BalconyLedge"], false);
            CreateMeshChild(bRoot.transform, "Buildings_RoofsAndCornices",  roofsAndCornices.Build("Mesh_BldRoofs"), mats["ConcreteRoof"], true);
            CreateMeshChild(bRoot.transform, "Buildings_VillaTileRoofs",    tileRoofs.Build("Mesh_BldTileRoofs"), mats["TerracottaRoof"], false);
            CreateMeshChild(bRoot.transform, "Buildings_AwningsBlue",       awningsBlue.Build("Mesh_AwningsBlue"), mats["AwningBlue"], false);
            CreateMeshChild(bRoot.transform, "Buildings_AwningsCoral",      awningsCoral.Build("Mesh_AwningsCoral"), mats["AwningCoral"], false);

            // ICONIC HERO SKYLINE: Enjoy Punta del Este Resort & Casino (Parada 4 Mansa vista!) + Trump & Le Parc sculpted towers!
            {
                float ex = 390f, ez = 1015f;
                float ey = SampleTerrainY(terrain, ex, ez) - 0.5f;
                var enjoy = SpawnModel("Enjoy_ResortCasino", new Vector3(ex, ey, ez), Quaternion.Euler(0f, -125f, 0f), Vector3.one * 1.15f, bRoot.transform, mats, true);
                if (enjoy != null)
                {
                    foreach (var mr in enjoy.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (mr.name.Contains("Enjoy_SignBoard"))
                            mr.sharedMaterial = mats["Sign_Enjoy"];
                    }
                }
            }

            string[] heroTowerPrefabs = { "Tower_TrumpCylinder", "Tower_LeParcCurved", "Tower_CurvedGlass", "Tower_TwinTerrace" };
            Vector4[] heroTowerSpecs =
            {
                new Vector4( 620f,  730f,  32f, 1.12f),
                new Vector4( 715f,  790f,  48f, 1.05f),
                new Vector4( 825f,  845f,  35f, 1.22f),
                new Vector4( 945f,  910f,  42f, 1.08f),
                new Vector4( 490f,  690f,  15f, 0.96f),
                new Vector4( 395f,  845f, -12f, 1.16f),
                new Vector4( 535f,  940f,  20f, 1.04f),
                new Vector4( 680f,  965f,  30f, 1.12f),
                new Vector4( 510f,  420f,  38f, 0.88f),
                new Vector4( 425f,  330f,  35f, 0.84f),
            };

            for (int i = 0; i < heroTowerSpecs.Length; i++)
            {
                var sp = heroTowerSpecs[i];
                string tName = heroTowerPrefabs[i % heroTowerPrefabs.Length];
                float y = SampleTerrainY(terrain, sp.x, sp.y) - 0.6f;
                var inst = SpawnModel(tName, new Vector3(sp.x, y, sp.y), Quaternion.Euler(0f, sp.z, 0f), new Vector3(1.05f, sp.w, 1.05f), bRoot.transform, mats, true);
                if (inst != null) inst.name = $"HeroTower_{tName}_{i:00}";
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

            // 1. LOS DEDOS / LA MANO DE PUNTA DEL ESTE v2 (Playa Brava Parada 1 — exact OSM 794.6, 511.3!)
            {
                float x = 794.6f, z = 511.3f;
                float y = Mathf.Max(SampleTerrainY(terrain, x, z), 1.6f);
                var dedos = SpawnModel("LaMano_LosDedos_v2", new Vector3(x, y - 0.08f, z), Quaternion.Euler(0f, 132f, 0f), Vector3.one * 1.25f, lmRoot.transform, mats, true);
                if (dedos != null)
                {
                    foreach (var r in dedos.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (r.name.Contains("LaMano_")) r.sharedMaterial = mats["Sculpture"];
                    }
                }
                // Timber beach boardwalk (pasarela) with low wooden side rails from Rambla onto the Brava sand!
                var pasarela = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pasarela.name = "Pasarela_LosDedos";
                pasarela.transform.SetParent(lmRoot.transform);
                pasarela.transform.position = new Vector3(771f, y + 0.14f, 522f);
                pasarela.transform.rotation = Quaternion.Euler(0f, 118f, 0f);
                pasarela.transform.localScale = new Vector3(3.4f, 0.22f, 36f);
                pasarela.GetComponent<MeshRenderer>().sharedMaterial = mats["WoodDock"];

                // Coastal dune grass tufts (espartillo) framing the natural Playa Brava dunes around Los Dedos
                var duneRng = new System.Random(511);
                for (int dg = 0; dg < 26; dg++)
                {
                    float ang = (float)duneRng.NextDouble() * Mathf.PI * 2f;
                    float rad = 11f + (float)duneRng.NextDouble() * 34f;
                    float gx = x + Mathf.Cos(ang) * rad;
                    float gz = z + Mathf.Sin(ang) * rad;
                    float gy = SampleTerrainY(terrain, gx, gz);
                    if (gy > 1.3f && gy < 4.1f)
                    {
                        var tuft = SpawnModel("Prop_DuneGrass", new Vector3(gx, gy - 0.05f, gz),
                            Quaternion.Euler(0f, (float)duneRng.NextDouble() * 360f, 0f),
                            Vector3.one * (0.85f + (float)duneRng.NextDouble() * 0.65f), lmRoot.transform, mats, false);
                        ApplyDuneGrassMaterial(tuft, mats);
                    }
                }

                // Beach Parador, Lifeguard Tower & Umbrellas framing Playa Brava right near Los Dedos!
                SpawnModel("Beach_LifeguardTower", new Vector3(818f, SampleTerrainY(terrain, 818f, 492f), 492f), Quaternion.Euler(0f, 125f, 0f), Vector3.one, lmRoot.transform, mats, true);
                SpawnModel("Beach_Parador", new Vector3(755f, SampleTerrainY(terrain, 755f, 470f), 470f), Quaternion.Euler(0f, 128f, 0f), Vector3.one, lmRoot.transform, mats, true);
                for (int u = 0; u < 10; u++)
                {
                    float ux = 766f + u * 10.5f;
                    float uz = 480f + u * 6.8f - (u % 2) * 14f;
                    float uy = SampleTerrainY(terrain, ux, uz);
                    if (uy > 0.7f)
                        SpawnModel("Beach_UmbrellaSet", new Vector3(ux, uy, uz), Quaternion.Euler(0f, 125f + u * 11f, 0f), Vector3.one, lmRoot.transform, mats, false);
                }
                roadNet.pedestrianPoints.Add(new Vector3(786f, y + 0.2f, 514f));
                roadNet.pedestrianPoints.Add(new Vector3(802f, y + 0.2f, 504f));
            }

            // 2. FARO DE PUNTA DEL ESTE v2 + PLAZA DEL FARO PARK (exact OSM -514.8, -705.6) + Rotating Lighthouse Beam!
            {
                float x = -514.8f, z = -705.6f;
                float y = SampleTerrainY(terrain, x, z);
                var faro = SpawnModel("Faro_PuntaDelEste_v2", new Vector3(x, y - 0.3f, z), Quaternion.Euler(0f, 20f, 0f), Vector3.one, lmRoot.transform, mats, true);

                // Lush landscaped Plaza del Faro park lawn + brick promenade paths (eliminates bare terrain!)
                CreateGroundPad(lmRoot.transform, "PlazaDelFaro_Lawn", new Vector3(-472f, y + 0.08f, -692f), new Vector3(112f, 0.25f, 92f), 14f, mats["Grass"]);
                CreateGroundPad(lmRoot.transform, "PlazaDelFaro_BrickWalk", new Vector3(-472f, y + 0.14f, -692f), new Vector3(16f, 0.24f, 88f), 14f, mats["BrickPlaza"]);
                CreateGroundPad(lmRoot.transform, "PlazaDelFaro_CrossWalk", new Vector3(-472f, y + 0.14f, -692f), new Vector3(96f, 0.24f, 14f), 14f, mats["BrickPlaza"]);

                // Poly Haven painted wooden park benches around Plaza del Faro
                Vector3[] faroBenches =
                {
                    new Vector3(-484f, 0f, -680f), new Vector3(-460f, 0f, -682f),
                    new Vector3(-495f, 0f, -705f), new Vector3(-448f, 0f, -706f),
                };
                for (int b = 0; b < faroBenches.Length; b++)
                {
                    float by = SampleTerrainY(terrain, faroBenches[b].x, faroBenches[b].z) + 0.12f;
                    var bench = SpawnModel("PH_painted_wooden_bench", new Vector3(faroBenches[b].x, by, faroBenches[b].z),
                        Quaternion.Euler(0f, b * 90f + 18f, 0f), Vector3.one * 1.15f, lmRoot.transform, mats, false);
                    ApplySingleMaterial(bench, mats["PH_Bench"]);
                }

                // Rotating volumetric beam rig inside the lantern room (40.5m up!)
                var rotor = new GameObject("Faro_BeaconRotor");
                rotor.transform.SetParent(lmRoot.transform);
                rotor.transform.position = new Vector3(x, y + 40.5f, z);

                var spotGo = new GameObject("Faro_Spotlight");
                spotGo.transform.SetParent(rotor.transform, false);
                spotGo.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
                var spot = spotGo.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = Palette.SunAfternoon;
                spot.intensity = 1500f;
                spot.range = 320f;
                spot.spotAngle = 24f;

                // Soft atmospheric translucent volumetric beam cone at night
                var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cone.name = "Faro_VolumetricBeam";
                UnityEngine.Object.DestroyImmediate(cone.GetComponent<Collider>());
                cone.transform.SetParent(rotor.transform, false);
                cone.transform.localPosition = new Vector3(0f, -4f, 54f);
                cone.transform.localRotation = Quaternion.Euler(95f, 0f, 0f);
                cone.transform.localScale = new Vector3(3.8f, 54f, 3.8f);
                var beamMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                beamMat.color = new Color(1.0f, 0.94f, 0.72f, 0.048f);
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

            // 3. IGLESIA DE LA CANDELARIA v2 (exact OSM -412.9, -714.6)
            {
                float x = -412.9f, z = -714.6f;
                float y = SampleTerrainY(terrain, x, z);
                SpawnModel("Iglesia_Candelaria_v2", new Vector3(x, y - 0.20f, z), Quaternion.Euler(0f, -42f, 0f), Vector3.one, lmRoot.transform, mats, true);
            }

            // 4. PUERTO DE PUNTA DEL ESTE v2 (Clean Concrete Quay + Photogrammetry Granite Breakwater + 4 Non-Overlapping Marinas + Fish Stalls + 32 Vessels + Seagulls!)
            {
                var puertoRoot = new GameObject("Landmark_PuertoPuntaDelEste");
                puertoRoot.transform.SetParent(lmRoot.transform);

                // Yacht Club Punta del Este at exact OSM (-403.6, -236.5) + Illuminated Sign
                float ycY = SampleTerrainY(terrain, -403.6f, -236.5f);
                SpawnModel("Puerto_YachtClub", new Vector3(-403.6f, ycY - 0.2f, -236.5f), Quaternion.Euler(0f, -55f, 0f), Vector3.one, puertoRoot.transform, mats, true);
                CreateIlluminatedSignBoard(puertoRoot.transform, "SignBoard_YachtClub", new Vector3(-400f, ycY + 9.2f, -230f), -55f, new Vector2(8.5f, 2.4f), mats["Sign_YachtClub"]);

                // Clean Poured-Concrete Waterfront Quay Promenade & Main Seaward Escollera Breakwater (100% clear of Rambla road!)
                var quayMesh = new MeshBuilder();
                Vector3[][] concreteQuays =
                {
                    new[] { new Vector3(-488f, 2.25f, -156f), new Vector3(-286f, 2.25f, -142f) }, // Seaward waterfront quay promenade (clear of Rambla!)
                    new[] { new Vector3(-488f, 2.40f, -158f), new Vector3(-538f, 2.40f,  175f) }, // Iconic seaward Escollera Breakwater!
                };
                foreach (var cq in concreteQuays)
                {
                    Vector3 dir = (cq[1] - cq[0]).normalized;
                    Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                    AddRibbonBox(quayMesh, cq[0], cq[1], right * 6.5f, 4.4f);
                }
                CreateMeshChild(puertoRoot.transform, "Puerto_ConcreteQuayAndEscollera", quayMesh.Build("Mesh_PuertoQuay"), mats["ConcreteQuay"], true);

                // 4 Neat Non-Overlapping Floating Oak-Deck Marina Finger Piers extending NW into the calm harbor!
                var marinaMesh = new MeshBuilder();
                Vector3[][] fingerPiers =
                {
                    new[] { new Vector3(-460f, 1.85f, -152f), new Vector3(-488f, 1.85f, -20f) },
                    new[] { new Vector3(-415f, 1.85f, -150f), new Vector3(-442f, 1.85f, -26f) },
                    new[] { new Vector3(-370f, 1.85f, -147f), new Vector3(-395f, 1.85f, -34f) },
                    new[] { new Vector3(-324f, 1.85f, -144f), new Vector3(-348f, 1.85f, -44f) },
                };
                foreach (var fp in fingerPiers)
                {
                    Vector3 dir = (fp[1] - fp[0]).normalized;
                    Vector3 right = new Vector3(dir.z, 0f, -dir.x);
                    AddRibbonBox(marinaMesh, fp[0], fp[1], right * 2.5f, 2.6f);
                    roadNet.pedestrianPoints.Add(Vector3.Lerp(fp[0], fp[1], 0.38f) + Vector3.up * 0.25f);
                    // Mooring bollards neatly lining each marina finger pier!
                    for (int k = 1; k <= 5; k++)
                    {
                        Vector3 bp = Vector3.Lerp(fp[0], fp[1], k / 6f) + right * 2.0f;
                        SpawnModel("Puerto_Bollard", bp, Quaternion.identity, Vector3.one, puertoRoot.transform, mats, false);
                    }
                }
                CreateMeshChild(puertoRoot.transform, "Puerto_FloatingMarinas", marinaMesh.Build("Mesh_PuertoMarinas"), mats["WoodDock"], true);

                // Poly Haven Photogrammetry Granite Rocks armoring the outer seaward edge of the Escollera breakwater!
                string[] phRockModels = { "PH_coast_land_rocks_02", "PH_coast_land_rocks_03", "PH_coast_land_rocks_04", "PH_boulder_01" };
                string[] phRockMats   = { "PH_Rock02", "PH_Rock03", "PH_Rock04", "PH_Boulder" };
                var rockRng = new System.Random(1889);
                for (int r = 0; r < 24; r++)
                {
                    float t = r / 23f;
                    Vector3 pt = Vector3.Lerp(new Vector3(-498f, 0.2f, -176f), new Vector3(-550f, 0.2f, 182f), t);
                    pt.x -= 4.5f + (float)rockRng.NextDouble() * 6.5f;
                    int idx = r % phRockModels.Length;
                    float sc = idx == 3 ? (2.2f + (float)rockRng.NextDouble() * 1.4f) : (1.15f + (float)rockRng.NextDouble() * 0.75f);
                    var rock = SpawnModel(phRockModels[idx], pt, Quaternion.Euler(0f, (float)rockRng.NextDouble() * 360f, 0f), Vector3.one * sc, puertoRoot.transform, mats, false);
                    ApplySingleMaterial(rock, mats[phRockMats[idx]]);
                }

                // Iconic Blue & White Artisanal Fish Market Stalls (Puestos de Pesca del Puerto) neatly on the seaward concrete quay (zero road overlap!)
                for (int fs = 0; fs < 6; fs++)
                {
                    Vector3 fpos = Vector3.Lerp(new Vector3(-478f, 2.25f, -157f), new Vector3(-418f, 2.25f, -153f), fs / 5f);
                    SpawnModel("Puerto_FishStall", fpos, Quaternion.Euler(0f, -6f, 0f), Vector3.one * 1.05f, puertoRoot.transform, mats, true);
                }
                CreateIlluminatedSignBoard(puertoRoot.transform, "SignBoard_ParrillaPuerto", new Vector3(-448f, 6.2f, -160f), -6f, new Vector2(7.6f, 2.2f), mats["Sign_ParrillaPuerto"]);

                // 32 Detailed Vessels moored tightly right beside the 4 marina walkways & anchored across calm Playa Mansa bay!
                string[] boatModels = { "Boat_SuperYacht_v2", "Boat_Catamaran", "Boat_Sailboat", "Boat_FishingBoat" };
                Vector3[] boatSpots =
                {
                    // Slip row 1 (right alongside Promenade & Pier 1 — hero foreground yachts cleanly clear of quay!)
                    new Vector3(-474f, 0.0f, -132f), new Vector3(-478f, 0.0f, -104f), new Vector3(-482f, 0.0f, -76f), new Vector3(-486f, 0.0f, -48f),
                    new Vector3(-446f, 0.0f, -130f), new Vector3(-451f, 0.0f, -102f), new Vector3(-456f, 0.0f, -74f), new Vector3(-460f, 0.0f, -46f),
                    // Slip row 2 (between Pier 2 & Pier 3)
                    new Vector3(-400f, 0.0f, -130f), new Vector3(-405f, 0.0f, -100f), new Vector3(-410f, 0.0f, -72f), new Vector3(-416f, 0.0f, -44f),
                    new Vector3(-355f, 0.0f, -128f), new Vector3(-360f, 0.0f, -98f),  new Vector3(-366f, 0.0f, -68f), new Vector3(-372f, 0.0f, -40f),
                    // Escollera inner anchorage (dramatic superyachts & catamarans!)
                    new Vector3(-512f, 0.0f, -65f),  new Vector3(-516f, 0.0f, -12f),  new Vector3(-520f, 0.0f,  42f), new Vector3(-522f, 0.0f, 102f),
                    // Anchored across tranquil Playa Mansa bay framing Isla Gorriti & Enjoy Mansa!
                    new Vector3(-620f, 0.0f,  95f),  new Vector3(-590f, 0.0f, 240f),  new Vector3(-460f, 0.0f, 345f), new Vector3(-340f, 0.0f, 430f),
                    new Vector3(-240f, 0.0f, 540f),  new Vector3(-165f, 0.0f, 680f),  new Vector3(-710f, 0.0f, -85f), new Vector3(-745f, 0.0f, 260f),
                    new Vector3(-540f, 0.0f, 520f),  new Vector3(-390f, 0.0f, 760f),  new Vector3(-210f, 0.0f, 890f), new Vector3(-660f, 0.0f, 410f),
                };

                for (int i = 0; i < boatSpots.Length; i++)
                {
                    string bModel = boatModels[i % boatModels.Length];
                    float yaw = (i < 20) ? -72f + (i % 3) * 5f : (i * 47f) % 360f;
                    var boat = SpawnModel(bModel, boatSpots[i], Quaternion.Euler(0f, yaw, 0f), Vector3.one * 1.12f, puertoRoot.transform, mats, false);
                    if (boat != null)
                    {
                        var bob = boat.AddComponent<FloatingBoatBob>();
                        bob.bobAmplitude = (i < 20) ? 0.08f : 0.15f;
                        bob.phaseOffset = i * 0.85f;
                    }
                }

                // Lounging Sea Lions (Lobos Marinos) resting right beside the Puerto fish stalls & marinas!
                Vector3[] sealionSpots =
                {
                    new Vector3(-466f, 2.18f, -166f),
                    new Vector3(-456f, 2.18f, -164f),
                    new Vector3(-420f, 1.88f, -155f),
                    new Vector3(-506f, 2.38f, -35f),
                };
                for (int i = 0; i < sealionSpots.Length; i++)
                    SpawnModel("SeaLion_Puerto", sealionSpots[i], Quaternion.Euler(0f, i * 85f, 0f), Vector3.one * 1.15f, puertoRoot.transform, mats, false);

                // Soaring Flocks of Coastal Seagulls circling above Puerto de Punta del Este & Playa Brava!
                Vector3[] gullCenters = { new Vector3(-435f, 26f, -95f), new Vector3(790f, 24f, 515f) };
                for (int g = 0; g < 12; g++)
                {
                    Vector3 c = gullCenters[g < 7 ? 0 : 1];
                    var gull = SpawnModel("Seagull_Bird", c + new Vector3(0f, (g % 4) * 3.2f, 0f), Quaternion.identity, Vector3.one * 1.35f, puertoRoot.transform, mats, false);
                    if (gull != null)
                    {
                        var flock = gull.AddComponent<CoastalSeagullFlock>();
                        flock.orbitRadius = 26f + (g % 5) * 9f;
                        flock.orbitSpeed = 0.22f + (g % 3) * 0.06f;
                        flock.phaseOffset = g * 1.15f;
                    }
                }
            }

            // 5. PLAZA ARTIGAS & AV. GORLERO LIVING COMMERCIAL STRIP (exact OSM 207.6, -77.8)
            {
                var plazaRoot = new GameObject("Landmark_PlazaArtigas");
                plazaRoot.transform.SetParent(lmRoot.transform);
                float px = 207.6f, pz = -77.8f;
                float py = SampleTerrainY(terrain, px, pz);

                // Lush Brick Plaza & Garden Lawn Base Pad (flush with sidewalks & zero bare sand inside Plaza Artigas!)
                CreateGroundPad(plazaRoot.transform, "PlazaArtigas_BrickBase", new Vector3(px, py + 0.04f, pz), new Vector3(62f, 0.12f, 62f), 45f, mats["BrickPlaza"]);
                CreateGroundPad(plazaRoot.transform, "PlazaArtigas_LawnRing",  new Vector3(px, py + 0.07f, pz), new Vector3(36f, 0.12f, 36f), 45f, mats["Grass"]);

                // Stone Monument Fountain centerpiece!
                SpawnModel("Plaza_Fountain", new Vector3(px, py + 0.22f, pz), Quaternion.identity, Vector3.one * 1.1f, plazaRoot.transform, mats, true);

                // 12 Artisan Market Stalls framing the square
                for (int row = -1; row <= 1; row++)
                {
                    for (int col = -1; col <= 1; col++)
                    {
                        if (row == 0 && col == 0) continue;
                        float sx = px + col * 14f;
                        float sz = pz + row * 14f;
                        float sy = SampleTerrainY(terrain, sx, sz) + 0.18f;
                        SpawnModel("PlazaArtigas_Stall", new Vector3(sx, sy, sz), Quaternion.Euler(0f, 45f, 0f), Vector3.one, plazaRoot.transform, mats, true);
                    }
                }

                // Poly Haven Modular Street Seating + Gorlero Sidewalk Cafe Tables & Parasols + Bus Stop!
                var modSeat = SpawnModel("PH_modular_street_seating", new Vector3(px + 18f, py + 0.22f, pz + 4f), Quaternion.Euler(0f, 45f, 0f), Vector3.one * 1.15f, plazaRoot.transform, mats, false);
                SpawnModel("PH_modular_street_seating", new Vector3(px - 18f, py + 0.22f, pz - 4f), Quaternion.Euler(0f, 225f, 0f), Vector3.one * 1.15f, plazaRoot.transform, mats, false);

                Vector3[] cafeSpots =
                {
                    new Vector3(230f, 0f, -52f), new Vector3(238f, 0f, -42f), new Vector3(246f, 0f, -34f),
                    new Vector3(178f, 0f, -104f), new Vector3(168f, 0f, -114f), new Vector3(284f, 0f, 14f),
                    new Vector3(312f, 0f, 42f), new Vector3(340f, 0f, 70f),
                };
                for (int cf = 0; cf < cafeSpots.Length; cf++)
                {
                    float cy = SampleTerrainY(terrain, cafeSpots[cf].x, cafeSpots[cf].z) + 0.18f;
                    SpawnModel("Gorlero_CafeSet", new Vector3(cafeSpots[cf].x, cy, cafeSpots[cf].z), Quaternion.Euler(0f, 45f + cf * 15f, 0f), Vector3.one, plazaRoot.transform, mats, false);
                }

                // Uruguayan Bus Stops along Av. Gorlero & Rambla Brava
                SpawnModel("Gorlero_BusStop", new Vector3(256f, SampleTerrainY(terrain, 256f, -22f) + 0.18f, -22f), Quaternion.Euler(0f, -45f, 0f), Vector3.one, plazaRoot.transform, mats, true);
                SpawnModel("Gorlero_BusStop", new Vector3(715f, SampleTerrainY(terrain, 715f, 545f) + 0.18f, 545f), Quaternion.Euler(0f, 38f, 0f), Vector3.one, plazaRoot.transform, mats, true);

                // Iconic Illuminated Punta del Este Business Storefront Signs along Av. Gorlero & Calle 20!
                var shopSigns = new (string matKey, float x, float z, float yaw)[]
                {
                    ("Sign_ChurrosManolo", 224f,  -40f,  135f),
                    ("Sign_CafeGorlero",   248f,  -20f,  -45f),
                    ("Sign_Heladeria",     192f,  -95f,  135f),
                    ("Sign_CasinoNogaro",  325f,   58f,  -45f),
                    ("Sign_Boutique20",    265f,  -88f,  -45f),
                };
                foreach (var ss in shopSigns)
                {
                    float sy = SampleTerrainY(terrain, ss.x, ss.z) + 5.2f;
                    CreateIlluminatedSignBoard(plazaRoot.transform, "Shop_" + ss.matKey, new Vector3(ss.x, sy, ss.z), ss.yaw, new Vector2(6.4f, 1.95f), mats[ss.matKey]);
                }

                roadNet.pedestrianPoints.Add(new Vector3(px, py + 0.25f, pz));
            }

            // 6. PUNTA DE LAS SALINAS ROCKY SOUTHERN TIP (La Glorieta Gazebo + 36 Poly Haven Photogrammetry Granite Rocks!)
            {
                var rockRoot = new GameObject("Landmark_PuntaSalinasRocks");
                rockRoot.transform.SetParent(lmRoot.transform);

                // Iconic Punta de las Salinas viewing gazebo (La Glorieta) right on the open rocky southern headland overlooking the ocean!
                float gx = -512f, gz = -1046f;
                float gy = Mathf.Max(SampleTerrainY(terrain, gx, gz), 2.1f);
                SpawnModel("PuntaSalinas_Glorieta", new Vector3(gx, gy - 0.12f, gz), Quaternion.Euler(0f, 18f, 0f), Vector3.one * 1.15f, rockRoot.transform, mats, true);

                string[] phRockModels = { "PH_coast_land_rocks_02", "PH_coast_land_rocks_03", "PH_coast_land_rocks_04", "PH_boulder_01" };
                string[] phRockMats   = { "PH_Rock02", "PH_Rock03", "PH_Rock04", "PH_Boulder" };
                var rng = new System.Random(777);
                for (int i = 0; i < 36; i++)
                {
                    float ang = Mathf.Lerp(-168f, 18f, i / 35f) * Mathf.Deg2Rad;
                    float rad = 82f + (float)rng.NextDouble() * 62f;
                    float rx = -505f + Mathf.Cos(ang) * rad;
                    float rz = -940f + Mathf.Sin(ang) * rad * 0.78f;
                    float ry = SampleTerrainY(terrain, rx, rz);
                    if (ry > -2.2f && ry < 3.8f)
                    {
                        int idx = i % phRockModels.Length;
                        float sc = idx == 3 ? (2.2f + (float)rng.NextDouble() * 1.8f) : (1.25f + (float)rng.NextDouble() * 0.95f);
                        var rock = SpawnModel(phRockModels[idx], new Vector3(rx, ry - 0.35f, rz),
                            Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * sc, rockRoot.transform, mats, true);
                        ApplySingleMaterial(rock, mats[phRockMats[idx]]);
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

                // Palm trees lining the Rambla & Av. Gorlero every ~2-3 nodes (lush 24-frond Phoenix A/B/C!)
                if (i % 2 == 0 && palmCount < 245)
                {
                    float side = (i % 4 == 0) ? 1f : -1f;
                    Vector3 pPos = node.position + right * (side * 8.6f);
                    pPos.y = SampleTerrainY(terrain, pPos.x, pPos.z) - 0.15f;
                    if (pPos.y > 1.2f)
                    {
                        int pMod = palmCount % 3;
                        string pm = pMod == 0 ? "Palm_Phoenix_A" : (pMod == 1 ? "Palm_Phoenix_B" : "Palm_Phoenix_C");
                        float sc = 0.85f + (float)rng.NextDouble() * 0.35f;
                        var palm = SpawnModel(pm, pPos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * sc, propsRoot.transform, mats, false);
                        ApplyPalmMaterials(palm, mats);
                        palmCount++;
                    }
                }

                // Rambla Streetlamps every 4th avenue/rambla node
                if (i % 4 == 1 && lampCount < 90)
                {
                    Vector3 lPos = node.position + right * 7.4f;
                    lPos.y = SampleTerrainY(terrain, lPos.x, lPos.z);
                    if (lPos.y > 1.8f)
                    {
                        var lamp = SpawnModel("Prop_RamblaLamp", lPos, Quaternion.Euler(0f, yaw - 90f, 0f), Vector3.one, propsRoot.transform, mats, false);
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

                // Coastal Poly Haven Painted Wooden Benches on Rambla promenade
                if (node.isRambla && i % 6 == 2 && benchCount < 42)
                {
                    Vector3 bPos = node.position - right * 8.2f;
                    bPos.y = SampleTerrainY(terrain, bPos.x, bPos.z);
                    if (bPos.y > 2.2f)
                    {
                        var bench = SpawnModel("PH_painted_wooden_bench", bPos, Quaternion.Euler(0f, yaw + 90f, 0f), Vector3.one * 1.15f, propsRoot.transform, mats, false);
                        ApplySingleMaterial(bench, mats["PH_Bench"]);
                        benchCount++;
                    }
                }

                // Functional Cycling Traffic Lights (Green/Yellow/Red!) at major intersections (3+ neighbors)
                if (node.neighbors.Count >= 3 && i % 5 == 0 && tlCount < 16)
                {
                    Vector3 tlPos = node.position + right * 7.4f;
                    tlPos.y = SampleTerrainY(terrain, tlPos.x, tlPos.z);
                    var tlObj = SpawnModel("Prop_TrafficLight", tlPos, Quaternion.Euler(0f, yaw - 90f, 0f), Vector3.one, propsRoot.transform, mats, false);
                    AttachWorkingTrafficLight(tlObj, tlCount * 4.5f);
                    tlCount++;
                }
            }

            // Realistic Branching Maritime Pines (Pine_Maritime_A/B — replaces cartoony 3-sphere pines!) around Plaza del Faro, Plaza Artigas & Playa Mansa dunes
            Vector3[] pineCenters =
            {
                new Vector3(-472.0f, 0f, -692.0f), // Plaza del Faro (ring framing Faro & Candelaria without blocking views!)
                new Vector3( 207.6f, 0f,  -77.8f), // Plaza Artigas
                new Vector3( 260.0f, 0f,  720.0f), // Playa Mansa dunes
                new Vector3( 310.0f, 0f,  920.0f), // Mansa Parada 2
            };
            for (int pc = 0; pc < pineCenters.Length; pc++)
            {
                var c = pineCenters[pc];
                for (int k = 0; k < 12; k++)
                {
                    float ang = k * 30f * Mathf.Deg2Rad;
                    float rad = (pc == 0 ? 34f : 22f) + (k % 3) * 14f;
                    float px = c.x + Mathf.Cos(ang) * rad;
                    float pz = c.z + Mathf.Sin(ang) * rad;
                    float py = SampleTerrainY(terrain, px, pz);
                    if (py > 2.0f)
                    {
                        string pineFbx = (k % 2 == 0) ? "Pine_Maritime_A" : "Pine_Maritime_B";
                        var pine = SpawnModel(pineFbx, new Vector3(px, py - 0.1f, pz), Quaternion.Euler(0f, k * 47f, 0f), Vector3.one * (0.92f + (k % 3) * 0.18f), propsRoot.transform, mats, false);
                        ApplyPineMaterials(pine, mats);
                    }
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
                ("Cabrio",        "Car_Cabrio_Punta",     "Punta Spyder Cabrio",   VehicleArchetype.Cabrio,        2550f, 156f, false),
                ("SportsGT",      "Car_SportsGT",         "Atlántico V8 GT",       VehicleArchetype.SportsGT,      3100f, 182f, false),
                ("BeachSUV",      "Car_BeachSUV",         "Mansa 4x4 SUV",         VehicleArchetype.BeachSUV,      2750f, 136f, false),
                ("CitySedan",     "Car_CitySedan",        "Gorlero Sedán",         VehicleArchetype.CitySedan,     2200f, 130f, false),
                ("Taxi",          "Car_TaxiPunta",        "Taxi Punta del Este",   VehicleArchetype.Taxi,          2250f, 132f, false),
                ("PoliceCruiser", "Car_PoliciaMaldonado", "Patrullero Maldonado",  VehicleArchetype.PoliceCruiser, 3000f, 168f, true),
            };

            foreach (var sp in specs)
            {
                var tpl = CreateDrivableVehicleObject(sp.key, sp.fbx, sp.display, sp.arch, sp.torque, sp.maxKmh, sp.police, mats);
                tpl.transform.SetParent(templatesRoot.transform);
                tpl.SetActive(false);
                dict[sp.key] = tpl;
            }

            // Spawn 12 parked, ready-to-drive sculpted v2 vehicles right at iconic Punta del Este spots!
            var parkedSpots = new (string key, float x, float z, float yaw)[]
            {
                ("Cabrio",        732f,  526f, 222f), // Right next to Player Spawn at Parada 1 / Los Dedos!
                ("SportsGT",      226f,  -46f, 224f), // Av. Gorlero & Plaza Artigas
                ("Taxi",          244f,  -30f, 224f), // Iconic Punta Taxi outside Cafe Gorlero!
                ("BeachSUV",     -386f, -206f, -45f), // Puerto de Punta del Este
                ("Taxi",         -412f, -192f, -85f), // Taxi waiting at Puerto de Punta del Este!
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

            // Driver Seat Pivot (snugly seated inside the bucket seat behind the steering wheel!)
            var seatGo = new GameObject("DriverSeatPivot");
            seatGo.transform.SetParent(root.transform, false);
            float seatY = arch == VehicleArchetype.BeachSUV ? 0.04f : (arch == VehicleArchetype.Cabrio ? -0.10f : -0.14f);
            seatGo.transform.localPosition = new Vector3(-0.36f, seatY, -0.18f);
            seatGo.transform.localRotation = Quaternion.identity;
            ctrl.driverSeatPivot = seatGo.transform;

            // Tire drift smoke, dark rubber skidmark TrailRenderers & hard-collision sparks!
            ctrl.driftSmokeL = CreateDriftSmoke(root.transform, "DriftSmoke_L", new Vector3(-0.86f, 0.16f, -1.32f));
            ctrl.driftSmokeR = CreateDriftSmoke(root.transform, "DriftSmoke_R", new Vector3( 0.86f, 0.16f, -1.32f));
            ctrl.skidTrailL  = CreateSkidTrail(root.transform, "SkidTrail_L", new Vector3(-0.82f, 0.06f, -1.32f), mats["Skidmark"]);
            ctrl.skidTrailR  = CreateSkidTrail(root.transform, "SkidTrail_R", new Vector3( 0.82f, 0.06f, -1.32f), mats["Skidmark"]);
            ctrl.crashSparks = CreateCrashSparks(root.transform, "CrashSparks");

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

            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Char_Humanoid_v2.fbx");
            if (charPrefab != null)
            {
                // Clean upright wrapper preserves the FBX's imported Z-up -> Y-up rotation while rotating cleanly around Y!
                var visualWrap = new GameObject("Visual_Character");
                visualWrap.transform.SetParent(root.transform, false);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                model.name = "Char_Model";
                model.transform.SetParent(visualWrap.transform, false);
                UpgradeFbxMaterialsToUrp(model, mats);

                // Orient wrapper strictly so sunglasses face root's LOCAL +Z (keeping character upright!)
                var shades = FindDeep(model.transform, "Sunglasses_Bridge");
                if (shades != null)
                {
                    var sr = shades.GetComponent<Renderer>();
                    Vector3 shadeCenter = sr != null ? sr.bounds.center : shades.position;
                    float localZ = root.transform.InverseTransformPoint(shadeCenter).z;
                    if (localZ < 0f)
                        visualWrap.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }

                foot.visualRoot = visualWrap.transform;
                foot.charAnim = model.AddComponent<ProceduralCharacterAnimator>();
            }

            return foot;
        }

        private static GameObject BuildPedestrianTemplate()
        {
            var tpl = new GameObject("Pedestrian_Template");
            var charPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/Char_Humanoid_v2.fbx");
            if (charPrefab != null)
            {
                var visualWrap = new GameObject("Visual");
                visualWrap.transform.SetParent(tpl.transform, false);

                var model = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
                model.name = "Char_Model";
                model.transform.SetParent(visualWrap.transform, false);
                UpgradeFbxMaterialsToUrp(model, null);
                var shades = FindDeep(model.transform, "Sunglasses_Bridge");
                if (shades != null)
                {
                    var sr = shades.GetComponent<Renderer>();
                    Vector3 shadeCenter = sr != null ? sr.bounds.center : shades.position;
                    if (tpl.transform.InverseTransformPoint(shadeCenter).z < 0f)
                        visualWrap.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
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

            // Title Cinematic Drone Camera framing sunlit Los Dedos (La Mano v2), Punta Skyline & PureSky clouds!
            var titleGo = new GameObject("CM_TitleDroneCamera");
            titleGo.transform.SetParent(camRoot.transform);
            var titleCam = titleGo.AddComponent<CinemachineCamera>();
            titleCam.Priority = 30;
            titleCam.Lens.FieldOfView = 50f;
            titleCam.Lens.NearClipPlane = 0.3f;
            titleCam.Lens.FarClipPlane = 3800f;
            titleGo.transform.position = new Vector3(774f, 8.6f, 490f);
            titleGo.transform.LookAt(new Vector3(794.6f, 11.4f, 511.3f));

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
            ctrl.skyboxDay = mats["SkyboxDay"];
            ctrl.skyboxSunset = mats["SkyboxSunset"];
            ctrl.skyboxNight = mats["SkyboxNight"];
            ctrl.globalReflectionProbe = probe;
            ctrl.oceanMaterial = mats["Ocean"];
            ctrl.glassTowerMaterial = mats["GlassTower"];
            ctrl.gorleroFacadeMaterial = mats["Gorlero"];
            ctrl.villaFacadeMaterial = mats["Villa"];
            ctrl.extraFacadeMaterials = new List<Material> { mats["BronzeTower"], mats["TerracottaRes"] };
            ctrl.emissiveSignMaterials = new List<Material>
            {
                mats["Sign_Enjoy"], mats["Sign_ChurrosManolo"], mats["Sign_CafeGorlero"],
                mats["Sign_Heladeria"], mats["Sign_CasinoNogaro"], mats["Sign_Boutique20"],
                mats["Sign_ParrillaPuerto"], mats["Sign_YachtClub"]
            };
            ctrl.faroBeaconRotor = extras.faroRotor;
            ctrl.faroSpotLight = extras.faroSpot;
            ctrl.faroBeamConeRenderer = extras.faroBeamCone;
            ctrl.streetPointLights = streetLights;
            ctrl.ApplyMode(TimeOfDayMode.SunnyAfternoon);

            return ctrl;
        }

        // --- V2 SCENARIO, FOLIAGE & VFX HELPERS ---

        private static void CreateGroundPad(Transform parent, string name, Vector3 pos, Vector3 scale, float yaw, Material mat)
        {
            // World-scale UV-tiled ground slab (eliminates ALL texture stretching on large plaza lawns & brick walkways!)
            var mb = new MeshBuilder();
            float hx = scale.x * 0.5f;
            float hy = scale.y * 0.5f;
            float hz = scale.z * 0.5f;
            float uTile = Mathf.Max(1f, scale.x / 3.8f);
            float vTile = Mathf.Max(1f, scale.z / 3.8f);

            mb.AddQuadUp(
                new Vector3(-hx, hy, -hz), new Vector3(-hx, hy,  hz),
                new Vector3( hx, hy,  hz), new Vector3( hx, hy, -hz),
                new Vector2(0f, 0f), new Vector2(0f, vTile), new Vector2(uTile, vTile), new Vector2(uTile, 0f));

            // Clean thin side skirts
            mb.AddQuadDoubleSided(new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(-hx, -hy, -hz), Vector2.zero, new Vector2(uTile, 0), new Vector2(uTile, 0.1f), new Vector2(0, 0.1f));
            mb.AddQuadDoubleSided(new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz), Vector2.zero, new Vector2(uTile, 0), new Vector2(uTile, 0.1f), new Vector2(0, 0.1f));
            mb.AddQuadDoubleSided(new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz), new Vector3(hx, -hy, -hz), Vector2.zero, new Vector2(vTile, 0), new Vector2(vTile, 0.1f), new Vector2(0, 0.1f));
            mb.AddQuadDoubleSided(new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, -hy, -hz), new Vector3(-hx, -hy, hz), Vector2.zero, new Vector2(vTile, 0), new Vector2(vTile, 0.1f), new Vector2(0, 0.1f));

            var mesh = mb.Build("Mesh_" + name);
            SaveMeshAsset(mesh, "Mesh_" + name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = scale;
        }

        private static void CreateIlluminatedSignBoard(Transform parent, string name, Vector3 pos, float yaw, Vector2 size, Material signMat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "SignFrame";
            UnityEngine.Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(root.transform, false);
            frame.transform.localScale = new Vector3(size.x + 0.24f, size.y + 0.24f, 0.22f);
            frame.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Mat_DarkPlinth.mat");

            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            front.name = "SignFace_Front";
            UnityEngine.Object.DestroyImmediate(front.GetComponent<Collider>());
            front.transform.SetParent(root.transform, false);
            front.transform.localPosition = new Vector3(0f, 0f, -0.125f);
            front.transform.localScale = new Vector3(size.x, size.y, 1f);
            front.GetComponent<MeshRenderer>().sharedMaterial = signMat;

            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "SignFace_Back";
            UnityEngine.Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.125f);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            back.transform.localScale = new Vector3(size.x, size.y, 1f);
            back.GetComponent<MeshRenderer>().sharedMaterial = signMat;
        }

        private static void ApplySingleMaterial(GameObject root, Material mat)
        {
            if (root == null || mat == null) return;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                mr.sharedMaterial = mat;
        }

        private static void ApplyPineMaterials(GameObject pine, Dictionary<string, Material> mats)
        {
            if (pine == null) return;
            foreach (var mr in pine.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.name.Contains("Canopy"))
                    mr.sharedMaterial = mats["PineNeedles"];
                else
                    mr.sharedMaterial = mats["PalmBark"];
            }
        }

        private static void ApplyDuneGrassMaterial(GameObject tuft, Dictionary<string, Material> mats)
        {
            if (tuft == null) return;
            foreach (var mr in tuft.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterial = mats["DuneGrass"];
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static void AttachWorkingTrafficLight(GameObject tlObj, float phaseOffset)
        {
            if (tlObj == null) return;
            var ctrl = tlObj.AddComponent<TrafficLightController>();
            ctrl.phaseOffset = phaseOffset;
            ctrl.cycleDuration = 18f;

            var baseMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Mat_DarkPlinth.mat");
            Renderer MakeLens(string lensName, float yOff)
            {
                var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sp.name = lensName;
                UnityEngine.Object.DestroyImmediate(sp.GetComponent<Collider>());
                sp.transform.SetParent(tlObj.transform, false);
                sp.transform.localPosition = new Vector3(0f, yOff, 0.34f);
                sp.transform.localScale = Vector3.one * 0.24f;
                var mr = sp.GetComponent<MeshRenderer>();
                if (baseMat != null) mr.sharedMaterial = baseMat;
                return mr;
            }
            ctrl.redLens    = MakeLens("Lens_Red",    5.35f);
            ctrl.yellowLens = MakeLens("Lens_Yellow", 5.02f);
            ctrl.greenLens  = MakeLens("Lens_Green",  4.69f);
        }

        private static TrailRenderer CreateSkidTrail(Transform parent, string name, Vector3 localPos, Material skidMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 6.5f;
            tr.startWidth = 0.26f;
            tr.endWidth = 0.24f;
            tr.minVertexDistance = 0.25f;
            tr.alignment = LineAlignment.TransformZ;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            tr.sharedMaterial = skidMat;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.emitting = false;
            return tr;
        }

        private static ParticleSystem CreateCrashSparks(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.25f;
            main.startLifetime = 0.35f;
            main.startSpeed = 7.5f;
            main.startSize = 0.14f;
            main.gravityModifier = 1.4f;
            main.startColor = Palette.HudGoldAccent;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var pMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            pMat.color = Palette.HudGoldAccent;
            pr.sharedMaterial = pMat;
            return ps;
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
