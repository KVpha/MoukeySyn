
using CommonLib;
using MouseSyncClientCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
    
    // 用于相对模式中保存上一次同步的绝对位置
    private int lastSyncedX = 0;
    private int lastSyncedY = 0;
    
    public ClientNetwork(in string ip,in int port) {
        //isSimulate = false;
        Connection = Connection.connect(ip,port,receive);
        Connection.onError += Connection_onError;
        //send the resolution and machine name
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
            else if (splited[0] == DataExchange.MOUSE_CALIBRATE)
            {
                handleMouseCalibration(splited);
            }
            else if (splited[0] == DataExchange.KEY)
            {
                handleKeyboardEvent(splited);
            }
        }catch(Exception e)
        {
            LogHandler($"Error Parse: {e.Message}");
        }
    
    }
    public static  bool isSimulate=true;
    
    // 处理绝对鼠标移动事件
    private void handleMouseEvent(string[] msg)
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
        else
        {
            if (Programe.isDebug)
            {
                LogHandler("simulate btn press");
            }

            if (DataExchange.MOUSE_KEY_MAP.ContainsKey(button))
            {
                mouseInput.dwFlags = DataExchange.MOUSE_KEY_MAP[button];
            }
            else
            {
                LogHandler("Error:can not prase :" + button);
            }
        }
        if (isSimulate)
        {
            Input.sendMouseInput(mouseInput);
        }
    }
    
    // 处理相对鼠标移动事件（用于3D游戏）
    private void handleMouseEventRelative(string[] msg)
    {
        try
        {
            int button = int.Parse(msg[1]);
            int deltaX = int.Parse(msg[2]);
            int deltaY = int.Parse(msg[3]);
            var mouseData = int.Parse(msg[4]);
            
            MOUSEINPUT mouseInput = new();
            mouseInput.dx = deltaX;
            mouseInput.dy = deltaY;
            mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_MOVE;

            if (button==(int)MouseMessagesHook.WM_MOUSEWHEEL) {
                if (Programe.isDebug)
                {
                    LogHandler("Simulate wheel (relative mode)");
                }
                mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_WHEEL;
                mouseInput.mouseData = mouseData>>16;
            }
            else
            {
                if (Programe.isDebug)
                {
                    LogHandler($"Mouse relative move: deltaX={deltaX}, deltaY={deltaY}");
                }
                if (DataExchange.MOUSE_KEY_MAP.ContainsKey(button))
                {
                    mouseInput.dwFlags = DataExchange.MOUSE_KEY_MAP[button];
                }
                else
                {
                    LogHandler("Error:can not parse :" + button);
                }
            }
            
            if (isSimulate)
            {
                // 发送相对移动
                Input.sendMouseInputRelative(mouseInput);
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleMouseEventRelative: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 处理鼠标位置校准（同步绝对位置）
    /// 服务端定期发送其当前鼠标位置，客户端将自己的鼠标位置同步到相同位置
    /// 用于防止相对模式下的位置漂移
    /// </summary>
    private void handleMouseCalibration(string[] msg)
    {
        try
        {
            int serverX = int.Parse(msg[1]);
            int serverY = int.Parse(msg[2]);
            
            if (Programe.isDebug)
            {
                LogHandler($"Mouse calibration received: server at ({serverX}, {serverY})");
            }
            
            // 仅在相对模式下执行校准
            if (isSimulate && Info.instance.UseRelativeMouseMode)
            {
                // 获取客户端当前鼠标位置
                GetCursorPos(out POINT clientPos);
                
                // 计算位置差异
                int deltaX = serverX - clientPos.X;
                int deltaY = serverY - clientPos.Y;
                
                // 如果位置差异超过5像素，执行校准
                if (Math.Abs(deltaX) > 5 || Math.Abs(deltaY) > 5)
                {
                    // 将客户端鼠标位置同步到服务端位置
                    SetCursorPos(serverX, serverY);
                    lastSyncedX = serverX;
                    lastSyncedY = serverY;
                    
                    if (Programe.isDebug)
                    {
                        LogHandler($"Mouse position calibrated from ({clientPos.X}, {clientPos.Y}) to ({serverX}, {serverY})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleMouseCalibration: {ex.Message}");
        }
    }
    
    private void handleKeyboardEvent(string[] msg)
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
            Input.sendKeyboardInput(input);
        }
        else
        {
            LogHandler("Unknown Key");
        }
        
    }
}
