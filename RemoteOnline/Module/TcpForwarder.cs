using System.Net;
using System.Net.Sockets;

namespace RemoteOnline.Module;

public class TcpForwarder
{
    private readonly string _localIp;
    private readonly int _localPort;
    private readonly string _remoteIp;
    private readonly int _remotePort;

    public TcpForwarder(string localIp, int localPort, string remoteIp, int remotePort)
    {
        _localIp = localIp;
        _localPort = localPort;
        _remoteIp = remoteIp;
        _remotePort = remotePort;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var localEndPoint = new IPEndPoint(IPAddress.Parse(_localIp), _localPort);
        var listener = new TcpListener(localEndPoint);

        listener.Start();
        Console.WriteLine($"端口转发服务已启动，监听 {_localIp}:{_localPort} -> {_remoteIp}:{_remotePort}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient localClient, CancellationToken cancellationToken)
    {
        TcpClient remoteClient = null;

        try
        {
            // 连接到远程服务器
            remoteClient = new TcpClient();
            await remoteClient.ConnectAsync(_remoteIp, _remotePort);

            Console.WriteLine($"客户端 {localClient.Client.RemoteEndPoint} 已连接，已转发到 {_remoteIp}:{_remotePort}");

            // 创建双向数据流
            var localStream = localClient.GetStream();
            var remoteStream = remoteClient.GetStream();

            // 启动双向转发任务
            var localToRemoteTask = CopyStreamAsync(localStream, remoteStream, cancellationToken);
            var remoteToLocalTask = CopyStreamAsync(remoteStream, localStream, cancellationToken);

            // 等待任意一个任务完成
            await Task.WhenAny(localToRemoteTask, remoteToLocalTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理客户端连接时出错: {ex.Message}");
        }
        finally
        {
            localClient.Close();
            remoteClient?.Close();
            Console.WriteLine($"客户端连接已关闭");
        }
    }

    private async Task CopyStreamAsync(NetworkStream source, NetworkStream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] buffer = new byte[4096];
            int bytesRead;

            while ((bytesRead =
                       await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // 连接关闭或出错是正常现象
            if (!(ex is OperationCanceledException))
            {
                Console.WriteLine($"数据流复制错误: {ex.Message}");
            }
        }
    }
}