using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace AdManager.Common;

/// <summary>Преобразования значений AD, DN, SID, FILETIME и LDAP-фильтров.</summary>
public static class AdValue
{
    public const long NeverFileTime = 0x7FFFFFFFFFFFFFFF;

    public static bool HasFlag(int value, int flag) => (value & flag) == flag;

    public static int GetInt(object? value, int fallback = 0)
    {
        if (value is null) return fallback;
        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    public static long GetLong(object? value, long fallback = 0)
    {
        if (value is null) return fallback;
        if (value is long l) return l;
        if (value is int i) return i;
        if (value is DateTime dt) return dt.ToFileTimeUtc();

        // ADSI для больших чисел (pwdLastSet, accountExpires, lastLogon и т.п.)
        // возвращает COM-объект IADsLargeInteger с HighPart/LowPart.
        if (Marshal.IsComObject(value) && TryComLargeInteger(value, out long comValue))
        {
            return comValue;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryComLargeInteger(object value, out long result)
    {
        result = 0;
        try
        {
            Type type = value.GetType();
            object? high = type.InvokeMember("HighPart", BindingFlags.GetProperty, null, value, null);
            object? low = type.InvokeMember("LowPart", BindingFlags.GetProperty, null, value, null);
            if (high is null || low is null) return false;

            result = ((long)Convert.ToInt32(high, CultureInfo.InvariantCulture) << 32) |
                     (uint)Convert.ToInt32(low, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetString(object? value)
    {
        if (value is null) return string.Empty;
        if (value is byte[] bytes) return Encoding.UTF8.GetString(bytes);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public static DateTime? FileTimeToDateTime(long value)
    {
        if (value <= 0 || value == NeverFileTime) return null;
        try
        {
            if (value > DateTime.MaxValue.ToFileTimeUtc())
            {
                return null;
            }

            return DateTime.FromFileTimeUtc(value);
        }
        catch
        {
            return null;
        }
    }

    public static DateTime? FileTimeToDateTime(object? value) => FileTimeToDateTime(GetLong(value));

    public static DateTime? AccountExpiresToDateTime(long value)
    {
        if (value <= 0 || value == NeverFileTime) return null;
        return FileTimeToDateTime(value);
    }

    public static long DateTimeToAccountExpires(DateTime? value)
        => value.HasValue ? value.Value.ToUniversalTime().ToFileTimeUtc() : NeverFileTime;

    public static string ToLocalText(DateTime? utc, string format = "dd.MM.yyyy HH:mm")
        => utc.HasValue ? utc.Value.ToLocalTime().ToString(format, CultureInfo.CurrentCulture) : "—";

    public static string SidToString(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        try
        {
            return new SecurityIdentifier(bytes, 0).Value;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GuidToString(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        try
        {
            return new Guid(bytes).ToString("D");
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string BytesToHex(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Экранирование значения для LDAP-фильтра (RFC 4515).</summary>
    public static string EscapeLdapFilter(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Экранирование значения RDN для CN=..., OU=...</summary>
    public static string EscapeDnValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' or '#')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        string result = sb.ToString();
        if (result.StartsWith(' ')) result = "\\" + result;
        if (result.EndsWith(' ')) result = result[..^1] + "\\ ";
        return result;
    }

    public static IReadOnlyList<string> SplitDn(string? dn)
    {
        if (string.IsNullOrWhiteSpace(dn)) return Array.Empty<string>();
        var parts = new List<string>();
        var current = new StringBuilder();
        bool escaped = false;
        foreach (char c in dn)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                current.Append(c);
                escaped = true;
                continue;
            }

            if (c == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    public static string DnParent(string? dn)
    {
        var parts = SplitDn(dn);
        return parts.Count <= 1 ? string.Empty : string.Join(",", parts.Skip(1));
    }

    public static string DnToName(string? dn)
    {
        var parts = SplitDn(dn);
        if (parts.Count == 0) return string.Empty;
        string first = parts[0];
        int eq = first.IndexOf('=');
        string value = eq >= 0 ? first[(eq + 1)..] : first;
        return UnescapeDnValue(value);
    }

    public static string DnToOu(string? dn)
    {
        var parts = SplitDn(dn);
        var ou = parts.Skip(1).Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)).ToList();
        return ou.Count == 0 ? string.Empty : string.Join(" / ", ou.Select(p => UnescapeDnValue(p[3..])));
    }

    public static string UnescapeDnValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                sb.Append(value[i]);
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }

    public static string Initials(string? displayName, string? sam)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var words = displayName.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                return (words[0][0].ToString() + words[1][0]).ToUpperInvariant();
            }

            if (words.Length == 1 && words[0].Length >= 2)
            {
                return words[0][..2].ToUpperInvariant();
            }
        }

        return string.IsNullOrWhiteSpace(sam) ? "?" : sam[..Math.Min(2, sam.Length)].ToUpperInvariant();
    }

    public static string FormatAttributeValue(string name, object? value)
    {
        if (value is null) return string.Empty;
        if (value is byte[] bytes)
        {
            if (name.Equals("objectSid", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("sIDHistory", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("tokenGroups", StringComparison.OrdinalIgnoreCase))
            {
                return SidToString(bytes);
            }

            if (name.Equals("objectGUID", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("msExchMailboxGuid", StringComparison.OrdinalIgnoreCase))
            {
                return GuidToString(bytes);
            }

            return $"<binary {bytes.Length} bytes> {BytesToHex(bytes.AsSpan(0, Math.Min(bytes.Length, 32)).ToArray())}";
        }

        if (Marshal.IsComObject(value) && TryComLargeInteger(value, out long largeInteger))
        {
            if (name.Equals("accountExpires", StringComparison.OrdinalIgnoreCase) && largeInteger == NeverFileTime)
            {
                return "никогда";
            }

            return largeInteger.ToString(CultureInfo.InvariantCulture);
        }

        if (value is DateTime dt) return dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
        if (value is bool b) return b ? "TRUE" : "FALSE";
        if (value is Array array)
        {
            var items = new List<string>();
            foreach (object? item in array)
            {
                items.Add(FormatAttributeValue(name, item));
            }

            return string.Join("; ", items);
        }

        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public static string GroupTypeText(int groupType)
    {
        string scope = (groupType & 0x00000002) != 0 ? "Глобальная"
            : (groupType & 0x00000004) != 0 ? "Локальная домена"
            : (groupType & 0x00000008) != 0 ? "Универсальная"
            : "Неизвестная";
        string kind = (groupType & unchecked((int)0x80000000)) != 0 ? "Безопасность" : "Рассылка";
        return $"{kind}, {scope}";
    }

    public static string GroupScopeText(int groupType)
    {
        if ((groupType & 0x00000002) != 0) return "Глобальная";
        if ((groupType & 0x00000004) != 0) return "Локальная домена";
        if ((groupType & 0x00000008) != 0) return "Универсальная";
        return "Неизвестная";
    }

    public static bool IsSecurityGroup(int groupType) => (groupType & unchecked((int)0x80000000)) != 0;
}
