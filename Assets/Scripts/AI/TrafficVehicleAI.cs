using UnityEngine;
using GtaPunta.Core;
using GtaPunta.Vehicles;
using GtaPunta.World;

namespace GtaPunta.AI
{
    [RequireComponent(typeof(GtaVehicleController))]
    public class TrafficVehicleAI : MonoBehaviour
    {
        public RoadNetworkRuntime roadNetwork;
        public float cruiseSpeedKmh = 42f;
        public float waypointReachDist = 9.5f;

        private GtaVehicleController _vehicle;
        private int _currentNodeIdx = -1;
        private int _prevNodeIdx = -1;
        private float _stuckTimer;
        private bool _released;

        private void Awake()
        {
            _vehicle = GetComponent<GtaVehicleController>();
        }

        public void InitializeAtNode(RoadNetworkRuntime network, int startNodeIdx)
        {
            roadNetwork = network;
            _released = false;
            _stuckTimer = 0f;
            _prevNodeIdx = startNodeIdx;
            _currentNodeIdx = PickNextNeighbor(startNodeIdx, -1);

            if (roadNetwork != null && startNodeIdx >= 0 && startNodeIdx < roadNetwork.roadNodes.Count)
            {
                Vector3 pos = roadNetwork.roadNodes[startNodeIdx].position;
                Vector3 dir = Vector3.forward;
                if (_currentNodeIdx >= 0)
                {
                    dir = roadNetwork.roadNodes[_currentNodeIdx].position - pos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.1f) dir.Normalize();
                }
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                transform.position = pos + right * 2.1f + Vector3.up * 0.45f;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        public void ReleaseToPlayer()
        {
            _released = true;
            enabled = false;
        }

        private void Update()
        {
            if (_released || _vehicle.IsPlayerDriven || roadNetwork == null || roadNetwork.roadNodes.Count == 0)
                return;

            if (_currentNodeIdx < 0 || _currentNodeIdx >= roadNetwork.roadNodes.Count)
            {
                _currentNodeIdx = roadNetwork.FindNearestRoadNode(transform.position, 200f);
                if (_currentNodeIdx < 0) return;
            }

            Vector3 targetPos = roadNetwork.roadNodes[_currentNodeIdx].position;
            Vector3 toTarget = targetPos - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 1f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
                targetPos += right * 2.0f;
                toTarget = targetPos - transform.position;
                toTarget.y = 0f;
            }

            if (toTarget.magnitude < waypointReachDist)
            {
                int next = PickNextNeighbor(_currentNodeIdx, _prevNodeIdx);
                _prevNodeIdx = _currentNodeIdx;
                _currentNodeIdx = next;
                if (_currentNodeIdx < 0) return;
                targetPos = roadNetwork.roadNodes[_currentNodeIdx].position;
                toTarget = targetPos - transform.position;
                toTarget.y = 0f;
            }

            float signedAngle = Vector3.SignedAngle(transform.forward, toTarget.normalized, Vector3.up);
            float steer = Mathf.Clamp(signedAngle / 36f, -1f, 1f);

            bool obstacleAhead = false;
            Vector3 rayOrigin = transform.position + Vector3.up * 0.9f + transform.forward * 2.3f;
            if (Physics.SphereCast(rayOrigin, 0.85f, transform.forward, out RaycastHit hit, 11.5f, ~LayerMask.GetMask("Water", "Ignore Raycast"), QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform) &&
                    (hit.collider.GetComponentInParent<GtaVehicleController>() != null ||
                     hit.collider.GetComponentInParent<Player.PlayerFootController>() != null ||
                     hit.collider.GetComponentInParent<PedestrianAI>() != null))
                {
                    obstacleAhead = true;
                }
            }

            float cornerSlow = Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(Mathf.Abs(signedAngle) / 55f));
            float desiredSpeed = cruiseSpeedKmh * cornerSlow;
            float throttle = 0f;
            bool brake = false;

            if (obstacleAhead)
            {
                throttle = -0.6f;
                brake = true;
            }
            else if (_vehicle.SpeedKmh < desiredSpeed)
            {
                throttle = Mathf.Clamp01((desiredSpeed - _vehicle.SpeedKmh) / 18f) * 0.72f + 0.18f;
            }

            _vehicle.SetAIInputs(throttle, steer, brake);

            if (_vehicle.SpeedKmh < 1.2f && !obstacleAhead)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 6f)
                {
                    _stuckTimer = 0f;
                    Vector3 playerPos = GameManager.Instance != null ? GameManager.Instance.GetActivePlayerPosition() : Vector3.zero;
                    int newNode = roadNetwork.FindRandomSpawnNode(playerPos, 95f, 340f);
                    if (newNode >= 0)
                    {
                        var rb = GetComponent<Rigidbody>();
                        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        InitializeAtNode(roadNetwork, newNode);
                    }
                }
            }
            else
            {
                _stuckTimer = 0f;
            }
        }

        private int PickNextNeighbor(int nodeIdx, int avoidIdx)
        {
            var node = roadNetwork.roadNodes[nodeIdx];
            if (node.neighbors == null || node.neighbors.Count == 0) return -1;

            int best = node.neighbors[0];
            float bestScore = -999f;
            Vector3 fwd = transform.forward;
            if (avoidIdx >= 0 && avoidIdx < roadNetwork.roadNodes.Count)
            {
                Vector3 d = node.position - roadNetwork.roadNodes[avoidIdx].position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.1f) fwd = d.normalized;
            }

            for (int i = 0; i < node.neighbors.Count; i++)
            {
                int cand = node.neighbors[i];
                if (cand == avoidIdx && node.neighbors.Count > 1) continue;
                Vector3 dir = roadNetwork.roadNodes[cand].position - node.position;
                dir.y = 0f;
                float align = dir.sqrMagnitude > 0.1f ? Vector3.Dot(fwd, dir.normalized) : 0f;
                float ramblaBonus = roadNetwork.roadNodes[cand].isRambla ? 0.25f : 0f;
                float score = align + ramblaBonus + Random.Range(-0.25f, 0.25f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cand;
                }
            }
            return best;
        }
    }
}
