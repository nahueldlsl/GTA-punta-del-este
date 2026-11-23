using System.Collections.Generic;
using UnityEngine;

namespace GtaPunta.World
{
    /// <summary>
    /// Functional Av. Gorlero & Rambla traffic light controller cycling realistic
    /// Green -> Yellow -> Red emissive lenses and exposing clean red-light checks for AI traffic.
    /// </summary>
    public class TrafficLightController : MonoBehaviour
    {
        public static readonly List<TrafficLightController> ActiveSignals = new List<TrafficLightController>();

        public Renderer redLens;
        public Renderer yellowLens;
        public Renderer greenLens;
        public float cycleDuration = 18f;
        public float phaseOffset = 0f;

        public bool IsRed { get; private set; }
        public bool IsYellow { get; private set; }
        public bool IsGreen { get; private set; }

        private MaterialPropertyBlock _mpbRed;
        private MaterialPropertyBlock _mpbYellow;
        private MaterialPropertyBlock _mpbGreen;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void OnEnable()
        {
            if (!ActiveSignals.Contains(this))
                ActiveSignals.Add(this);
        }

        private void OnDisable()
        {
            ActiveSignals.Remove(this);
        }

        private void Awake()
        {
            _mpbRed = new MaterialPropertyBlock();
            _mpbYellow = new MaterialPropertyBlock();
            _mpbGreen = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float t = Mathf.Repeat(Time.time + phaseOffset, cycleDuration);
            float greenEnd = cycleDuration * 0.48f;
            float yellowEnd = cycleDuration * 0.60f;

            bool g = t < greenEnd;
            bool y = t >= greenEnd && t < yellowEnd;
            bool r = t >= yellowEnd;

            if (g != IsGreen || y != IsYellow || r != IsRed)
            {
                IsGreen = g;
                IsYellow = y;
                IsRed = r;
                ApplyLens(redLens, _mpbRed, new Color(0.95f, 0.10f, 0.08f), r ? 3.5f : 0.0f);
                ApplyLens(yellowLens, _mpbYellow, new Color(1.0f, 0.78f, 0.08f), y ? 3.2f : 0.0f);
                ApplyLens(greenLens, _mpbGreen, new Color(0.10f, 0.92f, 0.32f), g ? 3.2f : 0.0f);
            }
        }

        private static void ApplyLens(Renderer rend, MaterialPropertyBlock mpb, Color activeCol, float emitStrength)
        {
            if (rend == null) return;
            Color offCol = new Color(0.08f, 0.09f, 0.10f, 1f);
            mpb.SetColor(BaseColorId, emitStrength > 0.1f ? activeCol : offCol);
            mpb.SetColor(EmissionColorId, activeCol * emitStrength);
            rend.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Returns true if a vehicle approaching from worldPos heading along forward
        /// is within the stop zone (5m..14m ahead) of an active red traffic light facing it.
        /// </summary>
        public static bool ShouldStopForRedLight(Vector3 vehiclePos, Vector3 vehicleForward)
        {
            for (int i = 0; i < ActiveSignals.Count; i++)
            {
                var sig = ActiveSignals[i];
                if (sig == null || !sig.IsRed) continue;
                Vector3 toSig = sig.transform.position - vehiclePos;
                toSig.y = 0f;
                float distSq = toSig.sqrMagnitude;
                if (distSq < 25f || distSq > 225f) continue; // 5m .. 15m stop zone
                float ahead = Vector3.Dot(vehicleForward, toSig.normalized);
                if (ahead > 0.72f)
                {
                    float facingDot = Vector3.Dot(sig.transform.forward, -vehicleForward);
                    if (facingDot > 0.45f)
                        return true;
                }
            }
            return false;
        }
    }
}
