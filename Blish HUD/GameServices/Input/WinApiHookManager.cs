using System.Threading;
using System;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Blish_HUD.Input {

    internal class WinApiHookManager : IHookManager {

        private static readonly Logger Logger = Logger.GetLogger<WinApiHookManager>();

        private readonly IMouseHookManager    mouseHookManager;
        private readonly IKeyboardHookManager keyboardHookManager;
        private readonly AutoResetEvent       inputHookEvent = new AutoResetEvent(false);
        private volatile bool                 stopRequested;
        private          Thread               thread;
        private          bool                 inputSuccessful = false;

        public WinApiHookManager() : this(new WinApiMouseHookManager(), new WinApiKeyboardHookManager()) { }

        internal WinApiHookManager(IMouseHookManager mouse, IKeyboardHookManager keyboard) {
            mouseHookManager = mouse;
            keyboardHookManager = keyboard;
        }

        public void Load() { /* NOOP */ }

        public void Unload() => DisableHook();

        public bool EnableHook() {
            if (thread != null) return inputSuccessful;

            Logger.Debug("Enabling WinAPI input hooks");

            stopRequested = false;
            inputSuccessful = false;
            thread = new Thread(Loop) { IsBackground = true, Name = "Blish HUD input hooks" };
            thread.Start();

            // Wait for the hook to be completed and return the status
            inputHookEvent.WaitOne();
            if (!inputSuccessful) {
                thread.Join();
                thread = null;
            }
            return inputSuccessful;
        }

        public void DisableHook() {
            if ((thread == null) || stopRequested) return;

            Logger.Debug("Disabling WinAPI input hooks");

            stopRequested = true;
            thread.Join();

            stopRequested = false;
            inputSuccessful = false;
            thread        = null;
        }

        public void RegisterMouseHandler(HandleMouseInputDelegate handleMouseInputCallback) { mouseHookManager.RegisterHandler(handleMouseInputCallback); }

        public void UnregisterMouseHandler(HandleMouseInputDelegate handleMouseInputCallback) { mouseHookManager.UnregisterHandler(handleMouseInputCallback); }

        public void RegisterKeyboardHandler(HandleKeyboardInputDelegate handleKeyboardInputCallback) { keyboardHookManager.RegisterHandler(handleKeyboardInputCallback); }

        public void UnregisterKeyboardHandler(HandleKeyboardInputDelegate handleKeyboardInputCallback) { keyboardHookManager.UnregisterHandler(handleKeyboardInputCallback); }

        private void Loop() {
            using Timer timer = new Timer {
                Interval = 10
            };

            timer.Tick += (sender, e) => {
                if (stopRequested) {
                    Logger.Debug("Stopping message loop");
                    Application.ExitThread();
                }
            };

            bool signaled = false;
            try {
                inputSuccessful = mouseHookManager.EnableHook() && keyboardHookManager.EnableHook();
                if (!inputSuccessful) return;
                inputHookEvent.Set();
                signaled = true;
                timer.Start();
                Application.Run();
            } catch (Exception ex) {
                inputSuccessful = false;
                Logger.Error(ex, "Input hook thread failed.");
            } finally {
                // Includes partial installation failure; release on the owning thread.
                timer.Stop();
                keyboardHookManager.DisableHook();
                mouseHookManager.DisableHook();
                if (!signaled) inputHookEvent.Set();
            }
        }

        #region IDisposable Support

        private bool isDisposed = false;

        protected virtual void Dispose(bool isDisposing) {
            if (!isDisposed) {
                if (isDisposing) {
                    Unload();
                    inputHookEvent.Dispose();
                }

                isDisposed = true;
            }
        }

        public void Dispose() { Dispose(true); }

        #endregion

    }

}
