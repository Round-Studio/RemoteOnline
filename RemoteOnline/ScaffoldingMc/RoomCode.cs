using System.Text.RegularExpressions;

namespace RemoteOnline.ScaffoldingMc;

public class RoomCode
{
    public string FullCode { get; }
    public string NPart { get; }
    public string SPart { get; }
    public string NetworkName => $"scaffolding-mc-{NPart}";
    public string NetworkKey => SPart;

    private static readonly char[] ValidChars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly Random random = new Random();
    private static readonly Regex RoomCodeRegex = new Regex(@"^U/([A-Z0-9]{4})-([A-Z0-9]{4})-([A-Z0-9]{4})-([A-Z0-9]{4})$");

    public RoomCode(string code)
    {
        if (!ValidateRoomCode(code))
            throw new ArgumentException("无效的房间码格式");

        var match = RoomCodeRegex.Match(code);
        NPart = $"{match.Groups[1].Value}-{match.Groups[2].Value}";
        SPart = $"{match.Groups[3].Value}-{match.Groups[4].Value}";
        FullCode = code;
    }

    // 私有构造函数用于生成器
    private RoomCode(string part1, string part2, string part3, string part4)
    {
        NPart = $"{part1}-{part2}";
        SPart = $"{part3}-{part4}";
        FullCode = $"U/{NPart}-{SPart}";
    }

    public static bool ValidateRoomCode(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode) || !roomCode.StartsWith("U/"))
            return false;

        var match = RoomCodeRegex.Match(roomCode);
        if (!match.Success)
            return false;

        string part1 = match.Groups[1].Value;
        string part2 = match.Groups[2].Value;
        string part3 = match.Groups[3].Value;
        string part4 = match.Groups[4].Value;

        string nPart = part1 + part2;
        string sPart = part3 + part4;
        string fullCodeWithoutDash = nPart + sPart;

        // 检查字符是否合法
        foreach (char c in fullCodeWithoutDash)
        {
            if (Array.IndexOf(ValidChars, c) == -1)
                return false;
        }

        // 计算校验和 (小端序)
        return CalculateChecksum(fullCodeWithoutDash) == 0;
    }

    private static int CalculateChecksum(string data)
    {
        long totalValue = 0;
        long baseMultiplier = 1;

        for (int i = data.Length - 1; i >= 0; i--)
        {
            int charValue = Array.IndexOf(ValidChars, data[i]);
            totalValue += charValue * baseMultiplier;
            baseMultiplier *= 34;

            // 防止数值溢出
            if (baseMultiplier > long.MaxValue / 34 && i > 0)
            {
                totalValue %= 7;
                baseMultiplier %= 7;
            }
        }

        return (int)(totalValue % 7);
    }

    // 生成有效的房间码
    public static RoomCode GenerateCode()
    {
        int attempts = 0;
        const int maxAttempts = 10000;

        while (attempts < maxAttempts)
        {
            attempts++;
            
            // 生成4个随机部分
            string part1 = GenerateRandomPart();
            string part2 = GenerateRandomPart();
            string part3 = GenerateRandomPart();
            string part4 = GenerateRandomPart();

            string fullCode = $"U/{part1}-{part2}-{part3}-{part4}";

            if (ValidateRoomCode(fullCode))
            {
                Console.WriteLine($@"生成成功，尝试次数: {attempts}");
                return new RoomCode(part1, part2, part3, part4);
            }
        }

        throw new InvalidOperationException($"在 {maxAttempts} 次尝试后未能生成有效的房间码");
    }

    // 智能生成（更高效的方法）
    public static RoomCode GenerateSmartCode()
    {
        int attempts = 0;
        const int maxAttempts = 1000;

        while (attempts < maxAttempts)
        {
            attempts++;
            
            // 生成前3个部分
            string part1 = GenerateRandomPart();
            string part2 = GenerateRandomPart();
            string part3 = GenerateRandomPart();

            // 计算第4个部分以满足校验和
            string part4 = CalculateValidPart4(part1, part2, part3);
            
            if (part4 != null)
            {
                Console.WriteLine($@"智能生成成功，尝试次数: {attempts}");
                return new RoomCode(part1, part2, part3, part4);
            }
        }

        // 如果智能方法失败，回退到随机方法
        Console.WriteLine(@"智能生成失败，使用随机方法...");
        return GenerateCode();
    }

    // 计算满足校验和的第4部分
    private static string CalculateValidPart4(string part1, string part2, string part3)
    {
        string nPart = part1 + part2;
        string baseString = nPart + part3; // 前12个字符

        // 尝试生成第4部分
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string part4 = GenerateRandomPart();
            string fullString = baseString + part4;
            
            if (CalculateChecksum(fullString) == 0)
                return part4;
        }

        return null;
    }

    private static string GenerateRandomPart()
    {
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = ValidChars[random.Next(ValidChars.Length)];
        }
        return new string(chars);
    }

    public override string ToString()
    {
        return $"房间码: {FullCode}\n网络名称: {NetworkName}\n网络密钥: {NetworkKey}";
    }
}