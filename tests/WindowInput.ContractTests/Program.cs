using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Blish_HUD;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;
using MouseArgs = Blish_HUD.Input.MouseEventArgs;

internal static class Program {
    private static int _passed;
    private static readonly KeyCapture[] NoBindings = Array.Empty<KeyCapture>();
    [STAThread]
    private static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "--smoke") return RuntimeSmoke.Run(args);
        try {
            Run("rapid clicks and wheel retain order and event-time button state", OrderedMouse);
            Run("release ownership survives leaving HUD bounds", MouseOwnership);
            Run("resumed held buttons do not synthesize a HUD click", ResumedMouse);
            Run("camera drag cannot acquire HUD input midway", CameraDrag);
            Run("old visit queues cannot leak into the next visit", SessionIsolation);
            Run("shortcut matching uses exact event-time modifiers", ShortcutModifiers);
            Run("key repeat/release retain ownership when capture changes", KeyboardOwnership);
            Run("system chords and pre-held keys pass through", SystemKeys);
            Run("text callbacks stay off hook and preserve modifier state", DeferredText);
            Run("partial hook failure rolls back; next attempt reports actual result", HookFailures);
            Run("hook lifetime is idempotent and cleanup uses its installing thread", HookLifecycle);
            Run("native WinAPI hooks install, disable and reinstall", NativeHooks);
            Run("native window visibility, nonactivation, z-order and hit testing", NativeWindows);
            Console.WriteLine($"PASS: {_passed} behavioral scenarios.");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
    private static void Run(string name, Action test) { test(); _passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    private static MouseArgs Mouse(MouseEventType type, int wheel = 0) => new MouseArgs(type, 50, 60, wheel << 16, 0, 0, 0);
    private static bool Add(MouseInputBuffer buffer, MouseEventType type, bool hud = true, bool block = true, bool pointer = true, int wheel = 0)
        => buffer.Add(Mouse(type, wheel), new Point(50, 60), hud, block, pointer);
    private static KeyboardEventArgs Key(Keys key, bool down = true) => new KeyboardEventArgs(down ? KeyboardEventType.KeyDown : KeyboardEventType.KeyUp, key);
    private static bool Add(KeyboardInputBuffer buffer, Keys key, bool down = true, KeyCapture[] bindings = null,
        Action<string> text = null, bool escape = false, bool allow = true)
        => buffer.Add(Key(key, down), bindings ?? NoBindings, text, escape, allow);
    private static void OrderedMouse() {
        var buffer = new MouseInputBuffer(default);
        for (int i = 0; i < 20; i++) {
            Check(Add(buffer, MouseEventType.LeftMouseButtonPressed), "HUD down must be consumed");
            Check(Add(buffer, MouseEventType.LeftMouseButtonReleased), "HUD up must be consumed");
            Add(buffer, MouseEventType.MouseWheelScrolled, wheel: i % 2 == 0 ? 120 : -120);
        }
        var events = buffer.Events.ToArray();
        Check(events.Length == 60, "A fast event was overwritten");
        for (int i = 0; i < 20; i++) {
            Check(events[i * 3].State.LeftButton == ButtonState.Pressed, "Lost down state");
            Check(events[i * 3 + 1].State.LeftButton == ButtonState.Released, "Lost up state");
            Check(events[i * 3 + 2].State.ScrollWheelValue == (i % 2 == 0 ? 120 : -120), "Lost wheel delta/order");
        }
    }
    private static void MouseOwnership() {
        var buffer = new MouseInputBuffer(default);
        Check(!Add(buffer, MouseEventType.LeftMouseButtonPressed, hud: false), "Game down swallowed");
        Check(!Add(buffer, MouseEventType.LeftMouseButtonReleased), "Game-owned up swallowed over HUD");
        Check(!buffer.Events.ToArray()[1].DispatchToControls, "Game-origin release clicked HUD");
        Check(Add(buffer, MouseEventType.LeftMouseButtonPressed), "HUD down escaped");
        Check(buffer.IsReleaseOfHeldButton(MouseEventType.LeftMouseButtonReleased), "Held sequence forgotten");
        Check(Add(buffer, MouseEventType.LeftMouseButtonReleased, hud: false, pointer: false), "HUD-owned up leaked to another window");
        Check(!buffer.Events.ToArray()[3].DispatchToControls, "Off-game release dispatched to HUD");
        Check(!Add(buffer, MouseEventType.LeftMouseButtonPressed, block: false), "DoNotBlock down swallowed");
        Check(!Add(buffer, MouseEventType.LeftMouseButtonReleased), "DoNotBlock up swallowed");
    }
    private static void ResumedMouse() {
        var buffer = new MouseInputBuffer(new MouseState(0, 0, 0, ButtonState.Pressed, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released));
        Check(!Add(buffer, MouseEventType.LeftMouseButtonReleased), "Pre-held release was consumed");
        Check(buffer.Events.TryDequeue(out var release) && !release.DispatchToControls, "Synthetic click on resume");
        Check(Add(buffer, MouseEventType.LeftMouseButtonPressed), "First real click after resume lost");
        Check(Add(buffer, MouseEventType.LeftMouseButtonReleased), "First real release after resume lost");
    }
    private static void CameraDrag() {
        var buffer = new MouseInputBuffer(default);
        Check(!Add(buffer, MouseEventType.RightMouseButtonPressed, hud: false), "Camera down swallowed");
        Check(buffer.CameraDragging, "Camera gesture not tracked");
        Check(!Add(buffer, MouseEventType.MouseWheelScrolled, wheel: 120), "HUD stole wheel during camera drag");
        Check(!Add(buffer, MouseEventType.LeftMouseButtonPressed), "HUD stole left button during camera drag");
        Check(!Add(buffer, MouseEventType.RightMouseButtonReleased), "Camera up swallowed");
        Check(!buffer.CameraDragging, "Camera stuck after release");
        Check(Add(buffer, MouseEventType.RightMouseButtonPressed), "HUD right click did not capture");
        Check(!buffer.CameraDragging, "HUD right click started camera drag");
    }
    private static void SessionIsolation() {
        var old = new InputSession(new IntPtr(42), default, new byte[256]);
        Add(old.Mouse, MouseEventType.LeftMouseButtonPressed);
        var resumed = new InputSession(new IntPtr(42), default, new byte[256]);
        Add(old.Mouse, MouseEventType.LeftMouseButtonReleased);
        Add(old.Keyboard, Keys.A);
        Check(resumed.Mouse.Events.IsEmpty && resumed.Keyboard.Events.IsEmpty, "Stale events crossed visits");
        Check(!resumed.Mouse.CameraDragging, "Stale camera drag crossed visits");
    }
    private static void ShortcutModifiers() {
        var buffer = new KeyboardInputBuffer(new byte[256]);
        var ctrlA = new[] { new KeyCapture(Keys.A, ModifierKeys.Ctrl) };
        Add(buffer, Keys.LeftControl);
        Add(buffer, Keys.LeftShift);
        Check(!Add(buffer, Keys.A, bindings: ctrlA), "Extra modifier incorrectly matched Ctrl+A");
        Add(buffer, Keys.A, down: false);
        Add(buffer, Keys.LeftShift, down: false);
        Check(Add(buffer, Keys.A, bindings: ctrlA), "Exact Ctrl+A was not consumed");
        Add(buffer, Keys.LeftControl, down: false);
        Check(Add(buffer, Keys.A, down: false), "Release ownership depended on current modifier");
        Check(buffer.Events.Count == 8, "Fast keyboard events were overwritten");
    }
    private static void KeyboardOwnership() {
        var buffer = new KeyboardInputBuffer(new byte[256]);
        var a = new[] { new KeyCapture(Keys.A, ModifierKeys.None) };
        Check(!Add(buffer, Keys.A), "Unowned down blocked");
        Check(!Add(buffer, Keys.A, bindings: a), "Repeat started capturing midway");
        Check(!Add(buffer, Keys.A, down: false, bindings: a), "Unowned up blocked");
        Check(Add(buffer, Keys.A, bindings: a), "Owned down passed");
        Check(Add(buffer, Keys.A, allow: false), "Owned repeat escaped after UI change");
        Check(Add(buffer, Keys.A, down: false, allow: false), "Owned up escaped after UI change");
        Check(Add(buffer, Keys.Escape, escape: true), "HUD close Escape not consumed");
    }
    private static void SystemKeys() {
        var keys = new byte[256]; keys[(int)Keys.A] = 0x80;
        var buffer = new KeyboardInputBuffer(keys);
        Action<string> target = _ => { };
        Check(!Add(buffer, Keys.A, text: target), "Pre-held key repeat consumed on return");
        Check(!Add(buffer, Keys.A, down: false, text: target), "Pre-held key up consumed on return");
        Check(!Add(buffer, Keys.LeftAlt, text: target), "Alt consumed");
        Check(!Add(buffer, Keys.Tab, text: target), "Alt+Tab consumed");
        Add(buffer, Keys.Tab, down: false); Add(buffer, Keys.LeftAlt, down: false);
        Check(!Add(buffer, Keys.LeftWindows, text: target), "Windows key consumed");
        Check(!Add(buffer, Keys.D, text: target), "Win+D consumed");
    }
    private static void DeferredText() {
        var buffer = new KeyboardInputBuffer(new byte[256]);
        int calls = 0;
        Action<string> target = _ => calls++;
        Add(buffer, Keys.LeftShift);
        Check(Add(buffer, Keys.A, text: target), "Text input not captured");
        Add(buffer, Keys.LeftShift, down: false);
        Check(calls == 0, "Hook called text delegate");
        var pending = buffer.Events.ToArray()[1];
        Check(pending.TextTarget == target && (pending.TextKeyState[0x10] & 0x80) != 0,
            "Deferred text lost event-time Shift state");
    }
    private sealed class FakeHooks : IMouseHookManager, IKeyboardHookManager {
        public bool Result = true;
        public bool Throw;
        public bool Installed;
        public int EnableCount, InstallThread, RemoveThread;
        public bool EnableHook() {
            EnableCount++; InstallThread = Thread.CurrentThread.ManagedThreadId;
            if (Throw) throw new InvalidOperationException("test install failure");
            return Installed = Result;
        }
        public void DisableHook() { Installed = false; RemoveThread = Thread.CurrentThread.ManagedThreadId; }
        public void RegisterHandler(HandleMouseInputDelegate callback) { }
        public void UnregisterHandler(HandleMouseInputDelegate callback) { }
        public void RegisterHandler(HandleKeyboardInputDelegate callback) { }
        public void UnregisterHandler(HandleKeyboardInputDelegate callback) { }
    }
    private static void HookFailures() {
        var mouse = new FakeHooks(); var keyboard = new FakeHooks { Result = false };
        using var manager = new WinApiHookManager(mouse, keyboard);
        Check(!manager.EnableHook(), "Partial failure reported success");
        Check(!mouse.Installed && !keyboard.Installed, "Partial hook installation leaked");
        keyboard.Result = true;
        Check(manager.EnableHook(), "Successful retry did not install");
        manager.DisableHook();
        keyboard.Result = false;
        Check(!manager.EnableHook(), "Old success leaked into a later failed attempt");
        keyboard.Throw = true;
        Check(!manager.EnableHook() && !mouse.Installed, "Throwing installer leaked/deadlocked");
    }
    private static void HookLifecycle() {
        var mouse = new FakeHooks(); var keyboard = new FakeHooks();
        using var manager = new WinApiHookManager(mouse, keyboard);
        Check(manager.EnableHook() && manager.EnableHook(), "Repeated enable changed success");
        Check(mouse.EnableCount == 1 && keyboard.EnableCount == 1, "Repeated enable reordered the hook chain");
        manager.DisableHook();
        Check(!mouse.Installed && !keyboard.Installed, "Disable leaked hooks");
        Check(mouse.InstallThread == mouse.RemoveThread && keyboard.InstallThread == keyboard.RemoveThread,
            "Hooks removed from a different thread");
        manager.DisableHook();
    }
    private static void NativeHooks() {
        using var manager = new WinApiHookManager();
        Check(manager.EnableHook() && manager.EnableHook(), "Native hook install failed");
        manager.DisableHook();
        Check(manager.EnableHook(), "Native hook reinstall failed");
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    private static bool Topmost(IntPtr handle) => (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0;
    private static void Place(IntPtr handle, IntPtr after) => Check(SetWindowPos(handle, after, 0, 0, 0, 0, 0x13), "Native positioning failed");
    private static bool Above(IntPtr upper, IntPtr lower) {
        for (var window = GetWindow(lower, 3); window != IntPtr.Zero; window = GetWindow(window, 3))
            if (window == upper) return true;
        return false;
    }
    private sealed class TestForm : Form {
        protected override bool ShowWithoutActivation => true;
        public TestForm(System.Drawing.Rectangle bounds) {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Bounds = bounds;
        }
    }
    private static void NativeWindows() {
        var foreground = WindowUtil.GetForegroundWindow();
        var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        var bounds = new System.Drawing.Rectangle(area.Right - 220, area.Bottom - 140, 180, 100);
        using var game = new TestForm(bounds);
        using var hud = new TestForm(bounds);
        using var other = new TestForm(bounds);
        game.Show(); hud.Show();
        var gameHandle = game.Handle; var hudHandle = hud.Handle;
        WindowUtil.SetupOverlay(hudHandle);
        Place(gameHandle, IntPtr.Zero);
        WindowUtil.PlaceAboveGame(hudHandle, gameHandle);
        Check(Above(hudHandle, gameHandle) && !Topmost(hudHandle), "Ordinary HUD placement failed");
        var point = new Point(bounds.X + 50, bounds.Y + 50);
        Check(WindowUtil.IsPointerOverGame(gameHandle, point), "Transparent HUD intercepted native hit test");

        other.Show(); Place(other.Handle, IntPtr.Zero);
        WindowUtil.PlaceAboveGame(hudHandle, gameHandle);
        Check(Above(other.Handle, hudHandle) && Above(hudHandle, gameHandle), "HUD jumped over another overlay");
        Check(!WindowUtil.IsPointerOverGame(gameHandle, point), "Foreign overlay input was treated as game input");
        other.Hide();

        Place(gameHandle, new IntPtr(-1));
        WindowUtil.PlaceAboveGame(hudHandle, gameHandle);
        Check(Topmost(hudHandle) && Above(hudHandle, gameHandle), "Topmost game did not get an adjacent HUD");
        Place(other.Handle, new IntPtr(-1));
        Place(gameHandle, new IntPtr(-2));
        WindowUtil.PlaceAboveGame(hudHandle, gameHandle);
        Check(!Topmost(hudHandle), "HUD remained globally topmost after game returned to ordinary group");

        var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var active = new WindowUtil.OverlayWindowState(gameHandle, gameHandle, rect, WindowUtil.OverlayWindowKind.Game);
        var background = new WindowUtil.OverlayWindowState(gameHandle, foreground, rect, WindowUtil.OverlayWindowKind.Background);
        for (int i = 0; i < 5; i++) {
            WindowUtil.UpdateOverlay(hudHandle, background);
            Check(!WindowUtil.IsWindowVisible(hudHandle), "HUD visible over external app");
            WindowUtil.UpdateOverlay(hudHandle, active);
            Check(WindowUtil.IsWindowVisible(hudHandle), "HUD did not restore on return");
        }
        Check(GameService.Graphics.Resolution == rect.Size, "Client bounds did not update resolution");
        Check(WindowUtil.GetForegroundWindow() == foreground, "Overlay operation stole foreground focus");
        WindowUtil.UpdateOverlay(hudHandle, new WindowUtil.OverlayWindowState(gameHandle, foreground, rect, WindowUtil.OverlayWindowKind.Minimized));
        Check(!WindowUtil.IsWindowVisible(hudHandle), "Minimized game kept HUD visible");
        Check(WindowUtil.ReadOverlayState(hudHandle, IntPtr.Zero).Kind == WindowUtil.OverlayWindowKind.Unavailable, "Missing game not classified");
        Check(WindowUtil.ReadOverlayState(hudHandle, gameHandle).Kind == WindowUtil.OverlayWindowKind.Background, "External foreground not classified as background");
        Console.WriteLine("Native/managed HUD visibility after hide: " + WindowUtil.IsWindowVisible(hudHandle) + "/" + hud.Visible);
    }
}
