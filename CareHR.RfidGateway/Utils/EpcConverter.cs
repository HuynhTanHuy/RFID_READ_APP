using System.Text;

namespace CareHR.RfidGateway.Utils;

public static class EpcConverter
{
    public static string ToHex(byte[]? code, byte length)
    {
        if (code is null || length == 0)
        {
            return string.Empty;
        }

        var count = Math.Min(length, code.Length);
        if (count <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(count * 2);
        for (var i = 0; i < count; i++)
        {
            sb.Append(code[i].ToString("X2"));
        }

        return sb.ToString();
    }
}
