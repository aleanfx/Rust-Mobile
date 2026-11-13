using UnityEngine;

namespace Oxfall.Game.Controls
{
    /// <summary>
    /// What the player asked for this frame, however they asked (touch, keyboard). Gameplay reads
    /// only this, so touch controls, a gamepad or a network replay can all drive the same player.
    /// </summary>
    public sealed class PlayerInput
    {
        /// <summary>Stick direction, each axis -1..1, length at most 1.</summary>
        public Vector2 Move;

        /// <summary>How far to turn this frame, in degrees: x is yaw (right +), y is pitch (up +).</summary>
        public Vector2 Look;

        public bool Sprint;

        /// <summary>True for the one frame the jump button went down.</summary>
        public bool Jump;

        /// <summary>True while the attack button is held.</summary>
        public bool Attack;

        public void ClearFrame()
        {
            Look = Vector2.zero;
            Jump = false;
        }
    }
}
