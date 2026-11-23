using UnityEngine;
using UnityEngine.InputSystem;
using GtaPunta.AI;
using GtaPunta.Core;
using GtaPunta.Vehicles;
using GtaPunta.Wanted;

namespace GtaPunta.Player
{
    /// <summary>
    /// Handles seamless GTA-style Enter / Exit Vehicle interaction (press F or E)
    /// for parked vehicles, circulating traffic, and police cruisers, with raycast-safe
    /// door exit placement and smooth Cinemachine camera transitions.
    /// </summary>
    public class VehicleInteractionController : MonoBehaviour
    {
        public float interactRadius = 4.6f;
        public PlayerFootController playerFoot;
        public GtaCameraRig cameraRig;

        public bool IsInVehicle { get; private set; }
        public GtaVehicleController CurrentVehicle { get; private set; }
        public GtaVehicleController NearbyVehicle { get; private set; }

        private void Start()
        {
            if (playerFoot == null) playerFoot = GetComponent<PlayerFootController>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<GtaCameraRig>();
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            if (!IsInVehicle)
            {
                NearbyVehicle = FindClosestVehicle();
            }
            else
            {
                NearbyVehicle = null;
                if (CurrentVehicle != null)
                    transform.position = CurrentVehicle.transform.position;
            }

            var kb = Keyboard.current;
            if (kb != null && (kb.fKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
            {
                ToggleVehicle();
            }
        }

        public void ToggleVehicle()
        {
            if (IsInVehicle)
                ExitVehicle();
            else if (NearbyVehicle != null)
                EnterVehicle(NearbyVehicle);
        }

        public void EnterVehicle(GtaVehicleController vehicle)
        {
            if (vehicle == null || IsInVehicle) return;

            // If this is an AI traffic or police vehicle, hijack it cleanly!
            bool wasAiDriven = false;
            var trafficAi = vehicle.GetComponent<TrafficVehicleAI>();
            if (trafficAi != null && trafficAi.enabled)
            {
                trafficAi.ReleaseToPlayer();
                wasAiDriven = true;
            }
            var policeAi = vehicle.GetComponent<PolicePursuitAI>();
            if (policeAi != null && policeAi.enabled)
            {
                policeAi.enabled = false;
                wasAiDriven = true;
            }

            IsInVehicle = true;
            CurrentVehicle = vehicle;

            if (playerFoot != null) playerFoot.ControlsEnabled = false;
            vehicle.SetPlayerDriven(true);

            cameraRig?.SetVehicleTarget(vehicle.transform);
            AudioManager.Instance?.PlayOneShot2D("Car_Door", 0.65f);
            AudioManager.Instance?.SetInVehicle(true);
            AudioManager.Instance?.SetListenerTarget(vehicle.transform);

            if (vehicle.isPoliceVehicle)
                WantedSystem.Instance?.ReportCrime(WantedCrimeType.StolePoliceCar, vehicle.transform.position);
            else if (wasAiDriven)
                WantedSystem.Instance?.ReportCrime(WantedCrimeType.VehicleTheft, vehicle.transform.position);
        }

        public void ExitVehicle()
        {
            if (!IsInVehicle || CurrentVehicle == null) return;

            Vector3 exitPos = FindSafeExitPosition(CurrentVehicle.transform);
            float exitYaw = CurrentVehicle.transform.eulerAngles.y;

            CurrentVehicle.SetPlayerDriven(false);
            CurrentVehicle = null;
            IsInVehicle = false;

            if (playerFoot != null)
            {
                playerFoot.ControlsEnabled = true;
                playerFoot.Teleport(exitPos, exitYaw);
                cameraRig?.SetFootTarget(playerFoot.cameraTargetPivot != null ? playerFoot.cameraTargetPivot : playerFoot.transform);
                AudioManager.Instance?.SetListenerTarget(playerFoot.transform);
            }

            AudioManager.Instance?.PlayOneShot2D("Car_Door", 0.60f, 0.92f, 0.98f);
            AudioManager.Instance?.SetInVehicle(false);
        }

        public void ForceExitVehicle()
        {
            if (IsInVehicle) ExitVehicle();
        }

        private Vector3 FindSafeExitPosition(Transform car)
        {
            Vector3[] candidateOffsets =
            {
                -car.right * 2.35f,
                 car.right * 2.35f,
                -car.forward * 3.1f,
                 car.forward * 3.1f,
                 Vector3.up * 2.2f
            };

            foreach (var off in candidateOffsets)
            {
                Vector3 candidate = car.position + off + Vector3.up * 0.8f;
                if (!Physics.CheckSphere(candidate, 0.45f, ~LayerMask.GetMask("Water", "Ignore Raycast")))
                {
                    if (Physics.Raycast(candidate + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f))
                        return hit.point + Vector3.up * 0.15f;
                    return candidate;
                }
            }
            return car.position + Vector3.up * 2.1f;
        }

        private GtaVehicleController FindClosestVehicle()
        {
            var all = FindObjectsByType<GtaVehicleController>(FindObjectsSortMode.None);
            GtaVehicleController best = null;
            float bestDistSq = interactRadius * interactRadius;
            Vector3 myPos = transform.position;

            for (int i = 0; i < all.Length; i++)
            {
                var v = all[i];
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                float sq = (v.transform.position - myPos).sqrMagnitude;
                if (sq < bestDistSq)
                {
                    bestDistSq = sq;
                    best = v;
                }
            }
            return best;
        }
    }
}
