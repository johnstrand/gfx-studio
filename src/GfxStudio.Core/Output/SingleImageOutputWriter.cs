using System.Buffers.Binary;
using SkiaSharp;

namespace GfxStudio.Core.Output;

/// <summary>Encodes a rendered bitmap according to its output file extension.</summary>
public sealed class SingleImageOutputWriter
{
    public byte[] Encode(SKBitmap bitmap, string filename)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension == ".bmp")
        {
            return EncodeBmp(bitmap);
        }

        if (extension == ".gif")
        {
            return Gif.GfxGifEncoder.Encode([bitmap]);
        }

        var format = extension switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => throw new NotSupportedException($"Unsupported output image format '{extension}'.")
        };

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100)
            ?? throw new InvalidOperationException($"SkiaSharp could not encode '{extension}' images.");
        return data.ToArray();
    }

    public void Write(SKBitmap bitmap, string path)
    {
        var encoded = Encode(bitmap, path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, encoded);
    }

    private static byte[] EncodeBmp(SKBitmap bitmap)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        const int bytesPerPixel = 4;
        var imageBytes = checked(bitmap.Width * bitmap.Height * bytesPerPixel);
        var output = new byte[checked(fileHeaderSize + infoHeaderSize + imageBytes)];
        output[0] = (byte)'B';
        output[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), output.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(10), fileHeaderSize + infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(14), infoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(18), bitmap.Width);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(22), bitmap.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(34), imageBytes);

        var offset = fileHeaderSize + infoHeaderSize;
        for (var y = bitmap.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                output[offset++] = color.Blue;
                output[offset++] = color.Green;
                output[offset++] = color.Red;
                output[offset++] = color.Alpha;
            }
        }

        return output;
    }
}
