"""Compile the draft with Windows D3DCompiler and check transport against an
analytic uniform medium. No game execution, dependency installation or GPU use.
"""
import ctypes
import hashlib
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parent


def blob_bytes(blob):
    table = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(table[3])(blob)
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(table[4])(blob)
    return ctypes.string_at(pointer, size)


def release(blob):
    if blob:
        table = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
        ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(table[2])(blob)


def compile_entries():
    dll = ctypes.WinDLL("d3dcompiler_47.dll")
    compiler = dll.D3DCompile
    compiler.restype = ctypes.c_long
    compiler.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p,
                        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p,
                        ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                        ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
    # MonoGame uses legacy effect technique syntax. Compile the exact entry-point
    # source before those declarations; this does not check effect packaging.
    source = (ROOT / "VolumetricTransportDraft.fx").read_bytes().split(b"\ntechnique SeedTransport", 1)[0]
    # Force the otherwise unused reprojection helper through the compiler too.
    source += b"""
float4 ReprojectionCheckPS(VSOutput input) : SV_TARGET {
    float2 uv; float slice;
    bool valid = PreviousFroxelCoordinate(float3(input.Position.xy, -5),
        InverseProjection, InverseProjection, NearClip, FogFarClip, uv, slice);
    return float4(uv, slice, valid ? 1 : 0);
}
"""
    buffer = ctypes.create_string_buffer(source)
    results = []
    entries = [("FullscreenVS", "vs_4_0"), ("SeedPS", "ps_4_0"),
               ("ScanPS", "ps_4_0"), ("SampleDebugPS", "ps_4_0"),
               ("ReprojectionCheckPS", "ps_4_0")]
    for entry, target in entries:
        code, errors = ctypes.c_void_p(), ctypes.c_void_p()
        hr = compiler(buffer, len(source), b"VolumetricTransportDraft.fx", None, None,
                      entry.encode(), target.encode(), 1 << 11, 0,
                      ctypes.byref(code), ctypes.byref(errors))  # strictness
        try:
            diagnostics = blob_bytes(errors).decode("utf-8", "replace") if errors else ""
            if hr < 0:
                raise RuntimeError(f"{entry} compile failed: {diagnostics}")
            bytecode = blob_bytes(code)
            results.append({"entry": entry, "target": target, "bytes": len(bytecode),
                            "sha256": hashlib.sha256(bytecode).hexdigest(),
                            "diagnostics": diagnostics})
        finally:
            release(code)
            release(errors)
    return results


def segment(q, sigma, length):
    tau = sigma * length
    w = length * (1 - tau / 2 + tau * tau / 6) if tau < 0.001 else -math.expm1(-tau) / sigma
    return q * w, math.exp(-tau)


def combine(a, b):
    return a[0] + a[1] * b[0], a[1] * b[1]


def validate_transport():
    checks = 0
    max_error = 0
    for sigma in (0, 1e-12, 0.02, 0.8, 20):
        for ray_cosine in (1, 0.8, 0.2):
            boundaries = [400 ** (k / 128) for k in range(129)]
            segments = [segment(0.7, sigma, (b - a) / ray_cosine)
                        for a, b in zip(boundaries, boundaries[1:])]
            scanned = segments[:]
            stride = 1
            while stride < len(scanned):
                previous = scanned
                scanned = [combine(previous[i - stride], v) if i >= stride else v
                           for i, v in enumerate(previous)]
                stride *= 2
            sequential = (0, 1)
            for i, value in enumerate(segments):
                sequential = combine(sequential, value)
                analytic = segment(0.7, sigma, (boundaries[i + 1] - 1) / ray_cosine)
                for result in (sequential, scanned[i]):
                    error = max(abs(result[0] - analytic[0]), abs(result[1] - analytic[1]))
                    max_error = max(max_error, error)
                    assert error < 1e-7, (sigma, ray_cosine, i, error)
                    checks += 1
            for z in (1, 1.001, 1.05, 7, 30, 399, 400):
                s = min(int(math.floor(math.log(z) / math.log(400) * 128)), 127)
                before = scanned[s - 1] if s > 0 else (0, 1)
                result = combine(before, segment(0.7, sigma, (z - boundaries[s]) / ray_cosine))
                analytic = segment(0.7, sigma, (z - 1) / ray_cosine)
                error = max(abs(result[0] - analytic[0]), abs(result[1] - analytic[1]))
                max_error = max(max_error, error)
                assert error < 1e-7, ("partial", sigma, ray_cosine, z, error)
                checks += 1
    return {"comparisons": checks, "maximum_absolute_error": max_error,
            "limitations": "CPU double precision; does not measure GPU half-float error, visual quality or speed."}


if __name__ == "__main__":
    result = {"shader_sha256": hashlib.sha256((ROOT / "VolumetricTransportDraft.fx").read_bytes()).hexdigest(),
              "compiled_entries": compile_entries(), "transport": validate_transport(),
              "limitations": ["Individual shader entries, not MonoGame effect packaging.",
                              "No GPU render, runtime binding, integration or performance test."]}
    (ROOT / "draft-validation.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"compiled_entries": len(result["compiled_entries"]), "transport": result["transport"]}))
