using System.Text;

namespace RemoteOnline.Parser;

public class Base36Encoder
{
    private const string Chars = "0123456789abcdefghijklmnopqrstuvwxy$";

    public static string ToBase36(long num)
    {
        if (num == 0) return "0";
        var sb = new StringBuilder();
        while (num > 0)
        {
            sb.Insert(0, Chars[(int)(num % 36)]);
            num /= 36;
        }
        return sb.ToString();
    }

    public static long FromBase36(string str)
    {
        long result = 0;
        foreach (char c in str.ToLower())
        {
            result = result * 36 + Chars.IndexOf(c);
        }
        return result;
    }
}