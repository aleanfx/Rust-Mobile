using System;
using Oxfall.Core.Building;
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
    /// gives back half of what went into it and everything that stood on it.
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

        public bool Active { get; private set; }

        /// <summary>A piece went up a tier.</summary>
        public event Action Upgraded;
        /// <summary>Something was taken down.</summary>
        public event Action Demolished;

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
            if (_grid.Upgrade(_aimed, next) != PlaceResult.Ok) return;
            BuildCosts.TryPay(_inventory, cost);
            _hud.ShowToast($"{BuildSystem.Name(_aimed.Type)} de {BuildingTiers.Name(next).ToLowerInvariant()}");
            Debug.Log($"[Oxfall] Upgraded {_aimed} to {next}");
            Upgraded?.Invoke();
        }

        /// <summary>The first tap asks; a second tap on the same piece within three seconds takes it down.</summary>
        public void Demolish()
        {
            if (!Active) return;
            Aim();
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
            int pieces = 0;
            void Refund(PieceRef piece, BuildingTier tier)
            {
                pieces++;
                foreach (var item in BuildCosts.Refund(piece.Type, tier)) Give(item.ItemId, item.Amount);
            }
            void ReturnDoor(Edge edge) => Give(ItemIds.WoodenDoor, 1);
            _grid.PieceRemoved += Refund;
            _grid.DoorRemoved += ReturnDoor;
            _grid.Remove(_aimed);
            _grid.PieceRemoved -= Refund;
            _grid.DoorRemoved -= ReturnDoor;
            _hasAim = false;
            _info = null;
            _hud.ShowToast(pieces > 1 ? $"Demolido: {pieces} piezas (recuperas la mitad)" : "Demolido (recuperas la mitad)");
            Debug.Log($"[Oxfall] Demolished {pieces} pieces");
            Demolished?.Invoke();
        }

        private void Give(string itemId, int amount)
        {
            if (_inventory.Add(itemId, amount) > 0) _hud.ShowToast("Inventario lleno: parte se perdió");
        }

        /// <summary>Finds the piece in the crosshair and shows what the hammer would do to it.</summary>
        private void Aim()
        {
            var ray = new Ray(_head.position, _head.forward);
            bool found = false;
            PieceRef piece = default;
            if (Physics.Raycast(ray, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var tag = hit.collider.GetComponent<PieceTag>();
                if (tag != null && _grid.Exists(tag.Piece))
                {
                    found = true;
                    piece = tag.Piece;
                }
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
