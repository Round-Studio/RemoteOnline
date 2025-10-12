using System;
using System.Text;

namespace RemoteOnline.Parser;

public class RoomCodeGenerator
{
    private static readonly char[] ValidChars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly Random random = new Random();

    // 生成有效的房间码
    public static string Generate()
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

            if (RoomCodeParser.ValidateRoomCode(fullCode))
            {
                Console.WriteLine($"生成成功，尝试次数: {attempts}");
                return fullCode;
            }
        }

        throw new InvalidOperationException($"在 {maxAttempts} 次尝试后未能生成有效的房间码");
    }

    // 智能生成（更高效的方法）
    public static string GenerateSmart()
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
                Console.WriteLine($"智能生成成功，尝试次数: {attempts}");
                return $"U/{part1}-{part2}-{part3}-{part4}";
            }
        }

        // 如果智能方法失败，回退到随机方法
        Console.WriteLine("智能生成失败，使用随机方法...");
        return Generate();
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

    // 计算校验和 (小端序)
    internal static int CalculateChecksum(string data)
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

    private static string GenerateRandomPart()
    {
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = ValidChars[random.Next(ValidChars.Length)];
        }
        return new string(chars);
    }

    // 批量生成
    public static string[] GenerateBatch(int count)
    {
        var codes = new string[count];
        for (int i = 0; i < count; i++)
        {
            codes[i] = GenerateSmart();
        }
        return codes;
    }
}