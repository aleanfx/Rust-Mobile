using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Game.Controls;
using Oxfall.Game.Player;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game
{
    /// <summary>
    /// Builds and runs a game session: the world, the player, the controls and the HUD. Everything
    /// is made here in code, so the scene file stays empty and there is nothing to set up by hand.
    /// </summary>
    public sealed class OxfallGame : MonoBehaviour
    {
        /// <summary>The island's seed. A weekly wipe will hand out a new one from the server.</summary>
        public const int DefaultSeed = 20261113;

        private const float RespawnTickSeconds = 1f;

        private WorldInstance _world;
        private ItemCatalog _catalog;
        private Inventory _inventory;
        private Hud _hud;
        private GraphicsQuality _quality;
        private PlayerController _player;
        private float _respawnTimer;

        /// <summary>
        /// Runs on its own once the first scene has loaded. No scene object or reference is needed,
        /// so a scene can never lose its link to this script.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<OxfallGame>() != null) return;
            new GameObject("Oxfall").AddComponent<OxfallGame>();
        }

        private void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var materials = new MaterialLibrary();
            _world = WorldBuilder.Build(DefaultSeed, materials);
            Debug.Log($"[Oxfall] World built in {clock.ElapsedMilliseconds} ms: {_world.Nodes.Count} resources, " +
                      $"spawn {_world.SpawnPosition}, {SystemInfo.graphicsDeviceName}, {SystemInfo.systemMemorySize} MB RAM.");

            _catalog = ItemCatalog.CreateDefault();
            _inventory = new Inventory(_catalog);
            _inventory.Add(ItemIds.Rock, 1);

            var input = new PlayerInput();

            // The player: a body that moves, a head that looks, the camera in the head.
            var playerObject = new GameObject("Player");
            playerObject.transform.position = _world.SpawnPosition;
            playerObject.AddComponent<CharacterController>();
            var player = playerObject.AddComponent<PlayerController>();
            player.Init(input, _world.SpawnYaw);
            _player = player;

            var camera = CreateCamera(player.Head);

            var gatherer = playerObject.AddComponent<Gatherer>();
            gatherer.Init(input, _inventory, _catalog, player.Head, MeshFactory.HandRock(), materials.HandRock);

            _hud = Hud.Create();
            var controls = _hud.gameObject.AddComponent<TouchControls>();
            controls.Init(input, _hud);

            _quality = new GraphicsQuality(camera, _world.Sun);
            _hud.SetQualityLabel(GraphicsQuality.Name(_quality.Tier));
            controls.QualityTapped += () =>
            {
                _quality.Next();
                _hud.SetQualityLabel(GraphicsQuality.Name(_quality.Tier));
                _hud.ShowToast("Gráficos: " + GraphicsQuality.Name(_quality.Tier));
            };

            gatherer.Message += message =>
            {
                _hud.ShowToast(message);
                Debug.Log("[Oxfall] Gathered " + message);
            };
            _inventory.Changed += _ => _hud.RefreshResources(_inventory, _catalog);
            _hud.RefreshResources(_inventory, _catalog);
            _hud.ShowToast("Bienvenido a Oxfall: golpea árboles y rocas");
        }

        /// <summary>
        /// Commands for automated tests of the web build, sent from the page with
        /// <c>unityInstance.SendMessage('Oxfall', 'DebugCommand', 'nearest-tree')</c>. Nothing in the
        /// game calls it, and only the page hosting the game can.
        /// </summary>
        public void DebugCommand(string command)
        {
            switch (command)
            {
                case "nearest-tree": FaceNearest(ResourceNodeType.Tree); break;
                case "nearest-stone": FaceNearest(ResourceNodeType.Stone); break;
                case "nearest-ore": FaceNearest(ResourceNodeType.MetalOre); break;
                case "nearest-hemp": FaceNearest(ResourceNodeType.Hemp); break;
                case "quality-low": SetQuality(QualityTier.Low); break;
                case "quality-medium": SetQuality(QualityTier.Medium); break;
                case "quality-high": SetQuality(QualityTier.High); break;
                case "stats":
                    Debug.Log($"[Oxfall] Stats: wood={_inventory.Count(ItemIds.Wood)} stone={_inventory.Count(ItemIds.Stone)} " +
                              $"ore={_inventory.Count(ItemIds.MetalOre)} cloth={_inventory.Count(ItemIds.Cloth)} " +
                              $"pos={_player.transform.position} quality={GraphicsQuality.Name(_quality.Tier)} " +
                              $"screen={Screen.width}x{Screen.height}");
                    break;
                default:
                    Debug.LogWarning("[Oxfall] Unknown debug command: " + command);
                    break;
            }
        }

        private void SetQuality(QualityTier tier)
        {
            _quality.Apply(tier);
            _hud.SetQualityLabel(GraphicsQuality.Name(tier));
        }

        /// <summary>Stands the player a step from the nearest resource of a kind, looking at it.</summary>
        private void FaceNearest(ResourceNodeType type)
        {
            ResourceNodeView best = null;
            float bestDistance = float.MaxValue;
            var here = _player.transform.position;
            foreach (var node in _world.Nodes)
            {
                if (node.Node.Type != type || node.Node.IsDepleted) continue;
                float d = (node.transform.position - here).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = node; }
            }
            if (best == null)
            {
                Debug.LogWarning($"[Oxfall] No {type} left to face.");
                return;
            }

            var target = best.GetComponent<Collider>().bounds.center;
            var away = here - best.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away = away.normalized;
            var feet = best.transform.position + away * 2.0f;
            feet.y = _world.Shape.Height(feet.x, feet.z) + 0.05f;

            var toTarget = target - (feet + Vector3.up * PlayerController.EyeHeight);
            float yaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg;
            _player.Teleport(feet, yaw, pitch);
            Debug.Log($"[Oxfall] Facing {best.name} at {best.transform.position}");
        }

        private static Camera CreateCamera(Transform head)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(head, false);
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 300f;
            camera.fieldOfView = 70f;
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = WorldBuilder.FogColour;
            go.AddComponent<AudioListener>();
            return camera;
        }

        private void Update()
        {
            // Trees and rocks grow back on their own timers; checking once a second is plenty.
            _respawnTimer += Time.deltaTime;
            if (_respawnTimer < RespawnTickSeconds || _world == null) return;
            foreach (var node in _world.Nodes) node.TickRespawn(_respawnTimer);
            _respawnTimer = 0f;
        }
    }
}
