using System.Diagnostics;
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
                Arguments = $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" --hostname \"{HostHomeName}\" -p tcp://public.easytier.top:11010 --dhcp --ipv4 10.144.144.1 --no-tun",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
                
        EasytierProcess.Start();
        
        IsRunning = true;
        Console.WriteLine($"联机码：{RoomCode}");
                
        EasytierProcess.OutputDataReceived +=  (sender, args) => Console.WriteLine(args.Data);
        EasytierProcess.BeginOutputReadLine();
        EasytierProcess.BeginErrorReadLine();
        EasytierProcess.WaitForExit();
    }

    public async Task ConnectRoom(string code)
    {
        RoomCode = code;
        ServiceType = ServiceStatus.Client;
        
        var codeBody = new RoomCode(RoomCode);
        
        EasytierProcess = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierPath,
                Arguments = $"--network-name \"{codeBody.NetworkName}\" --network-secret \"{codeBody.NetworkKey}\" -p tcp://public.easytier.top:11010 --no-tun",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
                
        EasytierProcess.Start();
        
        IsRunning = true;
                
        EasytierProcess.OutputDataReceived +=  (sender, args) => Console.WriteLine(args.Data);
        EasytierProcess.BeginOutputReadLine();
        EasytierProcess.BeginErrorReadLine();
        EasytierProcess.WaitForExit();
    }

    private string RunCliCommand(string command)
    {
        if (!IsRunning) throw new NullReferenceException("请确保房间开启");
        if (Process.GetProcessesByName(Path.GetFileName(EasyTierPath)).Length <= 0)
            throw new NullReferenceException("请确保房间开启");

        var proc = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = EasyTierCliPath,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        proc.Start();
        IsRunning = true;

        var sb = new StringBuilder();

        proc.OutputDataReceived += (sender, args) => sb.Append(args.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return sb.ToString();
    }
}