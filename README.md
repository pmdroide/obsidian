<div align="center">
  <img src="Docs/Icon.png" width="80px" alt="Project Icon">

  [![Stars](https://img.shields.io/github/stars/pmdroide/obsidian?style=flat-square)](https://github.com/pmdroide/obsidian/stargazers)
  [![Forks](https://img.shields.io/github/forks/pmdroide/obsidian?style=flat-square)](https://github.com/pmdroide/obsidian/network/members)
  [![Downloads](https://img.shields.io/github/downloads/pmdroide/obsidian/total?style=flat-square)](https://github.com/pmdroide/obsidian/releases)
</div>

# Obsidian

Obsidian is a game engine royalty free with objective to make game development easy and simple to understand.
The engine uses Monogame as the framework, and the editor uses Avalonia.

## Getting started

### Prerequisites

- [Git](https://git-scm.com/)
- [Visual Studio Code](https://code.visualstudio.com/download)
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- DirectX 11 or newer (Download dxsetup from the official microsoft website to check the version)

### Building

```
# Clone repo
git clone https://github.com/pmdroide/obsidian.git

```

Once you've cloned the repo, run the bootstrap script from the repository root to restore dependencies and build the engine:

```powershell
cd obsidian
.\Tools\Scripts\bootstrap.bat
```

## Versions

### Engine and Editor
Engine and Editor use the following version format: `MAJOR.MINOR.PATCH`

- Obsidian 1.5.0
- Editor 1.5.0

### Tools and frameworks

- Monogame 3.8.4.1
- Directx 11
- Dotnet 10

## Repository structure

```text
obsidian/
|-- .claude/             Claude agent instructions and development skills
|-- .github/             GitHub Actions workflows
|-- .vscode/             VS Code build tasks and launch configurations
|-- Art/                 Local source artwork (git-ignored)
|-- Builds/              Published editors, games and tools (git-ignored)
|-- ContentPipeline/     MonoGame content processors for skinned models and animation
|-- Docs/                Documentation, diagrams, research and website
|   |-- dgml/            Dependency and rendering pipeline diagrams
|   |-- excalidraw/      Editable architecture diagrams
|   |-- markdown/        Engine, editor and feature guides
|   |-- Research/        Rendering research, evidence and helper tools
|   `-- website/         Documentation website source
|-- Editor/
|   `-- Anvil/           Avalonia editor, viewport host and MCP server
|-- Engine/              MonoGame runtime, renderer, physics and gameplay systems
|   |-- Content/         Game assets, scenes, shaders, UI and sample gameplay scripts
|   `-- thirdparty/      Third-party runtime integrations
|-- state/               Local workspace state (currently empty; untracked)
|-- Tests/
|   |-- Components/      Engine and editor integration checks
|   `-- McpInstaller/    Standalone MCP installer checks
|-- Tools/
|   |-- McpInstaller/    CLI for configuring Anvil MCP in supported clients
|   `-- Scripts/         Repository setup, build, cleanup and installer scripts
|-- Vista/               XML/CSS UI library rendered through MonoGame
|-- Engine.slnx          .NET solution
`-- *.md                Project overview, documentation and contribution guides
```

Build-generated `bin/` and `obj/` folders hold compiled output and intermediate files and are git-ignored. `Art/`, `Builds/` and `state/` are local folders and may be absent in a fresh clone.

Run these scripts from the repository root:

| Script | Purpose |
| --- | --- |
| [bootstrap.bat](Tools/Scripts/bootstrap.bat) | Restore dependencies and build the solution. |
| [build-editor.ps1](Tools/Scripts/build-editor.ps1) | Publish the Anvil editor to `Builds/Anvil`; supports `-Clean`, `-Run` and `-Output`. |
| [build-mcp-installer.ps1](Tools/Scripts/build-mcp-installer.ps1) | Publish the standalone MCP installer to `Builds/McpInstaller`. |
| [clean.ps1](Tools/Scripts/clean.ps1) | Remove project and content build output; use `-WhatIf` to preview. |
| [install-mcp.ps1](Tools/Scripts/install-mcp.ps1) | Configure Anvil MCP for Claude Code, Cursor, Codex or VS Code. |

## Troubleshooting

Sometimes the game might fail due to old packages or assets that were in old build of the game, to fix this use the [clean.ps1](Tools/Scripts/clean.ps1) file to cleanup the old build.

```
.\Tools\Scripts\clean.ps1          # delete
.\Tools\Scripts\clean.ps1 -WhatIf  # just list what would go
```

## Contributing 
If you want to contribute to the engine, please check the [contributing guide](CONTRIBUTING.md).

## Star History

<a href="https://www.star-history.com/?repos=pmdroide%2Fobsidian&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=pmdroide/obsidian&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=pmdroide/obsidian&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=pmdroide/obsidian&type=date&legend=top-left" />
 </picture>
</a>

## License
The obsidian engine source code is licensed under the [MIT License](LICENSE)

The FMOD Engine is a proprietary sound engine owned by Firelight Technologies Pty Ltd. It is **not** covered by this repository's MIT license. Use of FMOD is governed strictly by the **FMOD End User License Agreement (EULA)**.

To legally comply with FMOD's terms and maintain an open-source workflow, **this repository does not include FMOD’s proprietary SDK headers or pre-compiled binaries (`.dll`, `.so`, `.dylib`, etc.)**. You must download them directly from FMOD.
