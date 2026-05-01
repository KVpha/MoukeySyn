using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace MouseSyncServerCore;

/// <summary>
/// 客户端PC - 优化发送队列和时间戳
/// </summary>
public class ClientPC
{
    public Connection Connection { get; private set; }
    public event EventHandler<string> onMessgeReceived;
    
    public string Name { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public TcpClient tcp { get; set; }

    private Queue<(string msg, long timestamp)> sendQueue = new();
    private readonly object queueLock = new();
    private long lastSendTime = Stopwatch.GetTimestamp();
    private int sentCount = 0;
    private int droppedCount = 0;

    public ClientPC(Connection connection, EventHandler<string> onMessageReceived)
    {
        connection.messageHander = received;
        this.onMessgeReceived = onMessageReceived;
        this.tcp = connection.TCPclient;
        this.IP = ((IPEndPoint)(tcp.Client.RemoteEndPoint)).Address.ToString();

        ServerCore.instance.addClient(this);

        Connection = connection;
        connection.onError += Connection_onError;
        connection.StartReceive();
    }

    private void Connection_onError(Exception e)
    {
        ServerCore.instance.removeClient(this);
    }

    public void received(string msg)
    {
        if (Entry.isDebug)
        {
            Console.WriteLine("Received: " + msg);
        }

        var splited = msg.Split(DataExchange.SPLIT);
        if (splited.Length > 1)
        {
            if (splited[0] == DataExchange.RESOLUTION)
            {
                Resolution = splited[1];
            }
            else if (splited[0] == DataExchange.NAME)
            {
                Name = splited[1];
            }
        }
        onMessgeReceived?.Invoke(this, msg);
    }

    /// <summary>
    /// 发送绝对鼠标坐标（桌面模式）- 即时发送
    /// </summary>
    public void sendMouse(MouseInputData e)
    {
        try
        {
            string msg = Utils.format(
                DataExchange.MOUSE,
                e.code,
                e.hookStruct.pt.X,
                e.hookStruct.pt.Y,
                e.hookStruct.mouseData
            );
            
            // 直接发送，不排队
            Connection.send(msg);
            Interlocked.Increment(ref sentCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client {IP}] Error sending mouse: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送相对鼠标位移（3D游戏模式）- 即时发送
    /// </summary>
    public void sendMouseRelative(MouseInputData e)
    {
        try
        {
            string msg = Utils.format(
                DataExchange.MOUSE_RELATIVE,
                e.code,
                e.deltaX*4,
                e.deltaY*4,
                e.hookStruct.mouseData
            );
            // 上面提高两倍偏移发送，20260502修改改善尝试
            // 鼠标事件优先发送，无延迟
            Connection.send(msg, priority: 10);
            Interlocked.Increment(ref sentCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client {IP}] Error sending mouse relative: {ex.Message}");
            Interlocked.Increment(ref droppedCount);
        }
    }

    /// <summary>
    /// 发送键盘事件 - 即时发送
    /// </summary>
    public void sendKeyboard(KeyboardInputData data)
    {
        try
        {
            string msg = Utils.format(
                DataExchange.KEY,
                data.code,
                data.HookStruct.vkCode,
                data.HookStruct.scanCode
            );
            
            // 键盘事件优先发送
            Connection.send(msg, priority: 10);
            Interlocked.Increment(ref sentCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client {IP}] Error sending keyboard: {ex.Message}");
            Interlocked.Increment(ref droppedCount);
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public (int sent, int dropped) GetStats()
    {
        return (sentCount, droppedCount);
    }

    public void ResetStats()
    {
        sentCount = 0;
        droppedCount = 0;
    }
}
