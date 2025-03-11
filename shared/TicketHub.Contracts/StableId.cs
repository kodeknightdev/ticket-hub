using System.Security.Cryptography;
using System.Text;

namespace TicketHub.Contracts;

/// <summary>RFC 4122 v5 ids, matching Python's uuid.uuid5(StableId.Namespace, name).</summary>
public static class StableId
{
    public static readonly Guid Namespace = Guid.Parse("6f2c1a4e-8d3b-4c7a-9e10-5b7d2f4a8c61");

    public static Guid From(string name)
    {
        var ns = Namespace.ToByteArray(bigEndian: true);
        var bytes = Encoding.UTF8.GetBytes(name);
        var hash = SHA1.HashData([.. ns, .. bytes]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
