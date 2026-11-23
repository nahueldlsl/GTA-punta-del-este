using UnityEngine;

namespace GtaPunta.Player
{
    /// <summary>
    /// Velocity-driven procedural locomotion animation for our articulated Blender Char_Humanoid.fbx.
    /// Drives idle breathing, walking/sprinting gait bob, alternating limb swings,
    /// acceleration lean, turn bank, jump stretch/landing squash, and seated driving pose.
    /// </summary>
    public class ProceduralCharacterAnimator : MonoBehaviour
    {
        public float stepFrequency = 2.35f;
        public float bobAmplitude = 0.055f;
        public float armSwingDeg = 38f;
        public float legSwingDeg = 42f;
        public float leanAngleDeg = 7.5f;

        private Transform _hips;
        private Transform _spine;
        private Transform _head;
        private Transform _armL, _armR;
        private Transform _forearmL, _forearmR;
        private Transform _legL, _legR;
        private Transform _shinL, _shinR;

        private Vector3 _hipsBaseLocalPos;
        private float _gaitPhase;
        private float _blendSpeed;
        private float _squashTimer;
        private float _squashAmount;
        private bool _isGrounded = true;
        private bool _isSeated;
        private float _currentSpeedNorm;
        private float _turnRate;

        private void Awake()
        {
            CacheBones();
        }

        public void CacheBones()
        {
            _hips     = FindChildRecursive(transform, "Hips");
            _spine    = FindChildRecursive(transform, "Spine");
            _head     = FindChildRecursive(transform, "Head");
            _armL     = FindChildRecursive(transform, "Arm_L");
            _armR     = FindChildRecursive(transform, "Arm_R");
            _forearmL = FindChildRecursive(transform, "Forearm_L");
            _forearmR = FindChildRecursive(transform, "Forearm_R");
            _legL     = FindChildRecursive(transform, "Leg_L");
            _legR     = FindChildRecursive(transform, "Leg_R");
            _shinL    = FindChildRecursive(transform, "Shin_L");
            _shinR    = FindChildRecursive(transform, "Shin_R");

            if (_hips != null)
                _hipsBaseLocalPos = _hips.localPosition;
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        public void SetLocomotionState(float normalizedSpeed, bool grounded, float turnRate = 0f, bool seated = false)
        {
            if (!_isGrounded && grounded)
            {
                // Landing squash pulse
                _squashTimer = 0.14f;
                _squashAmount = 0.14f;
            }
            _isGrounded = grounded;
            _isSeated = seated;
            _currentSpeedNorm = Mathf.Clamp01(normalizedSpeed);
            _turnRate = Mathf.Clamp(turnRate, -1f, 1f);
        }

        private void LateUpdate()
        {
            if (_hips == null) return;

            float dt = Time.deltaTime;
            _blendSpeed = Mathf.Lerp(_blendSpeed, _currentSpeedNorm, dt * 10f);

            if (_isSeated)
            {
                ApplySeatedPose();
                return;
            }

            // Advance gait cycle proportionally to speed
            float freq = Mathf.Lerp(1.5f, stepFrequency * 1.35f, _blendSpeed);
            _gaitPhase += dt * freq * Mathf.PI * 2f * Mathf.Max(_blendSpeed, 0.05f);

            float sinGait = Mathf.Sin(_gaitPhase);
            float cosGait = Mathf.Cos(_gaitPhase);
            float idleBreath = Mathf.Sin(Time.time * 2.4f);

            // Hips vertical bob + subtle weight roll
            float bobY = Mathf.Abs(sinGait) * bobAmplitude * _blendSpeed + idleBreath * 0.012f * (1f - _blendSpeed);
            _hips.localPosition = _hipsBaseLocalPos + new Vector3(0f, bobY, 0f);
            float hipRoll = cosGait * 2.6f * _blendSpeed;
            _hips.localRotation = Quaternion.Euler(0f, 0f, hipRoll);

            // Squash & stretch on jump/land
            Vector3 scale = Vector3.one;
            if (!_isGrounded)
            {
                scale = new Vector3(0.95f, 1.07f, 0.95f);
            }
            else if (_squashTimer > 0f)
            {
                _squashTimer -= dt;
                float s = Mathf.Clamp01(_squashTimer / 0.14f) * _squashAmount;
                scale = new Vector3(1f + s * 0.7f, 1f - s, 1f + s * 0.7f);
            }
            transform.localScale = Vector3.Lerp(transform.localScale, scale, dt * 16f);

            // Spine lean into motion & turn bank
            if (_spine != null)
            {
                float pitch = leanAngleDeg * _blendSpeed + idleBreath * 1.4f * (1f - _blendSpeed);
                float roll = -_turnRate * 5.5f * _blendSpeed;
                float twist = -sinGait * 5.5f * _blendSpeed;
                _spine.localRotation = Quaternion.Euler(pitch, twist, roll);
            }

            // Head subtle counter-stabilization & idle coastal look-around
            if (_head != null)
            {
                float headYaw = Mathf.Sin(Time.time * 0.7f) * 7f * (1f - _blendSpeed) + _turnRate * 14f;
                _head.localRotation = Quaternion.Euler(-leanAngleDeg * 0.4f * _blendSpeed, headYaw, 0f);
            }

            // Arms & Forearms opposite to legs
            float armA = (_isGrounded ? sinGait * armSwingDeg * _blendSpeed : -28f);
            if (_armL != null) _armL.localRotation = Quaternion.Euler(-armA, 0f, 4f + idleBreath * 1.5f);
            if (_armR != null) _armR.localRotation = Quaternion.Euler( armA, 0f, -4f - idleBreath * 1.5f);

            float elbowFlexL = _isGrounded ? Mathf.Lerp(10f, 48f, _blendSpeed * Mathf.Clamp01(sinGait * 0.5f + 0.5f)) : 32f;
            float elbowFlexR = _isGrounded ? Mathf.Lerp(10f, 48f, _blendSpeed * Mathf.Clamp01(-sinGait * 0.5f + 0.5f)) : 32f;
            if (_forearmL != null) _forearmL.localRotation = Quaternion.Euler(-elbowFlexL, 0f, 0f);
            if (_forearmR != null) _forearmR.localRotation = Quaternion.Euler(-elbowFlexR, 0f, 0f);

            // Legs & Shins
            float legA = _isGrounded ? sinGait * legSwingDeg * _blendSpeed : 22f;
            if (_legL != null) _legL.localRotation = Quaternion.Euler( legA, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-legA, 0f, 0f);

            float kneeL = _isGrounded ? Mathf.Max(0f, -cosGait) * 44f * _blendSpeed : 34f;
            float kneeR = _isGrounded ? Mathf.Max(0f,  cosGait) * 44f * _blendSpeed : 24f;
            if (_shinL != null) _shinL.localRotation = Quaternion.Euler(kneeL, 0f, 0f);
            if (_shinR != null) _shinR.localRotation = Quaternion.Euler(kneeR, 0f, 0f);
        }

        private void ApplySeatedPose()
        {
            _hips.localPosition = _hipsBaseLocalPos + new Vector3(0f, -0.34f, 0f);
            if (_spine != null) _spine.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            if (_legL != null) _legL.localRotation = Quaternion.Euler(-82f, 0f, 0f);
            if (_legR != null) _legR.localRotation = Quaternion.Euler(-82f, 0f, 0f);
            if (_shinL != null) _shinL.localRotation = Quaternion.Euler(78f, 0f, 0f);
            if (_shinR != null) _shinR.localRotation = Quaternion.Euler(78f, 0f, 0f);
            if (_armL != null) _armL.localRotation = Quaternion.Euler(-52f, 8f, 6f);
            if (_armR != null) _armR.localRotation = Quaternion.Euler(-52f, -8f, -6f);
            if (_forearmL != null) _forearmL.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            if (_forearmR != null) _forearmR.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        }
    }
}
