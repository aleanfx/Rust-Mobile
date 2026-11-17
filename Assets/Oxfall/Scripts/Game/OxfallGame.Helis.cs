using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Gathering;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;
using Oxfall.Game.Audio;
using Oxfall.Game.Net;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game
{
    /// <summary>The patrol helicopter in the game: run here alone (online the server runs it), drawn and heard either way.</summary>
    public sealed partial class OxfallGame
    {
        private PatrolHelis _helis;
        private HeliViews _heliViews;
        private readonly List<Fauna.Prey> _heliPrey = new List<Fauna.Prey>();
        private readonly List<HeliShot> _heliShots = new List<HeliShot>();
        private readonly List<PatrolHeli> _heliCrashed = new List<PatrolHeli>();
        private readonly List<HeliSnap> _heliSnaps = new List<HeliSnap>();
        private float _heliToastAt = -10f, _baseToastAt = -20f;
        private float _onlineRealSince = -100f;

        /// <summary>The helicopter's rules alone, and its views (with what to say when one comes and goes down).</summary>
        private void CreateHelis(MaterialLibrary materials, GameAudio audio)
        {
            _helis = new PatrolHelis(_world.Shape.Ground, s_seed, Time.time)
            {
                Shielded = (x0, y0, z0, x1, y1, z1) => Explosives.Shielded(_settlement, x0, y0, z0, x1, y1, z1),
                RocketBurst = (heli, x, y, z) =>
                {
                    int struck = Explosives.RocketBlast(_settlement, x, y, z, null);
                    if (struck > 0) Debug.Log($"[Oxfall] A helicopter rocket struck {struck} built piece(s) at ({x:0.0}, {y:0.0}, {z:0.0})");
                },
            };
            _actions.Helis = _helis;
            _actions.HeliHit += (id, damage, down) =>
            {
                _hud.ShowHitMarker(down);
                Debug.Log($"[Oxfall] Hit the helicopter {id}: -{damage:0}{(down ? ", it is going down" : "")}");
            };
            _heliViews = new HeliViews(materials, audio.Bank, _explosions)
            {
                Locate = key =>
                {
                    int me = _actions.Online ? _net.PlayerId : GameActions.OxfallSoloKey;
                    if (key == me && _player != null) return _player.transform.position + Vector3.up;
                    if (_actions.Online && _net.Players.TryGetValue((ushort)key, out var them)) return them.Root.position + Vector3.up;
                    return null;
                },
            };
            _heliViews.Arrived += s =>
            {
                if (_title != null) return;
                _hud.ShowToast("¡Helicóptero de patrulla sobre la isla! Ponte a cubierto o derríbalo");
                audio.Play(SoundCue.Accepted);
                Debug.Log($"[Oxfall] Patrol helicopter {s.Id} came in at ({s.X:0}, {s.Y:0}, {s.Z:0})");
            };
            _heliViews.RocketLanded += at =>
            {
                // Its rockets bursting on a base (whoever's): the walls take it; say so now and then.
                if (_title != null || !_settlement.Grid.FoundationNear(at.x, at.z, 4.5f) || Time.time - _baseToastAt < 12f) return;
                _baseToastAt = Time.time;
                _hud.ShowToast("¡Los cohetes del helicóptero revientan la base! La madera no aguanta: piedra o metal");
                Debug.Log("[Oxfall] Toast: los cohetes revientan la base");
            };
            _heliViews.Downed += s =>
            {
                if (_title == null) _hud.ShowToast("¡Helicóptero derribado! Sus cajas arden entre los restos");
                Debug.Log($"[Oxfall] Patrol helicopter {s.Id} going down at ({s.X:0}, {s.Z:0})");
            };
        }

        /// <summary>Online: what the server says of the helicopters.</summary>
        private void ListenToHelis()
        {
            _net.HelisSeen += snaps =>
            {
                // Those already about when this player joined are not news (a real clock: a slow phone's game clock lags).
                _heliViews?.Apply(snaps, false, Time.realtimeSinceStartup - _onlineRealSince < 4f);
            };
            _net.StatusChanged += state => { if (state == NetClient.Status.Online) _onlineRealSince = Time.realtimeSinceStartup; };
            _net.HeliFired += (shot, damage, killed) => OnHeliFired(shot, damage, killed, _net.PlayerId);
        }

        /// <summary>Alone, the helicopter flies, hunts and fires here; either way it is drawn.</summary>
        private void TickHelis(float dt)
        {
            if (_heliViews == null) return;
            if (!_actions.Online && _helis != null)
            {
                _heliPrey.Clear();
                if (_title == null && !_vitals.IsDead && _player != null)
                {
                    var p = _player.transform.position;
                    _heliPrey.Add(new Fauna.Prey(GameActions.OxfallSoloKey, p.x, p.y, p.z));
                }
                _heliShots.Clear();
                _heliCrashed.Clear();
                if (_title == null) _helis.Tick(Time.time, dt, _heliPrey, _heliShots, null, _heliCrashed);
                foreach (var shot in _heliShots)
                {
                    int damage = 0;
                    bool killed = false;
                    if (shot.Target == GameActions.OxfallSoloKey && shot.Damage > 0f && !_vitals.IsDead)
                    {
                        // Named before the round lands, in case it is the last.
                        _killer = "el helicóptero de patrulla";
                        damage = Mathf.RoundToInt(_survivor.TakeHit(shot.Damage));
                        killed = _vitals.IsDead;
                        if (!killed) _killer = null;
                    }
                    OnHeliFired(shot, damage, killed, GameActions.OxfallSoloKey);
                }
                foreach (var wreck in _heliCrashed)
                    for (int i = 0; i < PatrolHelis.Crates; i++)
                    {
                        _helis.CrateSpot(wreck, i, out float x, out float y, out float z, out float yaw);
                        _settlement.PlaceHeliCrate(x, y, z, yaw, LootTable.HeliCrate(new System.Random()));
                    }
                _heliSnaps.Clear();
                foreach (var h in _helis.All) _heliSnaps.Add(HeliSnap.Of(h));
                _heliViews.Apply(_heliSnaps, true);
            }
            _heliViews.Night = _dayNight != null && _dayNight.Cycle.Daylight < 0.35f;
            _heliViews.Tick(dt, _camera != null ? _camera.transform.position : Vector3.zero);
        }

        /// <summary>A helicopter fired: drawn and heard; if it hit this player, it hurts.</summary>
        private void OnHeliFired(HeliShot shot, int damage, bool killed, int me)
        {
            if (shot.Kind != HeliShotKind.Splash) _heliViews?.Shot(shot);
            if (shot.Target != me || damage <= 0) return;
            if (_actions.Online && killed) _killer = "el helicóptero de patrulla";
            _hud.ShowHurt(damage);
            _audio.Play(SoundCue.Hurt);
            _explosions?.Punch(shot.Kind == HeliShotKind.Splash ? 4f : 1.2f);
            if (shot.Kind == HeliShotKind.Splash) _explosions?.Shake(0.8f);
            if (!killed && Time.time - _heliToastAt > 4f)
            {
                _heliToastAt = Time.time;
                _hud.ShowToast(shot.Kind == HeliShotKind.Splash ? $"¡Un cohete del helicóptero!  −{damage}" : "¡El helicóptero te dispara! Busca techo");
            }
        }

        /// <summary>The helicopters, their wrecks and their crates on the map.</summary>
        private void HeliPins(List<MapUi.Pin> pins)
        {
            if (_heliViews == null) return;
            foreach (var s in _heliViews.All)
                pins.Add(s.State == HeliState.Wreck
                    ? new MapUi.Pin(new Vector3(s.X, s.Y, s.Z), Icons.Flame, new Color(1f, 0.55f, 0.2f), "Restos del helicóptero")
                    : new MapUi.Pin(new Vector3(s.X, s.Y, s.Z), Icons.Copter, UiTheme.Bad, "Helicóptero de patrulla"));
            foreach (var d in _settlement.Deployables)
                if (d.Kind == DeployableKind.HeliCrate && !_settlement.IsCrateOpen(d.Id))
                {
                    pins.Add(new MapUi.Pin(new Vector3(d.X, d.Y, d.Z), Icons.Box, new Color(1f, 0.55f, 0.2f), LockedCrates.Clock(_settlement.HackLeft(d.Id))));
                    break;
                }
        }

        /// <summary>Over each of a wreck's crates: how long it still burns, or that it can be opened.</summary>
        private void HeliCrateTags()
        {
            if (_camera == null || _deployables == null) return;
            foreach (var d in _settlement.Deployables)
            {
                if (d.Kind != DeployableKind.HeliCrate || !_deployables.TryGetView(d.Id, out var view)) continue;
                var at = view.transform.position + Vector3.up * 1.25f;
                if (_settlement.IsCrateOpen(d.Id)) _nameTags.Show(_camera, at, "CAJA DEL HELICÓPTERO", 25f, UiTheme.Good);
                else _nameTags.Show(_camera, at, "ARDIENDO  " + LockedCrates.Clock(_settlement.HackLeft(d.Id)), 120f, new Color(1f, 0.55f, 0.2f));
            }
        }

        /// <summary>Test commands for the helicopter; false if the command is not one of them.</summary>
        private bool HeliCommand(string command)
        {
            switch (command)
            {
                case "heli": DevHeli(0); return true;
                case "heli-here": DevHeli(1); return true;
                case "heli-hurt": DevHeli(2); return true;
                case "heli-down": DevHeli(3); return true;
                case "heli-clear": DevHeli(4); return true;
                case "heli-state":
                    foreach (var s in _heliViews.All)
                        Debug.Log($"[Oxfall] Heli {s.Id} {s.State} health={s.Health:0} target={s.Target} at ({s.X:0.0}, {s.Y:0.0}, {s.Z:0.0}) yaw={s.Yaw:0}");
                    foreach (var d in _settlement.Deployables)
                        if (d.Kind == DeployableKind.HeliCrate)
                            Debug.Log($"[Oxfall] Heli crate {d.Id} open={_settlement.IsCrateOpen(d.Id)} left={_settlement.HackLeft(d.Id):0.0} at ({d.X:0.0}, {d.Z:0.0})");
                    Debug.Log($"[Oxfall] Heli count={_heliViews.Count} health={_vitals.Health:0}");
                    return true;
                case "view-heli": FaceHeli(); return true;
                case "go-wreck": ToWreck(); return true;
                case "open-heli-crate":
                    if (_settlement.TryGet(Nearest(DeployableKind.HeliCrate), out var c) && _settlement.IsCrateOpen(c.Id))
                    {
                        GoTo(c.Id);
                        OpenBox(c.Id);
                    }
                    else Debug.Log("[Oxfall] Heli crate still burning (or none)");
                    return true;
                case "heli-cool":
                    // Test, alone: the wreck's crates cool down at once.
                    foreach (var d in _settlement.Deployables)
                        if (d.Kind == DeployableKind.HeliCrate && !_settlement.IsCrateOpen(d.Id)) _settlement.SetHack(d.Id, 1f);
                    return true;
                case "shoot-heli": ShootHeli(); return true;
                case "rocket-hut": RocketHut(); return true;
                case "heli-aggro":
                    // Test, alone: as if the player had shot it from where they stand (hidden or not).
                    if (!_actions.Online && _player != null)
                    {
                        var at = _player.transform.position;
                        foreach (var h in _helis.All)
                            if (!h.Down) _helis.Hit(h.Id, 1f, false, GameActions.OxfallSoloKey, at.x, at.y, at.z, Time.time);
                    }
                    return true;
                case "hut-state":
                {
                    var grid = _settlement.Grid;
                    float health = 0f, max = 0f;
                    foreach (var piece in grid.Pieces)
                    {
                        health += grid.Health(piece);
                        max += grid.MaxHealth(piece);
                    }
                    Debug.Log($"[Oxfall] Hut pieces={grid.PieceCount} health={health:0} max={max:0}");
                    return true;
                }
                case "view-hut":
                {
                    // Test view: 9 m south of the hut, looking at it.
                    var at = _rocketHut + new Vector3(0.5f, 0f, -9f);
                    at.y = _world.Shape.Height(at.x, at.z) + 0.05f;
                    var to = _rocketHut + Vector3.up * 2.2f - (at + Vector3.up * 1.6f);
                    _player.Teleport(at, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                    return true;
                }
                case "into-hut":
                    _player.Teleport(_rocketHut + Vector3.up * 0.05f, 0f, 0f);
                    return true;
                default: return false;
            }
        }

        /// <summary>Test: the helicopter (0 comes in, 1 comes for the player, 2 badly hurt, 3 shot down, 4 gone).</summary>
        private void DevHeli(int how)
        {
            if (_actions.Online)
            {
                _net.SendAct(ActKind.DevHeli, how);
                return;
            }
            var p = _player.transform.position;
            switch (how)
            {
                case 0: _helis.Launch(Time.time); break;
                case 1: _helis.Launch(Time.time, p.x, p.z, 150f); break;
                case 2:
                case 3:
                    foreach (var h in _helis.All)
                        if (!h.Down) _helis.SetHealth(h.Id, how == 2 ? PatrolHelis.MaxHealth * 0.25f : 0f, Time.time);
                    break;
                case 4:
                    _helis.Clear();
                    break;
            }
        }

        /// <summary>Test view: the player turns to look at the nearest helicopter.</summary>
        private void FaceHeli()
        {
            float best = float.MaxValue;
            Vector3 at = default;
            foreach (var s in _heliViews.All)
            {
                if (!_heliViews.TryGetPosition(s.Id, out var p)) continue;
                float d = (p - _player.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    at = p;
                }
            }
            if (best == float.MaxValue) return;
            var to = at - _player.Head.position;
            _player.Teleport(_player.transform.position, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
        }

        /// <summary>Test: the player fires the gun in hand at the nearest helicopter's hull (aimed for them).</summary>
        private void ShootHeli()
        {
            FaceHeli();
            // The colliders where the helicopter is drawn this frame (on a slow machine they lag a frame behind).
            Physics.SyncTransforms();
            _gatherer.UseOnce();
        }

        private Vector3 _rocketHut;

        /// <summary>
        /// Test, alone: a 2×1 wooden hut (wooden walls all round, a wooden roof) on the nearest flat
        /// open ground, free, with the player inside it.
        /// </summary>
        private void RocketHut()
        {
            var grid = _settlement.Grid;
            var ground = _world.Shape.Ground;
            var p = _player.transform.position;
            float best = float.MaxValue;
            Cell chosen = default;
            float chosenTop = 0f;
            const float S = BuildingGrid.CellSize;
            var near = BuildingGrid.CellAt(p.x, p.z);
            for (int dz = -25; dz <= 25; dz++)
            for (int dx = -25; dx <= 25; dx++)
            {
                var c = new Cell(near.X + dx, near.Z + dz);
                float lo = float.MaxValue, hi = float.MinValue;
                bool ok = true;
                for (float x = c.X * S - 1f; x <= (c.X + 2) * S + 1f && ok; x += 1.5f)
                for (float z = c.Z * S - 1f; z <= (c.Z + 1) * S + 1f && ok; z += 1.5f)
                {
                    float g = ground.Height(x, z);
                    lo = Mathf.Min(lo, g);
                    hi = Mathf.Max(hi, g);
                    if (g < IslandGround.WaterLevel + 1f || Monuments.NoBuilding(ground, x, z)
                        || (_settlement.Obstacles != null && _settlement.Obstacles.Blocks(x, z, 0.5f))) ok = false;
                }
                float score = (hi - lo) * 30f + Mathf.Sqrt(dx * dx + dz * dz);
                if (!ok || hi - lo > 1.5f || score >= best) continue;
                best = score;
                chosen = c;
                chosenTop = hi + 0.2f;
            }
            if (best == float.MaxValue)
            {
                Debug.Log("[Oxfall] Rocket hut: no flat ground near");
                return;
            }
            var cells = new[] { chosen, new Cell(chosen.X + 1, chosen.Z) };
            foreach (var c in cells) grid.PlaceFoundation(c, chosenTop);
            foreach (var c in cells)
                foreach (var e in c.Edges)
                    if (!grid.HasEdgePiece(e) && !e.Equals(cells[0].East)) grid.PlaceEdge(e, PieceType.Wall);
            foreach (var c in cells) grid.PlaceRoof(c);
            foreach (var piece in new List<PieceRef>(grid.Pieces))
                if (grid.TryGetTier(piece, out var tier) && tier == BuildingTier.Twig) grid.Upgrade(piece, BuildingTier.Wood);
            _rocketHut = new Vector3((chosen.X + 1) * S, chosenTop, (chosen.Z + 0.5f) * S);
            _player.Teleport(_rocketHut + Vector3.up * 0.05f, 0f, 0f);
            Debug.Log($"[Oxfall] Rocket hut at {chosen}, pieces={grid.PieceCount}");
        }

        /// <summary>Test view: some metres from the wreck, looking at it and its crates.</summary>
        private void ToWreck()
        {
            foreach (var s in _heliViews.All)
            {
                if (s.State != HeliState.Wreck) continue;
                var wreck = new Vector3(s.X, s.Y, s.Z);
                var at = wreck + new Vector3(9f, 0f, -9f);
                at.y = Monuments.SurfaceHeight(_world.Shape.Ground, at.x, at.z) + 0.05f;
                var to = wreck + Vector3.up * 1.2f - (at + Vector3.up * 1.6f);
                _player.Teleport(at, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                return;
            }
        }
    }
}
