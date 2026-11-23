using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using GtaPunta.Core;
using GtaPunta.Vehicles;

namespace GtaPunta.Player
{
    /// <summary>
    /// Cinemachine 3 camera controller for GTA: Punta del Este:
    /// - Title Mode: sweeping scenic drone view over Los Dedos, Playa Brava & Punta del Este towers.
    /// - Foot Mode: smooth third-person over-shoulder orbital camera with anti-clip raycast pullback.
    /// - Vehicle Mode: dynamic chase-orbit camera with speed-reactive FOV kick, auto-recenter, and impact impulse shake.
    /// </summary>
    public class GtaCameraRig : MonoBehaviour
    {
        [Header("Cinemachine 3 Cameras")]
        public CinemachineCamera gameplayCam;
        public CinemachineCamera titleCam;
        public CinemachineOrbitalFollow orbitalFollow;
        public CinemachineImpulseSource impulseSource;

        [Header("Orbit Settings")]
        public float mouseSensitivityX = 0.22f;
        public float mouseSensitivityY = 0.16f;
        public float footRadius = 4.6f;
        public float vehicleRadius = 7.4f;
        public float minPitch = -8f;
        public float maxPitch = 58f;
        public float autoRecenterDelay = 1.6f;
        public float autoRecenterSpeed = 3.2f;

        [Header("Title Orbit")]
        public Vector3 titleFocusPoint = new Vector3(794.6f, 7.8f, 511.3f); // Los Dedos / Playa Brava + Punta Skyline & PureSky!
        public float titleOrbitRadius = 21f;
        public float titleOrbitHeight = 6.4f;
        public float titleOrbitSpeed = 4.8f;

        private Transform _currentTarget;
        private GtaVehicleController _activeVehicle;
        private bool _isVehicleMode;
        private bool _isTitleMode = true;
        private float _yaw = 35f;
        private float _pitch = 16f;
        private float _timeSinceMouseMove = 5f;
        private float _titleAngle = 215f;
        private float _currentRadius = 4.6f;

        private void Awake()
        {
            if (gameplayCam != null && orbitalFollow == null)
                orbitalFollow = gameplayCam.GetComponent<CinemachineOrbitalFollow>();
        }

        public void SetTitleMode(bool titleMode)
        {
            _isTitleMode = titleMode;
            if (titleCam != null) titleCam.Priority = titleMode ? 30 : 5;
            if (gameplayCam != null) gameplayCam.Priority = titleMode ? 10 : 25;
        }

        public void SetCameraAngles(float yaw, float pitch = 15f)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            _timeSinceMouseMove = 0f;
            if (orbitalFollow != null)
            {
                orbitalFollow.HorizontalAxis.Value = _yaw;
                orbitalFollow.VerticalAxis.Value = _pitch;
            }
        }

        public void SetFootTarget(Transform footPivot)
        {
            _currentTarget = footPivot;
            _activeVehicle = null;
            _isVehicleMode = false;
            if (gameplayCam != null)
            {
                gameplayCam.Target.TrackingTarget = footPivot;
                gameplayCam.Target.LookAtTarget = footPivot;
            }
        }

        public void SetVehicleTarget(Transform vehicleRoot)
        {
            _currentTarget = vehicleRoot;
            _activeVehicle = vehicleRoot != null ? vehicleRoot.GetComponent<GtaVehicleController>() : null;
            _isVehicleMode = true;
            if (gameplayCam != null)
            {
                gameplayCam.Target.TrackingTarget = vehicleRoot;
                gameplayCam.Target.LookAtTarget = vehicleRoot;
            }
            if (vehicleRoot != null)
                _yaw = vehicleRoot.eulerAngles.y;
        }

        public void TriggerImpactShake(float intensity)
        {
            if (impulseSource != null)
                impulseSource.GenerateImpulse(Mathf.Clamp(intensity, 0.1f, 1f) * 0.45f);
        }

        private void LateUpdate()
        {
            if (_isTitleMode)
            {
                UpdateTitleCamera();
                return;
            }

            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
                return;

            if (orbitalFollow == null || _currentTarget == null) return;

            float dt = Time.deltaTime;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (delta.sqrMagnitude > 0.01f)
                {
                    _yaw += delta.x * mouseSensitivityX;
                    _pitch = Mathf.Clamp(_pitch - delta.y * mouseSensitivityY, minPitch, maxPitch);
                    _timeSinceMouseMove = 0f;
                }
                else
                {
                    _timeSinceMouseMove += dt;
                }
            }

            // Smooth GTA auto-recenter behind moving vehicle when user stops steering camera
            if (_isVehicleMode && _activeVehicle != null && _activeVehicle.SpeedKmh > 8f && _timeSinceMouseMove > autoRecenterDelay)
            {
                float carYaw = _activeVehicle.transform.eulerAngles.y;
                _yaw = Mathf.LerpAngle(_yaw, carYaw, dt * autoRecenterSpeed);
                _pitch = Mathf.Lerp(_pitch, 15f, dt * 2.2f);
            }

            // Compute desired radius + clean obstacle pullback raycast (avoids CinemachineDeoccluder bug!)
            float desiredRadius = _isVehicleMode ? vehicleRadius : footRadius;
            Vector3 pivot = _currentTarget.position + Vector3.up * (_isVehicleMode ? 1.55f : 0.25f);
            Quaternion orbitRot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 backDir = orbitRot * Vector3.back;

            float safeRadius = desiredRadius;
            int mask = ~LayerMask.GetMask("Ignore Raycast", "Water", "UI");
            if (Physics.SphereCast(pivot, 0.28f, backDir, out RaycastHit hit, desiredRadius, mask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(_currentTarget) && hit.transform != _currentTarget)
                    safeRadius = Mathf.Clamp(hit.distance - 0.35f, 1.5f, desiredRadius);
            }

            _currentRadius = Mathf.Lerp(_currentRadius, safeRadius, dt * 14f);

            orbitalFollow.HorizontalAxis.Value = _yaw;
            orbitalFollow.VerticalAxis.Value = _pitch;
            orbitalFollow.Radius = _currentRadius;
            orbitalFollow.TargetOffset = _isVehicleMode
                ? new Vector3(0f, 1.65f, 0f)
                : new Vector3(0.45f, 0.15f, 0f);

            // Speed-reactive FOV kick while driving along the Rambla
            if (gameplayCam != null)
            {
                float targetFov = _isVehicleMode && _activeVehicle != null
                    ? Mathf.Lerp(58f, 72f, Mathf.Clamp01(_activeVehicle.SpeedKmh / 140f))
                    : 58f;
                var lens = gameplayCam.Lens;
                lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFov, dt * 5f);
                gameplayCam.Lens = lens;
            }
        }

        private void UpdateTitleCamera()
        {
            if (titleCam == null) return;
            _titleAngle += titleOrbitSpeed * Time.unscaledDeltaTime;
            float rad = _titleAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(rad) * titleOrbitRadius, titleOrbitHeight, Mathf.Sin(rad) * titleOrbitRadius);
            titleCam.transform.position = titleFocusPoint + offset;
            titleCam.transform.LookAt(titleFocusPoint + Vector3.up * 1.8f);
        }
    }
}
