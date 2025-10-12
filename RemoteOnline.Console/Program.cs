using RemoteOnline.Entry;
using RemoteOnline.Module;
using RemoteOnline.Parser;

class Program
{
    static void Main()
    {
        var forwarder = new TcpForwarder("127.0.0.1",25565,"127.0.0.1", 58126);
        forwarder.StartAsync();
        CoreInfo.ETFilePath = @"G:\easytier-windows-x86_64\easytier-core.exe";
        Console.WriteLine("=== 房间码验证 ===");
        string roomCodeStr = "U/7RBQ-NETH-WMJ5-L22V";
        
        try
        {
            var roomCode = new RoomCodeParser(roomCodeStr);
            Console.WriteLine("✅ 房间码验证成功！");
            Console.WriteLine(roomCode.ToString());

            EasyTierManager.JoinEasyTierNetwork(roomCode.NetworkName, roomCode.NetworkKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 房间码验证失败: {ex.Message}");
        }
    }
}