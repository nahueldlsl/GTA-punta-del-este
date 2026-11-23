using System.Collections.Generic;
using UnityEngine;
using GtaPunta.Core;
using GtaPunta.World;

namespace GtaPunta.AI
{
    /// <summary>
    /// Populates Punta del Este with summer pedestrians strolling Av. Gorlero,
    /// the Rambla promenade, Puerto piers, Playa Mansa, and Playa Brava / Los Dedos.
    /// </summary>
    public class PedestrianManager : MonoBehaviour
    {
        public RoadNetworkRuntime roadNetwork;
        public GameObject pedestrianTemplate;
        public int targetPedestrianCount = 48;

        private readonly List<PedestrianAI> _peds = new List<PedestrianAI>();
        public int ActivePedestrianCount => _peds.Count;

        private void Start()
        {
            if (roadNetwork == null) roadNetwork = FindFirstObjectByType<RoadNetworkRuntime>();
            if (pedestrianTemplate == null || roadNetwork == null || roadNetwork.pedestrianPoints.Count == 0)
                return;

            Color[] summerShirts =
            {
                Palette.Hex("#F2EFEA"),
                Palette.Hex("#2CA5C9"),
                Palette.Hex("#E86A50"),
                Palette.Hex("#F2B84B"),
                Palette.Hex("#3A7D5C"),
                Palette.Hex("#284B7A"),
                Palette.Hex("#D96B8A"),
            };

            Color[] summerPants =
            {
                Palette.Hex("#DFCFAF"),
                Palette.Hex("#354C66"),
                Palette.Hex("#2A2F36"),
                Palette.Hex("#C7B293"),
            };

            int count = Mathf.Min(targetPedestrianCount, roadNetwork.pedestrianPoints.Count);
            for (int i = 0; i < count; i++)
            {
                Vector3 pt = roadNetwork.pedestrianPoints[(i * 7) % roadNetwork.pedestrianPoints.Count];
                var pedObj = Instantiate(pedestrianTemplate, pt, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
                pedObj.name = "Pedestrian_" + i;
                pedObj.SetActive(true);

                Color shirt = summerShirts[i % summerShirts.Length];
                Color pants = summerPants[i % summerPants.Length];
                foreach (var mr in pedObj.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = mr.materials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] == null) continue;
                        if (mats[m].name.Contains("Char_Shirt")) mats[m].color = shirt;
                        else if (mats[m].name.Contains("Char_Pants")) mats[m].color = pants;
                    }
                }

                var ai = pedObj.GetComponent<PedestrianAI>();
                if (ai == null) ai = pedObj.AddComponent<PedestrianAI>();
                ai.walkSpeed = Random.Range(1.35f, 2.05f);
                ai.Initialize(pt);
                _peds.Add(ai);
            }
        }
    }
}
