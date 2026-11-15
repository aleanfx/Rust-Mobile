using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Controls;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// A satchel charge in hand: aim at a wall, a door or the ground within a throw, and attack throws
    /// it there, where it sticks. Up close to a base, the aim shows what it is and how many charges
    /// it would take to bring down.
    /// </summary>
    public sealed class ChargeThrower : MonoBehaviour
    {
        private BuildingGrid _grid;
        private PlayerInput _input;
        private Transform _head;
        private Hud _hud;
        private Inventory _inventory;
        private Explosions _explosions;
        private string _info;
        private float _cooldown;
        private DeployableTag _aimedAt;

        public bool Active { get; private set; }

        public GameActions Actions { get; set; }

        /// <summary>For what is set down (a box in the crosshair shows how many charges it takes too).</summary>
        public Settlement Settlement { get; set; }

        /// <summary>A charge left the hand (for the swing of the arm and its sound).</summary>
        public event System.Action Thrown;

        public void Init(BuildingGrid grid, PlayerInput input, Transform head, Hud hud, Inventory inventory, Explosions explosions)
        {
            _grid = grid;
            _input = input;
            _head = head;
            _hud = hud;
            _inventory = inventory;
            _explosions = explosions;
        }

        /// <summary>What is thrown: a satchel charge or a beancan grenade.</summary>
        public ChargeKind Kind { get; private set; }

        public void SetActive(bool on, ChargeKind kind = ChargeKind.Satchel)
        {
            if (on == Active && (!on || kind == Kind)) return;
            Active = on;
            Kind = kind;
            _info = null;
            _hud.SetAimInfo(null);
            if (!on) return;
            _hud.ShowToast(kind == ChargeKind.Beancan
                ? "Apunta y toca LANZAR: la granada vuela hasta 14 m y explota a los 3,5 segundos"
                : "Apunta a una pared o una puerta cercana y toca LANZAR: explota a los 5 segundos. ¡Aléjate!");
        }

        private void Update()
        {
            if (!Active) return;
            _cooldown -= Time.deltaTime;
            if (_input.AttackPressed) Throw();
            ShowAim();
        }

        /// <summary>Throws a charge at what is in the crosshair. For tests too.</summary>
        public void Throw()
        {
            if (!Active || _cooldown > 0f) return;
            if (_inventory.Count(Explosives.ItemFor(Kind)) < 1)
            {
                _hud.ShowToast(Kind == ChargeKind.Beancan ? "No te quedan granadas" : "No te quedan cargas");
                return;
            }
            if (!Aim(out var target, out var point, out var normal))
            {
                _hud.ShowToast(Kind == ChargeKind.Beancan ? "Ahí no cae: apunta al suelo o a una pared" : "Muy lejos: acércate a menos de 5 m");
                return;
            }
            var from = _head.position + _head.right * 0.25f - _head.up * 0.2f + _head.forward * 0.3f;
            if (!Actions.Throw(target, point, normal, Kind))
            {
                _hud.ShowToast("Ahí no se pega");
                return;
            }
            _cooldown = Explosives.ThrowSeconds;
            _explosions.Throw(from, point, normal, Kind);
            Thrown?.Invoke();
            Debug.Log($"[Oxfall] Threw a charge at {target} ({point.x:0.0}, {point.y:0.0}, {point.z:0.0})");
        }

        /// <summary>What a throw now would stick to: a piece, a door, or the ground, and where, within reach.</summary>
        private bool Aim(out ChargeTarget target, out Vector3 point, out Vector3 normal)
        {
            target = ChargeTarget.Ground;
            point = default;
            normal = Vector3.up;
            _aimedAt = null;
            var ray = new Ray(_head.position, _head.forward);
            float reach = Explosives.ReachFor(Kind);
            if (!Physics.Raycast(ray, out var hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // A grenade thrown at the open: it lands on the ground below the end of its throw.
                if (Kind != ChargeKind.Beancan) return false;
                var end = ray.GetPoint(reach - 0.5f);
                if (!Physics.Raycast(end + Vector3.up * 2f, Vector3.down, out hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false;
                if (Vector3.Distance(_head.position, hit.point) > reach) return false;
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = hit.point;
            normal = hit.normal;
            _aimedAt = hit.collider.GetComponent<DeployableTag>();
            var tag = hit.collider.GetComponent<PieceTag>();
            if (tag != null)
            {
                if (tag.IsDoor && tag.Piece.IsEdge && _grid.HasDoor(tag.Piece.Edge)) target = ChargeTarget.OnDoor(tag.Piece.Edge);
                else if (_grid.Exists(tag.Piece)) target = ChargeTarget.OnPiece(tag.Piece);
            }
            return true;
        }

        /// <summary>Up close to a base: what is in the crosshair and how many charges would bring it down.</summary>
        private void ShowAim()
        {
            string info = "";
            if (Kind == ChargeKind.Satchel && Aim(out var target, out _, out _))
            {
                if (target.Surface == ChargeSurface.Door)
                {
                    var edge = target.Piece.Edge;
                    var kind = _grid.DoorKindOf(edge);
                    info = $"{DoorKinds.Name(kind)}  ·  {Charges(_grid.DoorHealth(edge))}";
                }
                else if (target.Surface == ChargeSurface.Piece && _grid.TryGetTier(target.Piece, out var tier))
                {
                    string name = $"{BuildSystem.Name(target.Piece.Type)} de {BuildingTiers.Name(tier).ToLowerInvariant()}";
                    info = $"{name}  ·  {Charges(_grid.Health(target.Piece))}";
                }
                else if (_aimedAt != null && Settlement != null && Deployables.MaxHealth(_aimedAt.Kind) > 0f && Settlement.TryGet(_aimedAt.Id, out _))
                {
                    float bite = HighWalls.IsWall(_aimedAt.Kind) ? Explosives.BlastDamage : Explosives.DeployableDamage;
                    int n = Mathf.CeilToInt(Settlement.DeployableHealth(_aimedAt.Id) / bite - 1e-4f);
                    info = $"{Deployables.Name(_aimedAt.Kind)}  ·  {(n == 1 ? "cae con 1 carga" : $"cae con {n} cargas")}";
                }
            }
            if (info == _info) return;
            _info = info;
            _hud.SetAimInfo(info.Length > 0 ? info : null);
        }

        private static string Charges(float health)
        {
            int n = Explosives.ChargesFor(health);
            return n == 1 ? "cae con 1 carga" : $"cae con {n} cargas";
        }
    }
}
