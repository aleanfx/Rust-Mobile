using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
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

        /// <summary>The island's animals when playing alone (online the server keeps them).</summary>
        public Fauna Fauna { get; set; }

        /// <summary>What a blow at an animal did (its id, the blow, the damage): at once alone, when the server says online.</summary>
        public event System.Action<int, AnimalBlow, float> AnimalHit;

        /// <summary>
        /// A blow at an animal with the tool in hand: a living one is hurt and may die, a carcass is
        /// cut up into the inventory. Online the server judges it and answers.
        /// </summary>
        public void StrikeAnimal(int id, ToolKind tool)
        {
            if (Online)
            {
                _net.SendStrikeAnimal(id, tool);
                return;
            }
            if (Fauna == null || !Fauna.TryGet(id, out var animal) || _survivor.Vitals.IsDead) return;
            var blow = AnimalBlow.None;
            float damage = 0f;
            if (animal.IsDead)
            {
                if (animal.Carcass <= 0) return;
                var yields = Fauna.Harvest(id, tool);
                if (yields.Length == 0) blow = AnimalBlow.NeedsBlade;
                else
                {
                    foreach (var (item, amount) in yields)
                        if (amount > 0) Inventory.Add(item, amount);
                    blow = animal.Carcass > 0 ? AnimalBlow.Cut : AnimalBlow.UsedUp;
                }
            }
            else
            {
                if (!Combat.CanHurt(tool)) return;
                damage = Mathf.Min(Combat.MeleeDamage(tool), animal.Health);
                var p = _player.position;
                blow = Fauna.Hit(id, Combat.MeleeDamage(tool), 0, p.x, p.z) ? AnimalBlow.Killed : AnimalBlow.Hurt;
            }
            AnimalHit?.Invoke(id, blow, damage);
        }

        /// <summary>
        /// Looses an arrow (it is spent at once): from where, which way. False with no bow or no arrow
        /// (online, the server spends it and may still refuse).
        /// </summary>
        public bool Shoot(Vector3 origin, float yaw, float pitch)
        {
            if (_survivor.Vitals.IsDead || !_survivor.HasTool(ToolKind.Bow) || Inventory.Count(ItemIds.Arrow) < 1) return false;
            if (Online)
            {
                _net.SendShoot(origin, yaw, pitch);
                return true;
            }
            Inventory.Remove(ItemIds.Arrow, 1);
            _shots.Enqueue(new Vector3(origin.x, origin.y, origin.z));
            while (_shots.Count > 6) _shots.Dequeue();
            return true;
        }

        private readonly System.Collections.Generic.Queue<Vector3> _shots = new System.Collections.Generic.Queue<Vector3>();

        // ---- Guns

        private readonly System.Collections.Generic.Dictionary<ToolKind, GunState> _guns = new System.Collections.Generic.Dictionary<ToolKind, GunState>();
        private GunSpec _lastGun;
        private Vector3 _lastGunFrom;
        private float _lastGunAt = float.NegativeInfinity;

        /// <summary>This player's gun of this kind: rounds in it, and whether it is being reloaded (on this phone's clock).</summary>
        public GunState GunState(ToolKind tool)
        {
            if (!_guns.TryGetValue(tool, out var state)) _guns[tool] = state = new GunState();
            return state;
        }

        /// <summary>
        /// Fires the gun of this kind from where, which way, with the seed of its spray: carried, a round for
        /// it, loaded and not too fast. The round is spent (online, by the server, which may still refuse).
        /// </summary>
        public bool Fire(ToolKind tool, Vector3 origin, float yaw, float pitch, ushort seed)
        {
            if (!Firearms.TryGet(tool, out var gun) || _survivor.Vitals.IsDead || !_survivor.HasTool(tool) || Inventory.Count(gun.Ammo) < 1) return false;
            var state = GunState(tool);
            if (!state.CanFire(gun, Time.time)) return false;
            state.Fire(gun, Time.time);
            if (Online)
            {
                _net.SendFire(tool, origin, yaw, pitch, seed);
                return true;
            }
            Inventory.Remove(gun.Ammo, 1);
            _lastGun = gun;
            _lastGunFrom = origin;
            _lastGunAt = Time.time;
            return true;
        }

        /// <summary>Some pellets of this player's last shot hit something (a player online, an animal, a sleeper).</summary>
        public void GunHit(ArrowTarget kind, int target, int pellets)
        {
            if (Online)
            {
                _net.SendGunHit(kind, target, pellets);
                return;
            }
            if (_lastGun == null || Time.time - _lastGunAt > 1f) return;
            if (kind != ArrowTarget.Animal || Fauna == null || !Fauna.TryGet(target, out var animal) || animal.IsDead) return;
            float distance = Vector2.Distance(new Vector2(_lastGunFrom.x, _lastGunFrom.z), new Vector2(animal.X, animal.Z));
            float amount = Firearms.Damage(_lastGun, pellets, distance);
            float damage = Mathf.Min(amount, animal.Health);
            bool killed = Fauna.Hit(target, amount, 0, _lastGunFrom.x, _lastGunFrom.z);
            AnimalHit?.Invoke(target, killed ? AnimalBlow.Killed : AnimalBlow.Hurt, damage);
        }

        /// <summary>Reloads the gun of this kind before it is empty. False if it is full or already being reloaded.</summary>
        public bool ReloadGun(ToolKind tool)
        {
            if (!Firearms.TryGet(tool, out var gun) || Inventory.Count(gun.Ammo) < 1 || !GunState(tool).Reload(gun, Time.time)) return false;
            if (Online) _net.SendReload(tool);
            return true;
        }

        // ---- Satchel charges

        private readonly System.Collections.Generic.List<LiveCharge> _charges = new System.Collections.Generic.List<LiveCharge>();
        private int _nextCharge = 1;
        private float _lastThrow = -10f;

        /// <summary>Alone: a charge this player threw is stuck and fizzing (seconds left). Online the server says so.</summary>
        public event System.Action<LiveCharge, float> ChargeArmed;

        /// <summary>Alone: a charge is going off, just before its blast reaches the building.</summary>
        public event System.Action<LiveCharge> ChargeBlast;

        /// <summary>
        /// Throws a charge that sticks at <paramref name="at"/> to <paramref name="target"/>. Online the
        /// server judges it; alone it is checked here, spent, and its fuse lit. False if refused at once.
        /// </summary>
        public bool Throw(ChargeTarget target, Vector3 at, Vector3 normal)
        {
            if (_survivor.Vitals.IsDead || Inventory.Count(ItemIds.SatchelCharge) < 1) return false;
            if (Online)
            {
                _net.SendThrow(target, at, normal);
                return true;
            }
            float now = Time.time;
            if (now - _lastThrow < Explosives.ThrowSeconds * 0.75f) return false;
            if (!Explosives.CanStick(_settlement.Grid, target, at.x, at.y, at.z)) return false;
            Inventory.Remove(ItemIds.SatchelCharge, 1);
            _lastThrow = now;
            var n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            var charge = new LiveCharge(_nextCharge++, 0, at.x, at.y, at.z, n.x, n.y, n.z, target, now + Explosives.FuseSeconds);
            _charges.Add(charge);
            ChargeArmed?.Invoke(charge, Explosives.FuseSeconds);
            return true;
        }

        /// <summary>Alone: burns fuses down; one that reaches its end goes off (shown first, then the building takes the blast).</summary>
        public void TickCharges()
        {
            if (_charges.Count == 0) return;
            if (Online)
            {
                _charges.Clear();
                return;
            }
            float now = Time.time;
            for (int i = 0; i < _charges.Count;)
            {
                var c = _charges[i];
                if (now < c.GoesOffAt)
                {
                    i++;
                    continue;
                }
                _charges.RemoveAt(i);
                ChargeBlast?.Invoke(c);
                Explosives.Blast(_settlement.Grid, c.Target, null);
                Explosives.Splash(_settlement, c.X, c.Y, c.Z, null);
            }
        }

        /// <summary>This player's oldest arrow in the air landed on something (a player online, an animal, or nothing).</summary>
        public void ArrowHit(ArrowTarget kind, int target)
        {
            if (Online)
            {
                _net.SendArrowHit(kind, target);
                return;
            }
            if (_shots.Count == 0) return;
            var from = _shots.Dequeue();
            if (kind != ArrowTarget.Animal || Fauna == null || !Fauna.TryGet(target, out var animal) || animal.IsDead) return;
            float damage = Mathf.Min(Combat.ArrowDamage, animal.Health);
            bool killed = Fauna.Hit(target, Combat.ArrowDamage, 0, from.x, from.z);
            AnimalHit?.Invoke(target, killed ? AnimalBlow.Killed : AnimalBlow.Hurt, damage);
        }

        /// <summary>Online: the server's answer to a blow at an animal.</summary>
        public void OnServerAnimalHit(int id, AnimalBlow blow, int damage) => AnimalHit?.Invoke(id, blow, damage);

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

        public ActionResult Wear(int slot)
        {
            if (!Online) return PlayerActions.Wear(_survivor, slot);
            if (Inventory[slot].IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.Wear, slot);
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
            var p = _player.position;
            if (!Online) return PlayerActions.Craft(_survivor, recipe, _settlement, p.x, p.y, p.z);
            if (recipe.NeedsWorkbench && !PlayerActions.NearWorkbench(_settlement, p.x, p.y, p.z)) return ActionResult.NeedsWorkbench;
            if (_survivor.Crafting.Jobs.Count >= CraftingQueue.MaxJobs) return ActionResult.QueueFull;
            if (!recipe.CanAfford(Inventory)) return ActionResult.NotEnoughResources;
            _net.SendAct(ActKind.Craft, recipe: recipe.Id);
            return ActionResult.Ok;
        }

        public ActionResult Eat(int slot)
        {
            if (!Online) return PlayerActions.Eat(_survivor, slot);
            var stack = Inventory[slot];
            if (stack.IsEmpty || !Inventory.Catalog.TryGet(stack.ItemId, out var def)
                || (def.Category != ItemCategory.Food && def.Category != ItemCategory.Medical))
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
            if (!Online) return PlayerActions.Open(_settlement, _survivor, 0, id, p.x, p.y, p.z);
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

        /// <summary>Everything in the open box that fits into the inventory.</summary>
        public ActionResult TakeAll()
        {
            if (!Online) return PlayerActions.TakeAll(_settlement, _survivor);
            var box = _settlement.Container(_survivor.Looting);
            if (box == null || box.IsEmpty) return ActionResult.Invalid;
            _net.SendAct(ActKind.TakeAll);
            return ActionResult.Ok;
        }

        /// <summary>
        /// The player just died: alone, what they carried goes into a bag where they fell. Online the
        /// server does that itself, and the bag arrives with everything else it changes.
        /// </summary>
        public void DropBag()
        {
            if (Online) return;
            var p = _player.position;
            PlayerActions.DropBag(_settlement, _survivor, 0, p.x, p.y, p.z, _player.eulerAngles.y, out _);
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
                case ActionResult.NeedsFloor: return "Necesita un piso debajo (un techo en el piso de abajo)";
                case ActionResult.TooHigh: return $"Máximo {BuildingGrid.MaxLevel + 1} pisos";
                case ActionResult.NeedsWorkbench: return "Hace falta una mesa de trabajo cerca";
                case ActionResult.Monument: return "Las ruinas son de todos: aquí no se construye";
                case ActionResult.NothingToRecycle: return "Mete algo que se recicle: piezas, herramientas, armas o ropa";
                case ActionResult.Obstructed: return "Hay un árbol o una roca en medio";
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
                case ActionResult.Locked: return "Cerrado con candado";
                case ActionResult.WrongCode: return $"Código incorrecto: descarga de {CodeLocks.WrongCodeShock:0} de vida";
                case ActionResult.NoLock: return "No tiene candado con código";
                case ActionResult.NotDamaged: return "Está entero";
                case ActionResult.RecentlyDamaged: return "Recién dañado: espera unos segundos para repararlo";
                default: return "No se puede";
            }
        }
    }
}
