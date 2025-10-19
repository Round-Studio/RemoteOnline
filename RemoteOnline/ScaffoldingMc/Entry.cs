namespace RemoteOnline.ScaffoldingMc;



// 请求和响应数据结构
public class ScfRequest
{
    public string Type { get; set; }
    public byte[] Body { get; set; }
}

public class ScfResponse
{
    public byte Status { get; set; }
    public byte[] Body { get; set; }
}

// 数据模型
public class PlayerPingData
{
    public string name { get; set; }
    public string machine_id { get; set; }
    public string vendor { get; set; }
}

public class PlayerProfile
{
    public string name { get; set; }
    public string machine_id { get; set; }
    public string vendor { get; set; }
    public string kind { get; set; } // "HOST" or "GUEST"
    public DateTime lastPingTime { get; set; } = DateTime.UtcNow;
}