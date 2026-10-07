# First Steps

Build the engine and open Anvil to start exploring the sample scenes.

## Prerequisites

- Windows with DirectX 11 support.
- The .NET 10 SDK.
- Git and a C# development environment, such as Visual Studio Code or Visual Studio.
- The FMOD native libraries for audio. Follow [Setting Up FMOD](setup_fmod.md) for the required version and file locations. If FMOD cannot initialize, the engine runs without audio.

## Get the source

```powershell
git clone https://github.com/pmdroide/obsidian.git
cd obsidian
```

## Build the workspace

Run these commands in PowerShell from the repository root:

```powershell
dotnet restore
dotnet build Engine.slnx
```

The build compiles the runtime and editor, builds the custom content pipeline extension, and processes the MonoGame content.

## Open Anvil

```powershell
dotnet run --project Editor/Anvil/Anvil.csproj
```

Anvil embeds the engine viewport alongside the hierarchy and inspector. Open a sample scene from `Engine/Content/Scenes`, select a GameObject, and explore its components. Press **Play** to run scene logic and **Stop** to return to editing.

Read [GameObject Components](Gameobject_Components.md) for the inspector workflow and [Anvil Editor Architecture](Editor_Architecture.md) for how changes reach the engine.

## Run the standalone engine

```powershell
dotnet run --project Engine/Engine.csproj
```

The standalone engine starts the scene at index 0 in `Engine/Content/System/SceneList.json`. See [Scenes & Game Flow](Scenes_and_Game_Flow.md) to configure the scene order and switch scenes from scripts.

## Choose a next step

- Write your first [Script Behaviour](Script_Behaviours.md).
- Add assets with the [Importing Assets](Importing%20Structure.md) guide.
- Try the animation sample described in [Skeletal Animation](Skeletal_Animation.md).
- Build a game menu with [Vista UI](VistaUI_Architecture.md).
