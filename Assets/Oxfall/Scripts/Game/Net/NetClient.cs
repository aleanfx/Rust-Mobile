using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Game.Rendering;
using UnityEngine;
using Pose = Oxfall.Core.Net.Pose;

namespace Oxfall.Game.Net
{
    /// <summary>
    /// The game's link to the server: says hello, sends where the player is
    /// <see cref="Protocol.SendRate"/> times a second, and keeps a figure for every other player
    /// from the snapshots. Everything happens on the main thread, polled once a frame.
    /// </summary>
    public sealed class NetClient : MonoBehaviour
    {
        public enum Status { Off, Connecting, Online, Failed }

        private GameSocket _socket;
        private string _name;
        private MaterialLibrary _materials;
        private Transform _local;
        private Transform _others;
        private readonly NetWriter _writer = new NetWriter();
        private readonly Dictionary<ushort, RemotePlayer> _players = new Dictionary<ushort, RemotePlayer>();
        private readonly List<(ushort Id, Pose Pose, bool Down, float Pitch)> _snapshot = new List<(ushort, Pose, bool, float)>();
        private readonly ServerClock _clock = new ServerClock();
        private float _sendTimer;
        private bool _teleportPending;
        /// <summary>
        /// How long the connection has been trying, counting at most a fifth of a second per frame: a
        /// slow phone that stalls for seconds while the island loads must not give up on a server that
        /// would have answered.
        /// </summary>
        private float _connectWaited;

        public Status State { get; private set; }
        public ushort PlayerId { get; private set; }
        public int Seed { get; private set; }
        /// <summary>What marks this player's buildings, bags and cupboards on the server (stays the same between visits).</summary>
        public int Owner { get; private set; }
        public string Url { get; private set; }
        public string FailReason { get; private set; }
        public IReadOnlyDictionary<ushort, RemotePlayer> Players => _players;

        /// <summary>Where this player looks (degrees up), sent with each move so others see where they aim.</summary>
        public float LocalPitch { get; set; }

        /// <summary>What this player holds (an item id; empty for bare hands), told to the server when it changes.</summary>
        public string LocalHeld { get; set; } = "";
        private string _sentHeld;

        /// <summary>The look of an item, to put it in other players' hands (null if it has none).</summary>
        public Func<string, Oxfall.Game.Items.ItemModel> Models { get; set; }

        /// <summary>Everyone online, this player included.</summary>
        public int OnlineCount => State == Status.Online ? _players.Count + 1 : 0;

        /// <summary>The status changed (connected, turned away, lost).</summary>
        public event Action<Status> StatusChanged;

        /// <summary>The server put the player back (a move it did not believe).</summary>
        public event Action<Pose> Corrected;

        /// <summary>How much is left in some trees, rocks or plants (0: gone until they grow back).</summary>
        public event Action<List<(int Id, int Remaining)>> NodesChanged;

        /// <summary>What one of this player's hits gave (it is already in the inventory the server keeps).</summary>
        public event Action<List<(string ItemId, int Amount)>> Gathered;

        /// <summary>The server let the player in, at this spot (where they were last time, or a beach).</summary>
        public event Action<Pose> Welcomed;

        /// <summary>Inventory slots the server changed.</summary>
        public event Action<List<(int Slot, ItemStack Stack)>> SlotsChanged;

        /// <summary>The crafting queue as the server runs it: (recipe id, seconds done).</summary>
        public event Action<List<(string RecipeId, float Elapsed)>> CraftsChanged;

        /// <summary>A job finished on the server: recipe id, and how many did not fit.</summary>
        public event Action<string, int> Crafted;

        /// <summary>Health, food and water as the server has them.</summary>
        public event Action<float, float, float> VitalsChanged;

        /// <summary>Something this player asked for did not happen.</summary>
        public event Action<ActionResult> ActionFailed;

        /// <summary>Changes to what stands on the island, to apply in order.</summary>
        public event Action<List<BuildOp>> Built;

        /// <summary>The island's clock as the server has it (0 to 24).</summary>
        public event Action<float> TimeOfDay;

        /// <summary>The open box or furnace: (id, slot count, slots that changed); id 0 when it closed.</summary>
        public event Action<int, int, List<(int Slot, ItemStack Stack)>> Looted;

        /// <summary>Someone was hurt: target, attacker (0: nobody), damage, whether it killed them.</summary>
        public event Action<ushort, ushort, int, bool> Hurt;

        /// <summary>The animals near this player now (the list is reused: copy what is kept).</summary>
        public event Action<List<AnimalSnap>> AnimalsSeen;

        /// <summary>What this player's blow at an animal did: its id, the blow, the damage.</summary>
        public event Action<int, AnimalBlow, int> AnimalHit;

        /// <summary>An animal hurt someone: who, what kind of animal, how much, whether it killed them.</summary>
        public event Action<ushort, AnimalKind, int, bool> Bitten;

        private readonly List<AnimalSnap> _animals = new List<AnimalSnap>();
        private readonly List<SleeperSnap> _sleepers = new List<SleeperSnap>();

        private readonly string[] _outfit = new string[Inventory.WearSlots];

        /// <summary>Everyone asleep on the island (sent whole whenever it changes).</summary>
        public event Action<List<SleeperSnap>> SleepersSeen;

        /// <summary>A sleeper was hurt: whose body (owner), attacker id (0: long ago), their name, damage, killed.</summary>
        public event Action<int, ushort, string, int, bool> SleeperHit;

        /// <summary>A line of chat: from whom (0: the server), their name, what, and whether only the team heard it.</summary>
        public event Action<ushort, string, string, bool> ChatLine;

        /// <summary>This player's team as the server tells it: its id (0: none), leader, members, and who asked them in ("" for nobody).</summary>
        public event Action<int, int, List<TeamMate>, string> TeamChanged;

        private readonly List<TeamMate> _teamMates = new List<TeamMate>();

        /// <summary>Someone came or went (their name).</summary>
        public event Action<string> PlayerJoined;
        public event Action<string> PlayerLeft;

        /// <summary>Someone else loosed an arrow: from where, which way (yaw, pitch), for it to be seen flying.</summary>
        public event Action<Vector3, float, float> ArrowSeen;

        /// <summary>A satchel charge stuck somewhere and fizzing (anyone's, this player's own too), and the seconds left on its fuse.</summary>
        public event Action<LiveCharge, float> ChargeArmed;

        /// <summary>A charge went off: its id and where.</summary>
        public event Action<int, Vector3> Blasted;

        /// <summary>Someone else fired a gun: who, which kind, from where, which way (yaw, pitch) and the seed of its spray.</summary>
        public event Action<ushort, ToolKind, Vector3, float, float, ushort> Gunshot;

        /// <summary>A supply plane on its way (its times put on this machine's clock).</summary>
        public event Action<Oxfall.Core.World.Airdrop> AirdropSeen;

        private readonly List<(int, ItemStack)> _loot = new List<(int, ItemStack)>();

        private readonly List<(int Id, int Remaining)> _nodeStates = new List<(int, int)>();
        private readonly List<(string ItemId, int Amount)> _gathered = new List<(string, int)>();
        private readonly List<(int Slot, ItemStack Stack)> _slots = new List<(int, ItemStack)>();
        private readonly List<(string RecipeId, float Elapsed)> _crafts = new List<(string, float)>();
        private readonly List<BuildOp> _ops = new List<BuildOp>();

        /// <summary>A swing at a node; the server answers with what it gave.</summary>
        public void SendHit(int nodeId, ToolKind tool)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Hit(_writer, nodeId, (byte)tool);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        /// <summary>A blow at another player; the server judges it and tells everyone if it landed.</summary>
        public void SendStrike(ushort target, ToolKind tool)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Strike(_writer, target, (byte)tool);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendStrikeSleeper(int owner, ToolKind tool)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.StrikeSleeper(_writer, owner, (byte)tool);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendChat(string text, bool team = false)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Chat(_writer, text, team);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendTeam(TeamOp op, int arg)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.TeamAct(_writer, op, arg);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendShoot(Vector3 origin, float yaw, float pitch)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Shoot(_writer, origin.x, origin.y, origin.z, yaw, pitch);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendThrow(ChargeTarget target, Vector3 at, Vector3 normal)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Throw(_writer, target, at.x, at.y, at.z, normal.x, normal.y, normal.z);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendFire(ToolKind tool, Vector3 origin, float yaw, float pitch, ushort seed)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Fire(_writer, tool, origin.x, origin.y, origin.z, yaw, pitch, seed);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendGunHit(ArrowTarget kind, int target, int pellets)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.GunHit(_writer, kind, target, pellets);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendReload(ToolKind tool)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Reload(_writer, tool);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendArrowHit(ArrowTarget kind, int target)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.ArrowHit(_writer, kind, target);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void SendStrikeAnimal(int animal, ToolKind tool)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.StrikeAnimal(_writer, animal, (byte)tool);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        /// <summary>A player's name by id (this player's own included), or null.</summary>
        public string NameOf(ushort id) => id == PlayerId ? "tú" : _players.TryGetValue(id, out var p) ? p.Name : null;

        /// <summary>Something done to the player's own belongings; the server answers with the change.</summary>
        public void SendAct(ActKind kind, int a = 0, int b = 0, string recipe = null)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Act(_writer, kind, a, b, recipe);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        /// <summary>A change to what stands on the island; the server tells everyone if it was made.</summary>
        public void SendBuild(BuildOp op)
        {
            if (State != Status.Online || _socket == null) return;
            Messages.Build(_writer, op);
            _socket.Send(_writer.Buffer, _writer.Length);
        }

        public void Init(MaterialLibrary materials, Transform localPlayer)
        {
            _materials = materials;
            _local = localPlayer;
            _others = new GameObject("Other players").transform;
        }

        public void Connect(string url, string playerName)
        {
            Disconnect();
            Url = url;
            _name = playerName;
            FailReason = null;
            _helloSent = false;
            _socket = GameSocket.Open(url);
            _connectWaited = 0f;
            SetState(Status.Connecting);
            Debug.Log("[Oxfall] Connecting to " + url);
        }

        /// <summary>The same server again, under the same name (after <see cref="Disconnect"/>).</summary>
        public void Reconnect()
        {
            if (!string.IsNullOrEmpty(Url)) Connect(Url, _name);
        }

        public void Disconnect()
        {
            _socket?.Dispose();
            _socket = null;
            foreach (var p in _players.Values) p.Destroy();
            _players.Clear();
            PlayerId = 0;
            if (State != Status.Failed) SetState(Status.Off);
        }

        /// <summary>The next move tells the server the player was moved on purpose (woke up elsewhere).</summary>
        public void MarkTeleport() => _teleportPending = true;

        private void SetState(Status state)
        {
            if (State == state) return;
            State = state;
            StatusChanged?.Invoke(state);
        }

        private void Fail(string reason)
        {
            FailReason = reason;
            Debug.Log("[Oxfall] Offline: " + reason);
            _socket?.Dispose();
            _socket = null;
            foreach (var p in _players.Values) p.Destroy();
            _players.Clear();
            SetState(Status.Failed);
        }

        private void Update()
        {
            if (_socket == null) return;

            if (State == Status.Connecting && _socket.State == GameSocket.Status.Connecting
                && (_connectWaited += Mathf.Min(Time.unscaledDeltaTime, 0.2f)) > 8f)
            {
                Fail("No se pudo conectar con el servidor.");
                return;
            }
            if (_socket.State == GameSocket.Status.Closed)
            {
                Fail(State == Status.Online ? "Se perdió la conexión con el servidor." : "No se pudo conectar con el servidor.");
                return;
            }
            if (_socket.State != GameSocket.Status.Open) return;

            if (State == Status.Connecting && PlayerId == 0 && !_helloSent)
            {
                Messages.Hello(_writer, _name, Token);
                _socket.Send(_writer.Buffer, _writer.Length);
                _helloSent = true;
            }

            // Everything that arrived since the last frame.
            for (int guard = 0; guard < 256; guard++)
            {
                var message = _socket?.Receive();
                if (message == null) break;
                try { Handle(message); }
                catch (ProtocolException e)
                {
                    Fail("Mensaje inválido del servidor: " + e.Message);
                    return;
                }
                catch (Exception e)
                {
                    // A handler in the game failed: logged, and the connection carries on.
                    Debug.LogException(e);
                }
            }
            if (_socket == null || State != Status.Online) return;

            float now = _clock.Now(Time.unscaledTime) - PoseBuffer.Delay;
            float dt = Time.deltaTime;
            foreach (var p in _players.Values) p.Draw(now, dt);

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f && _local != null)
            {
                _sendTimer += 1f / Protocol.SendRate;
                if (_sendTimer < 0f) _sendTimer = 0f;
                var p = _local.position;
                Messages.Move(_writer, new Pose(p.x, p.y, p.z, _local.eulerAngles.y), _teleportPending, LocalPitch);
                _socket.Send(_writer.Buffer, _writer.Length);
                _teleportPending = false;
                if (LocalHeld != _sentHeld)
                {
                    _sentHeld = LocalHeld;
                    Messages.Hold(_writer, LocalHeld);
                    _socket.Send(_writer.Buffer, _writer.Length);
                }
            }
        }

        private bool _helloSent;

        private void Handle(byte[] message)
        {
            var reader = new NetReader(message, 1, message.Length - 1);
            switch (Protocol.PeekType(message, message.Length))
            {
                case MessageType.Welcome:
                {
                    Messages.ReadWelcome(reader, out var id, out var seed, out var serverTime, out var pose, out int owner);
                    PlayerId = id;
                    Seed = seed;
                    Owner = owner;
                    _clock.Observe(serverTime, Time.unscaledTime);
                    _teleportPending = false;
                    _sentHeld = null;
                    SetState(Status.Online);
                    Debug.Log($"[Oxfall] Online as player {id} on {Url} (seed {seed}) at {pose}");
                    Welcomed?.Invoke(pose);
                    break;
                }
                case MessageType.Rejected:
                    Fail(Messages.ReadRejected(reader));
                    break;
                case MessageType.Joined:
                {
                    Messages.ReadJoined(reader, out var id, out var name, out _);
                    if (id == PlayerId || _players.ContainsKey(id)) break;
                    _players[id] = new RemotePlayer(id, name, _materials, _others);
                    Debug.Log($"[Oxfall] {name} joined ({OnlineCount} online)");
                    PlayerJoined?.Invoke(name);
                    break;
                }
                case MessageType.Left:
                {
                    var id = Messages.ReadLeft(reader);
                    if (!_players.TryGetValue(id, out var player)) break;
                    player.Destroy();
                    _players.Remove(id);
                    Debug.Log($"[Oxfall] {player.Name} left ({OnlineCount} online)");
                    PlayerLeft?.Invoke(player.Name);
                    break;
                }
                case MessageType.Snapshot:
                {
                    float time = Messages.ReadSnapshot(reader, _snapshot);
                    _clock.Observe(time, Time.unscaledTime);
                    foreach (var (id, pose, down, pitch) in _snapshot)
                    {
                        if (!_players.TryGetValue(id, out var player)) continue;
                        player.Poses.Add(time, pose);
                        player.SetDown(down);
                        player.SetPitch(pitch);
                    }
                    break;
                }
                case MessageType.NodeStates:
                    Messages.ReadNodeStates(reader, _nodeStates);
                    NodesChanged?.Invoke(_nodeStates);
                    break;
                case MessageType.Gathered:
                    Messages.ReadGathered(reader, _gathered);
                    Gathered?.Invoke(_gathered);
                    break;
                case MessageType.Slots:
                    Messages.ReadSlots(reader, _slots);
                    SlotsChanged?.Invoke(_slots);
                    break;
                case MessageType.Crafts:
                    Messages.ReadCrafts(reader, _crafts);
                    CraftsChanged?.Invoke(_crafts);
                    break;
                case MessageType.Crafted:
                {
                    string recipe = Messages.ReadCrafted(reader, out int lost);
                    Crafted?.Invoke(recipe, lost);
                    break;
                }
                case MessageType.VitalsState:
                {
                    Messages.ReadVitals(reader, out float health, out float food, out float water);
                    VitalsChanged?.Invoke(health, food, water);
                    break;
                }
                case MessageType.Failed:
                    ActionFailed?.Invoke(Messages.ReadFailed(reader));
                    break;
                case MessageType.Built:
                    Messages.ReadBuilt(reader, _ops);
                    Built?.Invoke(_ops);
                    break;
                case MessageType.TimeOfDay:
                    TimeOfDay?.Invoke(Messages.ReadTimeOfDay(reader));
                    break;
                case MessageType.Hurt:
                {
                    Messages.ReadHurt(reader, out ushort target, out ushort attacker, out int damage, out bool killed);
                    if (_players.TryGetValue(target, out var victim)) victim.Flinch();
                    Hurt?.Invoke(target, attacker, damage, killed);
                    break;
                }
                case MessageType.Held:
                {
                    Messages.ReadHeld(reader, out ushort who, out string item);
                    if (_players.TryGetValue(who, out var holder)) holder.SetHeld(item, string.IsNullOrEmpty(item) ? null : Models?.Invoke(item));
                    break;
                }
                case MessageType.Outfit:
                {
                    Messages.ReadOutfit(reader, out ushort who, _outfit);
                    if (_players.TryGetValue(who, out var dressed)) dressed.SetOutfit(_outfit);
                    break;
                }
                case MessageType.Sleepers:
                    Messages.ReadSleepers(reader, _sleepers);
                    SleepersSeen?.Invoke(_sleepers);
                    break;
                case MessageType.SleeperHit:
                {
                    Messages.ReadSleeperHit(reader, out int owner, out ushort attacker, out string by, out int damage, out bool killed);
                    SleeperHit?.Invoke(owner, attacker, by, damage, killed);
                    break;
                }
                case MessageType.Animals:
                    Messages.ReadAnimals(reader, _animals);
                    AnimalsSeen?.Invoke(_animals);
                    break;
                case MessageType.AnimalHit:
                {
                    Messages.ReadAnimalHit(reader, out int animal, out var blow, out int damage);
                    AnimalHit?.Invoke(animal, blow, damage);
                    break;
                }
                case MessageType.ChatLine:
                {
                    Messages.ReadChatLine(reader, out ushort from, out string name, out string text, out bool team);
                    ChatLine?.Invoke(from, name, text, team);
                    break;
                }
                case MessageType.TeamState:
                {
                    Messages.ReadTeamState(reader, out int teamId, out int leader, _teamMates, out string inviteFrom);
                    TeamChanged?.Invoke(teamId, leader, _teamMates, inviteFrom);
                    break;
                }
                case MessageType.Arrow:
                {
                    Messages.ReadArrow(reader, out ushort shooter, out float ax, out float ay, out float az, out float yaw, out float pitch);
                    if (shooter != PlayerId) ArrowSeen?.Invoke(new Vector3(ax, ay, az), yaw, pitch);
                    break;
                }
                case MessageType.ChargeArmed:
                {
                    var charge = Messages.ReadChargeArmed(reader, out float left);
                    ChargeArmed?.Invoke(charge, left);
                    break;
                }
                case MessageType.Gunshot:
                {
                    Messages.ReadGunshot(reader, out ushort shooter, out var tool, out float gx, out float gy, out float gz,
                        out float yaw, out float pitch, out ushort seed);
                    if (shooter != PlayerId) Gunshot?.Invoke(shooter, tool, new Vector3(gx, gy, gz), yaw, pitch, seed);
                    break;
                }
                case MessageType.Blast:
                {
                    Messages.ReadBlast(reader, out int id, out float bx, out float by, out float bz);
                    Blasted?.Invoke(id, new Vector3(bx, by, bz));
                    break;
                }
                case MessageType.Bitten:
                {
                    Messages.ReadBitten(reader, out ushort target, out var kind, out int damage, out bool killed);
                    if (_players.TryGetValue(target, out var victim)) victim.Flinch();
                    Bitten?.Invoke(target, kind, damage, killed);
                    break;
                }
                case MessageType.Airdrop:
                {
                    AirdropSeen?.Invoke(Messages.ReadAirdrop(reader, Time.time));
                    break;
                }
                case MessageType.Loot:
                {
                    int box = Messages.ReadLoot(reader, out int slotCount, _loot);
                    Looted?.Invoke(box, slotCount, _loot);
                    break;
                }
                case MessageType.Correction:
                {
                    var pose = Messages.ReadCorrection(reader);
                    Debug.Log("[Oxfall] The server put the player back at " + pose);
                    Corrected?.Invoke(pose);
                    break;
                }
            }
        }

        private void OnDestroy() => _socket?.Dispose();

        /// <summary>
        /// This phone's own secret, made the first time and kept: it is how the server knows the
        /// player's survivor when they come back.
        /// </summary>
        public static string Token
        {
            get
            {
                string token = PlayerPrefs.GetString("oxfall.token", "");
                if (Protocol.IsValidToken(token)) return token;
                token = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("oxfall.token", token);
                PlayerPrefs.Save();
                return token;
            }
        }

        /// <summary>The game's own server, once it is up (empty until then).</summary>
        public const string OfficialServer = "";

        /// <summary>A parameter of the web page's address (<c>?solo</c> gives ""), or null if absent.</summary>
        public static string UrlParam(string key)
        {
            string url = Application.absoluteURL;
            int q = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');
            if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                int eq = part.IndexOf('=');
                string k = eq < 0 ? part : part.Substring(0, eq);
                if (k != key) continue;
                return eq < 0 ? "" : Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
            }
            return null;
        }

        /// <summary>
        /// The server to play on: <c>?server=wss://…</c> in the web page's address, or what was saved
        /// on the phone. Empty means play alone.
        /// </summary>
        public static string ChosenServer(out string playerName)
        {
            playerName = PlayerPrefs.GetString("oxfall.name", "");
            string server = PlayerPrefs.GetString("oxfall.server", OfficialServer);
            string url = Application.absoluteURL;
            int q = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');
            if (q >= 0)
            {
                foreach (var part in url.Substring(q + 1).Split('&', '#'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = part.Substring(0, eq), value = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                    if (key == "server") server = value;
                    else if (key == "name") playerName = value;
                }
            }
            if (string.IsNullOrEmpty(playerName)) playerName = "Superviviente";
            return server;
        }
    }
}
