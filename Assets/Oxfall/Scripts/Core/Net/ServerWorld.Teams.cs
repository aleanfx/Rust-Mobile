using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>The island's teams as the server keeps them, and what each player is told of theirs.</summary>
    public sealed partial class ServerWorld
    {
        /// <summary>How near another player must be to be asked into a team (face to face, as on the PC game).</summary>
        public const float InviteReach = 6f;

        private readonly List<TeamMate> _mates = new List<TeamMate>();

        public Teams Teams { get; } = new Teams();

        /// <summary>
        /// Something a player asks of their team: to invite another player (by player id; near enough to
        /// talk to), to say yes or no to an invitation, to leave, or as leader to put someone out or hand
        /// over the lead (by owner number).
        /// </summary>
        public ActionResult TeamAct(ushort playerId, TeamOp op, int arg)
        {
            if (!_online.TryGetValue(playerId, out var record)) return ActionResult.Invalid;
            int owner = record.Owner;
            switch (op)
            {
                case TeamOp.Invite:
                {
                    if (arg <= 0 || arg > ushort.MaxValue || arg == playerId) return ActionResult.Invalid;
                    if (!_online.TryGetValue((ushort)arg, out var other) || other.Survivor.Vitals.IsDead) return ActionResult.Invalid;
                    if (!Players.TryGet(playerId, out var me) || !Players.TryGet((ushort)arg, out var them)) return ActionResult.Invalid;
                    float dx = me.Pose.X - them.Pose.X, dy = me.Pose.Y - them.Pose.Y, dz = me.Pose.Z - them.Pose.Z;
                    if (dx * dx + dy * dy + dz * dz > InviteReach * InviteReach) return ActionResult.TooFar;
                    return Teams.Invite(owner, other.Owner, Time);
                }
                case TeamOp.Accept: return Teams.Accept(owner, Time);
                case TeamOp.Decline: return Teams.Decline(owner);
                case TeamOp.Leave: return Teams.Leave(owner);
                case TeamOp.Kick: return Teams.Kick(owner, arg);
                case TeamOp.Promote: return Teams.Promote(owner, arg);
                default: return ActionResult.Invalid;
            }
        }

        /// <summary>Whether a survivor has anything to be told about a team: one they are in, or an invitation.</summary>
        public bool HasTeamNews(int owner) => Teams.TryGetTeam(owner, out _) || Teams.TryGetInvite(owner, Time, out _);

        /// <summary>Writes what <paramref name="owner"/> is told of their team: its members, where each is, and any invitation.</summary>
        public void WriteTeam(NetWriter w, int owner)
        {
            _mates.Clear();
            int id = 0, leader = 0;
            if (Teams.TryGetTeam(owner, out var team))
            {
                id = team.Id;
                leader = team.Leader;
                foreach (int m in team.MemberList)
                {
                    if (!_byOwner.TryGetValue(m, out var mate)) continue;
                    bool online = mate.PlayerId != 0 && Players.TryGet(mate.PlayerId, out _);
                    var pose = online && Players.TryGet(mate.PlayerId, out var player) ? player.Pose : mate.Pose;
                    _mates.Add(new TeamMate(m, online ? mate.PlayerId : (ushort)0, mate.Name, pose.X, pose.Z, online, mate.Survivor.Vitals.IsDead));
                }
            }
            string from = Teams.TryGetInvite(owner, Time, out int inviter) && _byOwner.TryGetValue(inviter, out var by) ? by.Name ?? "" : "";
            Messages.TeamState(w, id, leader, _mates, from);
        }

        /// <summary>Whether two players (by player id) are in one team.</summary>
        public bool Teammates(ushort a, ushort b) =>
            _online.TryGetValue(a, out var ra) && _online.TryGetValue(b, out var rb) && Teams.SameTeam(ra.Owner, rb.Owner);
    }
}
