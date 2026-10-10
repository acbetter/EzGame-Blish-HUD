using System;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Blish_HUD.Input {
    // Each foreground visit owns its queues. Revocation discards in-flight old input.
    internal sealed class InputSession {
        public IntPtr GameWindow { get; }
        public MouseInputBuffer Mouse { get; }
        public KeyboardInputBuffer Keyboard { get; }
        public InputSession(IntPtr gameWindow, MouseState mouse, byte[] keys) {
            GameWindow = gameWindow;
            Mouse = new MouseInputBuffer(mouse);
            Keyboard = new KeyboardInputBuffer(keys);
        }
    }

    internal readonly struct PendingMouseInput {
        public MouseEventArgs Event { get; }
        public MouseState State { get; }
        public bool DispatchToControls { get; }
        public PendingMouseInput(MouseEventArgs input, MouseState state, bool dispatch) {
            Event = input; State = state; DispatchToControls = dispatch;
        }
    }

    internal sealed class MouseInputBuffer {
        public readonly ConcurrentQueue<PendingMouseInput> Events = new ConcurrentQueue<PendingMouseInput>();
        private MouseState _state;
        private bool _leftInHud, _rightInHud, _blockLeft, _blockRight;
        private volatile bool _cameraDragging;
        public bool CameraDragging => _cameraDragging;
        public bool IsReleaseOfHeldButton(MouseEventType type) =>
            (type == MouseEventType.LeftMouseButtonReleased && _state.LeftButton == ButtonState.Pressed)
            || (type == MouseEventType.RightMouseButtonReleased && _state.RightButton == ButtonState.Pressed);
        public MouseInputBuffer(MouseState initial) {
            _state = initial;
            _cameraDragging = initial.RightButton == ButtonState.Pressed;
        }

        // Hook-thread only. No control, module or window calls here.
        public bool Add(MouseEventArgs input, Point position, bool overHud, bool block, bool pointerAvailable) {
            var left = _state.LeftButton;
            var right = _state.RightButton;
            bool dispatch = pointerAvailable;
            bool handled = false;
            switch (input.EventType) {
                case MouseEventType.LeftMouseButtonPressed:
                    left = ButtonState.Pressed;
                    _leftInHud = overHud && pointerAvailable && !_cameraDragging;
                    _blockLeft = _leftInHud && block;
                    dispatch = pointerAvailable && !_cameraDragging;
                    handled = _blockLeft;
                    break;
                case MouseEventType.LeftMouseButtonReleased:
                    left = ButtonState.Released;
                    dispatch = _leftInHud && pointerAvailable;
                    handled = _blockLeft;
                    _leftInHud = _blockLeft = false;
                    break;
                case MouseEventType.RightMouseButtonPressed:
                    right = ButtonState.Pressed;
                    _rightInHud = overHud && pointerAvailable && !_cameraDragging;
                    _blockRight = _rightInHud && block;
                    _cameraDragging = !_rightInHud;
                    dispatch = _rightInHud;
                    handled = _blockRight;
                    break;
                case MouseEventType.RightMouseButtonReleased:
                    right = ButtonState.Released;
                    dispatch = _rightInHud && pointerAvailable;
                    handled = _blockRight;
                    _rightInHud = _blockRight = _cameraDragging = false;
                    break;
                case MouseEventType.MouseWheelScrolled:
                    dispatch = pointerAvailable && !_cameraDragging;
                    handled = overHud && block && dispatch;
                    break;
                default:
                    return false;
            }
            _state = new MouseState(position.X, position.Y, input.WheelDelta, left, _state.MiddleButton, right, _state.XButton1, _state.XButton2);
            Events.Enqueue(new PendingMouseInput(input, _state, dispatch));
            return handled;
        }
    }

    internal readonly struct PendingKeyboardInput {
        public KeyboardEventArgs Event { get; }
        public byte[] TextKeyState { get; }
        public Action<string> TextTarget { get; }
        public bool Escape { get; }
        public PendingKeyboardInput(KeyboardEventArgs input, byte[] textKeys, Action<string> target, bool escape) {
            Event = input; TextKeyState = textKeys; TextTarget = target; Escape = escape;
        }
    }

    internal readonly struct KeyCapture {
        public Keys Key { get; }
        public ModifierKeys Modifiers { get; }
        public KeyCapture(Keys key, ModifierKeys modifiers) { Key = key; Modifiers = modifiers; }
        public bool Matches(Keys key, ModifierKeys modifiers) => Key == key
            && (Modifiers == modifiers || Key == Keys.LeftAlt || Key == Keys.RightAlt
                || Key == Keys.LeftControl || Key == Keys.RightControl || Key == Keys.LeftShift || Key == Keys.RightShift);
    }

    internal sealed class KeyboardInputBuffer {
        public readonly ConcurrentQueue<PendingKeyboardInput> Events = new ConcurrentQueue<PendingKeyboardInput>();
        private readonly byte[] _keys;
        private readonly bool[] _blocked = new bool[256];
        public byte[] InitialKeys { get; }
        public KeyboardInputBuffer(byte[] initialKeys) {
            InitialKeys = (byte[])initialKeys.Clone();
            _keys = (byte[])initialKeys.Clone();
        }
        private bool Down(Keys key) => (_keys[(int)key] & 0x80) != 0;
        private ModifierKeys Modifiers =>
            (Down(Keys.LeftControl) || Down(Keys.RightControl) ? ModifierKeys.Ctrl : ModifierKeys.None)
            | (Down(Keys.LeftAlt) || Down(Keys.RightAlt) ? ModifierKeys.Alt : ModifierKeys.None)
            | (Down(Keys.LeftShift) || Down(Keys.RightShift) ? ModifierKeys.Shift : ModifierKeys.None);
        private static bool IsSystemKey(Keys key) => key == Keys.LeftWindows || key == Keys.RightWindows
            || key == Keys.LeftAlt || key == Keys.RightAlt || key == Keys.LeftControl || key == Keys.RightControl
            || key == Keys.LeftShift || key == Keys.RightShift || key == Keys.Tab || key == Keys.NumLock || key == Keys.CapsLock;

        public bool Add(KeyboardEventArgs input, KeyCapture[] bindings, Action<string> textTarget, bool escape, bool allowBlocking) {
            int key = (int)input.Key;
            if (key <= 0 || key >= _keys.Length) return false;
            bool down = input.EventType == KeyboardEventType.KeyDown;
            bool wasDown = (_keys[key] & 0x80) != 0;
            if (down && !wasDown && (input.Key == Keys.CapsLock || input.Key == Keys.NumLock || input.Key == Keys.Scroll)) _keys[key] ^= 1;
            _keys[key] = (byte)((_keys[key] & 1) | (down ? 0x80 : 0));
            var modifiers = Modifiers;
            _keys[0x10] = (byte)((modifiers & ModifierKeys.Shift) != 0 ? 0x80 : 0);
            _keys[0x11] = (byte)((modifiers & ModifierKeys.Ctrl) != 0 ? 0x80 : 0);
            _keys[0x12] = (byte)((modifiers & ModifierKeys.Alt) != 0 ? 0x80 : 0);
            bool system = IsSystemKey(input.Key) || Down(Keys.LeftWindows) || Down(Keys.RightWindows);
            bool handleEscape = down && input.Key == Keys.Escape && escape && allowBlocking && !system;
            bool handled = _blocked[key];
            if (down && !wasDown) {
                handled = allowBlocking && !system && (handleEscape || textTarget != null);
                if (allowBlocking && !system && !handled) {
                    foreach (var binding in bindings) {
                        if (binding.Matches(input.Key, modifiers)) { handled = true; break; }
                    }
                }
                _blocked[key] = handled;
            }
            if (!down) _blocked[key] = false;
            Events.Enqueue(new PendingKeyboardInput(input,
                textTarget != null && down && !system ? (byte[])_keys.Clone() : null, textTarget, handleEscape));
            return handled;
        }
    }
}
