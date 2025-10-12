using System;
using System.Collections.Generic;
using RemoteOnline.Core;

namespace RemoteOnline.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("基于 EasyTier 的联机模块");
            
            System.Console.WriteLine("1. 创建房间");
            System.Console.WriteLine("2. 加入房间");

            System.Console.Write("选择：");
            var key = System.Console.ReadLine();
            if (key == "1")
            {
                System.Console.Write("本地端口：");
                var port = System.Console.ReadLine();

                var server = new OnlineService(@"easytier-core.exe");
                server.CreateRoom(int.Parse(port));
                server.Run();
            }
            else
            {
                System.Console.Write("联机码：");
                var code = System.Console.ReadLine();

                var server = new OnlineService(@"easytier-core.exe");
                server.LinkRoom(code);
                server.Run();
            }
        }
    }
}