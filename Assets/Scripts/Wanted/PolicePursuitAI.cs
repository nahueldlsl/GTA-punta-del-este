using UnityEngine;
using GtaPunta.Core;
using GtaPunta.Vehicles;
using GtaPunta.World;

namespace GtaPunta.Wanted
{
    [RequireComponent(typeof(GtaVehicleController))]
    public class PolicePursuitAI : MonoBehaviour
    {
        public RoadNetworkRuntime roadNetwork;
        public float maxChaseSpeedKmh = 96f;

        private GtaVehicleController _vehicle;
        private float _reverseTimer;
        private float _stuckTimer;

        private void Awake()
        {
            _vehicle = GetComponent<GtaVehicleController>();
        }

        private void Update()
        {
            if (_vehicle.IsPlayerDriven) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                _vehicle.SetAIInputs(0f, 0f, true);
                return;
            }

            _vehicle.SetSiren(true);
            Vector3 targetPos = GameManager.Instance != null ? GameManager.Instance.GetActivePlayerPosition() : Vector3.zero;
            Vector3 toTarget = targetPos - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (_reverseTimer > 0f)
            {
                _reverseTimer -= Time.deltaTime;
                _vehicle.SetAIInputs(-0.75f, -0.8f, false);
                return;
            }

            if (dist < 7.5f)
            {
                _vehicle.SetAIInputs(0f, 0f, true);
                return;
            }

            Vector3 desiredDir = toTarget.normalized;
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            bool hitCenter = Physics.Raycast(origin, transform.forward, 8.5f, ~LayerMask.GetMask("Water", "Ignore Raycast"));
            bool hitLeft   = Physics.Raycast(origin, Quaternion.Euler(0, -28f, 0) * transform.forward, 7.5f, ~LayerMask.GetMask("Water", "Ignore Raycast"));
            bool hitRight  = Physics.Raycast(origin, Quaternion.Euler(0,  28f, 0) * transform.forward, 7.5f, ~LayerMask.GetMask("Water", "Ignore Raycast"));

            float steer = Mathf.Clamp(Vector3.SignedAngle(transform.forward, desiredDir, Vector3.up) / 30f, -1f, 1f);
            if (hitCenter)
                steer = hitLeft && !hitRight ? 1f : -1f;
            else if (hitLeft && !hitRight)
                steer = Mathf.Max(steer, 0.65f);
            else if (hitRight && !hitLeft)
                steer = Mathf.Min(steer, -0.65f);

            float speedLimit = Mathf.Lerp(45f, maxChaseSpeedKmh, Mathf.Clamp01(dist / 50f));
            float throttle = _vehicle.SpeedKmh < speedLimit ? 0.92f : 0.15f;
            _vehicle.SetAIInputs(throttle, steer, false);

            if (_vehicle.SpeedKmh < 1.8f && dist > 11f)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 2.2f)
                {
                    _stuckTimer = 0f;
                    _reverseTimer = 1.15f;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }
        }
    }
}
