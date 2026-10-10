using System;
using System.Threading;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;

namespace Blish_HUD {
    public class InputService : GameService {
        private static readonly Logger Logger = Logger.GetLogger<InputService>();
        private readonly IHookManager _hookManager;
        private InputSession _session;
        private InputSession _uiSession;
        private bool _hooksEnabled;
        private bool _interfaceHidden;
        private (IntPtr Game, IntPtr Foreground, WindowUtil.OverlayWindowKind Kind, bool Visible, bool Input)? _loggedState;
        internal long MouseCallbacks;
        internal long KeyboardCallbacks;
        public MouseHandler Mouse { get; }
        public KeyboardHandler Keyboard { get; }
        internal InputSession Session => _uiSession;
        internal bool IsInputEnabled => Volatile.Read(ref _session) != null;

        internal InputService() {
            Mouse = new MouseHandler();
            Keyboard = new KeyboardHandler();
            _hookManager = ApplicationSettings.Instance.DebugEnabled
                ? (IHookManager)new DebugHelperHookManager() : new WinApiHookManager();
        }

        // Hook callbacks can revoke input immediately. UI cancellation runs before
        // controls on the main thread, even if the game regained focus meanwhile.
        internal InputSession GetSession() {
            var session = Volatile.Read(ref _session);
            if (session != null && WindowUtil.GetForegroundWindow() != session.GameWindow) {
                Interlocked.CompareExchange(ref _session, null, session);
                return null;
            }
            return session;
        }
        internal void SuspendInput() => Interlocked.Exchange(ref _session, null);
        internal void EnableHooks() {
            if (_hooksEnabled) return;
            _hooksEnabled = _hookManager.EnableHook();
            if (!_hooksEnabled) Logger.Error("Failed to acquire input hooks.");
        }
        internal void DisableHooks() {
            SuspendInput();
            _hookManager.DisableHook();
            _hooksEnabled = false;
        }
        private void OnGw2Started(object sender, EventArgs args) => EnableHooks();
        private void OnGw2Closed(object sender, EventArgs args) => DisableHooks();
        protected override void Initialize() { }
        protected override void Load() {
            _hookManager.Load();
            _hookManager.RegisterMouseHandler(Mouse.HandleInput);
            _hookManager.RegisterKeyboardHandler(Keyboard.HandleInput);
            GameIntegration.Gw2Instance.Gw2Started += OnGw2Started;
            GameIntegration.Gw2Instance.Gw2Closed += OnGw2Closed;
            if (GameIntegration.Gw2Instance.Gw2IsRunning) EnableHooks();
        }
        protected override void Unload() {
            GameIntegration.Gw2Instance.Gw2Started -= OnGw2Started;
            GameIntegration.Gw2Instance.Gw2Closed -= OnGw2Closed;
            DisableHooks();
            CancelInteraction();
            _hookManager.UnregisterMouseHandler(Mouse.HandleInput);
            _hookManager.UnregisterKeyboardHandler(Keyboard.HandleInput);
            _hookManager.Dispose();
        }
        private void CancelInteraction() {
            if (_uiSession == null) return;
            SuspendInput();
            Mouse.OnDisable();
            Keyboard.OnDisable();
            _uiSession = null;
        }
        protected override void Update(GameTime gameTime) {
            var instance = GameIntegration.Gw2Instance;
            bool ready = _hooksEnabled && instance.Gw2IsRunning && instance.Gw2HasFocus
                && WindowUtil.IsWindowVisible(BlishHud.Instance.FormHandle)
                && WindowUtil.GetForegroundWindow() == instance.Gw2WindowHandle;
            if (!ready || _interfaceHidden != Overlay.InterfaceHidden) SuspendInput();
            _interfaceHidden = Overlay.InterfaceHidden;
            if (_uiSession != GetSession()) CancelInteraction();
            if (ready && _uiSession == null) {
                _uiSession = new InputSession(instance.Gw2WindowHandle, MouseHandler.ReadPhysicalState(), TypedInputUtil.ReadKeyboardState());
                Mouse.OnEnable();
                Keyboard.OnEnable();
                Volatile.Write(ref _session, _uiSession);
            }
            if (_uiSession != null) {
                Mouse.Update();
                Keyboard.Update();
                if (GetSession() == null) CancelInteraction();
            }
            LogState();
        }
        private void LogState() {
            var window = GameIntegration.Gw2Instance.WindowState;
            var foreground = WindowUtil.GetForegroundWindow();
            var state = (window.GameWindow, foreground, window.Kind,
                WindowUtil.IsWindowVisible(BlishHud.Instance.FormHandle), IsInputEnabled);
            if (_loggedState.HasValue && _loggedState.Value == state) return;
            _loggedState = state;
            Logger.Info("Overlay state={state}, game={game}, foreground={foreground}, foregroundPid={pid}, visible={visible}, input={input}, hooks={hooks}, hover={hover}, cameraDrag={camera}, cursorVisible={cursor}, mouseCallbacks={mouseCallbacks}, keyboardCallbacks={keyboardCallbacks}",
                window.Kind, window.GameWindow, foreground, WindowUtil.GetWindowProcessId(foreground), state.Item4, state.Item5,
                _hooksEnabled, Mouse.ActiveControl?.GetType().Name ?? "none", Mouse.CameraDragging, Mouse.CursorIsVisible,
                Interlocked.Read(ref MouseCallbacks), Interlocked.Read(ref KeyboardCallbacks));
        }
    }
}
