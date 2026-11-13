using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Environment;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Survival;
using Oxfall.Game.Audio;
using Oxfall.Game.Construction;
using Oxfall.Game.Controls;
using Oxfall.Game.Items;
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
        private Hotbar _hotbar;
        private InventoryUi _inventoryUi;
        private Gatherer _gatherer;
        private Hud _hud;
        private GraphicsQuality _quality;
        private GrassField _grass;
        private PlayerController _player;
        private RecipeBook _recipes;
        private CraftingQueue _crafting;
        private BuildSystem _build;
        private CampfirePlacer _campfires;
        private DayNight _dayNight;
        private Vitals _vitals;
        private VitalsUi _vitalsUi;
        private PlayerInput _input;
        private float _respawnTimer;
        private float _vitalsTimer;
        private bool _warnedHungry, _warnedThirsty;

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
            GraphicsQuality.ApplyFrameRate();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var materials = new MaterialLibrary();
            _world = WorldBuilder.Build(DefaultSeed, materials);
            Debug.Log($"[Oxfall] World built in {clock.ElapsedMilliseconds} ms: {_world.Nodes.Count} resources, " +
                      $"spawn {_world.SpawnPosition}, {SystemInfo.graphicsDeviceName}, {SystemInfo.systemMemorySize} MB RAM.");

            _catalog = ItemCatalog.CreateDefault();
            _inventory = new Inventory(_catalog);
            // What a fresh player starts with in the genre: a rock to gather with and a torch.
            _inventory.Add(ItemIds.Rock, 1);
            _inventory.Add(ItemIds.Torch, 1);
            _hotbar = new Hotbar(_inventory);
            var models = ItemModels.Create(materials);
            _vitals = new Vitals();

            var input = new PlayerInput();
            _input = input;

            // The player: a body that moves, a head that looks, the camera in the head.
            var playerObject = new GameObject("Player");
            playerObject.transform.position = _world.SpawnPosition;
            playerObject.AddComponent<CharacterController>();
            var player = playerObject.AddComponent<PlayerController>();
            player.Init(input, _world.SpawnYaw);
            _player = player;

            var camera = CreateCamera(player.Head);

            var gatherer = playerObject.AddComponent<Gatherer>();
            gatherer.Init(input, _inventory, _hotbar, _catalog, player.Head, models, materials.Flame, _vitals, _world.Shape);
            _gatherer = gatherer;

            _hud = Hud.Create();
            var controls = _hud.gameObject.AddComponent<TouchControls>();
            controls.Init(input, _hud);

            // The hotbar and the inventory panel; their icons are rendered from the item models.
            _inventoryUi = new InventoryUi(_hud, _inventory, _hotbar);
            controls.SlotKey += slot => _hotbar.Select(slot);
            controls.InventoryKey += _inventoryUi.Toggle;
            ItemIcons.Render(models, _world.Sun).Ready += _inventoryUi.Refresh;

            // Sounds, all made at start-up.
            var audio = GameAudio.Create(playerObject.transform, _world.Shape);
            gatherer.Sound += audio.Play;
            gatherer.FireSound = audio.Bank.FireLoop;
            controls.Tapped += () => audio.Play(SoundCue.Tap);

            // Health, food and water, and what happens on death.
            _vitalsUi = new VitalsUi(_hud, _vitals);
            _vitals.Died += OnDied;
            _vitalsUi.WakeUpTapped += WakeUp;

            // The clock: the island starts in the morning; a day lasts 30 minutes and a night 10.
            _dayNight = _world.Root.gameObject.AddComponent<DayNight>();
            _dayNight.ClockChanged += _hud.SetClock;
            _dayNight.Init(new DayCycle(9f), _world.Sun, camera);

            // Grass around the player; how far and how thick depends on the graphics level.
            var grass = _world.Root.gameObject.AddComponent<GrassField>();
            grass.Init(_world.Shape, materials.Grass, materials.GrassIsCards, player.transform);
            _grass = grass;

            _quality = new GraphicsQuality(camera, _world.Sun, grass, materials.Ground);
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
            // Crafting: the Core queue takes the ingredients up front and hands the item over when done.
            _recipes = RecipeBook.CreateDefault(_catalog);
            _crafting = new CraftingQueue(_inventory);
            _crafting.Completed += (recipe, amount) =>
            {
                audio.Play(SoundCue.Crafted);
                var made = _catalog.Get(recipe.OutputItemId);
                bool onBar = false;
                for (int i = 0; i < Inventory.HotbarSize; i++) onBar |= _inventory[i].ItemId == made.Id;
                _hud.ShowToast("Fabricado: " + made.DisplayName + (onBar && made.Category != ItemCategory.Resource
                    ? " (tócalo en la barra para usarlo)" : ""));
                Debug.Log("[Oxfall] Crafted " + made.DisplayName);
            };
            _crafting.Overflow = (itemId, amount) => _hud.ShowToast("Inventario lleno");
            _hud.BuildCraftPanel(_recipes, _catalog, _inventory, TryCraft);

            // Building: the Core grid holds what stands where; BuildSystem aims, checks terrain and draws.
            // Holding the building plan is building mode.
            _build = playerObject.AddComponent<BuildSystem>();
            _build.Init(new BuildingGrid(), input, _inventory, _world.Shape, player, materials, _hud);
            _hud.PieceTapped += _build.CyclePiece;
            _build.Placed += () => audio.Play(SoundCue.Place);

            // Holding a campfire shows where it would go; attack puts it down.
            var campfire = models[ItemIds.Campfire];
            _campfires = playerObject.AddComponent<CampfirePlacer>();
            _campfires.Init(input, _inventory, player, materials, _hud, campfire.Mesh, campfire.Materials);
            _campfires.Placed += fire =>
            {
                audio.Play(SoundCue.Place);
                GameAudio.Attach(fire, audio.Bank.FireLoop, 0.7f, 16f);
            };

            _inventory.Changed += _ =>
            {
                _hud.RefreshResources(_inventory, _catalog);
                _hud.RefreshCraftPanel();
                OnHeldChanged();
            };
            _hotbar.SelectionChanged += _ => OnHeldChanged();
            _hud.RefreshResources(_inventory, _catalog);
            _hud.ShowToast("Bienvenido a Oxfall: golpea árboles y rocas");
        }

        /// <summary>The inventory or the hotbar choice changed: redraw, and update the hand and building mode.</summary>
        private void OnHeldChanged()
        {
            _inventoryUi.Refresh();
            _gatherer.RefreshHeld();
            // Leave the old mode before entering the new one: both drive the attack button's label.
            bool plan = _hotbar.IsHolding(ItemIds.BuildingPlan);
            bool fire = _hotbar.IsHolding(ItemIds.Campfire);
            if (!plan) _build.SetActive(false);
            if (!fire) _campfires.SetActive(false);
            if (plan) _build.SetActive(true);
            if (fire) _campfires.SetActive(true);
            bool food = _hotbar.HeldDefinition?.Category == ItemCategory.Food;
            _hud.SetAttackLabel(plan || fire ? "COLOCAR" : food ? "COMER" : "GOLPEAR");
        }

        private void OnDied()
        {
            Debug.Log("[Oxfall] The player died.");
            _inventoryUi.Close();
            _hud.CloseCraftPanel();
            _player.enabled = false;
            _gatherer.enabled = false;
            _vitalsUi.ShowDeath(true);
        }

        /// <summary>
        /// Wakes up on the beach as a fresh player: back to a rock and a torch. (Later the old
        /// inventory will lie in a bag where the player fell.)
        /// </summary>
        private void WakeUp()
        {
            _inventory.Clear();
            _inventory.Add(ItemIds.Rock, 1);
            _inventory.Add(ItemIds.Torch, 1);
            if (_hotbar.Selected != 0) _hotbar.Select(0);
            _vitals.Respawn();
            _warnedHungry = _warnedThirsty = false;
            _player.enabled = true;
            _gatherer.enabled = true;
            _player.Teleport(_world.SpawnPosition, _world.SpawnYaw, 0f);
            _vitalsUi.ShowDeath(false);
            _vitalsUi.Refresh();
            _hud.ShowToast("Has despertado en la playa");
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
                case "view-hill": ViewFromHill(); break;
                case "view-forest": ViewInForest(); break;
                case "view-sea":
                    // From the spawn on the beach, out to sea and a little down, so the near water shows.
                    _player.Teleport(_world.SpawnPosition, _world.SpawnYaw + 180f, 14f);
                    break;
                case "nearest-stone": FaceNearest(ResourceNodeType.Stone); break;
                case "nearest-ore": FaceNearest(ResourceNodeType.MetalOre); break;
                case "nearest-hemp": FaceNearest(ResourceNodeType.Hemp); break;
                case "nearest-berries": FaceNearest(ResourceNodeType.BerryBush); break;
                case "quality-low": SetQuality(QualityTier.Low); break;
                case "quality-medium": SetQuality(QualityTier.Medium); break;
                case "quality-high": SetQuality(QualityTier.High); break;
                case "craft-hatchet": TryCraft(_recipes.Get("stone_hatchet")); break;
                case "craft-pickaxe": TryCraft(_recipes.Get("stone_pickaxe")); break;
                case "craft-panel": _hud.ToggleCraftPanel(); break;
                case "give":
                    _inventory.Add(ItemIds.Wood, 1000);
                    _inventory.Add(ItemIds.Stone, 1000);
                    _inventory.Add(ItemIds.BuildingPlan, 1);
                    break;
                case "give-all":
                    _inventory.Add(ItemIds.StoneHatchet, 1);
                    _inventory.Add(ItemIds.StonePickaxe, 1);
                    _inventory.Add(ItemIds.BuildingPlan, 1);
                    _inventory.Add(ItemIds.Campfire, 2);
                    _inventory.Add(ItemIds.Wood, 1500);
                    _inventory.Add(ItemIds.Stone, 700);
                    _inventory.Add(ItemIds.MetalOre, 240);
                    _inventory.Add(ItemIds.Cloth, 40);
                    break;
                case "build": _hotbar.SelectItem(ItemIds.BuildingPlan); break;
                case "piece": _build.CyclePiece(); break;
                case "place": _build.Place(); break;
                case "inventory": _inventoryUi.Toggle(); break;
                case "place-fire": _campfires.Place(); break;
                case "starve": _vitals.Damage(1000f); break;
                case "use": _gatherer.UseOnce(); break;
                case "wake": WakeUp(); break;
                case "hungry": _vitals.Respawn(40f, 30f, 20f); _vitalsUi.Refresh(); break;
                case "view-pond":
                    if (_world.Shape.Ponds.Count > 0)
                    {
                        var pond = _world.Shape.Ponds[0];
                        var edge = pond.Centre + new Vector2(pond.Radius + 1.2f, 0f);
                        var feet = new Vector3(edge.x, _world.Shape.Height(edge.x, edge.y) + 0.05f, edge.y);
                        _player.Teleport(feet, -90f, 35f);
                    }
                    break;
                case "stats":
                    Debug.Log($"[Oxfall] Stats: wood={_inventory.Count(ItemIds.Wood)} stone={_inventory.Count(ItemIds.Stone)} " +
                              $"ore={_inventory.Count(ItemIds.MetalOre)} cloth={_inventory.Count(ItemIds.Cloth)} " +
                              $"hatchet={_inventory.Count(ItemIds.StoneHatchet)} pickaxe={_inventory.Count(ItemIds.StonePickaxe)} " +
                              $"held={_hotbar.Held} building={_build.Active} campfires={_campfires.PlacedCount} " +
                              $"time={_dayNight.Cycle.Clock} health={_vitals.Health:0} food={_vitals.Food:0} water={_vitals.Water:0} " +
                              $"ponds={_world.Shape.Ponds.Count} berries={_inventory.Count(ItemIds.Berries)} " +
                              $"pos={_player.transform.position} quality={GraphicsQuality.Name(_quality.Tier)} " +
                              $"screen={Screen.width}x{Screen.height} grass={_grass.BladeCount}");
                    break;
                default:
                    // slot-N chooses hotbar slot N (1 to 6); tap-N taps inventory slot N (0 to 29).
                    if (command.StartsWith("slot-") && int.TryParse(command.Substring(5), out int slot))
                        _hotbar.Select(slot - 1);
                    else if (command.StartsWith("tap-") && int.TryParse(command.Substring(4), out int tap))
                        _inventoryUi.TapSlot(tap);
                    // time-H jumps to hour H (0 to 23).
                    else if (command.StartsWith("time-") && float.TryParse(command.Substring(5),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hour))
                        _dayNight.SetHour(hour);
                    else
                        Debug.LogWarning("[Oxfall] Unknown debug command: " + command);
                    break;
            }
        }

        private void TryCraft(Recipe recipe)
        {
            string item = _catalog.Get(recipe.OutputItemId).DisplayName;
            switch (_crafting.Enqueue(recipe))
            {
                case CraftingQueue.EnqueueResult.Queued:
                    _hud.ShowToast($"Fabricando {item}… {recipe.CraftSeconds:0} s");
                    break;
                case CraftingQueue.EnqueueResult.MissingIngredients:
                    _hud.ShowToast("Faltan materiales para " + item);
                    break;
                default:
                    _hud.ShowToast("La cola de fabricación está llena");
                    break;
            }
        }

        private void SetQuality(QualityTier tier)
        {
            _quality.Apply(tier);
            _hud.SetQualityLabel(GraphicsQuality.Name(tier));
        }

        /// <summary>Stands the player a step from the nearest resource of a kind, looking at it.</summary>
        /// <summary>Test view: the highest ground near the middle, looking out over the island.</summary>
        private void ViewFromHill()
        {
            var shape = _world.Shape;
            var best = Vector3.zero;
            for (float z = -120f; z <= 120f; z += 6f)
            for (float x = -120f; x <= 120f; x += 6f)
            {
                float h = shape.Height(x, z);
                if (h > best.y) best = new Vector3(x, h, z);
            }
            float yaw = Mathf.Atan2(-best.x, -best.z) * Mathf.Rad2Deg + 180f;
            _player.Teleport(best + Vector3.up * 0.3f, yaw, 8f);
            Debug.Log($"[Oxfall] View from the hill at {best}");
        }

        /// <summary>Test view: standing in grass at the edge of a forest, looking along the ground.</summary>
        private void ViewInForest()
        {
            var shape = _world.Shape;
            var here = _player.transform.position;
            var best = here;
            float bestScore = float.MinValue;
            for (float z = -150f; z <= 150f; z += 5f)
            for (float x = -150f; x <= 150f; x += 5f)
            {
                float h = shape.Height(x, z);
                float score = shape.GrassAmount(x, z, h) + shape.Forest(x, z) * 0.6f
                              - Mathf.Abs(shape.Forest(x, z) - 0.5f) - new Vector2(x - here.x, z - here.z).magnitude * 0.002f;
                if (score > bestScore) { bestScore = score; best = new Vector3(x, h, z); }
            }
            _player.Teleport(best + Vector3.up * 0.3f, 210f, 2f);
            Debug.Log($"[Oxfall] View in the forest at {best}");
        }

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
            camera.cullingMask = ~(1 << ItemIcons.Layer);
            go.AddComponent<AudioListener>();
            return camera;
        }

        /// <summary>Food and water run down (faster when sprinting); warns once each when one runs low.</summary>
        private void TickVitals(float dt)
        {
            if (_vitals == null || _vitals.IsDead) return;
            bool running = _input.Sprint && _input.Move.sqrMagnitude > 0.01f;
            _vitals.Tick(dt, running ? 1.8f : 1f);
            _vitalsTimer -= dt;
            if (_vitalsTimer > 0f) return;
            _vitalsTimer = 0.5f;
            _vitalsUi.Refresh();
            if (_vitals.Food01 < 0.12f && !_warnedHungry) _hud.ShowToast("Tienes hambre: busca moras");
            if (_vitals.Water01 < 0.12f && !_warnedThirsty) _hud.ShowToast("Tienes sed: bebe en una charca");
            _warnedHungry = _vitals.Food01 < 0.12f;
            _warnedThirsty = _vitals.Water01 < 0.12f;
        }

        private void Update()
        {
            TickVitals(Time.deltaTime);

            // Trees and rocks grow back on their own timers; checking once a second is plenty.
            _crafting?.Tick(Time.deltaTime);
            _respawnTimer += Time.deltaTime;
            if (_respawnTimer < RespawnTickSeconds || _world == null) return;
            foreach (var node in _world.Nodes) node.TickRespawn(_respawnTimer);
            _respawnTimer = 0f;
        }
    }
}
