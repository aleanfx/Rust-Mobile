using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    /// <summary>Teams: asked in face to face, a minute to say yes, at most four, a leader who can put people out.</summary>
    public class TeamTests
    {
        private const int Seed = 20261113;
        private static readonly string[] Tokens =
        {
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "cccccccccccccccccccccccccccccccc",
            "dddddddddddddddddddddddddddddddd", "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
        };
        private static readonly string[] Names = { "Ale", "Bea", "Caro", "Dani", "Eli" };

        [Test]
        public void A_leader_asks_people_in_and_they_say_yes()
        {
            var teams = new Teams();
            var told = new List<int>();
            teams.Changed += owners => told.AddRange(owners);
            Assert.AreEqual(ActionResult.Ok, teams.Invite(1, 2, 0f));
            Assert.IsTrue(teams.TryGetInvite(2, 10f, out int from) && from == 1);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, told);
            Assert.IsFalse(teams.SameTeam(1, 2), "not until they say yes");
            Assert.AreEqual(ActionResult.Ok, teams.Accept(2, 10f));
            Assert.IsTrue(teams.SameTeam(1, 2));
            Assert.IsTrue(teams.TryGetTeam(2, out var team));
            Assert.AreEqual(1, team.Leader);
            CollectionAssert.AreEqual(new[] { 1, 2 }, team.MemberList);
            Assert.IsFalse(teams.TryGetInvite(2, 10f, out _));

            Assert.AreEqual(ActionResult.NotLeader, teams.Invite(2, 3, 11f), "only the leader asks people in");
            Assert.AreEqual(ActionResult.HasTeam, teams.Invite(1, 2, 11f));
            Assert.AreEqual(ActionResult.Invalid, teams.Invite(1, 1, 11f));
            Assert.IsTrue(teams.SameTeam(5, 5), "everyone is on their own side");
        }

        [Test]
        public void An_invitation_runs_out_and_can_be_turned_down()
        {
            var teams = new Teams();
            teams.Invite(1, 2, 0f);
            Assert.AreEqual(ActionResult.NoInvite, teams.Accept(2, Teams.InviteSeconds + 1f), "too late");
            Assert.IsFalse(teams.SameTeam(1, 2));

            teams.Invite(1, 3, 100f);
            Assert.AreEqual(ActionResult.Ok, teams.Decline(3));
            Assert.AreEqual(ActionResult.NoInvite, teams.Accept(3, 101f));
            Assert.IsFalse(teams.TryGetTeam(1, out _), "a team made for an invitation nobody took is gone");
            Assert.AreEqual(ActionResult.NoInvite, teams.Decline(3));
        }

        [Test]
        public void Four_at_most()
        {
            var teams = new Teams();
            for (int o = 2; o <= Teams.MaxMembers; o++)
            {
                Assert.AreEqual(ActionResult.Ok, teams.Invite(1, o, 0f));
                Assert.AreEqual(ActionResult.Ok, teams.Accept(o, 1f));
            }
            Assert.AreEqual(ActionResult.TeamFull, teams.Invite(1, 9, 2f));
            Assert.AreEqual(Teams.MaxMembers, teams.All.Single().MemberList.Count);
        }

        [Test]
        public void Leaving_kicking_and_handing_over_the_lead()
        {
            var teams = new Teams();
            foreach (int o in new[] { 2, 3 })
            {
                teams.Invite(1, o, 0f);
                teams.Accept(o, 0f);
            }
            Assert.AreEqual(ActionResult.NotLeader, teams.Kick(2, 3));
            Assert.AreEqual(ActionResult.Ok, teams.Promote(1, 3));
            Assert.IsTrue(teams.TryGetTeam(1, out var team) && team.Leader == 3);
            Assert.AreEqual(3, team.MemberList[0], "the leader first");
            Assert.AreEqual(ActionResult.Ok, teams.Kick(3, 1));
            Assert.IsFalse(teams.SameTeam(1, 3));
            Assert.AreEqual(ActionResult.NoTeam, teams.Leave(1));

            // The leader leaves: the lead passes on; the last one alone: no team.
            teams.Invite(3, 4, 0f);
            teams.Accept(4, 0f);
            Assert.AreEqual(ActionResult.Ok, teams.Leave(3));
            Assert.IsTrue(teams.TryGetTeam(2, out team) && team.Leader == 2);
            Assert.AreEqual(ActionResult.Ok, teams.Leave(4));
            Assert.IsFalse(teams.TryGetTeam(2, out _));
            Assert.AreEqual(0, teams.Count);
        }

        [Test]
        public void On_the_server_asking_in_is_face_to_face_and_everyone_is_told()
        {
            var world = new ServerWorld(Seed);
            var players = new List<ServerState.Player>();
            var records = new List<SurvivorRecord>();
            for (int i = 0; i < 3; i++)
            {
                records.Add(world.Join(Protocol.Version, Names[i], Tokens[i], 0f, out var p, out _, out _));
                players.Add(p);
            }
            var at = players[0].Pose;
            world.Players.Place(players[1].Id, new Pose(at.X + 2f, at.Y, at.Z, 0f), 0f);
            world.Players.Place(players[2].Id, new Pose(at.X + 40f, at.Y, at.Z, 0f), 0f);

            Assert.AreEqual(ActionResult.TooFar, world.TeamAct(players[0].Id, TeamOp.Invite, players[2].Id));
            Assert.AreEqual(ActionResult.Invalid, world.TeamAct(players[0].Id, TeamOp.Invite, 999));
            Assert.AreEqual(ActionResult.Ok, world.TeamAct(players[0].Id, TeamOp.Invite, players[1].Id));

            // Bea is told who asked her.
            var w = new NetWriter();
            world.WriteTeam(w, records[1].Owner);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.TeamState, (MessageType)r.Byte());
            var mates = new List<TeamMate>();
            Messages.ReadTeamState(r, out int id, out _, mates, out string from);
            Assert.AreEqual((0, "Ale"), (id, from));
            Assert.IsEmpty(mates);

            Assert.AreEqual(ActionResult.Ok, world.TeamAct(players[1].Id, TeamOp.Accept, 0));
            Assert.IsTrue(world.Teammates(players[0].Id, players[1].Id));
            Assert.IsFalse(world.Teammates(players[0].Id, players[2].Id));

            // Both are told the team, with where each is; one away is still in it, asleep.
            world.Leave(players[1].Id);
            world.WriteTeam(w, records[0].Owner);
            r = new NetReader(w.Buffer, 0, w.Length);
            r.Byte();
            Messages.ReadTeamState(r, out id, out int leader, mates, out from);
            Assert.AreNotEqual(0, id);
            Assert.AreEqual(records[0].Owner, leader);
            Assert.AreEqual("", from);
            Assert.AreEqual(2, mates.Count);
            Assert.AreEqual(("Ale", true, players[0].Id), (mates[0].Name, mates[0].Online, mates[0].PlayerId));
            Assert.AreEqual(("Bea", false, (ushort)0), (mates[1].Name, mates[1].Online, mates[1].PlayerId));
            Assert.AreEqual(at.X + 2f, mates[1].X, 1e-3f, "where she lies");

            // Kept with the island.
            var loaded = new ServerWorld(Seed);
            Assert.IsTrue(loaded.Load(world.Save(), out var why), why);
            Assert.IsTrue(loaded.Teams.SameTeam(records[0].Owner, records[1].Owner));
            Assert.IsFalse(loaded.Teams.SameTeam(records[0].Owner, records[2].Owner));
        }

        [Test]
        public void Team_chat_and_team_messages_round_trip()
        {
            var w = new NetWriter();
            Messages.Chat(w, "vamos a la ruina", true);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Chat, (MessageType)r.Byte());
            Assert.AreEqual("vamos a la ruina", Messages.ReadChat(r, out bool team));
            Assert.IsTrue(team);

            Messages.ChatLine(w, 4, "Bea", "voy", true);
            r = new NetReader(w.Buffer, 0, w.Length);
            r.Byte();
            Messages.ReadChatLine(r, out ushort from, out string name, out string text, out team);
            Assert.AreEqual((4, "Bea", "voy", true), (from, name, text, team));

            Messages.TeamAct(w, TeamOp.Kick, 77);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.TeamAct, (MessageType)r.Byte());
            Messages.ReadTeamAct(r, out var op, out int arg);
            Assert.AreEqual((TeamOp.Kick, 77), (op, arg));
        }
    }
}
