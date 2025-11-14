using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RemoteOnline.ScaffoldingMc;

public class RoomScfService : IDisposable
{
    private readonly ushort _port;
    private TcpListener _listener;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _isDisposed = false;

    // Minecraft服务器状态
    public ushort MinecraftServerPort { get; set; }
    public bool IsMinecraftServerRunning { get; set; }

    // 玩家管理
    private readonly Dictionary<string, PlayerProfile> _players;
    private readonly object _playersLock = new object();

    // 房主信息
    private string _hostMachineId;
    private string _hostPlayerName;

    // 支持的协议列表
    private readonly HashSet<string> _supportedProtocols;

    public RoomScfService(int port)
    {
        if (port <= 1024 || port > 65535)
            throw new ArgumentException("端口必须在1025-65535之间");

        _port = (ushort)port;
        _players = new Dictionary<string, PlayerProfile>();
        _supportedProtocols = new HashSet<string>
        {
            "c:ping",
            "c:protocols",
            "c:server_port",
            "c:player_ping",
            "c:player_profiles_list"
        };

        // 初始化房主信息
        _hostMachineId = GenerateHostMachineId();
        _hostPlayerName = "Host";

        InitializeServer();
    }

    /// <summary>
    /// 设置房主玩家
    /// </summary>
    /// <param name="playerName">房主玩家名称</param>
    /// <param name="machineId">房主设备ID（如果为空则自动生成）</param>
    public void SetHostPlayer(string playerName, string machineId = null)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("房主玩家名称不能为空");

        lock (_playersLock)
        {
            _hostPlayerName = playerName.Trim();
            _hostMachineId = string.IsNullOrEmpty(machineId) ? GenerateHostMachineId() : machineId;

            // 更新或添加房主到玩家列表
            _players[_hostMachineId] = new PlayerProfile
            {
                name = _hostPlayerName,
                machine_id = _hostMachineId,
                vendor = "RemoteOnline, 未知 EasyTier 版本",
                kind = "HOST",
                lastPingTime = DateTime.UtcNow
            };

            Console.WriteLine($@"设置房主: {_hostPlayerName} (ID: {_hostMachineId})");
        }
    }

    /// <summary>
    /// 获取房主信息
    /// </summary>
    public (string name, string machineId) GetHostInfo()
    {
        return (_hostPlayerName, _hostMachineId);
    }

    /// <summary>
    /// 设置房主为当前玩家（如果房主不存在）
    /// </summary>
    public void SetHostIfNotExists(string playerName = "Host")
    {
        lock (_playersLock)
        {
            if (string.IsNullOrEmpty(_hostMachineId))
            {
                SetHostPlayer(playerName);
            }
        }
    }

    /// <summary>
    /// 检查指定玩家是否是房主
    /// </summary>
    public bool IsHostPlayer(string machineId)
    {
        return machineId == _hostMachineId;
    }

    /// <summary>
    /// 移除房主状态（慎用）
    /// </summary>
    public void RemoveHostStatus()
    {
        lock (_playersLock)
        {
            if (_players.ContainsKey(_hostMachineId))
            {
                // 将房主降级为普通玩家
                _players[_hostMachineId].kind = "GUEST";
            }

            _hostMachineId = null;
            _hostPlayerName = null;
            Console.WriteLine(@"房主状态已移除");
        }
    }

    /// <summary>
    /// 转移房主权限给其他玩家
    /// </summary>
    public bool TransferHost(string targetMachineId)
    {
        lock (_playersLock)
        {
            if (_players.ContainsKey(targetMachineId))
            {
                var targetPlayer = _players[targetMachineId];

                // 将原房主降级为普通玩家
                if (!string.IsNullOrEmpty(_hostMachineId)
                    && _players.ContainsKey(_hostMachineId)
                    && _hostMachineId != targetMachineId)
                {
                    _players[_hostMachineId].kind = "GUEST";
                }

                // 设置新房主
                _hostMachineId = targetMachineId;
                _hostPlayerName = targetPlayer.name;
                targetPlayer.kind = "HOST";

                Console.WriteLine($@"房主权限已转移给: {_hostPlayerName} (ID: {_hostMachineId})");
                return true;
            }

            Console.WriteLine($@"转移房主失败: 未找到玩家 {targetMachineId}");
            return false;
        }
    }

    /// <summary>
    /// 强制设置某个玩家为房主（即使该玩家不在线）
    /// </summary>
    public void ForceSetHost(string playerName, string machineId)
    {
        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(machineId))
            throw new ArgumentException("玩家名称和设备ID不能为空");

        lock (_playersLock)
        {
            // 将原房主降级
            if (!string.IsNullOrEmpty(_hostMachineId)
                && _players.ContainsKey(_hostMachineId)
                && _hostMachineId != machineId)
            {
                _players[_hostMachineId].kind = "GUEST";
            }

            // 设置新房主
            _hostPlayerName = playerName.Trim();
            _hostMachineId = machineId;

            // 如果玩家不在列表中，添加新房主
            if (!_players.ContainsKey(machineId))
            {
                _players[machineId] = new PlayerProfile
                {
                    name = _hostPlayerName,
                    machine_id = _hostMachineId,
                    vendor = "host",
                    kind = "HOST",
                    lastPingTime = DateTime.UtcNow
                };
            }
            else
            {
                _players[machineId].kind = "HOST";
                _players[machineId].name = _hostPlayerName;
            }

            Console.WriteLine($@"强制设置房主: {_hostPlayerName} (ID: {_hostMachineId})");
        }
    }

    private void InitializeServer()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _cancellationTokenSource = new CancellationTokenSource();

        // 自动设置默认房主
        SetHostIfNotExists();
    }

    /// <summary>
    /// 启动SCF服务器
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _listener.Start();
            Console.WriteLine($@"SCF服务器启动在端口 {_port}");
            Console.WriteLine($@"联机中心Hostname: scaffolding-mc-server-{_port}");
            Console.WriteLine($@"房主玩家: {_hostPlayerName} (ID: {_hostMachineId})");

            await AcceptConnectionsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"服务器启动失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 停止SCF服务器
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _listener?.Stop();
        Console.WriteLine(@"SCF服务器已停止");
    }

    private async Task AcceptConnectionsAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client), _cancellationTokenSource.Token);
            }
            catch (ObjectDisposedException)
            {
                // 服务器已停止
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"接受连接时出错: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                while (client.Connected && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var request = await ReadRequestAsync(stream);
                    if (request == null) break;

                    var response = await ProcessRequestAsync(request);
                    await SendResponseAsync(stream, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"处理客户端时出错: {ex.Message}");
            }
        }
    }

    private async Task<ScfRequest> ReadRequestAsync(NetworkStream stream)
    {
        try
        {
            // 读取请求类型长度 (1字节)
            var typeLengthBuffer = new byte[1];
            var bytesRead = await stream.ReadAsync(typeLengthBuffer, 0, 1);
            if (bytesRead == 0) return null;

            byte typeLength = typeLengthBuffer[0];

            // 读取请求类型
            var typeBuffer = new byte[typeLength];
            bytesRead = await stream.ReadAsync(typeBuffer, 0, typeLength);
            if (bytesRead != typeLength) return null;

            string requestType = Encoding.ASCII.GetString(typeBuffer);

            // 验证请求类型格式
            if (!IsValidProtocol(requestType))
            {
                await SendErrorResponseAsync(stream, 255, "无效的协议格式");
                return null;
            }

            // 读取请求体长度 (4字节，大端序)
            var lengthBuffer = new byte[4];
            bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
            if (bytesRead != 4) return null;

            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuffer);

            uint bodyLength = BitConverter.ToUInt32(lengthBuffer, 0);

            // 读取请求体
            byte[] requestBody = Array.Empty<byte>();
            if (bodyLength > 0)
            {
                requestBody = new byte[bodyLength];
                int totalRead = 0;
                while (totalRead < bodyLength)
                {
                    bytesRead = await stream.ReadAsync(requestBody, totalRead, (int)(bodyLength - totalRead));
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }
            }

            return new ScfRequest
            {
                Type = requestType,
                Body = requestBody
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"读取请求时出错: {ex.Message}");
            return null;
        }
    }

    private bool IsValidProtocol(string protocol)
    {
        // 格式: {namespace}:{value}
        var parts = protocol.Split(':');
        if (parts.Length != 2) return false;

        // 检查命名空间和值是否只包含小写字母、下划线、数字
        foreach (var part in parts)
        {
            foreach (char c in part)
            {
                if (!(char.IsLower(c) || c == '_' || char.IsDigit(c)))
                    return false;
            }
        }

        return true;
    }

    private async Task<ScfResponse> ProcessRequestAsync(ScfRequest request)
    {
        try
        {
            Console.WriteLine($@"处理请求: {request.Type}");

            if (_supportedProtocols.Contains(request.Type))
            {
                return request.Type switch
                {
                    "c:ping" => await HandlePingAsync(request),
                    "c:protocols" => await HandleProtocolsAsync(request),
                    "c:server_port" => await HandleServerPortAsync(request),
                    "c:player_ping" => await HandlePlayerPingAsync(request),
                    "c:player_profiles_list" => await HandlePlayerProfileListAsync(request),
                    _ => new ScfResponse { Status = 255, Body = Encoding.UTF8.GetBytes($"未知协议: {request.Type}") }
                };
            }
            else
            {
                return new ScfResponse { Status = 255, Body = Encoding.UTF8.GetBytes($"不支持的协议: {request.Type}") };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"处理请求 {request.Type} 时出错: {ex.Message}");
            return new ScfResponse { Status = 255, Body = Encoding.UTF8.GetBytes(ex.Message) };
        }
    }

    private Task<ScfResponse> HandlePingAsync(ScfRequest request)
    {
        // c:ping - 直接返回请求体内容
        return Task.FromResult(new ScfResponse
        {
            Status = 0,
            Body = request.Body
        });
    }

    private Task<ScfResponse> HandleProtocolsAsync(ScfRequest request)
    {
        // c:protocols - 返回支持的协议列表
        var protocols = string.Join("\0", _supportedProtocols);
        var responseBody = Encoding.ASCII.GetBytes(protocols);

        return Task.FromResult(new ScfResponse
        {
            Status = 0,
            Body = responseBody
        });
    }

    private Task<ScfResponse> HandleServerPortAsync(ScfRequest request)
    {
        // c:server_port - 返回Minecraft服务器端口
        if (!IsMinecraftServerRunning)
        {
            return Task.FromResult(new ScfResponse
            {
                Status = 32, // 服务器未启动
                Body = Array.Empty<byte>()
            });
        }

        var portBytes = BitConverter.GetBytes(MinecraftServerPort);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(portBytes);

        return Task.FromResult(new ScfResponse
        {
            Status = 0,
            Body = portBytes
        });
    }

    private Task<ScfResponse> HandlePlayerPingAsync(ScfRequest request)
    {
        // c:player_ping - 更新玩家心跳
        try
        {
            var json = Encoding.UTF8.GetString(request.Body);
            var playerData = JsonSerializer.Deserialize<PlayerPingData>(json);

            if (playerData != null)
            {
                lock (_playersLock)
                {
                    // 确定玩家类型：如果是房主设备ID则为HOST，否则为GUEST
                    string playerKind = playerData.machine_id == _hostMachineId ? "HOST" : "GUEST";

                    _players[playerData.machine_id] = new PlayerProfile
                    {
                        name = playerData.name,
                        machine_id = playerData.machine_id,
                        vendor = playerData.vendor,
                        kind = playerKind,
                        lastPingTime = DateTime.UtcNow
                    };
                }

                Console.WriteLine(
                    $@"玩家心跳: {playerData.name} ({playerData.machine_id}) - {(_players[playerData.machine_id].kind == "HOST" ? "房主" : "访客")}");
            }

            return Task.FromResult(new ScfResponse
            {
                Status = 0,
                Body = Array.Empty<byte>()
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScfResponse
            {
                Status = 255,
                Body = Encoding.UTF8.GetBytes($"解析玩家数据失败: {ex.Message}")
            });
        }
    }

    private Task<ScfResponse> HandlePlayerProfileListAsync(ScfRequest request)
    {
        // c:player_profile_list - 返回玩家列表
        lock (_playersLock)
        {
            // 清理超时玩家（30秒未心跳），但保留房主
            var timeout = DateTime.UtcNow.AddSeconds(-30);
            var expiredPlayers = new List<string>();

            foreach (var kvp in _players)
            {
                // 房主不清理，其他玩家超时清理
                if (kvp.Value.kind != "HOST" && kvp.Value.lastPingTime < timeout)
                    expiredPlayers.Add(kvp.Key);
            }

            foreach (var machineId in expiredPlayers)
            {
                _players.Remove(machineId);
                Console.WriteLine($@"清理超时玩家: {machineId}");
            }

            // 构建响应 - 使用更兼容的JSON序列化方式
            var playerList = new List<PlayerProfileResponse>();
            foreach (var player in _players.Values)
            {
                playerList.Add(new PlayerProfileResponse
                {
                    name = player.name,
                    machine_id = player.machine_id,
                    vendor = player.vendor,
                    kind = player.kind
                });
            }

            // 使用更严格的JSON序列化设置
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // 使用驼峰命名
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string json;
            try
            {
                json = JsonSerializer.Serialize(playerList, jsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"JSON序列化失败: {ex.Message}");
                // 回退到手动构建JSON
                json = BuildManualJson(playerList);
            }

            Console.WriteLine($@"返回玩家列表JSON: {json}"); // 调试日志

            return Task.FromResult(new ScfResponse
            {
                Status = 0,
                Body = Encoding.UTF8.GetBytes(json)
            });
        }
    }

// 手动构建JSON的备选方案
    private string BuildManualJson(List<PlayerProfileResponse> players)
    {
        var sb = new StringBuilder();
        sb.Append('[');

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            sb.Append('{');
            sb.Append($"\"name\":\"{EscapeJsonString(player.name)}\",");
            sb.Append($"\"machine_id\":\"{EscapeJsonString(player.machine_id)}\",");
            sb.Append($"\"vendor\":\"{EscapeJsonString(player.vendor)}\",");
            sb.Append($"\"kind\":\"{EscapeJsonString(player.kind)}\"");
            sb.Append('}');

            if (i < players.Count - 1)
                sb.Append(',');
        }

        sb.Append(']');
        return sb.ToString();
    }

// 转义JSON字符串中的特殊字符
    private string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

// 专门的响应数据模型
    public class PlayerProfileResponse
    {
        public string name { get; set; }
        public string machine_id { get; set; }
        public string vendor { get; set; }
        public string kind { get; set; } // "HOST" or "GUEST"
    }

    private async Task SendResponseAsync(NetworkStream stream, ScfResponse response)
    {
        try
        {
            var packet = new List<byte>();

            // 状态 (1字节)
            packet.Add(response.Status);

            // 响应体长度 (4字节，大端序)
            var bodyLength = BitConverter.GetBytes((uint)response.Body.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bodyLength);
            packet.AddRange(bodyLength);

            // 响应体
            packet.AddRange(response.Body);

            await stream.WriteAsync(packet.ToArray(), 0, packet.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"发送响应时出错: {ex.Message}");
        }
    }

    private async Task SendErrorResponseAsync(NetworkStream stream, byte status, string message)
    {
        var response = new ScfResponse
        {
            Status = status,
            Body = Encoding.UTF8.GetBytes(message)
        };
        await SendResponseAsync(stream, response);
    }

    /// <summary>
    /// 生成主机设备ID
    /// </summary>
    private string GenerateHostMachineId()
    {
        // 使用机器名和当前时间生成稳定的主机ID
        return $"host_{Environment.MachineName}_{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// 添加自定义协议支持
    /// </summary>
    public void AddCustomProtocol(string protocol, Func<ScfRequest, Task<ScfResponse>> handler)
    {
        if (!IsValidProtocol(protocol))
            throw new ArgumentException("无效的协议格式");

        _supportedProtocols.Add(protocol);
        // 注意：这里需要扩展以支持自定义处理器
    }

    /// <summary>
    /// 获取当前在线玩家数量
    /// </summary>
    public int GetOnlinePlayerCount()
    {
        lock (_playersLock)
        {
            return _players.Count;
        }
    }

    /// <summary>
    /// 获取玩家列表（包含房主信息）
    /// </summary>
    public List<PlayerProfile> GetPlayerList()
    {
        lock (_playersLock)
        {
            return new List<PlayerProfile>(_players.Values);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _isDisposed = true;
        }
    }
}
