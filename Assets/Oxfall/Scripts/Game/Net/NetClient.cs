using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
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
        private readonly List<(ushort Id, Pose Pose)> _snapshot = new List<(ushort, Pose)>();
        private readonly ServerClock _clock = new ServerClock();
        private float _sendTimer;
        private bool _teleportPending;
        private float _connectStarted;

        public Status State { get; private set; }
        public ushort PlayerId { get; private set; }
        public int Seed { get; private set; }
        public string Url { get; private set; }
        public string FailReason { get; private set; }
        public IReadOnlyDictionary<ushort, RemotePlayer> Players => _players;

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
            _connectStarted = Time.unscaledTime;
            SetState(Status.Connecting);
            Debug.Log("[Oxfall] Connecting to " + url);
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
                && Time.unscaledTime - _connectStarted > 8f)
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
                Messages.Move(_writer, new Pose(p.x, p.y, p.z, _local.eulerAngles.y), _teleportPending);
                _socket.Send(_writer.Buffer, _writer.Length);
                _teleportPending = false;
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
                    Messages.ReadWelcome(reader, out var id, out var seed, out var serverTime, out var pose);
                    PlayerId = id;
                    Seed = seed;
                    _clock.Observe(serverTime, Time.unscaledTime);
                    _teleportPending = false;
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
                    break;
                }
                case MessageType.Left:
                {
                    var id = Messages.ReadLeft(reader);
                    if (!_players.TryGetValue(id, out var player)) break;
                    player.Destroy();
                    _players.Remove(id);
                    Debug.Log($"[Oxfall] {player.Name} left ({OnlineCount} online)");
                    break;
                }
                case MessageType.Snapshot:
                {
                    float time = Messages.ReadSnapshot(reader, _snapshot);
                    _clock.Observe(time, Time.unscaledTime);
                    foreach (var (id, pose) in _snapshot)
                        if (_players.TryGetValue(id, out var player)) player.Poses.Add(time, pose);
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

        /// <summary>
        /// The server to play on: <c>?server=wss://…</c> in the web page's address, or what was saved
        /// on the phone. Empty means play alone.
        /// </summary>
        public static string ChosenServer(out string playerName)
        {
            playerName = PlayerPrefs.GetString("oxfall.name", "");
            string server = PlayerPrefs.GetString("oxfall.server", "");
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
