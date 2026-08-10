using System;
using System.IO;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace v2rayF.Services;

public static class QrCodeDecoder
{
    public static string? DecodeFromImageBytes(byte[] imageBytes)
    {
        if (imageBytes.Length == 0)
            return null;

        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            return bitmap is null ? null : DecodeBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    public static string? DecodeFromStream(Stream stream)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(stream);
            return bitmap is null ? null : DecodeBitmap(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeBitmap(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
            return null;

        var colors = bitmap.Pixels;
        var rgb = new byte[width * height * 3];
        var dst = 0;
        for (var i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            rgb[dst++] = c.Red;
            rgb[dst++] = c.Green;
            rgb[dst++] = c.Blue;
        }

        var source = new RGBLuminanceSource(rgb, width, height);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };

        var result = reader.Decode(source);
        return string.IsNullOrWhiteSpace(result?.Text) ? null : result.Text.Trim();
    }
}
