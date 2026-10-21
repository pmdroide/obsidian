using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System.Text.Json.Serialization;

namespace Engine.Components;

public enum MaterialShader { Standard, Water }

/// <summary>Per-object surface and texture overrides.</summary>
public sealed class MaterialComponent : GameComponent
{
    public const string TypeId = "material";
    public const float MaxWaveHeight = 10f;
    public const float MinPuddleSize = 0.1f, MaxPuddleSize = 50f;
    public const float MinTiling = 0.01f, MaxTiling = 1000f;
    // Keep the initial water-example API; new records persist the existing material type enum.
    [JsonIgnore]
    public MaterialShader Shader
    {
        get => MaterialType == MaterialEffect.MaterialTypes.Water ? MaterialShader.Water : MaterialShader.Standard;
        set => MaterialType = value == MaterialShader.Water ? MaterialEffect.MaterialTypes.Water : MaterialEffect.MaterialTypes.Basic;
    }
    [JsonPropertyName("Shader"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaterialShader? LegacyShader
    {
        get => null;
        set { if (value.HasValue) Shader = value.Value; }
    }
    public MaterialEffect.MaterialTypes MaterialType { get; set; } = MaterialEffect.MaterialTypes.Basic;
    public bool IsTransparent { get; set; }
    public float Red { get; set; } = 1f;
    public float Green { get; set; } = 1f;
    public float Blue { get; set; } = 1f;
    public float Roughness { get; set; } = 0.25f;
    public float Metallic { get; set; }
    public float EmissiveStrength { get; set; }
    public bool CastShadows { get; set; } = true;
    public float Opacity { get; set; } = 0.65f;
    public float WaveScale { get; set; } = 0.3f;
    public float WaveSpeed { get; set; } = 1f;
    public float WaveStrength { get; set; } = 0.2f;
    /// <summary>Water only: crest-to-trough height in metres of the swell that moves the surface mesh.</summary>
    public float WaveHeight { get; set; } = 0.5f;
    /// <summary>Water only: metres you can see into the water before it takes the surface colour.</summary>
    public float Clarity { get; set; } = 4f;
    /// <summary>Water only: shore and wave-crest foam amount, 0..1.</summary>
    public float Foam { get; set; } = 0.5f;
    /// <summary>Wet Surface only: how wet it is in dry weather, 0..1 (1 also fills its puddles).</summary>
    public float Wetness { get; set; }
    /// <summary>Wet Surface only: share of the upward faces puddles cover when soaked, 0..1.</summary>
    public float PuddleAmount { get; set; } = 0.5f;
    /// <summary>Wet Surface only: typical puddle size in metres (world space, independent of tiling).</summary>
    public float PuddleSize { get; set; } = 3f;
    /// <summary>Wet Surface only: rain soaks the surface beyond <see cref="Wetness"/> and ripples its puddles.</summary>
    public bool WeatherWetness { get; set; } = true;
    /// <summary>Rain beads on the surface and runs down it (all shaders but Wet Surface, which pools puddles instead).</summary>
    public bool RainDrops { get; set; } = true;
    /// <summary>Texture repeats across the mesh UVs, horizontally and vertically.</summary>
    public float TilingU { get; set; } = 1f;
    public float TilingV { get; set; } = 1f;

    // null inherits the model's map; empty explicitly removes it.
    public string BaseColorTexture { get; set; }
    public string NormalTexture { get; set; }
    public string RoughnessTexture { get; set; }
    public string MetallicTexture { get; set; }
    public string MaskTexture { get; set; }
    public string DisplacementTexture { get; set; }
    /// <summary>Ambient occlusion map (red channel): darkens only the indirect light, like Source 2's baked AO (phase 6.2).</summary>
    public string AmbientOcclusionTexture { get; set; }

    /// <summary>
    /// Content-relative material asset (<see cref="MaterialAsset"/>, "Materials/Brick.mat") this component shows, or
    /// null for its own settings. While linked, the settings above are a copy of the asset's (kept in sync with the
    /// file, saved in the scene as a fallback); <see cref="MaterialAsset.SaveFrom"/> writes edits back to the asset.
    /// </summary>
    public string MaterialPath { get; set; }

    // The asset version (and path) the settings were last copied from; runtime only.
    [JsonIgnore] internal MaterialAsset SyncedAsset { get; private set; }
    [JsonIgnore] internal string SyncedPath { get; set; }

    internal bool IsSyncedWith(MaterialAsset asset) =>
        ReferenceEquals(SyncedAsset, asset) && MaterialAsset.SamePath(SyncedPath, asset.Path);

    /// <summary>
    /// Links this component to a material asset ("Brick" or "Materials/Brick.mat") and takes its surface. Call
    /// <see cref="GameComponent.OnChanged"/> (or use <see cref="MaterialAsset.Assign"/>) to redraw. False (logged)
    /// when there is no such material.
    /// </summary>
    public bool Link(string nameOrPath)
    {
        var asset = MaterialAsset.Load(nameOrPath);
        if (asset == null)
        {
            global::Engine.Editor.EditorBridge.Log($"Material: no material asset '{nameOrPath}'.");
            return false;
        }
        MaterialPath = asset.Path;
        Sync(asset);
        return true;
    }

    internal void Sync(MaterialAsset asset, bool copy = true)
    {
        if (copy) asset.CopyTo(this);
        SyncedAsset = asset;
        SyncedPath = asset.Path;
    }

    public static bool IsTextureAsset(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant()
            is ".png" or ".jpg" or ".jpeg" or ".tga" or ".dds" or ".bmp";

    public override void OnAdded(BasicEntity owner)
    {
        // Added already linked (MaterialAsset.Assign, scripts, scene loads): show the asset's current surface.
        if (MaterialPath != null && MaterialAsset.Load(MaterialPath) is { } asset)
        {
            MaterialPath = asset.Path;
            Sync(asset);
        }
        base.OnAdded(owner);
    }

    public override void OnChanged(BasicEntity owner) => owner.RefreshMaterials();
    public override void OnRemoved(BasicEntity owner) => owner.RefreshMaterials();

    /// <summary>A new Material component starts from the surface the gameobject already shows.</summary>
    public static MaterialComponent FromOwner(BasicEntity owner) => FromMaterial(owner?.Material ??
        owner?.Model?.Meshes.SelectMany(m => m.MeshParts).Select(p => p.Effect).OfType<MaterialEffect>().FirstOrDefault());

    public static MaterialComponent FromMaterial(MaterialEffect material) => material == null ? new() : new()
    {
        Red = material.DiffuseColor.X,
        Green = material.DiffuseColor.Y,
        Blue = material.DiffuseColor.Z,
        Roughness = material.Roughness,
        Metallic = material.Metallic,
        EmissiveStrength = material.EmissiveStrength,
        MaterialType = material.Type,
        IsTransparent = material.IsTransparent,
        CastShadows = material.HasShadow,
        Opacity = material.Opacity,
        WaveScale = material.WaveScale,
        WaveSpeed = material.WaveSpeed,
        WaveStrength = material.WaveStrength,
        WaveHeight = material.WaveHeight,
        Clarity = material.WaterClarity,
        Foam = material.WaterFoam,
        Wetness = material.Wetness,
        PuddleAmount = material.PuddleAmount,
        PuddleSize = material.PuddleSize,
        WeatherWetness = material.WeatherWetness,
        RainDrops = material.RainDrops,
        TilingU = material.TextureTiling.X,
        TilingV = material.TextureTiling.Y,
    };

    public void ApplyTo(MaterialEffect material, ContentManager content = null)
    {
        material.IsInstanceMaterial = true;
        material.DiffuseColor = new Vector3(Limit(Red, 0, 1), Limit(Green, 0, 1), Limit(Blue, 0, 1));
        material.Roughness = Limit(Roughness, 0.001f, 1);
        material.Metallic = Limit(Metallic, 0, 1);
        material.EmissiveStrength = Limit(EmissiveStrength, 0, 8);
        material.Type = Enum.IsDefined(MaterialType) ? MaterialType : MaterialEffect.MaterialTypes.Basic;
        material.IsTransparent = IsTransparent || material.Type == MaterialEffect.MaterialTypes.Water;
        // Water is blended and does not have a matching alpha shadow pass.
        material.HasShadow = CastShadows && material.Type != MaterialEffect.MaterialTypes.Water;
        material.Opacity = Limit(Opacity, 0, 1);
        material.WaveScale = Limit(WaveScale, 0.001f, 10);
        material.WaveSpeed = Limit(WaveSpeed, 0, 10);
        material.WaveStrength = Limit(WaveStrength, 0, 2);
        material.WaveHeight = Limit(WaveHeight, 0, MaxWaveHeight);
        material.WaterClarity = Limit(Clarity, 0.05f, 50);
        material.WaterFoam = Limit(Foam, 0, 1);
        material.Wetness = Limit(Wetness, 0, 1);
        material.PuddleAmount = Limit(PuddleAmount, 0, 1);
        material.PuddleSize = Limit(PuddleSize, MinPuddleSize, MaxPuddleSize);
        material.WeatherWetness = WeatherWetness;
        material.RainDrops = RainDrops;
        material.TextureTiling = new Vector2(Limit(TilingU, MinTiling, MaxTiling), Limit(TilingV, MinTiling, MaxTiling));
        content ??= Globals.content;
        ApplyTexture(BaseColorTexture, content, texture => material.AlbedoMap = texture);
        ApplyTexture(NormalTexture, content, texture => material.NormalMap = texture);
        material.UseComponentRoughnessMap = ApplyTexture(RoughnessTexture, content, texture => material.RoughnessMap = texture) && material.HasRoughnessMap;
        material.UseComponentMetallicMap = ApplyTexture(MetallicTexture, content, texture => material.MetallicMap = texture) && material.HasMetallic;
        ApplyTexture(MaskTexture, content, texture => material.Mask = texture);
        ApplyTexture(DisplacementTexture, content, texture => material.DisplacementMap = texture);
        ApplyTexture(AmbientOcclusionTexture, content, texture => material.AmbientOcclusionMap = texture);
    }

    private static bool ApplyTexture(string path, ContentManager content, Action<Texture2D> assign)
    {
        if (path == null) return false;
        if (path.Length == 0) { assign(null); return false; }
        if (content == null || !IsTextureAsset(path)) return false;
        string asset = Path.ChangeExtension(path.Replace('\\', '/'), null);
        try
        {
            // The content manager owns/caches maps; material instances never dispose them.
            assign(content.Load<Texture2D>(asset));
            return true;
        }
        catch (ContentLoadException ex)
        {
            // A texture only a model references is built next to it as "<name>_0" (material assets captured from models).
            try
            {
                assign(content.Load<Texture2D>(asset + "_0"));
                return true;
            }
            catch (ContentLoadException) { }
            global::Engine.Editor.EditorBridge.Log($"Material texture '{path}' could not load: {ex.Message}");
            return false;
        }
    }

    internal static float Limit(float value, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
