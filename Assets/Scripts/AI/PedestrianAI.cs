using UnityEngine;
using GtaPunta.Core;
using GtaPunta.Player;
using GtaPunta.Wanted;

namespace GtaPunta.AI
{
    public class PedestrianAI : MonoBehaviour
    {
        public float walkSpeed = 1.65f;
        public float fleeSpeed = 5.4f;

        private ProceduralCharacterAnimator _anim;
        private Vector3 _targetPos;
        private Vector3 _homeCenter;
        private float _idleTimer;
        private float _fleeTimer;
        private Vector3 _fleeSource;
        private float _hitCooldown;

        private void Awake()
        {
            _anim = GetComponentInChildren<ProceduralCharacterAnimator>();
            if (_anim == null) _anim = gameObject.AddComponent<ProceduralCharacterAnimator>();
        }

        public void Initialize(Vector3 startPos)
        {
            transform.position = startPos;
            _homeCenter = startPos;
            PickNewStrollTarget();
        }

        private void PickNewStrollTarget()
        {
            Vector2 rnd = Random.insideUnitCircle * Random.Range(14f, 42f);
            Vector3 cand = _homeCenter + new Vector3(rnd.x, 0f, rnd.y);
            cand.y = SampleY(cand);
            if (cand.y < 0.65f)
                cand = _homeCenter;
            _targetPos = cand;
        }

        private float SampleY(Vector3 p)
        {
            if (Terrain.activeTerrain != null)
            {
                float ty = Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;
                if (Physics.Raycast(new Vector3(p.x, ty + 12f, p.z), Vector3.down, out RaycastHit hit, 30f, LayerMask.GetMask("Default")))
                    return hit.point.y;
                return ty;
            }
            return p.y;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_hitCooldown > 0f) _hitCooldown -= dt;

            if (GameManager.Instance != null)
            {
                var vi = GameManager.Instance.vehicleInteraction;
                if (vi != null && vi.IsInVehicle && vi.CurrentVehicle != null)
                {
                    var car = vi.CurrentVehicle;
                    Vector3 toCar = car.transform.position - transform.position;
                    float dist = toCar.magnitude;
                    if (dist < 2.4f && car.SpeedKmh > 14f && _hitCooldown <= 0f)
                    {
                        _hitCooldown = 3.0f;
                        _fleeTimer = 7.0f;
                        _fleeSource = car.transform.position;
                        Vector3 push = transform.position - car.transform.position;
                        push.y = 0f;
                        transform.position += (push.sqrMagnitude > 0.01f ? push.normalized : transform.right) * 2.2f;
                        AudioManager.Instance?.PlayOneShot2D("Crash_Impact", 0.45f, 1.15f, 1.3f);
                        WantedSystem.Instance?.ReportCrime(WantedCrimeType.HitPedestrian, transform.position);
                    }
                    else if (dist < 10f && car.SpeedKmh > 25f)
                    {
                        _fleeTimer = 3.5f;
                        _fleeSource = car.transform.position;
                    }
                }
            }

            Vector3 moveDir = Vector3.zero;
            float speed = 0f;

            if (_fleeTimer > 0f)
            {
                _fleeTimer -= dt;
                Vector3 away = transform.position - _fleeSource;
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f)
                {
                    moveDir = away.normalized;
                    speed = fleeSpeed;
                }
            }
            else if (_idleTimer > 0f)
            {
                _idleTimer -= dt;
            }
            else
            {
                Vector3 toT = _targetPos - transform.position;
                toT.y = 0f;
                if (toT.magnitude < 1.8f)
                {
                    _idleTimer = Random.Range(1.2f, 4.2f);
                    PickNewStrollTarget();
                }
                else
                {
                    moveDir = toT.normalized;
                    speed = walkSpeed;
                }
            }

            if (speed > 0.05f && moveDir.sqrMagnitude > 0.01f)
            {
                if (Physics.Raycast(transform.position + Vector3.up * 0.9f, moveDir, 1.4f, ~LayerMask.GetMask("Water", "Ignore Raycast")))
                {
                    PickNewStrollTarget();
                }
                else
                {
                    Vector3 next = transform.position + moveDir * (speed * dt);
                    next.y = SampleY(next);
                    if (next.y > 0.45f)
                    {
                        transform.position = next;
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir, Vector3.up), dt * 8f);
                    }
                    else
                    {
                        PickNewStrollTarget();
                    }
                }
            }

            _anim?.SetLocomotionState(Mathf.Clamp01(speed / fleeSpeed), true, 0f, false);
        }
    }
}
