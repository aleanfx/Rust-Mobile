using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Game.Items;
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
    /// smoothly between the positions the server sent, legs and arms swinging with their speed, with
    /// what they hold in their hands where their own screen shows it: a gun shouldered and raised
    /// where they look, a bow held out, a hatchet or a torch low on the right.
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

        // What they hold: shown where it sits on their own screen, from their eyes, turned up or down
        // with where they look; the arms reach for it.
        private static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0.05f);
        private static readonly Vector3 ShoulderL = new Vector3(-0.23f, 1.4f, 0f), ShoulderR = new Vector3(0.23f, 1.4f, 0f);
        private const float ArmLength = 0.56f;
        private enum Hands { Free, One, Rifle, Pistol, Bow }
        private Transform _held;
        private ItemModel _heldModel;
        private string _heldId = "";
        private Hands _hands;
        private float _pitch, _pitchTarget;

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

        /// <summary>Where they look, degrees up (from the snapshots): their gun follows it.</summary>
        public void SetPitch(float pitch) => _pitchTarget = Mathf.Clamp(pitch, -89f, 89f);

        /// <summary>The item id they hold (empty for bare hands)</summary>
        public string Held => _heldId;

        /// <summary>
        /// Puts what they now hold in their hands (<paramref name="model"/> null or not held in the
        /// hand: nothing shows). A torch burns.
        /// </summary>
        public void SetHeld(string itemId, ItemModel model)
        {
            itemId ??= "";
            if (itemId == _heldId) return;
            _heldId = itemId;
            if (_held != null) Object.Destroy(_held.gameObject);
            _held = null;
            _heldModel = null;
            _hands = Hands.Free;
            _armL.localScale = _armR.localScale = Vector3.one;
            if (model == null || !model.ShowInHand || model.Mesh == null) return;
            _heldModel = model;
            _held = Part("Held " + itemId, _figure, Vector3.zero, model.Mesh, model.Materials);
            _held.localScale = Vector3.one * model.HandScale;
            _hands = model.Fires ? (itemId == ItemIds.Revolver ? Hands.Pistol : Hands.Rifle) : model.Shoots ? Hands.Bow : Hands.One;
            if (model.Burns)
                Fire.Create(_held, model.FirePosition, 0.7f, 2, 9f, 2.4f, _flame ??= MeshFactory.Flame(), _materials.Flame);
            Draw(_lastTime, 0f);
        }

        private static Mesh _flame;
        private float _lastTime;

        /// <summary>Where the muzzle of the gun in their hands is now, if they hold one.</summary>
        public bool TryMuzzle(out Vector3 world)
        {
            world = default;
            if (_held == null || _heldModel == null || !_heldModel.Fires || Down || !Visible) return false;
            world = _held.TransformPoint(_heldModel.Muzzle);
            return true;
        }

        /// <summary>Turns an arm to reach from its shoulder towards a point (figure space), a little longer or shorter to get there.</summary>
        private static void Reach(Transform arm, Vector3 shoulder, Vector3 target)
        {
            var d = target - shoulder;
            if (d.sqrMagnitude < 1e-4f) return;
            arm.localRotation = Quaternion.FromToRotation(Vector3.down, d);
            arm.localScale = new Vector3(1f, Mathf.Clamp(d.magnitude / ArmLength, 0.7f, 1.3f), 1f);
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
            _lastTime = time;
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
            _armL.localScale = _armR.localScale = Vector3.one;
            if (_held == null) return;

            // What they hold, where it sits on their screen: aimed weapons follow where they look,
            // anything else only a little; a bob as they walk.
            _held.gameObject.SetActive(!Down);
            if (Down) return;
            _pitch = dt > 0f ? Mathf.Lerp(_pitch, _pitchTarget, 1f - Mathf.Exp(-dt * 12f)) : _pitchTarget;
            float p = _hands == Hands.One ? _pitch * 0.35f : _pitch;
            var look = Quaternion.Euler(-p, 0f, 0f);
            float bob = Mathf.Sin(_phase * 2f) * 0.012f * Mathf.Clamp01(_speed / 4f);
            var at = Eye + look * _heldModel.HandPosition + new Vector3(0f, bob, 0f);
            var turn = look * _heldModel.HandRotation;
            _held.localPosition = at;
            _held.localRotation = turn;
            float scale = _heldModel.HandScale;
            switch (_hands)
            {
                case Hands.Rifle:
                    // Shouldered: the right hand on the grip, the left under the fore-end.
                    Reach(_armR, ShoulderR, at);
                    Reach(_armL, ShoulderL, at + turn * new Vector3(0.24f * scale, -0.03f * scale, 0f));
                    break;
                case Hands.Pistol:
                    // Held out in both hands.
                    Reach(_armR, ShoulderR, at);
                    Reach(_armL, ShoulderL, at + look * new Vector3(-0.03f, -0.03f, -0.02f));
                    break;
                case Hands.Bow:
                    // The bow out in the left hand, the right at the string by the chin.
                    Reach(_armL, ShoulderL, at);
                    Reach(_armR, ShoulderR, Eye + look * new Vector3(0.06f, -0.14f, 0.18f));
                    break;
                default:
                    Reach(_armR, ShoulderR, at);
                    break;
            }
        }

        public void Destroy() => Object.Destroy(Root.gameObject);
    }
}
