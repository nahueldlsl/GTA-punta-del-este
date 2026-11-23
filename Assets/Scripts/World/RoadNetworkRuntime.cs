using System.Collections.Generic;
using UnityEngine;
using GtaPunta.Core;

namespace GtaPunta.World
{
    /// <summary>
    /// Stores the pre-built OpenStreetMap Punta del Este road graph, sidewalk stroll nodes,
    /// and iconic landmark metadata serialized into the scene by PuntaCityBuilder.
    /// </summary>
    public class RoadNetworkRuntime : MonoBehaviour
    {
        public List<RoadNode> roadNodes = new List<RoadNode>();
        public List<Vector3> pedestrianPoints = new List<Vector3>();
        public List<LandmarkRuntimeInfo> landmarks = new List<LandmarkRuntimeInfo>();

        public int FindNearestRoadNode(Vector3 worldPos, float maxDist = 150f)
        {
            int best = -1;
            float bestSq = maxDist * maxDist;
            for (int i = 0; i < roadNodes.Count; i++)
            {
                Vector3 d = roadNodes[i].position - worldPos;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        public int FindRandomSpawnNode(Vector3 avoidPos, float minDist, float maxDist)
        {
            if (roadNodes.Count == 0) return -1;
            int fallback = -1;
            for (int tries = 0; tries < 36; tries++)
            {
                int idx = Random.Range(0, roadNodes.Count);
                var n = roadNodes[idx];
                if (n.neighbors == null || n.neighbors.Count == 0) continue;
                float d = Vector2.Distance(new Vector2(n.position.x, n.position.z), new Vector2(avoidPos.x, avoidPos.z));
                if (d >= minDist && d <= maxDist)
                    return idx;
                if (fallback < 0 && d >= minDist * 0.5f)
                    fallback = idx;
            }
            return fallback >= 0 ? fallback : 0;
        }

        public string GetNearestRoadName(Vector3 pos, float maxDist = 48f)
        {
            float bestSq = maxDist * maxDist;
            string bestName = null;
            for (int i = 0; i < roadNodes.Count; i++)
            {
                if (string.IsNullOrEmpty(roadNodes[i].roadName)) continue;
                Vector3 d = roadNodes[i].position - pos;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestName = roadNodes[i].roadName;
                }
            }
            return bestName;
        }
    }
}
