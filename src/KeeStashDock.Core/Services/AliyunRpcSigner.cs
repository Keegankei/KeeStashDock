using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("KeeStashDock.Tests")]

namespace KeeStashDock.Core.Services;

/// <summary>Alibaba Cloud RPC V2 signing, including form fields in the canonical parameters.</summary>
internal static class AliyunRpcSigner
{
    internal static string Sign(string method, IEnumerable<KeyValuePair<string, string>> parameters,
        string accessKeySecret)
    {
        string canonical = string.Join("&", parameters
            .Where(pair => pair.Key != "Signature")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        string stringToSign = method.ToUpperInvariant() + "&%2F&" + Uri.EscapeDataString(canonical);
        // HMAC-SHA1 is required by this API protocol; the secret is never transmitted.
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(accessKeySecret + "&"),
            Encoding.UTF8.GetBytes(stringToSign)));
    }
}
