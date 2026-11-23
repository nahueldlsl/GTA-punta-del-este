using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GtaPunta.Core;

namespace GtaPunta.Atmosphere
{
    public enum TimeOfDayMode
    {
        SunnyAfternoon,
        MansaSunset,
        CoastalNight
    }

    /// <summary>
    /// Controls Punta del Este's iconic coastal daylight, golden Mansa sunset,
    /// and coastal night atmosphere (including El Faro's rotating lighthouse beam,
    /// glowing tower windows, streetlamps, skybox, ocean reflections, and URP post-processing).
    /// </summary>
    public class CoastalAtmosphereController : MonoBehaviour
    {
        [Header("Scene References")]
        public Light sunLight;
        public Volume postVolume;
        public Material skyboxMaterial;
        public Material skyboxDay;
        public Material skyboxSunset;
        public Material skyboxNight;
        public ReflectionProbe globalReflectionProbe;
        public Material oceanMaterial;
        public Material glassTowerMaterial;
        public Material gorleroFacadeMaterial;
        public Material villaFacadeMaterial;
        public List<Material> extraFacadeMaterials = new List<Material>();
        public List<Material> emissiveSignMaterials = new List<Material>();
        public Transform faroBeaconRotor;
        public Light faroSpotLight;
        public Renderer faroBeamConeRenderer;
        public List<Light> streetPointLights = new List<Light>();

        [Header("State")]
        public TimeOfDayMode currentMode = TimeOfDayMode.SunnyAfternoon;
        public float faroRotationSpeed = 34f;

        private void Start()
        {
            ApplyMode(currentMode);
        }

        private void Update()
        {
            if (faroBeaconRotor != null)
                faroBeaconRotor.Rotate(Vector3.up, faroRotationSpeed * Time.deltaTime, Space.Self);
        }

        public void CycleTimeOfDay()
        {
            int next = ((int)currentMode + 1) % 3;
            SetTimeOfDay((TimeOfDayMode)next);
        }

        public void SetTimeOfDay(TimeOfDayMode mode)
        {
            currentMode = mode;
            ApplyMode(mode);
        }

        public string GetTimeLabel()
        {
            return currentMode switch
            {
                TimeOfDayMode.SunnyAfternoon => "Tarde Soleada · 16:30",
                TimeOfDayMode.MansaSunset    => "Atardecer en la Mansa · 19:20",
                TimeOfDayMode.CoastalNight   => "Noche Costera · 22:15",
                _ => "Punta del Este"
            };
        }

        public void ApplyMode(TimeOfDayMode mode)
        {
            Color sunCol, skyTop, skyHoriz, ambSky, ambEq, ambGnd, fogCol, oceanSky, oceanHoriz;
            float sunIntensity, fogDensity, windowEmit, bloomInt, postExp;
            Vector3 sunEuler;
            bool nightLights;

            switch (mode)
            {
                case TimeOfDayMode.MansaSunset:
                    // Iconic golden-hour sun setting west-northwest over Playa Mansa & Isla Gorriti!
                    sunEuler = new Vector3(13f, -72f, 0f);
                    sunCol = Palette.SunSunset;
                    sunIntensity = 1.65f;
                    skyTop = Palette.SkyZenithSunset;
                    skyHoriz = Palette.SkyHorizonSunset;
                    ambSky = new Color(0.55f, 0.44f, 0.58f);
                    ambEq = new Color(0.76f, 0.54f, 0.44f);
                    ambGnd = new Color(0.28f, 0.22f, 0.20f);
                    fogCol = new Color(0.86f, 0.62f, 0.48f);
                    fogDensity = 0.00042f;
                    oceanSky = new Color(0.74f, 0.52f, 0.48f);
                    oceanHoriz = new Color(1.0f, 0.68f, 0.36f);
                    windowEmit = 0.75f;
                    bloomInt = 0.55f;
                    postExp = 0.10f;
                    nightLights = true;
                    break;

                case TimeOfDayMode.CoastalNight:
                    sunEuler = new Vector3(34f, -125f, 0f);
                    sunCol = Palette.MoonNight;
                    sunIntensity = 0.56f;
                    skyTop = Palette.Hex("#0B1A33");
                    skyHoriz = Palette.Hex("#1E385C");
                    ambSky = new Color(0.20f, 0.30f, 0.48f);
                    ambEq = new Color(0.18f, 0.24f, 0.36f);
                    ambGnd = new Color(0.10f, 0.13f, 0.19f);
                    fogCol = new Color(0.10f, 0.18f, 0.30f);
                    fogDensity = 0.00042f;
                    oceanSky = new Color(0.14f, 0.26f, 0.45f);
                    oceanHoriz = new Color(0.50f, 0.70f, 0.92f);
                    windowEmit = 2.15f;
                    bloomInt = 0.78f;
                    postExp = 0.32f;
                    nightLights = true;
                    break;

                default: // SunnyAfternoon
                    // Crisp summer coastal sun angled from WNW casting architectural depth across Gorlero & Brava
                    sunEuler = new Vector3(38f, -52f, 0f);
                    sunCol = Palette.SunAfternoon;
                    sunIntensity = 1.48f;
                    skyTop = Palette.SkyZenithDay;
                    skyHoriz = Palette.SkyHorizonDay;
                    ambSky = new Color(0.42f, 0.62f, 0.84f);
                    ambEq = new Color(0.74f, 0.78f, 0.80f);
                    ambGnd = Palette.GroundWarmBounce;
                    fogCol = new Color(0.74f, 0.86f, 0.95f);
                    fogDensity = 0.00028f;
                    oceanSky = new Color(0.46f, 0.74f, 0.94f);
                    oceanHoriz = Palette.SunAfternoon;
                    windowEmit = 0.05f;
                    bloomInt = 0.30f;
                    postExp = 0.08f;
                    nightLights = false;
                    break;
            }

            if (sunLight != null)
            {
                sunLight.transform.rotation = Quaternion.Euler(sunEuler);
                sunLight.color = sunCol;
                sunLight.intensity = sunIntensity;
            }

            // Pick real Poly Haven PureSky Panoramic HDRI skybox when assigned
            Material activeSky = mode switch
            {
                TimeOfDayMode.MansaSunset  => skyboxSunset != null ? skyboxSunset : skyboxMaterial,
                TimeOfDayMode.CoastalNight => skyboxNight != null ? skyboxNight : skyboxMaterial,
                _                          => skyboxDay != null ? skyboxDay : skyboxMaterial
            };

            if (activeSky != null)
            {
                if (activeSky.HasProperty("_SkyTint")) activeSky.SetColor("_SkyTint", skyTop);
                if (activeSky.HasProperty("_GroundColor")) activeSky.SetColor("_GroundColor", skyHoriz);
                if (activeSky.HasProperty("_AtmosphereThickness"))
                    activeSky.SetFloat("_AtmosphereThickness", mode == TimeOfDayMode.MansaSunset ? 1.55f : (mode == TimeOfDayMode.CoastalNight ? 0.35f : 0.95f));
                if (activeSky.HasProperty("_Exposure"))
                {
                    bool isPanoramic = activeSky.shader != null && activeSky.shader.name.Contains("Panoramic");
                    float expVal = isPanoramic
                        ? (mode == TimeOfDayMode.CoastalNight ? 0.85f : (mode == TimeOfDayMode.MansaSunset ? 1.15f : 1.05f))
                        : (mode == TimeOfDayMode.CoastalNight ? 0.28f : (mode == TimeOfDayMode.MansaSunset ? 1.15f : 1.25f));
                    activeSky.SetFloat("_Exposure", expVal);
                }
                RenderSettings.skybox = activeSky;
            }

            bool hasPanoramicSky = activeSky != null && activeSky.shader != null && activeSky.shader.name.Contains("Panoramic");
            if (hasPanoramicSky)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = mode == TimeOfDayMode.CoastalNight ? 0.72f : (mode == TimeOfDayMode.MansaSunset ? 0.92f : 1.05f);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = mode == TimeOfDayMode.CoastalNight ? 0.65f : 1.0f;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = ambSky;
                RenderSettings.ambientEquatorColor = ambEq;
                RenderSettings.ambientGroundColor = ambGnd;
            }

            DynamicGI.UpdateEnvironment();
            if (globalReflectionProbe != null)
                globalReflectionProbe.RenderProbe();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogCol;
            RenderSettings.fogDensity = fogDensity;

            if (oceanMaterial != null)
            {
                oceanMaterial.SetColor("_SkyReflectColor", oceanSky);
                oceanMaterial.SetColor("_HorizonGlowColor", oceanHoriz);
            }

            Color warmGlow = new Color(1.0f, 0.86f, 0.60f) * windowEmit;
            UpdateEmission(glassTowerMaterial, warmGlow);
            UpdateEmission(gorleroFacadeMaterial, new Color(1.0f, 0.82f, 0.54f) * windowEmit);
            UpdateEmission(villaFacadeMaterial, new Color(1.0f, 0.80f, 0.48f) * windowEmit);
            foreach (var extraMat in extraFacadeMaterials)
                UpdateEmission(extraMat, warmGlow);

            float signEmit = mode == TimeOfDayMode.CoastalNight ? 1.85f : (mode == TimeOfDayMode.MansaSunset ? 0.95f : 0.12f);
            foreach (var signMat in emissiveSignMaterials)
                UpdateEmission(signMat, Color.white * signEmit);

            if (faroSpotLight != null)
            {
                faroSpotLight.enabled = nightLights;
                faroSpotLight.intensity = mode == TimeOfDayMode.CoastalNight ? 1600f : 550f;
            }
            if (faroBeamConeRenderer != null)
            {
                faroBeamConeRenderer.enabled = nightLights;
            }

            foreach (var l in streetPointLights)
            {
                if (l != null) l.enabled = nightLights;
            }

            if (postVolume != null && postVolume.sharedProfile != null)
            {
                var prof = postVolume.sharedProfile;
                if (prof.TryGet<Bloom>(out var bloom))
                {
                    bloom.intensity.overrideState = true;
                    bloom.intensity.value = bloomInt;
                }
                if (prof.TryGet<ColorAdjustments>(out var ca))
                {
                    ca.postExposure.overrideState = true;
                    ca.postExposure.value = postExp;
                }
            }
        }

        private void UpdateEmission(Material mat, Color col)
        {
            if (mat == null) return;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", col);
        }
    }
}
