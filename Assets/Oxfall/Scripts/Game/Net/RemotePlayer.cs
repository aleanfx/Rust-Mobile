using Oxfall.Core.Net;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Net
{
    /// <summary>
    /// Another player as this phone sees them: a survivor figure drawn a moment in the past, moving
    /// smoothly between the positions the server sent, legs and arms swinging with their speed.
    /// </summary>
    public sealed class RemotePlayer
    {
        private static Mesh _body, _leg, _arm;

        public ushort Id { get; }
        public string Name { get; }
        public Transform Root { get; }
        public PoseBuffer Poses { get; } = new PoseBuffer();
        public bool Visible => Root.gameObject.activeSelf;

        private readonly Transform _legL, _legR, _armL, _armR;
        private Vector3 _last;
        private float _speed;
        private float _phase;

        public RemotePlayer(ushort id, string name, MaterialLibrary materials, Transform parent)
        {
            Id = id;
            Name = name;
            _body ??= MeshFactory.SurvivorBody();
            _leg ??= MeshFactory.SurvivorLeg();
            _arm ??= MeshFactory.SurvivorArm();

            Root = new GameObject("Player " + id + " " + name).transform;
            Root.SetParent(parent, false);
            Part("Body", Root, Vector3.zero, _body, materials.Shirt, materials.Skin);
            _legL = Part("Leg L", Root, new Vector3(-0.09f, 0.92f, 0f), _leg, materials.Trousers, materials.Char);
            _legR = Part("Leg R", Root, new Vector3(0.09f, 0.92f, 0f), _leg, materials.Trousers, materials.Char);
            _armL = Part("Arm L", Root, new Vector3(-0.23f, 1.4f, 0f), _arm, materials.Shirt, materials.Skin);
            _armR = Part("Arm R", Root, new Vector3(0.23f, 1.4f, 0f), _arm, materials.Shirt, materials.Skin);
            _armL.localRotation = Quaternion.Euler(0f, 0f, -6f);
            _armR.localRotation = Quaternion.Euler(0f, 0f, 6f);

            // Something to hit later (and for the camera not to pass through); not the player's own layer.
            var capsule = Root.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            Root.gameObject.SetActive(false);
        }

        private static Transform Part(string name, Transform parent, Vector3 at, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go.transform;
        }

        /// <summary>Puts the figure where the player was at <paramref name="time"/> (server clock).</summary>
        public void Draw(float time, float dt)
        {
            if (Poses.Count == 0) return;
            var pose = Poses.Sample(time);
            var position = new Vector3(pose.X, pose.Y, pose.Z);
            if (!Visible)
            {
                Root.gameObject.SetActive(true);
                _last = position;
            }
            Root.SetPositionAndRotation(position, Quaternion.Euler(0f, pose.Yaw, 0f));

            // Walk cycle from the ground speed: legs and arms swing opposite, faster when running.
            if (dt > 0f)
            {
                var step = position - _last;
                step.y = 0f;
                _speed = Mathf.Lerp(_speed, step.magnitude / dt, 1f - Mathf.Exp(-dt * 10f));
            }
            _last = position;
            float swing = Mathf.Clamp01(_speed / 4f) * 32f;
            _phase += dt * Mathf.Lerp(4f, 9f, Mathf.Clamp01(_speed / 6.6f));
            float s = Mathf.Sin(_phase) * swing;
            _legL.localRotation = Quaternion.Euler(s, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-s, 0f, 0f);
            _armL.localRotation = Quaternion.Euler(-s * 0.8f, 0f, -6f);
            _armR.localRotation = Quaternion.Euler(s * 0.8f, 0f, 6f);
        }

        public void Destroy() => Object.Destroy(Root.gameObject);
    }
}
