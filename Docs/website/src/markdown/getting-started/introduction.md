# Welcome to Obsidian

Obsidian is an open source, MIT-licensed C# game engine. It combines a MonoGame runtime, the **Anvil** desktop editor, and **Vista**, an XML/CSS interface layer for game menus and HUDs.

These docs cover the engine’s current systems, their architecture, and how to use them. The engine guides come directly from the repository’s `Docs/markdown` folder.

## Start building

Follow [First Steps](#/docs/getting-started/first-steps) to build the engine and open Anvil. The runtime targets Windows and .NET 10, with DirectX rendering through MonoGame WindowsDX.

## Find your topic

- **Scene:** [Scenes & Game Flow](Scenes_and_Game_Flow.md) and [GameObject Components](Gameobject_Components.md).
- **Code:** [Script Behaviours](Script_Behaviours.md), including scripts on the main camera and built-in helpers.
- **Editor:** [Anvil Editor Architecture](Editor_Architecture.md), the engine bridge, snapshots, and threading.
- **Assets:** [Importing Assets](Importing%20Structure.md), the MonoGame content pipeline and runtime asset registry.
- **Rendering:** [Materials & Water](Material_Component.md) and [Scene Lighting & Fog](Scene_Lighting_and_Fog.md).
- **Gameplay & Input:** [Input Architecture](Input%20Architecture.md), keyboard and mouse input in standalone and hosted modes.
- **Animation:** [Skeletal Animation](Skeletal_Animation.md), the Animator component and skinned model pipeline.
- **UI:** [Vista UI](VistaUI_Architecture.md), layout, supported CSS, and scripting documents.
- **Sound:** [Audio Architecture](Audio_Architecture.md) and [Setting Up FMOD](setup_fmod.md).
- **Building & Exporting:** [Exporting Shaders to Unity URP](Exporting%20Shaders%20to%20Unity%20URP.md).

## Take part

Browse the [source on GitHub](https://github.com/pmdroide/obsidian), read the [contributing guide](https://github.com/pmdroide/obsidian/blob/HEAD/CONTRIBUTING.md), or learn how to [report an issue](#/docs/getting-started/reporting-issues).
