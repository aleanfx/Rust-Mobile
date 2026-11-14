using System;
using Oxfall.Game.UI;
using UnityEngine;

namespace Oxfall.Game.Controls
{
    /// <summary>
    /// Turns fingers into <see cref="PlayerInput"/>. Each finger is claimed by one control when it
    /// touches down (a button, the stick, or the look area) and keeps it until it lifts, so the
    /// stick, looking and the buttons all work at the same time.
    /// </summary>
    /// <remarks>
    /// Reads raw touches rather than going through uGUI events: one place decides who owns a finger,
    /// and nothing depends on an EventSystem being in the scene.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class TouchControls : MonoBehaviour
    {
        private const int None = int.MinValue;

        /// <summary>Degrees turned by dragging one screen-height.</summary>
        public float LookDegreesPerScreen = 150f;

        /// <summary>Stick push (0..1) beyond which the player sprints, if pushing forward.</summary>
        public float SprintThreshold = 0.92f;

        public event Action QualityTapped;

        /// <summary>Keyboard only: a number key 1 to 6 (as a slot 0 to 5), and the inventory key (Tab or I).</summary>
        public event Action<int> SlotKey;
        public event Action InventoryKey;

        /// <summary>Raised when a button on screen is pressed (for its click).</summary>
        public event Action Tapped;

        private PlayerInput _input;
        private Hud _hud;

        private int _stickFinger = None;
        private Vector2 _stickCentre;
        private int _lookFinger = None;
        private Vector2 _lookLast;
        private int _attackFinger = None;
        private int _jumpFinger = None;
        private bool _mouseOnUi;

        public void Init(PlayerInput input, Hud hud)
        {
            _input = input;
            _hud = hud;
            UnityEngine.Input.multiTouchEnabled = true;
        }

        private void Update()
        {
            if (_input == null) return;
            _input.ClearFrame();

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                Handle(UnityEngine.Input.GetTouch(i));
            _input.Attack = _attackFinger != None;

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            KeyboardAndMouse();
#endif

            _hud.SetPressed(_hud.AttackImage, _attackFinger != None);
            _hud.SetPressed(_hud.JumpImage, _jumpFinger != None);
        }

        private void Handle(Touch touch)
        {
            int id = touch.fingerId;
            var pos = touch.position;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    Claim(id, pos);
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (id == _stickFinger) UpdateStick(pos);
                    else if (id == _lookFinger)
                    {
                        var delta = pos - _lookLast;
                        _lookLast = pos;
                        float scale = LookDegreesPerScreen / Mathf.Max(1f, Screen.height);
                        _input.Look += delta * scale;
                    }
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    Release(id);
                    break;
            }
        }

        /// <summary>
        /// Runs the button under a touch or click, if any. Returns true when the touch landed on a
        /// button or a panel (an open panel or the hotbar), which is then not a move or a look.
        /// </summary>
        private bool TapAt(Vector2 pos)
        {
            var modal = _hud.Modal;
            bool modalOpen = modal != null && modal.gameObject.activeInHierarchy;
            // Copied first: a tap may open a panel and add or hide buttons.
            foreach (var target in _hud.TapTargets.ToArray())
            {
                if (modalOpen && !target.Rect.IsChildOf(modal)) continue;
                if (!target.Rect.gameObject.activeInHierarchy || !Inside(target.Rect, pos)) continue;
                target.OnTap?.Invoke();
                Tapped?.Invoke();
                return true;
            }
            return modalOpen || _hud.Blocks(pos);
        }

        private void Claim(int id, Vector2 pos)
        {
            if (TapAt(pos)) return;

            if (Inside(_hud.AttackButton, pos) && _attackFinger == None)
            {
                _attackFinger = id;
                _input.AttackPressed = true;
                return;
            }
            if (Inside(_hud.JumpButton, pos) && _jumpFinger == None)
            {
                _jumpFinger = id;
                _input.Jump = true;
                return;
            }
            bool leftSide = pos.x < Screen.width * 0.42f && pos.y < Screen.height * 0.8f;
            if (leftSide && _stickFinger == None)
            {
                _stickFinger = id;
                // The stick jumps to the thumb, so it is always under it wherever it lands.
                _stickCentre = pos;
                _hud.JoystickBase.position = pos;
                UpdateStick(pos);
                return;
            }

            if (!leftSide && _lookFinger == None)
            {
                _lookFinger = id;
                _lookLast = pos;
            }
        }

        private void Release(int id)
        {
            if (id == _stickFinger)
            {
                _stickFinger = None;
                _input.Move = Vector2.zero;
                _input.Sprint = false;
                _hud.JoystickKnob.anchoredPosition = Vector2.zero;
                _hud.JoystickBase.anchoredPosition = _hud.JoystickHome;
            }
            if (id == _lookFinger) _lookFinger = None;
            if (id == _attackFinger) _attackFinger = None;
            if (id == _jumpFinger) _jumpFinger = None;
        }

        private void UpdateStick(Vector2 pos)
        {
            float radiusPixels = Hud.JoystickRadius * _hud.Canvas.scaleFactor;
            var offset = Vector2.ClampMagnitude(pos - _stickCentre, radiusPixels);
            var stick = offset / radiusPixels;
            _input.Move = stick;
            _input.Sprint = stick.magnitude >= SprintThreshold && stick.y > 0.7f;
            _hud.JoystickKnob.anchoredPosition = offset / _hud.Canvas.scaleFactor;
        }

        private static bool Inside(RectTransform rect, Vector2 screenPoint) =>
            rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        /// <summary>For trying the game on a computer: WASD, Shift, Space, mouse to look, left click to hit.</summary>
        private void KeyboardAndMouse()
        {
            if (UnityEngine.Input.touchCount > 0) return;

            var move = new Vector2(
                (UnityEngine.Input.GetKey(KeyCode.D) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.A) ? 1f : 0f),
                (UnityEngine.Input.GetKey(KeyCode.W) ? 1f : 0f) - (UnityEngine.Input.GetKey(KeyCode.S) ? 1f : 0f));
            if (move != Vector2.zero || _stickFinger == None)
            {
                _input.Move = Vector2.ClampMagnitude(move, 1f);
                _input.Sprint = UnityEngine.Input.GetKey(KeyCode.LeftShift) && move.y > 0f;
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) _input.Jump = true;
            if (UnityEngine.Input.GetMouseButton(1))
                _input.Look += new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"), UnityEngine.Input.GetAxisRaw("Mouse Y")) * 3f;

            // A click on a button presses it; anywhere else it hits (or places).
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _mouseOnUi = TapAt(UnityEngine.Input.mousePosition);
                if (!_mouseOnUi) _input.AttackPressed = true;
            }
            if (!UnityEngine.Input.GetMouseButton(0)) _mouseOnUi = false;
            _input.Attack |= UnityEngine.Input.GetMouseButton(0) && !_mouseOnUi;

            if (UnityEngine.Input.GetKeyDown(KeyCode.G)) QualityTapped?.Invoke();
            for (int i = 0; i < 6; i++)
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) SlotKey?.Invoke(i);
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab) || UnityEngine.Input.GetKeyDown(KeyCode.I)) InventoryKey?.Invoke();
            // E uses (doors), R presses the button above attack (next piece, demolish).
            if (UnityEngine.Input.GetKeyDown(KeyCode.E) && _hud.UseButton.gameObject.activeInHierarchy) PressTarget(_hud.UseButton);
            if (UnityEngine.Input.GetKeyDown(KeyCode.R) && _hud.PieceButton.gameObject.activeInHierarchy) PressTarget(_hud.PieceButton);
            if (UnityEngine.Input.GetKeyDown(KeyCode.L) && _hud.LockButton.gameObject.activeInHierarchy) PressTarget(_hud.LockButton);
        }

        private void PressTarget(RectTransform rect)
        {
            foreach (var target in _hud.TapTargets)
                if (target.Rect == rect) { target.OnTap?.Invoke(); return; }
        }
#endif
    }
}
