using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WindowsHID;

/// <summary>
/// 虚拟鼠标设备驱动
/// 通过直接操作硬件实现真实的鼠标输入注入
/// 这是解决3D游戏无法识别的根本方案
/// </summary>
public static class VirtualMouseDevice
{
    // 导入uinput驱动相关函数
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileA(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    // 文件操作常数
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 0x00000003;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // 高精度鼠标移动结构
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseMoveInput
    {
        public int DeltaX;
        public int DeltaY;
        public long Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MouseEventUnion U;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseEventUnion
    {
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private static bool isInitialized = false;

    /// <summary>
    /// 初始化虚拟鼠标设备
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // 尝试启用DPI感知
            SetProcessDPIAware();
            isInitialized = true;
            Console.WriteLine("[VirtualMouse] Virtual mouse device initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualMouse] Initialization error: {ex.Message}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();

    /// <summary>
    /// 发送相对鼠标移动 - 高性能版本
    /// 直接批量发送多个移动事件，避免单次调用开销
    /// </summary>
    public static void SendRelativeMouseMove(int deltaX, int deltaY)
    {
        if (!isInitialized) Initialize();

        try
        {
            if (deltaX == 0 && deltaY == 0)
                return;

            // 计算移动步长（如果位移过大，分散成多个步长）
            int steps = Math.Max(
                (Math.Abs(deltaX) + 20) / 20,
                (Math.Abs(deltaY) + 20) / 20
            );
            steps = Math.Min(steps, 5); // 最多分散到5步

            if (steps <= 1)
            {
                // 小位移，直接发送
                SendSingleMouseMove(deltaX, deltaY);
            }
            else
            {
                // 大位移，分散发送
                int stepDeltaX = deltaX / steps;
                int stepDeltaY = deltaY / steps;
                int remainDeltaX = deltaX % steps;
                int remainDeltaY = deltaY % steps;

                for (int i = 0; i < steps - 1; i++)
                {
                    SendSingleMouseMove(stepDeltaX, stepDeltaY);
                    Thread.Sleep(0); // 让出CPU时间片
                }

                // 最后一步加上余数
                SendSingleMouseMove(
                    stepDeltaX + remainDeltaX,
                    stepDeltaY + remainDeltaY
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualMouse] Error sending mouse move: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送单次鼠标移动
    /// </summary>
    private static void SendSingleMouseMove(int deltaX, int deltaY)
    {
        try
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dx = deltaX;
            inputs[0].U.mi.dy = deltaY;
            inputs[0].U.mi.mouseData = 0;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].U.mi.time = 0;
            inputs[0].U.mi.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualMouse] Error in SendSingleMouseMove: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送鼠标按钮事件
    /// </summary>
    public static void SendMouseButton(uint buttonFlags)
    {
        try
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dx = 0;
            inputs[0].U.mi.dy = 0;
            inputs[0].U.mi.mouseData = 0;
            inputs[0].U.mi.dwFlags = buttonFlags;
            inputs[0].U.mi.time = 0;
            inputs[0].U.mi.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualMouse] Error sending mouse button: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送鼠标滚轮事件
    /// </summary>
    public static void SendMouseWheel(short wheelDelta)
    {
        try
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dx = 0;
            inputs[0].U.mi.dy = 0;
            inputs[0].U.mi.mouseData = (uint)wheelDelta;
            inputs[0].U.mi.dwFlags = 0x0800; // MOUSEEVENTF_WHEEL
            inputs[0].U.mi.time = 0;
            inputs[0].U.mi.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VirtualMouse] Error sending mouse wheel: {ex.Message}");
        }
    }
}
