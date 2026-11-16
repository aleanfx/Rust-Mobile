using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Power, kept simple for a phone: generators, solar panels and batteries give it; lamps and
    /// turrets take it; switches pass it on or cut it. A cable runs from what gives power to what
    /// takes it (each thing has one cable in; each output feeds up to <see cref="MaxOutputs"/>), and
    /// what is plugged in first is served first: a device runs only when all it needs is left.
    /// Batteries fill from what is left over and give it back when the rest stops.
    /// </summary>
    public static class Electricity
    {
        public const float GeneratorPower = 30f;
        /// <summary>Seconds one unit of fuel keeps a generator running.</summary>
        public const float FuelSeconds = 60f;
        public const int GeneratorSlots = 1;

        public const float SolarPower = 20f;

        public const float BatteryOutput = 20f;
        public const float BatteryChargeRate = 20f;
        /// <summary>A battery's store: twenty minutes of a turret, about two nights.</summary>
        public const float BatteryCapacity = 12000f;

        public const float LampDemand = 2f;
        public const float TurretDemand = 10f;
        /// <summary>What a sensor, a siren or a door controller takes.</summary>
        public const float SmallDemand = 1f;

        /// <summary>How near someone must be for a sensor to notice them (metres).</summary>
        public const float SensorRange = 10f;

        /// <summary>How near a door a door controller must stand (metres, to the middle of the doorway).</summary>
        public const float DoorReach = 2.5f;

        /// <summary>How long a cable can be (metres, end to end).</summary>
        public const float WireReach = 20f;

        /// <summary>How many things one output feeds.</summary>
        public const int MaxOutputs = 4;

        public static bool IsElectric(DeployableKind kind) => HasInput(kind) || HasOutput(kind);

        /// <summary>Whether a cable can come into it.</summary>
        public static bool HasInput(DeployableKind kind) =>
            kind == DeployableKind.Battery || kind == DeployableKind.ElectricSwitch || kind == DeployableKind.Lamp || kind == DeployableKind.AutoTurret
            || kind == DeployableKind.MotionSensor || kind == DeployableKind.SirenLight || kind == DeployableKind.DoorController;

        /// <summary>Whether a cable can go out of it.</summary>
        public static bool HasOutput(DeployableKind kind) =>
            kind == DeployableKind.Generator || kind == DeployableKind.SolarPanel || kind == DeployableKind.Battery || kind == DeployableKind.ElectricSwitch
            || kind == DeployableKind.MotionSensor;

        /// <summary>Whether it passes power on only some of the time (a switch when on, a sensor when it sees someone).</summary>
        public static bool Passes(DeployableKind kind) => kind == DeployableKind.ElectricSwitch || kind == DeployableKind.MotionSensor;

        /// <summary>Whether it can be tapped to change: a switch on or off, a sensor to strangers only or everyone.</summary>
        public static bool Toggles(DeployableKind kind) => kind == DeployableKind.ElectricSwitch || kind == DeployableKind.MotionSensor;

        /// <summary>Power it takes to run (0: none of its own).</summary>
        public static float Demand(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Lamp: return LampDemand;
                case DeployableKind.AutoTurret: return TurretDemand;
                case DeployableKind.MotionSensor:
                case DeployableKind.SirenLight:
                case DeployableKind.DoorController:
                    return SmallDemand;
                default: return 0f;
            }
        }

        /// <summary>What a generator burns.</summary>
        public static bool Burns(string itemId) => itemId == ItemIds.LowGradeFuel;

        /// <summary>A solar panel's output with the sun this high (0..1 of its peak) and this much rain.</summary>
        public static float Solar(float sun01, float rain01) =>
            SolarPower * Math.Max(0f, Math.Min(1f, sun01)) * (1f - 0.6f * Math.Max(0f, Math.Min(1f, rain01)));

        /// <summary>Whether a device with this kind and this level of power in is running.</summary>
        public static bool Running(DeployableKind kind, float level) =>
            Demand(kind) > 0f ? level >= Demand(kind) - 1e-3f : level > 0f;

        /// <summary>A sensor's level shows it sees someone: what it takes, and half a unit more at least.</summary>
        public static bool Sensing(DeployableKind kind, float level) =>
            kind == DeployableKind.MotionSensor && level >= SmallDemand + 0.25f;

        /// <summary>The doorway nearest a spot, within <see cref="DoorReach"/>, that holds a door.</summary>
        public static bool TryDoorNear(Settlement settlement, float x, float y, float z, out Edge door)
        {
            door = default;
            float best = DoorReach * DoorReach;
            bool found = false;
            foreach (var pair in settlement.Grid.Doors)
            {
                var (ex, ey, ez) = PlayerActions.EdgePoint(settlement.Grid, pair.Key);
                // From the controller's feet to the doorway's middle, which is half a wall up.
                float dx = ex - x, dy = Math.Max(0f, Math.Abs(ey - y) - 1.5f), dz = ez - z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > best) continue;
                best = d2;
                door = pair.Key;
                found = true;
            }
            return found;
        }
    }

    /// <summary>
    /// The cables of a settlement and what flows in them: who feeds whom, which switches are on, what
    /// batteries hold and what reaches each device. Kept by the <see cref="Settlement"/>; the server
    /// (or the game alone) runs <see cref="Tick"/>, copies only take what it tells.
    /// </summary>
    public sealed class PowerGrid
    {
        private readonly Settlement _settlement;
        // Each device's one cable in: from what, and when it was plugged (first plugged, first served).
        private readonly Dictionary<int, (int Source, int Order)> _input = new Dictionary<int, (int, int)>();
        private int _nextOrder = 1;
        private readonly HashSet<int> _on = new HashSet<int>();
        private readonly Dictionary<int, float> _charge = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _burn = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _level = new Dictionary<int, float>();

        // Scratch for a tick.
        private readonly Dictionary<int, List<int>> _children = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, float> _next = new Dictionary<int, float>();
        private readonly HashSet<int> _visited = new HashSet<int>();
        private readonly List<int> _ids = new List<int>();

        public PowerGrid(Settlement settlement) => _settlement = settlement;

        /// <summary>How strong the sun is now (0..1 of noon), and how hard it rains: whoever ticks sets these.</summary>
        public float Sun { get; set; }
        public float Rain { get; set; }

        /// <summary>
        /// Whether a sensor sees someone near: everyone (true) or only strangers (false). Whoever ticks
        /// sets it; unset, sensors see nobody.
        /// </summary>
        public Func<Deployable, bool, bool> Senses { get; set; }

        // Door controllers that have opened their door, and the door each one works.
        private readonly HashSet<int> _opened = new HashSet<int>();

        /// <summary>A cable was plugged or unplugged, a switch flipped, or a device's power changed (its id).</summary>
        public event Action<int> Changed;

        /// <summary>Cables were plugged or unplugged.</summary>
        public event Action WiresChanged;

        /// <summary>Every cable: (device fed, what feeds it), in the order they were plugged.</summary>
        public IEnumerable<(int Device, int Source)> Wires
        {
            get
            {
                var list = new List<(int Device, int Source, int Order)>(_input.Count);
                foreach (var pair in _input) list.Add((pair.Key, pair.Value.Source, pair.Value.Order));
                list.Sort((a, b) => a.Order.CompareTo(b.Order));
                foreach (var w in list) yield return (w.Device, w.Source);
            }
        }

        public int WireCount => _input.Count;

        /// <summary>What feeds a device (0: nothing).</summary>
        public int SourceOf(int id) => _input.TryGetValue(id, out var w) ? w.Source : 0;

        /// <summary>How many cables go out of a device.</summary>
        public int OutputsOf(int id)
        {
            int n = 0;
            foreach (var w in _input.Values) if (w.Source == id) n++;
            return n;
        }

        public bool IsOn(int id) => _on.Contains(id);

        /// <summary>
        /// Power at a device now: for a lamp or turret what reaches it, for a switch what it passes on,
        /// for a generator or panel what it can give, for a battery what it gives.
        /// </summary>
        public float Level(int id) => _level.TryGetValue(id, out float l) ? l : 0f;

        /// <summary>What a battery holds (power-seconds).</summary>
        public float Charge(int id) => _charge.TryGetValue(id, out float c) ? c : 0f;

        public float Charge01(int id) => Charge(id) / Electricity.BatteryCapacity;

        /// <summary>Whether a device of its kind has what it needs to run.</summary>
        public bool Running(int id) =>
            _settlement.TryGet(id, out var d) && Electricity.Running(d.Kind, Level(id));

        /// <summary>
        /// Whether a cable could run from <paramref name="source"/> into <paramref name="device"/>:
        /// the right ends, near enough, the output not full, and no loop back.
        /// </summary>
        public ActionResult CanWire(int device, int source)
        {
            if (!_settlement.TryGet(device, out var d) || !_settlement.TryGet(source, out var s)) return ActionResult.NoPiece;
            if (device == source || !Electricity.HasInput(d.Kind) || !Electricity.HasOutput(s.Kind)) return ActionResult.CannotWire;
            float dx = d.X - s.X, dy = d.Y - s.Y, dz = d.Z - s.Z;
            if (dx * dx + dy * dy + dz * dz > Electricity.WireReach * Electricity.WireReach) return ActionResult.WireTooLong;
            if (SourceOf(device) != source && OutputsOf(source) >= Electricity.MaxOutputs) return ActionResult.OutputsFull;
            // Up from the source through what feeds it: meeting the device would close a loop.
            int at = source, steps = 0;
            while (at != 0 && steps++ < 512)
            {
                if (at == device) return ActionResult.CannotWire;
                at = SourceOf(at);
            }
            return ActionResult.Ok;
        }

        /// <summary>Plugs a cable from <paramref name="source"/> into <paramref name="device"/> (0: unplugs its cable in). No checks.</summary>
        public bool Connect(int device, int source)
        {
            if (!_settlement.TryGet(device, out _)) return false;
            if (source == 0)
            {
                if (!_input.Remove(device)) return true;
            }
            else
            {
                if (!_settlement.TryGet(source, out _)) return false;
                if (_input.TryGetValue(device, out var was) && was.Source == source) return true;
                _input[device] = (source, _nextOrder++);
            }
            WiresChanged?.Invoke();
            Changed?.Invoke(device);
            return true;
        }

        /// <summary>Turns a switch on or off, or sets a sensor to see everyone (on) or strangers only. False if it is neither.</summary>
        public bool SetOn(int id, bool on)
        {
            if (!_settlement.TryGet(id, out var d) || !Electricity.Toggles(d.Kind)) return false;
            if (on ? _on.Add(id) : _on.Remove(id)) Changed?.Invoke(id);
            return true;
        }

        /// <summary>Takes what the server says of a device: a battery's charge, or anything else's level.</summary>
        public bool Set(int id, float value)
        {
            if (!_settlement.TryGet(id, out var d) || !Electricity.IsElectric(d.Kind)) return false;
            value = Math.Max(0f, value);
            if (d.Kind == DeployableKind.Battery)
            {
                _charge[id] = Math.Min(value, Electricity.BatteryCapacity);
                _level[id] = _charge[id] > 0f ? Electricity.BatteryOutput : 0f;
            }
            else _level[id] = value;
            Changed?.Invoke(id);
            return true;
        }

        /// <summary>A device went: its cables (in and out) go with it.</summary>
        public void Forget(int id)
        {
            bool wires = _input.Remove(id);
            _ids.Clear();
            foreach (var pair in _input) if (pair.Value.Source == id) _ids.Add(pair.Key);
            foreach (int fed in _ids)
            {
                _input.Remove(fed);
                wires = true;
                if (_level.ContainsKey(fed)) _level[fed] = 0f;
                Changed?.Invoke(fed);
            }
            _on.Remove(id);
            _opened.Remove(id);
            _charge.Remove(id);
            _burn.Remove(id);
            _level.Remove(id);
            if (wires) WiresChanged?.Invoke();
        }

        public void Clear()
        {
            _input.Clear();
            _on.Clear();
            _opened.Clear();
            _charge.Clear();
            _burn.Clear();
            _level.Clear();
            _nextOrder = 1;
        }

        /// <summary>
        /// Power flows for <paramref name="dt"/> seconds: generators burn, panels catch the sun,
        /// batteries fill and empty, and each device gets what reaches it. Changes worth telling go
        /// into <paramref name="told"/> (when given) as <see cref="BuildOp.Power"/>.
        /// </summary>
        public void Tick(float dt, List<BuildOp> told)
        {
            if (dt <= 0f) return;
            _next.Clear();
            _visited.Clear();
            foreach (var list in _children.Values) list.Clear();
            foreach (var w in Wires)
            {
                if (!_children.TryGetValue(w.Source, out var list)) _children[w.Source] = list = new List<int>();
                list.Add(w.Device);
            }

            foreach (var d in _settlement.Deployables)
            {
                if (!Electricity.HasOutput(d.Kind) || Electricity.Passes(d.Kind)) continue;
                bool fed = _children.TryGetValue(d.Id, out var kids) && kids.Count > 0;
                float supply = 0f;
                switch (d.Kind)
                {
                    case DeployableKind.Generator:
                        supply = BurnGenerator(d.Id, dt, fed) ? Electricity.GeneratorPower : 0f;
                        _next[d.Id] = supply;
                        break;
                    case DeployableKind.SolarPanel:
                        supply = Electricity.Solar(Sun, Rain);
                        _next[d.Id] = supply;
                        break;
                    case DeployableKind.Battery:
                    {
                        float charge = Charge(d.Id);
                        supply = Math.Min(Electricity.BatteryOutput, charge / dt);
                        break;
                    }
                }
                float used = 0f;
                if (fed)
                    foreach (int kid in kids) used += Feed(kid, supply - used, dt);
                if (d.Kind == DeployableKind.Battery)
                {
                    _charge[d.Id] = Math.Max(0f, Charge(d.Id) - used * dt);
                    // A battery's own level is what it gives; its charge is told separately.
                    _next[d.Id] = used;
                }
            }

            // Tell what changed: every device's new level against the last, batteries in steps of 5%.
            _ids.Clear();
            foreach (var d in _settlement.Deployables) if (Electricity.IsElectric(d.Kind)) _ids.Add(d.Id);
            foreach (int id in _ids)
            {
                _settlement.TryGet(id, out var d);
                if (d.Kind == DeployableKind.Battery)
                {
                    float charge = Charge(id);
                    float last = _told.TryGetValue(id, out float t) ? t : -1f;
                    int step = Bucket(charge), lastStep = last < 0f ? -1 : Bucket(last);
                    _level[id] = _next.TryGetValue(id, out float give) ? give : 0f;
                    if (step == lastStep) continue;
                    _told[id] = charge;
                    told?.Add(BuildOp.Power(id, charge));
                    Changed?.Invoke(id);
                    continue;
                }
                float now = _next.TryGetValue(id, out float n) ? n : 0f;
                float before = Level(id);
                _level[id] = now;
                if (d.Kind == DeployableKind.DoorController) WorkDoor(d, Electricity.Running(d.Kind, now), told);
                if (Math.Abs(now - before) < 0.5f && Electricity.Running(d.Kind, now) == Electricity.Running(d.Kind, before)) continue;
                told?.Add(BuildOp.Power(id, (float)Math.Round(now, 1)));
                Changed?.Invoke(id);
            }
        }

        private readonly Dictionary<int, float> _told = new Dictionary<int, float>();

        private static int Bucket(float charge) => (int)Math.Floor(charge / Electricity.BatteryCapacity * 20f + 1e-4f);

        /// <summary>
        /// Offers <paramref name="offered"/> to a device and what hangs off it; returns what it took.
        /// </summary>
        private float Feed(int id, float offered, float dt)
        {
            if (!_visited.Add(id) || !_settlement.TryGet(id, out var d)) return 0f;
            offered = Math.Max(0f, offered);
            switch (d.Kind)
            {
                case DeployableKind.Lamp:
                case DeployableKind.AutoTurret:
                case DeployableKind.SirenLight:
                case DeployableKind.DoorController:
                {
                    float need = Electricity.Demand(d.Kind);
                    if (offered + 1e-4f < need) return 0f;
                    _next[id] = need;
                    return need;
                }
                case DeployableKind.ElectricSwitch:
                {
                    if (!_on.Contains(id) || offered <= 0f) return 0f;
                    float used = 0f;
                    if (_children.TryGetValue(id, out var kids))
                        foreach (int kid in kids) used += Feed(kid, offered - used, dt);
                    // A switch on with power behind it shows as live even with nothing drawing.
                    _next[id] = Math.Max(used, 0.01f);
                    return used;
                }
                case DeployableKind.MotionSensor:
                {
                    // Takes its own bit, and passes the rest on while it sees someone.
                    float need = Electricity.SmallDemand;
                    if (offered + 1e-4f < need) return 0f;
                    if (Senses == null || !Senses(d, _on.Contains(id)))
                    {
                        _next[id] = need;
                        return need;
                    }
                    float used = 0f;
                    if (_children.TryGetValue(id, out var kids))
                        foreach (int kid in kids) used += Feed(kid, offered - need - used, dt);
                    _next[id] = need + Math.Max(used, 0.6f);
                    return need + used;
                }
                case DeployableKind.Battery:
                {
                    float room = Electricity.BatteryCapacity - Charge(id);
                    float take = Math.Min(Math.Min(offered, Electricity.BatteryChargeRate), room / dt);
                    if (take <= 0f) return 0f;
                    _charge[id] = Charge(id) + take * dt;
                    return take;
                }
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// A door controller opens its door when power comes and shuts it when power goes (only then:
        /// in between, anyone with the code still works the door by hand).
        /// </summary>
        private void WorkDoor(Deployable controller, bool running, List<BuildOp> told)
        {
            if (running == _opened.Contains(controller.Id)) return;
            if (running) _opened.Add(controller.Id);
            else _opened.Remove(controller.Id);
            if (!Electricity.TryDoorNear(_settlement, controller.X, controller.Y, controller.Z, out var door)) return;
            if (_settlement.Grid.IsDoorOpen(door) == running) return;
            _settlement.Grid.ToggleDoor(door);
            told?.Add(BuildOp.ToggleDoor(door, running));
        }

        /// <summary>A generator with something plugged in burns its fuel: true while it runs.</summary>
        private bool BurnGenerator(int id, float dt, bool fed)
        {
            float left = _burn.TryGetValue(id, out float b) ? b : 0f;
            if (!fed) return false;
            if (left <= 0f)
            {
                var box = _settlement.Container(id);
                if (box == null || !box.Has(ItemIds.LowGradeFuel, 1)) return false;
                box.Remove(ItemIds.LowGradeFuel, 1);
                left += Electricity.FuelSeconds;
            }
            _burn[id] = left - dt;
            return true;
        }

        /// <summary>The ops that rebuild these cables and states on a copy.</summary>
        public void Describe(List<BuildOp> ops)
        {
            foreach (var w in Wires) ops.Add(BuildOp.Wire(w.Device, w.Source));
            foreach (int id in _on) ops.Add(BuildOp.Toggle(id, true));
            foreach (var pair in _charge) if (pair.Value > 0f) ops.Add(BuildOp.Power(pair.Key, pair.Value));
            foreach (var pair in _level)
                if (pair.Value > 0f && _settlement.TryGet(pair.Key, out var d) && d.Kind != DeployableKind.Battery)
                    ops.Add(BuildOp.Power(pair.Key, pair.Value));
        }
    }
}
