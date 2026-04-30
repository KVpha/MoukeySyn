
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


public class ClientNetwork
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);
    
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    public static LogHandler LogHandler { set; get; } = Console.WriteLine;
    
    public Connection Connection { get; private set; }
    public event EventHandler onLoaded=(s,b)=>LogHandler("Connected to server successfully");
    
    // 用于校准的变量
    private int lastCalibratedX = 0;
    private int lastCalibratedY = 0;
    private long lastCalibrationTime = 0;
    private const long CALIBRATION_INTERVAL = 100; // 100毫秒校准一次
    private bool isInRelativeMode = false;
    private object calibrationLock = new object();
    
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
    // 改进版本：添加定期校准功能防止位置漂移
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
                    LogHandler("simulate btn press (relative mode)");
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
                
                // 定期校准绝对位置（每100ms）
                CalibrateMousePosition();
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in handleMouseEventRelative: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 校准鼠标绝对位置，防止位置漂移
    /// 每100毫秒执行一次，将鼠标位置锁定在中心点附近
    /// </summary>
    private void CalibrateMousePosition()
    {
        lock (calibrationLock)
        {
            long currentTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            
            // 检查是否需要校准（100ms间隔）
            if (currentTime - lastCalibrationTime >= CALIBRATION_INTERVAL)
            {
                try
                {
                    // 获取当前鼠标位置
                    GetCursorPos(out POINT currentPos);
                    
                    // 计算屏幕中心位置
                    int centerX = Device.width / 2;
                    int centerY = Device.height / 2;
                    
                    // 如果鼠标位置偏离中心太远，重新定位到中心
                    int deltaX = currentPos.X - centerX;
                    int deltaY = currentPos.Y - centerY;
                    
                    // 如果偏差超过50像素，进行校准
                    if (Math.Abs(deltaX) > 50 || Math.Abs(deltaY) > 50)
                    {
                        SetCursorPos(centerX, centerY);
                        lastCalibratedX = centerX;
                        lastCalibratedY = centerY;
                        
                        if (Programe.isDebug)
                        {
                            LogHandler($"Mouse calibrated from ({currentPos.X}, {currentPos.Y}) to ({centerX}, {centerY})");
                        }
                    }
                    
                    lastCalibrationTime = currentTime;
                }
                catch (Exception ex)
                {
                    LogHandler($"Calibration error: {ex.Message}");
                }
            }
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

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}
