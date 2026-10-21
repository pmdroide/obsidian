using System.Reflection.PortableExecutable;
static class Imp
{
    public static void Run(string dll, ulong va)
    {
        var b = File.ReadAllBytes(dll);
        using var pe = new PEReader(new MemoryStream(b));
        var h = pe.PEHeaders; ulong ib = (ulong)h.PEHeader!.ImageBase;
        int Off(long rva) { foreach (var s in h.SectionHeaders) if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.VirtualSize) return (int)(rva - s.VirtualAddress + s.PointerToRawData); return -1; }
        var dir = h.PEHeader.ImportTableDirectory;
        for (int d = Off(dir.RelativeVirtualAddress); ; d += 20)
        {
            int ilt = BitConverter.ToInt32(b, d), name = BitConverter.ToInt32(b, d + 12), iat = BitConverter.ToInt32(b, d + 16);
            if (name == 0) break;
            string dn = System.Text.Encoding.ASCII.GetString(b, Off(name), 64).Split('\0')[0];
            int src = Off(ilt != 0 ? ilt : iat);
            for (int i = 0; ; i++)
            {
                long e = BitConverter.ToInt64(b, src + i * 8); if (e == 0) break;
                if (ib + (ulong)iat + (ulong)(i * 8) == va)
                    Console.WriteLine(e < 0 ? $"{dn}!#{e & 0xffff}" : $"{dn}!{System.Text.Encoding.ASCII.GetString(b, Off(e & 0x7fffffff) + 2, 64).Split('\0')[0]}");
            }
        }
    }
}
