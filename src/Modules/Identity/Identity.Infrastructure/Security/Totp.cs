using System.Security.Cryptography;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Minimal RFC 6238 TOTP and RFC 4648 Base32 helpers (HMAC-SHA1, 30s step, 6 digits) so MFA needs no
/// third-party dependency. Verification allows +/- one step to tolerate clock skew.
/// </summary>
internal static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int Step = 30;
    private const int Digits = 6;

    public static string GenerateSecret(int bytes = 20) => Base32Encode(RandomNumberGenerator.GetBytes(bytes));

    public static bool Verify(string base32Secret, string code, DateTimeOffset now, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        code = code.Trim();
        var key = Base32Decode(base32Secret);
        var counter = ToUnixTime(now) / Step;

        for (var offset = -window; offset <= window; offset++)
        {
            if (FixedTimeEquals(Compute(key, counter + offset), code))
                return true;
        }

        return false;
    }

    public static string Compute(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    private static long ToUnixTime(DateTimeOffset now) => now.ToUnixTimeSeconds();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(a),
            System.Text.Encoding.ASCII.GetBytes(b));

    public static string Base32Encode(byte[] data)
    {
        var result = new System.Text.StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
            result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return result.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var c in input)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0)
                continue;

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return [.. output];
    }
}
