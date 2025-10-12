using System;
using System.Diagnostics;
using System.IO;
using RemoteOnline.Entry;

namespace RemoteOnline.Module;

public class EasyTierManager
{
    /// <summary>
    /// 通过命令行加入 EasyTier 网络
    /// </summary>
    public static void JoinEasyTierNetwork(string networkName, string networkKey)
    {
        try
        {
            var lst = new List<string>()
            {
                "tcp://weior.top:11010",
                "tcp://net.corvus.icu:51121",
                "tcp://turn.bj.629957.xyz:11010",
                "tcp://et.gbc.moe:11010",
                "tcp://106.15.202.147:11010",
                "tcp://public.easytier.top:11010",
                "tcp://public2.easytier.cn:54321"
            };
            
            // 构建命令
            string command =
                $"--network-name {networkName} --network-secret {networkKey} --no-tun --compression=zstd --multi-thread --latency-first --enable-kcp-proxy --tcp-whitelist=0 --udp-whitelist=0 -r 58126";
            
            lst.ForEach(x=>command += $" -p {x}");
            
            // 执行 EasyTier 命令
            var processStartInfo = new ProcessStartInfo
            {
                FileName = CoreInfo.ETFilePath, // 或 "easytier.exe" 在 Windows 上
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processStartInfo);
            if (process == null)
                throw new InvalidOperationException("无法启动 EasyTier 进程");
            
            process.OutputDataReceived +=  (sender, args) => Console.WriteLine(args.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加入 EasyTier 网络失败: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// 离开 EasyTier 网络
    /// </summary>
    public static void LeaveEasyTierNetwork(string networkName)
    {
        try
        {
            string command = $"leave --network {networkName}";
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "easytier",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(processStartInfo);
            process?.WaitForExit(10000);
            
            Console.WriteLine($"已离开网络: {networkName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"离开网络时出错: {ex.Message}");
        }
    }
}