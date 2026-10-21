# Breakable glass: how Half-Life: Alyx does it

Research behind the Breakable Glass component (`Docs/markdown/Breakable_Glass.md`). Target: Half-Life: Alyx build of
2023-04-09 (`game/hlvr/bin/win64/server.dll` 22 730 680 bytes, `client.dll` 16 247 248 bytes).

`game/bin/win64/hlvr.exe` (149 KB) is only the launcher. The glass code is shared game code
(`src/game/shared/func_shatterglass.cpp`, `te/te_glassshatter.cpp`) compiled into `server.dll` and `client.dll`.

## How it was found

The REA MCP server timed out on connect during this session, and Ghidra wasn't configured for it
(`GHIDRA_INSTALL_DIR` unset). So the evidence comes from the project's own disassembler
(`Docs/Research/Source2Look/re_tool`, Iced + System.Reflection.Metadata):

```
grep -aoE "[ -~]{5,}" server.dll | grep -iE "shatter|glass|shard"     # class and field names, convars
dotnet run -c Release -- cvars <server.dll> "shatter|glass|shard"       # convar defaults
dotnet run -c Release -- xref  <server.dll> 0x181495310,... 20 40      # code reading each convar's value (+0x38)
dotnet run -c Release -- xref  <server.dll> @,1808E28E3,1808E2E90 0 0  # disassembly of a range
dotnet run -c Release -- imp   <server.dll> 180CC1960                   # import thunk -> vstdlib RandomFloat
```

## Observed

**Classes** (RTTI): `CFuncShatterglass` (entity `func_shatterglass`), `CShatterGlassShard`, `CShatterGlassShardMgr`,
`CShatterGlassShardPhysics` (falling shard; `C_` on the client), `CShatterGlassGameSystem`,
`CShatterGlassBreakableSystem`, `CTraceFilterBreakableGlass`, `CTEShatterSurface` / `CMsgTEShatterSurface`
(network event), `shard_model_desc_t` (networked `m_vecPanelVertices` of `Vector2D`, so shards are 2D polygons on the
pane), `CBuildShardModelJob` / `CShatterJob` (shard meshes are built on job threads as `shatter_mesh_%i.vmesh`,
`shatter_phys_%i.vphys`, `shatter_model_%s_%i.vmdl`).

**Fields and enums**: `m_flGlassThickness`/`m_flGlassHalfThickness`, `m_bGlassInFrame`, `m_bGlassNavIgnore`,
`m_hGlassMaterialDamaged`/`m_hGlassMaterialUndamaged` (`materials/glass/shatterglass*.vmat`), `m_ShatterPanelMode`,
`m_ShatterStressType`, `m_nSubShardGeneration`, `m_hParentShard`, `m_nParentPanelsNthShard`, `m_bShatterRateLimited`;
`ShatterGlassStressType` = `SHATTERGLASS_BLUNT`, `SHATTERGLASS_BALLISTIC`, `SHATTERGLASS_PULSE`,
`SHATTERGLASS_EXPLOSIVE`; `ShatterDamageCause` = bullet/melee/thrown/explosive/script; `ShardSolid_t` =
`SHARD_SOLID`/`SHARD_DEBRIS`; sounds `Glass.Window`, `Glass.Small`, `Phys.GlassBreakSm/Md/Lg`.

**Convars** (identical in server and client, flags 0x6000):

| Convar | Default | Meaning (inferred from use) |
|---|---|---|
| `shatterglass_voronoi_size` | 15 | grid cell of the first crack (inches = 0.381 m) |
| `shatterglass_subdiv_size` | 1.5 | longest edge segment when shard edges are subdivided |
| `shatterglass_hit_tolerance` | 2.0 | polygon scale for the shard hit test |
| `shatterglass_edge_uv_scale` | 0.05 | texture scale of the broken edges |
| `shatterglass_distort` | 1 | toggle |
| `shatterglass_shard_lifetime` | 15 | seconds a falling shard lives |
| `shatterglass_cleanup` / `_max` | 1 / 150 | remove the oldest shards above 150 |
| `cl_async_client_shatter` | 1 | build client shards on jobs |
| `fx_glass_velocity_cap` | 0 | max downward speed of glass particles |

**First crack** (`server.dll` 0x1808E27B0). It reads `shatterglass_voronoi_size` (V) at 0x1808E27EF and skips the
pattern when V ≥ the panel's width `[this+0x738]` or height `[this+0x73C]`. It seeds a `CUniformRandomStream`
(`SetSeed`), then loops x from -W/2 to W/2 and y from -H/2 to H/2 in steps of V. Each site is
`(x, y) + 0.25·V + RandomFloat(-0.49·V, 0.49·V)` per axis, and it is kept only when
`-0.49·W < x < 0.49·W` and the same for y. Then it starts each cell as the panel rectangle
(`±W/2, ±H/2` at 0x1808E2AE2) and clips it, which makes Voronoi cells by half-plane clipping.

**Hit test** (0x1808DACE0). It rejects polygons with fewer than 3 or more than 128 points, reads
`shatterglass_hit_tolerance` and scales the polygon about its centre (0x1808DCCB0) before the point-in-polygon test.

**Edge subdivision** (around 0x1808E660C). Edges longer than `shatterglass_subdiv_size` are cut into
`floor(len / size)` equal segments.

## Inferred / not recovered

- What exactly happens per stress type (the shape of the hole for `BALLISTIC` against `BLUNT`) wasn't traced. The
  engine's radial impact pattern (rings on jittered spokes) is a design choice that looks like real glass. It is not
  Valve's code.
- What `shatterglass_distort` does wasn't traced. The engine uses it for a small per-panel tilt, so reflections break
  along the cracks.
- `GlassInFrame` suggests that panels stay in the frame while connected to it. The engine drops panels that aren't
  joined to the border through shared edges.
- Edge UV scale and subdivision aren't used: the engine's shard edges are flat quads with the face's texture
  coordinates.
