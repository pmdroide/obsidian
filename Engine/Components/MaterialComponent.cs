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

    // null inherits the model's map; empty explicitly removes it.
    public string BaseColorTexture { get; set; }
    public string NormalTexture { get; set; }
    public string RoughnessTexture { get; set; }
    public string MetallicTexture { get; set; }
    public string MaskTexture { get; set; }
    public string DisplacementTexture { get; set; }

    public static bool IsTextureAsset(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant()
            is ".png" or ".jpg" or ".jpeg" or ".tga" or ".dds" or ".bmp";

    public override void OnChanged(BasicEntity owner) => owner.RefreshMaterials();

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
        content ??= Globals.content;
        ApplyTexture(BaseColorTexture, content, texture => material.AlbedoMap = texture);
        ApplyTexture(NormalTexture, content, texture => material.NormalMap = texture);
        material.UseComponentRoughnessMap = ApplyTexture(RoughnessTexture, content, texture => material.RoughnessMap = texture) && material.HasRoughnessMap;
        material.UseComponentMetallicMap = ApplyTexture(MetallicTexture, content, texture => material.MetallicMap = texture) && material.HasMetallic;
        ApplyTexture(MaskTexture, content, texture => material.Mask = texture);
        ApplyTexture(DisplacementTexture, content, texture => material.DisplacementMap = texture);
    }

    private static bool ApplyTexture(string path, ContentManager content, Action<Texture2D> assign)
    {
        if (path == null) return false;
        if (path.Length == 0) { assign(null); return false; }
        if (content == null || !IsTextureAsset(path)) return false;
        try
        {
            // The content manager owns/caches maps; material instances never dispose them.
            assign(content.Load<Texture2D>(Path.ChangeExtension(path.Replace('\\', '/'), null)));
            return true;
        }
        catch (ContentLoadException ex)
        {
            global::Engine.Editor.EditorBridge.Log($"Material texture '{path}' could not load: {ex.Message}");
            return false;
        }
    }

    private static float Limit(float value, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
