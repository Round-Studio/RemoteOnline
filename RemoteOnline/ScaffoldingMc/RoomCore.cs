using System.Diagnostics;
using System.Net;
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
                    $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" --hostname \"{HostHomeName}\" -p tcp://public.easytier.top:11010 --dhcp --ipv4 10.144.144.1 --no-tun --compression=zstd --multi-thread --latency-first --enable-kcp-proxy",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        EasytierProcess.Start();

        IsRunning = true;
        Console.WriteLine($"联机码：{RoomCode}");

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

        // 启动 EasyTier 进程
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

        EasytierProcess.OutputDataReceived += async (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine(args.Data);

                // 检测到新对等节点添加，并且是服务器节点
                if (args.Data.Contains("new peer added") && args.Data.Contains("scaffolding-mc-server"))
                {
                    // 使用锁和标志位防止重复执行
                    lock (_setupLock)
                    {
                        if (_isScfClientSetup) return;
                        _isScfClientSetup = true;
                    }

                    await Task.Delay(3000); // 给网络更多时间稳定

                    try
                    {
                        await SetupScfClient();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"设置 SCF 客户端失败: {ex.Message}");
                        // 重置标志以便重试
                        lock (_setupLock)
                        {
                            _isScfClientSetup = false;
                        }
                    }
                }
            }
        };

        EasytierProcess.BeginOutputReadLine();
        EasytierProcess.BeginErrorReadLine();

        // 启动后尝试设置 SCF 客户端
        await Task.Run(async () =>
        {
            await Task.Delay(8000); // 等待更长时间确保网络就绪

            lock (_setupLock)
            {
                if (_isScfClientSetup) return;
                _isScfClientSetup = true;
            }

            try
            {
                await SetupScfClient();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始设置 SCF 客户端失败: {ex.Message}");
                lock (_setupLock)
                {
                    _isScfClientSetup = false;
                }
            }
        });
    }

    private async Task SetupScfClient()
    {
        Console.WriteLine("开始设置 SCF 客户端...");

        // 步骤1: 获取主机名称
        if (string.IsNullOrEmpty(HostHomeName))
        {
            HostHomeName = await GetHostRoomName();
            if (string.IsNullOrEmpty(HostHomeName))
            {
                throw new Exception("无法找到主机房间名称");
            }

            Console.WriteLine($"发现主机: {HostHomeName}");
        }

        // 步骤2: 获取 SCF 服务器端口
        int scfPort = GetHostNamePort();
        Console.WriteLine($"SCF 服务器端口: {scfPort}");

        // 步骤3: 设置端口转发
        await SetupPortForwarding(scfPort);

        // 步骤4: 等待端口转发生效
        await Task.Delay(2000);

        // 步骤5: 连接 SCF 客户端服务
        await ConnectScfClientService();
    }

    private async Task<string> GetHostRoomName()
    {
        var retryCount = 0;
        const int maxRetries = 8;

        while (retryCount < maxRetries)
        {
            try
            {
                var peerOutput = RunCliCommand("peer");
                Console.WriteLine($"Peer 命令输出: {peerOutput}");

                var peerLines = peerOutput.Split('\n');

                // 查找包含服务器标识的行
                var hostLine = peerLines.FirstOrDefault(x =>
                    x.Contains("scaffolding-mc-server") &&
                    !x.Contains("offline"));

                if (!string.IsNullOrEmpty(hostLine))
                {
                    Console.WriteLine($"找到主机行: {hostLine}");

                    // 更健壮的解析逻辑
                    var parts = hostLine.Split('|');

                    if (parts.Length >= 3)
                    {
                        var hostName = parts[2].Trim();
                        if (!string.IsNullOrEmpty(hostName) && hostName.StartsWith("scaffolding-mc-server"))
                        {
                            return hostName;
                        }
                    }

                    // 备用解析方法：直接搜索主机名模式
                    var match = System.Text.RegularExpressions.Regex.Match(hostLine, @"scaffolding-mc-server-\d+");
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    Console.WriteLine($"未找到主机，等待重试... ({retryCount}/{maxRetries})");
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取主机名称时出错: {ex.Message}");
                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(3000);
                }
            }
        }

        throw new Exception($"在 {maxRetries} 次重试后无法找到主机房间名称");
    }

    private async Task SetupPortForwarding(int remoteScfPort)
    {
        try
        {
            // 先检查是否已存在相同的端口转发规则
            var existingForwards = RunCliCommand("port-forward list");
            if (!existingForwards.Contains($"{ScfServerPort}->{remoteScfPort}"))
            {
                // 添加端口转发规则
                var forwardResult =
                    RunCliCommand($"port-forward add tcp 127.0.0.1:{ScfServerPort} 10.144.144.1:{remoteScfPort}");
                Console.WriteLine($"端口转发设置结果: {forwardResult}");

                // 验证端口转发
                await Task.Delay(1000);
                var verifyForwards = RunCliCommand("port-forward list");
                Console.WriteLine($"当前端口转发规则: {verifyForwards}");
            }
            else
            {
                Console.WriteLine("端口转发规则已存在，跳过设置");
            }

            Console.WriteLine($"端口转发就绪: 0.0.0.0:{ScfServerPort} -> 10.144.144.1:{remoteScfPort}");
        }
        catch (Exception ex)
        {
            throw new Exception($"设置端口转发失败: {ex.Message}");
        }
    }

    private async Task ConnectScfClientService()
    {
        try
        {
            Console.WriteLine($"尝试连接 SCF 服务: 127.0.0.1:{ScfServerPort}");

            ClientService = new RoomScfClient("Dime");

            var connectTask =
                ClientService.ExecuteStandardWorkflowAsync(IPAddress.Parse("127.0.0.1"), (ushort)ScfServerPort);

            var completedTask = await Task.WhenAny(connectTask);

            // 确保连接任务完成
            await connectTask;

            Console.WriteLine("SCF 客户端连接成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SCF 客户端连接异常: {ex.GetType().Name}: {ex.Message}");
            throw new Exception($"连接 SCF 客户端服务失败: {ex.Message}");
        }
    }

    private int GetHostNamePort()
    {
        try
        {
            // 更健壮的端口解析
            var parts = HostHomeName.Split('-');
            if (parts.Length >= 4 && int.TryParse(parts[3], out int port))
            {
                return port;
            }

            throw new FormatException("主机名格式不正确");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析主机端口失败: {ex.Message}");
            throw new Exception($"无法从主机名 {HostHomeName} 解析端口");
        }
    }

    private string RunCliCommand(string command)
    {
        if (!IsRunning) throw new NullReferenceException("请确保房间开启");
        if (Process.GetProcessesByName("easytier-core").Length <= 0)
            throw new NullReferenceException("请确保房间开启");

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
        IsRunning = true;

        var sb = new StringBuilder();

        proc.OutputDataReceived += (sender, args) => { sb.AppendLine(args.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return sb.ToString();
    }
}