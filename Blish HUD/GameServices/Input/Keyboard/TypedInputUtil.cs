using System.Text;
using System.Runtime.InteropServices;

namespace Blish_HUD.Input {
    internal static class TypedInputUtil {

        // Note: Sometimes single VKCode represents multiple chars, thus string. 
        // E.g. typing "^1" (notice that when pressing 1 the both characters appear, 
        // because of this behavior, "^" is called dead key)

        [DllImport("user32.dll")]
        private static extern int ToUnicode(uint vkCode, uint scanCode, byte[] keyState, [Out, MarshalAs(UnmanagedType.LPWStr, SizeConst = 64)] System.Text.StringBuilder receivingBuffer, int bufferSize, uint flags);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint vkCode, uint mapType);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int keyState);

        private static uint _lastVirtKeyCode;
        private static uint _lastScanCode;
        private static byte[] _lastKeyState = new byte[256];
        private static bool _lastIsDead;

        /// <summary>
        /// Convert virtual-key code and keyboard state to unicode string
        /// </summary>
        internal static byte[] ReadKeyboardState() {
            var state = new byte[256];
            for (int key = 1; key < state.Length; key++) {
                if (WindowUtil.IsKeyDown(key)) state[key] = 0x80;
            }
            foreach (int key in new[] { 0x14, 0x90, 0x91 }) state[key] |= (byte)(GetKeyState(key) & 1);
            return state;
        }

        internal static void Reset() {
            _lastIsDead = false;
            _lastVirtKeyCode = 0;
        }

        internal static string VkCodeToString(uint vkCode, bool isKeyDown, byte[] keyState) {
            // ToUnicode needs StringBuilder, it populates that during execution.
            var output = new StringBuilder(5);

            uint scanCode = MapVirtualKey(vkCode, 0);

            int result = ToUnicode(vkCode, scanCode, keyState, output, 5, 0);

            switch (result) {
                case -1:
                    // dead-key character (accent or diacritic)

                    // clear buffer because it will otherwise crash `public Rectangle AbsoluteBounds` in Control.cs
                    // see also: http://archives.miloush.net/michkap/archive/2005/01/19/355870.html
                    while (ToUnicode(vkCode, scanCode, keyState, output, 5, 0) < 0) { /* SPIN */ }

                    // reinject last key because apparently when calling functions related to keyboard inputs
                    // messes up their internal states everywhere. :rolleyes:
                    // for reference see https://gist.github.com/Ciantic/471698
                    if (_lastVirtKeyCode != 0 && _lastIsDead) {
                        var temp = new StringBuilder(5);
                        ToUnicode(_lastVirtKeyCode, _lastScanCode, _lastKeyState, temp, 5, 0);
                    }

                    _lastIsDead = true;
                    _lastVirtKeyCode = vkCode;
                    _lastScanCode = scanCode;
                    _lastKeyState = (byte[])keyState.Clone();

                    if (isKeyDown) return "";
                    break;
                case 0:
                    // no translation for the current state of the keyboard
                    return "";
                case 2:
                    // two or more characters were written to the buffer
                    // this is most likely a dead-key that could not be combined with the current one
                case 1:
                default:
                    // single character was written to the buffer
                    break;

            }

            _lastIsDead = false;

            return isKeyDown ? output.ToString() : "";
        }
    }
}
