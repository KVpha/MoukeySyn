using CommonLib;
using System.Diagnostics;
using WindowsHID;

namespace MouseSyncServerCore;

public delegate void LogHandler(string message);

/// <summary>
/// 服务器核心 - 超低延迟优化版本
/// </summary>
public class ServerCore
{
    public LogHandler LogHandler { get; set; } = Console.WriteLine;
    public ConnectionServer connectionServer;
    private int port = Info.instance.Server_Port;
    public static ServerCore instance;
    private Thread broadcastThread;
    private HotkeyManager hotkeyManager;

    private List<ClientPC> clients = new();
    public readonly object globalLock = new();
    public event EventHandler<ClientPC> ClientAdd = (s, e) => { };
    public event EventHandler<ClientPC> ClientRemove;

    private volatile bool isRunning = true;
    private volatile bool isPause = false;

    // 性能监控
    private Stopwatch perfTimer = Stopwatch.StartNew();
    private long mouseEventsReceived = 0;
    private long keyEventsReceived = 0;
    private long mouseEventsSent = 0;

    public ServerCore(LogHandler logHandler)
    {
        this.LogHandler = logHandler;
        hotkeyManager = new(2, switchPause);
        instance = this;

        // 连接处理器
        ConnectionHandler handler = conn =>
        {
            var clientPC = new ClientPC(conn, (s, e) =>
            {
                if (s is ClientPC pc && (!string.IsNullOrEmpty(pc.Name)) && (!string.IsNullOrEmpty(pc.Resolution)))
                {
                    printTable();
                    LogHandler($"[CLIENT] {clients.Count}\t{pc.Name}\t{pc.Resolution}\t{pc.IP}");
                }
            });
        };

        connectionServer = new(port, handler, Networks.IsSupportIPv6);
        LogHandler($"[✓] Server listening on port {port}");
        connectionServer.OnError += e => LogHandler($"[!] Connection error: {e.Message}");

        // 优化：禁用采样，所有事件立即发送
        MouseHook.maxCount = 1;

        ClientRemove += ServerCore_ClientRemove;
        LogHandler($"[✓] IPv4: [{string.Join(", ", Networks.Ipv4s)}]");

        if (Networks.IsSupportIPv6)
        {
            LogHandler($"[✓] IPv6: [{string.Join(", ", Networks.Ipv6s)}]");
        }

        // 启动广播
        if (Info.instance.IsEnableBroadcast)
        {
            LogHandler("[→] Starting broadcast service...");
            broadcastThread = new(() =>
            {
                while (isRunning)
                {
                    try
                    {
                        Networks.broadcast();
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        LogHandler($"[!] Broadcast error: {ex.Message}");
                    }
                }
            })
            { IsBackground = true };
            broadcastThread.Start();
        }

        // 鼠标模式
        string mouseMode = Info.instance.UseRelativeMouseMode
            ? "Relative (3D Game Mode)"
            : "Absolute (Desktop Mode)";
        LogHandler($"[⌨] Mouse Mode: {mouseMode}");

        // 启动性能监控线程
        StartPerformanceMonitor();

        // 注册钩子回调
        MouseHook.addCallback(mouseHandler);
        KeyboardHook.addCallback(keyHandler);

        LogHandler("╔══════════════════════════════════╗");
        LogHandler("║  MouseSync Server Ready (v2.0)  ║");
        LogHandler("║  Ultra-Low Latency Mode          ║");
        LogHandler("╚══════════════════════════════════╝");
    }

    public ServerCore() : this(Console.WriteLine) { }

    /// <summary>
    /// 鼠标事件处理 - 立即转发
    /// </summary>
    public void mouseHandler(object? sender, MouseInputData e)
    {
        if (isPause) return;

        Interlocked.Increment(ref mouseEventsReceived);

        lock (globalLock)
        {
            for (int i = clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Info.instance.UseRelativeMouseMode)
                    {
                        // 相对模式：转发相对增量
                        clients[i].sendMouseRelative(e);
                    }
                    else
                    {
                        // 绝对模式：转发绝对坐标
                        clients[i].sendMouse(e);
                    }
                    Interlocked.Increment(ref mouseEventsSent);
                }
                catch (Exception ex)
                {
                    LogHandler($"[!] Error sending to {clients[i].IP}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 键盘事件处理 - 立即转发
    /// </summary>
    public void keyHandler(object? sender, KeyboardInputData e)
    {
        if (isPause) return;

        Interlocked.Increment(ref keyEventsReceived);

        if (Entry.isDebug)
        {
            Console.WriteLine($"[KEY] vkCode={e.HookStruct.vkCode}, code={e.code}");
        }

        lock (globalLock)
        {
            foreach (ClientPC pc in clients)
            {
                try
                {
                    pc.sendKeyboard(e);
                }
                catch (Exception ex)
                {
                    LogHandler($"[!] Error sending keyboard to {pc.IP}: {ex.Message}");
                }
            }
        }

        // 处理热键
        if (Info.instance.IsEnableHotKey)
        {
            if (e.HookStruct.vkCode == 161 || e.HookStruct.vkCode == 160)
            {
                hotkeyManager.setState(1, e.code == 256);
            }
            if (e.HookStruct.vkCode == 119)
            {
                hotkeyManager.setState(0, e.code == 256);
            }
        }
    }

    /// <summary>
    /// 性能监控线程
    /// </summary>
    private void StartPerformanceMonitor()
    {
        new Thread(() =>
        {
            while (isRunning)
            {
                Thread.Sleep(10000); // 每10秒统计一次

                long elapsed = perfTimer.ElapsedMilliseconds;
                if (elapsed > 0)
                {
                    double mouseRate = mouseEventsReceived * 1000.0 / elapsed;
                    double keyRate = keyEventsReceived * 1000.0 / elapsed;
                    
                    LogHandler($"[📊] Performance: Mouse={mouseRate:F1} evt/s | Keyboard={keyRate:F1} evt/s | Clients={clients.Count}");
                }

                perfTimer.Restart();
                mouseEventsReceived = 0;
                keyEventsReceived = 0;
            }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
    }

    private void printTable()
    {
        LogHandler("\n═══════════════════════════════════════════════");
        LogHandler("║ Connected Devices | Name | Resolution | IP");
        LogHandler("═══════════════════════════════════════════════");
    }

    public static void start()
    {
        if (ServerCore.instance != null)
        {
            throw new Exception("Server instance already exists");
        }
        new ServerCore();
    }

    public static void wait()
    {
        ServerCore.instance?.connectionServer.thread.Join();
    }

    private void ServerCore_ClientRemove(object? sender, ClientPC e)
    {
        LogHandler($"[↓] Device disconnected: {e.Name} ({e.IP})");
        LogHandler($"[#] Connected: {clients.Count}");
    }

    public void addClient(ClientPC pc)
    {
        lock (globalLock)
        {
            clients.Add(pc);
        }
        ClientAdd?.Invoke(this, pc);
    }

    public void removeClient(ClientPC pc)
    {
        lock (globalLock)
        {
            clients.Remove(pc);
        }
        ClientRemove?.Invoke(this, pc);
    }

    private void switchPause()
    {
        isPause = !isPause;
        string status = isPause ? "║ ⏸  PAUSED" : "║ ▶️  RUNNING";
        LogHandler($"\n{status}  |  Press Shift+F8 to toggle");
    }

    public void Stop()
    {
        isRunning = false;
        connectionServer?.close();
        Window.Destroy();
    }
}
