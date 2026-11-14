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

            var move = transform.right * _input.Move.x + transform.forward * _input.Move.y;
            move = Vector3.ClampMagnitude(move, 1f);

            float feetBelowWater = IslandShape.WaterLevel - transform.position.y;
            IsSwimming = feetBelowWater > SwimDepth;

            float speed = _input.Sprint ? SprintSpeed : WalkSpeed;
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
