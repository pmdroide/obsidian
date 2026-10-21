using System.Runtime.InteropServices;

static class Dxbc
{
    [DllImport("d3dcompiler_47.dll")]
    static extern int D3DDisassemble(byte[] src, nuint size, uint flags, string? comments, out IntPtr blob);

    // ID3DBlob vtable: QueryInterface, AddRef, Release, GetBufferPointer, GetBufferSize
    delegate IntPtr GetPtr(IntPtr self);
    delegate nuint GetSize(IntPtr self);
    delegate uint Release(IntPtr self);

    public static string Disassemble(byte[] code)
    {
        int start = IndexOf(code, "DXBC"u8.ToArray());
        if (start < 0) return "<no DXBC header>";
        int size = BitConverter.ToInt32(code, start + 24);
        var dx = code.AsSpan(start, Math.Min(size, code.Length - start)).ToArray();
        int hr = D3DDisassemble(dx, (nuint)dx.Length, 0, null, out var blob);
        if (hr != 0) return $"<D3DDisassemble failed 0x{hr:X8}>";
        var vt = Marshal.ReadIntPtr(blob);
        var ptr = Marshal.GetDelegateForFunctionPointer<GetPtr>(Marshal.ReadIntPtr(vt, 3 * IntPtr.Size))(blob);
        var len = Marshal.GetDelegateForFunctionPointer<GetSize>(Marshal.ReadIntPtr(vt, 4 * IntPtr.Size))(blob);
        var s = Marshal.PtrToStringAnsi(ptr, (int)len);
        Marshal.GetDelegateForFunctionPointer<Release>(Marshal.ReadIntPtr(vt, 2 * IntPtr.Size))(blob);
        return s;
    }

    static int IndexOf(byte[] h, byte[] n)
    {
        for (int i = 0; i + n.Length <= h.Length; i++)
        {
            int j = 0;
            while (j < n.Length && h[i + j] == n[j]) j++;
            if (j == n.Length) return i;
        }
        return -1;
    }
}
