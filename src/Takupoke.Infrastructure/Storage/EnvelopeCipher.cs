using System.Security.Cryptography;
using System.Text;

namespace Takupoke.Infrastructure.Storage;

public sealed class EnvelopeCipher(byte[] key) : IDisposable
{
    private readonly byte[] _key = key.ToArray();
    public byte[] Encrypt(byte[] plain, string purpose)
    {
        var bytes = new byte[1 + 12 + 16 + plain.Length];
        bytes[0] = 1;
        RandomNumberGenerator.Fill(bytes.AsSpan(1, 12));
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(bytes.AsSpan(1, 12), plain, bytes.AsSpan(29), bytes.AsSpan(13, 16), Encoding.UTF8.GetBytes(purpose));
        return bytes;
    }
    public byte[] Decrypt(byte[] bytes, string purpose)
    {
        if (bytes.Length < 29 || bytes[0] != 1) throw new InvalidDataException("学校データの暗号化形式を確認できません。");
        var plain = new byte[bytes.Length - 29];
        using var aes = new AesGcm(_key, 16);
        try { aes.Decrypt(bytes.AsSpan(1, 12), bytes.AsSpan(29), bytes.AsSpan(13, 16), plain, Encoding.UTF8.GetBytes(purpose)); }
        catch { CryptographicOperations.ZeroMemory(plain); throw; }
        return plain;
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
