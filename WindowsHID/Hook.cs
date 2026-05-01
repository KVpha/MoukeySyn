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
/// 鼠标钩子 - 捕获所有鼠标事件
/// 使用低级钩子(WH_MOUSE_LL)获取系统级鼠标事件
/// </summary>
public static class MouseHook
{
    static MouseHook()
    {
        
    }

    private static int lastMouseX = 0;
    private static int lastMouseY = 0;
    private static bool isFirstMove = true;

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
                        Console.WriteLine("[Hook] Mouse hook installed successfully");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hook] Error: {ex.Message}");
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
    
    static int count = 0;
    public static int maxCount = 5;

    private static IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMessagesHook msg = (MouseMessagesHook)wParam;

                if (msg == MouseMessagesHook.WM_MOUSEMOVE)
                {
                    // 采样检查
                    if (count >= maxCount)
                    {
                        count = 0;
                    }
                    else
                    {
                        count++;
                        return SystemLevel_IO.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                    }
                }

                // 计算相对位移
                int deltaX = hookStruct.pt.X - lastMouseX;
                int deltaY = hookStruct.pt.Y - lastMouseY;

                // 异常值检测
                if (Math.Abs(deltaX) > 2000 || Math.Abs(deltaY) > 2000)
                {
                    deltaX = 0;
                    deltaY = 0;
                }

                lastMouseX = hookStruct.pt.X;
                lastMouseY = hookStruct.pt.Y;

                // 如果是第一次移动，不发送增量
                if (!isFirstMove || msg != MouseMessagesHook.WM_MOUSEMOVE)
                {
                    MouseAction?.Invoke(null, new MouseInputData()
                    {
                        code = (int)wParam,
                        hookStruct = hookStruct,
                        deltaX = deltaX,
                        deltaY = deltaY
                    });
                }
                else
                {
                    isFirstMove = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hook] Error: {ex.Message}");
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
