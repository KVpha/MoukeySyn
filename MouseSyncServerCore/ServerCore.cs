
using CommonLib;
using WindowsHID;

namespace MouseSyncServerCore;

public class ServerCore
{
    public LogHandler LogHandler { get; set; } = Console.WriteLine;
    public ConnectionServer connectionServer;
    int port = Info.instance.Server_Port;
    public static ServerCore instance;
    Thread broadcastThread;
    Thread rawInputThread;
    
    private volatile bool isRunning = true;
    private IntPtr rawInputWindow = IntPtr.Zero;

    public ServerCore(LogHandler logHandler)
    {
        this.LogHandler = logHandler;
        hotkeyManager = new(2, switchPause);
        instance = this;

        ConnectionHandler handler = conn =>
        {
            var c = new ClientPC(conn, (s, b) =>
            {
                ClientPC c = (ClientPC)s;

                if ((!string.IsNullOrEmpty(c.Name)) && (!string.IsNullOrEmpty(c.Resolution)))
                {
                    printTable();
                    LogHandler($"{clients.Count}\t\t\t{c.Name}\t\t{c.Resolution}\t{c.IP}");
                }
            });
        };

        connectionServer = new(port, handler, Networks.IsSupportIPv6);
        LogHandler($"Listening on port {port}");

        connectionServer.OnError += e => Console.Error.WriteLine(e.ToString());

        if (Info.instance.UseRelativeMouseMode)
        {
            // 相对模式：使用Raw Input捕获
            LogHandler("Initializing Raw Input for 3D game mode...");
            InitializeRawInput();
        }
        else
        {
            // 绝对模式：使用传统钩子
            MouseHook.maxCount = Info.instance.MouseMovingRate;
        }

        ClientRemove += ServerCore_ClientRemove;
        LogHandler($"Local IPv4 is [{string.Join(", ", Networks.Ipv4s)}]");
        
        if (Networks.IsSupportIPv6)
        {
            LogHandler($"Local IPv6 is [{string.Join(", ", Networks.Ipv6s)}]");
        }
        
        if (Info.instance.IsEnableBroadcast)
        {
            LogHandler("Starting Broadcast");
            broadcastThread = new(() => {
                while (isRunning)
                {
                    try
                    {
                        if (Networks.broadcast())
                        {
                            if (Entry.isDebug)
                            {
                                LogHandler("Broadcasting successful");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHandler($"Broadcast error: {ex.Message}");
                    }
                    Thread.Sleep(2000);
                }
            })
            { IsBackground = true };
            broadcastThread.Start();
        }
        
        string mouseMode = Info.instance.UseRelativeMouseMode ? 
            "Relative (3D Game Mode - Raw Input)" : 
            "Absolute (Desktop Mode)";
        LogHandler($"Mouse Mode: {mouseMode}");

        LogHandler("----------Server is Ready----------");
    }

    private void InitializeRawInput()
    {
        try
        {
            // 启动Raw Input线程
            rawInputThread = new(RawInputThreadProc)
            {
                IsBackground = true
            };
            rawInputThread.Start();
        }
        catch (Exception ex)
        {
            LogHandler($"Raw Input initialization error: {ex.Message}");
        }
    }

    private void RawInputThreadProc()
    {
        try
        {
            // 创建消息窗口用于接收Raw Input消息
            // 这是必需的，因为Raw Input通过WM_INPUT消息传递
            var messageWindow = new RawInputMessageWindow();
            
            // 订阅Raw Input事件
            RawInputCapture.RawMouseMoved += (s, e) =>
            {
                if (isPause) return;
                
                // 创建虚拟MouseInputData对象
                var mouseData = new MouseInputData
                {
                    code = (int)MouseMessagesHook.WM_MOUSEMOVE,
                    hookStruct = new MSLLHOOKSTRUCT(),
                    deltaX = e.DeltaX,
                    deltaY = e.DeltaY
                };

                // 广播到所有客户端
                lock (globalLock)
                {
                    for (int i = clients.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            clients[i].sendMouseRelative(mouseData);
                        }
                        catch (Exception ex)
                        {
                            LogHandler($"Error sending mouse event: {ex.Message}");
                        }
                    }
                }
            };

            RawInputCapture.RawMouseButtonChanged += (s, e) =>
            {
                if (isPause) return;

                // 处理按钮事件
                uint buttonFlags = e.ButtonFlags;
                int code = 0;

                if ((buttonFlags & 0x0001) != 0) code = (int)MouseMessagesHook.WM_LBUTTONDOWN;
                else if ((buttonFlags & 0x0002) != 0) code = (int)MouseMessagesHook.WM_LBUTTONUP;
                else if ((buttonFlags & 0x0004) != 0) code = (int)MouseMessagesHook.WM_RBUTTONDOWN;
                else if ((buttonFlags & 0x0008) != 0) code = (int)MouseMessagesHook.WM_RBUTTONUP;
                else if ((buttonFlags & 0x0400) != 0) code = (int)MouseMessagesHook.WM_MOUSEWHEEL;

                if (code != 0)
                {
                    var mouseData = new MouseInputData
                    {
                        code = code,
                        hookStruct = new MSLLHOOKSTRUCT(),
                        deltaX = 0,
                        deltaY = 0
                    };

                    lock (globalLock)
                    {
                        for (int i = clients.Count - 1; i >= 0; i--)
                        {
                            try
                            {
                                clients[i].sendMouseRelative(mouseData);
                            }
                            catch (Exception ex)
                            {
                                LogHandler($"Error sending button event: {ex.Message}");
                            }
                        }
                    }
                }
            };

            // 保持线程运行
            while (isRunning)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Raw Input thread error: {ex.Message}");
        }
    }

    public ServerCore():this(Console.WriteLine) { }

    private void printTable()
    {
        LogHandler("\nConnected Devices\tMachine Name\tResolution\tIP Address");
    }

    public static void start()
    {
        if(ServerCore.instance != null)
        {
            throw new Exception("Unable to instance it twice");
        }
        new ServerCore();
    }

    public static void wait()
    {
        ServerCore.instance.connectionServer.thread.Join();
    }

    private void ServerCore_ClientRemove(object? sender, ClientPC e)
    {
        LogHandler($"Device Offline: {e.Name}");
        LogHandler($"{clients.Count} devices still connected");
    }

    List<ClientPC> clients = new List<ClientPC>();
    public readonly object globalLock = new();
    public event EventHandler<ClientPC> ClientAdd = (s, e) => { };
    public event EventHandler<ClientPC> ClientRemove;

    public void addClient(ClientPC pc)
    {
        lock (globalLock)
        {
            clients.Add(pc);
        }
        ClientAdd.Invoke(this, pc);
    }

    public void removeClient(ClientPC pc)
    {
        lock (globalLock)
        {
            clients.Remove(pc);
        }
        ClientRemove.Invoke(this, pc);
    }

    bool isPause = false;
    
    private void switchPause()
    {
        isPause = !isPause;
        Console.WriteLine((isPause ? "--Paused--" : "--Continuing--") + "----------Press Shift+F8 to change state");
    }

    HotkeyManager hotkeyManager;

    public void keyHandler(object? sender, KeyboardInputData e)
    {
        if (Entry.isDebug)
        {
            Console.WriteLine(e.HookStruct.vkCode+" "+e.code);
        }

        if (!isPause)
        {
            foreach (ClientPC pc in clients)
            {
                pc.sendKeyboard(e);
            }
        }

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

    public void mouseHandler(object? sender, MouseInputData e)
    {
        // 该方法仅在绝对模式下使用
        // 相对模式使用Raw Input，不调用此方法
        if (isPause) return;

        for(int i = clients.Count - 1; i >= 0; i--)
        {
            try
            {
                clients[i].sendMouse(e);
            }
            catch (Exception ex)
            {
                LogHandler($"Error: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        isRunning = false;
        RawInputCapture.Cleanup();
        connectionServer.close();
        Window.Destroy();
    }
}

/// <summary>
/// Raw Input消息窗口
/// 用于接收和处理WM_INPUT消息
/// </summary>
public class RawInputMessageWindow : System.Windows.Forms.Form
{
    public RawInputMessageWindow()
    {
        this.Text = "Raw Input Message Window";
        this.Width = 0;
        this.Height = 0;
        this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        this.Location = new System.Drawing.Point(-32000, -32000);
        
        RawInputCapture.Initialize(this.Handle);
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        const int WM_INPUT = 0x00FF;
        
        if (m.Msg == WM_INPUT)
        {
            RawInputCapture.ProcessRawInput(m.LParam);
        }

        base.WndProc(ref m);
    }
}
