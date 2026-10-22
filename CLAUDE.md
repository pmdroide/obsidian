# CLAUDE.md

## Spawning agents and task handling

Claude:
For searching the codebase, researching or other simple tasks use sonnet 5.5. For decision making and architect use Opus 5.5.

Codex:
For searching the codebase, researching or other simple tasks use GPT-5.6 Sol. For complex tasks use GPT-6.1 Sol. For blender use GPT 6 Astra.

## Doing tests

Only make tests if you think it might break the engine/editor, if its to check how the game looks or if you need to look to see how it behaves, go ahead.

## Project Snapshot

Obsidian is a C#/.NET 10 game engine/editor workspace. The runtime is a MonoGame WindowsDX engine, the editor is an Avalonia desktop app named Anvil, and `Vista` is a custom XML/CSS UI layer rendered inside MonoGame.

- `Engine/Engine.csproj` - MonoGame WindowsDX executable and core engine (`net10.0-windows`, MonoGame 3.8.4.1).
- `Editor/Anvil/Anvil.csproj` - Avalonia 12.0.3 editor shell that embeds the engine viewport.
- `Vista/Vista.csproj` - lightweight XML/CSS UI renderer built on AngleSharp + MonoGame.
- `ContentPipeline/ContentPipeline.csproj` - MonoGame pipeline extension (`SkinnedModelProcessor`), net8.0 because mgcb runs on .NET 8.

## Quick Commands

PowerShell from the repo root:

```powershell
dotnet build Engine.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
dotnet run --project Tests\Components\Components.csproj   # checks; --graphics for GPU, --build to publish the game
dotnet run --project Engine\Engine.csproj
dotnet run --project Editor\Anvil\Anvil.csproj
```

Setup/build scripts live in `Tools/Scripts/` (`bootstrap.bat` restores and builds).

## Core Files

- `Engine/Engine.cs` - MonoGame `Game`; creates graphics, physics, `ScreenManager`, `EditorBridge`.
- `Engine/Logic/ScreenManager.cs` - load/init/update/draw coordinator.
- `Engine/Logic/MainSceneLogic.cs` - live scene lists (entities, lights, decals, camera) and editor add/delete helpers.
- `Engine/Logic/EditorLogic.cs` - in-engine editor mode, selection, gizmos.
- `Engine/Renderer/Renderer.cs` + `RenderModules/` - render pipeline and render targets.
- `Engine/Entities/`, `Engine/Components/` - scene object types and gameobject components (`ComponentRegistry`).
- `Engine/Recources/` - assets (`Assets.cs`), shaders (`Shaders.cs`), settings (`GameSettings.cs`), materials. Keep the `Recources` spelling.
- `Engine/Content/Content.mgcb` - content manifest; paths are extensionless and relative to `Content`.
- `Engine/Editor/IEditorBridge.cs` + `EditorBridge.cs` - bridge contract and thread-safe operation queue/snapshots.
- `Editor/Anvil/Controls/MonoGameHost.cs` - embeds the engine HWND and drives `RunOneFrame`.
- `Editor/Anvil/ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml`, `Models/SceneObjectViewModel.cs`, `Services/BridgeReconciler.cs` - editor state, layout, inspector models, snapshot reconciliation.
- `Vista/UI/UIManager.cs` + `UIElement.cs` - Vista layout and drawing.

## Where to look next

Read these only when the task needs them:

- `Docs/Agent/Repo_Map.md` - every feature (animation, ragdolls, breakables, glass, liquid, cloth, NPCs, LOD, foliage, voxels, particles, physics, fog, weather, water, materials, prefabs, saves, render features, Source 2 look, lightmaps, Steam, TexGen...) with its files, sample scene and doc.
- `Docs/Agent/Recipes.md` - step-by-step "how do I add/change X" recipes.
- `Docs/Agent/Architecture.md` - runtime flow (standalone + Anvil), renderer phases, asset pipeline.
- `Docs/Agent/Adding_Components.md` - adding a gameobject component end to end.
- `Docs/Agent/Blender.md` - Blender MCP rules and game-asset checklist.
- `Docs/markdown/` - user docs per feature; `Docs/markdown/Engine_Wiring.md` - how systems connect.
- Reverse engineering: Ghidra is at `C:\Dev\Tools\ghidra_12.1.4_PUBLIC`.

## Project-Specific Rules

- Keep scene mutations on the engine/game thread. From Avalonia, enqueue through `IEditorBridge`; do not mutate engine lists directly.
- Preserve existing naming and spelling, including `Engine.Recources`. Saved enum/type names are never renamed.
- MonoGame content paths are extensionless and relative to the `Content` root.
- Anvil sets the current directory to the Engine assembly directory before booting MonoGame so content paths resolve correctly.
- Coordinate comments indicate Z is up; gravity is set to negative Z.
- Many shader parameters are cached statically in `Shaders.cs`; ensure `Globals.content` is set before shader static access.
- Render targets are manually owned and disposed. When resolution changes, follow `Renderer.UpdateResolution()` / `SetUpRenderTargets(...)` patterns.
- Existing code style is older C# in `Engine` and `Vista` with nullable mostly disabled; `Anvil` uses nullable + MVVM source generators.

## In the end of the session
Write what was changed, added or/and removed in the `CHANGELOG.md`.
Also sign who did it in the changelog, for example: "- Claude", "- Codex" at the end of the text.
Make sure to always update the [Engine_Wiring.md](Docs/markdown/Engine_Wiring.md), the version in the [README.md](README.md) make sure it follows the format MAJOR.MINOR.PATCH and update the [ISSUES.md](ISSUES.md) if you find any issues.
