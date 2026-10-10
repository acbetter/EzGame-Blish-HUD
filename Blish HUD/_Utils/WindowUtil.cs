using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Xna.Framework;

namespace Blish_HUD {

    internal static class WindowUtil {
        private static readonly Logger Logger = Logger.GetLogger(typeof(WindowUtil));

        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint GW_HWNDPREV = 3;
        private const uint GA_ROOT = 2;
        private const int ZORDER_CHECK_INTERVAL_MS = 1000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private static readonly Stopwatch ZOrderCheckTimer = Stopwatch.StartNew();
        private static Rectangle _bounds;
        private static IntPtr _gameWindow;
        private static OverlayWindowKind _lastKind;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point point);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point point);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLong64(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int index, uint value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLong64(IntPtr hWnd, int index, UIntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins margins);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }

        internal enum OverlayWindowKind { Unavailable, Background, Minimized, Game, NativeTool }

        internal readonly struct OverlayWindowState {
            public IntPtr GameWindow { get; }
            public IntPtr ForegroundWindow { get; }
            public Rectangle Bounds { get; }
            public OverlayWindowKind Kind { get; }
            public int ErrorCode { get; }
            public bool Visible => Kind == OverlayWindowKind.Game || Kind == OverlayWindowKind.NativeTool;
            public bool HasGameFocus => Kind == OverlayWindowKind.Game;

            public OverlayWindowState(IntPtr gameWindow, IntPtr foregroundWindow, Rectangle bounds, OverlayWindowKind kind, int errorCode = 0) {
                GameWindow = gameWindow;
                ForegroundWindow = foregroundWindow;
                Bounds = bounds;
                Kind = kind;
                ErrorCode = errorCode;
            }
        }

        internal static void SetForegroundWindowEx(IntPtr handle) => SetForegroundWindow(handle);
        internal static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        internal static uint GetWindowProcessId(IntPtr handle) {
            GetWindowThreadProcessId(handle, out var processId);
            return processId;
        }

        internal static Point GetMousePosition() {
            GetCursorPos(out var point);
            return new Point(point.X, point.Y);
        }

        // Layered, transparent HUD windows are skipped by native hit testing. A different
        // native overlay at this point owns the input even when it doesn't activate itself.
        internal static bool IsPointerOverGame(IntPtr gameWindow, Point point) {
            var target = WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
            return target != IntPtr.Zero && GetAncestor(target, GA_ROOT) == gameWindow;
        }

        private static uint GetExtendedStyle(IntPtr handle) => unchecked((uint)(IntPtr.Size == 8
            ? GetWindowLong64(handle, GWL_EXSTYLE).ToInt64()
            : GetWindowLong32(handle, GWL_EXSTYLE).ToInt64()));

        internal static void SetShowInTaskbar(IntPtr handle, bool showInTaskbar) {
            bool visible = IsWindowVisible(handle);
            if (visible) ShowWindow(handle, SW_HIDE);
            uint style = GetExtendedStyle(handle) & ~(WS_EX_APPWINDOW | WS_EX_TOOLWINDOW);
            style |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            style |= showInTaskbar ? WS_EX_APPWINDOW : WS_EX_TOOLWINDOW;
            if (IntPtr.Size == 8) SetWindowLong64(handle, GWL_EXSTYLE, new UIntPtr(style));
            else SetWindowLong32(handle, GWL_EXSTYLE, style);
            if (visible) ShowWindow(handle, SW_SHOWNOACTIVATE);
        }

        internal static void SetupOverlay(IntPtr handle) {
            // MonoGame owns the normal/borderless window styles. CS_* are class styles
            // and must not be written into GWL_STYLE.
            SetShowInTaskbar(handle, false);
            SetLayeredWindowAttributes(handle, 0, 255, 2);
        }

        internal static OverlayWindowState ReadOverlayState(IntPtr overlay, IntPtr game) {
            var foreground = GetForegroundWindow();
            if (game == IntPtr.Zero || !IsWindow(game))
                return new OverlayWindowState(game, foreground, Rectangle.Empty, OverlayWindowKind.Unavailable, 1400);
            if (IsIconic(game))
                return new OverlayWindowState(game, foreground, Rectangle.Empty, OverlayWindowKind.Minimized);
            if (!GetClientRect(game, out var client))
                return new OverlayWindowState(game, foreground, Rectangle.Empty, OverlayWindowKind.Unavailable, Marshal.GetLastWin32Error());
            var origin = System.Drawing.Point.Empty;
            if (!ClientToScreen(game, ref origin))
                return new OverlayWindowState(game, foreground, Rectangle.Empty, OverlayWindowKind.Unavailable, Marshal.GetLastWin32Error());
            var bounds = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
            var kind = foreground == game ? OverlayWindowKind.Game : OverlayWindowKind.Background;
            if (foreground != game && foreground != overlay && Form.FromHandle(foreground) is Form)
                kind = OverlayWindowKind.NativeTool;
            if (bounds.Width <= 0 || bounds.Height <= 0) kind = OverlayWindowKind.Minimized;
            return new OverlayWindowState(game, foreground, bounds, kind);
        }

        internal static void HideOverlay(IntPtr overlay) {
            if (IsWindowVisible(overlay)) ShowWindow(overlay, SW_HIDE);
        }

        internal static void UpdateOverlay(IntPtr overlay, OverlayWindowState state) {
            if (!state.Visible) {
                HideOverlay(overlay);
                _lastKind = state.Kind;
                return;
            }

            bool wasVisible = IsWindowVisible(overlay);
            bool newGame = _gameWindow != state.GameWindow;
            if (newGame || _bounds != state.Bounds || !wasVisible) {
                var bounds = state.Bounds;
                GameService.Graphics.Resolution = bounds.Size;
                if (!SetWindowPos(overlay, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE)) {
                    Logger.Warn("Failed to position HUD: {error}", Marshal.GetLastWin32Error());
                    HideOverlay(overlay);
                    return;
                }
                var margins = new Margins { Right = bounds.Width, Bottom = bounds.Height };
                DwmExtendFrameIntoClientArea(overlay, ref margins);
                _bounds = bounds;
                _gameWindow = state.GameWindow;
            }

            if (newGame || !wasVisible || _lastKind != state.Kind || HasLostZOrder(overlay, state.GameWindow)) {
                PlaceAboveGame(overlay, state.GameWindow);
            }
            if (!wasVisible) ShowWindow(overlay, SW_SHOWNOACTIVATE);
            _lastKind = state.Kind;
        }

        private static bool HasLostZOrder(IntPtr overlay, IntPtr game) {
            if (ZOrderCheckTimer.ElapsedMilliseconds < ZORDER_CHECK_INTERVAL_MS) return false;
            ZOrderCheckTimer.Restart();
            for (var window = GetWindow(game, GW_HWNDPREV); window != IntPtr.Zero; window = GetWindow(window, GW_HWNDPREV)) {
                if (window == overlay) return false;
            }
            return true;
        }

        internal static void PlaceAboveGame(IntPtr overlay, IntPtr game) {
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
            bool gameTopmost = (GetExtendedStyle(game) & WS_EX_TOPMOST) != 0;
            if (!gameTopmost && (GetExtendedStyle(overlay) & WS_EX_TOPMOST) != 0)
                SetWindowPos(overlay, HWND_NOTOPMOST, 0, 0, 0, 0, flags);

            var previous = GetWindow(game, GW_HWNDPREV);
            if (previous == overlay) return;
            // Insert next to the game, behind any overlays already above it. Only a
            // topmost game with no predecessor calls for HWND_TOPMOST.
            var after = previous == IntPtr.Zero ? (gameTopmost ? HWND_TOPMOST : IntPtr.Zero) : previous;
            if (!gameTopmost && previous != IntPtr.Zero && (GetExtendedStyle(previous) & WS_EX_TOPMOST) != 0)
                after = IntPtr.Zero; // Top of the ordinary group, without joining the topmost group.
            if (!SetWindowPos(overlay, after, 0, 0, 0, 0, flags))
                Logger.Warn("Failed to restore HUD z-order: {error}", Marshal.GetLastWin32Error());
        }

        internal static string GetClassNameOfWindow(IntPtr handle) {
            var name = new StringBuilder(256);
            GetClassName(handle, name, name.Capacity);
            return name.ToString();
        }
    }
}
