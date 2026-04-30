using System.Net;
using System.Net.Sockets;

namespace MouseSyncServerCore;

public class ClientPC
{
    public Connection Connection { get; private set; }
    public event EventHandler<string> onMessgeReceived;
/*    public ClientPC(string name, string resolution, string iP)
    {
        Console.Error.WriteLine("ONLY FOR DEBUG");
        //only for test
        Name = name;
        Resolution = resolution;
        IP = iP;
        ServerCore.instance.addClient(this);
        this.Connection = new ConnectionForDemo();
    }*/
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
        //Console.WriteLine("Lost Connection"+e.ToString());
        ServerCore.instance.removeClient(this);
    }


    //this is a call back
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
    
    // 发送绝对鼠标坐标
    public void sendMouse(MouseInputData e)
    {
        //Connection.send($"{e.pt.X}:{e.pt.Y}:{e.flags}:{e}");
        Connection.send(Utils.format(
            DataExchange.MOUSE,
            e.code,
            e.hookStruct.pt.X,
            e.hookStruct.pt.Y,
            e.hookStruct.mouseData
            ));
    }
    
    // 发送相对鼠标位移（用于3D游戏）
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
    
    /// <summary>
    /// 发送鼠标位置校准信息（服务端鼠标位置同步到客户端）
    /// 用于3D游戏模式下定期校准鼠标绝对位置，防止位置漂移
    /// </summary>
    public void sendMouseCalibration(int x, int y)
    {
        Connection.send(Utils.format(
            DataExchange.MOUSE_CALIBRATE,
            x,
            y
            ));
    }
    
    public void sendKeyboard(KeyboardInputData data)
    {
        Connection.send(Utils.format(
            DataExchange.KEY,
            data.code,//key down or up
            data.HookStruct.vkCode,
            data.HookStruct.scanCode
            ));
    }

    public string Name { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public TcpClient tcp { get; set; }

}
/*public class ConnectionForDemo : Connection
{


    public ConnectionForDemo(TcpClient client = null, MessageHander hander = null)
    {
        Console.Error.WriteLine("This Class is only for test");
    }

    public override  void send(string sb)
    {
        Console.WriteLine("Sending:   " + sb);
    }
}*/
