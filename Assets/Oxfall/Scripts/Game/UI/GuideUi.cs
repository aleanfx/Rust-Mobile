using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// First steps: a small card under the vitals with one thing to do at a time, from the first
    /// wood to the workbench, ticked off as the player does it (tap it for a hint). How far they got
    /// is kept between games; the × puts it away for good.
    /// </summary>
    public sealed class GuideUi
    {
        private const string StepKey = "oxfall.guide.step";
        private const string HiddenKey = "oxfall.guide.hidden";

        /// <summary>How near the player a piece must go up to count as theirs.</summary>
        private const float NearBuild = 20f;

        private sealed class Step
        {
            public string Text, Hint;
            /// <summary>The item whose icon it shows (null: the ruins' icon).</summary>
            public string Icon;
            /// <summary>Done when this has happened (see <see cref="GuideUi.Saw"/>)...</summary>
            public string Flag;
            /// <summary>...or when this many of this item are carried.</summary>
            public string Item;
            public int Amount;
        }

        private static readonly Step[] Steps =
        {
            new Step { Text = "Recoge 200 de madera", Hint = "Golpea un árbol: mantén GOLPEAR con la piedra en la mano", Icon = ItemIds.Wood, Item = ItemIds.Wood, Amount = 200 },
            new Step { Text = "Recoge 100 de piedra", Hint = "Golpea las rocas grandes; con un pico sale más", Icon = ItemIds.Stone, Item = ItemIds.Stone, Amount = 100 },
            new Step { Text = "Fabrica un hacha de piedra", Hint = "Toca FABRICAR (el martillo) y elige el hacha", Icon = ItemIds.StoneHatchet, Flag = "hatchet" },
            new Step { Text = "Construye un cimiento", Hint = "Fabrica un plano de construcción, llévalo en la mano y toca COLOCAR", Icon = ItemIds.BuildingPlan, Flag = "foundation" },
            new Step { Text = "Levanta paredes y pon una puerta", Hint = "Con el plano elige Marco de puerta; la puerta se fabrica en BASE", Icon = ItemIds.WoodenDoor, Flag = "door" },
            new Step { Text = "Pon un armario en tu base", Hint = "Nadie más construye cerca, y con madera o piedra dentro tu casa no se deteriora", Icon = ItemIds.ToolCupboard, Flag = "cupboard" },
            new Step { Text = "Pon un saco de dormir", Hint = "Si mueres, despiertas en él y no en la playa", Icon = ItemIds.SleepingBag, Flag = "bag" },
            new Step { Text = "Abre una caja en las ruinas", Hint = "Mira el mapa (toca la brújula). Ojo: hay bandidos armados", Icon = null, Flag = "crate" },
            new Step { Text = "Recicla piezas", Hint = "En las ruinas hay una recicladora: mete engranajes o tubos y enciéndela", Icon = ItemIds.Gears, Flag = "recycle" },
            new Step { Text = "Fabrica una mesa de trabajo", Hint = "Se hace con chatarra; cerca de ella fabricas armas y herramientas de metal", Icon = ItemIds.Workbench, Flag = "workbench" },
            new Step { Text = "Aprende un plano", Hint = "Junto a la mesa, en FABRICAR, elige algo con candado y apréndelo con chatarra", Icon = ItemIds.Scrap, Flag = "learn" },
        };

        private readonly Hud _hud;
        private readonly Inventory _inventory;
        private readonly Settlement _settlement;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly RectTransform _card;
        private readonly CanvasGroup _group;
        private readonly Image _icon, _accent, _bar;
        private readonly Text _caption, _count, _text;
        private readonly RectTransform _barFill;
        private const float BarWidth = 262f;
        private int _step;
        private bool _hidden;
        private float _doneFor, _check;
        private string _shown;

        /// <summary>Where the player is, to tell their building from a neighbour's.</summary>
        public System.Func<Vector3> Where;

        /// <summary>Who the player is (0 alone).</summary>
        public System.Func<int> Me;

        /// <summary>Whether a panel covers the screen (the card hides under it).</summary>
        public System.Func<bool> Covered;

        /// <summary>Whether the player has learned any blueprint.</summary>
        public System.Func<bool> HasLearned;

        /// <summary>A step was done.</summary>
        public event System.Action StepDone;

        public GuideUi(Hud hud, Inventory inventory, Settlement settlement)
        {
            _hud = hud;
            _inventory = inventory;
            _settlement = settlement;
            _step = Mathf.Clamp(PlayerPrefs.GetInt(StepKey, 0), 0, Steps.Length);
            _hidden = PlayerPrefs.GetInt(HiddenKey, 0) == 1;

            // What the player does, heard as it happens (online too: the server's copy comes back).
            settlement.Grid.CellPieceAdded += (type, cell) =>
            {
                if (type == PieceType.Foundation && Near(new Vector3((cell.X + 0.5f) * 3f, 0f, (cell.Z + 0.5f) * 3f))) Saw("foundation");
            };
            settlement.Grid.DoorPlaced += edge =>
            {
                if (Near(new Vector3(edge.X * 3f, 0f, edge.Z * 3f))) Saw("door");
            };
            settlement.DeployableAdded += d =>
            {
                if (d.Kind == DeployableKind.Recycler || d.Owner != (Me?.Invoke() ?? 0)) return;
                if (d.Kind == DeployableKind.ToolCupboard) Saw("cupboard");
                else if (d.Kind == DeployableKind.SleepingBag) Saw("bag");
                else if (d.Kind == DeployableKind.Workbench) Saw("workbench");
            };

            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            _card = UiFactory.NewRect("Guide", hud.SafeArea, topLeft, topLeft, new Vector2(24f, -232f), new Vector2(400f, 112f));
            UiFactory.AddImage(_card, UiFactory.Soft, UiTheme.Panel);
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var accent = UiFactory.NewRect("Accent", _card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(6f, 112f));
            _accent = UiFactory.AddImage(accent, null, UiTheme.Accent);

            var slot = UiFactory.NewRect("Slot", _card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(76f, 76f));
            UiFactory.AddImage(slot, UiFactory.Soft, UiTheme.Slot);
            _icon = UiFactory.AddIcon(slot, null, Vector2.zero, 60f, Color.white);

            var caption = UiFactory.NewRect("Caption", _card, topLeft, topLeft, new Vector2(112f, -10f), new Vector2(200f, 26f));
            _caption = UiFactory.AddText(caption, "", 18, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var count = UiFactory.NewRect("Count", _card, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -10f), new Vector2(110f, 26f));
            _count = UiFactory.AddText(count, "", 18, TextAnchor.MiddleRight, UiTheme.TextMuted, true);
            var text = UiFactory.NewRect("Text", _card, topLeft, topLeft, new Vector2(112f, -36f), new Vector2(BarWidth, 52f));
            _text = UiFactory.AddText(text, "", 24, TextAnchor.UpperLeft, UiTheme.Text, true);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.lineSpacing = 0.9f;

            var bar = UiFactory.NewRect("Bar", _card, topLeft, topLeft, new Vector2(112f, -94f), new Vector2(BarWidth, 6f));
            _bar = UiFactory.AddImage(bar, UiFactory.Soft, UiTheme.Slot);
            _barFill = UiFactory.NewRect("Fill", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 6f));
            UiFactory.AddImage(_barFill, UiFactory.Soft, UiTheme.Accent);

            // Put away for good: the × in the corner.
            var close = UiFactory.NewRect("Close", _card, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(40f, 40f));
            UiFactory.AddIcon(close, Icons.Close, Vector2.zero, 18f, UiTheme.TextFaint);
            hud.TapTargets.Add(new Hud.TapTarget { Rect = close, OnTap = Dismiss });
            hud.TapTargets.Add(new Hud.TapTarget { Rect = _card, OnTap = ShowHint });
            hud.Blockers.Add(_card);
            _card.gameObject.SetActive(false);
        }

        /// <summary>Something the steps wait for happened ("crate", "recycle", ...): it counts even ahead of its turn.</summary>
        public void Saw(string flag) => _seen.Add(flag);

        /// <summary>Whether its card is on screen (the chat's lines go under it then).</summary>
        public bool Showing => _card.gameObject.activeSelf;

        /// <summary>Back to the first step, shown again (for tests).</summary>
        public void Restart()
        {
            _step = 0;
            _hidden = false;
            _seen.Clear();
            _doneFor = 0f;
            _shown = null;
            PlayerPrefs.SetInt(StepKey, 0);
            PlayerPrefs.SetInt(HiddenKey, 0);
        }

        public void Tick(float dt)
        {
            bool show = !_hidden && _step < Steps.Length + 1 && !(Covered?.Invoke() ?? false);
            if (_card.gameObject.activeSelf != show) _card.gameObject.SetActive(show);
            if (_hidden || _step > Steps.Length) return;

            // The last step done: a word of farewell, then away.
            if (_step == Steps.Length)
            {
                ShowFinished();
                _doneFor += dt;
                if (_doneFor > 4f)
                {
                    _step++;
                    _card.gameObject.SetActive(false);
                }
                return;
            }

            var step = Steps[_step];
            if (_doneFor > 0f)
            {
                // Ticked off: a moment in green with a check, then the next one fades in.
                _doneFor -= dt;
                if (_doneFor <= 0f)
                {
                    _step++;
                    PlayerPrefs.SetInt(StepKey, _step);
                    _shown = null;
                    _group.alpha = 0f;
                }
                return;
            }
            _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt * 4f);

            if (_shown != step.Text) Show(step);
            // The item icons are drawn a moment after start: the card's picture comes when they are.
            if (!_icon.enabled && step.Icon != null)
            {
                var sprite = ItemIcons.Get(step.Icon);
                if (sprite != null)
                {
                    _icon.sprite = sprite;
                    _icon.enabled = true;
                }
            }
            int have = step.Item != null ? _inventory.Count(step.Item) : 0;
            if (step.Item != null)
            {
                _count.text = $"{Mathf.Min(have, step.Amount)}/{step.Amount}";
                _barFill.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(have / (float)step.Amount), 6f);
            }

            _check -= dt;
            if (_check > 0f) return;
            _check = 0.25f;
            if (step.Flag == "hatchet" && (_inventory.Count(ItemIds.StoneHatchet) > 0 || _inventory.Count(ItemIds.MetalHatchet) > 0)) Saw("hatchet");
            if (step.Flag == "workbench" && _inventory.Count(ItemIds.Workbench) > 0) Saw("workbench");
            if (step.Flag == "learn" && HasLearned != null && HasLearned()) Saw("learn");
            bool done = step.Item != null ? have >= step.Amount : _seen.Contains(step.Flag);
            if (done) Complete();
        }

        private void Show(Step step)
        {
            _shown = step.Text;
            _caption.text = $"PRIMEROS PASOS · {_step + 1}/{Steps.Length}";
            _text.text = step.Text;
            _text.color = UiTheme.Text;
            _accent.color = UiTheme.Accent;
            _count.text = "";
            _bar.gameObject.SetActive(step.Item != null);
            _barFill.sizeDelta = new Vector2(0f, 6f);
            var sprite = step.Icon == null ? Icons.Ruins : ItemIcons.Get(step.Icon);
            _icon.sprite = sprite;
            _icon.enabled = sprite != null;
            _icon.color = step.Icon == null ? UiTheme.TextMuted : Color.white;
        }

        private void Complete()
        {
            _doneFor = 1.3f;
            _text.text = "¡Hecho!";
            _text.color = UiTheme.Good;
            _accent.color = UiTheme.Good;
            _count.text = "";
            _bar.gameObject.SetActive(false);
            _icon.sprite = Icons.Check;
            _icon.color = UiTheme.Good;
            _icon.enabled = true;
            _group.alpha = 1f;
            StepDone?.Invoke();
        }

        private void ShowFinished()
        {
            if (_shown == "done") return;
            _shown = "done";
            _caption.text = "PRIMEROS PASOS";
            _text.text = "¡Listo! Ya sabes sobrevivir";
            _text.color = UiTheme.Good;
            _accent.color = UiTheme.Good;
            _count.text = "";
            _bar.gameObject.SetActive(false);
            _icon.sprite = Icons.Check;
            _icon.color = UiTheme.Good;
            _icon.enabled = true;
            _group.alpha = 1f;
        }

        private void ShowHint()
        {
            if (_hidden || !_card.gameObject.activeSelf || _step >= Steps.Length) return;
            _hud.ShowToast(Steps[_step].Hint);
        }

        private void Dismiss()
        {
            if (!_card.gameObject.activeSelf) return;
            _hidden = true;
            PlayerPrefs.SetInt(HiddenKey, 1);
            _card.gameObject.SetActive(false);
            _hud.ShowToast("Primeros pasos ocultos");
        }

        private bool Near(Vector3 at)
        {
            if (Where == null) return true;
            var here = Where();
            float dx = here.x - at.x, dz = here.z - at.z;
            return dx * dx + dz * dz < NearBuild * NearBuild;
        }
    }
}
