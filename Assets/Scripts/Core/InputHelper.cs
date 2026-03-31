using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OpenTTDUnity
{
    /// <summary>
    /// Thin wrapper that re-implements the legacy <c>UnityEngine.Input</c> queries
    /// on top of the new Input System package so the rest of the codebase can keep
    /// its simple polling style without referencing the deprecated API.
    /// </summary>
    public static class InputHelper
    {
        // ── Keyboard ────────────────────────────────────────────────────────

        /// <summary>Returns true during the frame the key was pressed.</summary>
        public static bool GetKeyDown(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        /// <summary>Returns true while the key is held down.</summary>
        public static bool GetKey(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        /// <summary>Returns true during the frame the key was released.</summary>
        public static bool GetKeyUp(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasReleasedThisFrame;
        }

        // ── Mouse buttons ───────────────────────────────────────────────────

        /// <summary>Returns true during the frame the mouse button was pressed.
        /// 0 = left, 1 = right, 2 = middle.</summary>
        public static bool GetMouseButtonDown(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return GetMouseButtonControl(m, button)?.wasPressedThisFrame ?? false;
        }

        /// <summary>Returns true while the mouse button is held.</summary>
        public static bool GetMouseButton(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return GetMouseButtonControl(m, button)?.isPressed ?? false;
        }

        /// <summary>Returns true during the frame the mouse button was released.</summary>
        public static bool GetMouseButtonUp(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return GetMouseButtonControl(m, button)?.wasReleasedThisFrame ?? false;
        }

        // ── Mouse position & scroll ─────────────────────────────────────────

        /// <summary>Current mouse position in screen pixels.</summary>
        public static Vector2 mousePosition
        {
            get
            {
                var m = Mouse.current;
                return m != null ? m.position.ReadValue() : Vector2.zero;
            }
        }

        /// <summary>Mouse scroll delta this frame (y = vertical).</summary>
        public static Vector2 scrollDelta
        {
            get
            {
                var m = Mouse.current;
                return m != null ? m.scroll.ReadValue() / 120f : Vector2.zero;
            }
        }

        /// <summary>Vertical scroll amount this frame (replaces Input.GetAxis("Mouse ScrollWheel")).</summary>
        public static float scrollWheel => scrollDelta.y;

        // ── Helpers ─────────────────────────────────────────────────────────

        private static ButtonControl GetMouseButtonControl(Mouse m, int button)
        {
            switch (button)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.forwardButton;
                case 4: return m.backButton;
                default: return null;
            }
        }
    }
}
