using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RemoteOnline.ScaffoldingMc;

public class RoomScfClient : IDisposable
{
    private readonly string _playerName;
    private readonly string _vendor;
    private readonly string _machineId;
    
    // 联机中心信息
    private IPAddress _serverIp;
    private ushort _scfPort;
    private bool _isConnected = false;
    
    // 支持的协议列表
    private readonly HashSet<string> _supportedProtocols;
    
    // 心跳相关
    private System.Threading.Timer _heartbeatTimer;
    private bool _disposed = false;

    public RoomScfClient(string playerName, string vendor = "RemoteOnline, 未知 EasyTier 版本")
    {
        _playerName = playerName ?? throw new ArgumentException("玩家名称不能为空");
        _vendor = vendor ?? "RemoteOnline, 未知 EasyTier 版本";
        _machineId = GenerateMachineId();
        
        _supportedProtocols = new HashSet<string>
        {
            "c:ping",
            "c:protocols", 
            "c:server_port",
            "c:player_ping",
            "c:player_profiles_list"
        };
    }

    /// <summary>
    /// 生成设备ID
    /// </summary>
    private string GenerateMachineId()
    {
        // 根据硬件信息生成稳定的设备ID
        // 这里使用简化实现，实际应该根据CPU、主板等硬件信息生成
        var machineInfo = $"{Environment.MachineName}_{Environment.UserName}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    /// <summary>
    /// 连接到联机中心
    /// </summary>
    /// <param name="serverIp">联机中心虚拟IP</param>
    /// <param name="scfPort">联机中心端口</param>
    public async Task<bool> ConnectAsync(IPAddress serverIp, ushort scfPort)
    {
        if (_isConnected)
            throw new InvalidOperationException("客户端已连接");
            
        _serverIp = serverIp ?? throw new ArgumentNullException(nameof(serverIp));
        _scfPort = scfPort;

        try
        {
            // 1. 测试连接
            if (!await TestConnectionAsync())
            {
                Console.WriteLine("无法连接到联机中心");
                return false;
            }

            // 2. 发送初始心跳
            await SendPlayerPingAsync();

            // 3. 协商协议
            var commonProtocols = await NegotiateProtocolsAsync();
            if (commonProtocols.Count == 0)
            {
                Console.WriteLine("没有共同的协议支持");
                return false;
            }

            // 4. 启动心跳
            StartHeartbeat();

            _isConnected = true;
            Console.WriteLine($"成功连接到联机中心 {serverIp}:{scfPort}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    private async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_serverIp, _scfPort);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发送请求到联机中心
    /// </summary>
    private async Task<ScfResponse> SendRequestAsync(string protocol, byte[] requestBody = null)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_serverIp, _scfPort);
        
        using var stream = client.GetStream();
        return await SendRequestInternalAsync(stream, protocol, requestBody);
    }

    private async Task<ScfResponse> SendRequestInternalAsync(NetworkStream stream, string protocol, byte[] requestBody)
    {
        requestBody ??= Array.Empty<byte>();
        
        // 构建请求包
        var protocolBytes = Encoding.ASCII.GetBytes(protocol);
        var requestPacket = BuildRequestPacket(protocolBytes, requestBody);
        
        // 发送请求
        await stream.WriteAsync(requestPacket, 0, requestPacket.Length);
        
        // 读取响应
        return await ReadResponseAsync(stream);
    }

    private byte[] BuildRequestPacket(byte[] protocolBytes, byte[] requestBody)
    {
        var packet = new List<byte>();
        
        // 请求类型长度 (1字节)
        packet.Add((byte)protocolBytes.Length);
        
        // 请求类型
        packet.AddRange(protocolBytes);
        
        // 请求体长度 (4字节，大端序)
        var bodyLength = BitConverter.GetBytes((uint)requestBody.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bodyLength);
        packet.AddRange(bodyLength);
        
        // 请求体
        packet.AddRange(requestBody);
        
        return packet.ToArray();
    }

    private async Task<ScfResponse> ReadResponseAsync(NetworkStream stream)
    {
        var buffer = new byte[5];
        var bytesRead = await stream.ReadAsync(buffer, 0, 5);
        
        if (bytesRead < 5)
            throw new Exception("响应头不完整");
        
        // 读取状态 (1字节)
        byte status = buffer[0];
        
        // 读取响应体长度 (4字节，大端序)
        var lengthBytes = new byte[4];
        Array.Copy(buffer, 1, lengthBytes, 0, 4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        uint bodyLength = BitConverter.ToUInt32(lengthBytes, 0);
        
        // 读取响应体
        if (bodyLength > 0)
        {
            var body = new byte[bodyLength];
            var totalRead = 0;
            while (totalRead < bodyLength)
            {
                var read = await stream.ReadAsync(body, totalRead, (int)(bodyLength - totalRead));
                if (read == 0) break;
                totalRead += read;
            }
            
            if (status == 255) // 未知错误
            {
                var errorMsg = Encoding.UTF8.GetString(body);
                throw new Exception($"联机中心错误: {errorMsg}");
            }
            
            return new ScfResponse 
            { 
                Status = status, 
                Body = body 
            };
        }
        
        return new ScfResponse 
        { 
            Status = status, 
            Body = Array.Empty<byte>() 
        };
    }

    /// <summary>
    /// 测试联机中心是否正常 (c:ping)
    /// </summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            var testData = Encoding.UTF8.GetBytes("ping_test");
            var response = await SendRequestAsync("c:ping", testData);
            
            if (response.Status == 0 && response.Body.Length == testData.Length)
            {
                var responseData = Encoding.UTF8.GetString(response.Body);
                return responseData == "ping_test";
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 协商协议列表 (c:protocols)
    /// </summary>
    public async Task<HashSet<string>> NegotiateProtocolsAsync()
    {
        try
        {
            var supportedProtocols = string.Join("\0", _supportedProtocols);
            var requestBody = Encoding.ASCII.GetBytes(supportedProtocols);
            
            var response = await SendRequestAsync("c:protocols", requestBody);
            
            if (response.Status == 0)
            {
                var serverProtocolsStr = Encoding.ASCII.GetString(response.Body);
                var serverProtocols = serverProtocolsStr.Split('\0');
                
                var commonProtocols = new HashSet<string>();
                foreach (var protocol in serverProtocols)
                {
                    if (_supportedProtocols.Contains(protocol))
                        commonProtocols.Add(protocol);
                }
                
                Console.WriteLine($"协议协商成功，共同支持 {commonProtocols.Count} 个协议");
                return commonProtocols;
            }
            
            return new HashSet<string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"协议协商失败: {ex.Message}");
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// 获取Minecraft服务器端口 (c:server_port)
    /// </summary>
    public async Task<ushort?> GetServerPortAsync()
    {
        try
        {
            var response = await SendRequestAsync("c:server_port");
            
            if (response.Status == 0)
            {
                if (response.Body.Length != 2)
                    throw new Exception("服务器端口响应格式错误");
                
                // 大端序转换为ushort
                var portBytes = response.Body;
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(portBytes);
                
                return BitConverter.ToUInt16(portBytes, 0);
            }
            else if (response.Status == 32)
            {
                Console.WriteLine("Minecraft服务器未启动");
                return null;
            }
            else
            {
                throw new Exception($"获取服务器端口失败，状态码: {response.Status}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取服务器端口失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 发送玩家心跳 (c:player_ping)
    /// </summary>
    public async Task<bool> SendPlayerPingAsync()
    {
        try
        {
            var pingData = new PlayerPingData
            {
                name = _playerName,
                machine_id = _machineId,
                vendor = _vendor
            };
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            var requestBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pingData, jsonOptions));
            var response = await SendRequestAsync("c:player_ping", requestBody);
            
            if (response.Status == 0)
            {
                Console.WriteLine($"心跳发送成功: {_playerName}");
                return true;
            }
            else
            {
                Console.WriteLine($"心跳发送失败，状态码: {response.Status}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"心跳发送异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取玩家列表 (c:player_profiles_list)
    /// </summary>
    public async Task<List<PlayerProfile>> GetPlayerProfilesAsync()
    {
        try
        {
            var response = await SendRequestAsync("c:player_profiles_list");
            
            if (response.Status == 0)
            {
                var json = Encoding.UTF8.GetString(response.Body);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var playerProfiles = JsonSerializer.Deserialize<List<PlayerProfile>>(json, jsonOptions);
                Console.WriteLine($"获取到 {playerProfiles?.Count ?? 0} 个玩家信息");
                return playerProfiles ?? new List<PlayerProfile>();
            }
            else
            {
                Console.WriteLine($"获取玩家列表失败，状态码: {response.Status}");
                return new List<PlayerProfile>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取玩家列表异常: {ex.Message}");
            return new List<PlayerProfile>();
        }
    }

    /// <summary>
    /// 启动心跳定时器
    /// </summary>
    private void StartHeartbeat()
    {
        _heartbeatTimer = new System.Threading.Timer(
            async _ => await HeartbeatCycleAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(15) // 15秒间隔
        );
    }

    /// <summary>
    /// 心跳循环
    /// </summary>
    private async Task HeartbeatCycleAsync()
    {
        if (!_isConnected || _disposed) return;

        try
        {
            await SendPlayerPingAsync();
            await GetPlayerProfilesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"心跳循环异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 执行标准联机流程
    /// </summary>
    public async Task<(bool success, ushort? minecraftPort, List<PlayerProfile> players)> ExecuteStandardWorkflowAsync(
        IPAddress serverIp, ushort scfPort)
    {
        try
        {
            // 1. 连接
            if (!await ConnectAsync(serverIp, scfPort))
                return (false, null, new List<PlayerProfile>());

            // 2. 获取Minecraft服务器端口
            var minecraftPort = await GetServerPortAsync();
            if (!minecraftPort.HasValue)
                return (false, null, new List<PlayerProfile>());

            // 3. 获取初始玩家列表
            var players = await GetPlayerProfilesAsync();

            return (true, minecraftPort, players);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"标准联机流程执行失败: {ex.Message}");
            return (false, null, new List<PlayerProfile>());
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _isConnected = false;
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        Console.WriteLine("已断开与联机中心的连接");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
    }

    // 公共属性
    public string PlayerName => _playerName;
    public string MachineId => _machineId;
    public string Vendor => _vendor;
    public bool IsConnected => _isConnected;
}