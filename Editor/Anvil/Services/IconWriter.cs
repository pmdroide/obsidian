using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Anvil.Services;

/// <summary>
/// Converts any image Avalonia can decode (png, jpg, bmp, ico, ...) into a multi-size .ico.
/// Non-square sources are fitted (not stretched) into a transparent square. Each size is
/// stored as a PNG-compressed entry, which Windows (Vista+) and the .NET resource compiler
/// both accept, so the file works for the window icon and the executable icon alike.
/// Must run on the UI thread (RenderTargetBitmap).
/// </summary>
public static class IconWriter
{
    private static readonly int[] Sizes = { 256, 64, 48, 32, 16 };

    public static void WriteIco(Bitmap source, string destinationPath)
    {
        var images = new List<byte[]>();
        foreach (int size in Sizes)
            images.Add(RenderSquarePng(source, size));

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var fs = File.Create(destinationPath);
        using var w = new BinaryWriter(fs);

        // ICONDIR
        w.Write((ushort)0);           // reserved
        w.Write((ushort)1);           // type: icon
        w.Write((ushort)images.Count);

        // ICONDIRENTRY[] — planes/bitcount are set explicitly so consumers don't have to
        // sniff the (PNG) payload for them.
        int offset = 6 + 16 * images.Count;
        for (int i = 0; i < images.Count; i++)
        {
            int size = Sizes[i];
            w.Write((byte)(size >= 256 ? 0 : size)); // width (0 = 256)
            w.Write((byte)(size >= 256 ? 0 : size)); // height
            w.Write((byte)0);                        // palette colours
            w.Write((byte)0);                        // reserved
            w.Write((ushort)1);                      // planes
            w.Write((ushort)32);                     // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }

        foreach (byte[] png in images)
            w.Write(png);
    }

    private static byte[] RenderSquarePng(Bitmap source, int size)
    {
        double sw = source.PixelSize.Width, sh = source.PixelSize.Height;
        double scale = Math.Min(size / sw, size / sh);
        double dw = sw * scale, dh = sh * scale;
        var dest = new Rect((size - dw) / 2, (size - dh) / 2, dw, dh);

        using var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(source, new Rect(source.Size), dest);

        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
