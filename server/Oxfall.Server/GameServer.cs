using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Server
{
    /// <summary>
    /// The running game: one <see cref="ServerWorld"/> shared by every connection, guarded by a lock,
    /// and a loop that moves time on and sends everyone a snapshot <see cref="Protocol.TickRate"/>
    /// times a second. The rules themselves live in Core; this only moves messages in and out.
    /// </summary>
    public sealed class GameServer
    {
        private const int MaxMessageBytes = 4096;
        private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
        private const float CorrectionInterval = 0.5f;
        /// <summary>Taps in menus come fast, but not this fast: more in one second ends the connection.</summary>
        private const int MaxActionsPerSecond = 40;
        /// <summary>Build changes per message when describing the whole settlement to a newcomer.</summary>
        private const int OpsPerMessage = 200;
        /// <summary>How often the island is saved (and once more when the server stops).</summary>
        private const float SaveInterval = 60f;

        private readonly ServerWorld _world;
        private readonly string _savePath;
        private readonly object _saveLock = new object();
        private readonly ServerState _state;
        private readonly object _lock = new object();
        private readonly Dictionary<ushort, Session> _sessions = new Dictionary<ushort, Session>();
        private readonly string[] _worn = new string[Inventory.WearSlots];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ILogger _log;
        private readonly NetWriter _flushWriter = new NetWriter(1024);

        /// <param name="savePath">Where the island is saved; null keeps nothing between runs.</param>
        public GameServer(int seed, bool devMode, string savePath, ILogger log)
        {
            _log = log;
            _savePath = savePath;
            _world = LoadOrCreate(seed, devMode);
            _state = _world.Players;
            _log.LogInformation("Island {Seed}: {Nodes} trees, rocks and plants, {Pieces} building pieces, {Survivors} survivors{Dev}",
                seed, _world.NodeCount, _world.Settlement.Grid.PieceCount, _state.Count + CountRecords(),
                devMode ? " (DEV MODE: teleports allowed, one beach for everyone)" : "");
        }

        private int CountRecords()
        {
            int n = 0;
            foreach (var _ in _world.Records) n++;
            return n;
        }

        /// <summary>The saved island if there is one for this seed; otherwise a fresh one (a wipe).</summary>
        private ServerWorld LoadOrCreate(int seed, bool devMode)
        {
            if (!string.IsNullOrEmpty(_savePath) && File.Exists(_savePath))
            {
                var loaded = new ServerWorld(seed) { DevMode = devMode };
                if (loaded.Load(File.ReadAllBytes(_savePath), out var why))
                {
                    _log.LogInformation("Loaded the island from {Path}", _savePath);
                    return loaded;
                }
                // Kept aside rather than overwritten, in case it is wanted.
                string aside = _savePath + ".old";
                File.Copy(_savePath, aside, true);
                _log.LogWarning("Not loading {Path} ({Why}): a fresh island this time; the old save is in {Aside}", _savePath, why, aside);
            }
            return new ServerWorld(seed) { DevMode = devMode };
        }

        /// <summary>Writes the island to disk now (safely: a new file, then swapped in).</summary>
        public void SaveNow()
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            byte[] bytes;
            lock (_lock) bytes = _world.Save();
            lock (_saveLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(_savePath));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string temp = _savePath + ".tmp";
                    File.WriteAllBytes(temp, bytes);
                    File.Move(temp, _savePath, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    _log.LogError("Could not save the island to {Path}: {Message}", _savePath, e.Message);
                }
            }
        }

        public int Seed => _state.Seed;

        public int PlayerCount
        {
            get { lock (_lock) return _state.Count; }
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        /// <summary>The least time between two chat lines from one player.</summary>
        private const float ChatInterval = 1.2f;

        /// <summary>Moves time on and sends everyone a snapshot, every tick, until stopped.</summary>
        public async Task RunTicks(CancellationToken stop)
        {
            var writer = new NetWriter(2048);
            var period = TimeSpan.FromSeconds(1.0 / Protocol.TickRate);
            using var timer = new PeriodicTimer(period);
            float lastTick = Now, lastRespawn = Now, lastSave = Now, lastClock = Now;
            long ticks = 0;
            while (await timer.WaitForNextTickAsync(stop))
            {
                if (Now - lastSave >= SaveInterval)
                {
                    lastSave = Now;
                    _ = Task.Run(SaveNow);
                }
                lock (_lock)
                {
                    float now = Now;
                    _world.Tick(now - lastTick);
                    lastTick = now;
                    ticks++;
                    // Animals' bites, for everyone to see who was hurt (the victim's health follows in their update).
                    foreach (var (target, kind, damage, killed) in _world.Bites)
                    {
                        Messages.Bitten(writer, target, kind, damage, killed);
                        Broadcast(writer.ToArray());
                    }
                    _world.Bites.Clear();
                    // Charges that went off: the bang, who it hurt; what fell follows with the announcements.
                    foreach (var blast in _world.Blasts)
                    {
                        Messages.Blast(writer, blast.Id, blast.X, blast.Y, blast.Z);
                        Broadcast(writer.ToArray());
                    }
                    _world.Blasts.Clear();
                    foreach (var (target, by, damage, killed) in _world.BlastHurts)
                    {
                        Messages.Hurt(writer, target, by, damage, killed);
                        Broadcast(writer.ToArray());
                        if (killed) _log.LogInformation("Player {Id} killed by a charge of {By}", target, by);
                    }
                    _world.BlastHurts.Clear();
                    foreach (var (owner, by, byName, damage, killed) in _world.BlastSleeperHits)
                    {
                        Messages.SleeperHit(writer, owner, by, byName, damage, killed);
                        Broadcast(writer.ToArray());
                    }
                    _world.BlastSleeperHits.Clear();
                    // A furnace that burned out, for everyone to see.
                    if (_world.Announcements.Count > 0)
                    {
                        if (_sessions.Count > 0)
                        {
                            Messages.Built(writer, _world.Announcements, 0, _world.Announcements.Count);
                            Broadcast(writer.ToArray());
                        }
                        _world.Announcements.Clear();
                    }
                    // Someone fell asleep, woke up or was killed asleep: everyone gets the new list.
                    if (_world.SleepersChanged)
                    {
                        _world.SleepersChanged = false;
                        if (_sessions.Count > 0)
                        {
                            Messages.Sleepers(writer, _world.Sleepers);
                            Broadcast(writer.ToArray());
                        }
                    }

                    // Trees and rocks grow back whether anyone is online or not; once a second is plenty.
                    if (now - lastRespawn >= 1f)
                    {
                        var back = _world.TickRespawn(now - lastRespawn);
                        lastRespawn = now;
                        if (back.Count > 0)
                        {
                            Messages.NodeStates(writer, back);
                            Broadcast(writer.ToArray());
                        }
                    }
                    if (_sessions.Count == 0) continue;
                    // The clock now and then keeps everyone's day in step (each phone runs it in between).
                    if (now - lastClock >= 30f)
                    {
                        lastClock = now;
                        Messages.TimeOfDay(writer, _world.Day.Hours);
                        Broadcast(writer.ToArray());
                    }
                    Messages.Snapshot(writer, now, _state.Snapshot());
                    Broadcast(writer.ToArray());
                    // Clothes put on or taken off, twice a second, for everyone to see.
                    if (ticks % (Protocol.TickRate / 2) == 0)
                        foreach (var session in _sessions.Values)
                        {
                            Messages.Worn(session.Record.Survivor.Inventory, _worn);
                            string outfit = string.Join("|", _worn);
                            if (outfit == session.LastOutfit) continue;
                            session.LastOutfit = outfit;
                            Messages.Outfit(writer, session.Id, _worn);
                            Broadcast(writer.ToArray());
                        }
                    // The animals near each player, five times a second (their phones walk them on in between).
                    if (ticks % (Protocol.TickRate / 5) == 0)
                        foreach (var session in _sessions.Values)
                        {
                            if (!_state.TryGet(session.Id, out var player)) continue;
                            Messages.Animals(writer, _world.Fauna.All, player.Pose.X, player.Pose.Z, ServerWorld.AnimalRange);
                            session.Connection.Send(writer.ToArray());
                        }
                    foreach (var session in _sessions.Values) Flush(session, now);
                }
            }
        }

        /// <summary>Serves one player from the handshake until they leave, break the protocol or are replaced.</summary>
        public async Task Serve(WebSocket socket, string address, CancellationToken stop)
        {
            using var life = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var connection = new Connection(socket, life);
            var sending = connection.RunSends(life.Token);
            var writer = new NetWriter();
            var buffer = new byte[MaxMessageBytes];
            ushort id = 0;
            try
            {
                // The first message must be Hello, and soon.
                using (var helloWait = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {
                    helloWait.CancelAfter(HelloTimeout);
                    int n = await Receive(socket, buffer, helloWait.Token);
                    if (n <= 0 || Protocol.PeekType(buffer, n) != MessageType.Hello) return;
                    Messages.ReadHello(new NetReader(buffer, 1, n - 1), out var version, out var name, out var token);
                    if (!TryJoin(connection, writer, version, name, token, out id)) return;
                    _log.LogInformation("Player {Id} '{Name}' joined from {Address} ({Count} online)", id, name, address, PlayerCount);
                }

                float second = 0f;
                int actions = 0;
                while (!life.IsCancellationRequested)
                {
                    int n = await Receive(socket, buffer, life.Token);
                    if (n <= 0) break;
                    var type = Protocol.PeekType(buffer, n);
                    var reader = new NetReader(buffer, 1, n - 1);
                    lock (_lock)
                    {
                        if (!_sessions.TryGetValue(id, out var session) || session.Connection != connection) break;
                        float now = Now;
                        if (type != MessageType.Move)
                        {
                            if (now - second >= 1f)
                            {
                                second = now;
                                actions = 0;
                            }
                            if (++actions > MaxActionsPerSecond) throw new ProtocolException("Too many actions.");
                        }
                        Handle(session, type, reader, writer, now);
                        Flush(session, now);
                    }
                }
            }
            catch (ProtocolException e)
            {
                _log.LogWarning("Player {Id} from {Address} broke the protocol: {Message}", id, address, e.Message);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                if (id != 0) Leave(id, connection, writer);
                connection.Complete();
                await sending;
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closing.Token);
                    }
                    catch (Exception e) when (e is WebSocketException || e is OperationCanceledException) { }
                }
            }
        }

        private void Handle(Session session, MessageType type, NetReader reader, NetWriter writer, float now)
        {
            ushort id = session.Id;
            switch (type)
            {
                case MessageType.Move:
                {
                    Messages.ReadMove(reader, out var pose, out bool teleport);
                    if (session.Record.Survivor.Vitals.IsDead) break;
                    if (_state.ApplyMove(id, pose, teleport, now) || now - session.LastCorrection < CorrectionInterval) break;
                    session.LastCorrection = now;
                    SendPose(session, writer);
                    break;
                }
                case MessageType.Hit:
                {
                    Messages.ReadHit(reader, out int nodeId, out byte tool);
                    var got = _world.Hit(id, nodeId, (ToolKind)tool, now, out int lost);
                    if (got.Count == 0) break;
                    Messages.Gathered(writer, nodeId, got);
                    session.Connection.Send(writer.ToArray());
                    if (lost > 0) Fail(session, writer, ActionResult.InventoryFull);
                    _world.TryGetNode(nodeId, out var node, out _, out _);
                    Messages.NodeStates(writer, new[] { (node.Id, node.Remaining) });
                    Broadcast(writer.ToArray());
                    break;
                }
                case MessageType.Strike:
                {
                    Messages.ReadStrike(reader, out ushort target, out byte tool);
                    if (!_world.Strike(id, target, (ToolKind)tool, now, out float damage, out bool killed)) break;
                    // Everyone sees the blow land (the victim's own health follows in their next update).
                    Messages.Hurt(writer, target, id, damage, killed);
                    Broadcast(writer.ToArray());
                    break;
                }
                case MessageType.StrikeSleeper:
                {
                    Messages.ReadStrikeSleeper(reader, out int owner, out byte tool);
                    if (!_world.StrikeSleeper(id, owner, (ToolKind)tool, now, out float damage, out bool killed)) break;
                    Messages.SleeperHit(writer, owner, id, session.Record.Name, damage, killed);
                    Broadcast(writer.ToArray());
                    if (killed) _log.LogInformation("Player {Id} '{Name}' killed sleeper {Owner}", id, session.Record.Name, owner);
                    break;
                }
                case MessageType.StrikeAnimal:
                {
                    Messages.ReadStrikeAnimal(reader, out int animal, out byte tool);
                    var blow = _world.StrikeAnimal(id, animal, (ToolKind)tool, now, out float damage, out int lost);
                    if (blow == AnimalBlow.None) break;
                    Messages.AnimalHit(writer, animal, blow, damage);
                    session.Connection.Send(writer.ToArray());
                    if (lost > 0) Fail(session, writer, ActionResult.InventoryFull);
                    break;
                }
                case MessageType.Shoot:
                {
                    Messages.ReadShoot(reader, out float x, out float y, out float z, out float yaw, out float pitch);
                    if (!_world.Shoot(id, x, y, z, now)) break;
                    // Everyone else sees the arrow fly (the shooter already does).
                    Messages.Arrow(writer, id, x, y, z, yaw, pitch);
                    var arrow = writer.ToArray();
                    foreach (var other in _sessions.Values)
                        if (other.Id != id) other.Connection.Send(arrow);
                    break;
                }
                case MessageType.ArrowHit:
                {
                    Messages.ReadArrowHit(reader, out var kind, out int target);
                    if (!_world.ArrowHit(id, kind, target, now, out float damage, out bool killed, out var blow)) break;
                    if (kind == ArrowTarget.Player)
                    {
                        Messages.Hurt(writer, (ushort)target, id, damage, killed);
                        Broadcast(writer.ToArray());
                    }
                    else if (kind == ArrowTarget.Sleeper)
                    {
                        Messages.SleeperHit(writer, target, id, session.Record.Name, damage, killed);
                        Broadcast(writer.ToArray());
                    }
                    else
                    {
                        Messages.AnimalHit(writer, target, blow, damage);
                        session.Connection.Send(writer.ToArray());
                    }
                    break;
                }
                case MessageType.Throw:
                {
                    Messages.ReadThrow(reader, out var target, out float x, out float y, out float z, out float nx, out float ny, out float nz);
                    if (!_world.Throw(id, target, x, y, z, nx, ny, nz, now, out var charge)) break;
                    // Everyone sees it stuck there and fizzing, the thrower too (their throw only flew it there).
                    Messages.ChargeArmed(writer, charge, Explosives.FuseSeconds);
                    Broadcast(writer.ToArray());
                    _log.LogInformation("Player {Id} '{Name}' threw a charge at {Target}", id, session.Record.Name, target);
                    break;
                }
                case MessageType.Chat:
                {
                    string text = Protocol.CleanChat(Messages.ReadChat(reader));
                    // One line every second and a bit: enough to talk, too slow to flood.
                    if (text == null || now - session.LastChat < ChatInterval) break;
                    session.LastChat = now;
                    _log.LogInformation("Chat {Id} '{Name}': {Text}", id, session.Record.Name, text);
                    Messages.ChatLine(writer, id, session.Record.Name, text);
                    Broadcast(writer.ToArray());
                    break;
                }
                case MessageType.Act:
                {
                    var kind = Messages.ReadAct(reader, out int a, out int b, out string recipe);
                    var result = _world.Act(id, kind, a, b, recipe);
                    if (result != ActionResult.Ok) Fail(session, writer, result);
                    else if (kind == ActKind.Respawn) SendPose(session, writer);
                    break;
                }
                case MessageType.Build:
                {
                    var op = Messages.ReadBuild(reader);
                    var result = _world.Build(id, op, out var done);
                    if (result != ActionResult.Ok)
                    {
                        Fail(session, writer, result);
                        break;
                    }
                    Messages.Built(writer, new[] { done });
                    Broadcast(writer.ToArray());
                    break;
                }
                default:
                    throw new ProtocolException("A client may not send " + (byte)type + ".");
            }
        }

        /// <summary>Sends a player what changed about their own survivor.</summary>
        private void Flush(Session session, float now)
        {
            var record = session.Record;
            var w = _flushWriter;
            // The slots first, so a finished job's item is already in the inventory when it is announced.
            if (session.Sync.Slots(record.Survivor, w)) session.Connection.Send(w.ToArray());
            foreach (var (recipe, lost) in record.Crafted)
            {
                Messages.Crafted(w, recipe, lost);
                session.Connection.Send(w.ToArray());
            }
            record.Crafted.Clear();
            if (session.Sync.Crafts(record.Survivor, w)) session.Connection.Send(w.ToArray());
            if (session.Sync.Vitals(record.Survivor, w, now)) session.Connection.Send(w.ToArray());
            // The box or furnace they have open.
            if (session.Loot.Write(_world.Settlement, record.Survivor, w)) session.Connection.Send(w.ToArray());
        }

        private void SendPose(Session session, NetWriter writer)
        {
            _state.TryGet(session.Id, out var player);
            Messages.Correction(writer, player.Pose);
            session.Connection.Send(writer.ToArray());
        }

        private static void Fail(Session session, NetWriter writer, ActionResult why)
        {
            Messages.Failed(writer, why);
            session.Connection.Send(writer.ToArray());
        }

        private void Broadcast(byte[] message)
        {
            foreach (var s in _sessions.Values) s.Connection.Send(message);
        }

        private bool TryJoin(Connection connection, NetWriter writer, ushort version, string name, string token, out ushort id)
        {
            lock (_lock)
            {
                float now = Now;
                var record = _world.Join(version, name, token, now, out var player, out var replaced, out var reason);
                if (replaced != 0 && _sessions.TryGetValue(replaced, out var old))
                {
                    // The same survivor came back on a new connection: the old one is let go.
                    _sessions.Remove(replaced);
                    old.Connection.Kick();
                    Messages.Left(writer, replaced);
                    Broadcast(writer.ToArray());
                    _log.LogInformation("Player {Id} replaced by a new connection", replaced);
                }
                if (record == null)
                {
                    Messages.Rejected(writer, reason);
                    connection.Send(writer.ToArray());
                    id = 0;
                    return false;
                }
                id = player.Id;
                Messages.Welcome(writer, player.Id, _state.Seed, now, player.Pose, record.Owner);
                connection.Send(writer.ToArray());
                Messages.TimeOfDay(writer, _world.Day.Hours);
                connection.Send(writer.ToArray());
                // What has been cut or mined and not grown back yet.
                var damaged = _world.NotFull();
                if (damaged.Count > 0)
                {
                    Messages.NodeStates(writer, damaged);
                    connection.Send(writer.ToArray());
                }
                // Everything that stands on the island, in a few large messages.
                var ops = BuildOps.Describe(_world.Settlement);
                for (int start = 0; start < ops.Count; start += OpsPerMessage)
                {
                    Messages.Built(writer, ops, start, Math.Min(OpsPerMessage, ops.Count - start));
                    connection.Send(writer.ToArray());
                }
                // The newcomer learns who is here; everyone else learns about the newcomer.
                foreach (var other in _state.Players)
                {
                    if (other.Id == player.Id) continue;
                    Messages.Joined(writer, other.Id, other.Name, other.Pose);
                    connection.Send(writer.ToArray());
                }
                Messages.Joined(writer, player.Id, player.Name, player.Pose);
                Broadcast(writer.ToArray());
                // What everyone already here is wearing.
                foreach (var other in _sessions.Values)
                {
                    Messages.Outfit(writer, other.Id, Messages.Worn(other.Record.Survivor.Inventory, _worn));
                    connection.Send(writer.ToArray());
                }
                // Who sleeps where (this player no longer among them).
                Messages.Sleepers(writer, _world.Sleepers);
                connection.Send(writer.ToArray());
                // Charges fizzing right now, with what is left of their fuses.
                foreach (var charge in _world.LiveCharges)
                {
                    Messages.ChargeArmed(writer, charge, Math.Max(0f, charge.GoesOffAt - _world.Time));
                    connection.Send(writer.ToArray());
                }
                // Killed in their sleep while away: they learn by whom before they see themselves dead.
                string killedBy = _world.TakeKilledAsleep(record.Owner);
                if (killedBy != null)
                {
                    Messages.SleeperHit(writer, record.Owner, 0, killedBy, 0f, true);
                    connection.Send(writer.ToArray());
                }

                var session = new Session(player.Id, record, connection);
                _sessions[player.Id] = session;
                Flush(session, now);
                return true;
            }
        }

        private void Leave(ushort id, Connection connection, NetWriter writer)
        {
            lock (_lock)
            {
                // A session already replaced by a newer connection has nothing left to undo.
                if (!_sessions.TryGetValue(id, out var session) || session.Connection != connection) return;
                _sessions.Remove(id);
                _world.Leave(id);
                Messages.Left(writer, id);
                Broadcast(writer.ToArray());
            }
            _log.LogInformation("Player {Id} left ({Count} online)", id, PlayerCount);
        }

        /// <summary>One whole binary message into the buffer; 0 when the client closed.</summary>
        private static async Task<int> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
        {
            int count = 0;
            while (true)
            {
                if (count >= buffer.Length) throw new ProtocolException("Message too large.");
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), token);
                if (result.MessageType == WebSocketMessageType.Close) return 0;
                if (result.MessageType != WebSocketMessageType.Binary) throw new ProtocolException("Text frames are not used.");
                count += result.Count;
                if (result.EndOfMessage) return count;
            }
        }

        /// <summary>One player online: their survivor, what they have been told, and their connection.</summary>
        private sealed class Session
        {
            public readonly ushort Id;
            public readonly SurvivorRecord Record;
            public readonly Connection Connection;
            public readonly SurvivorSync Sync = new SurvivorSync();
            public readonly LootSync Loot = new LootSync();
            public float LastCorrection = -1f;
            public float LastChat = -10f;
            /// <summary>What everyone was last told this player wears (null: nothing told yet).</summary>
            public string LastOutfit;

            public Session(ushort id, SurvivorRecord record, Connection connection)
            {
                Id = id;
                Record = record;
                Connection = connection;
            }
        }

        /// <summary>
        /// One client's outgoing messages, sent one at a time in order. Nothing may be dropped (a lost
        /// change would leave the phone's copy wrong), so a client that falls far behind is let go;
        /// it comes back with a fresh copy of everything.
        /// </summary>
        private sealed class Connection
        {
            private readonly WebSocket _socket;
            private readonly CancellationTokenSource _life;
            private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

            public Connection(WebSocket socket, CancellationTokenSource life)
            {
                _socket = socket;
                _life = life;
            }

            public void Send(byte[] message)
            {
                if (!_outgoing.Writer.TryWrite(message)) Kick();
            }

            public void Complete() => _outgoing.Writer.TryComplete();

            /// <summary>Ends this connection (replaced, or too far behind).</summary>
            public void Kick()
            {
                _outgoing.Writer.TryComplete();
                try { _life.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            public async Task RunSends(CancellationToken stop)
            {
                try
                {
                    await foreach (var message in _outgoing.Reader.ReadAllAsync(stop))
                    {
                        if (_socket.State != WebSocketState.Open) return;
                        await _socket.SendAsync(message, WebSocketMessageType.Binary, true, stop);
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }
        }
    }
}
