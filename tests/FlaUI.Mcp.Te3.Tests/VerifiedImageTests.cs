using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

public sealed class VerifiedImageTests
{
    [Fact]
    public void DecodedPixelsComeFromTheBytesThatMatchedTheManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), "fla_verified_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Save(Color.Red);
            var captured = ArtifactFiles.Sha256(path);
            using var verified = VerifiedImage.Read(path, captured, "changed");

            Save(Color.Blue);
            Assert.Equal(Color.Red.ToArgb(), verified.GetPixel(0, 0).ToArgb());
            Assert.Throws<IOException>(() => VerifiedImage.Read(path, captured, "changed"));
        }
        finally
        {
            File.Delete(path);
        }

        void Save(Color color)
        {
            using var bitmap = new Bitmap(2, 2);
            bitmap.SetPixel(0, 0, color);
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
