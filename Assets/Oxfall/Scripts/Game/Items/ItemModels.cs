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
                [ItemIds.Torch] = new ItemModel
                {
                    Mesh = MeshFactory.Torch(),
                    Materials = new[] { m.Bark, m.Char },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.36f, -0.50f, 0.50f),
                    HandRotation = Grip(new Vector3(-0.18f, 0.92f, 0.35f), Vector3.forward),
                    IconRotation = Diagonal(new Vector3(0.45f, 0.89f, 0f), new Vector3(0.89f, -0.45f, 0f)),
                },
                [ItemIds.BuildingPlan] = new ItemModel
                {
                    Mesh = MeshFactory.BuildingPlan(),
                    Materials = new[] { m.Blueprint, m.Rope },
                    ShowInHand = true,
                    HandPosition = new Vector3(0.30f, -0.33f, 0.60f),
                    HandRotation = Quaternion.FromToRotation(Vector3.right, new Vector3(-0.55f, 0.55f, 0.62f)),
                    IconRotation = Quaternion.AngleAxis(20f, Vector3.up) * Quaternion.FromToRotation(Vector3.right, new Vector3(0.85f, 0.5f, 0f)),
                },
                [ItemIds.Campfire] = new ItemModel
                {
                    Mesh = MeshFactory.Campfire(),
                    Materials = new[] { m.Stone, m.Bark, m.Char },
                    IconRotation = Tilt(-32f, 15f),
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
                [ItemIds.MetalOre] = new ItemModel
                {
                    Mesh = MeshFactory.StonePile(8),
                    Materials = new[] { m.MetalOre },
                    IconRotation = Tilt(-30f, 60f),
                },
                [ItemIds.Cloth] = new ItemModel
                {
                    Mesh = MeshFactory.ClothRoll(),
                    Materials = new[] { m.Cloth, m.Rope },
                    IconRotation = Quaternion.AngleAxis(-20f, Vector3.right) * Quaternion.AngleAxis(-30f, Vector3.up),
                },
            };
            return models;
        }

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
