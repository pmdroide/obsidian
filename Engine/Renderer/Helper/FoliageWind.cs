using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Helper
{
    /// <summary>
    /// Wind state of one swaying gameobject (<see cref="Components.FoliageWindComponent"/>), read by
    /// <c>Shaders/Common/FoliageWind.fx</c> in the G-buffer, shadow map and editor id/outline passes.
    /// Lives on <see cref="Entities.TransformMatrix.Wind"/>; null there = the mesh doesn't move.
    /// </summary>
    public sealed class FoliageWind
    {
        /// <summary>Tree height in world metres (model height x scale): bending is relative to it.</summary>
        public float Height = 8;
        public float Bend = 1;
        public float Branch = 1;
        /// <summary>Leaf flutter; only materials of type <see cref="MaterialEffect.MaterialTypes.Foliage"/> flutter.</summary>
        public float Flutter = 1;
        /// <summary>World direction the wind blows towards (unit length).</summary>
        public Vector2 Direction = Vector2.UnitX;
        /// <summary>0 = calm, ~0.7 = a breeze (4 m/s), 3 = a storm.</summary>
        public float Strength;
        /// <summary>Per-tree offset (cycles) so neighbouring trees don't sway in step.</summary>
        public float Phase;
        /// <summary>The tree's wind clock in seconds; it runs faster in stronger wind.</summary>
        public float Clock;

        /// <summary>Wind speed (m/s) that maps to <see cref="Strength"/> 1.</summary>
        public const float StrengthReferenceSpeed = 6f;
    }

    /// <summary>A render module whose shader includes FoliageWind.fx.</summary>
    public interface IFoliageWindModule
    {
        FoliageWindParameters Wind { get; }
    }

    /// <summary>
    /// The FoliageWind.fx parameters of one effect. <see cref="MeshMaterialLibrary"/> calls
    /// <see cref="SetMaterial"/> per material and <see cref="SetInstance"/> per drawn mesh, before the pass applies.
    /// </summary>
    public sealed class FoliageWindParameters
    {
        private readonly EffectParameter _enabled;
        private readonly EffectParameter _params0;
        private readonly EffectParameter _params1;
        private readonly EffectParameter _time;
        private readonly EffectParameter _leaf;
        private bool _active = true;

        public FoliageWindParameters(Effect effect)
        {
            _enabled = effect.Parameters["WindEnabled"];
            _params0 = effect.Parameters["WindParams0"];
            _params1 = effect.Parameters["WindParams1"];
            _time = effect.Parameters["WindTime"];
            _leaf = effect.Parameters["WindLeaf"];
        }

        /// <summary>Leaves (Foliage material type) flutter; other materials only bend and sway.</summary>
        public void SetMaterial(MaterialEffect material)
        {
            _leaf?.SetValue(material != null && material.Type == MaterialEffect.MaterialTypes.Foliage ? 1f : 0f);
        }

        public void SetInstance(FoliageWind wind)
        {
            if (wind == null)
            {
                // Most meshes don't sway: only touch the shader when the state changes.
                if (_active) _enabled?.SetValue(false);
                _active = false;
                return;
            }
            _active = true;
            _enabled?.SetValue(true);
            _params0?.SetValue(new Vector4(wind.Height, wind.Bend, wind.Branch, wind.Flutter));
            _params1?.SetValue(new Vector4(wind.Direction.X, wind.Direction.Y, wind.Strength, wind.Phase));
            _time?.SetValue(wind.Clock);
        }
    }
}
