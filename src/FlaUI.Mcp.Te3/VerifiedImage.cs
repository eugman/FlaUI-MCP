using System.Drawing;
using System.Security.Cryptography;

/// <summary>Decodes the exact screenshot bytes whose hash was checked against the run manifest.</summary>
internal static class VerifiedImage
{
    public static Bitmap Read(string path, string? captured, string changedMessage)
    {
        var bytes = File.ReadAllBytes(path);
        if (captured != null && Convert.ToHexString(SHA256.HashData(bytes)) != captured)
        {
            throw new IOException(changedMessage);
        }

        using var stream = new MemoryStream(bytes);
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }
}
