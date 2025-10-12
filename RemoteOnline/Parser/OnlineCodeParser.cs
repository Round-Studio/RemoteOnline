using System.Text;

namespace RemoteOnline.Parser;

public class OnlineCodeParser
{
    public static string Encrypt(int id, int key, int port)
    {
        string b36_1 = Base36Encoder.ToBase36(id);
        string b36_2 = Base36Encoder.ToBase36(key);
        string b36_3 = Base36Encoder.ToBase36(port);
        return $"{b36_1}z{b36_2}z{b36_3}";
    }
    public static (int, int, int) Decrypt(string encrypted)
    {
        string[] parts = encrypted.Split('z');
        return (
            (int)Base36Encoder.FromBase36(parts[0]),
            (int)Base36Encoder.FromBase36(parts[1]),
            (int)Base36Encoder.FromBase36(parts[2])
        );
    }
}