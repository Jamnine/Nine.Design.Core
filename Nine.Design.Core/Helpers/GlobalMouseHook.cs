using System.Runtime.InteropServices;
using System.Windows;

namespace Nine.Design.Core.Helpers
{

namespace Nine.Design.Core
    {
        public class GlobalMouseHook : IDisposable
        {
            #region Win32 API
            private const int WH_MOUSE_LL = 14;
            private const int WM_LBUTTONDOWN = 0x0201;
            private const int WM_RBUTTONDOWN = 0x0204;
            private const int WM_MOUSEMOVE = 0x0200;

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MSLLHOOKSTRUCT
            {
                public POINT pt;
                public uint mouseData;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string lpModuleName);

            private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
            private LowLevelMouseProc _proc;
            private IntPtr _hookID = IntPtr.Zero;
            #endregion

            // 鼠标事件回调
            public event Action<Point> MouseClick;
            public event Action<Point> MouseMove;

            public GlobalMouseHook()
            {
                _proc = HookCallback;
                _hookID = SetHook(_proc);
            }

            private IntPtr SetHook(LowLevelMouseProc proc)
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }

            private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    var point = new Point(hookStruct.pt.X, hookStruct.pt.Y);

                    // 监听鼠标左键点击
                    if (wParam == (IntPtr)WM_LBUTTONDOWN)
                    {
                        MouseClick?.Invoke(point);
                    }
                    // 监听鼠标移动
                    else if (wParam == (IntPtr)WM_MOUSEMOVE)
                    {
                        MouseMove?.Invoke(point);
                    }
                }
                return CallNextHookEx(_hookID, nCode, wParam, lParam);
            }

            public void Dispose()
            {
                UnhookWindowsHookEx(_hookID);
            }
        }
    }
}