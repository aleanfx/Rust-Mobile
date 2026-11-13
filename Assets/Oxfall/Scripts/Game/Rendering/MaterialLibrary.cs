using UnityEngine;

namespace Oxfall.Game.Rendering
{
    /// <summary>
    /// Every material the game uses, made at start-up from one URP Lit material kept in Resources.
    /// </summary>
    /// <remarks>
    /// Shader.Find only works in a player build for shaders that something in the build references,
    /// so the editor setup saves <c>Resources/OxfallLit.mat</c> and everything here is a copy of it
    /// with a different colour or texture. Only colour, texture and smoothness change: none of those
    /// are shader keywords, so every copy uses variants the build already contains.
    /// </remarks>
    public sealed class MaterialLibrary
    {
        public const string BaseMaterialResource = "OxfallLit";
        public const string GroundMaterialResource = "OxfallGround";

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");

        private readonly Material _base;

        public Material Ground { get; private set; }
        public Material Water { get; }
        public Material Bark { get; }
        public Material[] Needles { get; }
        public Material[] Leaves { get; }
        public Material Stone { get; }
        public Material MetalOre { get; }
        public Material Hemp { get; }
        public Material HandRock { get; }
        public Material Planks { get; }
        public Material GhostOk { get; }
        public Material GhostBad { get; }

        public MaterialLibrary()
        {
            _base = Resources.Load<Material>(BaseMaterialResource);
            if (_base == null)
            {
                Debug.LogError($"[Oxfall] Resources/{BaseMaterialResource}.mat is missing; falling back to Shader.Find, which may render pink in a build.");
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _base = new Material(shader);
            }

            Water = Make("Water", new Color(0.10f, 0.30f, 0.36f), 0.86f);
            Bark = Make("Bark", new Color(0.33f, 0.24f, 0.17f), 0.05f);
            Needles = new[]
            {
                Make("Needles A", new Color(0.16f, 0.30f, 0.17f), 0.08f),
                Make("Needles B", new Color(0.20f, 0.34f, 0.18f), 0.08f),
            };
            Leaves = new[]
            {
                Make("Leaves A", new Color(0.30f, 0.45f, 0.18f), 0.08f),
                Make("Leaves B", new Color(0.40f, 0.48f, 0.20f), 0.08f),
            };
            Stone = Make("Stone", new Color(0.50f, 0.49f, 0.46f), 0.12f);
            MetalOre = Make("Metal ore", new Color(0.52f, 0.36f, 0.26f), 0.25f);
            Hemp = Make("Hemp", new Color(0.36f, 0.55f, 0.22f), 0.05f);
            HandRock = Make("Hand rock", new Color(0.55f, 0.53f, 0.50f), 0.15f);
            Planks = Make("Planks", new Color(0.60f, 0.45f, 0.28f), 0.08f);
            GhostOk = Make("Ghost ok", new Color(0.35f, 0.85f, 0.45f), 0.1f);
            GhostBad = Make("Ghost bad", new Color(0.90f, 0.30f, 0.25f), 0.1f);
        }

        /// <summary>The ground material, coloured by the island's colour map.</summary>
        public Material MakeGround(Texture2D colourMap)
        {
            // A copy of the ground material the editor setup saved, which adds close-up detail; the
            // plain material still works if it is missing.
            var detailed = Resources.Load<Material>(GroundMaterialResource);
            Ground = detailed != null ? new Material(detailed) { name = "Ground" } : Make("Ground", Color.white, 0.04f);
            Ground.SetTexture(BaseMap, colourMap);
            return Ground;
        }

        private Material Make(string name, Color colour, float smoothness)
        {
            var material = new Material(_base) { name = name };
            material.SetColor(BaseColor, colour);
            material.SetFloat(Smoothness, smoothness);
            material.SetFloat(Metallic, 0f);
            material.enableInstancing = true;
            return material;
        }
    }
}
