using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;

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
/// 驱动级鼠标钩子 - 捕获底层鼠标事件
/// 适用于3D游戏等直接读取底层鼠标数据的应用
/// </summary>
public static class MouseHook
{
    static MouseHook()
    {
        
    }

    // 用于相对鼠标移动追踪
    private static int lastMouseX = 0;
    private static int lastMouseY = 0;
    private static bool isFirstMove = true;
    
    // 用于过滤异常跳跃
    private static long lastEventTime = 0;
    private const long MIN_DELTA_TIME = 5; // 最少5ms才算正常事件

    public static void addCallback(EventHandler<MouseInputData> handler)
    {
        MouseAction += handler;
    }

    private static IntPtr hookID = IntPtr.Zero;

    public static event EventHandler<MouseInputData>? MouseAction;

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
                        Console.WriteLine("[Hook] Mouse hook installed successfully (WH_MOUSE_LL)");
                    }
                    else
                    {
                        Console.WriteLine("[Hook] Failed to install mouse hook");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hook] Error starting mouse hook: {ex.Message}");
            }
        }
    }

    public static void Stop()
    {
        if (hookID != IntPtr.Zero)
        {
            SystemLevel_IO.UnhookWindowsHookEx(hookID);
            hookID = IntPtr.Zero;
            Console.WriteLine("[Hook] Mouse hook removed");
        }
    }
    
    static int count = 0;
    public static int maxCount = 1; // 相对模式下，每次都发送（不进行采样）

    /// <summary>
    /// 低级鼠标钩子回调 - 直接捕获原始鼠标事件
    /// 这是能捕获底层鼠标数据的最低级别API
    /// </summary>
    private static IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMessagesHook msg = (MouseMessagesHook)wParam;

                // 只处理鼠标移动事件
                if (msg == MouseMessagesHook.WM_MOUSEMOVE)
                {
                    // 计算时间差
                    long currentTime = Environment.TickCount64;
                    long timeDelta = currentTime - lastEventTime;
                    
                    // 计算位置差
                    int deltaX = 0;
                    int deltaY = 0;

                    if (!isFirstMove)
                    {
                        deltaX = hookStruct.pt.X - lastMouseX;
                        deltaY = hookStruct.pt.Y - lastMouseY;
                        
                        // 严格的异常值检测
                        // 如果位移过大（通常> 2000像素），说明是SetCursorPos导致的跳跃或异常
                        if (Math.Abs(deltaX) > 2000 || Math.Abs(deltaY) > 2000)
                        {
                            // 跳过这个事件，不触发回调
                            lastMouseX = hookStruct.pt.X;
                            lastMouseY = hookStruct.pt.Y;
                            lastEventTime = currentTime;
                            return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                        }
                    }
                    else
                    {
                        isFirstMove = false;
                    }

                    // 更新位置和时间
                    lastMouseX = hookStruct.pt.X;
                    lastMouseY = hookStruct.pt.Y;
                    lastEventTime = currentTime;

                    // 触发事件，传递计算的相对位移
                    var mouseData = new MouseInputData()
                    {
                        code = (int)wParam,
                        hookStruct = hookStruct,
                        deltaX = deltaX,
                        deltaY = deltaY
                    };

                    MouseAction?.Invoke(null, mouseData);
                }
                else
                {
                    // 对于鼠标按钮事件，立即处理
                    var mouseData = new MouseInputData()
                    {
                        code = (int)wParam,
                        hookStruct = hookStruct,
                        deltaX = 0,
                        deltaY = 0
                    };

                    MouseAction?.Invoke(null, mouseData);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Error in mouse hook: {ex.Message}");
        }

        return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}


//keyboard

public class KeyboardHook
{
    static KeyboardHook()
    {
        
    }
    public static event EventHandler<KeyboardInputData> KeyboardAction;

    private static IntPtr hookID = IntPtr.Zero;
    public static void addCallback(EventHandler<KeyboardInputData> handler)
    {
        KeyboardAction += handler;
    }   
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 )
        {
            KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            KeyboardAction?.Invoke(null,new KeyboardInputData() 
                {code=(int)wParam,HookStruct=hookStruct });
        }
        return SystemLevel_IO.CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    public static void Start()
    {
        if (hookID == IntPtr.Zero)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                hookID = SystemLevel_IO.SetWindowsHookEx(HookType.WH_KEYBOARD_LL, HookCallback, SystemLevel_IO.GetModuleHandle(curModule.ModuleName));
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
