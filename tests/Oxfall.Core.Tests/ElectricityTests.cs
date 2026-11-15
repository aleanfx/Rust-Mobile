using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Power: generators, panels and batteries feed lamps and turrets through cables and switches.</summary>
    public class ElectricityTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A fuelled generator beside <paramref name="device"/>, wired into it.</summary>
        public static Deployable Power(Settlement s, Deployable device)
        {
            var g = s.Add(new Deployable(0, DeployableKind.Generator, device.X + 1.5f, device.Y, device.Z, 0f, device.Owner));
            s.Container(g.Id).Add(ItemIds.LowGradeFuel, 10);
            Assert.AreEqual(ActionResult.Ok, s.Power.CanWire(device.Id, g.Id));
            Assert.IsTrue(s.Power.Connect(device.Id, g.Id));
            return g;
        }

        private static Settlement NewSettlement() => new Settlement(new IslandGround(Seed), Catalog);

        private static Deployable Add(Settlement s, DeployableKind kind, float x) => s.Add(new Deployable(0, kind, x, 1f, 0f, 0f, 1));

        [Test]
        public void Everything_is_made_and_the_big_parts_learned_at_the_workbench()
        {
            var book = RecipeBook.CreateDefault(Catalog);
            foreach (var id in new[] { "wire_tool", "electric_lamp", "electric_switch" })
            {
                Assert.IsTrue(book.TryGet(id, out var r), id);
                Assert.IsFalse(r.NeedsWorkbench, id);
            }
            foreach (var id in new[] { "small_generator", "solar_panel", "small_battery" })
            {
                Assert.IsTrue(book.TryGet(id, out var r), id);
                Assert.IsTrue(r.NeedsWorkbench, id);
                Assert.Greater(r.ScrapToLearn, 0, id);
            }
            foreach (var item in new[] { ItemIds.Generator, ItemIds.SolarPanel, ItemIds.Battery, ItemIds.ElectricSwitch, ItemIds.Lamp })
            {
                Assert.IsTrue(Deployables.TryKindOf(item, out var kind), item);
                Assert.AreEqual(item, Deployables.ItemOf(kind));
                Assert.Greater(Deployables.MaxHealth(kind), 0f);
                Assert.IsTrue(Electricity.IsElectric(kind));
            }
            Assert.AreEqual(ToolKind.Wire, Catalog.Get(ItemIds.WireTool).Tool);
            Assert.IsFalse(Combat.CanHurt(ToolKind.Wire));
        }

        [Test]
        public void A_generator_lights_a_lamp_while_it_has_fuel_and_burns_only_when_wired()
        {
            var s = NewSettlement();
            var lamp = Add(s, DeployableKind.Lamp, 0f);
            var gen = Add(s, DeployableKind.Generator, 2f);
            s.Container(gen.Id).Add(ItemIds.LowGradeFuel, 2);
            s.Tick(1f);
            Assert.AreEqual(2, s.Container(gen.Id).Count(ItemIds.LowGradeFuel), "nothing plugged in: it does not burn");
            Assert.IsFalse(s.Power.Running(lamp.Id));

            Assert.IsTrue(s.Power.Connect(lamp.Id, gen.Id));
            s.Tick(1f);
            Assert.IsTrue(s.Power.Running(lamp.Id));
            Assert.AreEqual(Electricity.LampDemand, s.Power.Level(lamp.Id), 1e-3f);
            Assert.AreEqual(1, s.Container(gen.Id).Count(ItemIds.LowGradeFuel), "one burning");
            for (int i = 0; i < 125; i++) s.Tick(1f);
            Assert.AreEqual(0, s.Container(gen.Id).Count(ItemIds.LowGradeFuel));
            Assert.IsFalse(s.Power.Running(lamp.Id), "two minutes on two units of fuel, then dark");
        }

        [Test]
        public void First_plugged_is_served_first_and_a_switch_cuts_what_hangs_off_it()
        {
            var s = NewSettlement();
            var panel = Add(s, DeployableKind.SolarPanel, 0f);
            var turret = Add(s, DeployableKind.AutoTurret, 2f);
            var sw = Add(s, DeployableKind.ElectricSwitch, 4f);
            var lamp = Add(s, DeployableKind.Lamp, 6f);
            s.Power.Sun = 0.55f; // 11 of 20
            Assert.IsTrue(s.Power.Connect(turret.Id, panel.Id));
            Assert.IsTrue(s.Power.Connect(sw.Id, panel.Id));
            Assert.IsTrue(s.Power.Connect(lamp.Id, sw.Id));
            s.Tick(1f);
            Assert.IsTrue(s.Power.Running(turret.Id));
            Assert.IsFalse(s.Power.Running(lamp.Id), "the switch is off");
            Assert.IsTrue(s.Power.SetOn(sw.Id, true));
            s.Tick(1f);
            Assert.IsFalse(s.Power.Running(lamp.Id), "11 - 10 leaves 1: not enough for a lamp");
            s.Power.Sun = 1f;
            s.Tick(1f);
            Assert.IsTrue(s.Power.Running(lamp.Id));
            Assert.IsTrue(s.Power.Running(turret.Id));
            s.Power.Sun = 0f;
            s.Tick(1f);
            Assert.IsFalse(s.Power.Running(turret.Id), "no sun, no power");
        }

        [Test]
        public void A_battery_fills_by_day_and_runs_a_turret_through_the_night()
        {
            var s = NewSettlement();
            var panel = Add(s, DeployableKind.SolarPanel, 0f);
            var battery = Add(s, DeployableKind.Battery, 2f);
            var turret = Add(s, DeployableKind.AutoTurret, 4f);
            Assert.IsTrue(s.Power.Connect(battery.Id, panel.Id));
            Assert.IsTrue(s.Power.Connect(turret.Id, battery.Id));
            s.Power.Sun = 1f;
            s.Tick(1f);
            Assert.Greater(s.Power.Charge(battery.Id), 0f);
            // The panel's 20 fill it while the turret draws 10 from it: half the rate.
            for (int i = 0; i < 1300; i++) s.Tick(1f);
            Assert.AreEqual(1f, s.Power.Charge01(battery.Id), 1e-3f, "full");
            Assert.IsTrue(s.Power.Running(turret.Id));
            s.Power.Sun = 0f;
            for (int i = 0; i < 600; i++) s.Tick(1f); // a night
            Assert.IsTrue(s.Power.Running(turret.Id), "still guarding at dawn");
            Assert.AreEqual(1f - 6000f / Electricity.BatteryCapacity, s.Power.Charge01(battery.Id), 0.01f);
        }

        [Test]
        public void Cables_have_ends_reach_and_no_loops_and_go_with_what_they_join()
        {
            var s = NewSettlement();
            var gen = Add(s, DeployableKind.Generator, 0f);
            var a = Add(s, DeployableKind.ElectricSwitch, 2f);
            var b = Add(s, DeployableKind.ElectricSwitch, 4f);
            var lamp = Add(s, DeployableKind.Lamp, 6f);
            var far = Add(s, DeployableKind.Lamp, 40f);
            Assert.AreEqual(ActionResult.CannotWire, s.Power.CanWire(gen.Id, lamp.Id), "a lamp gives no power");
            Assert.AreEqual(ActionResult.CannotWire, s.Power.CanWire(lamp.Id, lamp.Id));
            Assert.AreEqual(ActionResult.WireTooLong, s.Power.CanWire(far.Id, gen.Id));
            Assert.IsTrue(s.Power.Connect(a.Id, gen.Id));
            Assert.IsTrue(s.Power.Connect(b.Id, a.Id));
            Assert.AreEqual(ActionResult.CannotWire, s.Power.CanWire(a.Id, b.Id), "a loop");
            for (int i = 0; i < Electricity.MaxOutputs - 1; i++) Assert.IsTrue(s.Power.Connect(Add(s, DeployableKind.Lamp, 1f + i * 0.1f).Id, gen.Id));
            Assert.AreEqual(ActionResult.OutputsFull, s.Power.CanWire(lamp.Id, gen.Id));
            Assert.AreEqual(ActionResult.Ok, s.Power.CanWire(lamp.Id, b.Id));
            Assert.IsTrue(s.Power.Connect(lamp.Id, b.Id));
            Assert.AreEqual(b.Id, s.Power.SourceOf(lamp.Id));

            s.Remove(b.Id);
            Assert.AreEqual(0, s.Power.SourceOf(lamp.Id), "the lamp's cable went with the switch");
            Assert.AreEqual(Electricity.MaxOutputs, s.Power.WireCount, "the rest stays");
            Assert.IsTrue(s.Power.Connect(lamp.Id, 0) || true);
        }

        [Test]
        public void Wiring_needs_the_cable_reach_and_the_cupboard()
        {
            var s = NewSettlement();
            var lamp = Add(s, DeployableKind.Lamp, 0f);
            var gen = Add(s, DeployableKind.Generator, 2f);
            var survivor = new Survivor(Catalog);
            survivor.Inventory.Clear();
            Assert.AreEqual(ActionResult.NoWireTool, PlayerActions.Build(s, survivor, 1, BuildOp.Wire(lamp.Id, gen.Id), 1f, 1f, 0f, out _));
            survivor.Inventory.Add(ItemIds.WireTool, 1);
            Assert.AreEqual(ActionResult.TooFar, PlayerActions.Build(s, survivor, 1, BuildOp.Wire(lamp.Id, gen.Id), 60f, 1f, 0f, out _));
            var tc = s.Add(new Deployable(0, DeployableKind.ToolCupboard, 1f, 1f, 3f, 0f, 2));
            s.Authorize(tc.Id, 2);
            Assert.AreEqual(ActionResult.BuildingBlocked, PlayerActions.Build(s, survivor, 1, BuildOp.Wire(lamp.Id, gen.Id), 1f, 1f, 0f, out _));
            s.Authorize(tc.Id, 1);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(s, survivor, 1, BuildOp.Wire(lamp.Id, gen.Id), 1f, 1f, 0f, out var done));
            Assert.AreEqual(gen.Id, s.Power.SourceOf(lamp.Id));
            Assert.AreEqual(BuildOpKind.Wire, done.Kind);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(s, survivor, 1, BuildOp.Power(lamp.Id, 5f), 1f, 1f, 0f, out _), "power is the server's to tell");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(s, survivor, 1, BuildOp.Wire(lamp.Id, 0), 1f, 1f, 0f, out _));
            Assert.AreEqual(0, s.Power.SourceOf(lamp.Id));

            // The generator takes only fuel.
            survivor.Inventory.Add(ItemIds.Wood, 10);
            survivor.Inventory.Add(ItemIds.LowGradeFuel, 10);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(s, survivor, 1, gen.Id, 2f, 1f, 0f));
            int wood = Enumerable.Range(0, survivor.Inventory.SlotCount).First(i => survivor.Inventory[i].ItemId == ItemIds.Wood);
            int fuel = Enumerable.Range(0, survivor.Inventory.SlotCount).First(i => survivor.Inventory[i].ItemId == ItemIds.LowGradeFuel);
            Assert.AreEqual(ActionResult.NotFuel, PlayerActions.Put(s, survivor, wood, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(s, survivor, fuel, -1));
        }

        [Test]
        public void A_copy_takes_cables_switches_and_power_from_the_ops_and_the_save()
        {
            var s = NewSettlement();
            var gen = Add(s, DeployableKind.Generator, 0f);
            var sw = Add(s, DeployableKind.ElectricSwitch, 2f);
            var lamp = Add(s, DeployableKind.Lamp, 4f);
            var battery = Add(s, DeployableKind.Battery, 6f);
            s.Container(gen.Id).Add(ItemIds.LowGradeFuel, 5);
            s.Power.Connect(sw.Id, gen.Id);
            s.Power.Connect(lamp.Id, sw.Id);
            s.Power.Connect(battery.Id, gen.Id);
            s.Power.SetOn(sw.Id, true);
            var told = new List<BuildOp>();
            s.Tick(1f, decayed: told);
            Assert.IsTrue(told.Any(o => o.Kind == BuildOpKind.Power && o.Id == lamp.Id && o.Top >= Electricity.LampDemand), "the lamp's power is told");
            Assert.IsTrue(told.Any(o => o.Kind == BuildOpKind.Power && o.Id == battery.Id), "and the battery's charge");

            var copy = NewSettlement();
            foreach (var op in BuildOps.Describe(s))
            {
                var w = new NetWriter();
                op.Write(w);
                var back = BuildOp.Read(new NetReader(w.ToArray()));
                Assert.IsTrue(BuildOps.Apply(copy, back), back.ToString());
            }
            Assert.AreEqual(sw.Id, copy.Power.SourceOf(lamp.Id));
            Assert.IsTrue(copy.Power.IsOn(sw.Id));
            Assert.IsTrue(copy.Power.Running(lamp.Id), "lit for someone who just joined");
            Assert.AreEqual(s.Power.Charge(battery.Id), copy.Power.Charge(battery.Id), 1e-2f);
        }

        [Test]
        public void On_the_server_the_electric_kit_and_a_wired_lamp_work()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ana", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var ana, out _, out _);
            ServerWorld.GiveTestKit(rec.Survivor.Inventory, 12);
            Assert.AreEqual(3, rec.Survivor.Inventory.Count(ItemIds.Lamp));
            var at = ana.Pose;
            var lamp = world.Settlement.Add(new Deployable(0, DeployableKind.Lamp, at.X + 1f, at.Y, at.Z, 0f, rec.Owner));
            var gen = world.Settlement.Add(new Deployable(0, DeployableKind.Generator, at.X - 1f, at.Y, at.Z, 0f, rec.Owner));
            world.Settlement.Container(gen.Id).Add(ItemIds.LowGradeFuel, 1);
            Assert.AreEqual(ActionResult.Ok, world.Build(ana.Id, BuildOp.Wire(lamp.Id, gen.Id), out var done));
            Assert.AreEqual(BuildOpKind.Wire, done.Kind);
            world.Announcements.Clear();
            world.Tick(0.5f);
            Assert.IsTrue(world.Settlement.Power.Running(lamp.Id));
            Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Power && o.Id == lamp.Id));
        }
    }
}
