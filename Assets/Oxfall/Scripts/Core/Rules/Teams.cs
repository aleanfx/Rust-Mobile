using System;
using System.Collections.Generic;

namespace Oxfall.Core.Rules
{
    /// <summary>A few survivors who play together: who leads it, and who is in it (by owner number).</summary>
    public sealed class Team
    {
        internal readonly List<int> Members = new List<int>();

        public Team(int id, int leader)
        {
            Id = id;
            Leader = leader;
            Members.Add(leader);
        }

        public int Id { get; }

        /// <summary>The one who invites and can put someone out.</summary>
        public int Leader { get; internal set; }

        /// <summary>Everyone in it, the leader first, then in the order they joined.</summary>
        public IReadOnlyList<int> MemberList => Members;
    }

    /// <summary>What someone asked of their team.</summary>
    public enum TeamOp : byte
    {
        /// <summary>Asks another player in (their player id): the asker's team, made for them if they have none.</summary>
        Invite = 1,
        Accept = 2,
        Decline = 3,
        Leave = 4,
        /// <summary>The leader puts a member out (their owner number).</summary>
        Kick = 5,
        /// <summary>The leader hands the lead to a member (their owner number).</summary>
        Promote = 6,
    }

    /// <summary>
    /// The island's teams, as on the PC game: a leader asks players in, one at a time; whoever is asked
    /// has a minute to say yes; anyone can leave, and the leader can put someone out or hand over the
    /// lead. A team of one is no team. Teammates see each other on the map and talk on their own.
    /// </summary>
    public sealed class Teams
    {
        /// <summary>The most a team holds.</summary>
        public const int MaxMembers = 4;

        /// <summary>How long an invitation stands.</summary>
        public const float InviteSeconds = 60f;

        private readonly Dictionary<int, Team> _teams = new Dictionary<int, Team>();
        private readonly Dictionary<int, int> _teamOf = new Dictionary<int, int>();
        private readonly Dictionary<int, (int Team, int From, float Until)> _invites = new Dictionary<int, (int, int, float)>();
        private int _nextId = 1;

        /// <summary>Someone's team or invitation changed: everyone with these owner numbers is to be told.</summary>
        public event Action<IReadOnlyList<int>> Changed;

        public IEnumerable<Team> All => _teams.Values;

        public int Count => _teams.Count;

        public bool TryGetTeam(int owner, out Team team)
        {
            team = null;
            return _teamOf.TryGetValue(owner, out int id) && _teams.TryGetValue(id, out team);
        }

        /// <summary>Whether two survivors are in one team (each is in their own with themselves).</summary>
        public bool SameTeam(int a, int b) => a == b || (_teamOf.TryGetValue(a, out int ta) && _teamOf.TryGetValue(b, out int tb) && ta == tb);

        /// <summary>Whoever asked this survivor in, if the invitation still stands.</summary>
        public bool TryGetInvite(int owner, float now, out int from)
        {
            from = 0;
            if (!_invites.TryGetValue(owner, out var invite) || invite.Until < now || !_teams.ContainsKey(invite.Team)) return false;
            from = invite.From;
            return true;
        }

        /// <summary><paramref name="from"/> asks <paramref name="to"/> into their team (one made for them if they have none).</summary>
        public ActionResult Invite(int from, int to, float now)
        {
            if (from == 0 || to == 0 || from == to) return ActionResult.Invalid;
            if (_teamOf.ContainsKey(to)) return ActionResult.HasTeam;
            if (TryGetTeam(from, out var team))
            {
                if (team.Leader != from) return ActionResult.NotLeader;
                if (team.Members.Count >= MaxMembers) return ActionResult.TeamFull;
            }
            else
            {
                team = new Team(_nextId++, from);
                _teams.Add(team.Id, team);
                _teamOf[from] = team.Id;
            }
            _invites[to] = (team.Id, from, now + InviteSeconds);
            Changed?.Invoke(new[] { from, to });
            return ActionResult.Ok;
        }

        public ActionResult Accept(int owner, float now)
        {
            if (!_invites.TryGetValue(owner, out var invite) || invite.Until < now || !_teams.TryGetValue(invite.Team, out var team))
            {
                _invites.Remove(owner);
                return ActionResult.NoInvite;
            }
            _invites.Remove(owner);
            if (_teamOf.ContainsKey(owner)) return ActionResult.HasTeam;
            if (team.Members.Count >= MaxMembers) return ActionResult.TeamFull;
            team.Members.Add(owner);
            _teamOf[owner] = team.Id;
            Changed?.Invoke(team.Members.ToArray());
            return ActionResult.Ok;
        }

        public ActionResult Decline(int owner)
        {
            if (!_invites.TryGetValue(owner, out var invite)) return ActionResult.NoInvite;
            _invites.Remove(owner);
            // A team made only for this invitation goes with it.
            if (_teams.TryGetValue(invite.Team, out var team) && team.Members.Count == 1 && !Pending(team.Id))
            {
                _teamOf.Remove(team.Leader);
                _teams.Remove(team.Id);
            }
            Changed?.Invoke(new[] { owner, invite.From });
            return ActionResult.Ok;
        }

        private bool Pending(int teamId)
        {
            foreach (var invite in _invites.Values)
                if (invite.Team == teamId) return true;
            return false;
        }

        public ActionResult Leave(int owner)
        {
            if (!TryGetTeam(owner, out var team)) return ActionResult.NoTeam;
            Remove(team, owner);
            return ActionResult.Ok;
        }

        public ActionResult Kick(int leader, int member)
        {
            if (!TryGetTeam(leader, out var team)) return ActionResult.NoTeam;
            if (team.Leader != leader) return ActionResult.NotLeader;
            if (member == leader || !team.Members.Contains(member)) return ActionResult.Invalid;
            Remove(team, member);
            return ActionResult.Ok;
        }

        public ActionResult Promote(int leader, int member)
        {
            if (!TryGetTeam(leader, out var team)) return ActionResult.NoTeam;
            if (team.Leader != leader) return ActionResult.NotLeader;
            if (member == leader || !team.Members.Contains(member)) return ActionResult.Invalid;
            team.Leader = member;
            // The leader is listed first.
            team.Members.Remove(member);
            team.Members.Insert(0, member);
            Changed?.Invoke(team.Members.ToArray());
            return ActionResult.Ok;
        }

        /// <summary>One out of a team; the lead passes to whoever joined first; one left alone: no team.</summary>
        private void Remove(Team team, int owner)
        {
            var told = team.Members.ToArray();
            team.Members.Remove(owner);
            _teamOf.Remove(owner);
            if (team.Members.Count <= 1)
            {
                foreach (int m in team.Members) _teamOf.Remove(m);
                team.Members.Clear();
                _teams.Remove(team.Id);
                // Invitations to it lapse.
                var lapsed = new List<int>();
                foreach (var pair in _invites)
                    if (pair.Value.Team == team.Id) lapsed.Add(pair.Key);
                foreach (int o in lapsed) _invites.Remove(o);
            }
            else if (team.Leader == owner) team.Leader = team.Members[0];
            Changed?.Invoke(told);
        }

        // ---- Saving

        /// <summary>The teams as they stand (invitations are not kept).</summary>
        public List<(int Id, int Leader, int[] Members)> Save()
        {
            var list = new List<(int, int, int[])>();
            foreach (var t in _teams.Values) list.Add((t.Id, t.Leader, t.Members.ToArray()));
            return list;
        }

        public void Load(IEnumerable<(int Id, int Leader, int[] Members)> teams)
        {
            _teams.Clear();
            _teamOf.Clear();
            _invites.Clear();
            _nextId = 1;
            foreach (var (id, leader, members) in teams)
            {
                if (id <= 0 || members == null || members.Length < 2) continue;
                var team = new Team(id, leader);
                team.Members.Clear();
                foreach (int m in members)
                    if (m != 0 && !_teamOf.ContainsKey(m) && team.Members.Count < MaxMembers)
                    {
                        team.Members.Add(m);
                        _teamOf[m] = id;
                    }
                if (team.Members.Count < 2 || !team.Members.Contains(leader))
                {
                    foreach (int m in team.Members) _teamOf.Remove(m);
                    continue;
                }
                team.Members.Remove(leader);
                team.Members.Insert(0, leader);
                _teams[id] = team;
                _nextId = Math.Max(_nextId, id + 1);
            }
        }
    }
}
