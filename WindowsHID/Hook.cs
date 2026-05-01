using System.Diagnostics;
using System.Net;
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

/// <summary>
/// 鼠标钩子 - 超低延迟版本
/// 优化：删除采样逻辑，所有事件立即发送
/// </summary>
public static class MouseHook
{
    private static int lastMouseX = 0;
    private static int lastMouseY = 0;
    private static bool isFirstMove = true;
    private static Stopwatch positionTimestamp = Stopwatch.StartNew();

    public static void addCallback(EventHandler<MouseInputData> handler)
    {
        MouseAction += handler;
    }

    private static IntPtr hookID = IntPtr.Zero;
    public static event EventHandler<MouseInputData>? MouseAction;

    // 弃用：移除采样逻辑
    public static int maxCount = 1; // 已弃用，保留向后兼容性

    public static void Start()
    {
        if (hookID == IntPtr.Zero)
        {
            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    nint handle = SystemLevel_IO.GetModuleHandle(curModule.ModuleName);
                    hookID = SystemLevel_IO.SetWindowsHookEx(HookType.WH_MOUSE_LL, LowLevelMouseProcCallback, handle);
                    
                    if (hookID != IntPtr.Zero)
                    {
                        Console.WriteLine("[✓] Mouse hook installed (ultra-low latency mode)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Hook error: {ex.Message}");
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
        try
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMessagesHook msg = (MouseMessagesHook)wParam;

                // 计算相对位移
                int deltaX = hookStruct.pt.X - lastMouseX;
                int deltaY = hookStruct.pt.Y - lastMouseY;

                // 异常值检测（防止鼠标跳变）
                if (Math.Abs(deltaX) > 3000 || Math.Abs(deltaY) > 3000)
                {
                    // 鼠标闪现，忽略本次
                    lastMouseX = hookStruct.pt.X;
                    lastMouseY = hookStruct.pt.Y;
                    return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                lastMouseX = hookStruct.pt.X;
                lastMouseY = hookStruct.pt.Y;

                // 第一次移动时初始化，不发送增量
                if (isFirstMove && msg == MouseMessagesHook.WM_MOUSEMOVE)
                {
                    isFirstMove = false;
                    positionTimestamp.Restart();
                    return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                // 优化：所有事件无延迟立即发送
                long timestamp = Stopwatch.GetTimestamp();
                MouseAction?.Invoke(null, new MouseInputData()
                {
                    code = (int)wParam,
                    hookStruct = hookStruct,
                    deltaX = deltaX,
                    deltaY = deltaY,
                    timestamp = timestamp  // 高精度时间戳
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Error: {ex.Message}");
        }

        return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}

/// <summary>
/// 键盘钩子 - 无采样，立即发送
/// </summary>
public class KeyboardHook
{
    public static event EventHandler<KeyboardInputData> KeyboardAction;
    private static IntPtr hookID = IntPtr.Zero;

    public static void addCallback(EventHandler<KeyboardInputData> handler)
    {
        KeyboardAction += handler;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                long timestamp = Stopwatch.GetTimestamp();
                
                KeyboardAction?.Invoke(null, new KeyboardInputData()
                {
                    code = (int)wParam,
                    HookStruct = hookStruct,
                    timestamp = timestamp  // 高精度时间戳
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Keyboard Hook] Error: {ex.Message}");
            }
        }
        return SystemLevel_IO.CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    public static void Start()
    {
        if (hookID == IntPtr.Zero)
        {
            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    hookID = SystemLevel_IO.SetWindowsHookEx(
                        HookType.WH_KEYBOARD_LL, 
                        HookCallback, 
                        SystemLevel_IO.GetModuleHandle(curModule.ModuleName));
                    
                    if (hookID != IntPtr.Zero)
                    {
                        Console.WriteLine("[✓] Keyboard hook installed");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Keyboard hook error: {ex.Message}");
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
}
