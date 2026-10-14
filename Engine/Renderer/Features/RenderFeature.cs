using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Features
{
    /// <summary>
    /// Where a render feature runs in <see cref="Renderer.Draw"/>. Saved by name: never rename a value.
    /// Ordered as in the pipeline.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RenderFeatureEvent
    {
        /// <summary>After the deferred compose (lit opaque scene, HDR), before forward/water/particles/weather.</summary>
        AfterOpaques,
        /// <summary>After water, particles and weather (whole HDR scene), before temporal AA and bloom.</summary>
        AfterTransparents,
        /// <summary>After temporal AA and bloom (HDR), before tonemapping and colour grading.</summary>
        AfterBloom,
        /// <summary>After tonemapping and colour grading (final LDR image), under the editor gizmos and the game UI.</summary>
        AfterPostProcessing,
    }

    /// <summary>How a feature's output lands on the image. Saved by name: never rename a value.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RenderFeatureBlend
    {
        /// <summary>The shader's output replaces the image (it reads <c>ScreenTexture</c> to keep it).</summary>
        Replace,
        /// <summary>Drawn over the image with the output alpha.</summary>
        AlphaBlend,
        /// <summary>The output is added to the image.</summary>
        Additive,
        /// <summary>The image is multiplied by the output.</summary>
        Multiply,
    }

    public enum RenderFeatureStatus
    {
        /// <summary>No compiled shader yet (or the shader file is missing).</summary>
        NotCompiled,
        Compiling,
        Ready,
        /// <summary>The last compile or load failed; <see cref="RenderFeature.Error"/> says why. A previous good shader keeps running.</summary>
        Error,
    }

    /// <summary>
    /// A user render pass: a full-screen shader (<c>Content/RenderFeatures/&lt;Name&gt;.fx</c>) drawn at an
    /// <see cref="RenderFeatureEvent"/> of the render pipeline. Project-wide, saved in
    /// <c>Content/System/RenderFeatures.json</c> (Anvil > Render Pipeline). Public get/set properties are saved.
    /// </summary>
    public sealed class RenderFeature
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Render Feature";
        public bool Enabled { get; set; } = true;
        public RenderFeatureEvent Event { get; set; } = RenderFeatureEvent.AfterTransparents;
        /// <summary>Content-relative .fx path, e.g. <c>RenderFeatures/Vignette.fx</c>.</summary>
        public string Shader { get; set; }
        /// <summary>Technique to run; empty runs the shader's first technique. Every pass of it is drawn.</summary>
        public string Technique { get; set; } = "";
        public RenderFeatureBlend Blend { get; set; } = RenderFeatureBlend.Replace;
        /// <summary>Also drawn while editing in Anvil (off: Play and the built game only).</summary>
        public bool ShowInEditMode { get; set; } = true;
        /// <summary>Shader parameter <c>Intensity</c>.</summary>
        public float Intensity { get; set; } = 1f;
        /// <summary>Shader parameter <c>Color</c> (float4, RGBA 0..1).</summary>
        public float[] Color { get; set; } = { 1f, 1f, 1f, 1f };
        /// <summary>Shader parameter <c>Params</c> (float4), free for the shader to use.</summary>
        public float[] Params { get; set; } = { 0f, 0f, 0f, 0f };

        [JsonIgnore] public RenderFeatureStatus Status { get; internal set; } = RenderFeatureStatus.NotCompiled;
        /// <summary>Compiler output of the last failed compile or load, else null.</summary>
        [JsonIgnore] public string Error { get; internal set; }

        // Runtime: the loaded effect, owned through its content manager.
        [JsonIgnore] internal Effect Effect;
        [JsonIgnore] internal ContentManager EffectContent;
        [JsonIgnore] internal DateTime CompiledSourceTimeUtc;
        [JsonIgnore] internal readonly Dictionary<string, EffectParameter> ParameterCache = new Dictionary<string, EffectParameter>();

        /// <summary>Whether the renderer draws it this frame.</summary>
        [JsonIgnore]
        public bool IsActive => Enabled && Effect != null && (ShowInEditMode || !Recources.GameSettings.e_enableeditor);

        internal void Normalize()
        {
            if (Id == Guid.Empty) Id = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(Name)) Name = "Render Feature";
            Technique ??= "";
            Shader = string.IsNullOrWhiteSpace(Shader) ? null : Shader.Trim().Replace('\\', '/').TrimStart('/');
            if (!float.IsFinite(Intensity)) Intensity = 1f;
            Color = Fix4(Color, 1f);
            Params = Fix4(Params, 0f);
            if (!Enum.IsDefined(typeof(RenderFeatureEvent), Event)) Event = RenderFeatureEvent.AfterTransparents;
            if (!Enum.IsDefined(typeof(RenderFeatureBlend), Blend)) Blend = RenderFeatureBlend.Replace;
        }

        private static float[] Fix4(float[] values, float fallback)
        {
            var result = new float[4];
            for (int i = 0; i < 4; i++)
                result[i] = values != null && i < values.Length && float.IsFinite(values[i]) ? values[i] : fallback;
            return result;
        }

        /// <summary>The saved settings only (no shader).</summary>
        public RenderFeature CloneSettings() => new RenderFeature
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Event = Event,
            Shader = Shader,
            Technique = Technique,
            Blend = Blend,
            ShowInEditMode = ShowInEditMode,
            Intensity = Intensity,
            Color = (float[])Color?.Clone(),
            Params = (float[])Params?.Clone(),
            Status = Status,
            Error = Error,
        };

        internal void CopySettingsFrom(RenderFeature other)
        {
            Name = other.Name;
            Enabled = other.Enabled;
            Event = other.Event;
            Shader = other.Shader;
            Technique = other.Technique;
            Blend = other.Blend;
            ShowInEditMode = other.ShowInEditMode;
            Intensity = other.Intensity;
            Color = (float[])other.Color?.Clone();
            Params = (float[])other.Params?.Clone();
            Normalize();
        }
    }

    public enum RenderPipelineLayerKind
    {
        /// <summary>A built-in pass of the engine.</summary>
        Stage,
        /// <summary>A place where render features run (<see cref="RenderFeatureEvent"/>).</summary>
        InjectionPoint,
        /// <summary>A user render feature.</summary>
        Feature,
    }

    /// <summary>One row of the render pipeline as Anvil lists it (<see cref="RenderFeatures.Describe"/>).</summary>
    public sealed class RenderPipelineLayer
    {
        public RenderPipelineLayerKind Kind { get; init; }
        public string Name { get; init; }
        public string Detail { get; init; }
        /// <summary>Stage: switched on in the render settings. Feature: enabled. Injection point: always true.</summary>
        public bool Active { get; init; }
        public RenderFeatureEvent Event { get; init; }
        /// <summary>Feature rows: a copy of its settings and status.</summary>
        public RenderFeature Feature { get; init; }
    }
}
