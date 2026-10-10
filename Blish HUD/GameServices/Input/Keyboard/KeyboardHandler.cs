using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework.Input;

namespace Blish_HUD.Input {
    public class KeyboardHandler : IInputHandler {
        public event EventHandler<KeyboardEventArgs> KeyPressed;
        public event EventHandler<KeyboardEventArgs> KeyReleased;
        public event EventHandler<KeyboardEventArgs> KeyStateChanged;
        public KeyboardState State { get; private set; }
        public ModifierKeys ActiveModifiers { get; private set; }
        private Control _focusedControl;
        public Control FocusedControl {
            get => _focusedControl;
            set { _focusedControl = value; Control.FocusedControl = value; }
        }
        private readonly List<Keys> _keysDown = new List<Keys>();
        public IReadOnlyList<Keys> KeysDown => _keysDown.AsReadOnly();
        private Action<string> _textInputDelegate;
        private readonly HashSet<KeyBinding> _bindings = new HashSet<KeyBinding>();
        private readonly object _bindingsLock = new object();
        private Capture _capture = Capture.Empty;
        private sealed class Capture {
            public static readonly Capture Empty = new Capture(Array.Empty<KeyCapture>(), null, false, false);
            public KeyCapture[] Bindings { get; }
            public Action<string> Text { get; }
            public bool Escape { get; }
            public bool AllowBlocking { get; }
            public Capture(KeyCapture[] bindings, Action<string> text, bool escape, bool allowBlocking) {
                Bindings = bindings; Text = text; Escape = escape; AllowBlocking = allowBlocking;
            }
        }
        internal KeyboardHandler() { }
        private void OnKeyStateChanged(KeyboardEventArgs input) {
            if (input.EventType == KeyboardEventType.KeyDown) KeyPressed?.Invoke(this, input);
            else KeyReleased?.Invoke(this, input);
            KeyStateChanged?.Invoke(this, input);
        }
        private void UpdateStates() {
            var keys = _keysDown.ToArray();
            State = new KeyboardState(keys);
            ActiveModifiers = KeysUtil.ModifiersFromKeys(keys);
        }
        internal void RegisterKeyBinding(KeyBinding binding) { lock (_bindingsLock) _bindings.Add(binding); }
        internal void UnregisterKeyBinding(KeyBinding binding) { lock (_bindingsLock) _bindings.Remove(binding); }
        public bool TextFieldIsActive() => (GameService.Gw2Mumble.IsAvailable && GameService.Gw2Mumble.UI.IsTextInputFocused) || _textInputDelegate != null;
        public void SetTextInputListner(Action<string> input) => _textInputDelegate = input;
        public void UnsetTextInputListner(Action<string> input) { if (_textInputDelegate == input) _textInputDelegate = null; }

        private Control EscapeTarget() {
            if (FocusedControl != null) return FocusedControl;
            if (!GameService.Overlay.CloseWindowOnEscape.Value) return null;
            var context = GameService.Graphics.SpriteScreen.Children.OfType<ContextMenuStrip>().FirstOrDefault(c => c.Visible);
            if (context != null) return context;
            var window = WindowBase2.ActiveWindow;
            return window != null && window.Visible && window.CanClose && window.CanCloseWithEscape ? window as Control : null;
        }
        private void RefreshCapture() {
            bool canBlock = !GameService.Overlay.InterfaceHidden
                && !(GameService.Gw2Mumble.IsAvailable && GameService.Gw2Mumble.UI.IsTextInputFocused);
            KeyCapture[] bindings;
            lock (_bindingsLock) {
                bindings = _bindings.Where(binding => binding.Enabled && binding.BlockSequenceFromGw2
                    && !(binding.IgnoreWhenInTextField && TextFieldIsActive()))
                    .Select(binding => new KeyCapture(binding.PrimaryKey, binding.ModifierKeys)).ToArray();
            }
            Volatile.Write(ref _capture, new Capture(bindings, canBlock ? _textInputDelegate : null,
                canBlock && EscapeTarget() != null, canBlock));
        }

        // Only update hook-owned key state and enqueue. Never call a control or module
        // here: a slow TextChanged handler must not stall the system keyboard chain.
        public bool HandleInput(KeyboardEventArgs input) {
            Interlocked.Increment(ref GameService.Input.KeyboardCallbacks);
            var session = GameService.Input.GetSession();
            if (session == null) return false;
            var capture = Volatile.Read(ref _capture);
            return session.Keyboard.Add(input, capture.Bindings, capture.Text, capture.Escape, capture.AllowBlocking);
        }
        public void Update() {
            var session = GameService.Input.GetSession();
            if (session == null) return;
            var focused = FocusedControl;
            if (focused != null && (!focused.Visible || focused.GetAncestors().Any(ancestor => !ancestor.Visible))) focused.UnsetFocus();
            RefreshCapture();
            while (session.Keyboard.Events.TryDequeue(out var pending)) {
                if (GameService.Input.GetSession() != session) return;
                var input = pending.Event;
                bool repeat = input.EventType == KeyboardEventType.KeyDown && _keysDown.Contains(input.Key);
                if (input.EventType == KeyboardEventType.KeyDown) {
                    if (!repeat) _keysDown.Add(input.Key);
                } else _keysDown.Remove(input.Key);
                UpdateStates();

                if (pending.Escape && !repeat) {
                    var target = EscapeTarget();
                    if (target == FocusedControl && target != null) target.UnsetFocus();
                    else target?.Hide();
                } else if (pending.TextKeyState != null && pending.TextTarget == _textInputDelegate) {
                    string text = TypedInputUtil.VkCodeToString((uint)input.Key, true, pending.TextKeyState);
                    pending.TextTarget?.Invoke(text);
                }
                if (!repeat) OnKeyStateChanged(input);
            }
            RefreshCapture();
        }
        public void OnEnable() {
            _keysDown.Clear();
            var keys = GameService.Input.Session.Keyboard.InitialKeys;
            foreach (Keys key in Enum.GetValues(typeof(Keys))) {
                if ((int)key > 0 && (int)key < keys.Length && (keys[(int)key] & 0x80) != 0) _keysDown.Add(key);
            }
            UpdateStates();
            RefreshCapture();
        }
        public void OnDisable() {
            Volatile.Write(ref _capture, Capture.Empty);
            FocusedControl?.UnsetFocus();
            FocusedControl = null;
            _textInputDelegate = null;
            var released = _keysDown.ToArray();
            _keysDown.Clear();
            UpdateStates();
            TypedInputUtil.Reset();
            foreach (var key in released) OnKeyStateChanged(new KeyboardEventArgs(KeyboardEventType.KeyUp, key));
        }
    }
}
