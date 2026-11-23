using UnityEngine;
using UnityEngine.InputSystem;
using GtaPunta.Core;

namespace GtaPunta.Player
{
    /// <summary>
    /// Responsive GTA-style third-person on-foot controller using CharacterController
    /// + New Input System, camera-relative movement, sprint, jump with coyote time,
    /// surface-aware footsteps (asphalt vs Punta sand), and deep-ocean safe return.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerFootController : MonoBehaviour
    {
        [Header("Movement Tuning")]
        public float walkSpeed = 4.6f;
        public float sprintSpeed = 8.4f;
        public float acceleration = 14f;
        public float turnSmoothSpeed = 12f;
        public float jumpHeight = 1.45f;
        public float gravity = -24f;
        public float coyoteTime = 0.14f;

        [Header("Visuals & Audio")]
        public Transform visualRoot;
        public ProceduralCharacterAnimator charAnim;
        public Transform cameraTargetPivot;

        private CharacterController _cc;
        private Vector3 _velocity;
        private Vector3 _horizontalVel;
        private float _coyoteTimer;
        private float _stepTimer;
        private Vector3 _lastSafeShorePos;
        private float _lastYaw;
        private bool _controlsEnabled = true;
        private Transform _visualOriginalParent;
        private Vector3 _visualOriginalLocalPos;
        private Quaternion _visualOriginalLocalRot;
        private bool _isSeatedInCar;
        private float _stamina = 1f;

        // Clean verification/debug input hook (non-intrusive, zero effect unless invoked)
        private Vector2 _debugMoveInput;
        private bool _debugSprint;
        private float _debugInputExpiry;

        public float StaminaNormalized => _stamina;

        public bool ControlsEnabled
        {
            get => _controlsEnabled;
            set
            {
                _controlsEnabled = value;
                if (_cc != null) _cc.enabled = value;
                if (visualRoot != null && !_isSeatedInCar) visualRoot.gameObject.SetActive(value);
            }
        }

        public void SetSeatedInVehicle(Transform seatPivot)
        {
            _isSeatedInCar = true;
            _controlsEnabled = false;
            if (_cc != null) _cc.enabled = false;
            if (visualRoot != null && seatPivot != null)
            {
                if (_visualOriginalParent == null)
                {
                    _visualOriginalParent = visualRoot.parent;
                    _visualOriginalLocalPos = visualRoot.localPosition;
                    _visualOriginalLocalRot = visualRoot.localRotation;
                }
                visualRoot.gameObject.SetActive(true);
                visualRoot.SetParent(seatPivot, false);
                visualRoot.localPosition = Vector3.zero;
                visualRoot.localRotation = _visualOriginalLocalRot;
            }
            charAnim?.SetLocomotionState(0f, true, 0f, true);
        }

        public void ClearSeatedInVehicle()
        {
            _isSeatedInCar = false;
            if (visualRoot != null && _visualOriginalParent != null)
            {
                visualRoot.SetParent(_visualOriginalParent, false);
                visualRoot.localPosition = _visualOriginalLocalPos;
                visualRoot.localRotation = _visualOriginalLocalRot;
                visualRoot.gameObject.SetActive(true);
            }
            charAnim?.SetLocomotionState(0f, true, 0f, false);
            _controlsEnabled = true;
            if (_cc != null) _cc.enabled = true;
        }

        public float CurrentSpeed => new Vector3(_horizontalVel.x, 0f, _horizontalVel.z).magnitude;
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.76f;
            _cc.radius = 0.34f;
            _cc.center = new Vector3(0f, 0.88f, 0f);
            _cc.stepOffset = 0.42f;
            _cc.slopeLimit = 50f;
            _lastSafeShorePos = transform.position;
        }

        private void Start()
        {
            if (charAnim == null) charAnim = GetComponentInChildren<ProceduralCharacterAnimator>();
            _lastSafeShorePos = transform.position;
            _lastYaw = transform.eulerAngles.y;
        }

        public void SetDebugMove(Vector2 move, bool sprint, float durationSeconds)
        {
            _debugMoveInput = move;
            _debugSprint = sprint;
            _debugInputExpiry = Time.time + durationSeconds;
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            bool wasEnabled = _cc.enabled;
            _cc.enabled = false;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _velocity = Vector3.zero;
            _horizontalVel = Vector3.zero;
            _cc.enabled = wasEnabled;
            if (pos.y > 0.5f) _lastSafeShorePos = pos;
        }

        private void Update()
        {
            if (!_controlsEnabled || !_cc.enabled) return;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
            {
                charAnim?.SetLocomotionState(0f, true);
                return;
            }

            float dt = Time.deltaTime;
            ReadMoveInput(out Vector2 input, out bool sprint, out bool jumpPressed);

            // Camera-relative horizontal movement
            Transform camT = Camera.main != null ? Camera.main.transform : transform;
            Vector3 camFwd = camT.forward; camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 0.001f) camFwd = Vector3.forward;
            camFwd.Normalize();
            Vector3 camRight = camT.right; camRight.y = 0f; camRight.Normalize();

            Vector3 moveDir = (camFwd * input.y + camRight * input.x);
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            // Stamina regen / sprint drain
            bool activelySprinting = sprint && moveDir.sqrMagnitude > 0.05f && _stamina > 0.05f;
            _stamina = Mathf.Clamp01(_stamina + (activelySprinting ? -0.14f : 0.24f) * dt);

            // Shallow beach water wading slowdown
            bool inShallowWater = transform.position.y < 0.25f;
            float speedMul = inShallowWater ? 0.65f : 1f;
            float targetSpeed = (activelySprinting ? sprintSpeed : walkSpeed) * speedMul;
            Vector3 desiredVel = moveDir * targetSpeed;

            _horizontalVel = Vector3.Lerp(_horizontalVel, desiredVel, dt * acceleration);

            // Smooth facing rotation toward movement direction
            if (moveDir.sqrMagnitude > 0.02f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, dt * turnSmoothSpeed);
            }

            // Grounding & coyote-time jump
            if (_cc.isGrounded)
            {
                _coyoteTimer = coyoteTime;
                if (_velocity.y < 0f) _velocity.y = -3.0f;
            }
            else
            {
                _coyoteTimer -= dt;
            }

            if (jumpPressed && _coyoteTimer > 0f && !inShallowWater)
            {
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _coyoteTimer = 0f;
            }

            _velocity.y += gravity * dt;

            Vector3 finalMove = _horizontalVel + new Vector3(0f, _velocity.y, 0f);
            _cc.Move(finalMove * dt);

            // Track safe dry shore position; return gently if wading too deep into Atlantic/Plata
            if (_cc.isGrounded && transform.position.y > 0.6f)
                _lastSafeShorePos = transform.position;
            else if (transform.position.y < -1.15f)
                Teleport(_lastSafeShorePos + Vector3.up * 0.5f, transform.eulerAngles.y);

            // Update procedural character animation
            float speedNorm = Mathf.Clamp01(CurrentSpeed / sprintSpeed);
            float yawDelta = Mathf.DeltaAngle(_lastYaw, transform.eulerAngles.y) / Mathf.Max(dt, 0.001f);
            _lastYaw = transform.eulerAngles.y;
            charAnim?.SetLocomotionState(speedNorm, _cc.isGrounded, Mathf.Clamp(yawDelta / 180f, -1f, 1f), false);

            // Surface-aware footsteps (sand near beach level vs urban asphalt/sidewalk)
            if (_cc.isGrounded && CurrentSpeed > 1.2f)
            {
                _stepTimer -= dt * Mathf.Lerp(1.6f, 3.1f, speedNorm);
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 1f;
                    bool onSand = transform.position.y < 3.05f;
                    AudioManager.Instance?.PlayOneShot2D(onSand ? "Footstep_Sand" : "Footstep_Asphalt", 0.30f, 0.90f, 1.12f);
                }
            }
        }

        private void ReadMoveInput(out Vector2 input, out bool sprint, out bool jumpPressed)
        {
            input = Vector2.zero;
            sprint = false;
            jumpPressed = false;

            if (Time.time < _debugInputExpiry)
            {
                input = _debugMoveInput;
                sprint = _debugSprint;
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;

            sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            jumpPressed = kb.spaceKey.wasPressedThisFrame;
        }
    }
}
