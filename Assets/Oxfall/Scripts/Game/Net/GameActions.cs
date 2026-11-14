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

        /// <summary>What marks this player's things: the server's number online, 0 alone.</summary>
        public int Owner => Online ? _net.Owner : 0;

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

        /// <summary>
        /// Wakes up in one of the player's sleeping bags. Alone, <paramref name="bag"/> is where to put
        /// them; online the server moves them there itself.
        /// </summary>
        public ActionResult RespawnInBag(int bagId, out Deployable bag)
        {
            var r = PlayerActions.CanUseBag(_settlement, Owner, bagId, Time.time, out bag);
            if (r != ActionResult.Ok) return r;
            if (!Online) return PlayerActions.RespawnInBag(_settlement, _survivor, 0, bagId, Time.time, out bag);
            // The server keeps its own clock for bags; this copy keeps one for showing the wait.
            _settlement.UseBag(bagId, Time.time);
            _net.SendAct(ActKind.Respawn, bagId);
            return ActionResult.Ok;
        }

        // ---- Boxes and furnaces

        /// <summary>The box or furnace open now (0: none).</summary>
        public int Looting => _survivor.Looting;

        public ActionResult Open(int id)
        {
            var p = _player.position;
            if (!Online) return PlayerActions.Open(_settlement, _survivor, id, p.x, p.y, p.z);
            if (_settlement.Container(id) == null) return ActionResult.NoPiece;
            // The copy opens at once; what is inside arrives from the server a moment later.
            _survivor.Looting = id;
            _net.SendAct(ActKind.Open, id);
            return ActionResult.Ok;
        }

        public void Close()
        {
            if (_survivor.Looting == 0) return;
            PlayerActions.Close(_survivor);
            if (Online) _net.SendAct(ActKind.Close);
        }

        /// <summary>From the open box's slot into an inventory slot (-1: wherever it fits).</summary>
        public ActionResult Take(int from, int to)
        {
            if (!Online) return PlayerActions.Take(_settlement, _survivor, from, to);
            var box = _settlement.Container(_survivor.Looting);
            if (box == null || from < 0 || from >= box.SlotCount || box[from].IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.Take, from, to);
            return ActionResult.Ok;
        }

        /// <summary>From an inventory slot into the open box (-1: wherever it fits).</summary>
        public ActionResult Put(int from, int to)
        {
            if (!Online) return PlayerActions.Put(_settlement, _survivor, from, to);
            if (_survivor.Looting == 0 || Inventory[from].IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.Put, from, to);
            return ActionResult.Ok;
        }

        public ActionResult MoveInBox(int from, int to)
        {
            if (!Online) return PlayerActions.MoveInBox(_settlement, _survivor, from, to);
            if (_survivor.Looting == 0 || from == to) return ActionResult.Invalid;
            _net.SendAct(ActKind.MoveInBox, from, to);
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
                case ActionResult.BuildingBlocked: return "Bloqueado: hay un armario de otro jugador cerca";
                case ActionResult.NotEmpty: return "Vacíalo (y apágalo) antes de recogerlo";
                case ActionResult.NoFuel: return "Necesita madera para arder";
                case ActionResult.NotReady: return "El saco aún no está listo";
                case ActionResult.TooClose: return "Está demasiado cerca de otra cosa";
                case ActionResult.NotOpen: return "Ábrelo primero";
                default: return "No se puede";
            }
        }
    }
}
