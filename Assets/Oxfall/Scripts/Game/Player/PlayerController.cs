using Oxfall.Core.Rules;
using Oxfall.Game.Controls;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Player
{
    /// <summary>
    /// First-person movement: walk, sprint, jump, swim at the surface, and look. The body turns left
    /// and right; only the head tilts up and down.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public const float Height = 1.8f;
        public const float EyeHeight = 1.62f;

        public float WalkSpeed = 4.2f;
        public float SprintSpeed = 6.6f;
        public float SwimSpeed = 2.6f;
        public float JumpSpeed = 6.0f;
        public float Gravity = 20f;

        /// <summary>Feet this far under the water line means swimming (the water is chest deep).</summary>
        public float SwimDepth = 1.25f;

        private CharacterController _body;
        private PlayerInput _input;
        private float _yaw;
        private float _pitch;
        private float _verticalSpeed;

        public Transform Head { get; private set; }
        public bool IsSwimming { get; private set; }

        /// <summary>On a ladder, going up or down it.</summary>
        public bool IsClimbing { get; private set; }

        /// <summary>The top of the ladder the feet are on, if they are on one (null: none).</summary>
        public System.Func<Vector3, float?> Climbable;

        /// <summary>
        /// Rowing a boat: given the oars (throttle, steer) and the frame's seconds, moves the boat and
        /// says where the seat is now and how far the boat turned (null: not in a boat).
        /// </summary>
        public System.Func<float, float, float, (Vector3 Seat, float Turned)> Row;

        /// <summary>The jump button while in a boat: get out.</summary>
        public event System.Action LeaveBoat;

        public bool IsRowing => Row != null;

        /// <summary>
        /// Flying a minicopter: given the stick, whether jump is held, where the pilot looks (yaw,
        /// pitch) and the frame's seconds, flies it and says where the seat is now (null: not flying).
        /// </summary>
        public System.Func<Vector2, bool, float, float, float, Vector3> Fly;

        public bool IsFlying => Fly != null;

        /// <summary>Eyes this far over the seat while sitting in a boat.</summary>
        public const float SeatedEyeHeight = 0.95f;

        /// <summary>Seconds left after jumping off a ladder before it catches the player again.</summary>
        private float _offLadder;

        public void Init(PlayerInput input, float yaw)
        {
            _input = input;
            _yaw = yaw;
            _body = GetComponent<CharacterController>();
            _body.height = Height;
            _body.radius = 0.35f;
            _body.center = new Vector3(0f, Height * 0.5f, 0f);
            _body.stepOffset = 0.45f;
            _body.slopeLimit = 50f;
            _body.skinWidth = 0.05f;
            _body.minMoveDistance = 0f;

            Head = new GameObject("Head").transform;
            Head.SetParent(transform, false);
            Head.localPosition = new Vector3(0f, EyeHeight, 0f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Moves the player instantly, facing the given way (pitch: positive looks down).</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        public void Teleport(Vector3 feet, float yaw, float pitch)
        {
            _body.enabled = false;
            transform.position = feet;
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            _verticalSpeed = 0f;
            _body.enabled = true;
        }

        /// <summary>Raises the player's feet to a height if they are below it (a floor was built under them).</summary>
        public void LiftTo(float y)
        {
            if (transform.position.y >= y) return;
            _body.enabled = false;
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
            _verticalSpeed = 0f;
            _body.enabled = true;
        }

        private void Update()
        {
            if (_input == null) return;
            float dt = Time.deltaTime;

            _yaw += _input.Look.x;
            _pitch = Mathf.Clamp(_pitch - _input.Look.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (Fly != null)
            {
                IsSwimming = false;
                IsClimbing = false;
                var seat = Fly(_input.Move, _input.JumpHeld, _yaw, _pitch, dt);
                _body.enabled = false;
                transform.position = seat;
                _body.enabled = true;
                _verticalSpeed = 0f;
                Head.localPosition = new Vector3(0f, SeatedEyeHeight, 0f);
                return;
            }
            if (Row != null)
            {
                IsSwimming = false;
                IsClimbing = false;
                if (_input.Jump)
                {
                    LeaveBoat?.Invoke();
                    return;
                }
                var (seat, turned) = Row(_input.Move.y, _input.Move.x, dt);
                // The view turns with the boat, as a rower's would.
                _yaw += turned;
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                _body.enabled = false;
                transform.position = seat;
                _body.enabled = true;
                _verticalSpeed = 0f;
                Head.localPosition = new Vector3(0f, SeatedEyeHeight, 0f);
                return;
            }
            if (Head.localPosition.y != EyeHeight) Head.localPosition = new Vector3(0f, EyeHeight, 0f);

            var move = transform.right * _input.Move.x + transform.forward * _input.Move.y;
            move = Vector3.ClampMagnitude(move, 1f);

            float feetBelowWater = IslandShape.WaterLevel - transform.position.y;
            IsSwimming = feetBelowWater > SwimDepth;

            float speed = _input.Sprint ? SprintSpeed : WalkSpeed;
            _offLadder = Mathf.Max(0f, _offLadder - dt);
            float? ladderTop = _offLadder > 0f || IsSwimming || Climbable == null ? null : Climbable(transform.position);
            // At its foot, stepping back walks away from it rather than down into the ground.
            IsClimbing = ladderTop.HasValue && !(_body.isGrounded && _input.Move.y <= 0.05f);
            if (IsClimbing)
            {
                if (_input.Jump)
                {
                    // Let go, and drop off backwards.
                    _offLadder = 0.6f;
                    _verticalSpeed = 1.5f;
                    _body.Move((-transform.forward * 2.5f + Vector3.up * _verticalSpeed) * dt);
                    KeepInsideWorld();
                    return;
                }
                // Forward climbs, back goes down; sideways steps along; pressing on carries over the top.
                float up = _input.Move.y * HighWalls.ClimbSpeed;
                if (up > 0f && transform.position.y + up * dt > ladderTop.Value - 0.25f) up = 0f;
                _verticalSpeed = 0f;
                var along = transform.right * _input.Move.x * 1.2f + transform.forward * Mathf.Max(0f, _input.Move.y) * 1.4f;
                _body.Move((along + Vector3.up * up) * dt);
                KeepInsideWorld();
                return;
            }
            if (IsSwimming)
            {
                speed = SwimSpeed;
                // Bob back up to the surface rather than sinking: no diving yet.
                float target = feetBelowWater - SwimDepth;
                _verticalSpeed = Mathf.Clamp(target * 3f, -2f, 2.5f);
            }
            else if (_body.isGrounded)
            {
                _verticalSpeed = -2f; // keeps the body pressed to slopes
                if (_input.Jump) _verticalSpeed = JumpSpeed;
            }
            else
            {
                _verticalSpeed -= Gravity * dt;
            }

            var velocity = move * speed + Vector3.up * _verticalSpeed;
            _body.Move(velocity * dt);

            KeepInsideWorld();
        }

        /// <summary>The sea goes on forever, but the playable world does not: swim too far and the current turns you back.</summary>
        private void KeepInsideWorld()
        {
            const float limit = IslandShape.HalfSize - 15f;
            var p = transform.position;
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude <= limit) return;
            flat = flat.normalized * limit;
            _body.enabled = false;
            transform.position = new Vector3(flat.x, p.y, flat.y);
            _body.enabled = true;
        }
    }
}
