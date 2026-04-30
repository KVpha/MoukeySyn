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





