using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using GtaPunta.Atmosphere;
using GtaPunta.Player;
using GtaPunta.UI;
using GtaPunta.Wanted;
using GtaPunta.World;

namespace GtaPunta.Core
{
    public enum GameState
    {
        Title,
        Playing,
        Paused,
        MapOpen,
        Busted
    }

    /// <summary>
    /// Master state machine and gameplay coordinator for GTA: Punta del Este.
    /// Owns GameState transitions, Landmark discovery (6 iconic spots),
    /// optional Rambla Circuit Time-Trial loop, District detection, and Fast Travel.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState CurrentState { get; private set; } = GameState.Title;

        [Header("Scene References")]
        public PlayerFootController playerFoot;
        public VehicleInteractionController vehicleInteraction;
        public GtaCameraRig cameraRig;
        public CoastalAtmosphereController atmosphere;
        public RoadNetworkRuntime roadNetwork;
        public WantedSystem wantedSystem;
        public GtaHudController hud;

        [Header("Spawn & Discovery")]
        public Vector3 initialPlayerPos;
        public float initialPlayerYaw = 35f;

        // Rambla Circuit Time Trial (optional free-roam activity!)
        public bool TimeTrialActive { get; private set; }
        public float TimeTrialElapsed { get; private set; }
        public float BestTimeTrialSeconds { get; private set; } = -1f;
        public int TimeTrialCheckpointIndex { get; private set; }
        public int TimeTrialTotalCheckpoints => _trialCheckpoints.Length;

        private readonly Vector3[] _trialCheckpoints =
        {
            new Vector3(685f, 4.2f, 520f),    // 1. Parada 1 Brava / Los Dedos
            new Vector3(110f, 4.5f, -260f),   // 2. Playa El Emir / Rambla Brava
            new Vector3(-470f, 4.8f, -940f),  // 3. Punta de las Salinas (Tip)
            new Vector3(-565f, 4.8f, -660f),  // 4. El Faro / Rambla Sur
            new Vector3(-395f, 4.5f, -245f),  // 5. Puerto de Punta del Este
            new Vector3(180f, 5.2f, -65f),    // 6. Av. Gorlero / Plaza Artigas
        };

        private GameObject _checkpointBeacon;
        private float _bustedTimer;
        private string _currentDistrict = "Península — Punta del Este";

        public string CurrentDistrict => _currentDistrict;
        public int DiscoveredCount
        {
            get
            {
                if (roadNetwork == null) return 0;
                int c = 0;
                foreach (var lm in roadNetwork.landmarks)
                    if (lm.discovered) c++;
                return c;
            }
        }
        public int TotalLandmarks => roadNetwork != null ? roadNetwork.landmarks.Count : 0;

        public event Action<GameState> OnStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            AutoWireReferences();
            if (playerFoot != null && initialPlayerPos == Vector3.zero)
            {
                initialPlayerPos = playerFoot.transform.position;
                initialPlayerYaw = playerFoot.transform.eulerAngles.y;
            }

            CreateTimeTrialBeacon();
            SetState(GameState.Title);
        }

        private void AutoWireReferences()
        {
            if (playerFoot == null) playerFoot = FindFirstObjectByType<PlayerFootController>();
            if (vehicleInteraction == null) vehicleInteraction = FindFirstObjectByType<VehicleInteractionController>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<GtaCameraRig>();
            if (atmosphere == null) atmosphere = FindFirstObjectByType<CoastalAtmosphereController>();
            if (roadNetwork == null) roadNetwork = FindFirstObjectByType<RoadNetworkRuntime>();
            if (wantedSystem == null) wantedSystem = FindFirstObjectByType<WantedSystem>();
            if (hud == null) hud = FindFirstObjectByType<GtaHudController>();

            if (AudioManager.Instance != null && playerFoot != null)
                AudioManager.Instance.SetListenerTarget(playerFoot.transform);
        }

        private void CreateTimeTrialBeacon()
        {
            _checkpointBeacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _checkpointBeacon.name = "RamblaTrial_CheckpointBeacon";
            Destroy(_checkpointBeacon.GetComponent<Collider>());
            _checkpointBeacon.transform.localScale = new Vector3(11f, 18f, 11f);
            var mr = _checkpointBeacon.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = new Color(Palette.HudGoldAccent.r, Palette.HudGoldAccent.g, Palette.HudGoldAccent.b, 0.35f);
            mat.SetFloat("_Surface", 1f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
            _checkpointBeacon.SetActive(false);
        }

        public Vector3 GetActivePlayerPosition()
        {
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null)
                return vehicleInteraction.CurrentVehicle.transform.position;
            if (playerFoot != null)
                return playerFoot.transform.position;
            return Vector3.zero;
        }

        public Transform GetActivePlayerTransform()
        {
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null)
                return vehicleInteraction.CurrentVehicle.transform;
            return playerFoot != null ? playerFoot.transform : transform;
        }

        private void Update()
        {
            var kb = Keyboard.current;

            if (CurrentState == GameState.Title)
            {
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                    StartPlaying();
                return;
            }

            if (CurrentState == GameState.Busted)
            {
                _bustedTimer -= Time.unscaledDeltaTime;
                if (_bustedTimer <= 0f)
                    RespawnAfterBusted();
                return;
            }

            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (CurrentState == GameState.MapOpen)
                        SetState(GameState.Playing);
                    else if (CurrentState == GameState.Paused)
                        SetState(GameState.Playing);
                    else if (CurrentState == GameState.Playing)
                        SetState(GameState.Paused);
                }
                else if (kb.mKey.wasPressedThisFrame)
                {
                    if (CurrentState == GameState.Playing)
                        SetState(GameState.MapOpen);
                    else if (CurrentState == GameState.MapOpen)
                        SetState(GameState.Playing);
                }
                else if (CurrentState == GameState.Playing)
                {
                    if (kb.tKey.wasPressedThisFrame && atmosphere != null)
                    {
                        atmosphere.CycleTimeOfDay();
                        hud?.ShowToast("HORA DEL DÍA: " + atmosphere.GetTimeLabel().ToUpperInvariant(), Palette.HudCyanAccent);
                    }
                    if (kb.vKey.wasPressedThisFrame && AudioManager.Instance != null)
                    {
                        AudioManager.Instance.ToggleRadio();
                        hud?.ShowToast("RADIO: " + AudioManager.Instance.RadioStationName, Palette.HudGoldAccent);
                    }
                    if (kb.kKey.wasPressedThisFrame)
                    {
                        ToggleRamblaTimeTrial();
                    }
                }
            }

            if (CurrentState == GameState.Playing)
            {
                Vector3 pos = GetActivePlayerPosition();
                UpdateDistrict(pos);
                CheckLandmarkDiscovery(pos);
                UpdateTimeTrial(pos);
            }
        }

        public void StartPlaying()
        {
            AudioManager.Instance?.PlayOneShot2D("Landmark_Chime", 0.45f, 1.0f, 1.0f);
            SetState(GameState.Playing);
            hud?.ShowToast("BIENVENIDO A PUNTA DEL ESTE — PRESIONÁ F PARA SUBIR AL AUTO", Palette.HudGoldAccent, 4.5f);
        }

        public void SetState(GameState newState)
        {
            CurrentState = newState;
            bool playing = (newState == GameState.Playing);
            Time.timeScale = (newState == GameState.Paused || newState == GameState.MapOpen) ? 0f : 1f;

            Cursor.visible = !playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;

            AudioManager.Instance?.SetPaused(newState == GameState.Paused || newState == GameState.MapOpen);
            if (cameraRig != null)
                cameraRig.SetTitleMode(newState == GameState.Title);

            hud?.ApplyState(newState);
            OnStateChanged?.Invoke(newState);
        }

        public void TriggerBusted()
        {
            if (CurrentState != GameState.Playing) return;
            _bustedTimer = 3.2f;
            TimeTrialActive = false;
            if (_checkpointBeacon != null) _checkpointBeacon.SetActive(false);
            AudioManager.Instance?.PlayOneShot2D("Wanted_Alert", 0.9f, 0.72f, 0.76f);
            SetState(GameState.Busted);
        }

        private void RespawnAfterBusted()
        {
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle)
                vehicleInteraction.ForceExitVehicle();

            wantedSystem?.ClearWanted();
            TeleportPlayerTo(initialPlayerPos, initialPlayerYaw);
            SetState(GameState.Playing);
            hud?.ShowToast("LIBERADO EN AV. GORLERO — NIVEL DE BÚSQUEDA LIMPIO", Palette.HudCyanAccent, 3.5f);
        }

        public void RestartSession()
        {
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle)
                vehicleInteraction.ForceExitVehicle();
            wantedSystem?.ClearWanted();
            TimeTrialActive = false;
            if (_checkpointBeacon != null) _checkpointBeacon.SetActive(false);

            if (roadNetwork != null)
            {
                foreach (var lm in roadNetwork.landmarks)
                    lm.discovered = false;
            }

            TeleportPlayerTo(initialPlayerPos, initialPlayerYaw);
            atmosphere?.SetTimeOfDay(TimeOfDayMode.SunnyAfternoon);
            SetState(GameState.Playing);
            hud?.ShowToast("SESIÓN REINICIADA — PENÍNSULA DE PUNTA DEL ESTE", Palette.HudGoldAccent, 3f);
        }

        public void TeleportToLandmark(string landmarkId)
        {
            if (roadNetwork == null) return;
            var lm = roadNetwork.landmarks.Find(x => x.id == landmarkId);
            if (lm == null) return;

            Vector3 target = lm.position + Vector3.up * 0.6f;
            float yaw = lm.teleportYaw;
            // Curated scenic clear-ground viewpoints framing every iconic landmark!
            if (lm.id == "el_faro") { target = new Vector3(-448f, 0f, -648f); yaw = -132f; }
            else if (lm.id == "los_dedos") { target = new Vector3(742f, 0f, 526f); yaw = 110f; }
            else if (lm.id == "iglesia_candelaria") { target = new Vector3(-408f, 0f, -678f); yaw = 185f; }
            else if (lm.id == "puerto") { target = new Vector3(-436f, 0f, -158f); yaw = -16f; }
            else if (lm.id == "plaza_artigas") { target = new Vector3(236f, 0f, -44f); yaw = 224f; }
            else if (lm.id == "punta_salinas") { target = new Vector3(-508f, 0f, -1022f); yaw = 186f; }

            target.y = SampleGroundY(target) + 0.45f;
            TeleportPlayerTo(target, yaw);
            SetState(GameState.Playing);
            hud?.ShowToast("VIAJE RÁPIDO: " + lm.displayName.ToUpperInvariant(), Palette.HudCyanAccent, 3.2f);
        }

        public void TeleportPlayerTo(Vector3 pos, float yaw)
        {
            if (vehicleInteraction != null && vehicleInteraction.IsInVehicle && vehicleInteraction.CurrentVehicle != null)
            {
                var v = vehicleInteraction.CurrentVehicle;
                var rb = v.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                v.transform.position = pos + Vector3.up * 0.6f;
                v.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else if (playerFoot != null)
            {
                playerFoot.Teleport(pos, yaw);
            }
            cameraRig?.SetCameraAngles(yaw, 14f);
        }

        public void ToggleRamblaTimeTrial()
        {
            if (TimeTrialActive)
            {
                TimeTrialActive = false;
                if (_checkpointBeacon != null) _checkpointBeacon.SetActive(false);
                hud?.ShowToast("CIRCUITO RAMBLA CANCELADO", Palette.HudTextMuted);
                return;
            }

            TimeTrialActive = true;
            TimeTrialElapsed = 0f;
            TimeTrialCheckpointIndex = 0;
            PositionCheckpointBeacon(0);
            AudioManager.Instance?.PlayOneShot2D("Landmark_Chime", 0.7f, 1.05f, 1.05f);
            hud?.ShowToast("CIRCUITO RAMBLA INICIADO — SEGUÍ LOS FAROS DORADOS (6 PUNTOS)", Palette.HudGoldAccent, 4f);
        }

        private void PositionCheckpointBeacon(int idx)
        {
            if (_checkpointBeacon == null || idx >= _trialCheckpoints.Length) return;
            Vector3 p = _trialCheckpoints[idx];
            p.y = SampleGroundY(p) + 9f;
            _checkpointBeacon.transform.position = p;
            _checkpointBeacon.SetActive(true);
        }

        private void UpdateTimeTrial(Vector3 playerPos)
        {
            if (!TimeTrialActive) return;
            TimeTrialElapsed += Time.deltaTime;
            Vector3 cp = _trialCheckpoints[TimeTrialCheckpointIndex];
            float dist = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(cp.x, cp.z));
            if (dist < 22f)
            {
                TimeTrialCheckpointIndex++;
                AudioManager.Instance?.PlayOneShot2D("Landmark_Chime", 0.75f, 1.0f + TimeTrialCheckpointIndex * 0.05f, 1.0f + TimeTrialCheckpointIndex * 0.05f);
                if (TimeTrialCheckpointIndex >= _trialCheckpoints.Length)
                {
                    TimeTrialActive = false;
                    if (_checkpointBeacon != null) _checkpointBeacon.SetActive(false);
                    if (BestTimeTrialSeconds < 0f || TimeTrialElapsed < BestTimeTrialSeconds)
                        BestTimeTrialSeconds = TimeTrialElapsed;
                    hud?.ShowToast($"¡VUELTA A LA RAMBLA COMPLETADA EN {TimeTrialElapsed:0.0}s!", Palette.HudSuccess, 5f);
                }
                else
                {
                    PositionCheckpointBeacon(TimeTrialCheckpointIndex);
                    hud?.ShowToast($"CHECKPOINT {TimeTrialCheckpointIndex}/{_trialCheckpoints.Length}", Palette.HudGoldAccent, 1.8f);
                }
            }
        }

        private void CheckLandmarkDiscovery(Vector3 pos)
        {
            if (roadNetwork == null) return;
            foreach (var lm in roadNetwork.landmarks)
            {
                if (lm.discovered) continue;
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(lm.position.x, lm.position.z));
                if (d < 52f)
                {
                    lm.discovered = true;
                    AudioManager.Instance?.PlayOneShot2D("Landmark_Chime", 0.8f);
                    hud?.ShowToast($"DESCUBRISTE: {lm.displayName.ToUpperInvariant()} ({DiscoveredCount}/{TotalLandmarks})", Palette.HudGoldAccent, 4.2f);
                }
            }
        }

        private void UpdateDistrict(Vector3 pos)
        {
            if (roadNetwork == null) return;
            // Nearest landmark takes priority if within 95m
            foreach (var lm in roadNetwork.landmarks)
            {
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(lm.position.x, lm.position.z));
                if (d < 90f)
                {
                    _currentDistrict = lm.district;
                    return;
                }
            }

            // Otherwise resolve nearest named road or Mansa/Brava beach
            string road = roadNetwork.GetNearestRoadName(pos, 55f);
            if (!string.IsNullOrEmpty(road))
            {
                _currentDistrict = road;
                return;
            }

            // Fall back to Playa Brava vs Playa Mansa geographically
            Vector2 p = new Vector2(pos.x + 480f, pos.z + 750f);
            Vector2 bravaDir = new Vector2(1400f, -930f).normalized;
            bool isBrava = Vector2.Dot(p, bravaDir) > -40f;
            _currentDistrict = isBrava ? "Playa Brava — Costa Atlántica" : "Playa Mansa — Bahía";
        }

        public float SampleGroundY(Vector3 worldPos)
        {
            if (Terrain.activeTerrain != null)
            {
                float ty = Terrain.activeTerrain.SampleHeight(worldPos) + Terrain.activeTerrain.transform.position.y;
                if (Physics.Raycast(new Vector3(worldPos.x, ty + 25f, worldPos.z), Vector3.down, out RaycastHit hit, 60f))
                    return hit.point.y;
                return ty;
            }
            return 4f;
        }
    }
}
