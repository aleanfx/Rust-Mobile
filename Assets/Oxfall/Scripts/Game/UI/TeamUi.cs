using System;
using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The team, as on the PC game: a button beside the chat's opens a panel with who is in it (the
    /// leader crowned, who is online and how far), the leader's buttons to hand over the lead or put
    /// someone out, and the way to the team's own chat or out of the team. An invitation shows as a
    /// card at the top with ACEPTAR and RECHAZAR. Only online.
    /// </summary>
    public sealed class TeamUi
    {
        private sealed class Row
        {
            public RectTransform Rect;
            public Image Badge;
            public Image Icon;
            public Text Name;
            public Text State;
            public RectTransform Promote;
            public RectTransform Kick;
            public int Owner;
        }

        private readonly Hud _hud;
        private readonly RectTransform _button;
        private readonly Image _buttonIcon;
        private readonly RectTransform _panel;
        private readonly Text _state;
        private readonly Text _empty;
        private readonly Row[] _rows = new Row[Teams.MaxMembers];
        private readonly RectTransform _talk;
        private readonly RectTransform _leave;
        private readonly RectTransform _card;
        private readonly Text _cardText;
        private readonly List<TeamMate> _mates = new List<TeamMate>();
        private int _teamId, _leader, _me;
        private string _inviteFrom = "";
        private float _refresh;

        /// <summary>Something the player asks of their team: what, and about whom (an owner number; 0 for nobody).</summary>
        public event Action<TeamOp, int> Asked;

        /// <summary>The player wants to say something to the team alone.</summary>
        public event Action Talk;

        /// <summary>Where the player is now (for how far each teammate is).</summary>
        public Func<Vector3> Me { get; set; }

        public bool IsOpen => _panel.gameObject.activeSelf;
        public bool InTeam => _teamId != 0;
        public IReadOnlyList<TeamMate> Mates => _mates;

        /// <summary>The HUD button (for tests: its place on screen).</summary>
        public RectTransform Button => _button;

        public TeamUi(Hud hud)
        {
            _hud = hud;
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);

            _button = UiFactory.NewRect("Team button", hud.SafeArea, topLeft, topLeft, new Vector2(444f, -20f), new Vector2(64f, 48f));
            UiFactory.AddImage(_button, UiFactory.Soft, UiTheme.Panel);
            _buttonIcon = UiFactory.AddIcon(_button, Icons.Team, Vector2.zero, 36f, UiTheme.Text);
            hud.TapTargets.Add(new Hud.TapTarget { Rect = _button, OnTap = Toggle });
            _button.gameObject.SetActive(false);

            _panel = UiFactory.NewRect("Team", hud.SafeArea, half, half, new Vector2(0f, 10f), new Vector2(900f, 620f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(_panel);
            hud.CloseButton(_panel, Close);
            var badge = UiFactory.NewRect("Badge", _panel, topLeft, topLeft, new Vector2(40f, -34f), new Vector2(96f, 96f));
            UiFactory.AddImage(badge, UiFactory.Soft, UiTheme.Slot);
            UiFactory.AddIcon(badge, Icons.Team, Vector2.zero, 62f, UiTheme.Good);
            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(160f, -34f), new Vector2(600f, 54f));
            UiFactory.AddText(title, "EQUIPO", 40, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var state = UiFactory.NewRect("State", _panel, topLeft, topLeft, new Vector2(160f, -88f), new Vector2(640f, 40f));
            _state = UiFactory.AddText(state, "", 27, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);

            var empty = UiFactory.NewRect("Empty", _panel, topLeft, topLeft, new Vector2(40f, -160f), new Vector2(820f, 300f));
            _empty = UiFactory.AddText(empty,
                "Juega con amigos: acércate a alguien, míralo y toca INVITAR.\n\n" +
                $"Hasta {Teams.MaxMembers} por equipo. Os veis en el mapa y con el nombre en verde a cualquier distancia, " +
                "y tenéis un chat solo para el equipo.", 27, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            _empty.lineSpacing = 1.15f;

            for (int i = 0; i < _rows.Length; i++)
            {
                var row = new Row();
                row.Rect = UiFactory.NewRect("Member " + i, _panel, topLeft, topLeft, new Vector2(40f, -150f - i * 84f), new Vector2(820f, 74f));
                UiFactory.AddImage(row.Rect, UiFactory.Soft, UiTheme.Slot);
                var icon = UiFactory.NewRect("Icon", row.Rect, new Vector2(0f, 0.5f), half, new Vector2(42f, 0f), new Vector2(52f, 52f));
                row.Badge = UiFactory.AddImage(icon, UiFactory.Soft, UiTheme.Panel);
                row.Icon = UiFactory.AddIcon(icon, Icons.Team, Vector2.zero, 34f, UiTheme.Text);
                var name = UiFactory.NewRect("Name", row.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(84f, 14f), new Vector2(330f, 40f));
                row.Name = UiFactory.AddText(name, "", 29, TextAnchor.MiddleLeft, UiTheme.Text, true);
                var status = UiFactory.NewRect("State", row.Rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(84f, -18f), new Vector2(330f, 32f));
                row.State = UiFactory.AddText(status, "", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted);
                int index = i;
                row.Promote = RowButton(row.Rect, "LÍDER", new Vector2(-196f, 0f), UiTheme.Panel, UiTheme.Text, () => Ask(TeamOp.Promote, _rows[index].Owner));
                row.Kick = RowButton(row.Rect, "EXPULSAR", new Vector2(-14f, 0f), UiTheme.Bad, Color.white, () => Ask(TeamOp.Kick, _rows[index].Owner));
                _rows[i] = row;
            }

            _talk = PanelButton("CHAT DE EQUIPO", new Vector2(40f, -508f), UiTheme.Good, Color.white, () =>
            {
                Close();
                Talk?.Invoke();
            });
            _leave = PanelButton("SALIR DEL EQUIPO", new Vector2(470f, -508f), UiTheme.Slot, UiTheme.Bad, () => Ask(TeamOp.Leave, 0));
            _panel.gameObject.SetActive(false);

            // The invitation: a card at the top, over the HUD.
            _card = UiFactory.NewRect("Invitation", hud.SafeArea, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(720f, 168f));
            UiFactory.AddImage(_card, UiFactory.Rounded, UiTheme.PanelSolid);
            var cardIcon = UiFactory.NewRect("Icon", _card, topLeft, topLeft, new Vector2(28f, -26f), new Vector2(64f, 64f));
            UiFactory.AddImage(cardIcon, UiFactory.Soft, UiTheme.Slot);
            UiFactory.AddIcon(cardIcon, Icons.Team, Vector2.zero, 42f, UiTheme.Good);
            var cardText = UiFactory.NewRect("Text", _card, topLeft, topLeft, new Vector2(110f, -24f), new Vector2(580f, 70f));
            _cardText = UiFactory.AddText(cardText, "", 28, TextAnchor.MiddleLeft, UiTheme.Text, true);
            _cardText.horizontalOverflow = HorizontalWrapMode.Wrap;
            CardButton("ACEPTAR", new Vector2(110f, -100f), UiTheme.Good, Color.white, () => Ask(TeamOp.Accept, 0));
            CardButton("RECHAZAR", new Vector2(400f, -100f), UiTheme.Slot, UiTheme.Text, () => Ask(TeamOp.Decline, 0));
            hud.Blockers.Add(_card);
            _card.gameObject.SetActive(false);
        }

        /// <summary>Online the button shows; alone there is no one to team with.</summary>
        public void SetOnline(bool online)
        {
            if (_button.gameObject.activeSelf != online) _button.gameObject.SetActive(online);
            if (online) return;
            Apply(0, 0, null, "", _me);
            Close();
        }

        /// <summary>What the server says of this player's team now.</summary>
        public void Apply(int teamId, int leader, IReadOnlyList<TeamMate> mates, string inviteFrom, int me)
        {
            bool joined = teamId != 0 && _teamId == 0;
            bool left = teamId == 0 && _teamId != 0;
            // Who is new (told to those already in it).
            string newcomer = null;
            if (!joined && teamId != 0 && mates != null)
                foreach (var m in mates)
                {
                    bool known = false;
                    foreach (var old in _mates) known |= old.Owner == m.Owner;
                    if (!known) newcomer = m.Name;
                }
            _me = me;
            _teamId = teamId;
            _leader = leader;
            _mates.Clear();
            if (mates != null) _mates.AddRange(mates);
            inviteFrom ??= "";
            if (inviteFrom != _inviteFrom && inviteFrom.Length > 0)
                Debug.Log($"[Oxfall] Team invitation from {inviteFrom}");
            _inviteFrom = inviteFrom;
            _cardText.text = $"<b>{inviteFrom}</b> te invita a su equipo";
            _cardText.supportRichText = true;
            _card.gameObject.SetActive(inviteFrom.Length > 0);
            _buttonIcon.color = teamId != 0 ? UiTheme.Good : UiTheme.Text;
            if (joined)
            {
                Debug.Log($"[Oxfall] Team {teamId}: {_mates.Count} members");
                if (_mates.Count > 1) _hud.ShowToast("Ya estás en un equipo: tus compañeros salen en verde");
            }
            if (newcomer != null)
            {
                Debug.Log($"[Oxfall] Team: {newcomer} joined");
                _hud.ShowToast($"{newcomer} se ha unido al equipo");
            }
            if (left)
            {
                Debug.Log("[Oxfall] Team: none now");
                _hud.ShowToast("Ya no estás en un equipo");
            }
            if (IsOpen) Refresh();
        }

        /// <summary>Whether the player with this player id is a teammate (not this player).</summary>
        public bool IsMate(ushort playerId)
        {
            if (playerId == 0) return false;
            foreach (var m in _mates)
                if (m.PlayerId == playerId && m.Owner != _me) return true;
            return false;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            _hud.CloseCraftPanel();
            _panel.gameObject.SetActive(true);
            Refresh();
        }

        public void Close() => _panel.gameObject.SetActive(false);

        public void Tick(float dt)
        {
            if (!IsOpen) return;
            _refresh -= dt;
            if (_refresh > 0f) return;
            _refresh = 0.5f;
            Refresh();
        }

        private void Ask(TeamOp op, int owner)
        {
            if (op == TeamOp.Accept || op == TeamOp.Decline) _card.gameObject.SetActive(false);
            Asked?.Invoke(op, owner);
        }

        private void Refresh()
        {
            bool inTeam = _teamId != 0;
            bool leading = inTeam && _leader == _me;
            string leaderName = "";
            foreach (var m in _mates)
                if (m.Owner == _leader) leaderName = m.Name;
            _state.text = inTeam ? $"{_mates.Count} de {Teams.MaxMembers}  ·  Líder: {leaderName}" : "SIN EQUIPO";
            _state.color = inTeam ? UiTheme.Good : UiTheme.TextMuted;
            _empty.gameObject.SetActive(!inTeam);
            var here = Me != null ? Me() : Vector3.zero;
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                bool shown = inTeam && i < _mates.Count;
                row.Rect.gameObject.SetActive(shown);
                if (!shown) continue;
                var m = _mates[i];
                bool me = m.Owner == _me, leader = m.Owner == _leader;
                row.Owner = m.Owner;
                row.Name.text = me ? $"{m.Name} (tú)" : m.Name;
                row.Icon.sprite = leader ? Icons.Crown : Icons.Team;
                row.Icon.color = leader ? UiTheme.Accent : m.Online ? UiTheme.Good : UiTheme.TextFaint;
                float distance = new Vector2(m.X - here.x, m.Z - here.z).magnitude;
                row.State.text = !m.Online ? "Desconectado (durmiendo)"
                    : m.Dead ? "Muerto"
                    : me ? "En línea" : $"En línea  ·  {distance:0} m  ·  {MapUi.Square(new Vector3(m.X, 0f, m.Z))}";
                row.State.color = m.Online && !m.Dead ? UiTheme.Good : UiTheme.TextMuted;
                row.Promote.gameObject.SetActive(leading && !me);
                row.Kick.gameObject.SetActive(leading && !me);
            }
            _talk.gameObject.SetActive(inTeam);
            _leave.gameObject.SetActive(inTeam);
        }

        private RectTransform RowButton(RectTransform row, string text, Vector2 position, Color fill, Color textColour, Action onTap)
        {
            var right = new Vector2(1f, 0.5f);
            var button = UiFactory.NewRect(text, row, right, right, position, new Vector2(170f, 56f));
            UiFactory.AddImage(button, UiFactory.Soft, fill);
            var label = UiFactory.NewRect("Label", button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 40f));
            UiFactory.AddText(label, text, 23, TextAnchor.MiddleCenter, textColour, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
            return button;
        }

        private RectTransform PanelButton(string text, Vector2 position, Color fill, Color textColour, Action onTap)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(text, _panel, topLeft, topLeft, position, new Vector2(390f, 84f));
            UiFactory.AddImage(button, UiFactory.Soft, fill);
            var label = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(370f, 56f));
            UiFactory.AddText(label, text, 30, TextAnchor.MiddleCenter, textColour, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
            return button;
        }

        private void CardButton(string text, Vector2 position, Color fill, Color textColour, Action onTap)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(text, _card, topLeft, topLeft, position, new Vector2(270f, 52f));
            UiFactory.AddImage(button, UiFactory.Soft, fill);
            var label = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(260f, 40f));
            UiFactory.AddText(label, text, 26, TextAnchor.MiddleCenter, textColour, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
        }
    }
}
