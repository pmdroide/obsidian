using System.IO;
using System.Text.Json.Serialization;
using Engine.Animation;
using Engine.Editor;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Components;

/// <summary>
/// Plays a skeletal animation clip on a skinned model (one built with the SkinnedModelProcessor)
/// during Play. Outside Play the model shows its bind pose. The clip can come from the model
/// itself or from another skinned model with the same bone names (<see cref="ClipSource"/>).
/// </summary>
public sealed class AnimatorComponent : GameComponent
{
    public const string TypeId = "animator";

    /// <summary>Content path of the model holding the clip (e.g. "GameObjects/Player/Walking"); empty = this model.</summary>
    public string ClipSource { get; set; } = "";
    /// <summary>Clip name inside the source; empty = its longest clip.</summary>
    public string ClipName { get; set; } = "";
    /// <summary>Playback rate; 1 = authored speed.</summary>
    public float Speed { get; set; } = 1f;
    public bool Loop { get; set; } = true;
    /// <summary>Removes the clip's horizontal root motion so the character animates on the spot.</summary>
    public bool InPlace { get; set; }

    /// <summary>Seconds into the current clip.</summary>
    [JsonIgnore] public float Time { get; private set; }
    [JsonIgnore] public bool IsPlaying => _skin != null;
    /// <summary>The clip resolved on the last start, or null.</summary>
    [JsonIgnore] public AnimationClip CurrentClip => _player?.Clip;
    [JsonIgnore] public AnimationPlayer Player => _player;

    private BasicEntity _owner;
    private AnimationPlayer _player;
    private SkinnedMeshInstance _skin;
    private bool _resolved;
    private float _posedTime = float.NaN;

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        if (owner == null) return; // the camera has no mesh to animate
        _owner = owner;
        Time = 0;
        Resolve();
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (owner != _owner || !_resolved)
        {
            _owner = owner;
            Resolve();
        }
        if (_player?.Clip == null || _skin == null) return;

        AnimationClip clip = _player.Clip;
        float speed = float.IsFinite(Speed) ? Speed : 1f;
        Time += (float)time.ElapsedGameTime.TotalSeconds * speed;
        if (clip.Duration > 0)
        {
            if (Loop) Time = ((Time % clip.Duration) + clip.Duration) % clip.Duration;
            else Time = MathHelper.Clamp(Time, 0, clip.Duration);
        }
        else Time = 0;

        // A finished one-shot (or Speed 0) keeps its last pose without re-skinning.
        if (Time == _posedTime && _player.InPlace == InPlace) return;
        Pose();
    }

    private void Pose()
    {
        _posedTime = Time;
        _player.InPlace = InPlace;
        _player.Evaluate(Time);
        _skin.Update(_player.SkinTransforms);
        // Skinned vertices moved: redo culling and redraw shadow maps this frame.
        _owner.WorldTransform.HasChanged = true;
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        // Pick up a new clip/source on the next Play-mode update.
        _resolved = false;
    }

    public override void OnStop()
    {
        if (_owner != null && _owner.WorldTransform.Skin == _skin)
        {
            _owner.WorldTransform.Skin = null;
            _owner.WorldTransform.HasChanged = true;
        }
        _skin?.Dispose();
        _skin = null;
        _player = null;
        _resolved = false;
        _posedTime = float.NaN;
    }

    private void Resolve()
    {
        _resolved = true;
        SkinningData skeleton = SkinningData.From(_owner.Model);
        if (skeleton == null)
        {
            EditorBridge.Log($"Animator: '{_owner.Name}' has no skeleton; build its model with the SkinnedModelProcessor.");
            Fail();
            return;
        }

        SkinningData clipSkeleton = skeleton;
        if (!string.IsNullOrWhiteSpace(ClipSource))
        {
            clipSkeleton = LoadSkeleton(ClipSource);
            if (clipSkeleton == null)
            {
                Fail();
                return;
            }
        }

        AnimationClip clip = clipSkeleton.FindClip(ClipName);
        if (clip == null)
        {
            EditorBridge.Log($"Animator: no clip '{ClipName}' in '{(string.IsNullOrWhiteSpace(ClipSource) ? _owner.Name : ClipSource)}'; showing the bind pose.");
            Fail();
            return;
        }

        if (_player == null || _player.Skeleton != skeleton) _player = new AnimationPlayer(skeleton);
        _player.SetClip(clip, clipSkeleton);

        if (_skin == null)
        {
            GraphicsDevice device = FindDevice(_owner.Model);
            if (device == null) return;
            _skin = new SkinnedMeshInstance(device, _owner.Model);
            if (!_skin.HasSkinnedBuffers)
            {
                EditorBridge.Log($"Animator: '{_owner.Name}' has no BlendIndices/BlendWeight vertices to skin.");
                Fail();
                return;
            }
        }
        // Upload the pose before the buffer is drawn: Play can start after this frame's update.
        Pose();
        _owner.WorldTransform.Skin = _skin;
    }

    private void Fail()
    {
        OnStop();
        _resolved = true; // don't retry every frame; an inspector edit resets this
    }

    private static SkinningData LoadSkeleton(string contentPath)
    {
        string path = Path.ChangeExtension(contentPath.Trim().Replace('\\', '/'), null);
        try
        {
            SkinningData data = SkinningData.From(Globals.content?.Load<Model>(path));
            if (data == null) EditorBridge.Log($"Animator: clip source '{contentPath}' has no skeleton.");
            return data;
        }
        catch (Exception ex)
        {
            EditorBridge.Log($"Animator: couldn't load clip source '{contentPath}': {ex.Message}");
            return null;
        }
    }

    private static GraphicsDevice FindDevice(Model model)
    {
        foreach (ModelMesh mesh in model.Meshes)
            foreach (ModelMeshPart part in mesh.MeshParts)
                if (part.VertexBuffer != null) return part.VertexBuffer.GraphicsDevice;
        return null;
    }
}
