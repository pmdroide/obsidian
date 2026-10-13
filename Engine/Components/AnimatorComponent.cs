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
/// A named animation of an <see cref="AnimatorComponent"/> ("Idle", "Walk", "Jump"), played from a
/// script with <c>GetComponent&lt;AnimatorComponent&gt;().Play("Walk")</c>.
/// </summary>
public sealed class AnimationAction
{
    /// <summary>What scripts call it by (case-insensitive).</summary>
    public string Name { get; set; } = "";
    /// <summary>Content path of the model holding the clip; empty = the Animator's Source.</summary>
    public string ClipSource { get; set; } = "";
    /// <summary>Clip name inside the source; empty = its longest clip.</summary>
    public string ClipName { get; set; } = "";
    /// <summary>Playback rate, multiplied by the Animator's Speed; negative plays backwards.</summary>
    public float Speed { get; set; } = 1f;
    /// <summary>Wraps around; without it the last frame holds and <see cref="AnimatorComponent.ActionFinished"/> fires.</summary>
    public bool Loop { get; set; } = true;
}

/// <summary>
/// Plays skeletal animation clips on a skinned model (one built with the SkinnedModelProcessor)
/// during Play. Outside Play the model shows its bind pose. Clips can come from the model itself
/// or from another skinned model with the same bone names (<see cref="ClipSource"/>).
/// <para>
/// The default clip (<see cref="ClipSource"/>/<see cref="ClipName"/>) plays on start unless
/// <see cref="StartAction"/> names one of the <see cref="Actions"/>. Scripts switch between actions
/// with <see cref="Play(string, float)"/> (crossfading over <see cref="FadeTime"/>), play one-shots
/// with <see cref="PlayOnce"/>, and control playback with <see cref="Stop"/>, <see cref="Pause"/>,
/// <see cref="Resume"/> and <see cref="Restart"/>.
/// </para>
/// </summary>
public sealed class AnimatorComponent : GameComponent
{
    public const string TypeId = "animator";
    /// <summary>The name <see cref="CurrentAction"/> reports, and <see cref="Play(string, float)"/> takes, for the default clip.</summary>
    public const string DefaultClip = "";

    /// <summary>Content path of the model holding the default clip (e.g. "GameObjects/Player/Walking"); empty = this model.</summary>
    public string ClipSource { get; set; } = "";
    /// <summary>Default clip name inside the source; empty = its longest clip.</summary>
    public string ClipName { get; set; } = "";
    /// <summary>Playback rate of everything this Animator plays; 1 = authored speed.</summary>
    public float Speed { get; set; } = 1f;
    /// <summary>Whether the default clip wraps (actions have their own <see cref="AnimationAction.Loop"/>).</summary>
    public bool Loop { get; set; } = true;
    /// <summary>Removes the clips' horizontal root motion so the character animates on the spot.</summary>
    public bool InPlace { get; set; }
    /// <summary>Named animations scripts switch between.</summary>
    public List<AnimationAction> Actions { get; set; } = new();
    /// <summary>Action playing when Play starts; empty = the default clip.</summary>
    public string StartAction { get; set; } = "";
    /// <summary>Seconds <see cref="Play(string, float)"/> blends from the previous animation; 0 switches at once.</summary>
    public float FadeTime { get; set; } = 0.2f;

    /// <summary>Seconds into the current clip (setting it seeks).</summary>
    [JsonIgnore]
    public float Time
    {
        get => _time;
        set
        {
            if (!float.IsFinite(value)) return;
            _time = value;
            _rewind = false;
            _finished = false;
            _posedTime = float.NaN;
        }
    }
    /// <summary>True while a clip poses the character (also while paused); false in the bind pose.</summary>
    [JsonIgnore] public bool IsPlaying => _skin != null && _player?.Clip != null && !_stopped;
    /// <summary>Time is held by <see cref="Pause"/>; the pose stays.</summary>
    [JsonIgnore] public bool IsPaused => _paused && !_stopped;
    /// <summary>Stopped by <see cref="Stop"/>: the character shows its bind pose until <see cref="Play()"/>.</summary>
    [JsonIgnore] public bool IsStopped => _stopped;
    /// <summary>A clip that doesn't loop reached its end (its last frame holds).</summary>
    [JsonIgnore] public bool IsFinished => _finished;
    /// <summary>True while blending from the previous animation.</summary>
    [JsonIgnore] public bool IsFading => _fading;
    /// <summary>Name of the playing action, or <see cref="DefaultClip"/> ("") for the default clip. Before Play: <see cref="StartAction"/>.</summary>
    [JsonIgnore] public string CurrentAction => _selected ? _actionName ?? DefaultClip : StartAction?.Trim() ?? DefaultClip;
    /// <summary>The clip resolved for the current action, or null.</summary>
    [JsonIgnore] public AnimationClip CurrentClip => _player?.Clip;
    /// <summary>Length of the current clip in seconds (0 without one).</summary>
    [JsonIgnore] public float Duration => CurrentClip?.Duration ?? 0f;
    /// <summary>Playback position from 0 (start) to 1 (end) of the current clip.</summary>
    [JsonIgnore] public float NormalizedTime => Duration > 0 ? MathHelper.Clamp(_time / Duration, 0, 1) : 0;
    /// <summary>Plays the current clip; its <see cref="AnimationPlayer.BoneTransforms"/> hold the (blended) pose.</summary>
    [JsonIgnore] public AnimationPlayer Player => _player;

    /// <summary>A non-looping action (or the default clip, name "") reached its end. Runs on the game thread.</summary>
    public event Action<AnimatorComponent, string> ActionFinished;

    private BasicEntity _owner;
    private SkinningData _skeleton;
    private AnimationPlayer _player;
    private SkinnedMeshInstance _skin;
    private bool _resolved;
    private float _time;
    private float _posedTime = float.NaN;

    // What plays. Kept from a Play/Stop call made before OnStart (a script listed above the Animator).
    private bool _started, _selected, _stopped, _paused, _finished;
    // The next bound clip starts at its beginning (its end at a negative speed).
    private bool _rewind;
    private string _actionName;
    // The current action (null = the default clip); its Speed/Loop are read live, so scripts can change them.
    private AnimationAction _action;
    private bool _once;
    private string _then;

    // Crossfade from the previous clip, which keeps advancing while it fades out.
    private AnimationPlayer _fadePlayer;
    private bool _fading, _fadeLoop;
    private float _fadeTime, _fadeSpeed, _fadeElapsed, _fadeDuration;

    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    ////////////////////////////////////////////////////////////////////////////////
    //  SCRIPT API
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>The action called <paramref name="name"/> (case-insensitive), or null.</summary>
    public AnimationAction FindAction(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Actions == null) return null;
        string key = name.Trim();
        foreach (AnimationAction action in Actions)
            if (action != null && SameName(action.Name?.Trim(), key)) return action;
        return null;
    }

    public bool HasAction(string name) => FindAction(name) != null;

    /// <summary>True when <paramref name="name"/> ("" = the default clip) is the current action and isn't stopped.</summary>
    public bool IsPlayingAction(string name) => !_stopped && SameName(name?.Trim() ?? DefaultClip, CurrentAction);

    /// <summary>
    /// Switches to the action called <paramref name="action"/> ("" = the default clip), blending over
    /// <paramref name="fadeSeconds"/> (negative = <see cref="FadeTime"/>). Calling it again for the action
    /// already playing does nothing, so it is safe every frame (<c>Play(moving ? "Walk" : "Idle")</c>).
    /// Also resumes a paused or stopped Animator. Returns false (and logs once) when there is no such action.
    /// </summary>
    public bool Play(string action, float fadeSeconds = -1f)
    {
        string name = action?.Trim() ?? DefaultClip;
        if (!Exists(name)) return false;
        Select();
        _paused = false;
        if (SameName(name, CurrentAction) && !_stopped)
        {
            // Already playing (a one-shot of it now just keeps going).
            _once = false;
            _then = null;
            return true;
        }
        Switch(name, fadeSeconds);
        return true;
    }

    /// <summary>Same as <see cref="Play(string, float)"/>: blends to <paramref name="action"/> over <paramref name="fadeSeconds"/>.</summary>
    public bool CrossFade(string action, float fadeSeconds) => Play(action, Math.Max(fadeSeconds, 0f));

    /// <summary>
    /// Plays <paramref name="action"/> once from its start (even if it loops or is already playing), then
    /// blends to <paramref name="then"/>: null = the action playing before, "" = the default clip.
    /// For jumps, attacks, hit reactions. <see cref="ActionFinished"/> fires when it ends.
    /// </summary>
    public bool PlayOnce(string action, string then = null, float fadeSeconds = -1f)
    {
        string name = action?.Trim() ?? DefaultClip;
        if (!Exists(name)) return false;
        Select();
        // Chained one-shots return to where the first one started.
        string next = then?.Trim() ?? (_once ? _then : CurrentAction);
        _paused = false;
        Switch(name, fadeSeconds);
        _once = true;
        _then = next;
        return true;
    }

    /// <summary>Starts (or resumes) the current action; after <see cref="Stop"/> it plays from the start.</summary>
    public void Play()
    {
        Select();
        _paused = false;
        if (_stopped) Switch(CurrentAction, 0f);
    }

    /// <summary>Plays the current action again from its start (or its end, at a negative speed).</summary>
    public void Restart()
    {
        Select();
        _paused = false;
        if (_stopped || _player?.Clip == null)
        {
            Switch(CurrentAction, 0f);
            return;
        }
        _finished = false;
        _fading = false;
        _rewind = true;
        Rewind();
        _posedTime = float.NaN;
    }

    /// <summary>Stops playback and shows the bind pose until <see cref="Play()"/> or <see cref="Play(string, float)"/>.</summary>
    public void Stop()
    {
        Select();
        _stopped = true;
        _paused = false;
        _finished = false;
        _fading = false;
        _once = false;
        _then = null;
        _time = 0;
        _posedTime = float.NaN;
        ReleaseSkin();
    }

    /// <summary>Holds the current pose; <see cref="Resume"/> (or any Play) continues.</summary>
    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    ////////////////////////////////////////////////////////////////////////////////
    //  ENGINE HOOKS
    ////////////////////////////////////////////////////////////////////////////////

    public override void OnStart(BasicEntity owner)
    {
        OnStop();
        if (owner == null) return; // the camera has no mesh to animate
        _owner = owner;
        _started = true;
        Select();
        if (!_stopped) Resolve();
    }

    public override void OnUpdate(BasicEntity owner, GameTime time)
    {
        if (owner == null) return;
        if (!_started)
        {
            // Added during Play: there was no OnStart.
            _started = true;
            Select();
        }
        if (_stopped) return;
        if (owner != _owner || !_resolved)
        {
            _owner = owner;
            Resolve();
        }
        if (_player?.Clip == null || _skin == null) return;

        // A limp ragdoll owns the pose; the clip picks up again when it recovers.
        if (owner.GetComponent<RagdollComponent>() is { IsActive: true })
        {
            _posedTime = float.NaN;
            return;
        }
        if (_owner.WorldTransform.Skin != _skin)
        {
            _owner.WorldTransform.Skin = _skin;
            _posedTime = float.NaN;
        }

        float dt = _paused ? 0f : (float)time.ElapsedGameTime.TotalSeconds;
        bool reachedEnd = Advance(ref _time, dt * EffectiveSpeed(ClipSpeed), _player.Clip.Duration, ClipLoop && !_once);
        if (_fading && dt > 0)
        {
            Advance(ref _fadeTime, dt * EffectiveSpeed(_fadeSpeed), _fadePlayer.Clip.Duration, _fadeLoop);
            _fadeElapsed += dt;
            if (_fadeElapsed >= _fadeDuration) _fading = false;
            _posedTime = float.NaN;
        }

        // A finished one-shot (or Speed 0) keeps its last pose without re-skinning.
        if (_time != _posedTime || _player.InPlace != InPlace) Pose();

        if (reachedEnd && !_finished) Finish();
    }

    private void Pose()
    {
        _posedTime = _time;
        _player.InPlace = InPlace;
        _player.Evaluate(_time);
        if (_fading)
        {
            _fadePlayer.InPlace = InPlace;
            _fadePlayer.Evaluate(_fadeTime);
            float t = MathHelper.Clamp(_fadeElapsed / _fadeDuration, 0, 1);
            _player.BlendFrom(_fadePlayer, t * t * (3 - 2 * t));
        }
        _skin.Update(_player.SkinTransforms);
        // Skinned vertices moved: redo culling and redraw shadow maps this frame.
        _owner.WorldTransform.HasChanged = true;
    }

    // The clip reached its end: tell scripts, then a one-shot hands over to what comes next.
    private void Finish()
    {
        _finished = true;
        string finished = CurrentAction;
        bool once = _once;
        string then = _then;
        try { ActionFinished?.Invoke(this, finished); }
        catch (Exception ex) { EditorBridge.Log("Animator.ActionFinished threw: " + ex); }

        // Unless a handler already started something else.
        if (!once || !_once || !_finished || !SameName(CurrentAction, finished)) return;
        _once = false;
        _then = null;
        if (then != null && (then.Length == 0 || HasAction(then))) Switch(then, -1f);
    }

    public override void OnChanged(BasicEntity owner)
    {
        base.OnChanged(owner);
        // Pick up a new clip/source/action list on the next Play-mode update.
        _resolved = false;
        _reported.Clear();
        if (!_started) Forget(); // Edit mode: the Start Action may have changed
    }

    public override void OnStop()
    {
        Release();
        // A real stop (end of Play, disabled) forgets what played. Before OnStart it keeps what a
        // script already asked for this session.
        if (!_started) return;
        _started = false;
        Forget();
    }

    public override void OnRemoved(BasicEntity owner)
    {
        base.OnRemoved(owner);
        Forget();
    }

    ////////////////////////////////////////////////////////////////////////////////
    //  PLAYBACK
    ////////////////////////////////////////////////////////////////////////////////

    // Picks the Start Action the first time anything asks this session.
    private void Select()
    {
        if (_selected) return;
        _selected = true;
        _actionName = StartAction?.Trim() ?? DefaultClip;
        _action = FindAction(_actionName);
        _time = 0;
        _rewind = true;
    }

    private void Forget()
    {
        _selected = false;
        _stopped = false;
        _paused = false;
        _finished = false;
        _once = false;
        _then = null;
        _actionName = null;
        _action = null;
        _time = 0;
        _reported.Clear();
    }

    // Makes `name` current from its start. Fades from the clip on screen when there is one.
    private void Switch(string name, float fadeSeconds)
    {
        bool wasPosing = _started && _resolved && IsPlaying;
        float fromTime = _time, fromSpeed = ClipSpeed;
        bool fromLoop = ClipLoop && !_once;

        _selected = true;
        _stopped = false;
        _finished = false;
        _once = false;
        _then = null;
        _actionName = name;
        _action = FindAction(name);
        _time = 0;
        _rewind = true;
        _posedTime = float.NaN;
        if (!_started) return; // OnStart resolves it
        if (!wasPosing)
        {
            _fading = false;
            _resolved = false; // resolves (and poses) on the next update
            return;
        }

        AnimationPlayer next = _fadePlayer ?? new AnimationPlayer(_skeleton);
        if (!BindClip(next))
        {
            Fail();
            return;
        }
        float fade = fadeSeconds < 0 ? FadeTime : fadeSeconds;
        _fading = float.IsFinite(fade) && fade > 0;
        if (_fading)
        {
            // The clip on screen keeps playing underneath while the new one fades in.
            _fadeTime = fromTime;
            _fadeSpeed = fromSpeed;
            _fadeLoop = fromLoop;
            _fadeElapsed = 0;
            _fadeDuration = fade;
        }
        _fadePlayer = _player; // also reused by the next switch
        _player = next;
        Rewind();
    }

    private void Rewind()
    {
        if (!_rewind || _player?.Clip == null) return;
        _rewind = false;
        _time = EffectiveSpeed(ClipSpeed) < 0 ? _player.Clip.Duration : 0;
    }

    private float ClipSpeed => _action == null ? 1f : float.IsFinite(_action.Speed) ? _action.Speed : 1f;
    private bool ClipLoop => _action?.Loop ?? Loop;

    private float EffectiveSpeed(float clipSpeed) => (float.IsFinite(Speed) ? Speed : 1f) * clipSpeed;

    // Moves `time` by `delta` in a clip of `duration`; true when a non-looping clip reaches its end.
    private static bool Advance(ref float time, float delta, float duration, bool loop)
    {
        if (!(duration > 0))
        {
            time = 0;
            return !loop;
        }
        time += delta;
        if (loop)
        {
            time = ((time % duration) + duration) % duration;
            return false;
        }
        bool ended = delta > 0 && time >= duration || delta < 0 && time <= 0;
        time = MathHelper.Clamp(time, 0, duration);
        return ended;
    }

    private bool Exists(string name)
    {
        if (name.Length == 0 || FindAction(name) != null) return true;
        Report("action:" + name, $"Animator: '{_owner?.Name}' has no action '{name}'.");
        return false;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    ////////////////////////////////////////////////////////////////////////////////
    //  RESOLVING CLIPS
    ////////////////////////////////////////////////////////////////////////////////

    private void Resolve()
    {
        Release();
        _resolved = true;
        _skeleton = SkinningData.From(_owner.Model);
        if (_skeleton == null)
        {
            Report("skeleton", $"Animator: '{_owner.Name}' has no skeleton; build its model with the SkinnedModelProcessor.");
            Fail();
            return;
        }

        if (!string.IsNullOrEmpty(_actionName) && FindAction(_actionName) == null)
        {
            Report("action:" + _actionName, $"Animator: '{_owner.Name}' has no action '{_actionName}'; playing the default clip.");
            _actionName = DefaultClip;
        }
        _action = FindAction(_actionName);
        _player = new AnimationPlayer(_skeleton);
        if (!BindClip(_player))
        {
            Fail();
            return;
        }
        Rewind();
        if (_player.Clip.Duration > 0) _time = MathHelper.Clamp(_time, 0, _player.Clip.Duration);

        GraphicsDevice device = FindDevice(_owner.Model);
        if (device == null) return;
        _skin = new SkinnedMeshInstance(device, _owner.Model);
        if (!_skin.HasSkinnedBuffers)
        {
            Report("buffers", $"Animator: '{_owner.Name}' has no BlendIndices/BlendWeight vertices to skin.");
            Fail();
            return;
        }
        // Upload the pose before the buffer is drawn: Play can start after this frame's update.
        Pose();
        if (_owner.GetComponent<RagdollComponent>() is not { IsActive: true }) _owner.WorldTransform.Skin = _skin;
    }

    // Puts the current action's clip (or the default clip) on `player`; false (logged) when it can't be found.
    private bool BindClip(AnimationPlayer player)
    {
        AnimationAction action = FindAction(_actionName);
        string source = action != null && !string.IsNullOrWhiteSpace(action.ClipSource) ? action.ClipSource : ClipSource;
        string clipName = action != null ? action.ClipName : ClipName;

        SkinningData clipSkeleton = _skeleton;
        if (!string.IsNullOrWhiteSpace(source))
        {
            clipSkeleton = LoadSkeleton(source);
            if (clipSkeleton == null) return false;
        }

        AnimationClip clip = clipSkeleton.FindClip(clipName);
        if (clip == null)
        {
            string from = string.IsNullOrWhiteSpace(source) ? _owner.Name : source;
            string of = action != null ? $" (action '{action.Name}')" : "";
            Report("clip:" + from + "|" + clipName, $"Animator: no clip '{clipName}' in '{from}'{of}; showing the bind pose.");
            return false;
        }
        player.SetClip(clip, clipSkeleton);
        return true;
    }

    private void Fail()
    {
        Release();
        _resolved = true; // don't retry every frame; an inspector edit or another action resets this
    }

    private void ReleaseSkin()
    {
        if (_owner == null || _skin == null || _owner.WorldTransform.Skin != _skin) return;
        _owner.WorldTransform.Skin = null;
        _owner.WorldTransform.HasChanged = true;
    }

    private void Release()
    {
        ReleaseSkin();
        _skin?.Dispose();
        _skin = null;
        _player = null;
        _fadePlayer = null;
        _fading = false;
        _resolved = false;
        _posedTime = float.NaN;
    }

    // Logs a problem once per Play session (and inspector edit), not every frame a script retries.
    private void Report(string key, string message)
    {
        if (_reported.Add(key)) EditorBridge.Log(message);
    }

    private SkinningData LoadSkeleton(string contentPath)
    {
        string path = Path.ChangeExtension(contentPath.Trim().Replace('\\', '/'), null);
        try
        {
            SkinningData data = SkinningData.From(Globals.content?.Load<Model>(path));
            if (data == null) Report("source:" + path, $"Animator: clip source '{contentPath}' has no skeleton.");
            return data;
        }
        catch (Exception ex)
        {
            Report("source:" + path, $"Animator: couldn't load clip source '{contentPath}': {ex.Message}");
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
