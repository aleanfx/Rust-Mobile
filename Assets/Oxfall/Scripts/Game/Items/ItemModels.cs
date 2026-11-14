using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Items
{
    /// <summary>How an item looks: its mesh and materials, how it sits in the hand, and its icon pose.</summary>
    public sealed class ItemModel
    {
        public Mesh Mesh;
        public Material[] Materials;

        /// <summary>Whether it shows in the hand when chosen on the hotbar. Resources don't.</summary>
        public bool ShowInHand;

        /// <summary>Where it rests in front of the camera, in the head's space (x right, y up, z ahead).</summary>
        public Vector3 HandPosition;
        public Quaternion HandRotation = Quaternion.identity;
        public float HandScale = 1f;

        /// <summary>Its rotation for the inventory icon, seen by a camera looking along +Z.</summary>
        public Quaternion IconRotation = Quaternion.identity;

        /// <summary>How much closer the icon frames it: thin diagonal tools leave room in their box.</summary>
        public float IconZoom = 1f;

        /// <summary>Whether it burns while held (the torch), and where the flame sits on the model.</summary>
        public bool Burns;
        public Vector3 FirePosition;

        /// <summary>Whether a blow is a stab straight ahead (a spear) rather than a swing.</summary>
        public bool Thrusts;

        /// <summary>Whether it is drawn and loosed (a bow) rather than swung.</summary>
        public bool Shoots;

        /// <summary>Whether it is a gun (attack fires it), and where its muzzle is on the model.</summary>
        public bool Fires;
        public Vector3 Muzzle;
    }

    /// <summary>
    /// The look of every item, built in code from <see cref="MeshFactory"/> shapes and the shared
    /// materials. The same model is held in the hand and rendered into the inventory icon, so the
    /// two always match.
    /// </summary>
    public static class ItemModels
    {
        public static Dictionary<string, ItemModel> Create(MaterialLibrary m)
        {
            var models = new Dictionary<string, ItemModel>
            {
                [ItemIds.Rock] = new ItemModel
                {
                    Mesh = MeshFactory.HandRock(),
                    Materials = new[] { m.HandRock },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.34f, -0.34f, 0.62f),
                    HandRotation = Quaternion.Euler(10f, -20f, 15f),
                    HandScale = 0.12f,
                    IconRotation = Tilt(-25f, 30f),
                },
                [ItemIds.StoneHatchet] = Tool(MeshFactory.Hatchet(), new[] { m.Bark, m.Stone, m.Rope }),
                [ItemIds.StonePickaxe] = Tool(MeshFactory.Pickaxe(), new[] { m.Bark, m.Stone, m.Rope }),
                [ItemIds.Hammer] = Tool(MeshFactory.Hammer(), new[] { m.Bark, m.Stone, m.Rope }),
                [ItemIds.MetalHatchet] = Tool(MeshFactory.MetalHatchet(), new[] { m.Bark, m.Steel, m.Rope }),
                [ItemIds.MetalPickaxe] = Tool(MeshFactory.MetalPickaxe(), new[] { m.Bark, m.Steel, m.Rope }),
                [ItemIds.WoodenSpear] = new ItemModel
                {
                    Mesh = MeshFactory.WoodenSpear(),
                    Materials = new[] { m.Bark, m.Char, m.Rope },
                    ShowInHand = true,
                    Thrusts = true,
                    // Low on the right, the point ahead and a little left, into the middle of the view.
                    HandPosition = new Vector3(0.3f, -0.36f, 0.3f),
                    HandRotation = Grip(new Vector3(-0.2f, 0.16f, 1f), Vector3.up),
                    IconRotation = Diagonal(new Vector3(0.7f, 0.7f, 0f), new Vector3(0.7f, -0.7f, 0f)),
                    IconZoom = 1.5f,
                },
                [ItemIds.SheetMetalDoor] = new ItemModel
                {
                    Mesh = MeshFactory.MetalDoor(),
                    Materials = new[] { m.SheetMetal, m.Metal },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.24f, -0.56f, 0.72f),
                    HandRotation = Quaternion.Euler(0f, -28f, 0f),
                    HandScale = 0.22f,
                    IconRotation = Tilt(-10f, 25f),
                },
                [ItemIds.CodeLock] = new ItemModel
                {
                    Mesh = MeshFactory.CodeLock(),
                    Materials = new[] { m.LockBody, m.LockKeys, m.LedRed },
                    ShowInHand = true,
                    // Held up a little right of centre, face on, like a phone.
                    HandPosition = new Vector3(0.2f, -0.2f, 0.42f),
                    HandRotation = Quaternion.Euler(-12f, 160f, 0f),
                    HandScale = 1.1f,
                    IconRotation = Tilt(-12f, 200f),
                },
                [ItemIds.WoodenDoor] = new ItemModel
                {
                    Mesh = MeshFactory.Door(),
                    Materials = new[] { m.Door },
                    ShowInHand = true,
                    // A small door carried low on the right, edge on, so it hides little.
                    HandPosition = new Vector3(0.24f, -0.56f, 0.72f),
                    HandRotation = Quaternion.Euler(0f, -28f, 0f),
                    HandScale = 0.22f,
                    IconRotation = Tilt(-10f, 25f),
                },
                [ItemIds.Torch] = new ItemModel
                {
                    Mesh = MeshFactory.Torch(),
                    Materials = new[] { m.Bark, m.Char },
                    ShowInHand = true,
                    Burns = true,
                    FirePosition = new Vector3(0f, 0.41f, 0f),
                    HandPosition = new Vector3(0.36f, -0.50f, 0.50f),
                    HandRotation = Grip(new Vector3(-0.18f, 0.92f, 0.35f), Vector3.forward),
                    IconRotation = Diagonal(new Vector3(0.45f, 0.89f, 0f), new Vector3(0.89f, -0.45f, 0f)),
                    IconZoom = 1.35f,
                },
                [ItemIds.BuildingPlan] = new ItemModel
                {
                    Mesh = MeshFactory.BuildingPlan(),
                    Materials = new[] { m.Blueprint, m.Rope },
                    ShowInHand = true,
                    // Held low on the right, pointing away up the screen, so it hides little of the view.
                    HandPosition = new Vector3(0.32f, -0.36f, 0.72f),
                    HandRotation = Quaternion.FromToRotation(Vector3.right, new Vector3(-0.3f, 0.3f, 0.9f)),
                    HandScale = 0.8f,
                    IconRotation = Quaternion.AngleAxis(20f, Vector3.up) * Quaternion.FromToRotation(Vector3.right, new Vector3(0.85f, 0.5f, 0f)),
                },
                [ItemIds.Campfire] = new ItemModel
                {
                    Mesh = MeshFactory.Campfire(),
                    Materials = new[] { m.Stone, m.Bark, m.Char },
                    IconRotation = Tilt(-32f, 15f),
                },
                [ItemIds.StorageBox] = new ItemModel
                {
                    Mesh = MeshFactory.StorageBox(),
                    Materials = new[] { m.Planks, m.Bark, m.Metal },
                    IconRotation = Tilt(-24f, 210f),
                },
                [ItemIds.Furnace] = new ItemModel
                {
                    Mesh = MeshFactory.Furnace(),
                    Materials = new[] { m.Stone, m.Char, m.Metal },
                    IconRotation = Tilt(-18f, 200f),
                },
                [ItemIds.SleepingBag] = new ItemModel
                {
                    Mesh = MeshFactory.SleepingBag(),
                    Materials = new[] { m.Bedroll, m.Cloth, m.Rope },
                    IconRotation = Tilt(-48f, 140f),
                    IconZoom = 1.1f,
                },
                [ItemIds.ToolCupboard] = new ItemModel
                {
                    Mesh = MeshFactory.ToolCupboard(),
                    Materials = new[] { m.Planks, m.Bark, m.Metal },
                    IconRotation = Tilt(-14f, 205f),
                },
                [ItemIds.Workbench] = new ItemModel
                {
                    Mesh = MeshFactory.Workbench(),
                    Materials = new[] { m.Planks, m.Steel, m.Metal },
                    IconRotation = Tilt(-18f, 200f),
                },
                [ItemIds.Scrap] = new ItemModel
                {
                    Mesh = MeshFactory.ScrapPile(),
                    Materials = new[] { m.BarrelRust, m.Metal },
                    IconRotation = Tilt(-38f, 20f),
                },
                [ItemIds.Gears] = new ItemModel
                {
                    Mesh = MeshFactory.Gears(),
                    Materials = new[] { m.Steel },
                    IconRotation = Tilt(-55f, 15f),
                },
                [ItemIds.MetalSpring] = new ItemModel
                {
                    Mesh = MeshFactory.MetalSpring(),
                    Materials = new[] { m.Steel },
                    IconRotation = Tilt(-20f, 0f),
                },
                [ItemIds.MetalPipe] = new ItemModel
                {
                    Mesh = MeshFactory.MetalPipe(),
                    Materials = new[] { m.Metal },
                    IconRotation = Tilt(-25f, 35f),
                },
                [ItemIds.MetalFragments] = new ItemModel
                {
                    Mesh = MeshFactory.MetalScraps(),
                    Materials = new[] { m.Metal },
                    IconRotation = Tilt(-40f, 25f),
                },
                [ItemIds.Charcoal] = new ItemModel
                {
                    Mesh = MeshFactory.CharcoalLumps(),
                    Materials = new[] { m.Char },
                    IconRotation = Tilt(-35f, 15f),
                },
                [ItemIds.Wood] = new ItemModel
                {
                    Mesh = MeshFactory.WoodPile(),
                    Materials = new[] { m.Bark, m.Planks },
                    IconRotation = Tilt(-25f, 40f),
                },
                [ItemIds.Stone] = new ItemModel
                {
                    Mesh = MeshFactory.StonePile(3),
                    Materials = new[] { m.Stone },
                    IconRotation = Tilt(-30f, 20f),
                },
                [ItemIds.SulfurOre] = new ItemModel
                {
                    Mesh = MeshFactory.StonePile(19),
                    Materials = new[] { m.Sulfur },
                    IconRotation = Tilt(-30f, 40f),
                },
                [ItemIds.Sulfur] = new ItemModel
                {
                    Mesh = MeshFactory.PowderHeap(),
                    Materials = new[] { m.Sulfur },
                    IconRotation = Tilt(-34f, 20f),
                    IconZoom = 1.1f,
                },
                [ItemIds.Gunpowder] = new ItemModel
                {
                    Mesh = MeshFactory.PowderHeap(),
                    Materials = new[] { m.Gunpowder },
                    IconRotation = Tilt(-34f, 70f),
                    IconZoom = 1.1f,
                },
                [ItemIds.SatchelCharge] = new ItemModel
                {
                    Mesh = MeshFactory.SatchelCharge(),
                    Materials = new[] { m.Satchel, m.Rope, m.Metal, m.Char },
                    ShowInHand = true,
                    // Held low on the right by its handle, the fuse up, ready to throw.
                    HandPosition = new Vector3(0.32f, -0.37f, 0.52f),
                    HandRotation = Quaternion.Euler(8f, 205f, -6f),
                    HandScale = 1.2f,
                    IconRotation = Tilt(-14f, 200f),
                    IconZoom = 1.25f,
                },
                [ItemIds.MetalOre] = new ItemModel
                {
                    Mesh = MeshFactory.StonePile(8),
                    Materials = new[] { m.MetalOre },
                    IconRotation = Tilt(-30f, 60f),
                },
                [ItemIds.Berries] = new ItemModel
                {
                    Mesh = MeshFactory.BerryCluster(),
                    Materials = new[] { m.Berries, m.Leaves[0] },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.26f, -0.27f, 0.5f),
                    HandRotation = Quaternion.Euler(-35f, 25f, 0f),
                    HandScale = 1.3f,
                    IconRotation = Tilt(-40f, 20f),
                },
                [ItemIds.Cloth] = new ItemModel
                {
                    Mesh = MeshFactory.ClothRoll(),
                    Materials = new[] { m.Cloth, m.Rope },
                    IconRotation = Quaternion.AngleAxis(-20f, Vector3.right) * Quaternion.AngleAxis(-30f, Vector3.up),
                },
                [ItemIds.WoodenBow] = new ItemModel
                {
                    Mesh = MeshFactory.WoodenBow(true),
                    Materials = new[] { m.Bark, m.Cloth, m.Rope },
                    ShowInHand = true,
                    // Held out ahead and a little left, canted, the arrow along the crosshair when drawn.
                    HandPosition = new Vector3(-0.05f, -0.16f, 0.62f),
                    HandRotation = Quaternion.Euler(0f, 0f, 14f),
                    IconRotation = Quaternion.AngleAxis(-45f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.up),
                    IconZoom = 1.25f,
                    Shoots = true,
                },
                // Guns: modelled along +X, turned to point ahead, held low on the right like a hip shot.
                [ItemIds.Blunderbuss] = new ItemModel
                {
                    Mesh = MeshFactory.Blunderbuss(),
                    Materials = new[] { m.Planks, m.GunMetal, m.Rope, m.Char },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.19f, -0.2f, 0.36f),
                    HandRotation = Quaternion.Euler(0f, -91.5f, 0f),
                    IconRotation = Quaternion.AngleAxis(12f, Vector3.forward),
                    IconZoom = 1.15f,
                    Fires = true,
                    Muzzle = MeshFactory.BlunderbussMuzzle,
                },
                [ItemIds.Revolver] = new ItemModel
                {
                    Mesh = MeshFactory.Revolver(),
                    Materials = new[] { m.Planks, m.GunMetal, m.Brass, m.Char },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.16f, -0.17f, 0.34f),
                    HandRotation = Quaternion.Euler(0f, -92f, 0f),
                    IconRotation = Quaternion.AngleAxis(8f, Vector3.forward),
                    IconZoom = 1.1f,
                    Fires = true,
                    Muzzle = MeshFactory.RevolverMuzzle,
                },
                [ItemIds.SemiRifle] = new ItemModel
                {
                    Mesh = MeshFactory.SemiRifle(),
                    Materials = new[] { m.Planks, m.GunMetal, m.Brass, m.Char },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.18f, -0.2f, 0.3f),
                    HandRotation = Quaternion.Euler(0f, -91.5f, 0f),
                    IconRotation = Quaternion.AngleAxis(10f, Vector3.forward),
                    IconZoom = 1.2f,
                    Fires = true,
                    Muzzle = MeshFactory.RifleMuzzle,
                },
                [ItemIds.RifleBullet] = new ItemModel
                {
                    Mesh = MeshFactory.RifleRounds(),
                    Materials = new[] { m.Brass, m.Brass },
                    IconRotation = Tilt(-25f, 20f),
                },
                [ItemIds.Shell] = new ItemModel
                {
                    Mesh = MeshFactory.Shells(),
                    Materials = new[] { m.ShellPaper, m.Brass },
                    IconRotation = Tilt(-25f, 20f),
                },
                [ItemIds.PistolBullet] = new ItemModel
                {
                    Mesh = MeshFactory.PistolRounds(),
                    Materials = new[] { m.Brass, m.Metal },
                    IconRotation = Tilt(-25f, 20f),
                },
                [ItemIds.Bandage] = new ItemModel
                {
                    Mesh = MeshFactory.ClothRoll(),
                    Materials = new[] { m.Bandage, m.MedRed },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.24f, -0.25f, 0.46f),
                    HandRotation = Quaternion.Euler(-25f, 30f, 10f),
                    HandScale = 0.9f,
                    IconRotation = Quaternion.AngleAxis(-20f, Vector3.right) * Quaternion.AngleAxis(-30f, Vector3.up),
                },
                [ItemIds.MedicalSyringe] = new ItemModel
                {
                    Mesh = MeshFactory.Syringe(),
                    Materials = new[] { m.SyringeBarrel, m.Plastic, m.MedRed, m.Steel },
                    ShowInHand = true,
                    // Held like a dart, the needle ahead and a little down and in.
                    HandPosition = new Vector3(0.2f, -0.22f, 0.42f),
                    HandRotation = Quaternion.Euler(0f, -70f, -14f),
                    HandScale = 1.4f,
                    IconRotation = Quaternion.AngleAxis(35f, Vector3.forward) * Quaternion.AngleAxis(-15f, Vector3.right),
                    IconZoom = 1.3f,
                },
                [ItemIds.Arrow] = new ItemModel
                {
                    Mesh = MeshFactory.Arrow(),
                    Materials = new[] { m.Bark, m.Stone, m.Bone },
                    IconRotation = Quaternion.AngleAxis(-45f, Vector3.forward) * Quaternion.AngleAxis(-90f, Vector3.up),
                    IconZoom = 1.6f,
                },
                [ItemIds.Leather] = new ItemModel
                {
                    Mesh = MeshFactory.FoldedHide(),
                    Materials = new[] { m.Leather, m.Rope },
                    IconRotation = Tilt(-38f, 30f),
                },
                // Clothing, laid flat for its icon (never held in the hand).
                [ItemIds.BurlapShirt] = Clothing(MeshFactory.Garment(true, false), m.Cloth, m.Rope),
                [ItemIds.BurlapTrousers] = Clothing(MeshFactory.Trousers(), m.Cloth, m.Rope),
                [ItemIds.BurlapHeadwrap] = Clothing(MeshFactory.Headwear(false), m.Cloth, m.Rope, Tilt(-15f, 30f)),
                [ItemIds.HideVest] = Clothing(MeshFactory.Garment(false, true), m.Leather, m.Rope),
                [ItemIds.HidePants] = Clothing(MeshFactory.Trousers(), m.Leather, m.Rope),
                [ItemIds.HideHood] = Clothing(MeshFactory.Headwear(true), m.Leather, m.Rope, Tilt(-15f, 30f)),
                [ItemIds.HideBoots] = Clothing(MeshFactory.Boots(), m.Leather, m.Char, Tilt(-12f, 20f)),
                [ItemIds.RawMeat] = Meat(MeshFactory.MeatCut(), new[] { m.RawMeat, m.Bone }),
                [ItemIds.CookedMeat] = Meat(MeshFactory.MeatCut(), new[] { m.CookedMeat, m.Bone }),
            };
            return models;
        }

        /// <summary>A cut of meat held out low on the right, ready to eat; lying flat, bone to the left, in its icon.</summary>
        private static ItemModel Clothing(Mesh mesh, Material main, Material trim, Quaternion? pose = null) => new ItemModel
        {
            Mesh = mesh,
            Materials = new[] { main, trim },
            IconRotation = pose ?? Tilt(-8f, 12f),
        };

        private static ItemModel Meat(Mesh mesh, Material[] materials) => new ItemModel
        {
            Mesh = mesh,
            Materials = materials,
            ShowInHand = true,
            HandPosition = new Vector3(0.24f, -0.26f, 0.48f),
            HandRotation = Quaternion.Euler(-50f, 20f, 10f),
            HandScale = 1.2f,
            IconRotation = Tilt(-42f, 15f),
            IconZoom = 1.05f,
        };

        /// <summary>
        /// A hatchet or pickaxe: gripped low in the right hand, the haft leaning up, in and ahead, so
        /// the head sits right of and below the crosshair; head across the icon, haft from bottom left.
        /// </summary>
        private static ItemModel Tool(Mesh mesh, Material[] materials) => new ItemModel
        {
            Mesh = mesh,
            Materials = materials,
            ShowInHand = true,
            HandPosition = new Vector3(0.36f, -0.52f, 0.42f),
            HandRotation = Grip(new Vector3(-0.33f, 0.82f, 0.47f), new Vector3(-0.6f, 0f, 0.8f)),
            IconRotation = Diagonal(new Vector3(0.6f, 0.8f, 0f), new Vector3(0.8f, -0.6f, 0f)),
            IconZoom = 1.25f,
        };

        /// <summary>The rotation that turns a tool's haft (+Y) along <paramref name="haft"/> and its head (+X) towards <paramref name="head"/>.</summary>
        private static Quaternion Grip(Vector3 haft, Vector3 head)
        {
            var forward = Vector3.ProjectOnPlane(head, haft.normalized);
            // LookRotation sends +Z ahead and +Y up; the turn about Y first sends the head (+X) to +Z.
            return Quaternion.LookRotation(forward, haft) * Quaternion.Euler(0f, -90f, 0f);
        }

        /// <summary>A tool laid across the icon, turned a little so it shows some depth.</summary>
        private static Quaternion Diagonal(Vector3 haft, Vector3 head) =>
            Quaternion.AngleAxis(22f, Vector3.up) * Quaternion.AngleAxis(-12f, Vector3.right) * Grip(haft, head);

        /// <summary>Seen a little from above (<paramref name="pitch"/> below 0 tips the top towards the camera) and turned.</summary>
        private static Quaternion Tilt(float pitch, float yaw) =>
            Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
    }
}
