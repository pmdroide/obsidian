using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;

namespace Engine.Components;

public enum MaterialShader { Standard, Water }

/// <summary>Per-object surface overrides. Imported texture maps stay on the source material.</summary>
public sealed class MaterialComponent : GameComponent
{
    public const string TypeId = "material";
    public MaterialShader Shader { get; set; }
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

    public override void OnChanged(BasicEntity owner) => owner.RefreshMaterials();

    public void ApplyTo(MaterialEffect material)
    {
        material.IsInstanceMaterial = true;
        material.DiffuseColor = new Vector3(Limit(Red, 0, 1), Limit(Green, 0, 1), Limit(Blue, 0, 1));
        material.Roughness = Limit(Roughness, 0.001f, 1);
        material.Metallic = Limit(Metallic, 0, 1);
        material.EmissiveStrength = Limit(EmissiveStrength, 0, 8);
        material.Type = Shader == MaterialShader.Water ? MaterialEffect.MaterialTypes.Water :
            material.EmissiveStrength > 0 ? MaterialEffect.MaterialTypes.Emissive : MaterialEffect.MaterialTypes.Basic;
        material.IsTransparent = Shader == MaterialShader.Water;
        // Water is blended and does not have a matching alpha shadow pass.
        material.HasShadow = CastShadows && Shader != MaterialShader.Water;
        material.Opacity = Limit(Opacity, 0, 1);
        material.WaveScale = Limit(WaveScale, 0.001f, 10);
        material.WaveSpeed = Limit(WaveSpeed, 0, 10);
        material.WaveStrength = Limit(WaveStrength, 0, 2);
    }

    private static float Limit(float value, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
