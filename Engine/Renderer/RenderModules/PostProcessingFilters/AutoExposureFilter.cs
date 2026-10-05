using System;
using Engine.Recources;
using Engine.Renderer.Helper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.RenderModules.PostProcessingFilters
{
    /// <summary>
    /// Eye adaptation / auto exposure.
    ///
    /// Meters the HDR image (center-weighted average of log2 luminance), then moves a 1x1 exposure value
    /// (in EV) toward the target that maps that average to <see cref="GameSettings.g_AutoExposureKey"/>.
    /// The adapted EV stays on the GPU; PostProcessing.fx reads it and multiplies it into its exposure,
    /// so the manual Exposure setting and the day/night offset act as compensation on top.
    ///
    /// Run Draw once per frame before tonemapping; it keeps its own history between frames.
    /// </summary>
    public class AutoExposureFilter : IDisposable
    {
        // 256 -> 64 -> 16 -> 4 -> 1; every downsample step is a 4x4 box.
        private const int MeterSize = 256;
        private const int ChainLength = 5;

        private readonly Effect _effect;
        private readonly EffectParameter _inputTextureParam;
        private readonly EffectParameter _previousExposureParam;
        private readonly EffectParameter _outputSizeParam;
        private readonly EffectParameter _centerWeightParam;
        private readonly EffectParameter _keyValueParam;
        private readonly EffectParameter _minExposureParam;
        private readonly EffectParameter _maxExposureParam;
        private readonly EffectParameter _speedDarkToLightParam;
        private readonly EffectParameter _speedLightToDarkParam;
        private readonly EffectParameter _deltaTimeParam;
        private readonly EffectParameter _resetParam;
        private readonly EffectPass _meterPass;
        private readonly EffectPass _downsamplePass;
        private readonly EffectPass _adaptPass;

        private FullScreenTriangle _fullScreenTriangle;
        private RenderTarget2D[] _luminanceChain;
        private RenderTarget2D _exposure1;
        private RenderTarget2D _exposure2;
        private bool _exposureFlip;
        private bool _hasHistory;

        /// <summary>The adapted exposure (1x1, EV) written by the last Draw, or null before the first one.</summary>
        public Texture2D CurrentExposure { get; private set; }

        public AutoExposureFilter(ContentManager content, string shaderPath)
        {
            _effect = content.Load<Effect>(shaderPath);
            _inputTextureParam = _effect.Parameters["InputTexture"];
            _previousExposureParam = _effect.Parameters["PreviousExposureTexture"];
            _outputSizeParam = _effect.Parameters["OutputSize"];
            _centerWeightParam = _effect.Parameters["CenterWeight"];
            _keyValueParam = _effect.Parameters["KeyValue"];
            _minExposureParam = _effect.Parameters["MinExposure"];
            _maxExposureParam = _effect.Parameters["MaxExposure"];
            _speedDarkToLightParam = _effect.Parameters["SpeedDarkToLight"];
            _speedLightToDarkParam = _effect.Parameters["SpeedLightToDark"];
            _deltaTimeParam = _effect.Parameters["DeltaTime"];
            _resetParam = _effect.Parameters["Reset"];

            _meterPass = _effect.Techniques["MeterLuminance"].Passes[0];
            _downsamplePass = _effect.Techniques["Downsample"].Passes[0];
            _adaptPass = _effect.Techniques["Adapt"].Passes[0];
        }

        public void Initialize(GraphicsDevice graphics, FullScreenTriangle fullScreenTriangle)
        {
            _fullScreenTriangle = fullScreenTriangle;

            _luminanceChain = new RenderTarget2D[ChainLength];
            for (int i = 0, size = MeterSize; i < ChainLength; i++, size /= 4)
                _luminanceChain[i] = new RenderTarget2D(graphics, size, size, false, SurfaceFormat.Vector2, DepthFormat.None);

            _exposure1 = new RenderTarget2D(graphics, 1, 1, false, SurfaceFormat.Single, DepthFormat.None);
            _exposure2 = new RenderTarget2D(graphics, 1, 1, false, SurfaceFormat.Single, DepthFormat.None);
        }

        /// <summary>Snap to the metered exposure on the next Draw instead of adapting from the old value.</summary>
        public void Reset()
        {
            _hasHistory = false;
        }

        /// <summary>
        /// Meters <paramref name="hdrInput"/> and adapts the exposure by <paramref name="deltaSeconds"/>.
        /// Returns the 1x1 exposure texture (EV in the red channel).
        /// </summary>
        public Texture2D Draw(GraphicsDevice graphics, Texture2D hdrInput, float deltaSeconds)
        {
            graphics.BlendState = BlendState.Opaque;
            graphics.DepthStencilState = DepthStencilState.None;
            graphics.RasterizerState = RasterizerState.CullNone;

            _centerWeightParam.SetValue(MathHelper.Clamp(GameSettings.g_AutoExposureCenterWeight, 0, 1));
            _keyValueParam.SetValue(Math.Max(GameSettings.g_AutoExposureKey, 0.001f));
            _minExposureParam.SetValue(Math.Min(GameSettings.g_AutoExposureMin, GameSettings.g_AutoExposureMax));
            _maxExposureParam.SetValue(Math.Max(GameSettings.g_AutoExposureMin, GameSettings.g_AutoExposureMax));
            _speedDarkToLightParam.SetValue(Math.Max(GameSettings.g_AutoExposureSpeedDarkToLight, 0));
            _speedLightToDarkParam.SetValue(Math.Max(GameSettings.g_AutoExposureSpeedLightToDark, 0));
            // A long hitch (loading, breakpoint) should not count as seconds of adaptation.
            _deltaTimeParam.SetValue(MathHelper.Clamp(deltaSeconds, 0, 0.25f));
            _resetParam.SetValue(_hasHistory ? 0f : 1f);

            // Meter
            graphics.SetRenderTarget(_luminanceChain[0]);
            _inputTextureParam.SetValue(hdrInput);
            _outputSizeParam.SetValue(new Vector2(MeterSize, MeterSize));
            _meterPass.Apply();
            _fullScreenTriangle.Draw(graphics);

            // Reduce to 1x1
            for (int i = 1; i < ChainLength; i++)
            {
                graphics.SetRenderTarget(_luminanceChain[i]);
                _inputTextureParam.SetValue(_luminanceChain[i - 1]);
                _downsamplePass.Apply();
                _fullScreenTriangle.Draw(graphics);
            }

            // Adapt: ping-pong so last frame's value can be read while writing this frame's.
            RenderTarget2D previous = _exposureFlip ? _exposure1 : _exposure2;
            RenderTarget2D output = _exposureFlip ? _exposure2 : _exposure1;
            _exposureFlip = !_exposureFlip;

            graphics.SetRenderTarget(output);
            _inputTextureParam.SetValue(_luminanceChain[ChainLength - 1]);
            _previousExposureParam.SetValue(previous);
            _adaptPass.Apply();
            _fullScreenTriangle.Draw(graphics);

            // Unbind so the next frame can render into these targets.
            _inputTextureParam.SetValue((Texture2D)null);
            _previousExposureParam.SetValue((Texture2D)null);

            _hasHistory = true;
            CurrentExposure = output;
            return output;
        }

        public void Dispose()
        {
            _effect?.Dispose();
            if (_luminanceChain != null)
                foreach (RenderTarget2D target in _luminanceChain)
                    target?.Dispose();
            _exposure1?.Dispose();
            _exposure2?.Dispose();
        }
    }
}
