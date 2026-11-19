using System.Collections;
using System.IO;
using System.Resources;
using System.Windows.Media.Imaging;

namespace TouchPanel.Tests.Assets;

/// <summary>
/// Verifies the shipped application icon asset: the exact ICO frame set, a sane ICONDIR, a
/// decodable file, and square non-empty PNG previews. The ICO bytes are read from the compiled
/// WPF resource (the same bytes the shell and the windows consume), not from the source folder.
/// </summary>
public sealed class AppIconAssetTests
{
    private static readonly int[] ExpectedFrameSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [Fact]
    public void PackagedIcon_HasTheExactExpectedFrameSet()
    {
        byte[] bytes = ReadPackagedAsset("Assets/touchpanel.ico");

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // type: icon
        int count = reader.ReadUInt16();
        Assert.Equal(ExpectedFrameSizes.Length, count);

        var frameSizes = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            reader.ReadByte(); // palette colours
            byte entryReserved = reader.ReadByte();
            ushort planes = reader.ReadUInt16();
            ushort bitCount = reader.ReadUInt16();
            uint bytesInResource = reader.ReadUInt32();
            uint imageOffset = reader.ReadUInt32();

            int actualWidth = width == 0 ? 256 : width;
            int actualHeight = height == 0 ? 256 : height;

            Assert.True(actualWidth == actualHeight, $"frame {i} is not square");
            Assert.Contains(actualWidth, ExpectedFrameSizes);
            Assert.Equal(0, entryReserved);
            Assert.Equal(1, planes);
            Assert.Equal(32, bitCount);
            Assert.True(bytesInResource > 0, $"frame {actualWidth} is empty");
            Assert.InRange((long)imageOffset + bytesInResource, 1, bytes.Length);

            frameSizes.Add(actualWidth);
        }

        Assert.Equal(ExpectedFrameSizes, frameSizes.OrderBy(size => size).ToArray());
        Assert.Equal(ExpectedFrameSizes.Length, frameSizes.Distinct().Count());
    }

    [Fact]
    public void PackagedIcon_LoadsWithoutCorruption()
    {
        byte[] bytes = ReadPackagedAsset("Assets/touchpanel.ico");
        using var stream = new MemoryStream(bytes);

        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        Assert.Equal(ExpectedFrameSizes.Length, decoder.Frames.Count);
        Assert.Equal(
            ExpectedFrameSizes,
            decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(size => size).ToArray());
        Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight));
    }

    [Theory]
    [InlineData("touchpanel-16.png", 16)]
    [InlineData("touchpanel-256.png", 256)]
    public void PreviewPng_IsSquareAndNonEmpty(string fileName, int expectedSize)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        Assert.True(File.Exists(path), $"missing preview {path}");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.NotEmpty(bytes);

        using var stream = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        Assert.NotEmpty(decoder.Frames);

        BitmapFrame frame = decoder.Frames[0];
        Assert.Equal(expectedSize, frame.PixelWidth);
        Assert.Equal(expectedSize, frame.PixelHeight);
    }

    /// <summary>
    /// Reads a WPF <c>&lt;Resource&gt;</c> straight out of the compiled <c>TouchPanel.g.resources</c>
    /// stream, so the test inspects exactly the bytes embedded in the built application assembly.
    /// </summary>
    private static byte[] ReadPackagedAsset(string relativePath)
    {
        var assembly = typeof(MainWindow).Assembly;
        using Stream? resources = assembly.GetManifestResourceStream("TouchPanel.g.resources");
        Assert.NotNull(resources);

        string key = relativePath.Replace('\\', '/').ToLowerInvariant();
        byte[]? found = null;
        using (var reader = new ResourceReader(resources!))
        {
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is string name &&
                    string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    // WPF stores binary resources as an UnmanagedMemoryStream; ResourceWriter
                    // deployments may hand back a byte[] instead. Accept both.
                    if (entry.Value is Stream stream)
                    {
                        using var memory = new MemoryStream();
                        stream.CopyTo(memory);
                        found = memory.ToArray();
                    }
                    else
                    {
                        found = entry.Value as byte[];
                    }

                    break;
                }
            }
        }

        Assert.NotNull(found);
        Assert.NotEmpty(found);
        return found!;
    }
}
