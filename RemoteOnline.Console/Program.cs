using System.Diagnostics;
using System.Net;
using RemoteOnline.Enum;
using RemoteOnline.ScaffoldingMc;

class Program
{
    static async Task Main()
    {
        Process.GetProcessesByName("easytier-core.exe").ToList().ForEach(x => x.Kill(true));
        
        RoomCore core = new RoomCore(ServiceStatus.Client);
        core.EasyTierPath = "easytier-core.exe";
        core.EasyTierCliPath = "easytier-cli.exe";
        core.HostPlayerName = "Dime";
        core.HostMachineId = "aaa";
        // await core.CreateRoom(25565);
        
        await core.ConnectRoom("U/TXVN-65UU-EEGH-HGBV");
    }
}