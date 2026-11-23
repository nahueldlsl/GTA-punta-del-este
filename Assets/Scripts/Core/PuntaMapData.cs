using System;
using System.Collections.Generic;
using UnityEngine;

namespace GtaPunta.Core
{
    [Serializable]
    public class PuntaMapRoot
    {
        public PuntaMapMeta meta;
        public List<LandmarkData> landmarks;
        public List<RoadData> roads;
        public List<BuildingData> buildings;
        public List<PierData> piers;
        public List<ParkData> parks;
    }

    [Serializable]
    public class PuntaMapMeta
    {
        public string city;
        public float originLat;
        public float originLon;
        public float[] boundsX;
        public float[] boundsZ;
    }

    [Serializable]
    public class LandmarkData
    {
        public string id;
        public string name;
        public string district;
        public float x;
        public float z;
        public float yaw;
    }

    [Serializable]
    public class RoadData
    {
        public string id;
        public string name;
        public string @class; // rambla, avenue, street, service
        public float width;
        public bool oneway;
        public List<float[]> points;
    }

    [Serializable]
    public class BuildingData
    {
        public string id;
        public string name;
        public string style;
        public float height;
        public float area;
        public float[] center;
        public List<float[]> points;
    }

    [Serializable]
    public class PierData
    {
        public string id;
        public string kind;
        public string name;
        public bool closed;
        public List<float[]> points;
    }

    [Serializable]
    public class ParkData
    {
        public string id;
        public string name;
        public List<float[]> points;
    }

    [Serializable]
    public class TerrainMeta
    {
        public float minX;
        public float maxX;
        public float minZ;
        public float maxZ;
        public float size;
        public float minY;
        public float maxY;
        public int heightRes;
    }

    /// <summary>
    /// Runtime waypoint node stored in the scene by PuntaCityBuilder so traffic & police AI
    /// follow the exact OpenStreetMap road network of Punta del Este cleanly.
    /// </summary>
    [Serializable]
    public class RoadNode
    {
        public Vector3 position;
        public string roadName;
        public bool isRambla;
        public bool isAvenue;
        public List<int> neighbors = new List<int>();
    }

    [Serializable]
    public class LandmarkRuntimeInfo
    {
        public string id;
        public string displayName;
        public string district;
        public Vector3 position;
        public float teleportYaw;
        public bool discovered;
    }
}
