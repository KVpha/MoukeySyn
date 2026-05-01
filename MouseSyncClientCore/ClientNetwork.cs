
using CommonLib;
using MouseSyncClientCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WindowsHID;

namespace MouseSync.Client;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

public class ClientNetwork
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int mouse_event(
        uint dwFlags,
        uint dx,
        uint dy,
        uint dwData,
        IntPtr dwExtraInfo);
    
    public static LogHandler LogHandler { set; get; } = Console.WriteLine;
    
    public Connection Connection { get; private set; }
    public event EventHandler onLoaded=(s,b)=>LogHandler("Connected to server successfully");
    
    private bool isInRelativeMode = false;
    
    public ClientNetwork(in string ip,in int port) {
        isInRelativeMode = Info.instance.UseRelativeMouseMode;
        
        Connection = Connection.connect(ip,port,receive);
        Connection.onError += Connection_onError;
        
        Connection.send("ok");
        sendPairData(DataExchange.RESOLUTION, Device.Resolution);
        sendPairData(DataExchange.NAME, Device.MachineName);
        Connection.StartReceive();
        onLoaded?.Invoke(this,EventArgs.Empty);
        
        int result=Connection.receiveTask.Result;
        if(result == 0)
        {
            throw new Exception("Cancelled by user");
        }
    }

    private void Connection_onError(Exception e)
    {
        LogHandler("Connection has been interrupted,disconnected form server\n");
        throw e;
    }

    private void sendPairData(string prefix,string content)
    {
        Connection.send($"{prefix}{DataExchange.SPLIT}{content}");
    }
    
    private void receive(string msg)
    {
        if (Programe.isDebug)
        {
            LogHandler("Received: " + msg);
        }
        
        var splited=msg.Split(DataExchange.SPLIT);
        
        try
        {
            if (splited[0] == DataExchange.MOUSE)
            {
                handleMouseEvent(splited);
            }
            else if (splited[0] == DataExchange.MOUSE_RELATIVE)
            {
                handleMouseEventRelative(splited);
            }
            else if (splited[0] == DataExchange.KEY)
            {
                handleKeyboardEvent(splited);
            }
        }
        catch(Exception e)
        {
            LogHandler($"Error Parse: {e.Message}");
        }
    }
    
    public static bool isSimulate = true;
    
    private void handleMouseEvent(string[] msg)
    {
        try
        {
            int button = int.Parse(msg[1]);
            int x = int.Parse(msg[2]);
            int y = int.Parse(msg[3]);
            var mouseData = int.Parse(msg[4]);
            
            MOUSEINPUT mouseInput = new();
            mouseInput.dy = y;
            mouseInput.dx = x;
            mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_MOVE;

            if (button==(int)MouseMessagesHook.WM_MOUSEWHEEL) {
                if (Programe.isDebug)
                {
                    LogHandler("Simulate wheel");
                }
                mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_WHEEL;
                mouseInput.mouseData = mouseData>>16;
            }
            else if (DataExchange.MOUSE_KEY_MAP.ContainsKey(button))
            {
                if (Programe.isDebug)
                {
                    LogHandler("simulate btn press");
                }
                mouseInput.dwFlags = DataExchange.MOUSE_KEY_MAP[button];
            }
            else
            {
                LogHandler("Error: can not parse button :" + button);
                return;
            }
            
            if (isSimulate)
            {
                Input.sendMouseInput(mouseInput);
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleMouseEvent: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 处理相对鼠标移动（Raw Input模式）
    /// 直接使用mouse_event驱动级API
    /// </summary>
    private void handleMouseEventRelative(string[] msg)
    {
        try
        {
            int button = int.Parse(msg[1]);
            int deltaX = int.Parse(msg[2]);
            int deltaY = int.Parse(msg[3]);
            var mouseData = int.Parse(msg[4]);
            
            if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
            {
                LogHandler($"[RELATIVE] deltaX={deltaX}, deltaY={deltaY}");
            }
            
            MouseMessagesHook mouseMsg = (MouseMessagesHook)button;

            switch (mouseMsg)
            {
                case MouseMessagesHook.WM_MOUSEMOVE:
                    // 相对移动 - 直接调用mouse_event驱动级API
                    if ((deltaX != 0 || deltaY != 0) && isSimulate)
                    {
                        // 使用mouse_event而不是SendInput
                        // MOUSEEVENTF_MOVE = 0x0001
                        const uint MOUSEEVENTF_MOVE = 0x0001;
                        
                        mouse_event(
                            MOUSEEVENTF_MOVE,
                            (uint)deltaX,
                            (uint)deltaY,
                            0,
                            IntPtr.Zero
                        );
                    }
                    break;

                case MouseMessagesHook.WM_MOUSEWHEEL:
                    // 滚轮
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_WHEEL = 0x0800;
                        mouse_event(
                            MOUSEEVENTF_WHEEL,
                            0,
                            0,
                            (uint)(mouseData >> 16),
                            IntPtr.Zero
                        );
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONDOWN:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONUP:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_LEFTUP = 0x0004;
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONDOWN:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONUP:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero);
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONDOWN:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
                        mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, IntPtr.Zero);
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONUP:
                    if (isSimulate)
                    {
                        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
                        mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, IntPtr.Zero);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleMouseEventRelative: {ex.Message}");
        }
    }
    
    private void handleKeyboardEvent(string[] msg)
    {
        try
        {
            int code = int.Parse(msg[1]);
            var vkcode = ushort.Parse(msg[2]);
            var scanCode = ushort.Parse(msg[3]);
            
            if(DataExchange.KEYEVENT_MAP.ContainsKey(code))
            {
                if(Programe.isDebug)
                {
                    LogHandler($"Key receive: {vkcode}");
                }

                var flag = DataExchange.KEYEVENT_MAP[code];
                var input = new Input.KEYBDINPUT() {
                    wVk = vkcode,
                    wScan = scanCode,
                    dwFlags = (uint)flag
                };
                
                if(isSimulate)
                {
                    Input.sendKeyboardInput(input);
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleKeyboardEvent: {ex.Message}");
        }
    }
}
