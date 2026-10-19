using System.Reflection.PortableExecutable;
using System.Text;
using Iced.Intel;

static class Xref
{
    public static void Run(string dll, string[] names, int before, int after) => Scan(dll, null, names, before, after);
    public static void Scan(string dll, Action<List<Instruction>, int, Func<ulong, string?>> cb) => Scan(dll, cb, Array.Empty<string>(), 0, 0);
    static void Scan(string dll, Action<List<Instruction>, int, Func<ulong, string?>>? cb, string[] names, int before, int after)
    {
        var bytes = File.ReadAllBytes(dll);
        using var pe = new PEReader(new MemoryStream(bytes));
        ulong imageBase = (ulong)pe.PEHeaders.PEHeader!.ImageBase;
        var secs = pe.PEHeaders.SectionHeaders.ToArray();
        int RvaToOff(ulong rva)
        {
            foreach (var s in secs)
                if (rva >= (ulong)s.VirtualAddress && rva < (ulong)(s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData)))
                {
                    ulong d = rva - (ulong)s.VirtualAddress;
                    if (d >= (ulong)s.SizeOfRawData) return -1;
                    return s.PointerToRawData + (int)d;
                }
            return -1;
        }
        string? CStr(ulong va)
        {
            int off = RvaToOff(va - imageBase);
            if (off < 0) return null;
            int end = off;
            while (end < bytes.Length && bytes[end] != 0 && end - off < 200) end++;
            if (end == off || end - off >= 200) return null;
            for (int i = off; i < end; i++) if (bytes[i] < 0x20 || bytes[i] > 0x7e) return null;
            return Encoding.ASCII.GetString(bytes, off, end - off);
        }
        string? FloatAt(ulong va, MemorySize size)
        {
            int off = RvaToOff(va - imageBase);
            if (off < 0 || off + 8 > bytes.Length) return null;
            if (size == MemorySize.Float32 || size == MemorySize.Packed128_Float32) return BitConverter.ToSingle(bytes, off).ToString("R");
            if (size == MemorySize.Float64) return BitConverter.ToDouble(bytes, off).ToString("R");
            return null;
        }
        var text = secs.First(s => s.Name == ".text");
        var code = new ByteArrayCodeReader(bytes, text.PointerToRawData, text.SizeOfRawData);
        var dec = Iced.Intel.Decoder.Create(64, code, imageBase + (ulong)text.VirtualAddress);
        var list = new List<Instruction>(4_000_000);
        while (code.CanReadByte) { dec.Decode(out var ins); list.Add(ins); }
        var fmt = new NasmFormatter();
        string Show(in Instruction ins)
        {
            var o = new StringOutput();
            fmt.Format(ins, o);
            var s = $"{ins.IP:X}  {o.ToStringAndReset()}";
            if (ins.IsIPRelativeMemoryOperand)
            {
                var t = ins.IPRelativeMemoryAddress;
                var str = CStr(t);
                if (str != null) s += $"   ; \"{str}\"";
                else { var f = FloatAt(t, ins.MemorySize); if (f != null) s += $"   ; {ins.MemorySize}={f}"; }
            }
            return s;
        }
        if (cb != null) { for (int k = 0; k < list.Count; k++) if (list[k].IsIPRelativeMemoryOperand) cb(list, k, CStr); return; }
        if (names.Length == 1 && names[0] == "@eight") { for (int k = 0; k + 80 < list.Count; k++) { var set = new HashSet<(Register, ulong)>(); int divs = 0; for (int j = k; j < k + 80; j++) { var m = list[j]; if ((m.Mnemonic == Mnemonic.Movss || m.Mnemonic == Mnemonic.Mulss || m.Mnemonic == Mnemonic.Addss || m.Mnemonic == Mnemonic.Divss || m.Mnemonic == Mnemonic.Subss) && m.Op1Kind == OpKind.Memory && m.MemoryBase != Register.RIP && m.MemoryBase != Register.RSP && m.MemoryIndex == Register.None) set.Add((m.MemoryBase, m.MemoryDisplacement64)); if (m.Mnemonic == Mnemonic.Divss) divs++; } bool hit = false; foreach (var (b, d) in set) { bool all = true; for (ulong o = 0; o < 32; o += 4) if (!set.Contains((b, d + o))) { all = false; break; } if (all) { hit = true; break; } } if (hit && divs >= 2) { Console.WriteLine("===== eight"); for (int j = k; j < k + 80; j++) Console.WriteLine(Show(list[j])); k += 80; } } return; }
        if (names.Length == 2 && names[0] == "@call") { ulong tgt = Convert.ToUInt64(names[1], 16); for (int k = 0; k < list.Count; k++) { var c = list[k]; if (c.Mnemonic == Mnemonic.Call && c.Op0Kind == OpKind.NearBranch64 && c.NearBranch64 == tgt) { Console.WriteLine("===== call"); for (int j = Math.Max(0, k - 12); j <= Math.Min(list.Count - 1, k + 3); j++) Console.WriteLine(Show(list[j])); } } return; }
        if (names.Length == 1 && names[0] == "@hable") { for (int k = 4; k < list.Count; k++) { var d = list[k]; if (d.Mnemonic != Mnemonic.Divss || d.Op1Kind != OpKind.Memory) continue; var dsp = d.MemoryDisplacement64; for (int q = k - 4; q < k; q++) { var m = list[q]; if (m.Mnemonic == Mnemonic.Movss && m.Op1Kind == OpKind.Memory && m.MemoryBase == d.MemoryBase && m.MemoryBase != Register.RIP && m.MemoryBase != Register.RSP && m.MemoryDisplacement64 + 4 == dsp) { Console.WriteLine("===== hit"); for (int j = Math.Max(0, k - 30); j < Math.Min(list.Count, k + 30); j++) Console.WriteLine(Show(list[j])); break; } } } return; }
        if (names.Length == 3 && names[0] == "@") { ulong a = Convert.ToUInt64(names[1], 16), b = Convert.ToUInt64(names[2], 16); foreach (var x in list) if (x.IP >= a && x.IP < b) Console.WriteLine(Show(x)); return; }
        var want = new HashSet<string>(names);
        for (int i = 0; i < list.Count; i++)
        {
            var ins = list[i];
            if (!ins.IsIPRelativeMemoryOperand) continue;
            var ta = ins.IPRelativeMemoryAddress;
            var str = CStr(ta);
            var hit = names.FirstOrDefault(n => n.StartsWith("0x") && ta >= Convert.ToUInt64(n, 16) && ta < Convert.ToUInt64(n, 16) + 0x60);
            if (hit != null) str = $"{hit}+{ta - Convert.ToUInt64(hit, 16):X}";
            else if (str == null || !want.Contains(str)) continue;
            Console.WriteLine($"===== {str}");
            for (int j = Math.Max(0, i - before); j < Math.Min(list.Count, i + after); j++)
                Console.WriteLine((j == i ? ">> " : "   ") + Show(list[j]));
        }
    }
}
static class ConVars
{
    // Compact: for every lea rdx,"name" followed (within 3) by a call, report r8 default / r9 flags / rax help found within 12 previous instructions.
    public static void Run(string dll, System.Text.RegularExpressions.Regex rx)
    {
        Xref.Scan(dll, (list, i, cstr) =>
        {
            var ins = list[i];
            if (ins.Op0Register != Register.RDX || ins.Mnemonic != Mnemonic.Lea) return;
            var name = cstr(ins.IPRelativeMemoryAddress);
            if (name == null || !rx.IsMatch(name)) return;
            bool call = false;
            for (int j = i + 1; j < Math.Min(list.Count, i + 4); j++) if (list[j].Mnemonic == Mnemonic.Call) call = true;
            if (!call) return;
            string def = "?", flags = "?", help = "", obj = "?";
            for (int j = i + 1; j < Math.Min(list.Count, i + 3); j++) if (list[j].Mnemonic == Mnemonic.Lea && list[j].Op0Register == Register.RCX) obj = list[j].IPRelativeMemoryAddress.ToString("X");
            for (int j = i - 1; j >= Math.Max(0, i - 14); j--)
            {
                var p = list[j];
                if (p.Mnemonic == Mnemonic.Int3 || p.Mnemonic == Mnemonic.Ret || p.Mnemonic == Mnemonic.Jmp) break;
                if (p.Mnemonic == Mnemonic.Lea && p.Op0Register == Register.R8 && def == "?") def = "\"" + (cstr(p.IPRelativeMemoryAddress) ?? "<nonstr>") + "\"";
                if (p.Op0Register == Register.R9D && flags == "?") flags = p.Mnemonic == Mnemonic.Xor ? "0" : p.Immediate32.ToString("X");
                if (p.Mnemonic == Mnemonic.Lea && p.Op0Register == Register.RAX && help == "") help = cstr(p.IPRelativeMemoryAddress) ?? "";
            }
            Console.WriteLine($"{name}	{def}	flags={flags}	obj={obj}	{help}");
        });
    }
}
