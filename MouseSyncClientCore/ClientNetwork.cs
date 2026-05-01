using CommonLib;
using MouseSyncClientCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

/// <summary>
/// 客户端网络 - 超低延迟优化版本
/// 支持相对/绝对鼠标模式和动态平滑
/// </summary>
public class ClientNetwork
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public static LogHandler LogHandler { set; get; } = Console.WriteLine;

    public Connection Connection { get; private set; }
    public event EventHandler onLoaded = (s, b) => LogHandler("[✓] Connected to server successfully");

    private MouseInputBuffer mouseBuffer;
    private Thread smoothingThread;
    private volatile bool isRunning = true;
    private volatile bool isInRelativeMode = false;
    private Stopwatch performanceTimer = Stopwatch.StartNew();
    private long mouseDeltasReceived = 0;
    private long mouseDelatasApplied = 0;

    public ClientNetwork(in string ip, in int port)
    {
        isInRelativeMode = Info.instance.UseRelativeMouseMode;

        if (isInRelativeMode)
        {
            // 相对模式：初始化虚拟鼠标和缓冲
            VirtualMouseDevice.Initialize();
            mouseBuffer = new MouseInputBuffer();

            // 启动动态平滑线程
            smoothingThread = new Thread(SmoothingThreadProc)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            smoothingThread.Start();

            LogHandler("[✓] Relative mouse mode (ultra-low latency with buffering)");
        }

        Connection = Connection.connect(ip, port, receive);
        Connection.onError += Connection_onError;

        Connection.send("ok");
        sendPairData(DataExchange.RESOLUTION, Device.Resolution);
        sendPairData(DataExchange.NAME, Device.MachineName);
        Connection.StartReceive();
        
        onLoaded?.Invoke(this, EventArgs.Empty);

        int result = Connection.receiveTask.Result;
        if (result == 0)
        {
            throw new Exception("Connection cancelled by user");
        }
    }

    /// <summary>
    /// 动态平滑线程 - 自适应帧率
    /// </summary>
    private void SmoothingThreadProc()
    {
        const int MIN_FRAME_TIME = 4;  // 最低 4ms (250fps)
        const int MAX_FRAME_TIME = 16; // 最高 16ms (62fps)
        const int ADAPTIVE_THRESHOLD = 3; // 缓冲大小阈值

        int currentFrameTime = 8; // 初始 8ms (125fps)
        Stopwatch frameTimer = Stopwatch.StartNew();

        try
        {
            while (isRunning)
            {
                long frameStart = frameTimer.ElapsedMilliseconds;

                // 获取平滑后的鼠标增量
                if (mouseBuffer.GetSmoothDelta(out int deltaX, out int deltaY))
                {
                    if (isSimulate)
                    {
                        // 发送到虚拟鼠标设备
                        VirtualMouseDevice.SendRelativeMouseMove(deltaX, deltaY);
                        Interlocked.Increment(ref mouseDelatasApplied);

                        if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
                        {
                            LogHandler($"[SMOOTH] deltaX={deltaX:+0;-0;0}, deltaY={deltaY:+0;-0;0}, bufSize={mouseBuffer.GetBufferSize()}");
                        }
                    }
                }

                // 自适应帧率：根据缓冲大小调整
                int bufferSize = mouseBuffer.GetBufferSize();
                if (bufferSize > ADAPTIVE_THRESHOLD)
                {
                    // 缓冲积压 - 加快处理
                    currentFrameTime = Math.Max(MIN_FRAME_TIME, currentFrameTime - 1);
                }
                else if (bufferSize == 0)
                {
                    // 缓冲为空 - 放慢处理以减少空转
                    currentFrameTime = Math.Min(MAX_FRAME_TIME, currentFrameTime + 1);
                }

                // 维持恒定帧率
                long elapsed = frameTimer.ElapsedMilliseconds - frameStart;
                int sleepTime = (int)(currentFrameTime - elapsed);
                if (sleepTime > 0)
                {
                    Thread.Sleep(sleepTime);
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"[!] Smoothing thread error: {ex.Message}");
        }
    }

    private void Connection_onError(Exception e)
    {
        isRunning = false;
        LogHandler("[!] Connection interrupted, disconnected from server\n");
        throw e;
    }

    private void sendPairData(string prefix, string content)
    {
        Connection.send($"{prefix}{DataExchange.SPLIT}{content}");
    }

    private void receive(string msg)
    {
        if (Programe.isDebug)
        {
            LogHandler("Received: " + msg);
        }

        var splited = msg.Split(DataExchange.SPLIT);

        try
        {
            if (splited[0] == DataExchange.MOUSE)
            {
                handleMouseEvent(splited);
            }
            else if (splited[0] == DataExchange.MOUSE_RELATIVE)
            {
                handleMouseEventRelative(splited);
                Interlocked.Increment(ref mouseDeltasReceived);
            }
            else if (splited[0] == DataExchange.KEY)
            {
                handleKeyboardEvent(splited);
            }
        }
        catch (Exception e)
        {
            LogHandler($"[!] Parse error: {e.Message}");
        }
    }

    public static bool isSimulate = true;

    /// <summary>
    /// 处理绝对鼠标坐标（桌面模式）
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

            if (button == (int)MouseMessagesHook.WM_MOUSEWHEEL)
            {
                if (Programe.isDebug)
                {
                    LogHandler("[MOUSE] Wheel");
                }
                mouseInput.dwFlags = MOUSEEVENTF.MOUSEEVENTF_WHEEL;
                mouseInput.mouseData = mouseData >> 16;
            }
            else if (DataExchange.MOUSE_KEY_MAP.ContainsKey(button))
            {
                if (Programe.isDebug)
                {
                    LogHandler("[MOUSE] Button");
                }
                mouseInput.dwFlags = DataExchange.MOUSE_KEY_MAP[button];
            }
            else
            {
                LogHandler($"[!] Unknown mouse button: {button}");
                return;
            }

            if (isSimulate)
            {
                Input.sendMouseInput(mouseInput);
            }
        }
        catch (Exception ex)
        {
            LogHandler($"[!] Mouse event error: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理相对鼠标位移（3D游戏模式 - 缓冲+平滑）
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
                    // 相对移动 - 添加到缓冲，由平滑线程处理
                    if (isInRelativeMode && isSimulate)
                    {
                        mouseBuffer.AddMouseDelta(deltaX, deltaY);

                        if (Programe.isDebug && (deltaX != 0 || deltaY != 0))
                        {
                            LogHandler($"[BUFFER] Added: deltaX={deltaX:+0;-0;0}, deltaY={deltaY:+0;-0;0}");
                        }
                    }
                    break;

                case MouseMessagesHook.WM_MOUSEWHEEL:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseWheel((short)(mouseData >> 16));
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0002);
                    }
                    break;

                case MouseMessagesHook.WM_LBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0004);
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0008);
                    }
                    break;

                case MouseMessagesHook.WM_RBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0010);
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONDOWN:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0020);
                    }
                    break;

                case MouseMessagesHook.WM_MBUTTONUP:
                    if (isSimulate)
                    {
                        VirtualMouseDevice.SendMouseButton(0x0040);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            LogHandler($"[!] Relative mouse error: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理键盘事件
    /// </summary>
    private void handleKeyboardEvent(string[] msg)
    {
        try
        {
            int code = int.Parse(msg[1]);
            var vkcode = ushort.Parse(msg[2]);
            var scanCode = ushort.Parse(msg[3]);

            if (DataExchange.KEYEVENT_MAP.ContainsKey(code))
            {
                if (Programe.isDebug)
                {
                    LogHandler($"[KEY] vkCode={vkcode}");
                }

                var flag = DataExchange.KEYEVENT_MAP[code];
                var input = new Input.KEYBDINPUT()
                {
                    wVk = vkcode,
                    wScan = scanCode,
                    dwFlags = (uint)flag
                };

                if (isSimulate)
                {
                    Input.sendKeyboardInput(input);
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"[!] Keyboard event error: {ex.Message}");
        }
    }
}
