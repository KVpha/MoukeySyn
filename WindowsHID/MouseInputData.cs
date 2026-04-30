using System;
using System.Runtime.InteropServices;

namespace WindowsHID;

/// <summary>
/// 鼠标输入事件数据，包含绝对坐标和相对位移
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MouseInputData
{
    /// <summary>
    /// 鼠标事件代码（WM_MOUSEMOVE, WM_LBUTTONDOWN等）
    /// </summary>
    public int code;
    
    /// <summary>
    /// 低级鼠标钩子结构，包含绝对坐标信息
    /// </summary>
    public MSLLHOOKSTRUCT hookStruct;
    
    /// <summary>
    /// 相对X位移（相对于上次位置）
    /// </summary>
    public int deltaX;
    
    /// <summary>
    /// 相对Y位移（相对于上次位置）
    /// </summary>
    public int deltaY;
}

/// <summary>
/// 键盘输入事件数据
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct KeyboardInputData
{
    /// <summary>
    /// 键盘事件代码（WM_KEYDOWN, WM_KEYUP等）
    /// </summary>
    public int code;
    
    /// <summary>
    /// 低级键盘钩子结构
    /// </summary>
    public KBDLLHOOKSTRUCT HookStruct;
}

/// <summary>
/// 低级鼠标钩子结构
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    /// <summary>
    /// 鼠标位置
    /// </summary>
    public POINT pt;
    
    /// <summary>
    /// 鼠标特定数据（如滚轮delta）
    /// </summary>
    public int mouseData;
    
    /// <summary>
    /// 事件标志
    /// </summary>
    public int flags;
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public int time;
    
    /// <summary>
    /// 额外信息指针
    /// </summary>
    public IntPtr dwExtraInfo;
}

/// <summary>
/// 点结构
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

/// <summary>
/// 低级键盘钩子结构
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    /// <summary>
    /// 虚拟键码
    /// </summary>
    public int vkCode;
    
    /// <summary>
    /// 扫描码
    /// </summary>
    public int scanCode;
    
    /// <summary>
    /// 标志位
    /// </summary>
    public int flags;
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public int time;
    
    /// <summary>
    /// 额外信息指针
    /// </summary>
    public IntPtr dwExtraInfo;
}

/// <summary>
/// 鼠标消息枚举
/// </summary>
public enum MouseMessagesHook : int
{
    WM_MOUSEMOVE = 0x0200,
    WM_LBUTTONDOWN = 0x0201,
    WM_LBUTTONUP = 0x0202,
    WM_RBUTTONDOWN = 0x0204,
    WM_RBUTTONUP = 0x0205,
    WM_MBUTTONDOWN = 0x0207,
    WM_MBUTTONUP = 0x0208,
    WM_MOUSEWHEEL = 0x020A,
}

/// <summary>
/// 键盘消息枚举
/// </summary>
public enum KeyboardMessages : int
{
    WM_KEYDOWN = 256,
    WM_KEYUP = 257,
    WM_SYSKEYDOWN = 260,
    WM_SYSKEYUP = 261,
}

/// <summary>
/// 钩子类型枚举
/// </summary>
public enum HookType : int
{
    WH_JOURNALRECORD = 0,
    WH_JOURNALPLAYBACK = 1,
    WH_KEYBOARD = 2,
    WH_GETMESSAGE = 3,
    WH_CALLWNDPROC = 4,
    WH_CBT = 5,
    WH_SYSMSGFILTER = 6,
    WH_MOUSE = 7,
    WH_HARDWARE = 8,
    WH_DEBUG = 9,
    WH_SHELL = 10,
    WH_FOREGROUND = 11,
    WH_CALLWNDPROCRET = 12,
    WH_KEYBOARD_LL = 13,
    WH_MOUSE_LL = 14
}
