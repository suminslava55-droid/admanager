using System.Security.Cryptography;

namespace AdManager.Services;

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*_-+=";

    public static string Generate(int length = 14)
    {
        length = Math.Clamp(length, 8, 64);
        const string all = Upper + Lower + Digits + Symbols;
        var chars = new char[length];

        // Гарантируем все классы символов.
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (int i = 4; i < length; i++)
        {
            chars[i] = Pick(all);
        }

        // Криптографический шаффл.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
