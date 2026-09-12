using System.Buffers.Binary;

namespace Looxdex.Api.Services.Detection;

/// <summary>
/// Reads pixel dimensions straight from an image header.
///
/// The detector returns boxes in pixels of the image we posted, and the Angular
/// overlay positions them in percentages, so we need the real size to convert.
/// Parsing the header avoids pulling in a full imaging library for four numbers.
/// </summary>
public static class ImageDimensionReader
{
    public static (int Width, int Height)? TryRead(ReadOnlySpan<byte> bytes)
    {
        if (TryReadPng(bytes, out var png)) return png;
        if (TryReadJpeg(bytes, out var jpeg)) return jpeg;
        if (TryReadWebp(bytes, out var webp)) return webp;
        if (TryReadGif(bytes, out var gif)) return gif;
        return null;
    }

    private static bool TryReadPng(ReadOnlySpan<byte> b, out (int, int) size)
    {
        size = default;
        // 8-byte signature, 4-byte chunk length, "IHDR", then width/height.
        if (b.Length < 24) return false;
        if (b[0] != 0x89 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;
        if (b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return false;

        var w = BinaryPrimitives.ReadInt32BigEndian(b.Slice(16, 4));
        var h = BinaryPrimitives.ReadInt32BigEndian(b.Slice(20, 4));
        if (w <= 0 || h <= 0) return false;

        size = (w, h);
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> b, out (int, int) size)
    {
        size = default;
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return false;

        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = b[i + 1];

            // Standalone markers carry no length payload.
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            // Start of scan — dimensions would have appeared before this point.
            if (marker == 0xDA) return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 2, 2));
            if (length < 2) return false;

            // SOF0-SOF15, excluding DHT (C4), JPG (C8) and DAC (CC).
            var isStartOfFrame = marker >= 0xC0 && marker <= 0xCF
                                 && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

            if (isStartOfFrame)
            {
                if (i + 9 >= b.Length) return false;
                var h = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 5, 2));
                var w = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i + 7, 2));
                if (w == 0 || h == 0) return false;
                size = (w, h);
                return true;
            }

            i += 2 + length;
        }

        return false;
    }

    private static bool TryReadWebp(ReadOnlySpan<byte> b, out (int, int) size)
    {
        size = default;
        if (b.Length < 30) return false;
        if (b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return false;
        if (b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return false;

        var format = b.Slice(12, 4);

        // Lossy: 'VP8 ' — dimensions sit after the 3-byte sync code.
        if (format[0] == 'V' && format[1] == 'P' && format[2] == '8' && format[3] == ' ')
        {
            var w = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(26, 2)) & 0x3FFF;
            var h = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(28, 2)) & 0x3FFF;
            if (w == 0 || h == 0) return false;
            size = (w, h);
            return true;
        }

        // Extended: 'VP8X' — 24-bit width-1 / height-1.
        if (format[0] == 'V' && format[1] == 'P' && format[2] == '8' && format[3] == 'X')
        {
            var w = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
            var h = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
            size = (w, h);
            return true;
        }

        return false;
    }

    private static bool TryReadGif(ReadOnlySpan<byte> b, out (int, int) size)
    {
        size = default;
        if (b.Length < 10) return false;
        if (b[0] != 'G' || b[1] != 'I' || b[2] != 'F') return false;

        var w = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(6, 2));
        var h = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(8, 2));
        if (w == 0 || h == 0) return false;

        size = (w, h);
        return true;
    }
}
