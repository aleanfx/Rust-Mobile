using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The tool cupboard's panel. On the left, whether the player may build around it, how many are
    /// authorized, and the two buttons the PC game has, AUTORIZARME and BORRAR LISTA (without a lock
    /// anyone can authorize themselves, as on an unlocked cupboard there). On the right its upkeep:
    /// how long what is stored in it keeps the base up, what that costs a day, and MATERIALES to
    /// stock it.
    /// </summary>
    public sealed class CupboardUi
    {
        private readonly Hud _hud;
        private readonly Settlement _settlement;
        private readonly GameActions _actions;
        private readonly RectTransform _panel;
        private readonly Text _state;
        private readonly Text _detail;
        private readonly Image _badge;
        private readonly RectTransform _authorize;
        private readonly RectTransform _clear;
        private readonly Text _upkeepHead;
        private readonly Text _upkeepDetail;
        private readonly Image _upkeepBar;
        private readonly RectTransform _materials;
        private readonly Dictionary<string, float> _cost = new Dictionary<string, float>();
        private Inventory _box;
        private int _id;

        public bool IsOpen => _panel.gameObject.activeSelf;
        public int OpenId => _id;

        /// <summary>The player authorized themselves or cleared the list.</summary>
        public event Action Changed;

        /// <summary>The player taps MATERIALES: the cupboard's slots, beside the inventory.</summary>
        public event Action<int> MaterialsRequested;

        public CupboardUi(Hud hud, Settlement settlement, GameActions actions)
        {
            _hud = hud;
            _settlement = settlement;
            _actions = actions;
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            _panel = UiFactory.NewRect("Cupboard", hud.SafeArea, half, half, new Vector2(0f, 20f), new Vector2(1180f, 560f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(_panel);
            hud.CloseButton(_panel, Close);

            var icon = UiFactory.NewRect("Badge", _panel, topLeft, topLeft, new Vector2(40f, -34f), new Vector2(96f, 96f));
            UiFactory.AddImage(icon, UiFactory.Soft, UiTheme.Slot);
            _badge = UiFactory.AddIcon(icon, Icons.Shield, Vector2.zero, 60f, UiTheme.Accent);
            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(160f, -34f), new Vector2(600f, 54f));
            UiFactory.AddText(title, "ARMARIO DE HERRAMIENTAS", 40, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var state = UiFactory.NewRect("State", _panel, topLeft, topLeft, new Vector2(160f, -88f), new Vector2(640f, 40f));
            _state = UiFactory.AddText(state, "", 28, TextAnchor.MiddleLeft, UiTheme.Accent, true);

            var detail = UiFactory.NewRect("Detail", _panel, topLeft, topLeft, new Vector2(40f, -160f), new Vector2(560f, 250f));
            _detail = UiFactory.AddText(detail, "", 26, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.lineSpacing = 1.15f;

            _authorize = Button("AUTORIZARME", new Vector2(40f, -432f), UiTheme.Accent, Color.white, Authorize);
            _clear = Button("BORRAR LISTA", new Vector2(40f, -432f), UiTheme.Slot, UiTheme.Text, ClearList);

            // The upkeep, in its own card on the right.
            var card = UiFactory.NewRect("Upkeep", _panel, topLeft, topLeft, new Vector2(640f, -150f), new Vector2(500f, 370f));
            UiFactory.AddImage(card, UiFactory.Soft, new Color(1f, 1f, 1f, 0.04f));
            var label = UiFactory.NewRect("Label", card, topLeft, topLeft, new Vector2(26f, -20f), new Vector2(450f, 30f));
            UiFactory.AddText(label, "MANTENIMIENTO", 24, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var head = UiFactory.NewRect("Head", card, topLeft, topLeft, new Vector2(26f, -56f), new Vector2(450f, 50f));
            _upkeepHead = UiFactory.AddText(head, "", 38, TextAnchor.MiddleLeft, UiTheme.Good, true);
            var track = UiFactory.NewRect("Track", card, topLeft, topLeft, new Vector2(26f, -116f), new Vector2(448f, 10f));
            UiFactory.AddImage(track, UiFactory.Soft, UiTheme.Slot);
            var bar = UiFactory.NewRect("Bar", track, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            _upkeepBar = UiFactory.AddImage(bar, UiFactory.Soft, UiTheme.Good);
            var upkeep = UiFactory.NewRect("Detail", card, topLeft, topLeft, new Vector2(26f, -142f), new Vector2(450f, 120f));
            _upkeepDetail = UiFactory.AddText(upkeep, "", 24, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _upkeepDetail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _upkeepDetail.lineSpacing = 1.1f;
            _materials = Button("MATERIALES", new Vector2(706f, -432f), UiTheme.Slot, UiTheme.Text, () =>
            {
                if (_id != 0) MaterialsRequested?.Invoke(_id);
            });
            _panel.gameObject.SetActive(false);
            settlement.DeployableChanged += id =>
            {
                if (id == _id && IsOpen) Refresh();
            };
            settlement.DeployableRemoved += id =>
            {
                if (id == _id) Close();
            };
        }

        public void Open(int id)
        {
            if (!_settlement.TryGet(id, out var d) || d.Kind != DeployableKind.ToolCupboard) return;
            Close();
            _id = id;
            _hud.CloseCraftPanel();
            _panel.gameObject.SetActive(true);
            // Opened like a box, so what is stored in it comes from the server (if its lock lets the player in).
            if (_settlement.CanPass(LockTarget.On(id), _actions.Owner) && _actions.Open(id) == ActionResult.Ok)
            {
                _box = _settlement.Container(id);
                if (_box != null) _box.Changed += OnBoxChanged;
            }
            Refresh();
        }

        public void Close()
        {
            _panel.gameObject.SetActive(false);
            if (_box != null)
            {
                _box.Changed -= OnBoxChanged;
                _box = null;
                _actions.Close();
            }
            _id = 0;
        }

        private void OnBoxChanged(int slot)
        {
            if (IsOpen) Refresh();
        }

        public void Authorize()
        {
            var result = _actions.Build(BuildOp.Authorize(_id));
            _hud.ShowToast(result == ActionResult.Ok ? "Autorizado: puedes construir aquí" : GameActions.Explain(result));
            if (result == ActionResult.Ok) Changed?.Invoke();
            Refresh();
        }

        public void ClearList()
        {
            var result = _actions.Build(BuildOp.ClearAuthorized(_id));
            _hud.ShowToast(result == ActionResult.Ok ? "Lista borrada: nadie está autorizado" : GameActions.Explain(result));
            if (result == ActionResult.Ok) Changed?.Invoke();
            Refresh();
        }

        private void Refresh()
        {
            if (_id == 0) return;
            bool mine = _settlement.IsAuthorized(_id, _actions.Owner);
            int count = _settlement.AuthorizedOn(_id).Count;
            _state.text = mine ? "ESTÁS AUTORIZADO" : "NO ESTÁS AUTORIZADO";
            _state.color = mine ? UiTheme.Good : UiTheme.Bad;
            _badge.color = mine ? UiTheme.Good : UiTheme.Accent;
            _detail.text =
                $"A {Deployables.PrivilegeRadius:0} m a su alrededor solo construyen los autorizados. " +
                $"Autorizados ahora: {count}.\n\n" +
                (_settlement.HasLock(LockTarget.On(_id))
                    ? "Tiene candado: solo quien sepa el código llega hasta aquí."
                    : "Ponlo dentro de tu casa y ponle un candado de código: si no, quien lo alcance puede autorizarse.");
            _authorize.gameObject.SetActive(!mine);
            _clear.gameObject.SetActive(mine);
            RefreshUpkeep();
        }

        private void RefreshUpkeep()
        {
            if (_box == null)
            {
                _upkeepHead.text = "CON CANDADO";
                _upkeepHead.color = UiTheme.TextMuted;
                _upkeepBar.rectTransform.anchorMax = new Vector2(0f, 1f);
                _upkeepDetail.text = "Solo quien sepa el código ve sus materiales.";
                _materials.gameObject.SetActive(false);
                return;
            }
            _materials.gameObject.SetActive(true);
            Describe(_settlement, _id, _cost, out string head, out string perDay, out int pieces, out var colour, out float share);
            _upkeepHead.text = head;
            _upkeepHead.color = colour;
            _upkeepBar.color = colour;
            _upkeepBar.rectTransform.anchorMax = new Vector2(share, 1f);
            _upkeepDetail.text = pieces == 0
                ? "Lo que construyas a su alrededor se paga desde aquí, con madera, piedra o metal."
                : $"Cada día: {perDay} ({pieces} {(pieces == 1 ? "pieza" : "piezas")})." +
                  (share <= 0f ? "\nSin materiales tu base pierde vida hasta caerse." : "\nGuarda más en MATERIALES para que dure.");
        }

        /// <summary>
        /// A cupboard's upkeep, for the player: a headline (how long its base is kept up, or that it
        /// decays), what that costs a day ("20 piedra · 5 madera", empty when it keeps nothing up), how
        /// many pieces it keeps, its colour, and how stocked it is (0..1, a week full).
        /// </summary>
        public static void Describe(Settlement settlement, int cupboard, Dictionary<string, float> cost, out string head, out string perDay,
            out int pieces, out Color colour, out float share)
        {
            pieces = Upkeep.DailyCost(settlement, cupboard, cost);
            perDay = "";
            if (pieces == 0)
            {
                head = "NADA QUE MANTENER";
                colour = UiTheme.TextMuted;
                share = 0f;
                return;
            }
            float covered = Upkeep.SecondsCovered(settlement, cupboard, cost);
            var parts = new List<string>(cost.Count);
            foreach (var pair in cost)
            {
                string name = settlement.Catalog.TryGet(pair.Key, out var d) ? d.DisplayName.ToLowerInvariant() : pair.Key;
                parts.Add(Mathf.CeilToInt(pair.Value) + " " + name);
            }
            perDay = string.Join(" · ", parts);
            if (covered < Upkeep.Interval)
            {
                head = "SE DETERIORA";
                colour = UiTheme.Bad;
                share = 0f;
                return;
            }
            head = "PROTEGIDA " + Span(covered);
            colour = covered < 3600f * 6f ? UiTheme.Accent : UiTheme.Good;
            share = Mathf.Clamp01(covered / (7f * Upkeep.Day));
        }

        /// <summary>A stretch of time, short: "2 d 5 h", "3 h 20 min", "15 min".</summary>
        public static string Span(float seconds)
        {
            if (float.IsInfinity(seconds)) return "";
            int minutes = (int)(seconds / 60f);
            int days = minutes / 1440, hours = minutes / 60 % 24;
            if (days > 0) return $"{days} d {hours} h";
            if (hours > 0) return $"{hours} h {minutes % 60} min";
            return $"{minutes} min";
        }

        private RectTransform Button(string text, Vector2 position, Color fill, Color textColour, Action onTap)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(text, _panel, topLeft, topLeft, position, new Vector2(370f, 92f));
            UiFactory.AddImage(button, UiFactory.Soft, fill);
            var label = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(350f, 60f));
            UiFactory.AddText(label, text, 32, TextAnchor.MiddleCenter, textColour, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
            return button;
        }
    }
}
