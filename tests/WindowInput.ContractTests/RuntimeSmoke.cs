using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Runs the actual release executable against a native game stand-in, with isolated
// settings. This checks service initialization and foreground transitions together.
internal static class RuntimeSmoke {
    private delegate bool EnumWindow(IntPtr handle, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    internal static int Run(string[] args) {
        if (args.Length < 3) throw new ArgumentException("--smoke <release exe> <isolated settings root> [--debug]");
        var originalForeground = GetForegroundWindow();
        var bounds = new System.Drawing.Rectangle(80, 80, 800, 600);
        using var game = new Form { Text = "EzGame input smoke: game stand-in", StartPosition = FormStartPosition.Manual, Bounds = bounds };
        using var browser = new Form { Text = "EzGame input smoke: external app stand-in", StartPosition = FormStartPosition.Manual, Bounds = bounds };
        using var timer = new Timer { Interval = 100 };
        var clock = Stopwatch.StartNew();
        Process hud = null;
        IntPtr hudWindow = IntPtr.Zero;
        int phase = 0;
        long phaseStarted = 0;
        Exception failure = null;
        bool passed = false;
        try {
            Directory.CreateDirectory(args[2]);
            game.Show();
            SetForegroundWindow(game.Handle);
            using var current = Process.GetCurrentProcess();
            hud = Process.Start(new ProcessStartInfo(args[1],
                $"--pid {current.Id} --settings \"{Path.Combine(args[2], "settings")}\" --progdata \"{Path.Combine(args[2], "programdata")}\" --maxfps 30" + (args.Length > 3 ? " --debug" : "")) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            timer.Tick += (sender, e) => {
                try {
                    if (clock.Elapsed.TotalSeconds > 45) throw new Exception("Runtime smoke timed out in phase " + phase);
                    if (hud.HasExited) throw new Exception("Release process exited early: " + hud.ExitCode);
                    if (hudWindow == IntPtr.Zero) {
                        EnumWindows((handle, _) => {
                            GetWindowThreadProcessId(handle, out var pid);
                            if (pid == hud.Id && (GetStyle(handle, -20).ToInt64() & 0x80020) == 0x80020) hudWindow = handle;
                            return true;
                        }, IntPtr.Zero);
                        if (hudWindow == IntPtr.Zero) return;
                    }
                    var elapsed = clock.ElapsedMilliseconds - phaseStarted;
                    switch (phase) {
                        case 0:
                            if (!IsWindowVisible(hudWindow)) return;
                            Require(GetForegroundWindow() == game.Handle, "HUD stole game foreground at startup");
                            Console.WriteLine("PASS release attached and displayed without activation");
                            browser.Show(); SetForegroundWindow(browser.Handle);
                            phase = 1; phaseStarted = clock.ElapsedMilliseconds;
                            break;
                        case 1:
                            if (elapsed < 1000) return;
                            Require(GetForegroundWindow() == browser.Handle, "External app did not acquire foreground");
                            Require(!IsWindowVisible(hudWindow), "Release HUD remained above external app");
                            Console.WriteLine("PASS release hid over external app");
                            browser.Hide(); SetForegroundWindow(game.Handle);
                            phase = 2; phaseStarted = clock.ElapsedMilliseconds;
                            break;
                        case 2:
                            if (elapsed < 1000) return;
                            Require(GetForegroundWindow() == game.Handle && IsWindowVisible(hudWindow), "Release failed to restore nonactivating HUD");
                            Console.WriteLine("PASS release restored after return to game stand-in");
                            game.WindowState = FormWindowState.Minimized;
                            phase = 3; phaseStarted = clock.ElapsedMilliseconds;
                            break;
                        case 3:
                            if (elapsed < 1000) return;
                            Require(!IsWindowVisible(hudWindow), "Minimized game kept HUD visible");
                            game.WindowState = FormWindowState.Normal; SetForegroundWindow(game.Handle);
                            phase = 4; phaseStarted = clock.ElapsedMilliseconds;
                            break;
                        case 4:
                            if (elapsed < 1000) return;
                            Require(IsWindowVisible(hudWindow) && GetForegroundWindow() == game.Handle, "Restore from minimize failed");
                            Console.WriteLine("PASS release hid/restored with minimize");
                            passed = true; timer.Stop(); Application.ExitThread();
                            break;
                    }
                } catch (Exception ex) { failure = ex; timer.Stop(); Application.ExitThread(); }
            };
            timer.Start(); Application.Run();
        } catch (Exception ex) { failure = ex; }
        finally {
            timer.Stop();
            if (hud != null && !hud.HasExited) {
                if (hudWindow != IntPtr.Zero) PostMessage(hudWindow, 0x10, IntPtr.Zero, IntPtr.Zero);
                if (!hud.WaitForExit(5000)) hud.Kill();
            }
            hud?.Dispose();
            game.Hide(); browser.Hide();
            SetForegroundWindow(originalForeground);
        }
        if (failure != null) Console.Error.WriteLine(failure);
        return passed && failure == null ? 0 : 1;
    }
    private static void Require(bool success, string message) { if (!success) throw new Exception(message); }
}
