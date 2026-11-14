using System;
using Oxfall.Core.Building;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Core.Items;
using Oxfall.Game.Controls;
using Oxfall.Game.Player;
using Oxfall.Game.UI;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// The hammer in hand: aim at something built and attack (MEJORAR) raises it a tier, twig to
    /// wood to stone, for its cost; the button above attack (DEMOLER, tapped twice) takes it down and
    /// gives back half of what went into it and everything that stood on it. Aimed at something set
    /// down (a box, a furnace), the same button (RECOGER) picks it back up.
    /// </summary>
    public sealed class HammerTool : MonoBehaviour
    {
        public float Reach = 5f;
        private const float ConfirmSeconds = 3f;

        private BuildingGrid _grid;
        private PlayerInput _input;
        private Inventory _inventory;
        private ItemCatalog _catalog;
        private Transform _head;
        private Hud _hud;

        private PieceRef _aimed;
        private bool _hasAim;
        private string _info;
        private float _confirmUntil = -1f;
        private PieceRef _confirming;
        private int _deployable;

        /// <summary>What is set down, to pick it back up.</summary>
        public Settlement Settlement { get; set; }

        public bool Active { get; private set; }

        /// <summary>A piece went up a tier.</summary>
        public event Action Upgraded;
        /// <summary>Something was taken down.</summary>
        public event Action Demolished;

        /// <summary>Where hammering goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public void Init(BuildingGrid grid, PlayerInput input, Inventory inventory, ItemCatalog catalog, Transform head, Hud hud)
        {
            _grid = grid;
            _input = input;
            _inventory = inventory;
            _catalog = catalog;
            _head = head;
            _hud = hud;
            _grid.PieceUpgraded += (p, t) => _info = null;
        }

        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            _hasAim = false;
            _info = null;
            _confirmUntil = -1f;
            _hud.SetPieceButton(null);
            _hud.SetAimInfo(null);
            if (!on) _hud.SetPlaceValid(false);
            else _hud.ShowToast("Apunta a lo que construiste: MEJORAR sube su nivel");
        }

        private void Update()
        {
            if (!Active) return;
            if (_input.AttackPressed) Upgrade();
            Aim();
        }

        /// <summary>Upgrades what is aimed at to the next tier, if it can be paid for.</summary>
        public void Upgrade()
        {
            if (!Active) return;
            Aim();
            if (_deployable != 0)
            {
                _hud.ShowToast("Esto no se mejora: RECOGER lo devuelve al inventario");
                return;
            }
            if (!_hasAim)
            {
                _hud.ShowToast("Apunta a algo que construiste");
                return;
            }
            _grid.TryGetTier(_aimed, out var tier);
            if (!BuildingTiers.TryNext(tier, out var next))
            {
                _hud.ShowToast("Ya está al nivel máximo");
                return;
            }
            var cost = BuildCosts.Upgrade(_aimed.Type, next);
            if (!BuildCosts.CanAfford(_inventory, cost))
            {
                _hud.ShowToast("Faltan materiales: " + BuildCosts.Describe(cost, _catalog));
                return;
            }
            var result = Actions.Build(BuildOp.Upgrade(_aimed, next));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            _hud.ShowToast($"{BuildSystem.Name(_aimed.Type)} de {BuildingTiers.Name(next).ToLowerInvariant()}");
            Debug.Log($"[Oxfall] Upgraded {_aimed} to {next}");
            Upgraded?.Invoke();
        }

        /// <summary>The first tap asks; a second tap on the same piece within three seconds takes it down.</summary>
        public void Demolish()
        {
            if (!Active) return;
            Aim();
            if (_deployable != 0)
            {
                PickUp();
                return;
            }
            if (!_hasAim) return;
            if (Time.time > _confirmUntil || !_confirming.Equals(_aimed))
            {
                _confirming = _aimed;
                _confirmUntil = Time.time + ConfirmSeconds;
                string what = BuildSystem.Name(_aimed.Type).ToLowerInvariant();
                _hud.ShowToast(_aimed.Type == PieceType.Foundation
                    ? $"Toca DEMOLER otra vez: el {what} cae con todo lo de encima"
                    : $"Toca DEMOLER otra vez para tirar: {what}");
                return;
            }
            _confirmUntil = -1f;
            // Counted as they fall: alone at once, online when the server's change arrives.
            int before = _grid.PieceCount;
            var result = Actions.Build(BuildOp.Remove(_aimed));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            int pieces = before - _grid.PieceCount;
            _hasAim = false;
            _info = null;
            _hud.ShowToast(pieces > 1 ? $"Demolido: {pieces} piezas (recuperas la mitad)" : "Demolido (recuperas la mitad)");
            Debug.Log($"[Oxfall] Demolished {_aimed} ({pieces} pieces here)");
            Demolished?.Invoke();
        }

        /// <summary>Picks up what is set down in the crosshair (empty, and out, first).</summary>
        private void PickUp()
        {
            if (Settlement == null || !Settlement.TryGet(_deployable, out var d)) return;
            var result = Actions.Build(BuildOp.Pickup(d.Id));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            _hud.ShowToast("Recogido: " + Deployables.Name(d.Kind).ToLowerInvariant());
            Debug.Log($"[Oxfall] Picked up {d}");
            _deployable = 0;
            _info = null;
            Demolished?.Invoke();
        }

        /// <summary>Finds the piece in the crosshair and shows what the hammer would do to it.</summary>
        private void Aim()
        {
            var ray = new Ray(_head.position, _head.forward);
            bool found = false;
            PieceRef piece = default;
            int deployable = 0;
            if (Physics.Raycast(ray, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var tag = hit.collider.GetComponent<PieceTag>();
                if (tag != null && _grid.Exists(tag.Piece))
                {
                    found = true;
                    piece = tag.Piece;
                }
                var placed = hit.collider.GetComponent<DeployableTag>();
                if (placed != null && Settlement != null && Settlement.TryGet(placed.Id, out _)) deployable = placed.Id;
            }

            if (deployable != _deployable) _info = null;
            _deployable = deployable;
            if (deployable != 0)
            {
                _hasAim = false;
                if (_info != null) return;
                Settlement.TryGet(deployable, out var d);
                _info = Deployables.Name(d.Kind) + "  ·  RECOGER lo devuelve al inventario";
                _hud.SetAimInfo(_info);
                _hud.SetPieceButton("RECOGER");
                _hud.SetPlaceValid(false);
                return;
            }

            if (found != _hasAim || (found && !piece.Equals(_aimed))) _info = null;
            _hasAim = found;
            _aimed = piece;
            if (_info != null) return;

            if (!found)
            {
                _info = "";
                _hud.SetAimInfo(null);
                _hud.SetPieceButton(null);
                _hud.SetPlaceValid(false);
                return;
            }

            _grid.TryGetTier(piece, out var tier);
            string name = $"{BuildSystem.Name(piece.Type)} de {BuildingTiers.Name(tier).ToLowerInvariant()}";
            bool canPay = false;
            if (BuildingTiers.TryNext(tier, out var next))
            {
                var cost = BuildCosts.Upgrade(piece.Type, next);
                canPay = BuildCosts.CanAfford(_inventory, cost);
                _info = $"{name}  ·  a {BuildingTiers.Name(next).ToLowerInvariant()}: {BuildCosts.Describe(cost, _catalog)}";
            }
            else _info = name + "  ·  nivel máximo";
            _hud.SetAimInfo(_info);
            _hud.SetPieceButton("DEMOLER");
            _hud.SetPlaceValid(canPay);
        }

        /// <summary>Forget the shown text, so the next frame redraws it (after the inventory changes).</summary>
        public void Refresh() => _info = null;
    }
}
