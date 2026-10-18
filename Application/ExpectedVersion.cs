namespace RentRight.Application;

// SQL Server rowversion is an opaque eight-byte value, never an access credential.
public static class ExpectedVersion
{
    public static bool TryParse(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != 12) return false;
        Span<byte> buffer = stackalloc byte[8];
        if (!Convert.TryFromBase64String(value, buffer, out var count) || count != 8) return false;
        bytes = buffer.ToArray();
        return Convert.ToBase64String(bytes) == value;
    }
}
