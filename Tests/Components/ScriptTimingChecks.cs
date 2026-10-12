using System.Collections;
using Engine.Components;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;

/// <summary>
/// ScriptBehaviour timers (After/Every/StartTimer), coroutines, tweens and easing, plus ScriptMath/ScriptRandom:
/// game time holds while paused, callbacks run after Update, and everything ends with the script.
/// </summary>
internal static class ScriptTimingChecks
{
    public static void Run()
    {
        var script = new TimingScript();
        int fired = 0;
        ScriptTimer once = script.After(1f, () => fired++);
        script.Tick(0.6f);
        Check(fired == 0 && !once.IsDone && Math.Abs(once.Remaining - 0.4f) < 1e-4f && Math.Abs(once.Progress - 0.6f) < 1e-4f,
            "After waits its delay, reporting Remaining and Progress");
        script.Tick(0.6f);
        Check(fired == 1 && once.IsDone && once.Completed == 1 && script.TimerCount == 0, "After runs once, then is done and dropped");
        script.Tick(5f);
        Check(fired == 1, "a finished timer never runs again");
        once.Restart();
        script.Tick(1f);
        Check(fired == 2 && once.IsDone, "Restart runs a finished timer again");

        int ticks = 0, completes = 0;
        ScriptTimer every = script.Every(0.5f, () => ticks++, times: 3).OnComplete(() => completes++);
        script.Tick(0.5f);
        script.Tick(0.5f);
        Check(ticks == 2 && !every.IsDone, "Every runs once per interval");
        script.Tick(5f);
        Check(ticks == 3 && every.IsDone && completes == 1, "Every stops after its count and runs OnComplete once");
        int forever = 0;
        ScriptTimer repeat = script.Every(0.25f, () => forever++);
        script.Tick(1f);
        Check(forever == 4 && repeat.IsRunning, "a long frame catches up on missed intervals");
        repeat.Pause();
        script.Tick(1f);
        Check(forever == 4 && repeat.IsPaused, "a paused timer holds");
        repeat.Resume();
        script.Tick(0.25f);
        repeat.Cancel();
        script.Tick(1f);
        Check(forever == 5 && repeat.IsCancelled && repeat.IsDone, "Resume continues it and Cancel ends it");
        int cancelledCompletes = 0;
        script.After(1f, null).OnComplete(() => cancelledCompletes++).Cancel();
        script.Tick(2f);
        Check(cancelledCompletes == 0, "a cancelled timer skips OnComplete");
        int everyFrame = 0;
        ScriptTimer frames = script.Every(0f, () => everyFrame++);
        script.Tick(0.1f);
        script.Tick(0.1f);
        frames.Cancel();
        Check(everyFrame == 2, "a zero interval runs once per frame instead of looping forever");

        ScriptTimer cooldown = script.StartTimer(0.3f);
        script.Tick(0.2f);
        bool ready = cooldown.IsDone;
        script.Tick(0.2f);
        Check(!ready && cooldown.IsDone, "StartTimer counts down with no action, as a cooldown");

        // Tweens.
        float value = -1f;
        ScriptTimer tween = script.Tween(0f, 10f, 1f, v => value = v);
        Check(value == 0f, "a tween applies its start value straight away");
        script.Tick(0.5f);
        Check(Math.Abs(value - 5f) < 1e-4f, "a linear tween is halfway at half time");
        script.Tick(0.75f);
        Check(value == 10f && tween.IsDone, "a tween ends exactly on its target");
        script.Tween(0f, 1f, 1f, v => value = v, Ease.InQuad);
        script.Tick(0.5f);
        Check(Math.Abs(value - 0.25f) < 1e-4f, "easing shapes the tween (InQuad at half time = 0.25)");
        script.Tick(1f);
        ScriptTimer pingPong = script.Tween(0f, 1f, 1f, v => value = v).Loop(pingPong: true);
        script.Tick(1f);
        script.Tick(0.25f);
        Check(Math.Abs(value - 0.75f) < 1e-4f && pingPong.Completed == 1 && pingPong.IsRunning,
            "a ping-pong loop plays back down after reaching the end");
        pingPong.Complete();
        Check(pingPong.IsDone && value == 0f, "Complete jumps to the end of the current (backwards) cycle");
        Color color = Color.Black;
        script.Tween(Color.Black, Color.White, 1f, c => color = c);
        script.Tick(0.5f);
        Check(Math.Abs(color.R - 128) <= 1, "colours tween too");
        script.Tick(1f);

        foreach (Ease ease in Enum.GetValues<Ease>())
            Check(Math.Abs(Easing.Evaluate(ease, 0f)) < 1e-4f && Math.Abs(Easing.Evaluate(ease, 1f) - 1f) < 1e-4f,
                $"Ease.{ease} starts at 0 and ends at 1");

        // Coroutines.
        var log = new List<string>();
        bool gate = false;
        ScriptCoroutine routine = script.StartCoroutine(Sequence(log, () => gate, script));
        Check(log.SequenceEqual(new[] { "start" }), "a coroutine runs to its first yield straight away");
        script.Tick(0.1f);
        Check(log.Last() == "next frame", "yield return null resumes on the next frame");
        script.Tick(0.4f);
        Check(log.Last() == "next frame", "yield return 0.5f waits half a second");
        script.Tick(0.2f);
        Check(log.Last() == "waited", "the wait ends once the seconds are up");
        script.Tick(1f);
        Check(log.Last() == "waited", "WaitUntil holds while its condition is false");
        gate = true;
        script.Tick(0.1f);
        Check(log.Last() == "nested end" && log.Contains("nested start"), "a yielded IEnumerator runs as a nested step");
        script.Tick(0.5f);
        Check(log.Last() == "nested end", "yielding a tween waits for it");
        script.Tick(0.6f);
        Check(log.Last() == "done" && routine.IsDone && script.CoroutineCount == 0, "the coroutine finishes and is dropped");
        var stopped = script.StartCoroutine(Forever(log));
        script.StopCoroutine(stopped);
        int before = log.Count;
        script.Tick(1f);
        Check(stopped.IsDone && log.Count == before, "StopCoroutine ends it for good");

        // Pause: game time holds, realtime keeps counting (only scripts that update while paused get ticks).
        bool paused = GameFlow.Paused;
        try
        {
            int game = 0, real = 0;
            script.After(0.5f, () => game++);
            script.After(0.5f, () => real++, realtime: true);
            float time = script.Time;
            GameFlow.Paused = true;
            script.Tick(1f);
            Check(game == 0 && real == 1 && script.Time == time, "while paused, game-time timers and Time hold; realtime ones run");
            GameFlow.Paused = false;
            script.Tick(1f);
            Check(game == 1 && script.Time == time + 1f, "they resume with the game");
        }
        finally { GameFlow.Paused = paused; }

        // Update runs before timers, and Shutdown ends everything.
        script.Order.Clear();
        script.After(0f, () => script.Order.Add("timer"));
        script.Tick(0.01f);
        Check(script.Order.SequenceEqual(new[] { "update", "timer" }), "timers run after Update in the same frame");
        ScriptTimer leftover = script.Every(1f, () => { });
        ScriptCoroutine leftoverRoutine = script.StartCoroutine(Forever(log));
        script.Shutdown();
        Check(leftover.IsCancelled && leftoverRoutine.IsDone && script.TimerCount == 0 && script.CoroutineCount == 0,
            "stopping the script cancels its timers and coroutines");

        // Transform tweens and the Timer Example on a real gameobject.
        var model = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One));
        var entity = new BasicEntity(model, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        var mover = new TimingScript();
        mover.Attach(entity);
        mover.MoveTo(new Vector3(4, 0, 0), 1f, Ease.Linear);
        mover.ScaleTo(new Vector3(2), 1f);
        mover.RotateTo(0, 0, 90, 1f);
        mover.Tick(0.5f);
        Check(Vector3.Distance(entity.Position, new Vector3(2, 0, 0)) < 1e-3f, "MoveTo slides the gameobject");
        mover.Tick(0.5f);
        Check(entity.Position == new Vector3(4, 0, 0) && Vector3.Distance(entity.Scale, new Vector3(2)) < 1e-4f &&
              Vector3.Distance(mover.Forward, -Vector3.UnitX) < 1e-3f,
            "MoveTo, ScaleTo and RotateTo end on their targets");

        Check(ScriptRegistry.Find(TimerExampleScript.ScriptId)?.DisplayName == "Timer Example", "the Timer Example script is registered");
        var exampleEntity = new BasicEntity(model, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        var example = new TimerExampleScript();
        example.Attach(exampleEntity);
        example.Start();
        float highest = 0f, smallest = 1f, largest = 1f;
        for (int i = 0; i < 6 * 60; i++)
        {
            example.Tick(1 / 60f);
            highest = Math.Max(highest, exampleEntity.Position.Z);
            smallest = Math.Min(smallest, exampleEntity.Scale.X);
            largest = Math.Max(largest, exampleEntity.Scale.X);
        }
        Check(highest > 0.9f && smallest >= 1f - 1e-4f && largest > 1.09f,
            "the Timer Example hops (coroutine + MoveTo) and breathes (ping-pong tween)");
        example.Shutdown();

        // A callback that throws stops the script like a throwing Update.
        ScriptRegistry.Register<ThrowingTimerScript>("test-throwing-timer", "Test Throwing Timer");
        var component = new ScriptBehaviourComponent { ScriptId = "test-throwing-timer" };
        var host = new BasicEntity(model, null, Vector3.Zero, Matrix.Identity, Vector3.One);
        var frame = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.5));
        component.OnStart(host);
        component.OnUpdate(host, frame);
        Check(!component.IsRunning && component.LastError?.Contains("Update failed") == true,
            "an exception in a timer callback stops the script with an Update error");
        component.OnStop();

        // ScriptMath and ScriptRandom.
        Check(ScriptMath.MoveTowards(0f, 10f, 3f) == 3f && ScriptMath.MoveTowards(9f, 10f, 3f) == 10f &&
              ScriptMath.MoveTowards(Vector3.Zero, new Vector3(10, 0, 0), 4f) == new Vector3(4, 0, 0),
            "MoveTowards steps without overshooting");
        float stepped = ScriptMath.Damp(ScriptMath.Damp(0f, 1f, 5f, 0.05f), 1f, 5f, 0.05f);
        Check(Math.Abs(stepped - ScriptMath.Damp(0f, 1f, 5f, 0.1f)) < 1e-5f, "Damp gives the same result at any frame rate");
        float position = 0f, velocity = 0f;
        for (int i = 0; i < 300; i++) position = ScriptMath.SmoothDamp(position, 5f, ref velocity, 0.3f, 1 / 60f);
        Check(Math.Abs(position - 5f) < 1e-3f, "SmoothDamp settles on its target");
        Check(ScriptMath.DeltaAngle(350f, 10f) == 20f && ScriptMath.DeltaAngle(10f, 350f) == -20f &&
              ScriptMath.Repeat(-1f, 3f) == 2f && ScriptMath.PingPong(1.5f, 1f) == 0.5f &&
              ScriptMath.Remap(5f, 0f, 10f, 100f, 200f) == 150f,
            "DeltaAngle, Repeat, PingPong and Remap");
        ScriptRandom.Seed(7);
        float a = ScriptRandom.Range(0f, 100f);
        int pickedA = ScriptRandom.Range(0, 1000);
        ScriptRandom.Seed(7);
        Check(ScriptRandom.Range(0f, 100f) == a && ScriptRandom.Range(0, 1000) == pickedA, "the same seed repeats the same numbers");
        bool inRange = true;
        for (int i = 0; i < 1000; i++)
        {
            int n = ScriptRandom.Range(2, 5);
            float f = ScriptRandom.Range(-1f, 1f);
            inRange &= n >= 2 && n < 5 && f >= -1f && f <= 1f && ScriptRandom.InsideUnitSphere.Length() <= 1.0001f &&
                       Math.Abs(ScriptRandom.OnUnitSphere.Length() - 1f) < 1e-4f && ScriptRandom.InsideUnitCircle.Length() <= 1.0001f;
        }
        Check(inRange, "random ranges (int max exclusive), sphere and circle points stay in bounds");
        ScriptRandom.Seed(Environment.TickCount);
    }

    private static IEnumerator Sequence(List<string> log, Func<bool> gate, TimingScript script)
    {
        log.Add("start");
        yield return null;
        log.Add("next frame");
        yield return 0.5f;
        log.Add("waited");
        yield return new WaitUntil(gate);
        yield return Nested(log);
        yield return script.Tween(1f, _ => { });
        log.Add("done");
    }

    private static IEnumerator Nested(List<string> log)
    {
        log.Add("nested start");
        log.Add("nested end");
        yield break;
    }

    private static IEnumerator Forever(List<string> log)
    {
        while (true)
        {
            yield return null;
            log.Add("tick");
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private sealed class TimingScript : ScriptBehaviour
    {
        public readonly List<string> Order = new();
        public int TimerCount => ((List<ScriptTimer>)typeof(ScriptBehaviour)
            .GetField("_timers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(this)!).Count;
        public int CoroutineCount => ((List<ScriptCoroutine>)typeof(ScriptBehaviour)
            .GetField("_coroutines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(this)!).Count;
        public override void Update() => Order.Add("update");
    }
}

public sealed class ThrowingTimerScript : ScriptBehaviour
{
    public override void Start() => After(0f, () => throw new InvalidOperationException("timer boom"));
}
