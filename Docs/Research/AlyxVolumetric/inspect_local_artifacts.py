"""Read-only supplementary inspection; this output is NOT REA Evidence.

No game code runs. Only the caller's output file is written. VPK payloads are
read in memory, CRC checked, and searched for identifiers; no shaders are copied.
"""
import argparse
import binascii
import hashlib
import json
import re
import struct
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def strings(data, pattern):
    return [{"file_offset": m.start(), "file_offset_hex": hex(m.start()),
             "value": m.group().decode("ascii")}
            for m in re.finditer(rb"[\x20-\x7e]{4,}", data)
            if pattern.search(m.group())]


def pe_imports(data):
    """Decode regular PE imports only (not delay-load or runtime LoadLibrary)."""
    if data[:2] != b"MZ":
        raise ValueError("not MZ")
    nt = struct.unpack_from("<I", data, 0x3c)[0]
    if data[nt:nt + 4] != b"PE\0\0":
        raise ValueError("not PE")
    sections, opt_size = struct.unpack_from("<H", data, nt + 6)[0], struct.unpack_from("<H", data, nt + 20)[0]
    opt = nt + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    directory = opt + (112 if magic == 0x20b else 96)
    step = 8 if magic == 0x20b else 4
    section_table = opt + opt_size
    ranges = []
    for i in range(sections):
        pos = section_table + i * 40
        vsize, rva, raw_size, raw = struct.unpack_from("<IIII", data, pos + 8)
        ranges.append((rva, raw_size, raw))

    def offset(rva):
        for start, size, raw in ranges:
            if start <= rva < start + size:
                return raw + rva - start
        raise ValueError(f"RVA {rva:x} not file-backed")

    def cstring(pos):
        return data[pos:data.index(b"\0", pos)].decode("ascii")

    rva, size = struct.unpack_from("<II", data, directory + 8)
    if not rva:
        return []
    pos = offset(rva)
    result = []
    for _ in range(min(size // 20 + 1, 4096)):
        original, stamp, chain, name, thunk = struct.unpack_from("<IIIII", data, pos)
        if not any((original, stamp, chain, name, thunk)):
            break
        functions = []
        entry_pos = offset(original or thunk)
        for _ in range(10000):
            entry = int.from_bytes(data[entry_pos:entry_pos + step], "little")
            if entry == 0:
                break
            functions.append({"ordinal": entry & 0xffff} if entry >> (step * 8 - 1)
                             else {"name": cstring(offset(entry) + 2)})
            entry_pos += step
        result.append({"module": cstring(offset(name)), "descriptor_file_offset": pos,
                       "functions": functions})
        pos += 20
    return result


def inspect_vpk(path):
    directory_bytes = path.read_bytes()
    magic, version, tree_size = struct.unpack_from("<III", directory_bytes)
    if magic != 0x55aa1234 or version not in (1, 2):
        raise ValueError("unsupported VPK header")
    header_size = 12 if version == 1 else 28
    tree_end = header_size + tree_size
    if tree_end > len(directory_bytes):
        raise ValueError("truncated VPK tree")
    cursor = header_size
    total = 0
    matches = []
    target = re.compile(r"volumetric|volume_fog|fog_lighting|fog_irradiance", re.I)
    identifiers = re.compile(rb"fog|volum|scatter|irradiance|extinct|anisotrop|history|reproj|jitter|noise|light|depth|texture|sampler|dispatch|thread|g_|\b[SD]_|integrat|accum|temporal|pass", re.I)

    def name():
        nonlocal cursor
        end = directory_bytes.index(b"\0", cursor, tree_end)
        result = directory_bytes[cursor:end].decode("utf-8")
        cursor = end + 1
        return result

    while True:
        ext = name()
        if not ext:
            break
        while True:
            folder = name()
            if not folder:
                break
            while True:
                stem = name()
                if not stem:
                    break
                entry_offset = cursor
                crc, preload_size, archive, payload_offset, payload_size, terminator = struct.unpack_from("<IHHIIH", directory_bytes, cursor)
                cursor += 18
                if terminator != 0xffff or cursor + preload_size > tree_end:
                    raise ValueError("invalid VPK entry")
                preload = directory_bytes[cursor:cursor + preload_size]
                cursor += preload_size
                total += 1
                logical = f"{folder + '/' if folder != ' ' else ''}{stem}{'.' + ext if ext != ' ' else ''}"
                if not target.search(logical):
                    continue
                record = {"logical_path": logical, "entry_file_offset": entry_offset,
                          "archive_index": archive, "archive_offset": payload_offset,
                          "payload_length": payload_size, "preload_length": preload_size,
                          "expected_crc32": f"{crc:08x}"}
                if payload_size + preload_size > 32 * 1024 * 1024:
                    record["payload_status"] = "not-read-size-bound"
                    matches.append(record)
                    continue
                source = path if archive == 0x7fff else path.with_name(path.name.removesuffix("_dir.vpk") + f"_{archive:03}.vpk")
                physical_offset = tree_end + payload_offset if archive == 0x7fff else payload_offset
                try:
                    with source.open("rb") as handle:
                        handle.seek(physical_offset)
                        data = preload + handle.read(payload_size)
                    if len(data) != payload_size + preload_size:
                        raise ValueError("truncated payload")
                    observed_crc = f"{binascii.crc32(data) & 0xffffffff:08x}"
                    record.update({"payload_source": str(source), "payload_source_offset": physical_offset,
                                   "sha256": sha(data), "observed_crc32": observed_crc,
                                   "crc_matches": observed_crc == record["expected_crc32"],
                                   "payload_status": "read", "identifiers": strings(data, identifiers)})
                except (OSError, ValueError) as error:
                    record["payload_status"] = "unavailable"
                    record["error"] = str(error)
                matches.append(record)
    return {"path": str(path), "sha256": sha(directory_bytes), "version": version,
            "tree_entries": total, "selected_entries": matches,
            "limitations": ["Directory and selected payloads only; VCS instructions are not decoded.",
                            "Identifiers do not establish dispatch, data flow, parameter values, or execution."]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = args.game_root.resolve()
    files = ["bin/win64/hlvr.exe", "bin/win64/engine2.dll", "bin/win64/scenesystem.dll",
             "bin/win64/worldrenderer.dll", "bin/win64/renderingpipelines.dll",
             "hlvr/bin/win64/client.dll", "hlvr/bin/win64/server.dll"]
    narrow = re.compile(rb"volumetric|volume.?fog|froxel|fog.?irradiance|fog.?history|fog.?voxel", re.I)
    records = []
    for relative in files:
        path = root / relative
        data = path.read_bytes()
        pattern = re.compile(rb"launcher|engine2|Source2|\.dll|volum|fog", re.I) if relative.endswith("hlvr.exe") else narrow
        records.append({"path": str(path), "sha256": sha(data), "byte_length": len(data),
                        "ascii_strings": strings(data, pattern),
                        "imports": pe_imports(data) if relative.endswith("hlvr.exe") else None})
    definitions = []
    for relative in ["core/base.fgd", "core/lights.fgd", "core/lights_base.fgd", "hlvr/hlvr.fgd"]:
        path = root / relative
        data = path.read_bytes()
        lines = data.decode("utf-8-sig").splitlines()
        chosen = [{"line": i + 1, "text": line} for i, line in enumerate(lines)
                  if re.search(r"volumetric|fog|irradiance|LPV", line, re.I)]
        definitions.append({"path": str(path), "sha256": sha(data), "selected_lines": chosen})
    vpks = [inspect_vpk(root / f"{folder}/{name}_dir.vpk")
            for folder, name in [("core", "shaders_pc"), ("hlvr", "shaders_pc")]]
    result = {"inspection_kind": "supplementary-local-static-inspection-not-REA-Evidence",
              "script_sha256": sha(Path(__file__).read_bytes()), "files": records,
              "definitions": definitions, "shader_archives": vpks,
              "limitations": ["No disassembly, decompilation, game execution or GPU capture.",
                              "ASCII matches are complete only for selected patterns in selected files.",
                              "Imports exclude delay-load and runtime module loading.",
                              "VPK CRC verifies member bytes against the directory, not publisher authenticity."]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "files": len(records),
                      "string_counts": {Path(f["path"]).name: len(f["ascii_strings"]) for f in records},
                      "shader_matches": {v["path"]: len(v["selected_entries"]) for v in vpks}}))


if __name__ == "__main__":
    main()
