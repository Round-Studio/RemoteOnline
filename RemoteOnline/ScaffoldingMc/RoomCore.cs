using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteOnline.Enum;

namespace RemoteOnline.ScaffoldingMc;

public class RoomCore
{
    public string EasyTierPath { get; set; }
    public string EasyTierCliPath { get; set; }
    public string RoomCode { get; private set; }
    public bool IsRunning { get; private set; } = false;
    public ServiceStatus ServiceType { get; private set; }
    public string HostHomeName { get; private set; }
    public int ScfServerPort { get; private set; } = new Random().Next(10000, 60000);
    public Process EasytierProcess { get; private set; }
    public string HostPlayerName { get; set; }
    public string HostMachineId { get; set; }
    private RoomScfService ServerService { get; set; }
    private RoomScfClient ClientService { get; set; }

    public RoomCore(ServiceStatus serviceType)
    {
        ServiceType = serviceType;
    }

    public async Task CreateRoom(int gamePort)
    {
        RoomCode = ScaffoldingMc.RoomCode.GenerateCode().FullCode;
        HostHomeName = $"scaffolding-mc-server-{ScfServerPort}";
        ServiceType = ServiceStatus.Server;

        ServerService = new RoomScfService(ScfServerPort);
        ServerService.MinecraftServerPort = (ushort)gamePort;
        ServerService.IsMinecraftServerRunning = true;
        ServerService.SetHostPlayer(HostPlayerName, HostMachineId);
        ServerService.StartAsync();

        var codeBody = new RoomCode(RoomCode);

        EasytierProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierPath,
                Arguments =
                    $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" --hostname \"{HostHomeName}\" -p tcp://public.easytier.top:11010 --ipv4 10.144.144.1 --no-tun --compression=zstd --multi-thread --latency-first --enable-kcp-proxy",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        EasytierProcess.Start();

        IsRunning = true;
        Console.WriteLine($@"联机码：{RoomCode}");

        EasytierProcess.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
        EasytierProcess.BeginOutputReadLine();
        EasytierProcess.BeginErrorReadLine();
        EasytierProcess.WaitForExit();
    }

    // 使用锁防止重复执行
    private readonly object _setupLock = new object();
    private bool _isScfClientSetup = false;

    public async Task ConnectRoom(string code)
    {
        RoomCode = code;
        ServiceType = ServiceStatus.Client;

        var codeBody = new RoomCode(RoomCode);

        // 第一步：启动基础 EasyTier 连接
        await StartEasyTierConnection(codeBody);

        // 第二步：等待网络连接并获取主机信息
        await WaitForHostConnection();

        // 第三步：重启 EasyTier 并设置端口转发
        await RestartEasyTierWithPortForwarding(codeBody);

        // 第四步：连接 SCF 客户端服务
        await ConnectScfClientService();
    }

    private async Task StartEasyTierConnection(RoomCode codeBody)
    {
        Console.WriteLine(@"启动 EasyTier 基础连接...");

        // 先清理可能存在的旧进程
        await StopEasyTierProcess();

        EasytierProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierPath,
                Arguments =
                    $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" -p tcp://public.easytier.top:11010 --no-tun -r 0.0.0.0:18917",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        EasytierProcess.Start();
        IsRunning = true;

        Console.WriteLine(@"EasyTier 基础连接已启动");
    }

    private async Task WaitForHostConnection()
    {
        Console.WriteLine(@"等待主机连接...");

        var hostFoundTaskCompletionSource = new TaskCompletionSource<bool>();
        var timeoutTask = Task.Delay(30000); // 30秒超时

        void OnOutputDataReceived(object sender, DataReceivedEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine(args.Data);

                // 检测到服务器节点
                if (args.Data.Contains("new peer added"))
                {
                    Console.WriteLine(@"检测到服务器节点，开始解析主机信息...");
                    
                    // 解析主机名称
                    try
                    {
                        HostHomeName = ExtractHostNameFromOutput(args.Data);
                        if (!string.IsNullOrEmpty(HostHomeName))
                        {
                            Console.WriteLine($@"发现主机: {HostHomeName}");
                            hostFoundTaskCompletionSource.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($@"解析主机信息失败: {ex.Message}");
                    }
                }
            }
        }

        EasytierProcess.OutputDataReceived += OnOutputDataReceived;
        EasytierProcess.BeginOutputReadLine();
        EasytierProcess.BeginErrorReadLine();

        // 等待主机连接或超时
        var completedTask = await Task.WhenAny(hostFoundTaskCompletionSource.Task, timeoutTask);

        // 移除事件处理器
        EasytierProcess.OutputDataReceived -= OnOutputDataReceived;

        if (completedTask == timeoutTask)
        {
            throw new Exception("等待主机连接超时");
        }

        if (string.IsNullOrEmpty(HostHomeName))
        {
            throw new Exception("无法获取主机信息");
        }

        // 给网络一点时间稳定
        await Task.Delay(3000);
    }

    private async Task RestartEasyTierWithPortForwarding(RoomCode codeBody)
    {
        Console.WriteLine(@"重启 EasyTier 并设置端口转发...");

        // 获取 SCF 服务器端口
        int scfPort = GetHostNamePort();
        Console.WriteLine($@"SCF 服务器端口: {scfPort}");

        // 停止当前进程
        await StopEasyTierProcess();

        // 重新启动 EasyTier，包含端口转发参数
        EasytierProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierPath,
                Arguments =
                    $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" -p tcp://public.easytier.top:11010 --no-tun -r 0.0.0.0:18917 --port-forward tcp 127.0.0.1:{ScfServerPort} 10.144.144.1:{scfPort}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        EasytierProcess.Start();
        IsRunning = true;

        Console.WriteLine($@"EasyTier 已重启，端口转发: 127.0.0.1:{ScfServerPort} -> 10.144.144.1:{scfPort}");

        // 等待进程启动和网络稳定
        await Task.Delay(20000);

        // 验证端口转发是否工作
        if (!await TestLocalPortConnection())
        {
            throw new Exception("端口转发验证失败");
        }

        Console.WriteLine(@"端口转发验证成功");
    }

    private async Task<bool> TestLocalPortConnection()
    {
        try
        {
            Console.WriteLine($@"测试本地端口连接: 127.0.0.1:{ScfServerPort}");
            
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", ScfServerPort);
            var timeoutTask = Task.Delay(5000);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Console.WriteLine(@"本地端口连接超时");
                return false;
            }
            
            await connectTask;
            
            if (client.Connected)
            {
                Console.WriteLine(@"本地端口连接成功");
                client.Close();
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"本地端口连接异常: {ex.Message}");
            return false;
        }
    }

    private async Task ConnectScfClientService()
    {
        var maxRetries = 3;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Console.WriteLine($@"尝试连接 SCF 服务 (尝试 {attempt}/{maxRetries}): 127.0.0.1:{ScfServerPort}");

                ClientService = new RoomScfClient("Dime");

                var (success, minecraftPort, players) = 
                    await ClientService.ExecuteStandardWorkflowAsync(IPAddress.Parse("127.0.0.1"), (ushort)ScfServerPort);

                if (success)
                {
                    Console.WriteLine($@"SCF 客户端连接成功! Minecraft 服务器端口: {minecraftPort}");
                    if (players != null && players.Count > 0)
                    {
                        Console.WriteLine($@"在线玩家: {string.Join(", ", players.Select(p => p))}");
                    }

                    // 启动输出监控
                    EasytierProcess.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
                    EasytierProcess.BeginOutputReadLine();
                    EasytierProcess.BeginErrorReadLine();

                    return;
                }
                else
                {
                    Console.WriteLine($@"第 {attempt} 次连接失败");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(3000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"第 {attempt} 次连接异常: {ex.GetType().Name}: {ex.Message}");
                if (attempt < maxRetries)
                {
                    await Task.Delay(3000);
                }
                else
                {
                    throw new Exception($"经过 {maxRetries} 次尝试后仍无法连接 SCF 服务: {ex.Message}");
                }
            }
        }
    }

    private string ExtractHostNameFromOutput(string output)
    {
        // 从日志输出中提取主机名
        // 示例日志: "new peer added. peer_id: 123456, hostname: scaffolding-mc-server-13448"
        
        var match = System.Text.RegularExpressions.Regex.Match(output, @"scaffolding-mc-server-\d+");
        if (match.Success)
        {
            return match.Value;
        }

        // 如果正则匹配失败，尝试从对等节点信息中获取
        try
        {
            var peerOutput = RunCliCommand("peer");
            var peerLines = peerOutput.Split('\n');
            
            var hostLine = peerLines.FirstOrDefault(x =>
                x.Contains("scaffolding-mc-server") && !x.Contains("offline"));

            if (!string.IsNullOrEmpty(hostLine))
            {
                var parts = hostLine.Split('|');
                if (parts.Length >= 3)
                {
                    var hostName = parts[2].Trim();
                    if (!string.IsNullOrEmpty(hostName) && hostName.StartsWith("scaffolding-mc-server"))
                    {
                        return hostName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"从对等节点信息获取主机名失败: {ex.Message}");
        }

        return null;
    }

    private async Task StopEasyTierProcess()
    {
        if (EasytierProcess != null && !EasytierProcess.HasExited)
        {
            try
            {
                EasytierProcess.Kill();
                await EasytierProcess.WaitForExitAsync();
                await Task.Delay(1000); // 确保进程完全退出
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"停止 EasyTier 进程时出错: {ex.Message}");
            }
        }

        // 清理可能残留的进程
        foreach (var process in Process.GetProcessesByName("easytier-core"))
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    private int GetHostNamePort()
    {
        try
        {
            var parts = HostHomeName.Split('-');
            if (parts.Length >= 4 && int.TryParse(parts[3], out int port))
            {
                return port;
            }

            throw new FormatException("主机名格式不正确");
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"解析主机端口失败: {ex.Message}");
            throw new Exception($"无法从主机名 {HostHomeName} 解析端口");
        }
    }

    private string RunCliCommand(string command)
    {
        if (!IsRunning) throw new NullReferenceException("请确保房间开启");
        
        var proc = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierCliPath,
                Arguments = $"-p 127.0.0.1:18917 {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        proc.Start();

        var sb = new StringBuilder();
        proc.OutputDataReceived += (sender, args) => { sb.AppendLine(args.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return sb.ToString();
    }
}