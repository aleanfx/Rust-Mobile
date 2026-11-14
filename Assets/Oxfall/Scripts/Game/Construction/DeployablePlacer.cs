using Oxfall.Core.Rules;
using Oxfall.Game.Controls;
using Oxfall.Game.Net;
using Oxfall.Game.Player;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// While a campfire, box, furnace, sleeping bag or tool cupboard is in hand: a ghost of it on the
    /// ground or floor ahead, green where it can go, and attack (COLOCAR) sets it down. Boxes,
    /// furnaces and cupboards face the player; a fire or a bag lies the way the player looks.
    /// </summary>
    public sealed class DeployablePlacer : MonoBehaviour
    {
        /// <summary>How far ahead of the player it goes, in metres.</summary>
        public float Reach = 2.4f;

        private PlayerInput _input;
        private PlayerController _player;
        private MaterialLibrary _materials;
        private Hud _hud;
        private Settlement _settlement;
        private DeployableViews _views;

        private GameObject _ghost;
        private MeshFilter _ghostFilter;
        private MeshRenderer _ghostRenderer;
        private Material[] _ghostOk;
        private Material[] _ghostBad;
        private bool _valid;
        private string _whyNot = "";
        private Vector3 _point;
        private float _yaw;

        /// <summary>This player set something down (the knock of placing it).</summary>
        public event System.Action Placed;

        /// <summary>Where placing goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public bool Active { get; private set; }
        public DeployableKind Kind { get; private set; }

        public void Init(PlayerInput input, PlayerController player, MaterialLibrary materials, Hud hud, Settlement settlement,
            DeployableViews views)
        {
            _input = input;
            _player = player;
            _materials = materials;
            _hud = hud;
            _settlement = settlement;
            _views = views;
            _ghost = new GameObject("Deployable ghost");
            _ghostFilter = _ghost.AddComponent<MeshFilter>();
            _ghostRenderer = _ghost.AddComponent<MeshRenderer>();
            _ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ghostRenderer.receiveShadows = false;
            _ghost.SetActive(false);
        }

        /// <summary>Holding one of these (or nothing to place: null).</summary>
        public void Hold(DeployableKind? kind)
        {
            if (kind == null)
            {
                if (!Active) return;
                Active = false;
                _ghost.SetActive(false);
                _hud.SetBuildMode(false, null);
                return;
            }
            bool changed = !Active || Kind != kind.Value;
            Kind = kind.Value;
            Active = true;
            if (!changed) return;
            var model = _views.ModelOf(Kind);
            _ghostFilter.sharedMesh = model.Mesh;
            _ghostOk = Fill(_materials.GhostOk, model.Materials.Length);
            _ghostBad = Fill(_materials.GhostBad, model.Materials.Length);
            _ghost.SetActive(true);
            _hud.SetBuildMode(true, null);
            _hud.ShowToast(Kind == DeployableKind.ToolCupboard
                ? "Ponlo en el centro de tu casa y toca COLOCAR"
                : "Apunta al suelo y toca COLOCAR");
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
            var result = Actions.Build(BuildOp.Deploy(new Deployable(0, Kind, _point.x, _point.y, _point.z, _yaw)));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            Placed?.Invoke();
            _hud.ShowToast(Done(Kind));
            Debug.Log($"[Oxfall] Placed {Kind} at {_point}");
        }

        private static string Done(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Campfire: return "Fogata encendida";
                case DeployableKind.StorageBox: return "Caja colocada: tócala con ABRIR";
                case DeployableKind.Furnace: return "Horno colocado: ábrelo, mete madera y mineral";
                case DeployableKind.SleepingBag: return "Saco colocado: al morir podrás despertar aquí";
                case DeployableKind.ToolCupboard: return "Armario colocado: solo los autorizados construyen cerca";
                default: return "Colocado";
            }
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

        /// <summary>Finds the ground (or a floor) ahead and checks that it fits there, by the same rules the server uses.</summary>
        private void Aim()
        {
            var forward = _player.Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = _player.transform.forward;
            forward.Normalize();
            float reach = Kind == DeployableKind.SleepingBag ? Reach + 0.4f : Reach;
            var ahead = _player.transform.position + forward * reach;
            float lookYaw = _player.transform.eulerAngles.y;
            _yaw = Kind == DeployableKind.Campfire || Kind == DeployableKind.SleepingBag ? lookYaw : lookYaw + 180f;

            _valid = true;
            _whyNot = "";
            var from = ahead + Vector3.up * 2.5f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponent<CharacterController>() == null)
            {
                _point = hit.point;
                if (hit.collider.GetComponentInParent<ResourceNodeView>() != null) Fail("Ahí hay un árbol o una roca");
                else if (hit.collider.GetComponent<DeployableTag>() != null) Fail("No cabe ahí");
                else if (hit.normal.y < 0.85f) Fail("El suelo está muy inclinado");
                else if (Blocked(hit.point, Deployables.Radius(Kind))) Fail("No cabe ahí");
                else
                {
                    var rules = PlayerActions.CanDeploy(_settlement, Actions.Owner, Kind, _point.x, _point.y, _point.z);
                    if (rules != ActionResult.Ok) Fail(GameActions.Explain(rules));
                }
            }
            else
            {
                _point = new Vector3(ahead.x, _player.transform.position.y, ahead.z);
                Fail("Apunta al suelo");
            }
            _ghost.transform.SetPositionAndRotation(_point, Quaternion.Euler(0f, _yaw, 0f));
        }

        /// <summary>Whether anything solid (a tree, a wall, something else set down) stands where it would go.</summary>
        private static bool Blocked(Vector3 ground, float radius)
        {
            // High enough that the slope of the ground itself doesn't count.
            var centre = ground + Vector3.up * (radius + 0.45f);
            foreach (var other in Physics.OverlapSphere(centre, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
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
