
using CommonLib;
using MouseSyncClientCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    public static LogHandler LogHandler { set; get; } = Console.WriteLine;
    
    public Connection Connection { get; private set; }
    public event EventHandler onLoaded=(s,b)=>LogHandler("Connected to server successfully");
    
    private MouseInputBuffer mouseBuffer;
    private Thread smoothingThread;
    private volatile bool isRunning = true;
    private volatile bool isInRelativeMode = false;

    public ClientNetwork(in string ip,in int port) {
        isInRelativeMode = Info.instance.UseRelativeMouseMode;
        
        if (isInRelativeMode)
        {
            // 相对模式：初始化虚拟鼠标和缓冲
            VirtualMouseDevice.Initialize();
            mouseBuffer = new MouseInputBuffer();
            
            // 启动平滑处理线程
            smoothingThread = new Thread(SmoothingThreadProc)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            smoothingThread.Start();
            
            LogHandler("[Client] Relative mouse mode activated with smoothing buffer");
        }
        
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

    /// <summary>
    /// 平滑处理线程
    /// 从缓冲中读取鼠标增量并以恒定速率发送
    /// </summary>
    private void SmoothingThreadProc()
    {
        const int FRAME_INTERVAL = 8; // 约125fps (1000ms / 8ms = 125)
        
        try
        {
            while (isRunning)
            {
                long frameStart = Environment.TickCount64;
                
                // 获取平滑后的鼠标增量
                if (mouseBuffer.GetSmoothDelta(out int deltaX, out int deltaY))
                {
                    // 发送到虚拟鼠标设备
                    VirtualMouseDevice.SendRelativeMouseMove(deltaX, deltaY);
                    
                    if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
                    {
                        LogHandler($"[SMOOTH] Sent: deltaX={deltaX}, deltaY={deltaY}, bufSize={mouseBuffer.GetBufferSize()}");
                    }
                }
                
                // 维持恒定帧率
                long elapsed = Environment.TickCount64 - frameStart;
                int sleepTime = (int)(FRAME_INTERVAL - elapsed);
                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"[Smoothing] Thread error: {ex.Message}");
        }
    }

    private void Connection_onError(Exception e)
    {
        isRunning = false;
        LogHandler("Connection has been interrupted, disconnected from server\n");
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
    /// 处理相对鼠标移动（缓冲 + 平滑模式）
    /// </summary>
    private void handleMouseEventRelative(string[] msg)
    {
        try
        {
            int button = int.Parse(msg[1]);
            int deltaX = int.Parse(msg[2]);
            int deltaY = int.Parse(msg[3]);
            var mouseData = int.Parse(msg[4]);
            
            MouseMessagesHook mouseMsg = (MouseMessagesHook)button;

            switch (mouseMsg)
            {
                case MouseMessagesHook.WM_MOUSEMOVE:
                    // 相对移动 - 添加到缓冲
                    if (isInRelativeMode && isSimulate)
                    {
                        mouseBuffer.AddMouseDelta(deltaX, deltaY);
                        
                        if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
                        {
                            LogHandler($"[BUFFER] Added: deltaX={deltaX}, deltaY={deltaY}");
                        }
                    }
                    break;

                case MouseMessagesHook.WM_MOUSEWHEEL:
                    // 滚轮 - 直接发送
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseWheel((short)(mouseData >> 16));
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0002); // MOUSEEVENTF_LEFTDOWN
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0004); // MOUSEEVENTF_LEFTUP
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0008); // MOUSEEVENTF_RIGHTDOWN
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0010); // MOUSEEVENTF_RIGHTUP
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0020); // MOUSEEVENTF_MIDDLEDOWN
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0040); // MOUSEEVENTF_MIDDLEUP
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

