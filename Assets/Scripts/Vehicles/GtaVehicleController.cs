using UnityEngine;
using UnityEngine.InputSystem;
using GtaPunta.Core;
using GtaPunta.Wanted;

namespace GtaPunta.Vehicles
{
    public enum VehicleArchetype
    {
        Cabrio,
        SportsGT,
        BeachSUV,
        CitySedan,
        Taxi,
        PoliceCruiser
    }

    /// <summary>
    /// Fun, planted GTA V arcade-sim WheelCollider vehicle controller for Punta del Este.
    /// Features speed-sensitive steering, anti-roll bars, handbrake drifting with smoke & screech,
    /// dynamic RPM engine audio, headlights/brake lights, horn, police emergency lightbar & siren,
    /// and seamless handoff between Player control and AI Traffic/Police control.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GtaVehicleController : MonoBehaviour
    {
        [Header("Identity & Profile")]
        public string vehicleDisplayName = "Punta Cabrio GT";
        public VehicleArchetype archetype = VehicleArchetype.Cabrio;
        public bool isPoliceVehicle;

        [Header("Handling Tuning")]
        public float motorTorque = 2400f;
        public float brakeTorque = 4200f;
        public float maxSpeedKmh = 148f;
        public float maxSteerAngle = 34f;
        public float highSpeedSteerAngle = 14f;
        public float antiRollForce = 6500f;
        public float downforce = 45f;

        [Header("Wheel Colliders & Visuals")]
        public WheelCollider wcFL;
        public WheelCollider wcFR;
        public WheelCollider wcRL;
        public WheelCollider wcRR;
        public Transform visualFL;
        public Transform visualFR;
        public Transform visualRL;
        public Transform visualRR;

        [Header("Lights, Seating & VFX")]
        public Transform driverSeatPivot;
        public Light headLightL;
        public Light headLightR;
        public Light sirenRedLight;
        public Light sirenBlueLight;
        public ParticleSystem driftSmokeL;
        public ParticleSystem driftSmokeR;
        public TrailRenderer skidTrailL;
        public TrailRenderer skidTrailR;
        public ParticleSystem crashSparks;

        public bool IsPlayerDriven { get; private set; }
        public float SpeedKmh { get; private set; }
        public float RpmNormalized { get; private set; }
        public int CurrentGear { get; private set; } = 1;
        public bool IsDrifting { get; private set; }
        public bool SirenActive { get; private set; }
        public float CurrentSteer => _steer;

        private Rigidbody _rb;
        private AudioSource _engineAudio;
        private AudioSource _screechAudio;
        private AudioSource _sirenAudio;

        // Inputs (shared by Player or AI!)
        private float _throttle;
        private float _steer;
        private bool _handbrake;
        private float _sirenFlashTimer;
        private float _lastCrashTime;

        // Debug/automated verification input hook
        private float _debugThrottle;
        private float _debugSteer;
        private float _debugExpiry;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = archetype switch
            {
                VehicleArchetype.BeachSUV => 1950f,
                VehicleArchetype.PoliceCruiser => 1700f,
                VehicleArchetype.SportsGT => 1380f,
                _ => 1520f
            };
            _rb.linearDamping = 0.06f;
            _rb.angularDamping = 0.65f;
            _rb.centerOfMass = new Vector3(0f, -0.32f, 0.08f);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void Start()
        {
            SetupAudio();
            if (isPoliceVehicle && !IsPlayerDriven)
                SetSiren(false);
        }

        private void SetupAudio()
        {
            var am = AudioManager.Instance;
            if (am == null) return;

            _engineAudio = gameObject.AddComponent<AudioSource>();
            _engineAudio.clip = am.GetClip("Engine_Idle_Loop");
            _engineAudio.loop = true;
            _engineAudio.spatialBlend = 1f;
            _engineAudio.minDistance = 4f;
            _engineAudio.maxDistance = 42f;
            _engineAudio.volume = 0f;
            if (_engineAudio.clip != null) _engineAudio.Play();

            _screechAudio = gameObject.AddComponent<AudioSource>();
            _screechAudio.clip = am.GetClip("Tire_Screech_Loop");
            _screechAudio.loop = true;
            _screechAudio.spatialBlend = 1f;
            _screechAudio.minDistance = 4f;
            _screechAudio.maxDistance = 45f;
            _screechAudio.volume = 0f;
            if (_screechAudio.clip != null) _screechAudio.Play();

            if (isPoliceVehicle)
            {
                _sirenAudio = gameObject.AddComponent<AudioSource>();
                _sirenAudio.clip = am.GetClip("Police_Siren_Loop");
                _sirenAudio.loop = true;
                _sirenAudio.spatialBlend = 1f;
                _sirenAudio.minDistance = 8f;
                _sirenAudio.maxDistance = 120f;
                _sirenAudio.volume = 0f;
                if (_sirenAudio.clip != null) _sirenAudio.Play();
            }
        }

        public void SetPlayerDriven(bool driven)
        {
            IsPlayerDriven = driven;
            if (!driven)
            {
                _throttle = 0f;
                _steer = 0f;
                _handbrake = true;
            }
            else
            {
                _handbrake = false;
            }
        }

        public void SetAIInputs(float throttle, float steer, bool handbrake)
        {
            if (IsPlayerDriven) return;
            _throttle = Mathf.Clamp(throttle, -1f, 1f);
            _steer = Mathf.Clamp(steer, -1f, 1f);
            _handbrake = handbrake;
        }

        public void SetDebugDrive(float throttle, float steer, float durationSeconds)
        {
            _debugThrottle = throttle;
            _debugSteer = steer;
            _debugExpiry = Time.time + durationSeconds;
        }

        public void SetSiren(bool active)
        {
            SirenActive = active && isPoliceVehicle;
            if (sirenRedLight != null) sirenRedLight.enabled = SirenActive;
            if (sirenBlueLight != null) sirenBlueLight.enabled = SirenActive;
        }

        private void Update()
        {
            bool playing = GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;

            if (IsPlayerDriven && playing)
                ReadPlayerInput();

            UpdateAudioAndVfx();
            UpdateLights();
        }

        private void ReadPlayerInput()
        {
            if (Time.time < _debugExpiry)
            {
                _throttle = _debugThrottle;
                _steer = _debugSteer;
                _handbrake = false;
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            float t = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) t += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) t -= 1f;
            _throttle = t;

            float s = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s -= 1f;
            _steer = Mathf.Lerp(_steer, s, Time.deltaTime * 10f);

            _handbrake = kb.spaceKey.isPressed;

            if (kb.hKey.wasPressedThisFrame)
                AudioManager.Instance?.PlayOneShot2D("Car_Horn", 0.55f, 0.96f, 1.04f);

            if (kb.qKey.wasPressedThisFrame && isPoliceVehicle)
                SetSiren(!SirenActive);

            // Quick un-flip recovery if player ever rolls onto roof or beaches on rocks
            if (kb.rKey.wasPressedThisFrame)
            {
                transform.position += Vector3.up * 1.1f;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        private void FixedUpdate()
        {
            if (wcFL == null || wcFR == null || wcRL == null || wcRR == null) return;

            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            SpeedKmh = _rb.linearVelocity.magnitude * 3.6f;

            // Speed-sensitive steering curve for responsive yet controlled Rambla high-speed cruising
            float speedFactor = Mathf.Clamp01(SpeedKmh / 110f);
            float steerAngle = _steer * Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, speedFactor);
            wcFL.steerAngle = steerAngle;
            wcFR.steerAngle = steerAngle;

            // Arcade-sim acceleration, braking & automatic reverse
            float appliedMotor = 0f;
            float appliedBrake = 0f;

            if (_handbrake)
            {
                appliedBrake = brakeTorque * 1.15f;
                SetRearSidewaysGrip(0.48f); // Fun GTA handbrake drift!
            }
            else
            {
                SetRearSidewaysGrip(1.22f);
                bool wantsReverse = _throttle < -0.05f && forwardSpeed < 1.2f;
                bool wantsBrake = (_throttle < -0.05f && forwardSpeed >= 1.2f) || (_throttle > 0.05f && forwardSpeed < -1.2f);

                if (wantsBrake)
                {
                    appliedBrake = brakeTorque;
                }
                else if (wantsReverse)
                {
                    appliedMotor = SpeedKmh < 34f ? _throttle * motorTorque * 0.65f : 0f;
                }
                else if (Mathf.Abs(_throttle) > 0.05f)
                {
                    float speedCap = maxSpeedKmh * (IsPlayerDriven ? 1f : 0.45f);
                    if (SpeedKmh < speedCap)
                        appliedMotor = _throttle * motorTorque;
                }
                else if (!IsPlayerDriven && SpeedKmh < 0.8f)
                {
                    appliedBrake = brakeTorque * 0.4f;
                }
            }

            // AWD bias (60% rear / 40% front) for crisp traction on Punta del Este beaches & Rambla
            wcRL.motorTorque = appliedMotor * 0.62f;
            wcRR.motorTorque = appliedMotor * 0.62f;
            wcFL.motorTorque = appliedMotor * 0.38f;
            wcFR.motorTorque = appliedMotor * 0.38f;

            wcFL.brakeTorque = _handbrake ? 0f : appliedBrake;
            wcFR.brakeTorque = _handbrake ? 0f : appliedBrake;
            wcRL.brakeTorque = appliedBrake;
            wcRR.brakeTorque = appliedBrake;

            // Anti-roll bars on front & rear axles
            ApplyAntiRoll(wcFL, wcFR);
            ApplyAntiRoll(wcRL, wcRR);

            // Downforce for planted high-speed feel
            _rb.AddForce(-transform.up * downforce * _rb.linearVelocity.magnitude);

            // Prevent sinking into deep ocean
            if (transform.position.y < -0.6f)
            {
                _rb.linearVelocity *= 0.90f;
                if (transform.position.y < -1.8f && IsPlayerDriven)
                {
                    transform.position = new Vector3(transform.position.x, 4.5f, transform.position.z);
                    _rb.linearVelocity = Vector3.zero;
                }
            }

            // Sync visual wheel meshes to WheelCollider poses
            SyncWheel(wcFL, visualFL);
            SyncWheel(wcFR, visualFR);
            SyncWheel(wcRL, visualRL);
            SyncWheel(wcRR, visualRR);

            // Telemetry: Gear & RPM
            if (forwardSpeed < -0.5f)
            {
                CurrentGear = -1; // Reverse
                RpmNormalized = Mathf.Clamp01(SpeedKmh / 35f);
            }
            else if (SpeedKmh < 2f && Mathf.Abs(_throttle) < 0.05f)
            {
                CurrentGear = 0; // Neutral
                RpmNormalized = 0.14f;
            }
            else
            {
                float gearSpan = maxSpeedKmh / 6f;
                CurrentGear = Mathf.Clamp(Mathf.FloorToInt(SpeedKmh / gearSpan) + 1, 1, 6);
                float inGear = (SpeedKmh - (CurrentGear - 1) * gearSpan) / gearSpan;
                RpmNormalized = Mathf.Clamp01(0.24f + inGear * 0.72f);
            }

            // Drift detection
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
            IsDrifting = SpeedKmh > 22f && (Mathf.Abs(localVel.x) > 3.2f || (_handbrake && SpeedKmh > 24f));
        }

        private void SetRearSidewaysGrip(float stiffness)
        {
            var fl = wcRL.sidewaysFriction; fl.stiffness = stiffness; wcRL.sidewaysFriction = fl;
            var fr = wcRR.sidewaysFriction; fr.stiffness = stiffness; wcRR.sidewaysFriction = fr;
        }

        private void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            float travelL = 1f, travelR = 1f;
            bool groundedL = left.GetGroundHit(out WheelHit hitL);
            if (groundedL) travelL = (-left.transform.InverseTransformPoint(hitL.point).y - left.radius) / left.suspensionDistance;
            bool groundedR = right.GetGroundHit(out WheelHit hitR);
            if (groundedR) travelR = (-right.transform.InverseTransformPoint(hitR.point).y - right.radius) / right.suspensionDistance;

            float force = (travelL - travelR) * antiRollForce;
            if (groundedL) _rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) _rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        private static void SyncWheel(WheelCollider col, Transform visual)
        {
            if (col == null || visual == null) return;
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            visual.position = pos;
            visual.rotation = rot;
        }

        private void UpdateAudioAndVfx()
        {
            float sfx = AudioManager.Instance != null ? AudioManager.Instance.EffectiveSfxGain : 0.8f;

            if (_engineAudio != null)
            {
                bool active = IsPlayerDriven || SpeedKmh > 2f;
                float targetVol = IsPlayerDriven ? 0.30f * sfx : (active ? 0.10f * sfx : 0f);
                _engineAudio.volume = Mathf.Lerp(_engineAudio.volume, targetVol, Time.deltaTime * 6f);
                _engineAudio.pitch = Mathf.Lerp(_engineAudio.pitch, 0.72f + RpmNormalized * 1.45f, Time.deltaTime * 8f);
            }

            if (_screechAudio != null)
            {
                float targetScreech = (IsDrifting && IsPlayerDriven) ? 0.34f * sfx : 0f;
                _screechAudio.volume = Mathf.Lerp(_screechAudio.volume, targetScreech, Time.deltaTime * 10f);
            }

            if (_sirenAudio != null)
            {
                float targetSiren = SirenActive ? 0.34f * sfx : 0f;
                _sirenAudio.volume = Mathf.Lerp(_sirenAudio.volume, targetSiren, Time.deltaTime * 8f);
            }

            bool showDrift = IsDrifting && IsPlayerDriven;
            EmitSmoke(driftSmokeL, showDrift);
            EmitSmoke(driftSmokeR, showDrift);
            if (skidTrailL != null) skidTrailL.emitting = showDrift && wcRL != null && wcRL.isGrounded;
            if (skidTrailR != null) skidTrailR.emitting = showDrift && wcRR != null && wcRR.isGrounded;
        }

        private static void EmitSmoke(ParticleSystem ps, bool active)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.enabled = active;
        }

        private void UpdateLights()
        {
            bool night = GameManager.Instance != null &&
                         GameManager.Instance.atmosphere != null &&
                         GameManager.Instance.atmosphere.currentMode != Atmosphere.TimeOfDayMode.SunnyAfternoon;

            bool headsOn = night && (IsPlayerDriven || SpeedKmh > 1.5f);
            if (headLightL != null) headLightL.enabled = headsOn;
            if (headLightR != null) headLightR.enabled = headsOn;

            if (SirenActive && sirenRedLight != null && sirenBlueLight != null)
            {
                _sirenFlashTimer += Time.deltaTime * 7.5f;
                bool redPhase = Mathf.Sin(_sirenFlashTimer * Mathf.PI) >= 0f;
                sirenRedLight.enabled = redPhase;
                sirenBlueLight.enabled = !redPhase;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impact = collision.relativeVelocity.magnitude;
            if (impact < 5.5f || Time.time - _lastCrashTime < 0.45f) return;
            _lastCrashTime = Time.time;

            if (IsPlayerDriven)
            {
                AudioManager.Instance?.PlayOneShot2D("Crash_Impact", Mathf.Clamp01(impact / 22f) * 0.75f, 0.9f, 1.1f);
                GameManager.Instance?.cameraRig?.TriggerImpactShake(Mathf.Clamp01(impact / 25f));
                if (crashSparks != null && impact > 8.5f && collision.contactCount > 0)
                {
                    crashSparks.transform.position = collision.GetContact(0).point;
                    crashSparks.Play();
                }

                // Trigger Wanted heat if player rams traffic or police cruisers recklessly!
                var otherCar = collision.collider.GetComponentInParent<GtaVehicleController>();
                if (otherCar != null && otherCar != this)
                {
                    if (otherCar.isPoliceVehicle)
                        WantedSystem.Instance?.ReportCrime(WantedCrimeType.RamPolice, transform.position);
                    else if (impact > 9.5f)
                        WantedSystem.Instance?.ReportCrime(WantedCrimeType.RecklessCrash, transform.position);
                }
            }
        }
    }
}
