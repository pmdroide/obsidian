using System.Diagnostics;
using System.IO.Compression;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// Checks for the AI texture generator (Docs/Research/AiTextures/PLAN.md). <see cref="Run"/> covers the Content.mgcb
/// entries for generated maps (task 0.3); <see cref="RunGraphics"/> builds a grayscale PNG with the real mgcb and checks
/// what reaches the texture.
/// </summary>
internal static partial class TexGenChecks
{
    private const string Header =
        "\n#----------------------------- Global Properties ----------------------------#\n\n" +
        "/outputDir:bin/$(Platform)\n/intermediateDir:obj/$(Platform)\n/platform:Windows\n/config:\n/profile:HiDef\n/compress:False\n\n" +
        "#---------------------------------- Content ---------------------------------#\n";

    public static void Run()
    {
        RunManifest();
        RunIds();
        RunProtocol();
        RunSettings();
        RunImporter();
        RunRunner();
    }

    private static void RunManifest()
    {
        string root = NewTempFolder();
        try
        {
            string manifest = Path.Combine(root, "Content.mgcb");
            File.WriteAllText(manifest, Header);

            var added = ContentManifest.RegisterGeneratedTextures(new[]
            {
                "Materials/Generated/stone_ab12/stone_BaseColor.png", "Materials/Generated/stone_ab12/stone_Rough.png",
                "Materials/Generated/stone_ab12/texgen.json", "Materials/Generated/stone_ab12/model.fbx",
            }, manifest);
            Check(added.Count == 2, "generated texture entries are written for PNG maps only (no json, no models)");

            string text = File.ReadAllText(manifest);
            string entry = text.Substring(text.IndexOf("#begin Materials/Generated/stone_ab12/stone_BaseColor.png", StringComparison.Ordinal));
            entry = entry.Substring(0, entry.IndexOf("/build:", StringComparison.Ordinal));
            Check(entry.Contains("/processorParam:ColorKeyEnabled=False") && entry.Contains("/processorParam:PremultiplyAlpha=False") &&
                  entry.Contains("/processorParam:GenerateMipmaps=True") && entry.Contains("/processorParam:TextureFormat=Color"),
                "a generated map has no colour key, no premultiplied alpha and mipmaps on");

            ContentManifest.RegisterGeneratedTextures(new[] { "Materials/Generated/stone_ab12/stone_BaseColor.png" }, manifest);
            Check(CountOf(File.ReadAllText(manifest), "#begin Materials/Generated/stone_ab12/stone_BaseColor.png") == 1,
                "registering the same generated map twice does not duplicate the entry");

        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ } }
    }

    public static void RunGraphics(GraphicsDevice graphics, ContentManager contentUnused)
    {
        string root = NewTempFolder();
        try
        {
            // 4x4 grayscale (L) = 128, and a 2x2 RGB image with a pure magenta pixel (the old colour key).
            WritePng(Path.Combine(root, "gray128.png"), 4, 4, 0, Enumerable.Repeat((byte)128, 16).ToArray());
            WritePng(Path.Combine(root, "magenta.png"), 2, 2, 2,
                new byte[] { 255, 0, 255, 10, 20, 30, 40, 50, 60, 70, 80, 90 });
            string manifest = Path.Combine(root, "Content.mgcb");
            File.WriteAllText(manifest, Header);
            ContentManifest.RegisterGeneratedTextures(new[] { "gray128.png", "magenta.png" }, manifest);

            var (exe, prefix) = AssetImporter.LocateMgcb();
            var psi = new ProcessStartInfo
            {
                FileName = exe, Arguments = prefix + "/@:Content.mgcb /workingDir:" + AssetImporter.EscapeArg(root),
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (var process = Process.Start(psi)!)
            {
                string log = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                Check(process.ExitCode == 0, "mgcb builds a grayscale and an RGB PNG as generated textures" +
                    (process.ExitCode == 0 ? "" : ": " + log));
            }

            var services = new GameServiceContainer();
            services.AddService<IGraphicsDeviceService>(new GraphicsDeviceServiceStub(graphics));
            using var content = new ContentManager(services, Path.Combine(root, "bin", "Windows"));
            var gray = content.Load<Texture2D>("gray128");
            var pixels = new Color[gray.Width * gray.Height];
            gray.GetData(pixels);
            Check(pixels.All(p => p.R == 128 && p.A == 255), "a grayscale PNG of 128 reaches the shader's .r as 128");

            var magenta = content.Load<Texture2D>("magenta");
            var colors = new Color[magenta.Width * magenta.Height];
            magenta.GetData(colors);
            Check(colors[0] == new Color(255, 0, 255, 255), "a pure magenta pixel stays opaque magenta (no colour key)");
            Check(colors[1] == new Color(10, 20, 30, 255), "colours are not premultiplied or altered");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* temp folder */ } }
    }

    // ---- Helpers ----

    private sealed class GraphicsDeviceServiceStub : IGraphicsDeviceService
    {
        public GraphicsDeviceServiceStub(GraphicsDevice device) => GraphicsDevice = device;
        public GraphicsDevice GraphicsDevice { get; }
        public event EventHandler<EventArgs>? DeviceCreated { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceDisposing { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceReset { add { } remove { } }
        public event EventHandler<EventArgs>? DeviceResetting { add { } remove { } }
    }

    private static string NewTempFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-texgen-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    // Minimal 8-bit PNG writer: colorType 0 = grayscale, 2 = RGB.
    private static void WritePng(string path, int width, int height, int colorType, byte[] data)
    {
        int channels = colorType == 0 ? 1 : 3;
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(data, y * width * channels, width * channels);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());

        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        WriteBigEndian(header, 0, width); WriteBigEndian(header, 4, height);
        header[8] = 8; header[9] = (byte)colorType;
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream stream, string type, byte[] body)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, body.Length);
        stream.Write(length);
        var typed = new byte[4 + body.Length];
        for (int i = 0; i < 4; i++) typed[i] = (byte)type[i];
        body.CopyTo(typed, 4);
        stream.Write(typed);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(typed)));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
