using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Environment;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;
using Oxfall.Game.Audio;
using Oxfall.Game.Construction;
using Oxfall.Game.Controls;
using Oxfall.Game.Items;
using Oxfall.Game.Net;
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
        private BuildingViews _buildings;
        private BuildSystem _build;
        private HammerTool _hammer;
        private DoorPlacer _doorPlacer;
        private Interactor _interactor;
        private DeployablePlacer _placer;
        private DeployableViews _deployables;
        private CupboardUi _cupboard;
        private LockViews _lockViews;
        private LockPlacer _lockPlacer;
        private ChargeThrower _thrower;
        private Explosions _explosions;
        /// <summary>When the last blast went off (a hurt just after one is the blast's).</summary>
        private float _lastBlast = -10f;
        // Guns: who fired last and when (for "te dispara"), and the attack label last shown for the gun in hand.
        private readonly System.Collections.Generic.Dictionary<ushort, float> _firedAt = new System.Collections.Generic.Dictionary<ushort, float>();
        private string _gunLabel;
        private KeypadUi _keypad;
        private GameAudio _audio;
        private DayNight _dayNight;
        private Vitals _vitals;
        private VitalsUi _vitalsUi;
        private PlayerInput _input;
        private NetClient _net;
        private Survivor _survivor;
        private Settlement _settlement;
        private GameActions _actions;
        private bool _quietPickups;
        private NameTags _nameTags;
        private Camera _camera;
        private int _shownOnline = -1;
        private float _respawnTimer;
        private float _vitalsTimer;
        private bool _warnedHungry, _warnedThirsty;
        // How many of each item the player had, to show what was just picked up or made.
        private readonly System.Collections.Generic.Dictionary<string, int> _owned =
            new System.Collections.Generic.Dictionary<string, int>();
        private readonly System.Collections.Generic.Dictionary<string, int> _counting =
            new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// Runs on its own once the first scene has loaded. No scene object or reference is needed,
        /// so a scene can never lose its link to this script.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // Also after the scene is loaded again (to build another island: see OnWelcomed).
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Spawn();
            Spawn();
        }

        private static void Spawn()
        {
            if (FindAnyObjectByType<OxfallGame>() != null) return;
            new GameObject("Oxfall").AddComponent<OxfallGame>();
        }

        // Carried over a scene reload when the server's island is another seed (a new wipe).
        private static int s_seed = DefaultSeed;
        private static string s_server;

        private TitleScreen _title;
        private bool _menuView;
        private float _menuAngle;
        private float _gameFog, _gameFar;
        // Who dealt the last blow that killed this player (online), for the death screen.
        private string _killer;

        // The island's animals: alone the game runs them, online the server does and says where they are.
        private Fauna _fauna;
        private AnimalViews _animalViews;
        private SleeperViews _sleeperViews;
        private readonly System.Collections.Generic.List<AnimalSnap> _animalSnaps = new System.Collections.Generic.List<AnimalSnap>();
        private readonly System.Collections.Generic.List<Fauna.Prey> _prey = new System.Collections.Generic.List<Fauna.Prey>();
        private readonly System.Collections.Generic.List<Fauna.Bite> _bites = new System.Collections.Generic.List<Fauna.Bite>();
        private float _bladeHintAt;
        private ArrowFlights _arrows;
        private ChatUi _chat;
        private float _onlineSince;

        private void Start()
        {
            GraphicsQuality.ApplyFrameRate();
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var materials = new MaterialLibrary();
            _world = WorldBuilder.Build(s_seed, materials);
            Debug.Log($"[Oxfall] World built in {clock.ElapsedMilliseconds} ms: {_world.Nodes.Count} resources, " +
                      $"spawn {_world.SpawnPosition}, {SystemInfo.graphicsDeviceName}, {SystemInfo.systemMemorySize} MB RAM.");

            // The player's survivor (a rock and a torch to start) and what stands on the island. Alone
            // they are the real thing; online they are copies the server keeps up to date.
            _catalog = ItemCatalog.CreateDefault();
            _survivor = new Survivor(_catalog);
            _inventory = _survivor.Inventory;
            _vitals = _survivor.Vitals;
            // Alone, this copy decides what a blast destroys; online the server does.
            _settlement = new Settlement(_world.Shape.Ground, _catalog) { DropsLoot = true };
            _hotbar = new Hotbar(_inventory);
            var models = ItemModels.Create(materials);

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
            _camera = camera;

            // Every change the player makes goes through the actions: the shared rules alone, the server online.
            _net = gameObject.AddComponent<NetClient>();
            _actions = new GameActions(_survivor, _settlement, playerObject.transform, _net);

            var gatherer = playerObject.AddComponent<Gatherer>();
            gatherer.Init(input, _inventory, _hotbar, _catalog, player.Head, models, materials.Flame, _vitals, _world.Shape);
            _gatherer = gatherer;
            gatherer.Actions = _actions;

            // Boars, wolves and deer: walking round trunks and rocks, kept out of bases.
            var ground = _world.Shape.Ground;
            _fauna = new Fauna(ground, s_seed) { Blocked = Fauna.Barriers(_settlement.Grid, new Obstacles(ResourceLayout.Generate(ground))) };
            _fauna.Populate();
            _actions.Fauna = _fauna;
            _animalViews = new AnimalViews(materials, (x, z) => _world.Shape.Height(x, z));
            _sleeperViews = new SleeperViews(materials);
            gatherer.StrikeAnimal = _actions.StrikeAnimal;

            // Arrows in flight, this player's and everyone else's.
            var arrowMaterials = new[] { materials.Bark, materials.Stone, materials.Bone };
            _arrows = ArrowFlights.Create(MeshFactory.Arrow(), arrowMaterials);
            gatherer.Arrows = _arrows;
            gatherer.StringMaterial = materials.Rope;
            gatherer.ArrowMaterials = arrowMaterials;
            _actions.AnimalHit += OnAnimalHit;

            _hud = Hud.Create();
            _hud.HeadingSource = player.Head;
            var controls = _hud.gameObject.AddComponent<TouchControls>();
            controls.Init(input, _hud);

            // The hotbar and the inventory panel; their icons are rendered from the item models.
            _inventoryUi = new InventoryUi(_hud, _inventory, _hotbar, _actions, _settlement);
            // The number keys type on an open keypad instead.
            controls.SlotKey += slot =>
            {
                if (_keypad == null || !_keypad.IsOpen) _hotbar.Select(slot);
            };
            controls.InventoryKey += _inventoryUi.Toggle;
            ItemIcons.Render(models, _world.Sun).Ready += () =>
            {
                _inventoryUi.Refresh();
                _hud.RefreshCraftPanel();
                OnHeldChanged();
            };

            // Sounds, all made at start-up.
            var audio = GameAudio.Create(playerObject.transform, _world.Shape);
            _audio = audio;
            gatherer.Sound += audio.Play;
            _animalViews.Called += audio.PlayAnimal;
            gatherer.FireSound = audio.Bank.FireLoop;
            controls.Tapped += () => audio.Play(SoundCue.Tap);

            // Health, food and water, and what happens on death.
            _vitalsUi = new VitalsUi(_hud, _vitals);
            _vitals.Died += OnDied;
            _vitalsUi.WakeUpTapped += WakeUp;
            _vitalsUi.BagTapped += WakeUpInBag;

            // The map, painted in the background a few rows a frame; the compass opens it.
            _map = new MapUi(_hud, (RectTransform)_hud.transform, _world.Shape);
            StartCoroutine(_map.Paint());
            _hud.MapTapped += _map.Toggle;

            // The clock: the island starts in the morning; a day lasts 30 minutes and a night 10.
            _dayNight = _world.Root.gameObject.AddComponent<DayNight>();
            _dayNight.ClockChanged += _hud.SetClock;
            _dayNight.Init(new DayCycle(9f), _world.Sun, camera);

            // Grass around the player; how far and how thick depends on the graphics level.
            var grass = _world.Root.gameObject.AddComponent<GrassField>();
            grass.Init(_world.Shape, materials.Grass, materials.GrassIsCards, player.transform);
            _grass = grass;

            _quality = new GraphicsQuality(camera, _world.Sun, grass, materials.Ground);
            _hud.SetQuality((int)_quality.Tier);
            controls.QualityTapped += () =>
            {
                _quality.Next();
                _hud.SetQuality((int)_quality.Tier);
                _hud.ShowToast("Gráficos: " + GraphicsQuality.Name(_quality.Tier));
            };
            _hud.QualityChosen += tier =>
            {
                SetQuality((QualityTier)tier);
                _hud.ShowToast("Gráficos: " + GraphicsQuality.Name(_quality.Tier));
            };

            gatherer.Message += message =>
            {
                _hud.ShowToast(message);
                Debug.Log("[Oxfall] Gatherer: " + message);
            };
            // Crafting: the Core queue takes the ingredients up front and hands the item over when done.
            _recipes = RecipeBook.CreateDefault(_catalog);
            _crafting = _survivor.Crafting;
            void Crafted(Recipe recipe, int lost)
            {
                audio.Play(SoundCue.Crafted);
                var made = _catalog.Get(recipe.OutputItemId);
                bool onBar = false;
                for (int i = 0; i < Inventory.HotbarSize; i++) onBar |= _inventory[i].ItemId == made.Id;
                if (lost > 0) _hud.ShowToast("Inventario lleno: " + made.DisplayName + " se perdió");
                else if (onBar && made.Category != ItemCategory.Resource)
                    _hud.ShowToast(made.DisplayName + ": tócalo en la barra para usarlo");
                Debug.Log("[Oxfall] Crafted " + made.DisplayName);
            }
            _crafting.Completed += Crafted;
            _crafting.Overflow = (itemId, amount) => _hud.ShowToast("Inventario lleno");
            _hud.BuildCraftPanel(_recipes, _catalog, _inventory, _crafting, TryCraft);

            // Building: the Core grid holds what stands where and BuildingViews draws it. The plan in
            // hand places twig pieces, the hammer upgrades and demolishes them, a door in hand hangs it.
            var grid = _settlement.Grid;
            _buildings = new BuildingViews(grid, materials) { Viewer = player.transform };
            _build = playerObject.AddComponent<BuildSystem>();
            _build.Init(_buildings, input, _inventory, _world.Shape, player, materials, _hud);
            _build.Actions = _actions;
            _build.Settlement = _settlement;
            _build.Placed += () => audio.Play(SoundCue.Place);
            _hammer = playerObject.AddComponent<HammerTool>();
            _hammer.Init(grid, input, _inventory, _catalog, player.Head, _hud);
            _hammer.Actions = _actions;
            _hammer.Settlement = _settlement;
            _hammer.Upgraded += () => { audio.Play(SoundCue.Place); _gatherer.PlaySwing(); };
            _hammer.Demolished += () => audio.Play(SoundCue.Wood);
            _hud.PieceTapped += () =>
            {
                if (_hammer.Active) _hammer.Demolish();
                else if (_build.Active) _build.CyclePiece();
            };
            _doorPlacer = playerObject.AddComponent<DoorPlacer>();
            _doorPlacer.Init(_buildings, input, _inventory, player.Head, _hud, materials);
            _doorPlacer.Actions = _actions;
            _doorPlacer.Placed += () => audio.Play(SoundCue.Place);

            // Things set down (campfires, boxes, furnaces, sleeping bags, tool cupboards): drawn from
            // the settlement; holding one shows where it would go and attack puts it down.
            _deployables = new DeployableViews(_settlement, models, materials);
            _deployables.FireStarted += fire => GameAudio.Attach(fire, audio.Bank.FireLoop, 0.7f, 16f);
            _placer = playerObject.AddComponent<DeployablePlacer>();
            _placer.Init(input, player, materials, _hud, _settlement, _deployables);
            _placer.Actions = _actions;
            _placer.Placed += () => audio.Play(SoundCue.Place);
            _cupboard = new CupboardUi(_hud, _settlement, _actions);

            // Code locks: drawn on doors, boxes and cupboards; one in hand goes on with attack, and
            // then its keypad opens to choose the code.
            _lockViews = new LockViews(_settlement, _buildings, _deployables, materials);
            _lockViews.Me = () => _actions.Owner;
            _keypad = new KeypadUi(_hud, _settlement, _actions);
            _keypad.Sound += audio.Play;
            _lockPlacer = playerObject.AddComponent<LockPlacer>();
            _lockPlacer.Init(_settlement, _lockViews, input, player.Head, _hud, materials);
            _lockPlacer.Actions = _actions;
            _lockPlacer.Placed += target =>
            {
                audio.Play(SoundCue.Place);
                _keypad.ExpectNew(target);
            };

            // Satchel charges: thrown at a wall or a door, they stick, fizz and go off; what falls
            // to a blast goes in rubble.
            _explosions = Explosions.Create(materials, audio.Bank, camera.transform, (x, z) => _world.Shape.Height(x, z));
            _buildings.Fell += _explosions.Fell;
            _deployables.Fell += _explosions.Fell;
            // Gunfire shows through the same effects.
            _gatherer.Effects = _explosions;
            _gatherer.Fired += tool =>
            {
                if (Firearms.TryGet(tool, out var gun))
                    Debug.Log($"[Oxfall] Fired {tool}: {_actions.GunState(tool).Loaded(gun, Time.time)} loaded, {_inventory.Count(gun.Ammo)} carried");
            };
            _thrower = playerObject.AddComponent<ChargeThrower>();
            _thrower.Init(grid, input, player.Head, _hud, _inventory, _explosions);
            _thrower.Actions = _actions;
            _thrower.Settlement = _settlement;
            _thrower.Thrown += () =>
            {
                _gatherer.PlaySwing();
                audio.Play(SoundCue.Swing);
            };
            _actions.ChargeArmed += (charge, left) => _explosions.Arm(charge, left, true);
            _actions.ChargeBlast += OnSoloBlast;

            // The use button: doors, boxes, furnaces, cupboards.
            _interactor = playerObject.AddComponent<Interactor>();
            _interactor.Init(_settlement, player.Head, _hud);
            _interactor.Actions = _actions;
            _interactor.Busy = () => (_keypad != null && _keypad.IsOpen) || (_cupboard != null && _cupboard.IsOpen) || (_inventoryUi != null && _inventoryUi.IsOpen);
            _interactor.DoorUsed += () => audio.Play(SoundCue.Wood);
            _interactor.OpenBox += OpenBox;
            _interactor.OpenCupboard += OpenCupboard;
            _interactor.EnterCode += OpenKeypad;
            _interactor.ManageLock += OpenKeypad;
            _hud.UseTapped += _interactor.Use;
            _hud.LockTapped += _interactor.UseLock;
            _hud.ReloadTapped += () =>
            {
                if (Firearms.IsGun(_hotbar.HeldTool) && _actions.ReloadGun(_hotbar.HeldTool)) _audio.Play(SoundCue.Reload);
            };
            // A box that goes (picked up, or gone from the server's island) closes.
            _settlement.DeployableRemoved += id =>
            {
                if (_inventoryUi.OpenBoxId != id) return;
                _survivor.Looting = 0;
                _inventoryUi.CloseBox(false);
            };

            _inventory.Changed += _ =>
            {
                ShowPickups();
                _hud.RefreshCraftPanel();
                OnHeldChanged();
            };
            _hotbar.SelectionChanged += _ => OnHeldChanged();
            CountOwned(_owned);

            // Multiplayer: with a server chosen (?server=wss://… in the web address), join it;
            // without one, the island is yours alone.
            _net.Init(materials, player.transform);
            _net.Models = id => models.TryGetValue(id, out var model) ? model : null;
            _net.StatusChanged += OnNetStatus;
            _net.Corrected += pose => _player.Teleport(new Vector3(pose.X, pose.Y, pose.Z), pose.Yaw, 0f);
            _net.NodesChanged += nodes =>
            {
                foreach (var (id, remaining) in nodes)
                    if (_world.NodesById.TryGetValue(id, out var view)) view.Sync(remaining);
            };
            // Online, the server keeps the real survivor and settlement; these keep the copies in step.
            _net.Welcomed += pose =>
            {
                // Another island this week: build it (the scene starts again and joins at once).
                if (_net.Seed != s_seed)
                {
                    Debug.Log($"[Oxfall] The server's island is seed {_net.Seed}: building it");
                    s_seed = _net.Seed;
                    s_server = _net.Url;
                    _net.Disconnect();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                    return;
                }
                if (_title != null) EnterGame();
                // A fresh copy of everything comes next: what was here alone makes way for it.
                _quietPickups = true;
                _inventoryUi.CloseBox(false);
                _cupboard.Close();
                _keypad.Close();
                _survivor.Looting = 0;
                _inventory.Clear();
                _crafting.Clear();
                _crafting.Mirror = true;
                _settlement.DropsLoot = false;
                _settlement.Clear();
                _player.Teleport(new Vector3(pose.X, pose.Y, pose.Z), pose.Yaw, 0f);
            };
            _net.SlotsChanged += slots =>
            {
                _inventory.SetSlots(slots);
                if (_quietPickups) CountOwned(_owned);
                _quietPickups = false;
            };
            _net.CraftsChanged += jobs =>
            {
                var loaded = new System.Collections.Generic.List<(Recipe, float)>(jobs.Count);
                foreach (var (id, elapsed) in jobs)
                    if (_recipes.TryGet(id, out var recipe)) loaded.Add((recipe, elapsed));
                _crafting.Load(loaded);
                _hud.RefreshCraftPanel();
            };
            _net.Crafted += (id, lost) =>
            {
                if (_recipes.TryGet(id, out var recipe)) Crafted(recipe, lost);
            };
            _net.VitalsChanged += (health, food, water) =>
            {
                _vitals.Set(health, food, water);
                _vitalsUi.Refresh();
            };
            _net.ActionFailed += why =>
            {
                // A wrong code shakes the keypad (it says so itself); anything else lets it be typed again.
                if (why == ActionResult.WrongCode && _keypad.IsOpen) _keypad.Wrong();
                else
                {
                    _keypad.Refused();
                    _hud.ShowToast(GameActions.Explain(why));
                }
                Debug.Log("[Oxfall] The server refused: " + why);
            };
            _net.TimeOfDay += hours =>
            {
                // Small drift is eased away by the next one; only a real difference is jumped.
                float diff = Mathf.Abs(Mathf.DeltaAngle(_dayNight.Cycle.Hours * 15f, hours * 15f)) / 15f;
                if (diff > 0.02f) _dayNight.SetHour(hours);
            };
            _net.Looted += (id, count, slots) =>
            {
                if (id == 0)
                {
                    // The server closed it (out of reach, gone, or dead).
                    _survivor.Looting = 0;
                    _inventoryUi.CloseBox(false);
                    return;
                }
                var box = _settlement.Container(id);
                if (box == null) return;
                _survivor.Looting = id;
                box.SetSlots(slots);
            };
            _net.Built += ops =>
            {
                foreach (var op in ops)
                    if (!BuildOps.Apply(_settlement, op)) Debug.LogWarning("[Oxfall] A building change did not fit: " + op);
            };
            _net.Hurt += OnHurt;
            _chat = new ChatUi(_hud);
            _chat.Said += text => _net.SendChat(text);
            _net.ChatLine += (from, name, text) => _chat.Add(from == 0 ? null : name, text, from == _net.PlayerId);
            // Who comes and goes (not the ones already there when this player arrives).
            _net.PlayerJoined += name => { if (Time.time - _onlineSince > 3f) _chat.Add(null, $"{name} ha llegado a la isla"); };
            _net.PlayerLeft += name => _chat.Add(null, $"{name} se ha ido");
            _net.Bitten += (target, kind, damage, killed) =>
            {
                Debug.Log($"[Oxfall] Bitten: {_net.NameOf(target) ?? "someone"} by {kind}, -{damage}{(killed ? ", killed" : "")}");
                if (target == _net.PlayerId) OnBitten(kind, damage, killed);
            };
            _net.AnimalHit += _actions.OnServerAnimalHit;
            _net.AnimalsSeen += animals => _animalViews.Apply(animals);
            _net.SleepersSeen += sleepers => _sleeperViews.Apply(sleepers, _net.Owner);
            _net.SleeperHit += OnSleeperHit;
            _net.ChargeArmed += (charge, left) => _explosions.Arm(charge, left, charge.Thrower == _net.PlayerId);
            _net.Blasted += (id, at) =>
            {
                _lastBlast = Time.time;
                _explosions.Blast(id, at);
                Debug.Log($"[Oxfall] Blast {id} at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0})");
            };
            _net.Gunshot += OnGunshot;
            _net.ArrowSeen += (origin, yaw, pitch) =>
            {
                var direction = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                _arrows.Launch(origin, direction);
            };
            _nameTags = new NameTags(_hud);

            // Straight in when the address says so (?server=… joins it, ?solo plays alone: for links
            // and tests) or when coming back from building the server's island; otherwise the title.
            var server = NetClient.ChosenServer(out var playerName);
            if (s_server != null)
            {
                _net.Connect(s_server, playerName);
                s_server = null;
                EnterGame();
            }
            else if (NetClient.UrlParam("server") != null)
            {
                _net.Connect(server, playerName);
                EnterGame();
            }
            else if (NetClient.UrlParam("solo") != null) EnterGame();
            else ShowTitle(server, playerName);
        }

        /// <summary>The title: the island turning behind it, the player and the HUD waiting.</summary>
        private void ShowTitle(string server, string playerName)
        {
            _title = TitleScreen.Create(playerName, ServerInfo(server));
            _hud.gameObject.SetActive(false);
            _player.enabled = false;
            _gatherer.enabled = false;
            _camera.transform.SetParent(null, true);
            _menuView = true;
            // The island from the air: thinner haze and a longer view than on foot, put back on entering.
            _gameFog = RenderSettings.fogDensity;
            _gameFar = _camera.farClipPlane;
            RenderSettings.fogDensity = _gameFog * 0.5f;
            _camera.farClipPlane = Mathf.Max(_gameFar, 480f);
            _dayNight.SetHour(17.4f);
            _title.NameChanged += name =>
            {
                PlayerPrefs.SetString("oxfall.name", name);
                PlayerPrefs.Save();
            };
            _title.PlaySolo += () =>
            {
                _dayNight.SetHour(9f);
                EnterGame();
            };
            _title.PlayOnline += () =>
            {
                string url = NetClient.ChosenServer(out var name);
                if (string.IsNullOrEmpty(url))
                {
                    _title.ShowError("Aún no hay servidor oficial: juega solo mientras tanto");
                    return;
                }
                _title.ShowConnecting(true);
                _net.Connect(url, name);
            };
            _title.Cancelled += () =>
            {
                _net.Disconnect();
                _title.ShowConnecting(false);
            };
            Debug.Log("[Oxfall] Title screen");
        }

        private static string ServerInfo(string server)
        {
            if (string.IsNullOrEmpty(server)) return "Servidor oficial: muy pronto";
            string host = server;
            int scheme = host.IndexOf("://", System.StringComparison.Ordinal);
            if (scheme >= 0) host = host.Substring(scheme + 3);
            int slash = host.IndexOf('/');
            if (slash >= 0) host = host.Substring(0, slash);
            return "Servidor: " + host;
        }

        /// <summary>From the title (or straight from the start) into the game.</summary>
        private void EnterGame()
        {
            if (_title != null)
            {
                _title.Close();
                _title = null;
            }
            if (_menuView)
            {
                _menuView = false;
                RenderSettings.fogDensity = _gameFog;
                _camera.farClipPlane = _gameFar;
                _camera.transform.SetParent(_player.Head, false);
                _camera.transform.localPosition = Vector3.zero;
                _camera.transform.localRotation = Quaternion.identity;
            }
            _hud.gameObject.SetActive(true);
            if (!_vitals.IsDead)
            {
                _player.enabled = true;
                _gatherer.enabled = true;
            }
            _hud.ShowToast(_actions.Online || _net.State == NetClient.Status.Connecting
                ? "Bienvenido: golpea árboles y rocas, construye y sobrevive"
                : "Bienvenido a Oxfall: golpea árboles y rocas");
            Debug.Log("[Oxfall] In the game");
        }

        /// <summary>Behind the title: a slow flight round the island at golden hour.</summary>
        private void TickMenuView(float dt)
        {
            if (!_menuView) return;
            _menuAngle += dt * 2.2f;
            float a = _menuAngle * Mathf.Deg2Rad;
            float radius = IslandShape.IslandRadius * 0.95f;
            var position = new Vector3(Mathf.Cos(a) * radius, 46f, Mathf.Sin(a) * radius);
            var target = new Vector3(Mathf.Cos(a + 1.1f) * 40f, 6f, Mathf.Sin(a + 1.1f) * 40f);
            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        }

        /// <summary>The inventory or the hotbar choice changed: redraw, and update the hand and building mode.</summary>
        private void OnHeldChanged()
        {
            _inventoryUi.Refresh();
            _gatherer.RefreshHeld();
            // Leave the old mode before entering the new one: both drive the attack button's label.
            var held = _hotbar.HeldDefinition;
            bool plan = _hotbar.IsHolding(ItemIds.BuildingPlan);
            bool deploy = Deployables.TryKindOf(held?.Id, out var kind);
            bool hammer = _hotbar.IsHolding(ItemIds.Hammer);
            bool door = DoorKinds.TryKindOf(held?.Id, out var doorKind);
            bool codeLock = _hotbar.IsHolding(ItemIds.CodeLock);
            bool satchel = held?.Tool == ToolKind.Explosive;
            if (!satchel) _thrower.SetActive(false);
            if (!plan) _build.SetActive(false);
            if (!deploy) _placer.Hold(null);
            if (!hammer) _hammer.SetActive(false);
            if (!door) _doorPlacer.SetActive(false);
            if (!codeLock) _lockPlacer.SetActive(false);
            if (plan) _build.SetActive(true);
            if (deploy) _placer.Hold(kind);
            if (hammer) { _hammer.SetActive(true); _hammer.Refresh(); }
            if (door) _doorPlacer.SetActive(true, doorKind);
            if (codeLock) _lockPlacer.SetActive(true);
            if (satchel) _thrower.SetActive(true);
            bool food = held?.Category == ItemCategory.Food;
            bool medical = held?.Category == ItemCategory.Medical;
            bool bow = held?.Tool == ToolKind.Bow;
            _gunLabel = null;
            int arrows = bow ? _inventory.Count(ItemIds.Arrow) : 0;
            _hud.SetAttackLabel(plan || deploy || door || codeLock ? "COLOCAR" : hammer ? "MEJORAR" : food ? "COMER" : medical ? "CURAR"
                : bow ? (arrows > 0 ? $"FLECHAS {arrows}" : "SIN FLECHAS")
                : Firearms.TryGet(held?.Tool ?? ToolKind.None, out var gun) ? GunLabel(gun)
                : satchel ? $"LANZAR {_inventory.Count(ItemIds.SatchelCharge)}" : "GOLPEAR");
            _hud.SetAttackIcon(held != null ? ItemIcons.Get(held.Id) : null);
        }

        /// <summary>Adds up what the inventory holds, item by item.</summary>
        private void CountOwned(System.Collections.Generic.Dictionary<string, int> counts)
        {
            counts.Clear();
            for (int i = 0; i < _inventory.SlotCount; i++)
            {
                var stack = _inventory[i];
                if (stack.IsEmpty) continue;
                counts.TryGetValue(stack.ItemId, out int n);
                counts[stack.ItemId] = n + stack.Amount;
            }
        }

        /// <summary>
        /// After the inventory changes: everything there is more of than before goes in the pickup
        /// feed (gathered, crafted, refunded). Moving items between slots changes no totals.
        /// </summary>
        private void ShowPickups()
        {
            // The first copy from the server is not a pickup.
            if (_quietPickups) return;
            CountOwned(_counting);
            foreach (var pair in _counting)
            {
                _owned.TryGetValue(pair.Key, out int before);
                if (pair.Value <= before) continue;
                int gained = pair.Value - before;
                var def = _catalog.Get(pair.Key);
                _hud.ShowPickup(pair.Key, ItemIcons.Get(pair.Key), gained, def.DisplayName);
                Debug.Log($"[Oxfall] Gathered +{gained} {def.DisplayName}");
            }
            _owned.Clear();
            foreach (var pair in _counting) _owned[pair.Key] = pair.Value;
        }

        /// <summary>Test view: turns to look at the nearest other player.</summary>
        private void FacePlayer()
        {
            RemotePlayer nearest = null;
            float best = float.MaxValue;
            foreach (var other in _net.Players.Values)
            {
                float d = (other.Root.position - _player.transform.position).sqrMagnitude;
                if (other.Visible && d < best) { best = d; nearest = other; }
            }
            if (nearest == null) return;
            var to = nearest.Root.position - _player.transform.position;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _player.Teleport(_player.transform.position, yaw, 4f);
            Debug.Log($"[Oxfall] Facing {nearest.Name}, {Mathf.Sqrt(best):0.0} m away");
        }

        private void OnNetStatus(NetClient.Status status)
        {
            _shownOnline = -1;
            switch (status)
            {
                case NetClient.Status.Connecting:
                    _hud.SetOnline("…", false);
                    break;
                case NetClient.Status.Online:
                    _hud.ShowToast("Conectado al servidor");
                    // From now on the server decides what trees and rocks give, and when they grow back.
                    _gatherer.RemoteHit = _net.SendHit;
                    // And whether a blow at another player lands.
                    _gatherer.StrikePlayer = _net.SendStrike;
                    _gatherer.StrikeSleeper = _net.SendStrikeSleeper;
                    // And where the animals are.
                    _animalViews.Clear();
                    _onlineSince = Time.time;
                    _chat.SetOnline(true);
                    break;
                case NetClient.Status.Failed:
                    _chat.SetOnline(false);
                    _title?.ShowError(_net.FailReason);
                    // Carries on alone with what the server last said.
                    _gatherer.RemoteHit = null;
                    _gatherer.StrikePlayer = null;
                    _gatherer.StrikeSleeper = null;
                    _sleeperViews.Clear();
                    _crafting.Mirror = false;
                    _settlement.DropsLoot = true;
                    _hud.SetOnline("SIN CONEXIÓN", false);
                    _hud.ShowToast(_net.FailReason);
                    break;
                default:
                    _chat.SetOnline(false);
                    _gatherer.RemoteHit = null;
                    _gatherer.StrikePlayer = null;
                    _gatherer.StrikeSleeper = null;
                    _sleeperViews.Clear();
                    _crafting.Mirror = false;
                    _settlement.DropsLoot = true;
                    _hud.SetOnline(null, false);
                    break;
            }
        }

        /// <summary>Every frame while online: the player count and the names over heads.</summary>
        private void TickNet()
        {
            if (_net == null) return;
            _nameTags.Begin();
            if (_net.State == NetClient.Status.Online)
            {
                if (_net.OnlineCount != _shownOnline)
                {
                    _shownOnline = _net.OnlineCount;
                    _hud.SetOnline(_shownOnline.ToString(), true);
                }
                foreach (var other in _net.Players.Values)
                    if (other.Visible && !other.Down) _nameTags.Show(_camera, other.Root.position + Vector3.up * 2.05f, other.Name);
                // Sleepers only up close: a body in the grass, and whose it is.
                foreach (var (at, name) in _sleeperViews.All())
                    _nameTags.Show(_camera, at + Vector3.up * 0.6f, $"{name} · durmiendo", 12f);
            }
            _nameTags.End();
        }

        /// <summary>A blow landed somewhere: felt if it hit this player, marked if this player struck it.</summary>
        private void OnHurt(ushort target, ushort attacker, int damage, bool killed)
        {
            string by = _net.NameOf(attacker) ?? "Alguien";
            string whom = _net.NameOf(target) ?? "alguien";
            bool blast = Time.time - _lastBlast < 0.6f;
            Debug.Log($"[Oxfall] Hurt: {whom} by {by}, -{damage}{(killed ? ", killed" : "")}{(blast ? " (blast)" : "")}");
            if (target == _net.PlayerId)
            {
                _hud.ShowHurt(damage);
                _audio.Play(SoundCue.Hurt);
                if (blast)
                {
                    string whose = attacker == target ? "tu propia carga explosiva" : $"una carga de {by}";
                    if (killed) _killer = whose;
                    else _hud.ShowToast($"Te alcanza {whose}  −{damage}");
                }
                else if (killed) _killer = by;
                else if (_firedAt.TryGetValue(attacker, out float firedAt) && Time.time - firedAt < 1.5f) _hud.ShowToast($"{by} te dispara  −{damage}");
                else _hud.ShowToast($"{by} te golpea  −{damage}");
            }
            else if (attacker == _net.PlayerId)
            {
                _hud.ShowHitMarker(killed);
                _audio.Play(SoundCue.HitBody);
                if (killed) _hud.ShowToast($"Has derribado a {whom}");
            }
        }

        /// <summary>Someone else fired a gun: the flash, the bang and where its pellets strike, as they see it.</summary>
        private void OnGunshot(ushort shooter, ToolKind tool, Vector3 origin, float yaw, float pitch, ushort seed)
        {
            if (!Firearms.TryGet(tool, out var gun)) return;
            _firedAt[shooter] = Time.time;
            Debug.Log($"[Oxfall] Gunshot by {_net.NameOf(shooter) ?? "someone"} ({tool})");
            var forward = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            // From the gun in their hands, when this phone shows it.
            var muzzle = _net.Players.TryGetValue(shooter, out var them) && them.TryMuzzle(out var held)
                ? held : origin + forward * 0.6f + Vector3.down * 0.15f;
            bool big = gun.Pellets > 1;
            _explosions.MuzzleFlash(muzzle, forward, big);
            Gatherer.Spray(gun, origin + forward * 0.5f, muzzle, yaw, pitch, seed, _explosions, null);
            GameAudio.PlayAt(big ? _audio.Bank.ShotgunShot : _audio.Bank.RevolverShot, muzzle, 1f, 6f, big ? 180f : 200f, 0.05f);
        }

        /// <summary>The attack button's label for the gun in hand: rounds in it, and how many more are carried.</summary>
        private string GunLabel(GunSpec gun)
        {
            int carried = _inventory.Count(gun.Ammo);
            if (carried == 0) return gun.Tool == ToolKind.Shotgun ? "SIN CARTUCHOS" : "SIN BALAS";
            var state = _actions.GunState(gun.Tool);
            if (state.Reloading(Time.time)) return "RECARGANDO…";
            int loaded = Mathf.Min(state.Loaded(gun, Time.time), carried);
            return $"DISPARAR {loaded}/{gun.Capacity} · {carried}";
        }

        /// <summary>Alone: one of this player's charges goes off; standing too near hurts.</summary>
        private void OnSoloBlast(LiveCharge charge)
        {
            var at = new Vector3(charge.X, charge.Y, charge.Z);
            _lastBlast = Time.time;
            _explosions.Blast(charge.Id, at);
            Debug.Log($"[Oxfall] Blast {charge.Id} at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}) on {charge.Target}");
            if (_vitals.IsDead) return;
            var middle = _player.transform.position + Vector3.up * 0.9f;
            float amount = Explosives.DamageToPlayer(Vector3.Distance(at, middle));
            if (amount <= 0f || Explosives.Shielded(_settlement.Grid, at.x, at.y, at.z, middle.x, middle.y, middle.z)) return;
            // Named before the blow lands, in case it is the last.
            _killer = "tu propia carga explosiva";
            int damage = Mathf.RoundToInt(_survivor.TakeHit(amount));
            _hud.ShowHurt(damage);
            _audio.Play(SoundCue.Hurt);
            if (_vitals.IsDead) return;
            _killer = null;
            _hud.ShowToast($"Te alcanza tu propia carga  −{damage}");
        }

        /// <summary>A sleeper was hurt: it jolts, the attacker sees the hit; it may be this player's own body, long ago.</summary>
        private void OnSleeperHit(int owner, ushort attacker, string by, int damage, bool killed)
        {
            Debug.Log($"[Oxfall] Sleeper hit: {owner} by {(string.IsNullOrEmpty(by) ? "someone" : by)}, -{damage}{(killed ? ", killed" : "")}");
            if (owner == _net.Owner)
            {
                // Killed in their sleep while away: the death screen says by whom.
                if (killed) _killer = $"{(string.IsNullOrEmpty(by) ? "alguien" : by)} mientras dormías";
                return;
            }
            _sleeperViews.Flinch(owner);
            if (attacker != _net.PlayerId) return;
            _hud.ShowHitMarker(killed);
            _audio.Play(SoundCue.HitBody);
            if (killed && _sleeperViews.TryGet(owner, out _, out var name)) _hud.ShowToast($"Has matado a {name} mientras dormía");
        }

        /// <summary>Turns to the nearest sleeper; with <paramref name="approach"/>, first steps up beside them.</summary>
        private void FaceSleeper(bool approach)
        {
            var eye = _player.transform.position;
            int owner = _sleeperViews.Nearest(eye, out var at);
            if (owner == 0)
            {
                Debug.Log("[Oxfall] No sleepers");
                return;
            }
            if (approach)
            {
                var away = eye - at;
                away.y = 0f;
                var stand = at + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.6f;
                stand.y = _world.Shape.Height(stand.x, stand.z) + 0.05f;
                eye = stand;
            }
            var to = at - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(1.5f, Mathf.Max(0.5f, new Vector2(to.x, to.z).magnitude)) * Mathf.Rad2Deg;
            _player.Teleport(eye, yaw, pitch);
            Debug.Log($"[Oxfall] Facing sleeper {owner}, {to.magnitude:0.0} m away");
        }

        private static string AnimalName(AnimalKind kind) => "un " + Animals.Name(kind).ToLowerInvariant();

        /// <summary>An animal hurt this player.</summary>
        private void OnBitten(AnimalKind kind, int damage, bool killed)
        {
            Debug.Log($"[Oxfall] Bitten by {kind}, -{damage}{(killed ? ", killed" : "")}");
            _hud.ShowHurt(damage);
            _audio.Play(SoundCue.Hurt);
            if (killed) _killer = AnimalName(kind);
            else _hud.ShowToast(kind == AnimalKind.Boar ? $"Un jabalí te embiste  −{damage}" : $"{char.ToUpperInvariant(AnimalName(kind)[0])}{AnimalName(kind).Substring(1)} te muerde  −{damage}");
        }

        /// <summary>What this player's blow at an animal did.</summary>
        private void OnAnimalHit(int id, AnimalBlow blow, float damage)
        {
            _animalViews.TryGet(id, out var kind, out _, out _);
            Debug.Log($"[Oxfall] Animal hit: {kind} {id} {blow} -{damage:0}");
            switch (blow)
            {
                case AnimalBlow.Hurt:
                case AnimalBlow.Killed:
                    _hud.ShowHitMarker(blow == AnimalBlow.Killed);
                    _audio.Play(SoundCue.HitBody);
                    _animalViews.Flinch(id);
                    if (blow == AnimalBlow.Killed)
                        _hud.ShowToast($"Has abatido {AnimalName(kind)}: golpea el cuerpo para sacar carne y cuero");
                    break;
                case AnimalBlow.Cut:
                    _audio.Play(SoundCue.HitBody);
                    _animalViews.Flinch(id);
                    break;
                case AnimalBlow.UsedUp:
                    _audio.Play(SoundCue.HitBody);
                    _hud.ShowToast("No queda nada que aprovechar");
                    break;
                case AnimalBlow.NeedsBlade:
                    if (Time.time < _bladeHintAt) break;
                    _bladeHintAt = Time.time + 2.5f;
                    _hud.ShowToast("Para despiezarlo necesitas una piedra, un hacha o una lanza");
                    break;
            }
        }

        /// <summary>Alone: the animals move on and attack. Online the server does that. Either way they are drawn.</summary>
        private void TickAnimals(float dt)
        {
            if (_animalViews == null) return;
            if (!_actions.Online && _fauna != null)
            {
                _prey.Clear();
                // On the title screen they roam the island behind the menu, minding no one.
                if (_title == null && !_vitals.IsDead)
                {
                    var p = _player.transform.position;
                    _prey.Add(new Fauna.Prey(0, p.x, p.y, p.z));
                }
                _bites.Clear();
                _fauna.Tick(dt, _prey, _bites);
                foreach (var bite in _bites)
                {
                    if (_vitals.IsDead || !_fauna.TryGet(bite.Animal, out var animal)) continue;
                    // Named before the blow lands, in case it is the last; what they wear takes its share.
                    _killer = AnimalName(animal.Kind);
                    int damage = Mathf.RoundToInt(_survivor.TakeHit(bite.Damage));
                    if (!_vitals.IsDead)
                    {
                        _killer = null;
                        OnBitten(animal.Kind, damage, false);
                    }
                    else
                    {
                        _hud.ShowHurt(damage);
                        _audio.Play(SoundCue.Hurt);
                    }
                }
                _animalSnaps.Clear();
                foreach (var a in _fauna.All) _animalSnaps.Add(AnimalSnap.Of(a));
                _animalViews.Apply(_animalSnaps);
            }
            _animalViews.Tick(dt, _camera != null ? _camera.transform.position : Vector3.zero);
            _sleeperViews.Tick(dt, _camera != null ? _camera.transform.position : Vector3.zero);
        }

        /// <summary>Test view: the nearest animal (living or dead): turns to it, or walks up to it first.</summary>
        private void FaceAnimal(bool approach)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            Vector3 at = default;
            var here = _player.transform.position;
            foreach (int id in _animalViews.Ids)
            {
                if (!_animalViews.TryGet(id, out _, out var position, out _)) continue;
                float d = (position - here).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = id;
                    at = position;
                }
            }
            if (best < 0) return;
            _animalViews.TryGet(best, out var kind, out _, out var state);
            var to = at - here;
            to.y = 0f;
            if (approach && to.magnitude > 2.2f)
            {
                var stand = at - to.normalized * 2.0f;
                stand.y = _world.Shape.Height(stand.x, stand.z) + 0.05f;
                here = stand;
                to = at - stand;
                to.y = 0f;
            }
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(here.y + 1.6f - (at.y + Animals.HalfSize(kind).Y * (state == AnimalState.Dead ? 0.4f : 1f)), to.magnitude) * Mathf.Rad2Deg;
            _player.Teleport(here, yaw, pitch);
            Debug.Log($"[Oxfall] Facing {kind} {best} ({state}), {to.magnitude:0.0} m away");
        }

        /// <summary>Test: an animal of this kind a few metres ahead (alone, or on a test server).</summary>
        private void SpawnAnimal(AnimalKind kind)
        {
            if (_actions.Online)
            {
                _net.SendAct(ActKind.DevSpawnAnimal, (int)kind);
                return;
            }
            // A deer beyond where it would bolt, a wolf within sight, a boar close by.
            float distance = kind == AnimalKind.Deer ? 16f : kind == AnimalKind.Wolf ? 14f : 7f;
            var t = _player.transform;
            var at = t.position + t.forward * distance;
            _fauna.Add(kind, at.x, at.z, t.eulerAngles.y + 180f);
        }

        private MapUi _map;
        private Vector3? _diedAt;
        private readonly System.Collections.Generic.List<MapUi.Pin> _mapPins = new System.Collections.Generic.List<MapUi.Pin>();

        /// <summary>The map's marks: this player's bags and cupboards, and where they last died.</summary>
        private void RefreshMap()
        {
            _mapPins.Clear();
            int owner = _actions.Owner;
            foreach (var d in _settlement.Deployables)
            {
                if (d.Owner != owner) continue;
                if (d.Kind == DeployableKind.SleepingBag)
                    _mapPins.Add(new MapUi.Pin(new Vector3(d.X, d.Y, d.Z), Icons.Bed, new Color(0.55f, 0.78f, 1f)));
                else if (d.Kind == DeployableKind.ToolCupboard)
                    _mapPins.Add(new MapUi.Pin(new Vector3(d.X, d.Y, d.Z), Icons.Home, new Color(1f, 0.82f, 0.4f)));
            }
            if (_diedAt.HasValue) _mapPins.Add(new MapUi.Pin(_diedAt.Value, Icons.Close, UiTheme.Bad, "Moriste aquí"));
            _map.Refresh(_player.transform.position, _player.Yaw, _mapPins);
        }

        private void OnDied()
        {
            Debug.Log("[Oxfall] The player died.");
            _map.Close();
            _diedAt = _player.transform.position;
            _inventoryUi.Close();
            _cupboard.Close();
            _keypad.Close();
            // What they carried stays where they fell, in a bag anyone can empty.
            _actions.DropBag();
            _hud.CloseCraftPanel();
            _player.enabled = false;
            _gatherer.enabled = false;
            _vitalsUi.SetCause(_killer != null ? $"Te mató {_killer}." : null);
            _killer = null;
            _vitalsUi.SetBags(MyBags());
            _vitalsUi.ShowDeath(true);
        }

        /// <summary>The player's sleeping bags, with how long until each can be woken up in.</summary>
        private System.Collections.Generic.List<(int, System.Func<float>)> MyBags()
        {
            var bags = new System.Collections.Generic.List<(int, System.Func<float>)>();
            foreach (var bag in _settlement.BagsOf(_actions.Owner))
            {
                int id = bag.Id;
                bags.Add((id, () => _settlement.BagReadyAt(id) - Time.time));
            }
            bags.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return bags;
        }

        /// <summary>
        /// Wakes up on the beach as a fresh player: back to a rock and a torch. (Later the old
        /// inventory will lie in a bag where the player fell.)
        /// </summary>
        private void WakeUp()
        {
            if (_actions.Respawn() != ActionResult.Ok) return;
            // Alone the game picks the beach; online the server does and moves the player there.
            if (!_actions.Online) _player.Teleport(_world.SpawnPosition, _world.SpawnYaw, 0f);
            AfterWakeUp("Has despertado en la playa");
        }

        /// <summary>Wakes up in one of the player's sleeping bags, if it is ready.</summary>
        private void WakeUpInBag(int id)
        {
            var result = _actions.RespawnInBag(id, out var bag);
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            if (!_actions.Online) _player.Teleport(new Vector3(bag.X, bag.Y + 0.1f, bag.Z), bag.Yaw, 0f);
            AfterWakeUp("Has despertado en tu saco de dormir");
        }

        private void AfterWakeUp(string message)
        {
            if (_hotbar.Selected != 0) _hotbar.Select(0);
            _warnedHungry = _warnedThirsty = false;
            _player.enabled = true;
            _gatherer.enabled = true;
            _vitalsUi.ShowDeath(false);
            _vitalsUi.Refresh();
            _hud.ShowToast(message);
        }

        /// <summary>Opens a box or furnace beside the inventory.</summary>
        private void OpenBox(int id)
        {
            _cupboard.Close();
            _keypad.Close();
            var result = _actions.Open(id);
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            _inventoryUi.OpenBox(id);
            _audio.Play(SoundCue.Wood);
            Debug.Log("[Oxfall] Opened box " + id);
        }

        private void OpenCupboard(int id)
        {
            _inventoryUi.Close();
            _keypad.Close();
            _cupboard.Open(id);
            Debug.Log("[Oxfall] Opened cupboard " + id);
        }

        /// <summary>A code lock's keypad: to type its code, choose one, or manage it.</summary>
        private void OpenKeypad(LockTarget target)
        {
            _inventoryUi.Close();
            _cupboard.Close();
            _keypad.Open(target);
        }

        /// <summary>Once a second: what is open closes when the player has walked away from it.</summary>
        private void CloseWhatIsFar()
        {
            float reach = PlayerActions.UseReach + 1f;
            var here = _player.transform.position;
            bool Far(int id) => !_settlement.TryGet(id, out var d) || (new Vector3(d.X, d.Y, d.Z) - here).sqrMagnitude > reach * reach;
            if (_inventoryUi.OpenBoxId != 0 && Far(_inventoryUi.OpenBoxId)) _inventoryUi.CloseBox(true);
            if (_cupboard.IsOpen && Far(_cupboard.OpenId)) _cupboard.Close();
            if (_keypad.IsOpen)
            {
                var at = PlayerActions.LockPoint(_settlement, _keypad.Target);
                if (!_settlement.HasLock(_keypad.Target) || (new Vector3(at.X, at.Y, at.Z) - here).sqrMagnitude > reach * reach) _keypad.Close();
            }
        }

        /// <summary>For tests: the nearest thing of a kind set down, or 0.</summary>
        private int Nearest(DeployableKind kind)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            var here = _player.transform.position;
            foreach (var d in _settlement.Deployables)
            {
                if (d.Kind != kind) continue;
                float distance = (new Vector3(d.X, d.Y, d.Z) - here).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = d.Id;
                }
            }
            return best;
        }

        /// <summary>For tests: turns the player, where they stand, to look at something set down.</summary>
        private void FaceDeployable(int id)
        {
            if (!_settlement.TryGet(id, out var d)) return;
            var eye = _player.Head.position;
            var to = new Vector3(d.X, d.Y + 0.35f, d.Z) - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            _player.Teleport(_player.transform.position, yaw, pitch);
        }

        /// <summary>
        /// Commands for automated tests of the web build, sent from the page with
        /// <c>unityInstance.SendMessage('Oxfall', 'DebugCommand', 'nearest-tree')</c>. Nothing in the
        /// game calls it, and only the page hosting the game can.
        /// </summary>
        public void DebugCommand(string command)
        {
            // Test commands move the player at will; the server is told so it does not pull them back.
            _net?.MarkTeleport();
            switch (command)
            {
                case "settings": _hud.ToggleSettings(); break;
                case "face-player": FacePlayer(); break;
                case "face-sleeper": FaceSleeper(false); break;
                case "approach-sleeper": FaceSleeper(true); break;
                case "face-animal": FaceAnimal(false); break;
                case "approach-animal": FaceAnimal(true); break;
                case "spawn-boar": SpawnAnimal(AnimalKind.Boar); break;
                case "spawn-wolf": SpawnAnimal(AnimalKind.Wolf); break;
                case "spawn-deer": SpawnAnimal(AnimalKind.Deer); break;
                case "clear-animals":
                    // Test, alone: no animals at all, so a shot is not spoiled by a passing wolf.
                    if (!_actions.Online) _fauna.Clear();
                    break;
                case "animals":
                    foreach (int id in _animalViews.Ids)
                        if (_animalViews.TryGet(id, out var k, out var pos, out var st))
                            Debug.Log($"[Oxfall] Animal {id} {k} {st} at {pos} ({Vector3.Distance(pos, _player.transform.position):0.0} m)");
                    break;
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
                case "nearest-barrel": FaceNearest(ResourceNodeType.Barrel); break;
                case "nearest-sulfur": FaceNearest(ResourceNodeType.Sulfur); break;
                case "satchel": HoldItem(ItemIds.SatchelCharge); break;
                case "revolver": HoldItem(ItemIds.Revolver); break;
                case "blunderbuss": HoldItem(ItemIds.Blunderbuss); break;
                case "bandage": HoldItem(ItemIds.Bandage); break;
                case "map": _map.Toggle(); break;
                case "syringe": HoldItem(ItemIds.MedicalSyringe); break;
                case "fire": _gatherer.UseOnce(); break;
                case "reload":
                    if (Firearms.IsGun(_hotbar.HeldTool) && _actions.ReloadGun(_hotbar.HeldTool)) _audio.Play(SoundCue.Reload);
                    break;
                case "throw": _thrower.Throw(); break;
                case "nearest-berries": FaceNearest(ResourceNodeType.BerryBush); break;
                case "quality-low": SetQuality(QualityTier.Low); break;
                case "quality-medium": SetQuality(QualityTier.Medium); break;
                case "quality-high": SetQuality(QualityTier.High); break;
                case "craft-hatchet": TryCraft(_recipes.Get("stone_hatchet")); break;
                case "craft-pickaxe": TryCraft(_recipes.Get("stone_pickaxe")); break;
                case "craft-panel": _hud.ToggleCraftPanel(); break;
                case "craft-tab-1": _hud.CraftTab(0); break;
                case "craft-tab-2": _hud.CraftTab(1); break;
                case "craft-tab-3": _hud.CraftTab(2); break;
                case "craft-tab-4": _hud.CraftTab(3); break;
                case "craft-tab-5": _hud.CraftTab(4); break;
                case "bow": HoldItem(ItemIds.WoodenBow); break;
                case "give" when _actions.Online:
                case "give-all" when _actions.Online:
                    // A test server hands out the kit itself (a real one refuses).
                    _net.SendAct(ActKind.DevGive);
                    break;
                case "give-raid" when _actions.Online:
                    _net.SendAct(ActKind.DevGive, 1);
                    break;
                case "give-raid":
                    ServerWorld.GiveTestKit(_inventory, 1);
                    break;
                case "give-guns" when _actions.Online:
                    _net.SendAct(ActKind.DevGive, 2);
                    break;
                case "give-guns":
                    ServerWorld.GiveTestKit(_inventory, 2);
                    break;
                case "give":
                    _inventory.Add(ItemIds.Wood, 1000);
                    _inventory.Add(ItemIds.Stone, 1000);
                    _inventory.Add(ItemIds.BuildingPlan, 1);
                    break;
                case "give-all":
                    // The same kit a test server hands out.
                    ServerWorld.GiveTestKit(_inventory);
                    break;
                // Puts on whatever hide clothing is carried; undress takes everything off.
                case "wear-hide":
                    foreach (var id in new[] { ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots })
                        for (int i = 0; i < _inventory.CarrySlots; i++)
                            if (_inventory[i].ItemId == id) { _actions.Wear(i); break; }
                    break;
                case "undress":
                    for (int i = _inventory.CarrySlots; i < _inventory.SlotCount; i++)
                        if (!_inventory[i].IsEmpty) _actions.Wear(i);
                    break;
                case "box": HoldItem(ItemIds.StorageBox); break;
                case "furnace": HoldItem(ItemIds.Furnace); break;
                case "sleeping-bag": HoldItem(ItemIds.SleepingBag); break;
                case "cupboard": HoldItem(ItemIds.ToolCupboard); break;
                case "campfire": HoldItem(ItemIds.Campfire); break;
                case "open-box": OpenBox(Nearest(DeployableKind.StorageBox)); break;
                case "open-furnace": OpenBox(Nearest(DeployableKind.Furnace)); break;
                case "open-cupboard": OpenCupboard(Nearest(DeployableKind.ToolCupboard)); break;
                case "face-box": FaceDeployable(Nearest(DeployableKind.StorageBox)); break;
                case "face-furnace": FaceDeployable(Nearest(DeployableKind.Furnace)); break;
                case "face-cupboard": FaceDeployable(Nearest(DeployableKind.ToolCupboard)); break;
                case "face-bag": FaceDeployable(Nearest(DeployableKind.Bag)); break;
                case "open-bag": OpenBox(Nearest(DeployableKind.Bag)); break;
                case "open-fire": OpenBox(Nearest(DeployableKind.Campfire)); break;
                case "face-fire": FaceDeployable(Nearest(DeployableKind.Campfire)); break;
                case "hatchet": HoldItem(ItemIds.StoneHatchet); break;
                case "raw-meat": HoldItem(ItemIds.RawMeat); break;
                case "cooked-meat": HoldItem(ItemIds.CookedMeat); break;
                case "put-meat":
                    // Test: the raw meat carried goes into the open campfire (or box).
                    for (int i = 0; i < _inventory.SlotCount; i++)
                        if (_inventory[i].ItemId == ItemIds.RawMeat)
                        {
                            Debug.Log($"[Oxfall] Put meat: {_actions.Put(i, -1)}");
                            break;
                        }
                    break;
                case "across": _inventoryUi.MoveAcross(); break;
                case "light": _inventoryUi.ToggleFurnace(); break;
                case "authorize": _cupboard.Authorize(); break;
                case "close-box": _inventoryUi.CloseBox(true); break;
                case "bag-1":
                {
                    var bags = MyBags();
                    if (bags.Count > 0) WakeUpInBag(bags[0].Item1);
                    break;
                }
                case "build": HoldItem(ItemIds.BuildingPlan); break;
                case "hammer": HoldItem(ItemIds.Hammer); break;
                case "door": HoldItem(ItemIds.WoodenDoor); break;
                case "metal-door": HoldItem(ItemIds.SheetMetalDoor); break;
                case "metal-hatchet": HoldItem(ItemIds.MetalHatchet); break;
                case "metal-pickaxe": HoldItem(ItemIds.MetalPickaxe); break;
                case "spear": HoldItem(ItemIds.WoodenSpear); break;
                case "code-lock": HoldItem(ItemIds.CodeLock); break;
                case "place-lock": _lockPlacer.Place(); break;
                case "lock-button": _interactor.UseLock(); break;
                case "keypad-ok": _keypad.Confirm(); break;
                case "keypad-toggle": _keypad.ToggleLocked(); break;
                case "keypad-remove": _keypad.Remove(); break;
                case "close-keypad": _keypad.Close(); break;
                case "lock-stranger":
                    // Every lock as if someone else had set its code: to try typing one alone.
                    var locks = new System.Collections.Generic.List<LockTarget>();
                    foreach (var pair in _settlement.Locks) locks.Add(pair.Key);
                    foreach (var target in locks) _settlement.SetCode(target, 4321, 9999);
                    break;
                case "upgrade": _hammer.Upgrade(); break;
                case "demolish": _hammer.Demolish(); break;
                case "place-door": _doorPlacer.Place(); break;
                case "use-door":
                case "interact": _interactor.Use(); break;
                case "build-hut": BuildTestHut(); break;
                case "build-tower": BuildTestTower(); break;
                case "on-stairs": StandOnStairs(); break;
                case "tower-top": ToTowerTop(); break;
                case "aim-wall": AimAtPiece(PieceType.Wall, 1.5f); break;
                case "aim-door": AimAtPiece(PieceType.Doorway, 1.1f); break;
                case "upgrade-all-1": UpgradeAll(BuildingTier.Wood); break;
                case "upgrade-all-2": UpgradeAll(BuildingTier.Stone); break;
                case "upgrade-all-3": UpgradeAll(BuildingTier.Metal); break;
                case "piece": _build.CyclePiece(); break;
                case "place": _build.Place(); break;
                case "inventory": _inventoryUi.Toggle(); break;
                case "place-fire":
                case "deploy": _placer.Place(); break;
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
                              $"held={_hotbar.Held} building={_build.Active} deployables={_deployables.Count} looting={_survivor.Looting} " +
                              $"time={_dayNight.Cycle.Clock} health={_vitals.Health:0} food={_vitals.Food:0} water={_vitals.Water:0} " +
                              $"ponds={_world.Shape.Ponds.Count} berries={_inventory.Count(ItemIds.Berries)} " +
                              $"pieces={_buildings.Grid.PieceCount} views={_buildings.Count} doorLook={_interactor.LookingAtDoor} lookAt={_interactor.LookingAt} " +
                              $"locks={_lockViews.Count} keypad={(_keypad.IsOpen ? _keypad.Current.ToString() : "closed")} " +
                              $"metal={_inventory.Count(ItemIds.MetalFragments)} animals={_animalViews.Count} sleepers={_sleeperViews.Count} arrows={_inventory.Count(ItemIds.Arrow)} armour={Mathf.RoundToInt(_inventory.Protection * 100f)} " +
                              $"satchels={_inventory.Count(ItemIds.SatchelCharge)} charges={_explosions.Live} sulfur={_inventory.Count(ItemIds.Sulfur)} " +
                              $"shells={_inventory.Count(ItemIds.Shell)} bullets={_inventory.Count(ItemIds.PistolBullet)} " +
                              $"meat={_inventory.Count(ItemIds.RawMeat)} cooked={_inventory.Count(ItemIds.CookedMeat)} leather={_inventory.Count(ItemIds.Leather)} " +
                              $"pos={_player.transform.position} quality={GraphicsQuality.Name(_quality.Tier)} " +
                              $"screen={Screen.width}x{Screen.height} grass={_grass.BladeCount} " +
                              $"net={_net.State} id={_net.PlayerId} online={_net.OnlineCount} " +
                              $"others={string.Join(";", System.Linq.Enumerable.Select(_net.Players.Values, p => p.Name + "@" + p.Root.position + (p.Held.Length > 0 ? "+" + p.Held : "")))}");
                    break;
                case "disconnect": _net.Disconnect(); break;
                case "reconnect": _net.Reconnect(); break;
                case "chat-button": _chat.Ask(); break;
                // Full health again (alone only: online the server keeps it); for slow test machines.
                case "heal": if (!_actions.Online && !_vitals.IsDead) _vitals.Set(Vitals.MaxHealth, _vitals.Food, _vitals.Water); break;
                default:
                    // connect:URL joins a server; slot-N chooses hotbar slot N (1 to 6); tap-N taps
                    // inventory slot N (0 to 29).
                    if (command.StartsWith("connect:"))
                        _net.Connect(command.Substring(8), "Tester");
                    else if (command.StartsWith("slot-") && int.TryParse(command.Substring(5), out int slot))
                        _hotbar.Select(slot - 1);
                    else if (command.StartsWith("say:"))
                        _chat.Send(command.Substring(4));
                    else if (command.StartsWith("tap-") && int.TryParse(command.Substring(4), out int tap))
                        _inventoryUi.TapSlot(tap);
                    // code-NNNN types a code on the open keypad and confirms it; key-N presses one key.
                    else if (command.StartsWith("code-"))
                        _keypad.Type(command.Substring(5));
                    else if (command.StartsWith("key-") && int.TryParse(command.Substring(4), out int key))
                        _keypad.Press(key);
                    // fwd-M / back-M walk the player M metres along (or against) where they face, onto the ground.
                    else if ((command.StartsWith("fwd-") || command.StartsWith("back-")) && float.TryParse(command.Substring(command.IndexOf('-') + 1),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float metres))
                    {
                        var flat = _player.transform.forward;
                        flat.y = 0f;
                        var to = _player.transform.position + flat.normalized * (command[0] == 'b' ? -metres : metres);
                        to.y = _world.Shape.Height(to.x, to.z) + 0.05f;
                        _player.Teleport(to, _player.Yaw, _player.Pitch);
                    }
                    // turn-D turns the player D degrees where they stand.
                    else if (command.StartsWith("turn-") && float.TryParse(command.Substring(5),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turn))
                        _player.Teleport(_player.transform.position, _player.Yaw + turn, _player.Pitch);
                    // box-tap-N taps slot N of the open box.
                    else if (command.StartsWith("box-tap-") && int.TryParse(command.Substring(8), out int boxTap))
                        _inventoryUi.TapBoxSlot(boxTap);
                    // time-H jumps to hour H (0 to 23).
                    else if (command.StartsWith("time-") && float.TryParse(command.Substring(5),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hour))
                        _dayNight.SetHour(hour);
                    // look-D looks D degrees up (negative: down).
                    else if (command.StartsWith("look-") && float.TryParse(command.Substring(5),
                                 System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float up))
                        _player.Teleport(_player.transform.position, _player.Yaw, -up);
                    else
                        Debug.LogWarning("[Oxfall] Unknown debug command: " + command);
                    break;
            }
        }

        /// <summary>
        /// For tests: a one-room twig hut ahead of the player (floor, two walls, a doorway facing the
        /// player, a roof), free, and the player turned to face its doorway from outside.
        /// </summary>
        private void BuildTestHut()
        {
            var grid = _buildings.Grid;
            var p = _player.transform.position;
            var forward = _player.transform.forward;
            forward.y = 0f;
            var ahead = p + forward.normalized * 5f;
            var cell = BuildingGrid.CellAt(ahead.x, ahead.z);
            float x0 = cell.X * BuildingGrid.CellSize, z0 = cell.Z * BuildingGrid.CellSize, s = BuildingGrid.CellSize;
            float top = Mathf.Max(Mathf.Max(_world.Shape.Height(x0, z0), _world.Shape.Height(x0 + s, z0)),
                Mathf.Max(_world.Shape.Height(x0, z0 + s), _world.Shape.Height(x0 + s, z0 + s))) + 0.25f;
            grid.PlaceFoundation(cell, top);
            // The doorway on the side facing the player.
            var toPlayer = new Vector3(p.x - (x0 + s * 0.5f), 0f, p.z - (z0 + s * 0.5f));
            Edge doorway = Mathf.Abs(toPlayer.x) > Mathf.Abs(toPlayer.z)
                ? (toPlayer.x > 0f ? cell.East : cell.West)
                : (toPlayer.z > 0f ? cell.North : cell.South);
            foreach (var edge in cell.Edges)
                grid.PlaceEdge(edge, edge.Equals(doorway) ? PieceType.Doorway : PieceType.Wall);
            grid.PlaceRoof(cell);
            Debug.Log($"[Oxfall] Test hut at {cell}, doorway {doorway}, pieces={grid.PieceCount}");
        }

        private Cell _towerStairs, _towerRoom;

        /// <summary>
        /// For tests: a two-storey twig house ahead of the player, free: two squares side by side, the
        /// one ahead with a doorway towards the player and a window, the other with spiral stairs up
        /// to the room above, which has windows on every side and a roof.
        /// </summary>
        private void BuildTestTower()
        {
            var grid = _buildings.Grid;
            var p = _player.transform.position;
            var forward = _player.transform.forward;
            forward.y = 0f;
            var ahead = p + forward.normalized * 5f;
            var room = BuildingGrid.CellAt(ahead.x, ahead.z);
            float s = BuildingGrid.CellSize;
            var toPlayer = new Vector3(p.x - (room.X + 0.5f) * s, 0f, p.z - (room.Z + 0.5f) * s);
            bool sideways = Mathf.Abs(toPlayer.x) > Mathf.Abs(toPlayer.z);
            Edge doorway = sideways ? (toPlayer.x > 0f ? room.East : room.West) : (toPlayer.z > 0f ? room.North : room.South);
            // The stairwell beside the room, across the line of sight; its stairs come out over the room.
            var stairs = sideways ? new Cell(room.X, room.Z + 1) : new Cell(room.X + 1, room.Z);
            int facing = sideways ? 2 : 3;
            float top = 0f;
            foreach (var cell in new[] { room, stairs })
            {
                float x0 = cell.X * s, z0 = cell.Z * s;
                top = Mathf.Max(top, Mathf.Max(Mathf.Max(_world.Shape.Height(x0, z0), _world.Shape.Height(x0 + s, z0)),
                    Mathf.Max(_world.Shape.Height(x0, z0 + s), _world.Shape.Height(x0 + s, z0 + s))) + 0.25f);
            }
            grid.PlaceFoundation(room, top);
            grid.PlaceFoundation(stairs, top);
            var opposite = sideways ? (doorway.Equals(room.East) ? room.West : room.East) : (doorway.Equals(room.North) ? room.South : room.North);
            for (int level = 0; level <= 1; level++)
            {
                foreach (var cell in new[] { new Cell(room.X, room.Z, level), new Cell(stairs.X, stairs.Z, level) })
                    foreach (var edge in cell.Edges)
                    {
                        // The two squares are one room: nothing between them.
                        bool between = (edge.CellA.Ground.Equals(room) && edge.CellB.Ground.Equals(stairs))
                                       || (edge.CellA.Ground.Equals(stairs) && edge.CellB.Ground.Equals(room));
                        if (between || grid.HasEdgePiece(edge)) continue;
                        bool roomSide = cell.Ground.Equals(room);
                        var type = level == 0
                            ? (edge.Equals(doorway) ? PieceType.Doorway : roomSide && edge.Equals(opposite) ? PieceType.Window : PieceType.Wall)
                            : (roomSide ? PieceType.Window : PieceType.Wall);
                        var placed = grid.PlaceEdge(edge, type);
                        if (placed != PlaceResult.Ok) Debug.Log($"[Oxfall] Tower: {type} at {edge}: {placed}");
                    }
                if (level == 0)
                {
                    grid.PlaceRoof(room);
                    grid.PlaceStairs(stairs, facing);
                }
            }
            grid.PlaceRoof(new Cell(room.X, room.Z, 1));
            grid.PlaceRoof(new Cell(stairs.X, stairs.Z, 1));
            _towerStairs = stairs;
            _towerRoom = room;
            Debug.Log($"[Oxfall] Test tower: room {room}, stairs {stairs} facing {facing}, doorway {doorway}, pieces={grid.PieceCount}");
        }

        /// <summary>For tests: drops the player onto the middle of the test tower's stairs, to see the steps hold them.</summary>
        private void StandOnStairs()
        {
            var grid = _buildings.Grid;
            if (!grid.TryGetFloorHeight(_towerStairs, out float floor)) return;
            var turn = BuildingViews.StairsRotation(grid.StairsFacing(_towerStairs));
            // Step 8 of 16, half way along it.
            float a = (8.5f * 360f / MeshFactory.SpiralSteps) * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Sin(a) * 0.9f, 0f, Mathf.Cos(a) * 0.9f);
            var at = new Vector3((_towerStairs.X + 0.5f) * BuildingGrid.CellSize, floor + 2.6f, (_towerStairs.Z + 0.5f) * BuildingGrid.CellSize) + turn * local;
            _player.Teleport(at, _player.transform.eulerAngles.y, 10f);
            Debug.Log($"[Oxfall] Dropped onto the stairs at {at}; step 9 top is {floor + 9 * BuildingViews.GroundRise / MeshFactory.SpiralSteps:0.00}");
        }

        /// <summary>For tests: puts the player on the test tower's upper floor, looking out of a window.</summary>
        private void ToTowerTop()
        {
            var grid = _buildings.Grid;
            var upper = new Cell(_towerRoom.X, _towerRoom.Z, 1);
            if (!grid.TryGetFloorHeight(upper, out float floor)) return;
            var at = new Vector3((upper.X + 0.5f) * BuildingGrid.CellSize, floor + 0.05f, (upper.Z + 0.5f) * BuildingGrid.CellSize);
            var stairsAt = new Vector3((_towerStairs.X + 0.5f) * BuildingGrid.CellSize, at.y, (_towerStairs.Z + 0.5f) * BuildingGrid.CellSize);
            var away = at - stairsAt;
            _player.Teleport(at, Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, 5f);
            Debug.Log($"[Oxfall] On the tower's upper floor at {at}");
        }

        /// <summary>For tests: takes an item in hand, moving it from the backpack to the last hotbar slot if need be.</summary>
        private void HoldItem(string id)
        {
            if (_hotbar.SelectItem(id)) return;
            for (int slot = Inventory.HotbarSize; slot < _inventory.CarrySlots; slot++)
            {
                if (_inventory[slot].ItemId != id) continue;
                _actions.MoveSlot(slot, Inventory.HotbarSize - 1);
                // Select would put away what is in hand if that slot is the one chosen already.
                if (!_hotbar.SelectItem(id) && _hotbar.Selected != Inventory.HotbarSize - 1) _hotbar.Select(Inventory.HotbarSize - 1);
                return;
            }
        }

        /// <summary>For tests: turns the player, where they stand, to look at the nearest wall or doorway.</summary>
        private void AimAtPiece(PieceType type, float height)
        {
            var eye = _player.Head.position;
            float best = float.MaxValue;
            Vector3 target = Vector3.zero;
            foreach (var pair in _buildings.Grid.EdgePieces)
            {
                if (pair.Value != type) continue;
                var mid = BuildingViews.EdgeMid(pair.Key);
                mid.y = _buildings.Grid.EdgeBaseHeight(pair.Key) + height;
                float d = (mid - eye).sqrMagnitude;
                if (d < best) { best = d; target = mid; }
            }
            if (best == float.MaxValue) return;
            var to = target - eye;
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            _player.Teleport(_player.transform.position, yaw, pitch);
            Debug.Log($"[Oxfall] Aimed at {type} {target} from {eye}: yaw {yaw:0} pitch {pitch:0}");
        }

        /// <summary>For tests: raises every piece to a tier, free.</summary>
        private void UpgradeAll(BuildingTier tier)
        {
            var pieces = new System.Collections.Generic.List<PieceRef>();
            foreach (var f in _buildings.Grid.Foundations) pieces.Add(PieceRef.Foundation(f.Key));
            foreach (var e in _buildings.Grid.EdgePieces) pieces.Add(PieceRef.OnEdge(e.Key, e.Value));
            foreach (var r in _buildings.Grid.Roofs) pieces.Add(PieceRef.Roof(r));
            foreach (var c in _buildings.Grid.Stairs) pieces.Add(PieceRef.Stairs(c));
            foreach (var piece in pieces) _buildings.Grid.Upgrade(piece, tier);
        }

        private void TryCraft(Recipe recipe)
        {
            string item = _catalog.Get(recipe.OutputItemId).DisplayName;
            var result = _actions.Craft(recipe);
            switch (result)
            {
                case ActionResult.Ok:
                    _hud.ShowToast($"Fabricando {item}… {recipe.CraftSeconds:0} s");
                    break;
                case ActionResult.NotEnoughResources:
                    _hud.ShowToast("Faltan materiales para " + item);
                    break;
                default:
                    _hud.ShowToast(GameActions.Explain(result));
                    break;
            }
        }

        private void SetQuality(QualityTier tier)
        {
            _quality.Apply(tier);
            _hud.SetQuality((int)tier);
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
            // Online, hunger and thirst run on the server; the copy here is updated from it.
            bool running = _input.Sprint && _input.Move.sqrMagnitude > 0.01f;
            if (!_actions.Online) _vitals.Tick(dt, running ? 1.8f : 1f);
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
            TickMenuView(Time.deltaTime);
            if (_title == null) TickVitals(Time.deltaTime);
            _vitalsUi?.Tick();
            _keypad?.Tick(Time.unscaledDeltaTime);
            // Where this player looks and what they hold, for the others to see.
            if (_net != null && _player != null && _hotbar != null)
            {
                _net.LocalPitch = -_player.Pitch;
                var held = _hotbar.Held;
                _net.LocalHeld = held.IsEmpty || _vitals.IsDead ? "" : held.ItemId;
            }
            TickNet();
            TickAnimals(Time.deltaTime);
            if (_map != null && _map.IsOpen) RefreshMap();
            _chat?.Tick(Time.unscaledDeltaTime);

            // Charges thrown alone burn down here (online the server sets them off).
            _actions?.TickCharges();
            // The gun in hand: its rounds and reloading show on the attack button, and reload when it has room.
            if (_hotbar != null && _actions != null && Firearms.TryGet(_hotbar.HeldTool, out var heldGun))
            {
                string label = GunLabel(heldGun);
                if (label != _gunLabel)
                {
                    _gunLabel = label;
                    _hud.SetAttackLabel(label);
                }
                var state = _actions.GunState(heldGun.Tool);
                int carried = _inventory.Count(heldGun.Ammo);
                _hud.SetReloadButton(heldGun.Capacity > 1 && carried > 0 && !state.Reloading(Time.time)
                                     && state.Loaded(heldGun, Time.time) < Mathf.Min(heldGun.Capacity, carried));
            }
            else _hud?.SetReloadButton(false);
            // Trees and rocks grow back on their own timers; checking once a second is plenty.
            _crafting?.Tick(Time.deltaTime);
            // Furnaces burn here alone; online the server burns them and says what came out.
            if (_settlement != null && !_actions.Online) _settlement.Tick(Time.deltaTime);
            _respawnTimer += Time.deltaTime;
            if (_respawnTimer < RespawnTickSeconds || _world == null) return;
            CloseWhatIsFar();
            // Online, the server grows things back and says so.
            if (_net != null && _net.State == NetClient.Status.Online) { _respawnTimer = 0f; return; }
            foreach (var node in _world.Nodes) node.TickRespawn(_respawnTimer);
            _respawnTimer = 0f;
        }
    }
}
