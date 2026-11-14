using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Net
{
    /// <summary>Marks another player's collider, so a blow that lands on it knows whom it hit.</summary>
    public sealed class PlayerTag : MonoBehaviour
    {
        public ushort Id;
    }

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

        /// <summary>Dead: lying where they fell until they wake up somewhere.</summary>
        public bool Down { get; private set; }

        private readonly Transform _figure, _legL, _legR, _armL, _armR;
        private readonly MaterialLibrary _materials;
        private static Mesh _hood, _wrap;
        private Transform _headwear;
        private string _outfit = "";
        private readonly CapsuleCollider _capsule;
        private float _fall;
        private float _flinch;
        private Vector3 _last;
        private float _speed;
        private float _phase;

        public RemotePlayer(ushort id, string name, MaterialLibrary materials, Transform parent)
        {
            Id = id;
            Name = name;
            _materials = materials;
            _body ??= MeshFactory.SurvivorBody();
            _leg ??= MeshFactory.SurvivorLeg();
            _arm ??= MeshFactory.SurvivorArm();

            Root = new GameObject("Player " + id + " " + name).transform;
            Root.SetParent(parent, false);
            // The figure in a child of its own, so it can fall over without the root turning.
            _figure = new GameObject("Figure").transform;
            _figure.SetParent(Root, false);
            Part("Body", _figure, Vector3.zero, _body, materials.Shirt, materials.Skin);
            _legL = Part("Leg L", _figure, new Vector3(-0.09f, 0.92f, 0f), _leg, materials.Trousers, materials.Char);
            _legR = Part("Leg R", _figure, new Vector3(0.09f, 0.92f, 0f), _leg, materials.Trousers, materials.Char);
            _armL = Part("Arm L", _figure, new Vector3(-0.23f, 1.4f, 0f), _arm, materials.Shirt, materials.Skin);
            _armR = Part("Arm R", _figure, new Vector3(0.23f, 1.4f, 0f), _arm, materials.Shirt, materials.Skin);
            _armL.localRotation = Quaternion.Euler(0f, 0f, -6f);
            _armR.localRotation = Quaternion.Euler(0f, 0f, 6f);

            // Something to hit later (and for the camera not to pass through); not the player's own layer.
            var capsule = Root.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            _capsule = capsule;
            Root.gameObject.AddComponent<PlayerTag>().Id = id;
            Root.gameObject.SetActive(false);
        }

        /// <summary>Falls over on dying (and can no longer be hit); stands again on waking up.</summary>
        public void SetDown(bool down)
        {
            if (down == Down) return;
            Down = down;
            _capsule.enabled = !down;
            if (!down) _fall = 0f;
        }

        /// <summary>
        /// Dresses the figure in what the player wears (item ids, head to feet; empty for nothing): burlap
        /// or hide on the chest and legs, hide boots, a headwrap or a hood.
        /// </summary>
        public void SetOutfit(string[] worn)
        {
            string outfit = string.Join("|", worn);
            if (outfit == _outfit) return;
            _outfit = outfit;
            var m = _materials;
            string head = worn.Length > 0 ? worn[0] : "", chest = worn.Length > 1 ? worn[1] : "";
            string legs = worn.Length > 2 ? worn[2] : "", feet = worn.Length > 3 ? worn[3] : "";
            var shirt = chest == ItemIds.BurlapShirt ? m.Cloth : chest == ItemIds.HideVest ? m.Leather : m.Shirt;
            // A vest has no sleeves: the arms keep the shirt underneath.
            var sleeves = chest == ItemIds.BurlapShirt ? m.Cloth : m.Shirt;
            var trousers = legs == ItemIds.BurlapTrousers ? m.Cloth : legs == ItemIds.HidePants ? m.Leather : m.Trousers;
            var boots = feet == ItemIds.HideBoots ? m.Leather : m.Char;
            Dress(_figure.Find("Body"), shirt, m.Skin);
            Dress(_armL, sleeves, m.Skin);
            Dress(_armR, sleeves, m.Skin);
            Dress(_legL, trousers, boots);
            Dress(_legR, trousers, boots);

            if (_headwear != null) Object.Destroy(_headwear.gameObject);
            _headwear = null;
            if (head == ItemIds.HideHood || head == ItemIds.BurlapHeadwrap)
            {
                bool hood = head == ItemIds.HideHood;
                var mesh = hood ? (_hood ??= MeshFactory.Headwear(true)) : (_wrap ??= MeshFactory.Headwear(false));
                _headwear = Part("Headwear", _figure, Vector3.zero, mesh, hood ? m.Leather : m.Cloth, m.Rope);
            }
        }

        private static void Dress(Transform part, Material main, Material second)
        {
            if (part == null) return;
            var r = part.GetComponent<MeshRenderer>();
            var mats = r.sharedMaterials;
            if (mats.Length > 0) mats[0] = main;
            if (mats.Length > 1) mats[1] = second;
            r.sharedMaterials = mats;
        }

        /// <summary>A blow landed: a quick jolt backwards.</summary>
        public void Flinch() => _flinch = 1f;

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
            // Falling over when dead (face down, ahead), a jolt when hit.
            _fall = Mathf.MoveTowards(_fall, Down ? 1f : 0f, dt * 2.5f);
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 6f);
            float f = _fall * _fall;
            _figure.localRotation = Quaternion.Euler(f * 88f - _flinch * 9f, 0f, 0f);
            _figure.localPosition = new Vector3(0f, f * 0.16f, 0f);
            if (Down) _speed = 0f;
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
