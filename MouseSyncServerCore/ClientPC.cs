using System.Net;
using System.Net.Sockets;

namespace MouseSyncServerCore;

public class ClientPC
{
    public Connection Connection { get; private set; }
    public event EventHandler<string> onMessgeReceived;

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
        onMessgeReceived.Invoke(this, msg);
    }
    
    /// <summary>
    /// 发送绝对鼠标坐标（桌面模式）
    /// </summary>
    public void sendMouse(MouseInputData e)
    {
        Connection.send(Utils.format(
            DataExchange.MOUSE,
            e.code,
            e.hookStruct.pt.X,
            e.hookStruct.pt.Y,
            e.hookStruct.mouseData
        ));
    }
    
    /// <summary>
    /// 发送相对鼠标位移（3D游戏模式）
    /// 直接转发deltaX和deltaY给客户端
    /// </summary>
    public void sendMouseRelative(MouseInputData e)
    {
        Connection.send(Utils.format(
            DataExchange.MOUSE_RELATIVE,
            e.code,
            e.deltaX,
            e.deltaY,
            e.hookStruct.mouseData
        ));
    }
    
    public void sendKeyboard(KeyboardInputData data)
    {
        Connection.send(Utils.format(
            DataExchange.KEY,
            data.code,
            data.HookStruct.vkCode,
            data.HookStruct.scanCode
        ));
    }

    public string Name { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public TcpClient tcp { get; set; }
}
