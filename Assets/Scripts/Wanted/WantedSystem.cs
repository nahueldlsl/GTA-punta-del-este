using System.Collections.Generic;
using UnityEngine;
using GtaPunta.Core;
using GtaPunta.Vehicles;
using GtaPunta.World;

namespace GtaPunta.Wanted
{
    public enum WantedCrimeType
    {
        RecklessCrash,
        VehicleTheft,
        HitPedestrian,
        RamPolice,
        StolePoliceCar
    }

    /// <summary>
    /// GTA-style 1 to 5 Star Wanted System for Punta del Este.
    /// Spawns Policia de Maldonado patrol cruisers that actively pursue and attempt to bust the player,
    /// with line-of-sight evasion cooldown and HUD radar telemetry.
    /// </summary>
    public class WantedSystem : MonoBehaviour
    {
        public static WantedSystem Instance { get; private set; }

        public RoadNetworkRuntime roadNetwork;
        public GameObject policeCruiserTemplate;
        public float evasionCooldownDuration = 18f;
        public float bustedDurationRequired = 3.5f;

        public int WantedStars { get; private set; }
        public bool IsEvading { get; private set; }
        public float EvasionProgress { get; private set; }
        public float BustedProgress { get; private set; }
        public List<PolicePursuitAI> ActivePolice { get; } = new List<PolicePursuitAI>();

        private float _evasionTimer;
        private float _bustedTimer;
        private float _spawnTimer;
        private float _heatScore;

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
            if (roadNetwork == null) roadNetwork = FindFirstObjectByType<RoadNetworkRuntime>();
        }

        public void ReportCrime(WantedCrimeType crime, Vector3 location)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            float heatAdd = crime switch
            {
                WantedCrimeType.RecklessCrash => 1.0f,
                WantedCrimeType.VehicleTheft => 1.25f,
                WantedCrimeType.HitPedestrian => 1.6f,
                WantedCrimeType.RamPolice => 2.2f,
                WantedCrimeType.StolePoliceCar => 2.5f,
                _ => 1.0f
            };

            _heatScore = Mathf.Min(10f, _heatScore + heatAdd);
            int targetStars = Mathf.Clamp(Mathf.CeilToInt(_heatScore * 0.55f), 1, 5);
            if (crime == WantedCrimeType.RamPolice || crime == WantedCrimeType.StolePoliceCar)
                targetStars = Mathf.Max(targetStars, 2);

            if (targetStars > WantedStars)
            {
                WantedStars = targetStars;
                AudioManager.Instance?.PlayOneShot2D("Wanted_Alert", 0.72f);
                string starWord = WantedStars == 1 ? "ESTRELLA" : "ESTRELLAS";
                GameManager.Instance?.hud?.ShowToast($"POLICÍA DE MALDONADO EN BÚSQUEDA ({WantedStars} {starWord})", Palette.HudWantedAlert, 3.2f);
            }

            IsEvading = false;
            _evasionTimer = 0f;
            EvasionProgress = 0f;
        }

        public void SetWantedStarsDirect(int stars)
        {
            WantedStars = Mathf.Clamp(stars, 0, 5);
            _heatScore = WantedStars * 1.85f;
            IsEvading = false;
            _evasionTimer = 0f;
            EvasionProgress = 0f;
            if (WantedStars == 0) ClearWanted();
            else AudioManager.Instance?.PlayOneShot2D("Wanted_Alert", 0.65f);
        }

        public void ClearWanted()
        {
            WantedStars = 0;
            _heatScore = 0f;
            IsEvading = false;
            _evasionTimer = 0f;
            _bustedTimer = 0f;
            EvasionProgress = 0f;
            BustedProgress = 0f;

            for (int i = ActivePolice.Count - 1; i >= 0; i--)
            {
                if (ActivePolice[i] != null)
                {
                    var v = ActivePolice[i].GetComponent<GtaVehicleController>();
                    if (v != null && !v.IsPlayerDriven)
                        Destroy(ActivePolice[i].gameObject);
                }
            }
            ActivePolice.Clear();
        }

        private void Update()
        {
            if (WantedStars <= 0) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            Vector3 playerPos = GameManager.Instance != null ? GameManager.Instance.GetActivePlayerPosition() : Vector3.zero;
            bool anyPoliceSeesPlayer = false;
            bool policeCloseToBust = false;

            for (int i = ActivePolice.Count - 1; i >= 0; i--)
            {
                var p = ActivePolice[i];
                if (p == null || !p.enabled)
                {
                    ActivePolice.RemoveAt(i);
                    continue;
                }

                float d = Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z), new Vector2(playerPos.x, playerPos.z));
                if (d < 115f) anyPoliceSeesPlayer = true;
                if (d < 10.5f) policeCloseToBust = true;

                // Reposition far-behind cruisers ahead on the road network
                if (d > 340f)
                    RespawnPoliceAhead(p, playerPos);
            }

            // Spawn patrol cruisers up to star limit (1 star -> 1 cruiser, 5 stars -> 4 cruisers)
            int desiredPolice = Mathf.Clamp(WantedStars, 1, 4);
            _spawnTimer += Time.deltaTime;
            if (ActivePolice.Count < desiredPolice && _spawnTimer > 2.2f)
            {
                _spawnTimer = 0f;
                SpawnPoliceCruiser(playerPos);
            }

            // Evasion cooldown when out of police visual range
            if (!anyPoliceSeesPlayer && ActivePolice.Count > 0)
            {
                IsEvading = true;
                _evasionTimer += Time.deltaTime;
                EvasionProgress = Mathf.Clamp01(_evasionTimer / evasionCooldownDuration);
                if (_evasionTimer >= evasionCooldownDuration)
                {
                    ClearWanted();
                    GameManager.Instance?.hud?.ShowToast("EVADISTE A LA POLICÍA — BÚSQUEDA TERMINADA", Palette.HudSuccess, 3.5f);
                    return;
                }
            }
            else
            {
                IsEvading = false;
                _evasionTimer = Mathf.Max(0f, _evasionTimer - Time.deltaTime * 2f);
                EvasionProgress = Mathf.Clamp01(_evasionTimer / evasionCooldownDuration);
            }

            // Busted check: player nearly stopped right next to a pursuing police car
            float playerSpeedKmh = GetPlayerSpeedKmh();
            if (policeCloseToBust && playerSpeedKmh < 7.5f)
            {
                _bustedTimer += Time.deltaTime;
                BustedProgress = Mathf.Clamp01(_bustedTimer / bustedDurationRequired);
                if (_bustedTimer >= bustedDurationRequired)
                {
                    _bustedTimer = 0f;
                    BustedProgress = 0f;
                    GameManager.Instance?.TriggerBusted();
                }
            }
            else
            {
                _bustedTimer = Mathf.Max(0f, _bustedTimer - Time.deltaTime * 2.5f);
                BustedProgress = Mathf.Clamp01(_bustedTimer / bustedDurationRequired);
            }
        }

        private float GetPlayerSpeedKmh()
        {
            var vi = GameManager.Instance?.vehicleInteraction;
            if (vi != null && vi.IsInVehicle && vi.CurrentVehicle != null)
                return vi.CurrentVehicle.SpeedKmh;
            var pf = GameManager.Instance?.playerFoot;
            return pf != null ? pf.CurrentSpeed * 3.6f : 0f;
        }

        private void SpawnPoliceCruiser(Vector3 playerPos)
        {
            if (policeCruiserTemplate == null || roadNetwork == null || roadNetwork.roadNodes.Count == 0)
                return;

            int nodeIdx = roadNetwork.FindRandomSpawnNode(playerPos, 62f, 155f);
            if (nodeIdx < 0) return;

            Vector3 spawnPos = roadNetwork.roadNodes[nodeIdx].position + Vector3.up * 0.5f;
            Vector3 toPlayer = playerPos - spawnPos; toPlayer.y = 0f;
            Quaternion rot = toPlayer.sqrMagnitude > 1f ? Quaternion.LookRotation(toPlayer.normalized, Vector3.up) : Quaternion.identity;

            var obj = Instantiate(policeCruiserTemplate, spawnPos, rot, transform);
            obj.name = "PoliciaMaldonado_" + ActivePolice.Count;
            obj.SetActive(true);

            var veh = obj.GetComponent<GtaVehicleController>();
            if (veh != null)
            {
                veh.isPoliceVehicle = true;
                veh.SetSiren(true);
            }

            var ai = obj.GetComponent<PolicePursuitAI>();
            if (ai == null) ai = obj.AddComponent<PolicePursuitAI>();
            ai.roadNetwork = roadNetwork;
            ActivePolice.Add(ai);
        }

        private void RespawnPoliceAhead(PolicePursuitAI police, Vector3 playerPos)
        {
            int nodeIdx = roadNetwork.FindRandomSpawnNode(playerPos, 75f, 165f);
            if (nodeIdx < 0) return;
            var rb = police.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Vector3 p = roadNetwork.roadNodes[nodeIdx].position + Vector3.up * 0.55f;
            Vector3 toP = playerPos - p; toP.y = 0f;
            police.transform.position = p;
            if (toP.sqrMagnitude > 1f) police.transform.rotation = Quaternion.LookRotation(toP.normalized, Vector3.up);
        }
    }
}
