using System;
using Oxfall.Core.Building;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Whatever is in hand: looking up close at something that can be used shows the use button.
    /// A door: ABRIR or CERRAR. A box or a furnace: ABRIR (its panel opens beside the inventory). A
    /// tool cupboard: ARMARIO (who may build here). A supply crate in the ruins: ABRIR (what it held
    /// goes into the inventory). A workbench: MESA (the crafting panel). Behind a code lock the player does not know, the
    /// button says CÓDIGO and opens its keypad instead; on a lock the player knows, a second, smaller
    /// button (CANDADO) opens it to lock, unlock, change the code or take it off.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        public float Reach = 3.2f;

        private enum Target
        {
            None,
            Door,
            Box,
            Cupboard,
            Crate,
            Workbench,
            Player,
            Boat,
            Crop,
            /// <summary>Something electric: a switch, a battery, a panel, a lamp; or any of them (generator and turret too) with the cable in hand.</summary>
            Electric,
        }

        private BuildingGrid _grid;
        private Settlement _settlement;
        private Transform _head;
        private Hud _hud;
        private Target _target;
        private Edge _door;
        private int _deployable;
        private ResourceNodeView _crate;
        private ushort _player;
        private int _boat;

        /// <summary>The empty boat in reach the player looks towards (0: none).</summary>
        public Func<int> BoatInSight { get; set; }

        /// <summary>Whether the player sits in a boat (the use button gets them out).</summary>
        public Func<bool> Rowing { get; set; }

        /// <summary>The player gets into a boat (its id).</summary>
        public event Action<int> Board;

        /// <summary>The player gets out of their boat.</summary>
        public event Action LeaveBoat;

        /// <summary>Whether this player could be asked into a team (online, not already a teammate, awake).</summary>
        public Func<ushort, bool> CanInvite { get; set; }

        /// <summary>The player asked another (by player id) into their team.</summary>
        public event Action<ushort> Invite;
        private bool _hasLock;
        private LockTarget _lock;
        private float _timer;

        /// <summary>A door opened or closed.</summary>
        public event Action DoorUsed;

        /// <summary>The player opens a box or furnace (its id).</summary>
        public event Action<int> OpenBox;

        /// <summary>The player opens a tool cupboard (its id).</summary>
        public event Action<int> OpenCupboard;

        /// <summary>The player opens a supply crate.</summary>
        public event Action<ResourceNodeView> OpenCrate;

        /// <summary>The player harvested a crop (what it was, how far along).</summary>
        public event Action<DeployableKind, CropStage> Harvested;

        /// <summary>The player taps MESA at a workbench.</summary>
        public event Action UseWorkbench;

        /// <summary>The player taps CÓDIGO on a lock they do not know.</summary>
        public event Action<LockTarget> EnterCode;

        /// <summary>The player taps CANDADO on a lock they know (or just put on).</summary>
        public event Action<LockTarget> ManageLock;

        /// <summary>Where opening goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        /// <summary>The item in hand (null: nothing), to know when the cable is held.</summary>
        public Func<string> HeldItem { get; set; }

        /// <summary>The device a cable is being run from (0: none).</summary>
        public int PendingSource { get; private set; }

        /// <summary>A cable was started from a device, or let go (0).</summary>
        public event Action<int> PendingChanged;

        private bool HoldingWire => HeldItem != null && HeldItem() == ItemIds.WireTool;

        private bool _infoShown;

        public bool LookingAtDoor => _target == Target.Door;

        /// <summary>The lock on what is in sight, if there is one.</summary>
        public bool LookingAtLock(out LockTarget target)
        {
            target = _lock;
            return _target != Target.None && _hasLock;
        }

        private int Me => Actions != null ? Actions.Owner : 0;

        /// <summary>True while a panel is open over the world: the buttons stay out of its way.</summary>
        public Func<bool> Busy;

        /// <summary>A lock in sight that keeps this player out.</summary>
        private bool Barred => _hasLock && !_settlement.CanPass(_lock, Me);

        /// <summary>The box, furnace or cupboard in sight (0: none).</summary>
        public int LookingAt => _target == Target.Box || _target == Target.Cupboard ? _deployable : 0;

        public void Init(Settlement settlement, Transform head, Hud hud)
        {
            _settlement = settlement;
            _grid = settlement.Grid;
            _head = head;
            _hud = hud;
            _grid.DoorToggled += (edge, open) => _timer = 0f;
            _grid.DoorRemoved += edge => _timer = 0f;
            settlement.DeployableRemoved += id => _timer = 0f;
            settlement.LockChanged += target => _timer = 0f;
        }

        private void Update()
        {
            if (_grid == null) return;
            // Ten looks a second are plenty for a button.
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.1f;
            if (Busy != null && Busy())
            {
                _hud.SetLockButton(false, false);
                _hud.SetUseButton(null);
                return;
            }
            if (Rowing != null && Rowing())
            {
                _hud.SetLockButton(false, false);
                _hud.SetUseButton("BAJAR", Icons.Boat);
                return;
            }
            Look();
            if (PendingSource != 0 && (!HoldingWire || !_settlement.TryGet(PendingSource, out _))) SetPending(0);
            ShowPowerInfo();
            CodeLockState state = null;
            bool manage = _target != Target.None && _hasLock && _settlement.TryGetLock(_lock, out state) && state.CanManage(Me);
            _hud.SetLockButton(manage, manage && state.Locked);
            if (Barred)
            {
                _hud.SetUseButton("CÓDIGO", Icons.Keypad);
                return;
            }
            switch (_target)
            {
                case Target.Door:
                    _hud.SetUseButton(_grid.IsDoorOpen(_door) ? "CERRAR" : "ABRIR");
                    break;
                case Target.Box:
                    _settlement.TryGet(_deployable, out var d);
                    if (d.Kind == DeployableKind.Bag) _hud.SetUseButton("BOLSA", Icons.Bag);
                    else if (d.Kind == DeployableKind.SupplyDrop) _hud.SetUseButton("SUMINISTROS", Icons.Box);
                    else if (d.Kind == DeployableKind.LockedCrate)
                    {
                        if (_settlement.IsCrateOpen(d.Id)) _hud.SetUseButton("CAJA MILITAR", Icons.Box);
                        else if (_settlement.IsHacked(d.Id)) _hud.SetUseButton("HACKEO " + LockedCrates.Clock(_settlement.HackLeft(d.Id)), Icons.Keypad);
                        else _hud.SetUseButton("HACKEAR", Icons.Keypad);
                    }
                    else if (d.Kind == DeployableKind.Furnace) _hud.SetUseButton("HORNO", Icons.Flame);
                    else if (d.Kind == DeployableKind.Campfire) _hud.SetUseButton("FOGATA", Icons.Flame);
                    else if (d.Kind == DeployableKind.Recycler) _hud.SetUseButton("RECICLAR", Icons.Recycle);
                    else if (d.Kind == DeployableKind.ShotgunTrap) _hud.SetUseButton("TRAMPA", Icons.Box);
                    else if (d.Kind == DeployableKind.AutoTurret) _hud.SetUseButton("TORRETA", Icons.Box);
                    else if (d.Kind == DeployableKind.VendingMachine) _hud.SetUseButton("TIENDA", Icons.Shop);
                    else _hud.SetUseButton("ABRIR", Icons.Box);
                    break;
                case Target.Cupboard:
                    _hud.SetUseButton("ARMARIO", Icons.Shield);
                    break;
                case Target.Crate:
                    _hud.SetUseButton("ABRIR", Icons.Box);
                    break;
                case Target.Workbench:
                    _hud.SetUseButton("MESA", Icons.Hammer);
                    break;
                case Target.Crop:
                {
                    var stage = _settlement.CropStageOf(_deployable);
                    if (Crops.Harvestable(stage)) _hud.SetUseButton(stage == CropStage.Ripe ? "COSECHAR" : "ARRANCAR", Icons.Hand);
                    else
                    {
                        _settlement.TryGet(_deployable, out var crop);
                        int percent = Mathf.FloorToInt(Crops.Growth01(crop.Kind, _settlement.CropAge(_deployable)) * 100f);
                        _hud.SetUseButton($"CRECE {percent}%", Icons.Hand);
                    }
                    break;
                }
                case Target.Player:
                    _hud.SetUseButton("INVITAR", Icons.Team);
                    break;
                case Target.Electric:
                    _hud.SetUseButton(ElectricLabel(), Icons.Bolt);
                    break;
                case Target.Boat:
                    _hud.SetUseButton(_boat > 2000 ? "MONTAR" : "SUBIR", _boat > 2000 ? Icons.Hand : _boat > 1000 ? Icons.Copter : Icons.Boat);
                    break;
                default:
                    _hud.SetUseButton(null);
                    break;
            }
        }

        private void Look()
        {
            LookAtThings();
            if (_target != Target.None) return;
            // Boats float on the water, with nothing to hit: one close by and looked towards will do.
            _boat = BoatInSight != null ? BoatInSight() : 0;
            if (_boat != 0) _target = Target.Boat;
        }

        private void LookAtThings()
        {
            _target = Target.None;
            _hasLock = false;
            var ray = new Ray(_head.position, _head.forward);
            if (!Physics.SphereCast(ray, 0.15f, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var piece = hit.collider.GetComponent<PieceTag>();
            if (piece != null && piece.Piece.IsEdge && _grid.HasDoor(piece.Piece.Edge))
            {
                _target = Target.Door;
                _door = piece.Piece.Edge;
                _lock = LockTarget.Door(_door);
                _hasLock = _settlement.HasLock(_lock);
                return;
            }
            var node = hit.collider.GetComponentInParent<ResourceNodeView>();
            if (node != null && node.Node.Type == ResourceNodeType.Crate && !node.Node.IsDepleted)
            {
                _target = Target.Crate;
                _crate = node;
                return;
            }
            var other = hit.collider.GetComponent<Net.PlayerTag>();
            if (other != null)
            {
                if (CanInvite != null && CanInvite(other.Id))
                {
                    _target = Target.Player;
                    _player = other.Id;
                }
                return;
            }
            var tag = hit.collider.GetComponent<DeployableTag>();
            if (tag == null || !_settlement.TryGet(tag.Id, out _)) return;
            _deployable = tag.Id;
            if (Electricity.IsElectric(tag.Kind) && (HoldingWire || _settlement.Container(tag.Id) == null))
            {
                _target = Target.Electric;
                return;
            }
            if (Crops.IsCrop(tag.Kind))
            {
                _target = Target.Crop;
                return;
            }
            if (tag.Kind == DeployableKind.ToolCupboard) _target = Target.Cupboard;
            else if (tag.Kind == DeployableKind.Workbench)
            {
                _target = Target.Workbench;
                return;
            }
            else if (_settlement.Container(tag.Id) != null) _target = Target.Box;
            _lock = LockTarget.On(tag.Id);
            _hasLock = _target != Target.None && _settlement.HasLock(_lock);
        }

        /// <summary>The lock button: the keypad of the lock in sight, to manage it.</summary>
        public void UseLock()
        {
            Look();
            if (_target == Target.None || !_hasLock) return;
            ManageLock?.Invoke(_lock);
        }

        /// <summary>Uses what is in sight, if anything.</summary>
        public void Use()
        {
            if (Rowing != null && Rowing())
            {
                LeaveBoat?.Invoke();
                _timer = 0f;
                return;
            }
            Look();
            if (Barred)
            {
                EnterCode?.Invoke(_lock);
                return;
            }
            switch (_target)
            {
                case Target.Door:
                    if (Actions.Build(BuildOp.ToggleDoor(_door, !_grid.IsDoorOpen(_door))) != ActionResult.Ok) return;
                    Debug.Log($"[Oxfall] Door at {_door} used");
                    DoorUsed?.Invoke();
                    break;
                case Target.Box:
                    if (_settlement.TryGet(_deployable, out var box) && box.Kind == DeployableKind.LockedCrate && !_settlement.IsCrateOpen(box.Id))
                    {
                        Hack(box.Id);
                        _timer = 0f;
                        break;
                    }
                    OpenBox?.Invoke(_deployable);
                    break;
                case Target.Cupboard:
                    OpenCupboard?.Invoke(_deployable);
                    break;
                case Target.Crate:
                    OpenCrate?.Invoke(_crate);
                    _timer = 0f;
                    break;
                case Target.Workbench:
                    UseWorkbench?.Invoke();
                    break;
                case Target.Crop:
                {
                    var stage = _settlement.CropStageOf(_deployable);
                    if (!Crops.Harvestable(stage))
                    {
                        _hud.ShowToast($"{Crops.StageName(stage)}: aún no se puede cosechar. La lluvia la hace crecer más rápido");
                        break;
                    }
                    _settlement.TryGet(_deployable, out var crop);
                    var result = Actions.Build(BuildOp.Harvest(_deployable));
                    if (result != ActionResult.Ok)
                    {
                        _hud.ShowToast(GameActions.Explain(result));
                        break;
                    }
                    Debug.Log($"[Oxfall] Harvested {crop.Kind} {_deployable} ({stage})");
                    Harvested?.Invoke(crop.Kind, stage);
                    _timer = 0f;
                    break;
                }
                case Target.Player:
                    Invite?.Invoke(_player);
                    _timer = 0f;
                    break;
                case Target.Boat:
                    Board?.Invoke(_boat);
                    _timer = 0f;
                    break;
                case Target.Electric:
                    UseElectric();
                    _timer = 0f;
                    break;
            }
        }

        // ---- Locked crates

        /// <summary>Starts hacking a locked crate, or says how long it has left.</summary>
        private void Hack(int id)
        {
            if (_settlement.IsHacked(id))
            {
                _hud.ShowToast($"Hackeando… se abre en {LockedCrates.Clock(_settlement.HackLeft(id))}. Defiéndela hasta entonces");
                return;
            }
            var result = Actions.Build(BuildOp.Hack(id));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            Debug.Log($"[Oxfall] Hack started on crate {id}");
            _hud.ShowToast($"Hackeo iniciado: se abre en {LockedCrates.Clock(LockedCrates.HackSeconds)}. Los guardias te oyeron");
        }

        // ---- Electricity

        private void SetPending(int source)
        {
            if (PendingSource == source) return;
            PendingSource = source;
            PendingChanged?.Invoke(source);
        }

        /// <summary>The use button's word for the electric thing in sight.</summary>
        private string ElectricLabel()
        {
            _settlement.TryGet(_deployable, out var d);
            var grid = _settlement.Power;
            if (HoldingWire)
            {
                if (PendingSource != 0)
                    return _deployable == PendingSource ? "SOLTAR CABLE" : Electricity.HasInput(d.Kind) ? "CONECTAR" : "SIN ENTRADA";
                if (Electricity.HasOutput(d.Kind)) return "CABLE DESDE";
                return grid.SourceOf(d.Id) != 0 ? "QUITAR CABLE" : "SIN CABLE";
            }
            switch (d.Kind)
            {
                case DeployableKind.ElectricSwitch: return grid.IsOn(d.Id) ? "APAGAR" : "ENCENDER";
                case DeployableKind.Battery: return $"BATERÍA {Mathf.FloorToInt(grid.Charge01(d.Id) * 100f)}%";
                case DeployableKind.SolarPanel: return $"PANEL {grid.Level(d.Id):0} W";
                case DeployableKind.Lamp: return grid.Running(d.Id) ? "LÁMPARA" : "SIN ENERGÍA";
                case DeployableKind.MotionSensor: return grid.IsOn(d.Id) ? "DETECTA: TODOS" : "DETECTA: EXTRAÑOS";
                case DeployableKind.SirenLight: return grid.Running(d.Id) ? "SIRENA" : "SIN ENERGÍA";
                case DeployableKind.DoorController: return grid.Running(d.Id) ? "PUERTA ABIERTA" : "SIN ENERGÍA";
                default: return Deployables.Name(d.Kind).ToUpperInvariant();
            }
        }

        /// <summary>Uses an electric thing: with the cable, runs or pulls cables; without, flips a switch or says how it is.</summary>
        private void UseElectric()
        {
            if (!_settlement.TryGet(_deployable, out var d)) return;
            var grid = _settlement.Power;
            if (HoldingWire)
            {
                if (PendingSource == 0)
                {
                    if (Electricity.HasOutput(d.Kind))
                    {
                        SetPending(d.Id);
                        _hud.ShowToast($"Cable desde {Deployables.Name(d.Kind)}: ahora toca lo que quieres alimentar (hasta {Electricity.WireReach:0} m)");
                        return;
                    }
                    if (grid.SourceOf(d.Id) != 0)
                    {
                        var cut = Actions.Build(BuildOp.Wire(d.Id, 0));
                        _hud.ShowToast(cut == ActionResult.Ok ? "Cable quitado" : GameActions.Explain(cut));
                        if (cut == ActionResult.Ok) Debug.Log($"[Oxfall] Unwired {d.Kind} {d.Id}");
                        return;
                    }
                    _hud.ShowToast($"{Deployables.Name(d.Kind)} no da energía: empieza el cable en un generador, panel, batería o interruptor");
                    return;
                }
                if (d.Id == PendingSource)
                {
                    SetPending(0);
                    _hud.ShowToast("Cable soltado");
                    return;
                }
                int source = PendingSource;
                var result = grid.CanWire(d.Id, source);
                if (result == ActionResult.Ok) result = Actions.Build(BuildOp.Wire(d.Id, source));
                if (result != ActionResult.Ok)
                {
                    _hud.ShowToast(GameActions.Explain(result));
                    return;
                }
                _settlement.TryGet(source, out var from);
                SetPending(0);
                Debug.Log($"[Oxfall] Wired {from.Kind} {source} -> {d.Kind} {d.Id}");
                string need = Electricity.Demand(d.Kind) > 0f ? $" (necesita {Electricity.Demand(d.Kind):0} W)" : "";
                _hud.ShowToast($"Conectado: {Deployables.Name(from.Kind)} → {Deployables.Name(d.Kind)}{need}");
                return;
            }
            switch (d.Kind)
            {
                case DeployableKind.ElectricSwitch:
                {
                    bool on = !grid.IsOn(d.Id);
                    var result = Actions.Build(BuildOp.Toggle(d.Id, on));
                    if (result != ActionResult.Ok) _hud.ShowToast(GameActions.Explain(result));
                    else Debug.Log($"[Oxfall] Switch {d.Id} {(on ? "on" : "off")}");
                    break;
                }
                case DeployableKind.MotionSensor:
                {
                    bool everyone = !grid.IsOn(d.Id);
                    var result = Actions.Build(BuildOp.Toggle(d.Id, everyone));
                    if (result != ActionResult.Ok) _hud.ShowToast(GameActions.Explain(result));
                    else
                    {
                        _hud.ShowToast(everyone ? "El sensor detecta a todos (también a ti)" : "El sensor detecta solo a extraños (sin permiso en tu armario)");
                        Debug.Log($"[Oxfall] Sensor {d.Id} {(everyone ? "everyone" : "strangers")}");
                    }
                    break;
                }
                default:
                    _hud.ShowToast(PowerInfo(d));
                    break;
            }
        }

        /// <summary>A line about an electric thing's state, under the crosshair.</summary>
        private void ShowPowerInfo()
        {
            string info = null;
            if (_target != Target.None && _settlement.TryGet(_deployable, out var d) && Electricity.IsElectric(d.Kind)
                && (_target == Target.Electric || _target == Target.Box))
                info = PowerInfo(d);
            if (info == null && !_infoShown) return;
            string held = HeldItem?.Invoke();
            // The hammer, the rod and the throwables say their own thing there.
            if (held == ItemIds.Hammer || held == ItemIds.FishingRod || held == ItemIds.SatchelCharge || held == ItemIds.Beancan) return;
            _hud.SetAimInfo(info);
            _infoShown = info != null;
        }

        private string PowerInfo(Deployable d)
        {
            var grid = _settlement.Power;
            string name = Deployables.Name(d.Kind);
            switch (d.Kind)
            {
                case DeployableKind.Generator:
                {
                    int fuel = _settlement.Container(d.Id)?.Count(ItemIds.LowGradeFuel) ?? 0;
                    if (grid.Running(d.Id)) return $"{name}: da {Electricity.GeneratorPower:0} W · combustible {fuel}";
                    return fuel > 0 ? $"{name}: conéctale algo con el cable · combustible {fuel}" : $"{name}: sin combustible";
                }
                case DeployableKind.SolarPanel:
                    return grid.Level(d.Id) > 0.5f ? $"{name}: da {grid.Level(d.Id):0} W ahora" : $"{name}: sin sol, no da nada";
                case DeployableKind.Battery:
                    return $"{name}: {Mathf.FloorToInt(grid.Charge01(d.Id) * 100f)}% · da hasta {Electricity.BatteryOutput:0} W";
                case DeployableKind.ElectricSwitch:
                    return $"{name}: {(grid.IsOn(d.Id) ? "encendido" : "apagado")}{(grid.SourceOf(d.Id) == 0 ? " · sin cable de entrada" : "")}";
                case DeployableKind.MotionSensor:
                {
                    string who = grid.IsOn(d.Id) ? "todos" : "extraños";
                    if (!grid.Running(d.Id)) return grid.SourceOf(d.Id) == 0 ? $"{name}: sin cable (necesita 1 W)" : $"{name}: sin energía";
                    return Electricity.Sensing(d.Kind, grid.Level(d.Id)) ? $"{name}: ¡detecta a alguien! ({who})" : $"{name}: vigilando {Electricity.SensorRange:0} m ({who})";
                }
                case DeployableKind.DoorController:
                    if (!Electricity.TryDoorNear(_settlement, d.X, d.Y, d.Z, out _)) return $"{name}: no hay puerta al lado";
                    if (grid.Running(d.Id)) return $"{name}: con energía, puerta abierta";
                    return grid.SourceOf(d.Id) == 0 ? $"{name}: sin cable (necesita 1 W)" : $"{name}: sin energía, puerta cerrada";
                case DeployableKind.Lamp:
                case DeployableKind.AutoTurret:
                case DeployableKind.SirenLight:
                {
                    float need = Electricity.Demand(d.Kind);
                    if (grid.Running(d.Id)) return $"{name}: con energía ({need:0} W)";
                    return grid.SourceOf(d.Id) == 0 ? $"{name}: sin cable (necesita {need:0} W)" : $"{name}: sin energía suficiente (necesita {need:0} W)";
                }
                default:
                    return null;
            }
        }
    }
}
