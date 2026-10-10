using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

// Only service dependencies are replaced. Window operations, hooks and event
// buffering compile directly from production sources and run on Windows.
namespace Blish_HUD {
    internal sealed class Logger {
        public static Logger GetLogger<T>() => new Logger();
        public static Logger GetLogger(Type type) => new Logger();
        public void Debug(string message, params object[] args) { }
        public void Warn(string message, params object[] args) { }
        public void Error(Exception error, string message) { }
    }
    internal static class GameService {
        public static GraphicsStub Graphics { get; } = new GraphicsStub();
        public static InputStub Input { get; } = new InputStub();
    }
    internal sealed class GraphicsStub { public Point Resolution { get; set; } }
    internal sealed class InputStub { public MouseStub Mouse { get; } = new MouseStub(); }
    internal sealed class MouseStub {
        public MouseState State { get; set; }
        public Point Position => State.Position;
    }
}
