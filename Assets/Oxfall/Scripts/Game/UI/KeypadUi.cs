using System;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// A code lock's panel. On the left, what the lock is doing and what can be done with it; on the
    /// right, its keypad: four boxes for the digits and a 3 by 4 pad. Three uses, as in the PC game:
    /// choosing the code of a lock just put on (that locks it), typing the code of someone else's
    /// lock (a wrong one shocks), and, for whoever knows it, locking, unlocking, changing the code or
    /// taking the lock off. Keys 0 to 9, Backspace and Enter work on a keyboard too.
    /// </summary>
    public sealed class KeypadUi
    {
        public enum Mode
        {
            /// <summary>A lock with no code yet: the first code locks it.</summary>
            Set,

            /// <summary>Someone else's locked lock: the right code lets the player through.</summary>
            Enter,

            /// <summary>A lock the player knows: lock, unlock, a new code, or off.</summary>
            Manage,
        }

        private const float PanelWidth = 940f;
        private const float PanelHeight = 620f;
        private const float Gap = 13f;

        private readonly Hud _hud;
        private readonly Settlement _settlement;
        private readonly GameActions _actions;
        private readonly RectTransform _panel;
        private readonly RectTransform _pad;
        private readonly Image _badge;
        private readonly Image _badgeIcon;
        private readonly Text _state;
        private readonly Text _detail;
        private readonly Text _padLabel;
        private readonly Image[] _boxes = new Image[4];
        private readonly Image[] _carets = new Image[4];
        private readonly Text[] _digits = new Text[4];
        private readonly Image[] _dots = new Image[4];
        private readonly Image _confirm;
        private readonly Image _confirmIcon;
        private readonly RectTransform _toggle;
        private readonly Text _toggleLabel;
        private readonly Image _toggleIcon;
        private readonly Image _toggleFill;
        private readonly RectTransform _remove;

        private LockTarget _target;
        private Mode _mode;
        private string _typed = "";
        private bool _awaiting;
        private float _awaitUntil;
        private float _shake;
        private float _flash;
        private Color _flashColour;
        private float _closeAt = -1f;
        private bool _expecting;
        private LockTarget _expected;

        public bool IsOpen => _panel.gameObject.activeSelf;
        public LockTarget Target => _target;
        public Mode Current => _mode;

        /// <summary>A sound to play: a key, a right code, a wrong one.</summary>
        public event Action<Audio.SoundCue> Sound;

        public KeypadUi(Hud hud, Settlement settlement, GameActions actions)
        {
            _hud = hud;
            _settlement = settlement;
            _actions = actions;
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            _panel = UiFactory.NewRect("Keypad", hud.SafeArea, half, half, new Vector2(0f, 30f), new Vector2(PanelWidth, PanelHeight));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(_panel);
            hud.CloseButton(_panel, Close);

            // The left: a badge, the name, the state, a line on what to do, and the buttons.
            var badge = UiFactory.NewRect("Badge", _panel, topLeft, topLeft, new Vector2(40f, -36f), new Vector2(100f, 100f));
            _badge = UiFactory.AddImage(badge, UiFactory.Soft, UiTheme.Slot);
            _badgeIcon = UiFactory.AddIcon(badge, Icons.Lock, Vector2.zero, 62f, UiTheme.Accent);
            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(164f, -40f), new Vector2(316f, 52f));
            UiFactory.AddText(title, "CANDADO", 40, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var state = UiFactory.NewRect("State", _panel, topLeft, topLeft, new Vector2(164f, -94f), new Vector2(316f, 38f));
            _state = UiFactory.AddText(state, "", 28, TextAnchor.MiddleLeft, UiTheme.Accent, true);
            var detail = UiFactory.NewRect("Detail", _panel, topLeft, topLeft, new Vector2(40f, -172f), new Vector2(420f, 220f));
            _detail = UiFactory.AddText(detail, "", 25, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.lineSpacing = 1.15f;

            _toggle = Button(_panel, "Toggle", new Vector2(40f, -414f), new Vector2(420f, 80f), UiTheme.Accent, ToggleLocked,
                out _toggleFill, out _toggleLabel, out _toggleIcon);
            _remove = Button(_panel, "Remove", new Vector2(40f, -506f), new Vector2(420f, 80f), UiTheme.Slot, Remove,
                out _, out var removeLabel, out var removeIcon);
            removeLabel.text = "QUITAR CANDADO";
            removeLabel.color = UiTheme.Text;
            removeIcon.sprite = Icons.Unlock;
            removeIcon.color = UiTheme.TextMuted;

            // The right: the digits typed so far, then the pad.
            _pad = UiFactory.NewRect("Pad", _panel, topLeft, topLeft, new Vector2(520f, -100f), new Vector2(380f, 486f));
            var label = UiFactory.NewRect("Label", _pad, topLeft, topLeft, new Vector2(0f, 0f), new Vector2(380f, 30f));
            _padLabel = UiFactory.AddText(label, "", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            for (int i = 0; i < 4; i++)
            {
                var box = UiFactory.NewRect("Digit " + i, _pad, topLeft, topLeft, new Vector2(i * (82f + 17.33f), -36f), new Vector2(82f, 100f));
                _boxes[i] = UiFactory.AddImage(box, UiFactory.Soft, UiTheme.Slot);
                var caret = UiFactory.NewRect("Caret", box, half, half, Vector2.zero, new Vector2(82f, 100f));
                _carets[i] = UiFactory.AddImage(caret, UiFactory.Outline, UiTheme.Accent);
                var digit = UiFactory.NewRect("Text", box, half, half, new Vector2(0f, 2f), new Vector2(82f, 90f));
                _digits[i] = UiFactory.AddText(digit, "", 58, TextAnchor.MiddleCenter, UiTheme.Text, true);
                var dot = UiFactory.NewRect("Dot", box, half, half, Vector2.zero, new Vector2(22f, 22f));
                _dots[i] = UiFactory.AddImage(dot, UiFactory.Circle, UiTheme.Text);
            }
            const float keyW = (380f - 2f * Gap) / 3f, keyH = 76f;
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 3; col++)
            {
                var position = new Vector2(col * (keyW + Gap), -152f - row * (keyH + 10f));
                if (row < 3)
                {
                    int digit = 1 + row * 3 + col;
                    Key(position, new Vector2(keyW, keyH), digit.ToString(), () => Press(digit));
                }
                else if (col == 0)
                {
                    var key = Key(position, new Vector2(keyW, keyH), "BORRAR", Delete);
                    var text = key.GetComponentInChildren<Text>();
                    text.fontSize = 24;
                    text.color = UiTheme.TextMuted;
                }
                else if (col == 1) Key(position, new Vector2(keyW, keyH), "0", () => Press(0));
                else
                {
                    var key = Key(position, new Vector2(keyW, keyH), "", Confirm);
                    _confirm = key.GetComponent<Image>();
                    _confirmIcon = UiFactory.AddIcon(key, Icons.Check, Vector2.zero, 40f, Color.white);
                }
            }
            _panel.gameObject.SetActive(false);

            settlement.LockChanged += OnLockChanged;
        }

        // ---- Opening and closing

        /// <summary>Opens on a lock, in the mode that fits what the player may do with it.</summary>
        public void Open(LockTarget target)
        {
            if (!_settlement.TryGetLock(target, out var state)) return;
            int me = _actions.Owner;
            var mode = !state.HasCode ? (state.CanManage(me) ? Mode.Set : Mode.Enter)
                : state.IsAuthorized(me) ? Mode.Manage : Mode.Enter;
            if (!state.HasCode && !state.CanManage(me)) return;
            Open(target, mode);
        }

        public void Open(LockTarget target, Mode mode)
        {
            _target = target;
            _mode = mode;
            _typed = "";
            _awaiting = false;
            _closeAt = -1f;
            _shake = 0f;
            _flash = 0f;
            _hud.CloseCraftPanel();
            _panel.gameObject.SetActive(true);
            Refresh();
            Debug.Log($"[Oxfall] Keypad open on {target} ({mode})");
        }

        public void Close()
        {
            if (!IsOpen) return;
            _panel.gameObject.SetActive(false);
            _awaiting = false;
            _closeAt = -1f;
        }

        /// <summary>
        /// A lock was just put on: its keypad opens to choose the code as soon as the lock is there
        /// (at once alone, when the server says so online).
        /// </summary>
        public void ExpectNew(LockTarget target)
        {
            _expecting = true;
            _expected = target;
            OnLockChanged(target);
        }

        // ---- Typing

        public void Press(int digit)
        {
            if (!IsOpen || _awaiting || _closeAt >= 0f || _typed.Length >= 4) return;
            _typed += (char)('0' + digit);
            Sound?.Invoke(Audio.SoundCue.KeyBeep);
            Refresh();
        }

        public void Delete()
        {
            if (!IsOpen || _awaiting || _typed.Length == 0) return;
            _typed = _typed.Substring(0, _typed.Length - 1);
            Sound?.Invoke(Audio.SoundCue.KeyBeep);
            Refresh();
        }

        /// <summary>For tests: types a whole code and confirms it.</summary>
        public void Type(string code)
        {
            foreach (char c in code)
                if (c >= '0' && c <= '9') Press(c - '0');
            Confirm();
        }

        public void Confirm()
        {
            if (!IsOpen || _awaiting || _closeAt >= 0f) return;
            if (_typed.Length < 4)
            {
                _hud.ShowToast("Escribe las 4 cifras");
                return;
            }
            int code = int.Parse(_typed);
            var op = _mode == Mode.Enter ? BuildOp.EnterCode(_target, code) : BuildOp.SetCode(_target, code);
            _awaiting = true;
            _awaitUntil = Time.unscaledTime + 4f;
            var result = _actions.Build(op);
            if (result == ActionResult.WrongCode) Wrong();
            else if (result != ActionResult.Ok)
            {
                _awaiting = false;
                _typed = "";
                _hud.ShowToast(GameActions.Explain(result));
                Refresh();
            }
            else if (_awaiting && !_actions.Online) Succeed();
        }

        /// <summary>The code was wrong (said by the rules alone, or by the server): a shake, red, and the shock.</summary>
        public void Wrong()
        {
            if (!IsOpen) return;
            _awaiting = false;
            _typed = "";
            _shake = 0.45f;
            _flash = 0.6f;
            _flashColour = UiTheme.Bad;
            Sound?.Invoke(Audio.SoundCue.Denied);
            _hud.ShowToast($"Código incorrecto: descarga de {CodeLocks.WrongCodeShock:0} de vida");
            Debug.Log("[Oxfall] Wrong code on " + _target);
            Refresh();
        }

        /// <summary>The server refused something else while waiting: let the player try again.</summary>
        public void Refused()
        {
            if (!_awaiting) return;
            _awaiting = false;
            _typed = "";
            Refresh();
        }

        private void Succeed()
        {
            _awaiting = false;
            _flash = 0.5f;
            _flashColour = UiTheme.Good;
            _closeAt = Time.unscaledTime + 0.45f;
            Sound?.Invoke(Audio.SoundCue.Accepted);
            if (_mode == Mode.Enter)
            {
                _hud.ShowToast("Código correcto: ya puedes pasar");
                Debug.Log("[Oxfall] Code accepted on " + _target);
            }
            else
            {
                _hud.ShowToast("Código guardado: el candado está cerrado. Compártelo solo con tu equipo");
                Debug.Log($"[Oxfall] Code set on {_target}");
            }
            Refresh();
        }

        // ---- The lock's buttons

        public void ToggleLocked()
        {
            if (!_settlement.TryGetLock(_target, out var state) || !state.HasCode) return;
            bool locking = !state.Locked;
            var result = _actions.Build(BuildOp.SetLocked(_target, locking));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            Sound?.Invoke(locking ? Audio.SoundCue.Accepted : Audio.SoundCue.KeyBeep);
            _hud.ShowToast(locking ? "Candado cerrado: solo pasan quienes saben el código" : "Candado abierto: cualquiera puede pasar");
            Debug.Log($"[Oxfall] Lock on {_target} {(locking ? "locked" : "unlocked")}");
        }

        public void Remove()
        {
            var result = _actions.Build(BuildOp.RemoveLock(_target));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            _hud.ShowToast("Candado quitado: vuelve a tu inventario");
            Debug.Log("[Oxfall] Lock removed from " + _target);
            Close();
        }

        // ---- Keeping up

        private void OnLockChanged(LockTarget target)
        {
            if (_expecting && target.Equals(_expected) && _settlement.TryGetLock(target, out var fresh))
            {
                _expecting = false;
                if (!fresh.HasCode && fresh.Owner == _actions.Owner) Open(target, Mode.Set);
                return;
            }
            if (!IsOpen || !target.Equals(_target)) return;
            if (!_settlement.TryGetLock(target, out var state))
            {
                Close();
                return;
            }
            int me = _actions.Owner;
            if (_awaiting)
            {
                // Online, the server's answer: the code was set, or this player was let through.
                if (_mode != Mode.Enter && state.HasCode) Succeed();
                else if (_mode == Mode.Enter && state.IsAuthorized(me)) Succeed();
                return;
            }
            if (_closeAt >= 0f) return;
            // Someone opened it meanwhile: nothing to type any more.
            if (_mode == Mode.Enter && _settlement.CanPass(target, me) && !state.IsAuthorized(me))
            {
                Close();
                return;
            }
            Refresh();
        }

        /// <summary>Every frame: the shake, the flash, the keyboard, and closing after a right code.</summary>
        public void Tick(float dt)
        {
            if (!IsOpen) return;
            if (_awaiting && Time.unscaledTime > _awaitUntil) Refused();
            if (_closeAt >= 0f && Time.unscaledTime >= _closeAt)
            {
                Close();
                return;
            }
            // Only while it moves: setting a position, even the same one, rebuilds the canvas.
            if (_shake > 0f)
            {
                _shake -= dt;
                float x = _shake > 0f ? Mathf.Sin(_shake * 70f) * 16f * Mathf.Clamp01(_shake / 0.45f) : 0f;
                _pad.anchoredPosition = new Vector2(520f + x, -100f);
            }
            if (_flash > 0f)
            {
                _flash -= dt;
                float k = Mathf.Clamp01(_flash / 0.6f);
                var fill = Color.Lerp(UiTheme.Slot, _flashColour.WithAlpha(0.85f), k);
                foreach (var box in _boxes) box.color = fill;
            }
            else if (_boxes[0].color != UiTheme.Slot)
                foreach (var box in _boxes) box.color = UiTheme.Slot;

            for (int d = 0; d <= 9; d++)
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha0 + d) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad0 + d)) Press(d);
            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace)) Delete();
            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) Confirm();
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void Refresh()
        {
            if (!_settlement.TryGetLock(_target, out var state)) return;
            bool hidden = _mode == Mode.Enter;
            for (int i = 0; i < 4; i++)
            {
                bool filled = i < _typed.Length;
                _digits[i].text = filled && !hidden ? _typed[i].ToString() : "";
                _dots[i].enabled = filled && hidden;
                _carets[i].enabled = i == _typed.Length && !_awaiting && _closeAt < 0f;
            }
            bool ready = _typed.Length == 4 && !_awaiting;
            _confirm.color = ready ? UiTheme.Accent : UiTheme.Slot;
            _confirmIcon.color = ready ? Color.white : UiTheme.TextFaint;

            switch (_mode)
            {
                case Mode.Set:
                    _state.text = "SIN CÓDIGO";
                    _state.color = UiTheme.Accent;
                    _detail.text = "Elige 4 cifras y confírmalas: el candado se cierra y solo tú lo abres.\n\n" +
                                   "Quien sepa el código también podrá pasar: compártelo solo con tu equipo.";
                    _padLabel.text = "CÓDIGO NUEVO";
                    break;
                case Mode.Enter:
                    _state.text = "CERRADO";
                    _state.color = UiTheme.Bad;
                    _detail.text = "Escribe el código de 4 cifras para pasar.\n\n" +
                                   $"Cada código incorrecto da una descarga de {CodeLocks.WrongCodeShock:0} de vida.";
                    _padLabel.text = "CÓDIGO";
                    break;
                default:
                    _state.text = state.Locked ? "CERRADO" : "ABIERTO";
                    _state.color = state.Locked ? UiTheme.Good : UiTheme.Accent;
                    int others = state.AuthorizedOwners.Count - 1;
                    _detail.text = (state.Locked
                                       ? "Solo pasan quienes saben el código."
                                       : "Abierto: cualquiera puede pasar hasta que lo cierres.") +
                                   (others > 0 ? $"\nLo saben {others} más." : "\nSolo tú lo sabes.") +
                                   "\n\nPara cambiar el código escribe uno nuevo: los demás tendrán que volver a escribirlo.";
                    _padLabel.text = "CAMBIAR CÓDIGO";
                    break;
            }
            bool manage = _mode == Mode.Manage;
            _toggle.gameObject.SetActive(manage);
            if (manage)
            {
                _toggleLabel.text = state.Locked ? "ABRIR CANDADO" : "CERRAR CANDADO";
                _toggleIcon.sprite = state.Locked ? Icons.Unlock : Icons.Lock;
                _toggleFill.color = state.Locked ? UiTheme.Slot : UiTheme.Accent;
                _toggleLabel.color = state.Locked ? UiTheme.Text : Color.white;
                _toggleIcon.color = state.Locked ? UiTheme.TextMuted : Color.white;
            }
            _remove.gameObject.SetActive(_mode != Mode.Enter);
            bool locked = _mode == Mode.Enter || (manage && state.Locked);
            _badgeIcon.sprite = _mode == Mode.Set ? Icons.Keypad : locked ? Icons.Lock : Icons.Unlock;
            _badgeIcon.color = _mode == Mode.Enter ? UiTheme.Bad : locked ? UiTheme.Good : UiTheme.Accent;
        }

        // ---- Small pieces

        private RectTransform Key(Vector2 position, Vector2 size, string text, Action onTap)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var key = UiFactory.NewRect("Key " + text, _pad, topLeft, topLeft, position, size);
            UiFactory.AddImage(key, UiFactory.Soft, UiTheme.Slot);
            if (text.Length > 0)
            {
                var label = UiFactory.NewRect("Label", key, half, half, new Vector2(0f, 1f), size);
                UiFactory.AddText(label, text, 42, TextAnchor.MiddleCenter, UiTheme.Text, true);
            }
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = key, OnTap = onTap });
            return key;
        }

        private RectTransform Button(RectTransform parent, string name, Vector2 position, Vector2 size, Color fill, Action onTap,
            out Image image, out Text label, out Image icon)
        {
            var topLeft = new Vector2(0f, 1f);
            var left = new Vector2(0f, 0.5f);
            var button = UiFactory.NewRect(name, parent, topLeft, topLeft, position, size);
            image = UiFactory.AddImage(button, UiFactory.Soft, fill);
            icon = UiFactory.AddIcon(button, Icons.Lock, new Vector2(48f, 0f), 40f, Color.white, left);
            var text = UiFactory.NewRect("Label", button, left, left, new Vector2(88f, 0f), new Vector2(size.x - 100f, 56f));
            label = UiFactory.AddText(text, "", 30, TextAnchor.MiddleLeft, Color.white, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
            return button;
        }
    }
}
