using System;
using System.Runtime.InteropServices;

namespace Oxfall.Game.Net
{
    /// <summary>The browser's text box (web build only), for typing a name.</summary>
    public static class BrowserPrompt
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern IntPtr OxfallPrompt(string title, string current);

        /// <summary>What the player typed, or null if they cancelled.</summary>
        public static string Ask(string title, string current)
        {
            var ptr = OxfallPrompt(title, current ?? "");
            if (ptr == IntPtr.Zero) return null;
            // The string was allocated on the browser side for us; it is small and asked for rarely.
            return Marshal.PtrToStringUTF8(ptr);
        }
#else
        public static string Ask(string title, string current) => null;
#endif
    }
}
