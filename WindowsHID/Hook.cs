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

public static class MouseHook
{
    static MouseHook()
    {
        
    }

    // 用于相对鼠标移动追踪
    private static int lastMouseX = 0;
    private static int lastMouseY = 0;
    private static bool isFirstMove = true;
    private static long lastEventTime = 0;

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
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                nint handle;
                handle= SystemLevel_IO.GetModuleHandle(curModule.ModuleName);
                //handle=SystemLevel_IO.GetConsoleWindow();
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
    
    static int count = 0;
    public static int maxCount = 5;//for moving event,only (1/maxCount) of messages will be sent
    
    /// <summary>
    /// 低级鼠标钩子回调 - 捕获所有鼠标事件
    /// 用于获取准确的相对鼠标位移数据
    /// </summary>
    private static IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        void triggerEvent()
        {
            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            
            // 计算相对位移
            int deltaX = 0;
            int deltaY = 0;
            
            if (!isFirstMove)
            {
                // 计算自上次位置以来的位移（直接差值，不进行任何处理）
                deltaX = hookStruct.pt.X - lastMouseX;
                deltaY = hookStruct.pt.Y - lastMouseY;
                
                // 仅过滤明显的异常值（可能是SetCursorPos导致的跳跃，通常在1000+像素）
                // 同时允许快速移动（正常范围内的任何位移）
                if (Math.Abs(deltaX) > 1200 || Math.Abs(deltaY) > 1200)
                {
                    // 极端异常值，可能是校准操作导致的，忽略
                    deltaX = 0;
                    deltaY = 0;
                }
            }
            else
            {
                // 第一次移动，初始化基准位置
                isFirstMove = false;
            }
            
            // 更新位置记录
            lastMouseX = hookStruct.pt.X;
            lastMouseY = hookStruct.pt.Y;
            lastEventTime = Environment.TickCount64;
            
            // 触发事件，传递计算的相对位移
            MouseAction?.Invoke(null, new MouseInputData() 
            { 
                code = (int)wParam, 
                hookStruct = hookStruct,
                deltaX = deltaX,
                deltaY = deltaY
            });
        }
        
        if (nCode >= 0)
        {
            MouseMessagesHook msg = (MouseMessagesHook)wParam;
            
            // 对于鼠标移动事件，使用采样率过滤
            if (msg == MouseMessagesHook.WM_MOUSEMOVE)
            {
                if (count == maxCount + 1 || count > maxCount + 1)
                {
                    count = 0;
                }
                if (count == 0)
                {
                    triggerEvent();
                }
                count++;
            }
            else
            {
                // 对于鼠标按钮事件，立即处理
                triggerEvent();
            }
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
