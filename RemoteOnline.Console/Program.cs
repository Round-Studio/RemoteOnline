using System.Net;
using RemoteOnline.Enum;
using RemoteOnline.ScaffoldingMc;

class Program
{
    static async Task Main()
    {
        RoomCore core = new RoomCore(ServiceStatus.Client);
        core.EasyTierPath = "easytier-core.exe";
        core.EasyTierCliPath = "easytier-cli.exe";
        await core.CreateRoom(25565);
    }
}