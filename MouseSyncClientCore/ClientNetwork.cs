
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
    
    public static LogHandler LogHandler { set; get; } = Console.WriteLine;
    
    public Connection Connection { get; private set; }
    public event EventHandler onLoaded=(s,b)=>LogHandler("Connected to server successfully");
    
    public ClientNetwork(in string ip,in int port) {
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
        try{
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
    
    public static bool isSimulate=true;
    
    /// <summary>
    /// 处理绝对鼠标移动事件（桌面模式）
    /// </summary>
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
    /// 处理相对鼠标移动事件（3D游戏模式）
    /// 直接转发相对位移值给Windows SendInput
    /// </summary>
    private void handleMouseEventRelative(string[] msg)
    {
        try
        {
            int button = int.Parse(msg[1]);
            int deltaX = int.Parse(msg[2]);
            int deltaY = int.Parse(msg[3]);
            var mouseData = int.Parse(msg[4]);
            
            // 调试输出
            if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
            {
                LogHandler($"[RELATIVE] button={button}, deltaX={deltaX}, deltaY={deltaY}, mouseData={mouseData}");
            }
            
            MOUSEINPUT mouseInput = new();
            mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_MOVE;

            // 判断事件类型
            if (button == (int)MouseMessagesHook.WM_MOUSEWHEEL) 
            {
                // 滚轮事件
                if (Programe.isDebug)
                {
                    LogHandler("Simulate wheel (relative mode)");
                }
                mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_WHEEL;
                mouseInput.mouseData = mouseData >> 16;
            }
            else if (button == (int)MouseMessagesHook.WM_MOUSEMOVE)
            {
                // 移动事件 - 直接使用相对位移
                mouseInput.dx = deltaX;
                mouseInput.dy = deltaY;
                mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_MOVE;
            }
            else if (DataExchange.MOUSE_KEY_MAP.ContainsKey(button))
            {
                // 按钮事件（左键、右键等）
                if (Programe.isDebug)
                {
                    LogHandler($"Mouse button: {button}");
                }
                mouseInput.dwFlags = DataExchange.MOUSE_KEY_MAP[button];
            }
            else
            {
                LogHandler($"Error: Unknown mouse event code: {button}");
                return;
            }
            
            // 发送输入
            if (isSimulate)
            {
                Input.sendMouseInputRelative(mouseInput);
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
            else
            {
                LogHandler("Unknown Key code: " + code);
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleKeyboardEvent: {ex.Message}");
        }
    }
}
