using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsHID;

public class Hook
{
    public static void StartAll()
    {
        MouseHook.Start();
        KeyboardHook.Start();
    }
}

public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

public static class SystemLevel_IO
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(HookType idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId = 0);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetConsoleWindow();
}

public static class MouseHook
{
    private static IntPtr hookID = IntPtr.Zero;
    public static event EventHandler<MouseInputData>? MouseAction;

    // 改进：增加采样率，减少过滤比例 (原来是maxCount=5，现改为2)
    public static int maxCount = 1; // 1表示每次都发送，提高实时性
    static int count = 0;

    public static void addCallback(EventHandler<MouseInputData> handler)
    {
        MouseAction += handler;
    }

    public static void Start()
    {
        if (hookID == IntPtr.Zero)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                nint handle = SystemLevel_IO.GetModuleHandle(curModule.ModuleName);
                hookID = SystemLevel_IO.SetWindowsHookEx(HookType.WH_MOUSE_LL, LowLevelMouseProcCallback, handle);
            }
        }
    }

    public static void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            SystemLevel_IO.UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
        }
    }

    private static IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseMessagesHook msgType = (MouseMessagesHook)wParam;

            // 改进：非移动事件立即发送，移动事件根据采样率发送
            if (msgType != MouseMessagesHook.WM_MOUSEMOVE)
            {
                TriggerMouseEvent(msgType, hookStruct);
            }
            else
            {
                // 采样率控制：maxCount=1时每次都发送
                if (maxCount <= 1 || count++ % (maxCount + 1) == 0)
                {
                    TriggerMouseEvent(msgType, hookStruct);
                }
            }
        }
        return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void TriggerMouseEvent(MouseMessagesHook msgType, MSLLHOOKSTRUCT hookStruct)
    {
        MouseAction?.Invoke(null, new MouseInputData()
        {
            code = (int)msgType,
            hookStruct = hookStruct,
            timestamp = Stopwatch.GetTimestamp() // 高精度时间戳
        });
    }
}

// 键盘钩子
public class KeyboardHook
{
    private static IntPtr hookID = IntPtr.Zero;
    public static event EventHandler<KeyboardInputData>? KeyboardAction;

    public static void addCallback(EventHandler<KeyboardInputData> handler)
    {
        KeyboardAction += handler;
    }

    public static void Start()
    {
        if (hookID == IntPtr.Zero)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                hookID = SystemLevel_IO.SetWindowsHookEx(HookType.WH_KEYBOARD_LL, HookCallback, 
                    SystemLevel_IO.GetModuleHandle(curModule.ModuleName));
            }
        }
    }

    public static void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            SystemLevel_IO.UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            KeyboardAction?.Invoke(null, new KeyboardInputData()
            {
                code = (int)wParam,
                HookStruct = hookStruct,
                timestamp = Stopwatch.GetTimestamp() // 高精度时间戳
            });
        }
        return SystemLevel_IO.CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }
}
