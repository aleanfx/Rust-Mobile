using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using UnityEngine;

namespace Oxfall.Game.Net
{
    /// <summary>
    /// Every change the player makes goes through here. Alone, the shared rules
    /// (<see cref="PlayerActions"/>) run on the phone's own survivor and settlement. Online, the
    /// request goes to the server, which runs the same rules and sends back the change; the phone's
    /// copies are only updated from what the server says. A quick check on the copy first answers
    /// the obvious refusals (no wood, queue full) at once, as alone.
    /// </summary>
    public sealed class GameActions
    {
        private readonly Survivor _survivor;
        private readonly Settlement _settlement;
        private readonly Transform _player;
        private readonly NetClient _net;

        public GameActions(Survivor survivor, Settlement settlement, Transform player, NetClient net)
        {
            _survivor = survivor;
            _settlement = settlement;
            _player = player;
            _net = net;
        }

        /// <summary>Online: changes take effect when the server says so, a moment later.</summary>
        public bool Online => _net != null && _net.State == NetClient.Status.Online;

        private Inventory Inventory => _survivor.Inventory;

        public ActionResult MoveSlot(int from, int to)
        {
            if (!Online) return PlayerActions.MoveSlot(_survivor, from, to);
            if (from == to || Inventory[from].IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.MoveSlot, from, to);
            return ActionResult.Ok;
        }

        public ActionResult QuickMove(int slot)
        {
            if (!Online) return PlayerActions.QuickMove(_survivor, slot);
            if (Inventory[slot].IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.QuickMove, slot);
            return ActionResult.Ok;
        }

        public ActionResult Split(int slot)
        {
            if (!Online) return PlayerActions.Split(_survivor, slot);
            if (Inventory[slot].Amount < 2) return ActionResult.Invalid;
            _net.SendAct(ActKind.Split, slot);
            return ActionResult.Ok;
        }

        public ActionResult Craft(Recipe recipe)
        {
            if (!Online) return PlayerActions.Craft(_survivor, recipe);
            if (_survivor.Crafting.Jobs.Count >= CraftingQueue.MaxJobs) return ActionResult.QueueFull;
            if (!recipe.CanAfford(Inventory)) return ActionResult.NotEnoughResources;
            _net.SendAct(ActKind.Craft, recipe: recipe.Id);
            return ActionResult.Ok;
        }

        public ActionResult Eat(int slot)
        {
            if (!Online) return PlayerActions.Eat(_survivor, slot);
            var stack = Inventory[slot];
            if (stack.IsEmpty || !Inventory.Catalog.TryGet(stack.ItemId, out var def) || def.Category != ItemCategory.Food)
                return ActionResult.NotFood;
            _net.SendAct(ActKind.Eat, slot);
            return ActionResult.Ok;
        }

        public ActionResult Drink()
        {
            var p = _player.position;
            if (!Online) return PlayerActions.Drink(_survivor, _settlement.Ground, p.x, p.y, p.z);
            _net.SendAct(ActKind.Drink);
            return ActionResult.Ok;
        }

        /// <summary>Builds, hammers, hangs or opens a door, or lights a campfire.</summary>
        public ActionResult Build(BuildOp op)
        {
            if (Online)
            {
                _net.SendBuild(op);
                return ActionResult.Ok;
            }
            var p = _player.position;
            // Alone, everything is the player's: owner 0.
            return PlayerActions.Build(_settlement, _survivor, 0, op, p.x, p.y, p.z, out _);
        }

        /// <summary>Wakes up after dying. Alone, the caller puts the player on the beach; online, the server does.</summary>
        public ActionResult Respawn()
        {
            if (!Online) return PlayerActions.Respawn(_survivor);
            _net.SendAct(ActKind.Respawn);
            return ActionResult.Ok;
        }

        /// <summary>What to tell the player when something did not happen.</summary>
        public static string Explain(ActionResult result)
        {
            switch (result)
            {
                case ActionResult.Dead: return "Estás muerto";
                case ActionResult.NotEnoughResources: return "Faltan materiales";
                case ActionResult.InventoryFull: return "Inventario lleno: parte se perdió";
                case ActionResult.QueueFull: return "La cola de fabricación está llena";
                case ActionResult.TooFar: return "Está demasiado lejos";
                case ActionResult.Occupied: return "Ahí ya hay algo";
                case ActionResult.NeedsFoundation: return "Necesita un cimiento debajo";
                case ActionResult.NeedsWall: return "El techo necesita una pared";
                case ActionResult.NeedsDoorway: return "Apunta a un marco de puerta vacío";
                case ActionResult.NoPiece: return "Ya no está ahí";
                case ActionResult.MaxTier: return "Ya está al nivel máximo";
                case ActionResult.InWater: return "No se puede construir en el agua";
                case ActionResult.TooSteep: return "El terreno es demasiado inclinado";
                case ActionResult.GroundInTheWay: return "El terreno tapa el cimiento";
                case ActionResult.NotFood: return "Eso no se come";
                case ActionResult.NoWater: return "Aquí no hay agua dulce";
                case ActionResult.NoDoor: return "Ahí no hay puerta";
                case ActionResult.NotYours: return "No es tuyo: solo quien lo construyó puede tocarlo";
                case ActionResult.NoSpace: return "No hay casillas libres";
                default: return "No se puede";
            }
        }
    }
}
