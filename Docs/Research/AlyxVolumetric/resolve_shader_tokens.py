"""Supplement native REA dossiers with independently reproducible token matches.

MurmurHash2 candidate uses lowercased ASCII and seed 0x31415926. This does not
prove REA's untyped binding/dispatch callees or decode the GPU instructions.
"""
import json
import re
from pathlib import Path


def token(text):
    data = text.lower().encode('ascii')
    m = 0x5bd1e995
    h = 0x31415926 ^ len(data)
    cursor = 0
    while cursor + 4 <= len(data):
        k = int.from_bytes(data[cursor:cursor + 4], 'little')
        k = k * m & 0xffffffff
        k ^= k >> 24
        k = k * m & 0xffffffff
        h = ((h * m & 0xffffffff) ^ k)
        cursor += 4
    tail = data[cursor:]
    if tail:
        h ^= int.from_bytes(tail, 'little')
        h = h * m & 0xffffffff
    h ^= h >> 13
    h = h * m & 0xffffffff
    return (h ^ (h >> 15)) & 0xffffffff


if __name__ == '__main__':
    root = Path(__file__).resolve().parent
    obs = json.loads((root / 'local-observations.json').read_text(encoding='utf-8-sig'))
    entries = [e for v in obs['shader_archives'] for e in v['selected_entries']
               if 'cs_volumetric_fog_pc_50_' in e['logical_path']]
    names = {s['value'] for e in entries for s in e.get('identifiers', [])
             if re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', s['value'])}
    records = []
    for filename in ('native-clipmap-render.json', 'native-fog-render.json'):
        ev = json.loads((root / filename).read_text(encoding='utf-8-sig'))
        pseudo = ev['normalized_result']['pseudocode']
        keys = {int(s, 16) for s in re.findall(r'\b0x[0-9a-f]{8}\b', pseudo)}
        matches = []
        for name in sorted(names):
            value = token(name)
            if value in keys:
                locations = [{'member': e['logical_path'], 'offsets':
                              [s['file_offset_hex'] for s in e['identifiers'] if s['value'] == name]}
                             for e in entries if any(s['value'] == name for s in e['identifiers'])]
                matches.append({'name': name, 'token': f'0x{value:08x}', 'shader_locations': locations})
        records.append({'dossier': filename, 'evidence_id': ev['evidence_id'], 'matches': matches})
    output = {'inspection_kind': 'supplementary-token-correlation-not-REA-Evidence',
              'algorithm': {'name': 'MurmurHash2', 'seed': '0x31415926',
                            'input': 'lowercased ASCII', 'integer_width': 32},
              'records': records,
              'limitations': ['Matching identifier hashes support binding-key identity; collisions are possible.',
                              'Does not recover typed callee signatures, GPU pass bodies or runtime execution.']}
    (root / 'shader-token-matches.json').write_text(json.dumps(output, indent=2) + '\n', encoding='utf-8')
    for record in records:
        print(record['dossier'], [(m['name'], m['token']) for m in record['matches']])
