using SteamDatabase.ValvePak;
using ValveResourceFormat;

const string Game = @"C:\Games\Half-Life - Alyx\game";
var mode = args[0];

Package Open(string mod, string name = "pak01_dir.vpk")
{
    var p = new Package();
    p.Read(Path.Combine(Game, mod, name));
    return p;
}

switch (mode)
{
    case "list": // list <mod> <vpkname> <regex>
    {
        using var pak = Open(args[1], args[2]);
        var rx = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var kv in pak.Entries)
            foreach (var e in kv.Value)
            {
                var full = e.GetFullPath();
                if (rx.IsMatch(full)) Console.WriteLine($"{full}\t{e.TotalLength}");
            }
        break;
    }
    case "dump": // dump <mod> <vpkname> <path> <outfile>
    {
        using var pak = Open(args[1], args[2]);
        var e = pak.FindEntry(args[3]) ?? throw new Exception("not found");
        pak.ReadEntry(e, out byte[] data);
        File.WriteAllBytes(args[4], data);
        Console.WriteLine($"{data.Length} bytes");
        break;
    }
    case "shader": // shader <mod> <vcs path in vpk> <outdir> [maxBlobs]
    {
        using var pak = Open(args[1], "shaders_pc_dir.vpk");
        var e = pak.FindEntry(args[2]) ?? throw new Exception("not found");
        pak.ReadEntry(e, out byte[] data);
        var outDir = args[3];
        Directory.CreateDirectory(outDir);
        int maxBlobs = args.Length > 4 ? int.Parse(args[4]) : int.MaxValue;
        var name = Path.GetFileName(args[2]);
        using var prog = new ValveResourceFormat.CompiledShader.VfxProgramData();
        prog.Read(name, new MemoryStream(data));
        using (var sw = new StringWriter())
        {
            using var w = new IndentedTextWriter(sw);
            prog.PrintSummary(w, prog);
            File.WriteAllText(Path.Combine(outDir, name + ".summary.txt"), sw.ToString());
        }
        var seen = new HashSet<string>();
        int n = 0;
        foreach (var id in prog.StaticComboEntries.Keys.OrderBy(k => k))
        {
            if (n >= maxBlobs) break;
            var combo = prog.GetStaticCombo(id);
            foreach (var sf in combo.ShaderFiles)
            {
                if (n >= maxBlobs) break;
                if (sf.IsEmpty()) continue;
                var bc = sf.Bytecode;
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bc));
                if (!seen.Add(hash)) continue;
                var text = Dxbc.Disassemble(bc);
                File.WriteAllText(Path.Combine(outDir, $"{name}.s{id}.f{sf.ShaderFileId}.asm"), text);
                n++;
            }
        }
        Console.WriteLine($"{n} unique blobs, {prog.StaticComboEntries.Count} static combos");
        break;
    }
    case "cvars": ConVars.Run(args[1], new System.Text.RegularExpressions.Regex(args[2])); break;
    case "imp": Imp.Run(args[1], Convert.ToUInt64(args[2], 16)); break;
    case "xref": Xref.Run(args[1], args[2].Split(","), int.Parse(args[3]), int.Parse(args[4])); break;
    case "lut": // lut <mod> <path> <out.raw>
    {
        using var pak = Open(args[1]);
        var e = pak.FindEntry(args[2]) ?? throw new Exception("nf");
        pak.ReadEntry(e, out byte[] data);
        using var res = new Resource { FileName = args[2] };
        res.Read(new MemoryStream(data));
        var t = (ValveResourceFormat.ResourceTypes.Texture)res.DataBlock!;
        var buf = new byte[t.CalculateBufferSizeForMipLevel(0)];
        t.ReadTextureMipLevel(buf, 0);
        File.WriteAllBytes(args[3], buf);
        Console.WriteLine($"{t.Width}x{t.Height}x{t.Depth} {t.Format} mips={t.NumMipLevels} bytes={buf.Length} flags={t.Flags}");
        break;
    }
    case "res": // res <mod> <vpkname> <regex> : print every matching resource's data block
    {
        using var pak = Open(args[1], args[2]);
        var rx = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var kv in pak.Entries)
            foreach (var e in kv.Value)
            {
                var full = e.GetFullPath();
                if (!rx.IsMatch(full)) continue;
                pak.ReadEntry(e, out byte[] data);
                using var res = new Resource { FileName = full };
                res.Read(new MemoryStream(data));
                Console.WriteLine($"===== {full}");
                Console.WriteLine(res.DataBlock?.ToString());
            }
        break;
    }
}
