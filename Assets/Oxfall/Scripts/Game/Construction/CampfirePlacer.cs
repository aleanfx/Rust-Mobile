using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Game.Controls;
using Oxfall.Game.Player;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// While a campfire is in hand: a ghost of it on the ground ahead, green where it can go, and
    /// attack puts one down. A placed campfire burns with a flickering light that holds the night
    /// back around it.
    /// </summary>
    public sealed class CampfirePlacer : MonoBehaviour
    {
        /// <summary>How far ahead of the player it goes, in metres.</summary>
        public float Reach = 2.4f;

        private const float Radius = 0.5f;

        private PlayerInput _input;
        private Inventory _inventory;
        private PlayerController _player;
        private MaterialLibrary _materials;
        private Hud _hud;
        private Mesh _mesh;
        private Mesh _flame;
        private Material[] _fireMaterials;
        private Transform _placed;

        private GameObject _ghost;
        private MeshRenderer _ghostRenderer;
        private Material[] _ghostOk;
        private Material[] _ghostBad;
        private bool _valid;
        private string _whyNot = "";
        private Vector3 _point;

        /// <summary>This player put a campfire down (the knock of placing it).</summary>
        public event System.Action Placed;

        /// <summary>A campfire appeared, anyone's (for its crackle).</summary>
        public event System.Action<GameObject> Spawned;

        /// <summary>Where placing goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        private readonly System.Collections.Generic.Dictionary<int, GameObject> _fires =
            new System.Collections.Generic.Dictionary<int, GameObject>();

        public bool Active { get; private set; }
        public int PlacedCount => _fires.Count;

        /// <summary>Draws a campfire of the settlement (this player's or anyone's).</summary>
        public void Show(Campfire fire)
        {
            if (_fires.ContainsKey(fire.Id)) return;
            var go = Spawn(new Vector3(fire.X, fire.Y, fire.Z), Quaternion.Euler(0f, fire.Yaw, 0f));
            _fires.Add(fire.Id, go);
            Spawned?.Invoke(go);
        }

        public void Hide(int id)
        {
            if (!_fires.TryGetValue(id, out var go)) return;
            _fires.Remove(id);
            Destroy(go);
        }

        public void Init(PlayerInput input, Inventory inventory, PlayerController player, MaterialLibrary materials, Hud hud,
            Mesh campfireMesh, Material[] campfireMaterials)
        {
            _input = input;
            _inventory = inventory;
            _player = player;
            _materials = materials;
            _hud = hud;
            _mesh = campfireMesh;
            _fireMaterials = campfireMaterials;
            _flame = MeshFactory.Flame();
            _placed = new GameObject("Campfires").transform;

            _ghost = new GameObject("Campfire ghost");
            _ghost.AddComponent<MeshFilter>().sharedMesh = campfireMesh;
            _ghostRenderer = _ghost.AddComponent<MeshRenderer>();
            _ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ghostRenderer.receiveShadows = false;
            _ghostOk = Fill(materials.GhostOk, campfireMaterials.Length);
            _ghostBad = Fill(materials.GhostBad, campfireMaterials.Length);
            _ghost.SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            _ghost.SetActive(on);
            _hud.SetBuildMode(on, null);
            if (on) _hud.ShowToast("Apunta al suelo y toca COLOCAR");
        }

        public void Place()
        {
            if (!Active) return;
            Aim();
            if (!_valid)
            {
                _hud.ShowToast(_whyNot);
                return;
            }
            var result = Actions.Build(BuildOp.PlaceCampfire(
                new Campfire(0, _point.x, _point.y, _point.z, _ghost.transform.eulerAngles.y)));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            Placed?.Invoke();
            _hud.ShowToast("Fogata encendida");
            Debug.Log($"[Oxfall] Placed a campfire at {_point}");
        }

        /// <summary>A burning campfire: stones and logs, three flames and a warm light.</summary>
        private GameObject Spawn(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Campfire");
            go.transform.SetParent(_placed, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = _fireMaterials;
            var collider = go.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.2f, 0f);
            collider.radius = 0.5f;
            collider.height = 0.4f;
            Fire.Create(go.transform, new Vector3(0f, 0.04f, 0f), 1.7f, 5, 14f, 3.4f, _flame, _materials.Flame);
            return go;
        }

        private void Update()
        {
            if (!Active) return;
            if (_input.AttackPressed) Place();
            if (!Active) return;
            Aim();
            _ghostRenderer.sharedMaterials = _valid ? _ghostOk : _ghostBad;
            _hud.SetPlaceValid(_valid);
        }

        /// <summary>Finds the ground (or a floor) ahead and checks that a fire fits there.</summary>
        private void Aim()
        {
            var forward = _player.Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = _player.transform.forward;
            forward.Normalize();
            var ahead = _player.transform.position + forward * Reach;

            _valid = true;
            _whyNot = "";
            var from = ahead + Vector3.up * 2.5f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponent<CharacterController>() == null)
            {
                _point = hit.point;
                if (hit.collider.GetComponentInParent<ResourceNodeView>() != null) Fail("Ahí hay un árbol o una roca");
                else if (hit.normal.y < 0.85f) Fail("El suelo está muy inclinado");
                else if (hit.point.y < IslandShape.WaterLevel + 0.3f) Fail("No se puede en el agua");
                else if (Blocked(hit.point)) Fail("No cabe ahí");
            }
            else
            {
                _point = new Vector3(ahead.x, _player.transform.position.y, ahead.z);
                Fail("Apunta al suelo");
            }
            _ghost.transform.SetPositionAndRotation(_point, Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f));
        }

        /// <summary>Whether anything solid (a tree, a wall, another fire) stands where the fire would go.</summary>
        private static bool Blocked(Vector3 ground)
        {
            // High enough that the slope of the ground itself doesn't count.
            var centre = ground + Vector3.up * (Radius + 0.45f);
            foreach (var other in Physics.OverlapSphere(centre, Radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (other.GetComponent<CharacterController>() == null) return true;
            return false;
        }

        private void Fail(string why)
        {
            _valid = false;
            _whyNot = why;
        }

        private static Material[] Fill(Material material, int count)
        {
            var array = new Material[count];
            for (int i = 0; i < count; i++) array[i] = material;
            return array;
        }
    }
}
