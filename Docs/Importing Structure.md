This project does **not** have a broad automatic asset discovery system. It mainly uses the **MonoGame content pipeline**: assets are listed in `Content.mgcb`, compiled to `.xnb`, then loaded manually by path in C#.

**Main Flow**
1. Build-time import is declared in [Content.mgcb](C:/Dev/GitHub/obsidian/DeferredEngine/Content/Content.mgcb:16).
2. The project includes that content file through [DeferredEngine.csproj](C:/Dev/GitHub/obsidian/DeferredEngine/DeferredEngine.csproj:20).
3. Runtime content root is set to `Content` in [Engine.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Engine.cs:37).
4. `ScreenManager` wires everything up and calls `Assets.Load(...)` in [ScreenManager.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Logic/ScreenManager.cs:83).
5. Most game assets are loaded explicitly in [Assets.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Recources/Assets.cs:94).

**Important Files**
- [DeferredEngine/Content/Content.mgcb](C:/Dev/GitHub/obsidian/DeferredEngine/Content/Content.mgcb:16)  
  The main import manifest. This says which importer/processor to use:
  - `.fbx`, `.obj`: `FbxImporter` + `ModelProcessor`
  - `.x`: `XImporter` + `ModelProcessor`
  - `.png`, `.jpg`, `.dds`, `.tif`: `TextureImporter` + `TextureProcessor`
  - `.spritefont`: `FontDescriptionImporter`
  - `.fx`: `EffectImporter` + `EffectProcessor`

- [DeferredEngine/DeferredEngine.csproj](C:/Dev/GitHub/obsidian/DeferredEngine/DeferredEngine.csproj:20)  
  Registers `Content/Content.mgcb` with `MonoGame.Content.Builder.Task`, so assets are built during project build.

- [DeferredEngine/Recources/Assets.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Recources/Assets.cs:94)  
  Central hard-coded asset registry. Loads models, textures, fonts, sky maps, materials, Sponza textures, icons, etc. Example paths are extensionless:
  ```csharp
  content.Load<Model>("Art/Editor/Arrow");
  content.Load<Texture2D>("Art/Editor/icon_light");
  content.Load<SpriteFont>("Fonts/defaultFont");
  ```

- [DeferredEngine/Recources/ModelDefinition.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Recources/ModelDefinition.cs:22)  
  Wraps a loaded `Model`. It also loads or creates:
  - `.bbox` bounding box sidecar files
  - `.sdft` signed distance field sidecar files

- [DeferredEngine/Recources/MaterialEffect.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Recources/MaterialEffect.cs:11)  
  Defines material texture slots:
  - `AlbedoMap`
  - `NormalMap`
  - `RoughnessMap`
  - `MetallicMap`
  - `DisplacementMap`
  - `Mask`

- [DeferredEngine/Renderer/RenderModules/GBufferRenderModule.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Renderer/RenderModules/GBufferRenderModule.cs:143)  
  Uses `MaterialEffect` flags to choose shader techniques depending on which maps a material has.

- [HelperSuite/GUIHelper/GUIContentLoader.cs](C:/Dev/GitHub/obsidian/HelperSuite/GUIHelper/GUIContentLoader.cs:15)  
  A separate runtime loader for GUI-selected files. Currently it really supports runtime `Texture2D` import. It copies an image, runs `mgcb.exe`, loads the generated `.xnb`, then deletes temporary files.

- [HelperSuite/ContentLoader/ThreadSafeContentManager.cs](C:/Dev/GitHub/obsidian/HelperSuite/ContentLoader/ThreadSafeContentManager.cs:6)  
  Small locked wrapper around `ContentManager.Load<T>()`.

- [DeferredEngine/Recources/ShaderManager.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Recources/ShaderManager.cs:18)  
  Debug shader hot-reload path. Rebuilds changed `.fx` files using `mgcb.exe`.

- [DeferredEngine/Logic/VideoIntroLogic.cs](C:/Dev/GitHub/obsidian/DeferredEngine/Logic/VideoIntroLogic.cs:20)  
  Video is not loaded through `ContentManager`. It uses LibVLC directly from `Content/intro.mp4`.

**Content Structure**
```text
DeferredEngine/
  Content/
    Content.mgcb              main MonoGame import manifest
    Art/                      models, editor icons, sky, test materials
      Default/
      Editor/
      Human/
      Tiger/
      Test/
      Truck/
    Sponza/
      Sponza.obj
      textures/
      *.bbox / *.sdff
    Shaders/
      *.fx
      noise textures, LUTs
    Fonts/
      *.spritefont
    Graphical User Interface/
      GUI images
    Video/
      intro.mp4
```

**In short:** put assets under `DeferredEngine/Content`, add them to `Content.mgcb`, build so MonoGame produces `.xnb`, then load them with `content.Load<T>("path/without/extension")`. Models often go through `ModelDefinition`, textures become material maps through `MaterialEffect`, and rendering chooses shader paths based on which maps are present.