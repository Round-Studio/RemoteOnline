using System.Text.RegularExpressions;

namespace RemoteOnline.Parser;

public class RoomCodeParser
{
    public string FullCode { get; }
    public string NPart { get; }
    public string SPart { get; }
    public string NetworkName => $"scaffolding-mc-{NPart}";
    public string NetworkKey => SPart;

    private static readonly char[] ValidChars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly Regex RoomCodeRegex = new Regex(@"^U/([A-Z0-9]{4})-([A-Z0-9]{4})-([A-Z0-9]{4})-([A-Z0-9]{4})$");

    public RoomCodeParser(string code)
    {
        if (!ValidateRoomCode(code))
            throw new ArgumentException("无效的房间码格式");

        var match = RoomCodeRegex.Match(code);
        NPart = $"{match.Groups[1].Value}-{match.Groups[2].Value}";
        SPart = $"{match.Groups[3].Value}-{match.Groups[4].Value}";
        FullCode = code;
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
        return RoomCodeGenerator.CalculateChecksum(fullCodeWithoutDash) == 0;
    }

    public override string ToString()
    {
        return $"房间码: {FullCode}\n网络名称: {NetworkName}\n网络密钥: {NetworkKey}";
    }
}