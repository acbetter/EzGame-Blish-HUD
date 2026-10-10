using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Blish_HUD.Controls;
using Blish_HUD.Input.WinApi;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Blish_HUD.Input {
    public class MouseHandler : IInputHandler {
        public Point Position => State.Position;
        public Point PositionRaw { get; private set; }
        public MouseState State { get; private set; }
        public bool CameraDragging => GameService.Input.GetSession()?.Mouse.CameraDragging ?? false;
        public Control ActiveControl { get; private set; }
        public bool CursorIsVisible { get; set; } = true;
        private CaptureMap _captureMap = CaptureMap.Empty;

        private readonly struct Region {
            public Rectangle Bounds { get; }
            public CaptureType Captures { get; }
            public Region(Rectangle bounds, CaptureType captures) { Bounds = bounds; Captures = captures; }
        }
        private sealed class CaptureMap {
            public static readonly CaptureMap Empty = new CaptureMap(Point.Zero, 1, Array.Empty<Region>(), false);
            public Point Origin { get; }
            public float Scale { get; }
            public Region[] Regions { get; }
            public bool Enabled { get; }
            public CaptureMap(Point origin, float scale, Region[] regions, bool enabled) {
                Origin = origin; Scale = scale; Regions = regions; Enabled = enabled;
            }
            public Point ToUi(Point screen) => new Point((int)((screen.X - Origin.X) / Scale), (int)((screen.Y - Origin.Y) / Scale));
            public CaptureType Hit(Point point, CaptureType requested) {
                var observed = CaptureType.None;
                foreach (var region in Regions) {
                    if (!region.Bounds.Contains(point)) continue;
                    if ((region.Captures & CaptureType.Filter) != 0) { observed = CaptureType.Filter; continue; }
                    if ((region.Captures & requested) != 0) return region.Captures;
                }
                return observed;
            }
        }

        internal MouseHandler() { }
        internal static MouseState ReadPhysicalState() {
            var point = WindowUtil.GetMousePosition();
            ButtonState Button(int key) => WindowUtil.IsKeyDown(key) ? ButtonState.Pressed : ButtonState.Released;
            return new MouseState(point.X, point.Y, 0, Button(1), Button(4), Button(2), Button(5), Button(6));
        }

        public bool HandleInput(MouseEventArgs input) {
            Interlocked.Increment(ref GameService.Input.MouseCallbacks);
            var session = GameService.Input.GetSession();
            if (session == null || input.EventType == MouseEventType.MouseMoved) return false;
            var map = Volatile.Read(ref _captureMap);
            if (!map.Enabled) return false;
            var screen = new Point(input.PointX, input.PointY);
            var point = map.ToUi(screen);
            bool overGame = WindowUtil.IsPointerOverGame(session.GameWindow, screen);
            if (!overGame && !session.Mouse.IsReleaseOfHeldButton(input.EventType)) return false;
            bool pointerAvailable = overGame && CursorExtern.GetCursorInfo().Flags != CursorFlags.CursorHiding;
            var capture = pointerAvailable ? map.Hit(point,
                input.EventType == MouseEventType.MouseWheelScrolled ? CaptureType.MouseWheel : CaptureType.Mouse) : CaptureType.None;
            bool filter = (capture & CaptureType.Filter) != 0;
            // Filters observe left clicks without consuming them; a right click
            // through a filter still belongs to the game's camera.
            bool overHud = capture != CaptureType.None && !(filter && input.EventType == MouseEventType.RightMouseButtonPressed);
            return session.Mouse.Add(input, point, overHud,
                capture != CaptureType.None && !filter && !capture.HasFlag(CaptureType.DoNotBlock), pointerAvailable);
        }

        // Snapshot only geometry and flags. Custom CapturesInput implementations and
        // all control callbacks stay on the main thread.
        private static void CollectRegions(Control control, Rectangle clip, List<Region> regions) {
            if (!control.Visible) return;
            var bounds = Rectangle.Intersect(clip, control.AbsoluteBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (control is Container container) {
                var children = container.Children.ToArray();
                foreach (var child in children.Select((value, index) => new { value, index })
                    .OrderByDescending(item => item.value.ZIndex).ThenByDescending(item => item.index))
                    CollectRegions(child.value, bounds, regions);
            }
            var captures = control.Captures;
            if (captures != CaptureType.None)
                regions.Add(new Region(bounds, captures));
        }

        private void RefreshCaptureMap() {
            var regions = new List<Region>();
            bool enabled = !GameService.Overlay.InterfaceHidden;
            if (enabled) {
                var screen = GameService.Graphics.SpriteScreen;
                CollectRegions(screen, screen.AbsoluteBounds, regions);
            }
            var origin = GameService.GameIntegration.Gw2Instance.WindowState.Bounds.Location;
            Volatile.Write(ref _captureMap, new CaptureMap(origin, GameService.Graphics.UIScaleMultiplier, regions.ToArray(), enabled));
        }

        private void SetActiveControl(Control control) {
            ActiveControl = control;
            Control.ActiveControl = control;
        }
        private void ClearHover() {
            foreach (var control in GameService.Graphics.SpriteScreen.GetDescendants()) {
                if (control.MouseOver) control.ClearMouseOver();
            }
            SetActiveControl(null);
        }
        private void UpdateHover(bool available) {
            if (!available) { ClearHover(); return; }
            foreach (var control in GameService.Graphics.SpriteScreen.GetDescendants()) {
                if (control.MouseOver && !control.AbsoluteBounds.Contains(Position)) control.ClearMouseOver();
            }
            SetActiveControl(GameService.Graphics.SpriteScreen.TriggerMouseInput(MouseEventType.MouseMoved, State));
        }

        public void Update() {
            var session = GameService.Input.GetSession();
            if (session == null) return;
            RefreshCaptureMap();
            while (session.Mouse.Events.TryDequeue(out var pending)) {
                if (GameService.Input.GetSession() != session) return;
                State = pending.State;
                PositionRaw = new Point(pending.Event.PointX, pending.Event.PointY);
                UpdateHover(pending.DispatchToControls);
                RaiseMouseEvent(pending.Event);
                if (pending.DispatchToControls)
                    GameService.Graphics.SpriteScreen.TriggerMouseInput(pending.Event.EventType, State);
            }
            var physical = ReadPhysicalState();
            PositionRaw = physical.Position;
            var point = _captureMap.ToUi(PositionRaw);
            bool moved = point != State.Position;
            // Consumed button events may never reach GetAsyncKeyState. Preserve the
            // ordered hook state until its matching release; sample position separately.
            State = new MouseState(point.X, point.Y, 0, State.LeftButton, physical.MiddleButton,
                State.RightButton, physical.XButton1, physical.XButton2);
            CursorIsVisible = CursorExtern.GetCursorInfo().Flags != CursorFlags.CursorHiding;
            UpdateHover(_captureMap.Enabled && CursorIsVisible && !session.Mouse.CameraDragging
                && WindowUtil.IsPointerOverGame(session.GameWindow, PositionRaw));
            if (moved) MouseMoved?.Invoke(this, new MouseEventArgs(MouseEventType.MouseMoved));
            RefreshCaptureMap();
        }

        private void RaiseMouseEvent(MouseEventArgs input) {
            switch (input.EventType) {
                case MouseEventType.LeftMouseButtonPressed: LeftMouseButtonPressed?.Invoke(this, input); break;
                case MouseEventType.LeftMouseButtonReleased: LeftMouseButtonReleased?.Invoke(this, input); break;
                case MouseEventType.RightMouseButtonPressed: RightMouseButtonPressed?.Invoke(this, input); break;
                case MouseEventType.RightMouseButtonReleased: RightMouseButtonReleased?.Invoke(this, input); break;
                case MouseEventType.MouseWheelScrolled: MouseWheelScrolled?.Invoke(this, input); break;
            }
        }

        public void RecalculateActiveControl() {
            if (GameService.Input.GetSession() == null) return;
            UpdateHover(_captureMap.Enabled && CursorIsVisible && !CameraDragging
                && WindowUtil.IsPointerOverGame(GameService.Input.Session.GameWindow, PositionRaw));
            RefreshCaptureMap();
        }
        public void OnEnable() {
            RefreshCaptureMap();
            var physical = ReadPhysicalState();
            PositionRaw = physical.Position;
            var point = _captureMap.ToUi(PositionRaw);
            State = new MouseState(point.X, point.Y, 0, physical.LeftButton, physical.MiddleButton,
                physical.RightButton, physical.XButton1, physical.XButton2);
            CursorIsVisible = CursorExtern.GetCursorInfo().Flags != CursorFlags.CursorHiding;
            UpdateHover(_captureMap.Enabled && CursorIsVisible && !GameService.Input.Session.Mouse.CameraDragging
                && WindowUtil.IsPointerOverGame(GameService.Input.Session.GameWindow, PositionRaw));
        }
        public void OnDisable() {
            Volatile.Write(ref _captureMap, CaptureMap.Empty);
            foreach (var control in GameService.Graphics.SpriteScreen.GetDescendants()) control.CancelInput();
            SetActiveControl(null);
            State = new MouseState(State.X, State.Y, 0, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released);
            InputCancelled?.Invoke(this, EventArgs.Empty);
        }
        public void UnsetActiveControl() => SetActiveControl(null);

        public event EventHandler InputCancelled;
        public event EventHandler<MouseEventArgs> MouseMoved;
        public event EventHandler<MouseEventArgs> LeftMouseButtonPressed;
        public event EventHandler<MouseEventArgs> LeftMouseButtonReleased;
        public event EventHandler<MouseEventArgs> RightMouseButtonPressed;
        public event EventHandler<MouseEventArgs> RightMouseButtonReleased;
        public event EventHandler<MouseEventArgs> MouseWheelScrolled;
    }
}
