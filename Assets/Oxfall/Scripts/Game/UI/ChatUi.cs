using System;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The server's chat, as on the PC game: lines down the left side under the vitals, the speaker's
    /// name in colour, each fading after a while; a speech-bubble button beside the player count to
    /// say something (the phone's keyboard, or the browser's text box on the web). Only online.
    /// </summary>
    public sealed class ChatUi
    {
        private const int Lines = 6;
        private const float LineHeight = 46f;
        private const float Stay = 12f;
        private const float Fade = 1.5f;
        private const float MaxWidth = 720f;

        private sealed class Line
        {
            public RectTransform Rect;
            public RectTransform Back;
            public CanvasGroup Group;
            public Text Text;
            public float Age = 99f;
        }

        private readonly Line[] _lines = new Line[Lines];
        private readonly RectTransform _root;
        private readonly RectTransform _button;
        private TouchScreenKeyboard _keyboard;
        private bool _toTeam;

        /// <summary>The player said something (already cleaned), and whether only to their team.</summary>
        public event Action<string, bool> Said;

        /// <summary>The chat button (for tests: its place on screen).</summary>
        public RectTransform Button => _button;

        public ChatUi(Hud hud)
        {
            var topLeft = new Vector2(0f, 1f);
            _root = UiFactory.NewRect("Chat", hud.SafeArea, topLeft, topLeft, new Vector2(24f, -232f), new Vector2(MaxWidth, Lines * LineHeight));
            _root.gameObject.AddComponent<Canvas>();
            for (int i = 0; i < Lines; i++)
            {
                var line = new Line();
                line.Rect = UiFactory.NewRect("Line", _root, topLeft, topLeft, new Vector2(0f, -i * LineHeight), new Vector2(MaxWidth, LineHeight - 6f));
                line.Group = line.Rect.gameObject.AddComponent<CanvasGroup>();
                line.Group.alpha = 0f;
                line.Back = UiFactory.NewRect("Back", line.Rect, topLeft, topLeft, Vector2.zero, new Vector2(200f, LineHeight - 6f));
                UiFactory.AddImage(line.Back, UiFactory.Soft, UiTheme.Panel);
                var text = UiFactory.NewRect("Text", line.Back, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(MaxWidth - 28f, LineHeight - 8f));
                line.Text = UiFactory.AddText(text, "", 25, TextAnchor.MiddleLeft, UiTheme.Text, true);
                line.Text.supportRichText = true;
                line.Text.horizontalOverflow = HorizontalWrapMode.Overflow;
                _lines[i] = line;
            }

            _button = UiFactory.NewRect("Chat button", hud.SafeArea, topLeft, topLeft, new Vector2(372f, -20f), new Vector2(64f, 48f));
            UiFactory.AddImage(_button, UiFactory.Soft, UiTheme.Panel);
            UiFactory.AddIcon(_button, Icons.Chat, Vector2.zero, 34f, UiTheme.Text);
            hud.TapTargets.Add(new Hud.TapTarget { Rect = _button, OnTap = Ask });
            _button.gameObject.SetActive(false);
        }

        /// <summary>How far down the lines start (under the first-steps card while it shows).</summary>
        public void SetDrop(float drop)
        {
            var at = new Vector2(24f, -232f - drop);
            if (_root.anchoredPosition != at) _root.anchoredPosition = at;
        }

        /// <summary>Online the button shows; offline there is no one to talk to.</summary>
        public void SetOnline(bool online)
        {
            if (_button.gameObject.activeSelf != online) _button.gameObject.SetActive(online);
        }

        /// <summary>A line from someone (or, with no name, from the server: joins, leaves, deaths); a team's own lines marked.</summary>
        public void Add(string name, string text, bool mine = false, bool team = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Older lines move down; the newest goes on top.
            var last = _lines[Lines - 1];
            for (int i = Lines - 1; i > 0; i--) _lines[i] = _lines[i - 1];
            _lines[0] = last;
            for (int i = 0; i < Lines; i++) _lines[i].Rect.anchoredPosition = new Vector2(0f, -i * LineHeight);

            string colour = ColorUtility.ToHtmlStringRGB(mine ? UiTheme.Good : UiTheme.Accent);
            string good = ColorUtility.ToHtmlStringRGB(UiTheme.Good);
            last.Text.text = string.IsNullOrEmpty(name)
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(UiTheme.TextMuted)}>{text}</color>"
                : team ? $"<color=#{good}><b>[EQUIPO]</b> {name}</color>  {text}"
                : $"<color=#{colour}>{name}</color>  {text}";
            last.Back.sizeDelta = new Vector2(Mathf.Min(MaxWidth, last.Text.preferredWidth + 28f), LineHeight - 6f);
            last.Age = 0f;
            last.Group.alpha = 1f;
            Debug.Log($"[Oxfall] Chat{(team ? " (team)" : "")}: {(string.IsNullOrEmpty(name) ? "*" : name)}: {text}");
        }

        /// <summary>Opens the keyboard to say something to everyone.</summary>
        public void Ask() => Ask(false);

        /// <summary>Opens the keyboard to say something to the team alone.</summary>
        public void AskTeam() => Ask(true);

        private void Ask(bool team)
        {
            _toTeam = team;
            string prompt = team ? "Mensaje para tu equipo" : "Mensaje para todos";
#if UNITY_WEBGL && !UNITY_EDITOR
            string typed = Net.BrowserPrompt.Ask(prompt, "");
            Send(typed, team);
#else
            if (TouchScreenKeyboard.isSupported)
                _keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, true, false, false, false, prompt,
                    Core.Net.Protocol.MaxChatLength);
#endif
        }

        /// <summary>Says something (for the keyboard's answer, and tests).</summary>
        public void Send(string typed, bool team = false)
        {
            string clean = Core.Net.Protocol.CleanChat(typed);
            if (clean != null) Said?.Invoke(clean, team);
        }

        public void Tick(float dt)
        {
            if (_keyboard != null && _keyboard.status != TouchScreenKeyboard.Status.Visible)
            {
                if (_keyboard.status == TouchScreenKeyboard.Status.Done) Send(_keyboard.text, _toTeam);
                _keyboard = null;
            }
            foreach (var line in _lines)
            {
                if (line.Age > Stay + Fade) continue;
                line.Age += dt;
                line.Group.alpha = line.Age <= Stay ? 1f : Mathf.Clamp01(1f - (line.Age - Stay) / Fade);
            }
        }
    }
}
