
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
    
    private volatile bool isRunning = true;
    private volatile bool isPause = false;

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

        // 相对模式：只使用钩子捕获，不进行采样
        if (Info.instance.UseRelativeMouseMode)
        {
            MouseHook.maxCount = 1; // 每次都发送
            LogHandler("[Server] Relative mouse mode (no sampling)");
        }
        else
        {
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
            "Relative (3D Game Mode - Buffered & Smoothed)" : 
            "Absolute (Desktop Mode)";
        LogHandler($"Mouse Mode: {mouseMode}");

        LogHandler("----------Server is Ready----------");
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
        if (isPause) return;

        try
        {
            // 在相对模式下，需要发送所有鼠标事件
            // 在绝对模式下，通过采样率过滤
            
            for(int i = clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Info.instance.UseRelativeMouseMode)
                    {
                        // 相对模式：转发原始鼠标事件
                        clients[i].sendMouseRelative(e);
                    }
                    else
                    {
                        // 绝对模式：转发绝对坐标
                        clients[i].sendMouse(e);
                    }
                }
                catch (Exception ex)
                {
                    LogHandler($"Error sending to client: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogHandler($"Error in mouseHandler: {ex.Message}");
        }
    }

    public void Stop()
    {
        isRunning = false;
        connectionServer.close();
        Window.Destroy();
    }
}
