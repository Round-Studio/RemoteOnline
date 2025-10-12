using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RemoteOnline.Enum;
using RemoteOnline.Parser;

namespace RemoteOnline.Core;

public class OnlineService
{
    public ServiceStatus Status { get; private set; }
    public string Name { get; set; }
    public string EasytierPath { get; set; } = string.Empty;
    public bool IsRunning { get; private set; } = false;
    public string OnlineCode { get;private set; } = string.Empty;
    public int LocalPort { get; private set; } = 25565;
    public Process EasytierProcess { get; private set; }
    public int Key { get;private set; } = new Random().Next(10, 99);
    public int ID { get;private set; } = new Random().Next(10000000, 99999999);

    private int RemotePort { get; set; } = 0;

    public OnlineService(string? easytierPath)
    {
        EasytierPath = easytierPath;
    }

    public void CreateRoom(int localPort)
    {
        LocalPort = localPort;
        Status = ServiceStatus.Server;
        
        OnlineCode = OnlineCodeParser.Encrypt(ID, Key, LocalPort);
    }

    public void LinkRoom(string code)
    {
        OnlineCode = code;
        LocalPort = new Random().Next(4000, 60000);
        Status = ServiceStatus.Client;
        
        var (id,key,RomPort) = OnlineCodeParser.Decrypt(code);
        
        ID = id;
        Key = key;
        RemotePort = RomPort;
        
        Console.WriteLine($"本地端口：{LocalPort}");
    }

    public void Stop()
    {
        if(IsRunning) 
        {
            IsRunning = false;
            EasytierProcess.Kill(true);
        }
    }

    public void Run()
    {
        if (!IsRunning)
        {
            if (Status == ServiceStatus.Server)
            {
                EasytierProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = EasytierPath,
                        Arguments = $"--network-name \"round-studio-online-mc-{OnlineCode}\" --network-secret \"{Key}\" -p tcp://public.easytier.cn:11010 --dhcp --ipv4 10.126.126.1 --no-tun",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                EasytierProcess.Start();
                
                Console.WriteLine($"联机码：{OnlineCode}");
                IsRunning = true;
                
                EasytierProcess.OutputDataReceived +=  (sender, args) => Console.WriteLine(args.Data);
                EasytierProcess.BeginOutputReadLine();
                EasytierProcess.BeginErrorReadLine();
                EasytierProcess.WaitForExit();
            }
            else
            {
                EasytierProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = EasytierPath,
                        Arguments = $"--network-name \"round-studio-online-mc-{OnlineCode}\" --network-secret \"{Key}\" -p tcp://public.easytier.cn:11010 --dhcp --port-forward=tcp://0.0.0.0:{LocalPort}/10.126.126.1:{RemotePort} --no-tun",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                EasytierProcess.Start();
                IsRunning = true;

                Task.Run(() =>
                {
                    string multicastGroup = "224.0.2.60";
                    int multicastPort = 4445;
                    using (UdpClient client = new UdpClient(new Random().Next(4000, 60000)))
                    {
                        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(multicastGroup), multicastPort);

                        byte[] ttl = new byte[] { 2 };
                        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);

                        while (IsRunning)
                        {
                            string message =
                                $"[MOTD]§e[RMCL] §fRMCL 联机房间[/MOTD][AD]{LocalPort}[/AD]";
                            byte[] data = Encoding.UTF8.GetBytes(message);

                            client.Send(data, data.Length, remoteEP);

                            Thread.Sleep(500);
                        }
                    }
                });
                
                EasytierProcess.OutputDataReceived +=  (sender, args) => Console.WriteLine(args.Data);
                EasytierProcess.BeginOutputReadLine();
                EasytierProcess.BeginErrorReadLine();
                EasytierProcess.WaitForExit();
            }
        }
    }
}