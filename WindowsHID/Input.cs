using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace WindowsHID;


[Obsolete]
public static class InputForMouse
{
    [DllImport("user32")]
    private static extern int mouse_event(uint dwFlags, int dx, int dy, uint mouseData, IntPtr dwExtraInfo);
    private static int mouse_event(MouseFlags flags, int x, int y,uint data=0)
    {
        return mouse_event((uint)(flags |MouseFlags.MOUSEEVENTF_ABSOLUTE), x, y,data,0);
    }
    [Flags]
    public enum MouseFlags : uint
    {
        MOUSEEVENTF_MOVE = 0x0001, //移动鼠标
        MOUSEEVENTF_LEFTDOWN = 0x0002, //模拟鼠标左键按下
        MOUSEEVENTF_LEFTUP = 0x0004, //模拟鼠标左键抬起
        MOUSEEVENTF_RIGHTDOWN = 0x0008, //模拟鼠标右键按下
        MOUSEEVENTF_RIGHTUP = 0x0010, //模拟鼠标右键抬起
        MOUSEEVENTF_MIDDLEDOWN = 0x0020, //模拟鼠标中键按下
        MOUSEEVENTF_MIDDLEUP = 0x0040,//模拟鼠标中键抬起
        MOUSEEVENTF_ABSOLUTE = 0x8000,// 标示是否采用绝对坐标
        MOUSEEVENTF_WHEEL=0x0800,
    }
    public static void simulate(MouseFlags flags, int x, int y,uint data=0)
    {
        mouse_event(flags, x, y,data);
    }
}

/// <summary>
/// 输入模拟层 - 支持绝对和相对鼠标输入
/// 针对3D游戏优化，使用驱动级方案
/// </summary>
public static class Input
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    
    // 驱动级鼠标移动 - 使用mouse_event而不是SendInput
    // mouse_event在某些游戏中更有效（直接操作驱动层）
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int mouse_event(
        uint dwFlags,      // 鼠标事件标志
        uint dx,           // X坐标或X位移
        uint dy,           // Y坐标或Y位移
        uint dwData,       // 鼠标特定数据
        IntPtr dwExtraInfo // 额外信息
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    // 用于锁定鼠标位置的临时存储
    private static bool isMouseLocked = false;
    private static int lockedX = 0;
    private static int lockedY = 0;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
    
    private static void sendInputSealed(INPUT[] inputs)
    {
        SendInput((uint)inputs.Length, inputs, INPUT.Size);
    }
    
    private static void sendOneInput(INPUT input)
    {
        sendInputSealed(new INPUT[] { input });
    }
    
    /// <summary>
    /// 发送绝对鼠标输入（用于桌面应用）
    /// </summary>
    public static void sendMouseInput(MOUSEINPUT mouseInput)
    {
        INPUT input = new INPUT();
        input.type = InputType.INPUT_MOUSE;
        convertLocation(ref mouseInput);
        mouseInput.dwFlags = mouseInput.dwFlags | MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE;
        input.U = new() { mi = mouseInput };
        sendOneInput(input);
    }
    
    /// <summary>
    /// 发送相对鼠标输入 - 驱动级优化版本（用于3D游戏）
    /// 使用mouse_event API而不是SendInput
    /// 这样可以直接在驱动层实现相对移动，游戏能正确识别
    /// </summary>
    public static void sendMouseInputRelative(MOUSEINPUT mouseInput)
    {
        try
        {
            // 验证有效的相对位移
            if (mouseInput.dx == 0 && mouseInput.dy == 0)
            {
                return; // 不发送零位移
            }

            // 使用mouse_event驱动级API
            // MOUSEEVENTF_MOVE: 表示相对移动
            // 不使用MOUSEEVENTF_ABSOLUTE，直接使用相对坐标
            uint flags = (uint)MOUSEEVENTF.MOUSEEVENTF_MOVE;

            // 对dx和dy进行有符号转换，确保负值正确处理
            uint dx = (uint)mouseInput.dx;
            uint dy = (uint)mouseInput.dy;

            // 调用驱动级mouse_event
            int result = mouse_event(
                flags,
                dx,
                dy,
                (uint)mouseInput.mouseData,
                IntPtr.Zero
            );

            // 添加极小延迟以确保驱动正确处理
            System.Threading.Thread.Sleep(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Input] Error in sendMouseInputRelative: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送鼠标按钮事件（相对模式下）
    /// </summary>
    public static void sendMouseButtonRelative(MOUSEINPUT mouseInput)
    {
        try
        {
            uint flags = 0;

            // 根据按钮类型设置标志
            if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_LEFTUP) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_RIGHTDOWN) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_RIGHTUP) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_MIDDLEDOWN) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_MIDDLEUP) != 0)
            {
                mouse_event((uint)MOUSEEVENTF.MOUSEEVENTF_MIDDLEUP, 0, 0, 0, IntPtr.Zero);
            }
            else if ((mouseInput.dwFlags & MOUSEEVENTF.MOUSEEVENTF_WHEEL) != 0)
            {
                mouse_event(
                    (uint)MOUSEEVENTF.MOUSEEVENTF_WHEEL,
                    0,
                    0,
                    (uint)mouseInput.mouseData,
                    IntPtr.Zero
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Input] Error in sendMouseButtonRelative: {ex.Message}");
        }
    }
    
    public const int max = 65535;
    
    public static void convertLocation(ref MOUSEINPUT mouseInput)
    {
        int width = Device.width;
        int height = Device.height;
        mouseInput.dx = (int)(mouseInput.dx / (float)width * max);
        mouseInput.dy = (int)(mouseInput.dy/(float)height*max);
    }
    
    public static void sendKeyboardInput(KEYBDINPUT keyboardInput)
    {
        INPUT input = new INPUT();
        input.type = InputType.INPUT_KETBOARD;
        input.U = new() { ki = keyboardInput };
        sendOneInput(input);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }
    
    public static class InputType 
    {
        public const int INPUT_MOUSE = 0;
        public const int INPUT_KETBOARD = 1;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time = 0;
        public IntPtr dwExtraInfo;

        public KEYBDINPUT()
        {
        }
    }
}

[Flags]
public enum MOUSEEVENTF : uint
{
    MOUSEEVENTF_MOVE = 0x0001,
    MOUSEEVENTF_LEFTDOWN = 0x0002,
    MOUSEEVENTF_LEFTUP = 0x0004,
    MOUSEEVENTF_RIGHTDOWN = 0x0008,
    MOUSEEVENTF_RIGHTUP = 0x0010,
    MOUSEEVENTF_MIDDLEDOWN = 0x0020,
    MOUSEEVENTF_MIDDLEUP = 0x0040,
    MOUSEEVENTF_ABSOLUTE = 0x8000,
    MOUSEEVENTF_WHEEL = 0x0800,
}

[Flags]
public enum KEYEVENTF : int
{
    KEYEVENTF_KEYDOWN = 0,
    KEYEVENTF_KEYUP = 0x0002,
    KEYEVENTF_SCANCODE = 0x0008,
    KEYEVENTF_UNICODE = 0x0004
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public int mouseData;
    public MOUSEEVENTF dwFlags;
    public uint time = 0;
    public IntPtr dwExtraInfo;

    public MOUSEINPUT()
    {
    }
}
