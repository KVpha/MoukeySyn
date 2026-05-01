using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WindowsHID;

/// <summary>
/// Raw Input捕获 - 直接从硬件捕获原始鼠标输入数据
/// 这是3D游戏使用的标准输入方法
/// </summary>
public static class RawInputCapture
{
    // Raw Input相关的DLL导入
    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList(
        IntPtr pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHdr);

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetRawInputBuffer(
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHdr);

    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RID_INPUT = 0x10000003;
    private const uint RID_HEADER = 0x10000005;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawMouseData
    {
        public ushort usFlags;
        public uint ulButtons;
        public short sButtonData;
        public int lLastX;
        public int lLastY;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawInputData
    {
        [FieldOffset(0)]
        public RawInputHeader header;

        [FieldOffset(24)]
        public RawMouseData mouse;
    }

    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    private static IntPtr lastMouseHandle = IntPtr.Zero;
    private static long lastEventTime = 0;
    private static int lastMouseX = 0;
    private static int lastMouseY = 0;

    public static event EventHandler<RawMouseEventArgs> RawMouseMoved;
    public static event EventHandler<RawMouseEventArgs> RawMouseButtonChanged;

    /// <summary>
    /// 初始化Raw Input捕获
    /// 必须在有窗口的线程中调用
    /// </summary>
    public static bool Initialize(IntPtr hwnd)
    {
        try
        {
            RawInputDevice[] rid = new RawInputDevice[1];
            rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
            rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
            rid[0].dwFlags = RIDEV_INPUTSINK;
            rid[0].hwndTarget = hwnd;

            if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(rid[0])))
            {
                Console.WriteLine("[RawInput] Failed to register raw input devices");
                return false;
            }

            Console.WriteLine("[RawInput] Successfully initialized");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawInput] Initialization error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 处理Raw Input消息
    /// 从WM_INPUT消息中提取鼠标数据
    /// </summary>
    public static void ProcessRawInput(IntPtr lParam)
    {
        try
        {
            uint pcbSize = (uint)Marshal.SizeOf<RawInputData>();
            uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();

            // 获取Raw Input数据
            if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref pcbSize, headerSize) == 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)pcbSize);
                try
                {
                    GetRawInputData(lParam, RID_INPUT, buffer, ref pcbSize, headerSize);
                    RawInputData rid = Marshal.PtrToStructure<RawInputData>(buffer);

                    if (rid.header.dwType == RIM_TYPEMOUSE)
                    {
                        ProcessMouseData(rid.mouse);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawInput] Error processing raw input: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理原始鼠标数据
    /// 提取相对移动和按钮状态
    /// </summary>
    private static void ProcessMouseData(RawMouseData mouseData)
    {
        try
        {
            long currentTime = Environment.TickCount64;
            
            // 检查是否是相对移动
            const ushort MOUSE_MOVE_RELATIVE = 0x00;
            const ushort MOUSE_MOVE_ABSOLUTE = 0x80;

            bool isRelativeMove = (mouseData.usFlags & MOUSE_MOVE_ABSOLUTE) == 0;

            if (isRelativeMove && (mouseData.lLastX != 0 || mouseData.lLastY != 0))
            {
                // 这是相对鼠标移动 - 这正是3D游戏需要的
                int deltaX = mouseData.lLastX;
                int deltaY = mouseData.lLastY;

                // 触发鼠标移动事件
                RawMouseMoved?.Invoke(null, new RawMouseEventArgs
                {
                    DeltaX = deltaX,
                    DeltaY = deltaY,
                    IsRelativeMove = true,
                    Timestamp = currentTime
                });

                if (Console.Out != null) // Debug output
                {
                    // Console.WriteLine($"[RawInput] Mouse relative move: X={deltaX}, Y={deltaY}");
                }
            }

            // 检查按钮状态
            const uint RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
            const uint RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
            const uint RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
            const uint RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
            const uint RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
            const uint RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
            const uint RI_MOUSE_BUTTON_4_DOWN = 0x0040;
            const uint RI_MOUSE_BUTTON_4_UP = 0x0080;
            const uint RI_MOUSE_BUTTON_5_DOWN = 0x0100;
            const uint RI_MOUSE_BUTTON_5_UP = 0x0200;
            const uint RI_MOUSE_WHEEL = 0x0400;

            uint buttonFlags = mouseData.ulButtons;

            if ((buttonFlags & (RI_MOUSE_LEFT_BUTTON_DOWN | RI_MOUSE_LEFT_BUTTON_UP |
                               RI_MOUSE_RIGHT_BUTTON_DOWN | RI_MOUSE_RIGHT_BUTTON_UP |
                               RI_MOUSE_MIDDLE_BUTTON_DOWN | RI_MOUSE_MIDDLE_BUTTON_UP |
                               RI_MOUSE_WHEEL)) != 0)
            {
                RawMouseButtonChanged?.Invoke(null, new RawMouseEventArgs
                {
                    ButtonFlags = buttonFlags,
                    WheelDelta = mouseData.sButtonData,
                    Timestamp = currentTime
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawInput] Error processing mouse data: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理Raw Input
    /// </summary>
    public static void Cleanup()
    {
        try
        {
            RawInputDevice[] rid = new RawInputDevice[1];
            rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
            rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
            rid[0].dwFlags = RIDEV_REMOVE;
            rid[0].hwndTarget = IntPtr.Zero;

            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(rid[0]));
            Console.WriteLine("[RawInput] Cleanup complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RawInput] Cleanup error: {ex.Message}");
        }
    }
}

/// <summary>
/// Raw Input事件参数
/// </summary>
public class RawMouseEventArgs : EventArgs
{
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
    public bool IsRelativeMove { get; set; }
    public uint ButtonFlags { get; set; }
    public short WheelDelta { get; set; }
    public long Timestamp { get; set; }
}
