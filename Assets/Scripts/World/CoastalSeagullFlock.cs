using UnityEngine;

namespace GtaPunta.World
{
    /// <summary>
    /// Animates a graceful soaring flock of coastal seagulls circling above
    /// Puerto de Punta del Este and Playa Brava with natural banking & altitude drift.
    /// </summary>
    public class CoastalSeagullFlock : MonoBehaviour
    {
        public float orbitRadius = 38f;
        public float orbitSpeed = 0.24f;
        public float altitudeBobAmp = 2.4f;
        public float phaseOffset = 0f;

        private Vector3 _center;
        private float _angle;
        private Vector3 _prevPos;

        private void Start()
        {
            _center = transform.position;
            _angle = phaseOffset;
            _prevPos = ComputePosition(_angle);
            transform.position = _prevPos;
        }

        private void Update()
        {
            _angle += orbitSpeed * Time.deltaTime;
            Vector3 nextPos = ComputePosition(_angle);
            Vector3 vel = nextPos - _prevPos;

            if (vel.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(vel.normalized, Vector3.up);
                float bank = Mathf.Sin(_angle * 2f + phaseOffset) * 14f + 16f;
                transform.rotation = look * Quaternion.Euler(0f, 0f, -bank);
            }

            transform.position = nextPos;
            _prevPos = nextPos;
        }

        private Vector3 ComputePosition(float a)
        {
            float r = orbitRadius * (1f + 0.14f * Mathf.Sin(a * 1.7f + phaseOffset));
            float x = _center.x + Mathf.Cos(a) * r;
            float z = _center.z + Mathf.Sin(a) * r;
            float y = _center.y + Mathf.Sin(a * 2.1f + phaseOffset) * altitudeBobAmp;
            return new Vector3(x, y, z);
        }
    }
}
