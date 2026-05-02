using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace WindowsHID;

public class Hook
{
    private static bool useRawInput = false;

    public static void SetRawInputMode(bool enabled)
    {
        useRawInput = enabled;
    }

    public static void StartAll()
    {
        MouseHook.Start();
        KeyboardHook.Start();

        // 如果启用 Raw Input，订阅事件并转接
        if (useRawInput)
        {
            RawInputCapture.RawMouseMoved += RawInputCapture_RawMouseMoved;
            RawInputCapture.RawMouseButtonChanged += RawInputCapture_RawMouseButtonChanged;
            Console.WriteLine("[✓] Raw Input events hooked");
        }
    }

    /// <summary>
    /// 处理原始鼠标移动事件 - 转接为 MouseInputData
    /// </summary>
    private static void RawInputCapture_RawMouseMoved(object sender, RawMouseEventArgs e)
    {
        try
        {
            // 构造 MouseInputData
            var mouseData = new MouseInputData()
            {
                code = (int)MouseMessagesHook.WM_MOUSEMOVE,
                hookStruct = new MSLLHOOKSTRUCT()
                {
                    pt = new POINT() { X = 0, Y = 0 },
                    mouseData = 0,
                    flags = 0,
                    time = (uint)Environment.TickCount,
                    dwExtraInfo = IntPtr.Zero
                },
                deltaX = e.DeltaX,
                deltaY = e.DeltaY,
                timestamp = e.Timestamp
            };

            MouseHook.InvokeMouseAction(mouseData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Error in RawMouseMoved: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理原始鼠标按钮事件 - 转接为 MouseInputData
    /// </summary>
    private static void RawInputCapture_RawMouseButtonChanged(object sender, RawMouseEventArgs e)
    {
        try
        {
            // 根据 ButtonFlags 确定按钮事件类型
            MouseMessagesHook msgType = MouseMessagesHook.WM_MOUSEMOVE;

            const uint RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
            const uint RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
            const uint RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
            const uint RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
            const uint RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
            const uint RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
            const uint RI_MOUSE_WHEEL = 0x0400;

            if ((e.ButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
                msgType = MouseMessagesHook.WM_LBUTTONDOWN;
            else if ((e.ButtonFlags & RI_MOUSE_LEFT_BUTTON_UP) != 0)
                msgType = MouseMessagesHook.WM_LBUTTONUP;
            else if ((e.ButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
                msgType = MouseMessagesHook.WM_RBUTTONDOWN;
            else if ((e.ButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP) != 0)
                msgType = MouseMessagesHook.WM_RBUTTONUP;
            else if ((e.ButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
                msgType = MouseMessagesHook.WM_MBUTTONDOWN;
            else if ((e.ButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
                msgType = MouseMessagesHook.WM_MBUTTONUP;
            else if ((e.ButtonFlags & RI_MOUSE_WHEEL) != 0)
                msgType = MouseMessagesHook.WM_MOUSEWHEEL;

            var mouseData = new MouseInputData()
            {
                code = (int)msgType,
                hookStruct = new MSLLHOOKSTRUCT()
                {
                    pt = new POINT() { X = 0, Y = 0 },
                    mouseData = (int)e.WheelDelta,
                    flags = 0,
                    time = (uint)Environment.TickCount,
                    dwExtraInfo = IntPtr.Zero
                },
                deltaX = 0,
                deltaY = 0,
                timestamp = e.Timestamp
            };

            MouseHook.InvokeMouseAction(mouseData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Error in RawMouseButtonChanged: {ex.Message}");
        }
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
/// 支持与 Raw Input 共存
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

    /// <summary>
    /// 内部方法：直接调用 MouseAction 事件（供 Raw Input 适配器使用）
    /// </summary>
    internal static void InvokeMouseAction(MouseInputData data)
    {
        MouseAction?.Invoke(null, data);
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

                // 第一次移动时初始化，不发送��量
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
