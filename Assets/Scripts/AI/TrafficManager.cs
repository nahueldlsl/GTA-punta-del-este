using System.Collections.Generic;
using UnityEngine;
using GtaPunta.Core;
using GtaPunta.World;

namespace GtaPunta.AI
{
    /// <summary>
    /// Spawns and recycles living Punta del Este traffic vehicles around the player
    /// so the Rambla and Av. Gorlero always feel vibrant without tanking frame rate.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public RoadNetworkRuntime roadNetwork;
        public List<GameObject> trafficVehicleTemplates = new List<GameObject>();
        public int targetTrafficCount = 20;
        public float minSpawnDistance = 62f;
        public float maxSpawnDistance = 320f;
        public float despawnDistance = 420f;

        private readonly List<TrafficVehicleAI> _activeTraffic = new List<TrafficVehicleAI>();
        private float _maintainTimer;

        public int ActiveTrafficCount
        {
            get
            {
                int c = 0;
                for (int i = 0; i < _activeTraffic.Count; i++)
                    if (_activeTraffic[i] != null && _activeTraffic[i].enabled) c++;
                return c;
            }
        }

        private void Start()
        {
            if (roadNetwork == null) roadNetwork = FindFirstObjectByType<RoadNetworkRuntime>();
            for (int i = 0; i < targetTrafficCount; i++)
                TrySpawnTraffic(true);
        }

        private void Update()
        {
            _maintainTimer += Time.deltaTime;
            if (_maintainTimer < 1.2f) return;
            _maintainTimer = 0f;

            Vector3 playerPos = GameManager.Instance != null ? GameManager.Instance.GetActivePlayerPosition() : Vector3.zero;

            for (int i = _activeTraffic.Count - 1; i >= 0; i--)
            {
                var ai = _activeTraffic[i];
                if (ai == null || !ai.enabled)
                {
                    _activeTraffic.RemoveAt(i);
                    continue;
                }

                float d = Vector2.Distance(new Vector2(ai.transform.position.x, ai.transform.position.z), new Vector2(playerPos.x, playerPos.z));
                if (d > despawnDistance)
                {
                    int newNode = roadNetwork.FindRandomSpawnNode(playerPos, minSpawnDistance, maxSpawnDistance);
                    if (newNode >= 0)
                    {
                        var rb = ai.GetComponent<Rigidbody>();
                        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        ai.InitializeAtNode(roadNetwork, newNode);
                    }
                }
            }

            if (_activeTraffic.Count < targetTrafficCount)
                TrySpawnTraffic(false);
        }

        private void TrySpawnTraffic(bool initialWave)
        {
            if (roadNetwork == null || roadNetwork.roadNodes.Count == 0 || trafficVehicleTemplates.Count == 0)
                return;

            Vector3 playerPos = GameManager.Instance != null ? GameManager.Instance.GetActivePlayerPosition() : Vector3.zero;
            int nodeIdx = roadNetwork.FindRandomSpawnNode(playerPos, initialWave ? 28f : minSpawnDistance, maxSpawnDistance);
            if (nodeIdx < 0) return;

            Vector3 spawnPos = roadNetwork.roadNodes[nodeIdx].position + Vector3.up * 0.5f;
            foreach (var existing in _activeTraffic)
            {
                if (existing != null && Vector3.Distance(existing.transform.position, spawnPos) < 16f)
                    return;
            }

            var template = trafficVehicleTemplates[Random.Range(0, trafficVehicleTemplates.Count)];
            if (template == null) return;

            var carObj = Instantiate(template, spawnPos, Quaternion.identity, transform);
            carObj.name = "Traffic_" + template.name + "_" + _activeTraffic.Count;
            carObj.SetActive(true);

            ApplyTrafficColorVariant(carObj);

            var ai = carObj.GetComponent<TrafficVehicleAI>();
            if (ai == null) ai = carObj.AddComponent<TrafficVehicleAI>();
            ai.cruiseSpeedKmh = Random.Range(34f, 48f);
            ai.InitializeAtNode(roadNetwork, nodeIdx);
            _activeTraffic.Add(ai);
        }

        private static void ApplyTrafficColorVariant(GameObject car)
        {
            Color[] coastalCarPaints =
            {
                Palette.Hex("#EAECEB"),
                Palette.Hex("#A5B0BC"),
                Palette.Hex("#1E3A5F"),
                Palette.Hex("#283038"),
                Palette.Hex("#B83232"),
                Palette.Hex("#2B7A96"),
                Palette.Hex("#D4B888"),
            };
            Color chosen = coastalCarPaints[Random.Range(0, coastalCarPaints.Length)];
            foreach (var mr in car.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = mr.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.Contains("Car_Paint") && !mats[i].name.Contains("Secondary"))
                        mats[i].color = chosen;
                }
            }
        }
    }
}
