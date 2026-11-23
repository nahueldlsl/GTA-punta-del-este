using UnityEngine;

namespace GtaPunta.World
{
    /// <summary>
    /// Gentle maritime bobbing & rolling motion for Puerto de Punta del Este yachts,
    /// sailboats, and artisanal fishing boats moored in Playa Mansa bay.
    /// </summary>
    public class FloatingBoatBob : MonoBehaviour
    {
        public float bobAmplitude = 0.14f;
        public float bobSpeed = 1.15f;
        public float rollAmplitude = 2.6f;
        public float pitchAmplitude = 1.4f;
        public float phaseOffset;

        private Vector3 _basePos;
        private Quaternion _baseRot;

        private void Start()
        {
            _basePos = transform.position;
            _baseRot = transform.rotation;
            if (Mathf.Approximately(phaseOffset, 0f))
                phaseOffset = (transform.position.x * 0.07f + transform.position.z * 0.11f);
        }

        private void Update()
        {
            float t = Time.time * bobSpeed + phaseOffset;
            float dy = Mathf.Sin(t) * bobAmplitude;
            float roll = Mathf.Sin(t * 0.85f + 0.8f) * rollAmplitude;
            float pitch = Mathf.Cos(t * 0.72f + 1.5f) * pitchAmplitude;

            transform.position = new Vector3(_basePos.x, _basePos.y + dy, _basePos.z);
            transform.rotation = _baseRot * Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
