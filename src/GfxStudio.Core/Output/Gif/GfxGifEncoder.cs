using System.Buffers.Binary;
using SkiaSharp;

namespace GfxStudio.Core.Output.Gif;

/// <summary>
/// Self-contained managed GIF89a animated image encoder with Median-Cut 256-color quantization,
/// optional Floyd-Steinberg dithering, and variable-width LZW compression.
/// </summary>
public sealed class GfxGifEncoder
{
    private const int PaletteSize = 256;
    private const int MaxOpaqueColors = 255;
    private const byte TransparentColorIndex = 255;

    public static int ComputeDelayCentiseconds(double fps, int? frameDelayMs = null)
    {
        if (frameDelayMs is { } overrideMs)
        {
            return Math.Clamp((int)Math.Round(overrideMs / 10.0, MidpointRounding.AwayFromZero), 1, ushort.MaxValue);
        }

        var safeFps = fps > 0.0 && double.IsFinite(fps) ? fps : 15.0;
        return Math.Clamp((int)Math.Round(100.0 / safeFps, MidpointRounding.AwayFromZero), 1, ushort.MaxValue);
    }

    public static byte[] Encode(
        IReadOnlyList<SKBitmap> frames,
        int delayCentiseconds = 7,
        int loopCount = 0,
        bool dither = true)
    {
        using var stream = new MemoryStream();
        Encode(stream, frames, delayCentiseconds, loopCount, dither);
        return stream.ToArray();
    }

    public static void Encode(
        Stream output,
        IReadOnlyList<SKBitmap> frames,
        int delayCentiseconds = 7,
        int loopCount = 0,
        bool dither = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required to encode a GIF.", nameof(frames));
        }

        var first = frames[0] ?? throw new ArgumentException("Frame bitmap cannot be null.", nameof(frames));
        var width = first.Width;
        var height = first.Height;
        if (width is <= 0 or > ushort.MaxValue || height is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), $"GIF dimensions ({width}x{height}) must be within 1..65535.");
        }

        var clampedDelay = (ushort)Math.Clamp(delayCentiseconds, 0, ushort.MaxValue);
        var clampedLoop = (ushort)Math.Clamp(loopCount, 0, ushort.MaxValue);

        var quantizedFrames = new (byte[] Palette, byte[] IndexedPixels)[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i] ?? throw new ArgumentException($"Frame {i} bitmap cannot be null.", nameof(frames));
            if (frame.Width != width || frame.Height != height)
            {
                throw new ArgumentException($"All GIF frames must match dimensions ({width}x{height}), but frame {i} is {frame.Width}x{frame.Height}.", nameof(frames));
            }

            quantizedFrames[i] = QuantizeFrame(frame, dither);
        }

        // 1. GIF89a Header
        output.Write("GIF89a"u8);

        // 2. Logical Screen Descriptor (with 256-entry Global Color Table from Frame 0)
        Span<byte> lsd = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[0..2], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[2..4], (ushort)height);
        lsd[4] = 0xF7; // Global Color Table flag (1), 8-bit color resolution (7 << 4), sort (0), size 256 (7)
        lsd[5] = TransparentColorIndex;
        lsd[6] = 0;
        output.Write(lsd);

        // 3. Global Color Table (768 bytes)
        output.Write(quantizedFrames[0].Palette);

        // 4. NETSCAPE2.0 Application Extension for looping
        WriteNetscapeLoopExtension(output, clampedLoop);

        // 5. Frames
        for (var i = 0; i < quantizedFrames.Length; i++)
        {
            var (palette, indexedPixels) = quantizedFrames[i];
            WriteGraphicControlExtension(output, clampedDelay, TransparentColorIndex);
            WriteImageDescriptor(output, (ushort)width, (ushort)height);
            output.Write(palette);
            WriteLzwImageData(output, indexedPixels);
        }

        // 6. GIF Trailer
        output.WriteByte(0x3B);
    }

    private static void WriteNetscapeLoopExtension(Stream output, ushort loopCount)
    {
        Span<byte> block = stackalloc byte[19];
        block[0] = 0x21; // Extension Introducer
        block[1] = 0xFF; // Application Extension Label
        block[2] = 0x0B; // Block Size (11 bytes)
        "NETSCAPE2.0"u8.CopyTo(block[3..14]);
        block[14] = 0x03; // Sub-block size
        block[15] = 0x01; // Loop sub-block ID
        BinaryPrimitives.WriteUInt16LittleEndian(block[16..18], loopCount);
        block[18] = 0x00; // Block terminator
        output.Write(block);
    }

    private static void WriteGraphicControlExtension(Stream output, ushort delayCentiseconds, byte transparentIndex)
    {
        Span<byte> gce = stackalloc byte[8];
        gce[0] = 0x21; // Extension Introducer
        gce[1] = 0xF9; // Graphic Control Label
        gce[2] = 0x04; // Byte Size
        gce[3] = 0x09; // Disposal = 2 (Restore to background: 2 << 2 = 0x08) | Transparent Color Flag (0x01)
        BinaryPrimitives.WriteUInt16LittleEndian(gce[4..6], delayCentiseconds);
        gce[6] = transparentIndex;
        gce[7] = 0x00; // Block terminator
        output.Write(gce);
    }

    private static void WriteImageDescriptor(Stream output, ushort width, ushort height)
    {
        Span<byte> desc = stackalloc byte[10];
        desc[0] = 0x2C; // Image Separator
        BinaryPrimitives.WriteUInt16LittleEndian(desc[1..3], 0); // Left
        BinaryPrimitives.WriteUInt16LittleEndian(desc[3..5], 0); // Top
        BinaryPrimitives.WriteUInt16LittleEndian(desc[5..7], width);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[7..9], height);
        desc[9] = 0x87; // Local Color Table flag (0x80) | 256 entries (0x07)
        output.Write(desc);
    }

    private static (byte[] Palette, byte[] IndexedPixels) QuantizeFrame(SKBitmap bitmap, bool dither)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelCount = width * height;

        var rChannel = new byte[pixelCount];
        var gChannel = new byte[pixelCount];
        var bChannel = new byte[pixelCount];
        var isTransparent = new bool[pixelCount];

        var histogram = new Dictionary<int, int>();

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x;
                var color = bitmap.GetPixel(x, y);
                if (color.Alpha < 128)
                {
                    isTransparent[idx] = true;
                    continue;
                }

                rChannel[idx] = color.Red;
                gChannel[idx] = color.Green;
                bChannel[idx] = color.Blue;

                var rgbKey = (color.Red << 16) | (color.Green << 8) | color.Blue;
                histogram[rgbKey] = histogram.GetValueOrDefault(rgbKey) + 1;
            }
        }

        var paletteEntries = BuildMedianCutPalette(histogram, MaxOpaqueColors);
        var paletteBytes = new byte[PaletteSize * 3];
        for (var i = 0; i < paletteEntries.Count; i++)
        {
            var (r, g, b) = paletteEntries[i];
            paletteBytes[i * 3] = r;
            paletteBytes[i * 3 + 1] = g;
            paletteBytes[i * 3 + 2] = b;
        }

        var indexedPixels = new byte[pixelCount];
        if (paletteEntries.Count == 0)
        {
            Array.Fill(indexedPixels, TransparentColorIndex);
            return (paletteBytes, indexedPixels);
        }

        // Exact lookup map when unique colors <= 255
        var exactLookup = new Dictionary<int, byte>(paletteEntries.Count);
        for (var i = 0; i < paletteEntries.Count; i++)
        {
            var (r, g, b) = paletteEntries[i];
            var key = (r << 16) | (g << 8) | b;
            exactLookup.TryAdd(key, (byte)i);
        }

        // 15-bit RGB nearest-color cache (32,768 entries)
        var nearestCache = new short[32768];
        Array.Fill(nearestCache, (short)-1);

        var useDithering = dither && histogram.Count > MaxOpaqueColors;
        if (!useDithering)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                if (isTransparent[i])
                {
                    indexedPixels[i] = TransparentColorIndex;
                    continue;
                }

                var r = rChannel[i];
                var g = gChannel[i];
                var b = bChannel[i];
                var key = (r << 16) | (g << 8) | b;
                indexedPixels[i] = exactLookup.TryGetValue(key, out var exactIdx)
                    ? exactIdx
                    : FindNearestPaletteIndex(r, g, b, paletteEntries, nearestCache);
            }

            return (paletteBytes, indexedPixels);
        }

        // Floyd-Steinberg error-diffusion dithering
        var errR = new float[pixelCount];
        var errG = new float[pixelCount];
        var errB = new float[pixelCount];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x;
                if (isTransparent[idx])
                {
                    indexedPixels[idx] = TransparentColorIndex;
                    continue;
                }

                var r = ClampByte(rChannel[idx] + errR[idx]);
                var g = ClampByte(gChannel[idx] + errG[idx]);
                var b = ClampByte(bChannel[idx] + errB[idx]);

                var key = (r << 16) | (g << 8) | b;
                var paletteIdx = exactLookup.TryGetValue(key, out var exactIdx)
                    ? exactIdx
                    : FindNearestPaletteIndex(r, g, b, paletteEntries, nearestCache);

                indexedPixels[idx] = paletteIdx;

                var (pr, pg, pb) = paletteEntries[paletteIdx];
                var dr = r - pr;
                var dg = g - pg;
                var db = b - pb;

                if (x + 1 < width && !isTransparent[idx + 1])
                {
                    errR[idx + 1] += dr * (7f / 16f);
                    errG[idx + 1] += dg * (7f / 16f);
                    errB[idx + 1] += db * (7f / 16f);
                }

                if (y + 1 < height)
                {
                    var nextRow = idx + width;
                    if (x > 0 && !isTransparent[nextRow - 1])
                    {
                        errR[nextRow - 1] += dr * (3f / 16f);
                        errG[nextRow - 1] += dg * (3f / 16f);
                        errB[nextRow - 1] += db * (3f / 16f);
                    }

                    if (!isTransparent[nextRow])
                    {
                        errR[nextRow] += dr * (5f / 16f);
                        errG[nextRow] += dg * (5f / 16f);
                        errB[nextRow] += db * (5f / 16f);
                    }

                    if (x + 1 < width && !isTransparent[nextRow + 1])
                    {
                        errR[nextRow + 1] += dr * (1f / 16f);
                        errG[nextRow + 1] += dg * (1f / 16f);
                        errB[nextRow + 1] += db * (1f / 16f);
                    }
                }
            }
        }

        return (paletteBytes, indexedPixels);
    }

    private static byte ClampByte(float value)
    {
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(rounded, 0, 255);
    }

    private static byte FindNearestPaletteIndex(
        byte r,
        byte g,
        byte b,
        List<(byte R, byte G, byte B)> palette,
        short[] cache)
    {
        var cacheKey = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
        var cached = cache[cacheKey];
        if (cached >= 0)
        {
            return (byte)cached;
        }

        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            var (pr, pg, pb) = palette[i];
            var dr = r - pr;
            var dg = g - pg;
            var db = b - pb;
            var dist = (dr * dr) + (dg * dg) + (db * db);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestIndex = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }

        cache[cacheKey] = (short)bestIndex;
        return (byte)bestIndex;
    }

    private static List<(byte R, byte G, byte B)> BuildMedianCutPalette(Dictionary<int, int> histogram, int maxColors)
    {
        if (histogram.Count == 0)
        {
            return [(0, 0, 0)];
        }

        var items = new List<ColorCount>(histogram.Count);
        foreach (var (rgb, count) in histogram)
        {
            var r = (byte)((rgb >> 16) & 0xFF);
            var g = (byte)((rgb >> 8) & 0xFF);
            var b = (byte)(rgb & 0xFF);
            items.Add(new ColorCount(r, g, b, count));
        }

        // Sort deterministically by RGB first
        items.Sort(static (a, b) =>
        {
            var cmp = a.R.CompareTo(b.R);
            if (cmp != 0) return cmp;
            cmp = a.G.CompareTo(b.G);
            return cmp != 0 ? cmp : a.B.CompareTo(b.B);
        });

        if (items.Count <= maxColors)
        {
            return items.Select(c => (c.R, c.G, c.B)).ToList();
        }

        var boxes = new List<ColorBox> { new(items, 0, items.Count) };
        while (boxes.Count < maxColors)
        {
            var bestBoxIndex = -1;
            var bestScore = 0;

            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Length <= 1 || box.MaxRange == 0)
                {
                    continue;
                }

                if (box.MaxRange > bestScore)
                {
                    bestScore = box.MaxRange;
                    bestBoxIndex = i;
                }
            }

            if (bestBoxIndex < 0)
            {
                break;
            }

            var target = boxes[bestBoxIndex];
            var (left, right) = target.Split();
            boxes[bestBoxIndex] = left;
            boxes.Add(right);
        }

        var result = new List<(byte R, byte G, byte B)>(boxes.Count);
        foreach (var box in boxes)
        {
            result.Add(box.AverageColor());
        }

        return result;
    }

    private static void WriteLzwImageData(Stream output, ReadOnlySpan<byte> indexedPixels)
    {
        const int minCodeSize = 8;
        const int clearCode = 1 << minCodeSize; // 256
        const int eoiCode = clearCode + 1;      // 257
        const int maxDictionaryCode = 4096;

        output.WriteByte(minCodeSize);

        var dict = new int[maxDictionaryCode * 256];
        Array.Fill(dict, -1);

        var codeSize = minCodeSize + 1; // 9 bits
        var nextCode = eoiCode + 1;     // 258

        var bitBuffer = 0;
        var bitCount = 0;
        Span<byte> subBlock = stackalloc byte[255];
        var subBlockLength = 0;

        void EmitByte(byte b, Span<byte> blockBuf)
        {
            blockBuf[subBlockLength++] = b;
            if (subBlockLength == 255)
            {
                output.WriteByte(255);
                output.Write(blockBuf);
                subBlockLength = 0;
            }
        }

        void EmitCode(int code, int bits, Span<byte> blockBuf)
        {
            bitBuffer |= code << bitCount;
            bitCount += bits;
            while (bitCount >= 8)
            {
                EmitByte((byte)(bitBuffer & 0xFF), blockBuf);
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        EmitCode(clearCode, codeSize, subBlock);

        if (indexedPixels.Length > 0)
        {
            int prefix = indexedPixels[0];

            for (var i = 1; i < indexedPixels.Length; i++)
            {
                int pixel = indexedPixels[i];
                var dictIndex = (prefix << 8) | pixel;
                var existingCode = dict[dictIndex];

                if (existingCode >= 0)
                {
                    prefix = existingCode;
                }
                else
                {
                    EmitCode(prefix, codeSize, subBlock);

                    if (nextCode < maxDictionaryCode)
                    {
                        dict[dictIndex] = nextCode;
                        if (nextCode == (1 << codeSize) && codeSize < 12)
                        {
                            codeSize++;
                        }

                        nextCode++;
                    }
                    else
                    {
                        EmitCode(clearCode, codeSize, subBlock);
                        Array.Fill(dict, -1);
                        codeSize = minCodeSize + 1;
                        nextCode = eoiCode + 1;
                    }

                    prefix = pixel;
                }
            }

            EmitCode(prefix, codeSize, subBlock);
        }

        EmitCode(eoiCode, codeSize, subBlock);

        if (bitCount > 0)
        {
            EmitByte((byte)(bitBuffer & 0xFF), subBlock);
        }

        if (subBlockLength > 0)
        {
            output.WriteByte((byte)subBlockLength);
            output.Write(subBlock[..subBlockLength]);
        }

        // Sub-block terminator
        output.WriteByte(0x00);
    }

    private readonly record struct ColorCount(byte R, byte G, byte B, int Count);

    private sealed class ColorBox
    {
        private readonly List<ColorCount> _items;
        private readonly int _start;

        public ColorBox(List<ColorCount> items, int start, int length)
        {
            _items = items;
            _start = start;
            Length = length;

            byte minR = 255, maxR = 0;
            byte minG = 255, maxG = 0;
            byte minB = 255, maxB = 0;
            long totalWeight = 0;

            for (var i = start; i < start + length; i++)
            {
                var item = items[i];
                if (item.R < minR) minR = item.R;
                if (item.R > maxR) maxR = item.R;
                if (item.G < minG) minG = item.G;
                if (item.G > maxG) maxG = item.G;
                if (item.B < minB) minB = item.B;
                if (item.B > maxB) maxB = item.B;
                totalWeight += item.Count;
            }

            RangeR = maxR - minR;
            RangeG = maxG - minG;
            RangeB = maxB - minB;
            TotalWeight = totalWeight;
            MaxRange = Math.Max(RangeR, Math.Max(RangeG, RangeB));
        }

        public int Length { get; }

        public int RangeR { get; }

        public int RangeG { get; }

        public int RangeB { get; }

        public int MaxRange { get; }

        public long TotalWeight { get; }

        public (ColorBox Left, ColorBox Right) Split()
        {
            Comparison<ColorCount> comparer = RangeR >= RangeG && RangeR >= RangeB
                ? static (a, b) => a.R.CompareTo(b.R)
                : RangeG >= RangeB
                    ? static (a, b) => a.G.CompareTo(b.G)
                    : static (a, b) => a.B.CompareTo(b.B);

            _items.Sort(_start, Length, Comparer<ColorCount>.Create(comparer));

            var halfWeight = TotalWeight / 2;
            long accumulated = 0;
            var splitOffset = 1;
            for (var i = 0; i < Length - 1; i++)
            {
                accumulated += _items[_start + i].Count;
                if (accumulated >= halfWeight)
                {
                    splitOffset = i + 1;
                    break;
                }
            }

            return (
                new ColorBox(_items, _start, splitOffset),
                new ColorBox(_items, _start + splitOffset, Length - splitOffset));
        }

        public (byte R, byte G, byte B) AverageColor()
        {
            long sumR = 0, sumG = 0, sumB = 0, total = 0;
            for (var i = _start; i < _start + Length; i++)
            {
                var c = _items[i];
                sumR += (long)c.R * c.Count;
                sumG += (long)c.G * c.Count;
                sumB += (long)c.B * c.Count;
                total += c.Count;
            }

            if (total <= 0)
            {
                return (0, 0, 0);
            }

            return (
                (byte)Math.Clamp((sumR + (total / 2)) / total, 0, 255),
                (byte)Math.Clamp((sumG + (total / 2)) / total, 0, 255),
                (byte)Math.Clamp((sumB + (total / 2)) / total, 0, 255));
        }
    }
}
