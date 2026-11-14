using System;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The tool cupboard's panel: whether the player may build around it, how many are authorized,
    /// and the two buttons the PC game has, AUTORIZARME and BORRAR LISTA. (Without a lock anyone
    /// can authorize themselves, as on an unlocked cupboard there.)
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
        private int _id;

        public bool IsOpen => _panel.gameObject.activeSelf;
        public int OpenId => _id;

        /// <summary>The player authorized themselves or cleared the list.</summary>
        public event Action Changed;

        public CupboardUi(Hud hud, Settlement settlement, GameActions actions)
        {
            _hud = hud;
            _settlement = settlement;
            _actions = actions;
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            _panel = UiFactory.NewRect("Cupboard", hud.SafeArea, half, half, new Vector2(0f, 20f), new Vector2(860f, 560f));
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

            var detail = UiFactory.NewRect("Detail", _panel, topLeft, topLeft, new Vector2(40f, -160f), new Vector2(780f, 200f));
            _detail = UiFactory.AddText(detail, "", 26, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.lineSpacing = 1.15f;

            _authorize = Button("AUTORIZARME", new Vector2(40f, -432f), UiTheme.Accent, Color.white, Authorize);
            _clear = Button("BORRAR LISTA", new Vector2(450f, -432f), UiTheme.Slot, UiTheme.Text, ClearList);
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
            _id = id;
            _hud.CloseCraftPanel();
            _panel.gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            _panel.gameObject.SetActive(false);
            _id = 0;
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
                "Ponlo dentro de tu casa y cierra la puerta: quien lo alcance puede autorizarse.";
            _authorize.gameObject.SetActive(!mine);
            _clear.gameObject.SetActive(mine);
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
