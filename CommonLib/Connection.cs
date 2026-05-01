using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CommonLib;

public class Connection
{
    public Exception? Exception { get; private set; }
    public readonly string EOF = DataExchange.EOF;
    public event onError? onError;
    
    public static int BufferSize { set; get; } = 4096; // 增大缓冲区

    public static Connection connect(string ip, int port, MessageHander hander)
    {
        TcpClient client = new TcpClient();
        client.Connect(IPAddress.Parse(ip), port);
        Connection conn = new Connection(client, hander);
        return conn;
    }

    public delegate void MessageHander(string msg);

    public readonly TcpClient TCPclient;
    private NetworkStream stream;
    public MessageHander messageHander { get; set; }

    public Connection(TcpClient client, MessageHander hander) : this(client)
    {
        this.messageHander = hander;
    }

    public Connection(TcpClient client)
    {
        this.TCPclient = client;
        stream = client.GetStream();
        
        // 优化TCP参数以降低延迟
        ConfigureTcpSocket();
    }

    /// <summary>
    /// 配置TCP套接字以实现超低延迟
    /// </summary>
    private void ConfigureTcpSocket()
    {
        try
        {
            // 1. 禁用Nagle算法 - 立即发送小数据包
            TCPclient.NoDelay = true;

            // 2. 增大缓冲区 - 减少系统调用
            TCPclient.SendBufferSize = 8192;
            TCPclient.ReceiveBufferSize = 8192;

            // 3. 设置KeepAlive - 检测连接异常
            TCPclient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // 4. 减少Send超时 - 快速失败
            TCPclient.SendTimeout = 5000;
            TCPclient.ReceiveTimeout = 5000;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Connection] Warning: {ex.Message}");
        }
    }

    public void StartReceive()
    {
        receiveTask = receive();
    }

    public void close()
    {
        try
        {
            stream?.Close();
            TCPclient?.Close();
        }
        catch { }
    }

    public Task<int> receiveTask { get; private set; }

    /// <summary>
    /// 异步发送 - 优先级无延迟
    /// </summary>
    public async void send(string msg, int priority = 0)
    {
        try
        {
            byte[] responseData = Encoding.UTF8.GetBytes(msg + EOF);
            await stream.WriteAsync(responseData, 0, responseData.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private async Task<int> receive()
    {
        try
        {
            byte[] buffer = new byte[BufferSize];
            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                
                if (bytesRead == 0)
                {
                    onError?.Invoke(new Exception("Connection closed by server"));
                    return -1;
                }

                string dataReceived = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var messages = dataReceived.Split(EOF);
                
                foreach (var msg in messages)
                {
                    if (!string.IsNullOrEmpty(msg))
                    {
                        try
                        {
                            messageHander?.Invoke(msg);
                        }
                        catch { }
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Exception = ex;
            onError?.Invoke(ex);
            return -1;
        }
    }
}

public delegate void ConnectionHandler(Connection connection);
public delegate void onError(Exception e);

/// <summary>
/// 连接服务器 - IPv4/IPv6支持
/// </summary>
public class ConnectionServer
{
    private ConnectionHandler handler;
    private TcpListener server;
    private TcpListener server_v6;
    
    public Thread thread { get; private set; }
    public Thread thread_v6 { get; private set; }

    public event onError OnError;

    public ConnectionServer(int port, ConnectionHandler handler) 
        : this(port, handler, isEnableIPv6: true) { }

    public ConnectionServer(int port, ConnectionHandler handler, bool isEnableIPv6)
    {
        this.handler = handler;
        
        if (isEnableIPv6)
        {
            try
            {
                server_v6 = new TcpListener(IPAddress.IPv6Any, port);
                server_v6.Server.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.IPv6Only,
                    false);
                server_v6.Start();
                thread_v6 = new(() => Start(server_v6, "IPv6"))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                thread_v6.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] IPv6 error: {ex.Message}");
            }
        }

        server = new TcpListener(IPAddress.Any, port);
        server.Start();
        thread = new(() => Start(server, "IPv4"))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        thread.Start();
    }

    private void Start(TcpListener listener, string addressFamily)
    {
        Console.WriteLine($"[✓] {addressFamily} server started on {listener.LocalEndpoint}");
        
        while (true)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                
                // 立即配置新连接
                client.NoDelay = true;
                client.SendBufferSize = 8192;
                client.ReceiveBufferSize = 8192;
                
                handler?.Invoke(new Connection(client));
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }
        Console.WriteLine($"[×] {addressFamily} server stopped");
    }

    public void close()
    {
        try
        {
            server?.Stop();
            server_v6?.Stop();
            thread?.Interrupt();
            thread_v6?.Interrupt();
        }
        catch { }
    }
}
