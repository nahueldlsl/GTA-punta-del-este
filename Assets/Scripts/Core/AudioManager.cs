using System.Collections.Generic;
using UnityEngine;

namespace GtaPunta.Core
{
    /// <summary>
    /// Centralized Audio Manager with clean static gain sliders (MusicGain / SfxGain / MasterGain),
    /// geographical Playa Mansa vs. Playa Brava ocean surf ambience crossfading,
    /// one-shot SFX helpers, and toggleable Punta Summer FM car radio.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Range(0f, 1f)] public float masterGain = 0.90f;
        [Range(0f, 1f)] public float musicGain = 0.60f;
        [Range(0f, 1f)] public float sfxGain = 0.85f;

        private AudioSource _bravaOceanSource;
        private AudioSource _mansaOceanSource;
        private AudioSource _radioSource;
        private AudioSource _uiSource;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private bool _radioEnabled = true;
        private bool _inVehicle;
        private bool _isPaused;
        private Transform _listenerTarget;

        public bool RadioEnabled => _radioEnabled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            LoadClips();
            SetupSources();
        }

        private void LoadClips()
        {
            string[] names =
            {
                "Ocean_Brava_Loop",
                "Ocean_Mansa_Loop",
                "Engine_Idle_Loop",
                "Tire_Screech_Loop",
                "Car_Horn",
                "Car_Door",
                "Crash_Impact",
                "Footstep_Asphalt",
                "Footstep_Sand",
                "Police_Siren_Loop",
                "Wanted_Alert",
                "Landmark_Chime",
                "Radio_Punta_Summer_Loop"
            };

            foreach (var n in names)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + n);
                if (clip != null)
                    _clips[n] = clip;
            }
        }

        private void SetupSources()
        {
            _bravaOceanSource = CreateSource("Ambience_Brava", true, 0f);
            _mansaOceanSource = CreateSource("Ambience_Mansa", true, 0f);
            _radioSource = CreateSource("Radio_PuntaFM", true, 0f);
            _uiSource = CreateSource("UI_SFX", false, 0f);

            if (_clips.TryGetValue("Ocean_Brava_Loop", out var brava))
            {
                _bravaOceanSource.clip = brava;
                _bravaOceanSource.Play();
            }
            if (_clips.TryGetValue("Ocean_Mansa_Loop", out var mansa))
            {
                _mansaOceanSource.clip = mansa;
                _mansaOceanSource.Play();
            }
            if (_clips.TryGetValue("Radio_Punta_Summer_Loop", out var radio))
            {
                _radioSource.clip = radio;
                _radioSource.Play();
            }
        }

        private AudioSource CreateSource(string goName, bool loop, float spatialBlend)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop = loop;
            src.spatialBlend = spatialBlend;
            src.playOnAwake = false;
            return src;
        }

        public AudioClip GetClip(string clipName)
        {
            return _clips.TryGetValue(clipName, out var c) ? c : null;
        }

        public void SetListenerTarget(Transform target) => _listenerTarget = target;

        public void SetInVehicle(bool inVehicle) => _inVehicle = inVehicle;

        public void SetPaused(bool paused) => _isPaused = paused;

        public void ToggleRadio()
        {
            _radioEnabled = !_radioEnabled;
        }

        public float EffectiveSfxGain => masterGain * sfxGain * (_isPaused ? 0.4f : 1f);
        public float EffectiveMusicGain => masterGain * musicGain * (_isPaused ? 0.35f : 1f);

        private void Update()
        {
            Vector3 pos = _listenerTarget != null
                ? _listenerTarget.position
                : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            // Geographical Mansa (West/NW) vs Brava (East/SE) ocean ambience crossfade
            Vector2 p = new Vector2(pos.x + 480f, pos.z + 750f);
            Vector2 bravaDir = new Vector2(1400f, -930f).normalized;
            float bravaWeight = Mathf.Clamp01((Vector2.Dot(p, bravaDir) + 120f) / 420f);

            float coastFactor = Mathf.Clamp01(1f - Mathf.Max(0f, pos.y - 3.2f) / 7f);
            float ambBase = (0.14f + 0.24f * coastFactor) * EffectiveSfxGain;

            if (_bravaOceanSource != null)
                _bravaOceanSource.volume = Mathf.Lerp(_bravaOceanSource.volume, ambBase * (0.25f + 0.75f * bravaWeight), Time.unscaledDeltaTime * 3f);

            if (_mansaOceanSource != null)
                _mansaOceanSource.volume = Mathf.Lerp(_mansaOceanSource.volume, ambBase * (0.25f + 0.75f * (1f - bravaWeight)), Time.unscaledDeltaTime * 3f);

            // Punta Summer FM plays crisply in vehicles (and softly on Title screen)
            if (_radioSource != null)
            {
                bool titleMode = GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Title;
                float targetRadio = (_inVehicle && _radioEnabled) ? 0.38f * EffectiveMusicGain
                                  : (titleMode ? 0.22f * EffectiveMusicGain : 0f);
                _radioSource.volume = Mathf.Lerp(_radioSource.volume, targetRadio, Time.unscaledDeltaTime * 3.5f);
            }
        }

        public void PlayOneShot2D(string clipName, float volume = 0.8f, float pitchMin = 0.96f, float pitchMax = 1.04f)
        {
            if (_uiSource == null || !_clips.TryGetValue(clipName, out var clip)) return;
            _uiSource.pitch = Random.Range(pitchMin, pitchMax);
            _uiSource.PlayOneShot(clip, volume * EffectiveSfxGain);
        }

        public void PlayAtPoint(string clipName, Vector3 pos, float volume = 0.8f)
        {
            if (!_clips.TryGetValue(clipName, out var clip)) return;
            AudioSource.PlayClipAtPoint(clip, pos, volume * EffectiveSfxGain);
        }
    }
}
