
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

        MouseHook.maxCount = Info.instance.UseRelativeMouseMode ? 1 : Info.instance.MouseMovingRate;
        ClientRemove += ServerCore_ClientRemove;
        LogHandler($"Local IPv4 is [{string.Join(", ", Networks.Ipv4s)}]");
        
        if (Networks.IsSupportIPv6)
        {
            LogHandler($"Local IPv6 is [{string.Join(", ", Networks.Ipv6s)}]");
        }
        else
        {
            LogHandler("Your network does NOT support IPv6");
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
        
        // 显示当前鼠标模式
        string mouseMode = Info.instance.UseRelativeMouseMode ? 
            "Relative (3D Game Mode - Driver Level)" : 
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
            if (e.HookStruct.vkCode == 161 || e.HookStruct.vkCode == 160)//shift
            {
                hotkeyManager.setState(1, e.code == 256);
            }
            if (e.HookStruct.vkCode == 119)//F8
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
            for(int i = clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Info.instance.UseRelativeMouseMode)
                    {
                        // 相对鼠标模式：直接转发原始数据
                        // 不进行任何处理，确保准确性
                        clients[i].sendMouseRelative(e);
                    }
                    else
                    {
                        // 绝对坐标模式
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
