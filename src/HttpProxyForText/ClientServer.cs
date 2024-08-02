using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HttpProxyForText;

public class ClientServer
{
    public int Port { get; set; }

    private Task? _serviceTask;
    // 创建 CancellationTokenSource
    private CancellationTokenSource? _cts;
    
    
    /// <summary>
    /// 开启服务
    /// </summary>
    public void Start(int port)
    {
        Port = port;
        
        if (_serviceTask != null) return;
        
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        
        _serviceTask = Task.Run(() => Service(token), token);
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    public void Stop()
    {
        if (_cts == null || _serviceTask == null) return;
        
        _cts.Cancel();
        
        _serviceTask = null;
        _cts = null;
    }
    
    
    /// <summary>
    /// 代理服务
    /// </summary>
    /// <param name="token"></param>
    private async Task Service(CancellationToken token)
    {
        TcpListener listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        Console.WriteLine($"Proxy server is listening on port {Port}...");

        while (true)
        {
            if (token.IsCancellationRequested)
            {
                Console.WriteLine("取消请求已收到，退出任务...");
                token.ThrowIfCancellationRequested();
            }
            
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client)); // 异步处理客户端连接
        }
    }

    
    /// <summary>
    /// 处理客户端
    /// </summary>
    /// <param name="client"></param>
    private static async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (NetworkStream clientStream = client.GetStream())
        {
            // 读取客户端请求
            string requestString = await ReadStreamAsync(clientStream);
            Console.WriteLine("Request:");
            Console.WriteLine(requestString);

            // 解析请求头
            string[] requestLines = requestString.Split("\r\n");
            if (requestLines.Length > 0)
            {
                string[] requestLineParts = requestLines[0].Split(' ');
                if (requestLineParts.Length >= 2)
                {
                    string method = requestLineParts[0];
                    string url = requestLineParts[1];
                    string version = requestLineParts[2];

                    Uri uri = new Uri(url);
                    string host = uri.Host;
                    int port = uri.Port;

                    // 连接目标服务器
                    using (TcpClient serverClient = new TcpClient(host, port))
                    using (NetworkStream serverStream = serverClient.GetStream())
                    {
                        // 打印并将客户端请求转发到目标服务器
                        Console.WriteLine("Forwarding request to server:");
                        Console.WriteLine(requestString); // 打印转发的请求内容

                        // 发送请求到目标服务器
                        byte[] requestBytes = Encoding.ASCII.GetBytes(requestString);
                        await serverStream.WriteAsync(requestBytes);
                        await serverStream.FlushAsync();

                        // 从目标服务器读取响应
                        string responseString = await ReadStreamAsync(serverStream);
                        Console.WriteLine("Response:");
                        Console.WriteLine(responseString);

                        // 将响应转发给客户端
                        byte[] responseBytes = Encoding.ASCII.GetBytes(responseString);
                        await clientStream.WriteAsync(responseBytes);
                        await clientStream.FlushAsync();
                    }
                }
            }
        }
    }
    

    /// <summary>
    /// 读取流异步
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    private static async Task<string> ReadStreamAsync(NetworkStream stream)
    {
        using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        {
            StringBuilder sb = new StringBuilder();
            char[] buffer = new char[1024];
            int bytesRead;

            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                sb.Append(buffer, 0, bytesRead);
                // HTTP 请求和响应通常以空行结束
                if (sb.ToString().Contains("\r\n\r\n"))
                {
                    break;
                }
            }

            return sb.ToString();
        }
    }
}