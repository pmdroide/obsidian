using System.Collections.ObjectModel;
using System.Reflection;
using Anvil.Models;
using Anvil.Services;
using Engine.Components;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Engine.Scripting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

/// <summary>Script Behaviours on the Main Camera, and the Freecam script.</summary>
internal static class FreecamChecks
{
    public static void Run()
    {
        static bool Near(Vector3 a, Vector3 b, float tolerance = 0.001f) => Vector3.Distance(a, b) < tolerance;

        var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
        var logic = new MainSceneLogic();
        var camera = new Camera(new Vector3(0, -10, 2), new Vector3(0, 0, 2));
        logic.ActiveScene.MainCamera = camera;
        var bridge = new EditorBridge();
        typeof(EditorBridge).GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
        var objects = new ObservableCollection<SceneObjectViewModel>();
        Publish();

        var cameraVm = objects.Single(o => o.Kind == EditorObjectKind.Camera);
        Check(cameraVm.CanAddComponents && !cameraVm.HasRole &&
              cameraVm.AddableComponents.Select(c => c.DisplayName).SequenceEqual(new[] { "Script Behaviour" }),
            "the Main Camera offers Add Component with Script Behaviour only");
        logic.ActiveScene.IsDirty = false;
        cameraVm.AddableComponents.Single().AddCommand.Execute(null);
        Publish();
        var component = camera.GetComponent<ScriptBehaviourComponent>();
        var editor = cameraVm.Components.OfType<ScriptBehaviourComponentViewModel>().SingleOrDefault();
        Check(component is { ScriptId: FreecamScript.ScriptId } && editor != null && logic.ActiveScene.IsDirty &&
              ReferenceEquals(component.HostCamera, camera) && !component.IsRunning,
            "adding a Script Behaviour to the Main Camera defaults to Freecam, shows its editor and stays idle in Edit mode");
        Check(!camera.AddComponent(new PhysicsComponent()), "the camera refuses components that need a gameobject");

        var play = new PlayModeController(logic);
        var frame = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.1));
        var keyboard = Input.keyboardState;
        var (mouse, lastMouse) = (Input.mouseState, Input.mouseLastState);
        try
        {
            play.Play();
            Check(component.IsRunning && camera.HasActiveScript && Near(camera.Forward, Vector3.UnitY),
                "Play starts Freecam on the camera without changing the saved view");

            Input.keyboardState = new KeyboardState(Keys.W);
            for (int i = 0; i < 30; i++) play.UpdateScripts(frame);
            Check(camera.Position.Y > -10 + 15 && Math.Abs(camera.Position.X) < 0.001f && Math.Abs(camera.Position.Z - 2) < 0.001f,
                $"holding W flies forward at about the default 8 m/s (moved to {camera.Position})");
            float walked = camera.Position.Y;
            Input.keyboardState = new KeyboardState(Keys.W, Keys.LeftShift);
            play.UpdateScripts(frame);
            Input.keyboardState = new KeyboardState(Keys.E);
            float z = camera.Position.Z;
            for (int i = 0; i < 10; i++) play.UpdateScripts(frame);
            Check(camera.Position.Y - walked > 0.8f * 1.2f && camera.Position.Z > z + 2,
                "Shift moves faster and E rises along world Z");

            Input.keyboardState = default;
            for (int i = 0; i < 30; i++) play.UpdateScripts(frame);
            Vector3 stopped = camera.Position;
            play.UpdateScripts(frame);
            Check(Near(camera.Position, stopped, 0.01f), "releasing the keys brings the camera to a stop");

            Input.mouseLastState = new MouseState(100, 100, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
            Input.mouseState = new MouseState(700, 100, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
            play.UpdateScripts(frame);
            Check(Near(camera.Forward, Vector3.UnitX) && Near(camera.Up, Vector3.UnitZ),
                "dragging right with the right mouse button turns the view right (600 px = 90 degrees) without rolling");
            Input.mouseLastState = Input.mouseState;
            Input.mouseState = new MouseState(700, -2000, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
            play.UpdateScripts(frame);
            Check(camera.Forward.Z > 0.99f && camera.Forward.Z < 1f && Math.Abs(Vector3.Dot(camera.Forward, camera.Up)) < 0.001f,
                "looking up stops just short of vertical");
            Input.mouseLastState = Input.mouseState = default;

            editor!.Enabled = false;
            Publish();
            play.UpdateScripts(frame);
            Check(!component.Enabled && !component.IsRunning && !camera.HasActiveScript,
                "the Inspector's Enabled checkbox reaches the camera's script and stops it");
            editor.Enabled = true;
            Publish();
            play.UpdateScripts(frame);
            Check(component.IsRunning, "re-enabling the camera script in Play starts it again");
        }
        finally
        {
            Input.keyboardState = keyboard;
            (Input.mouseState, Input.mouseLastState) = (mouse, lastMouse);
            play.Stop();
        }
        Check(!component.IsRunning && camera.Position == new Vector3(0, -10, 2) &&
              Near(camera.Forward, Vector3.UnitY) && camera.Up == Vector3.UnitZ,
            "Stop releases the script and puts the camera back where it was before Play");

        string file = Path.Combine(Path.GetTempPath(), $"anvil-freecam-{Guid.NewGuid():N}.obsc");
        try
        {
            SceneSerialization.SaveToFile(logic.ActiveScene, file, assets);
            var restored = SceneSerialization.LoadFromFile(file, assets).MainCamera;
            var restoredScript = restored.GetComponent<ScriptBehaviourComponent>();
            Check(restored.Components.Count == 1 && restoredScript.ScriptId == FreecamScript.ScriptId &&
                  restoredScript.InstanceId == component.InstanceId && ReferenceEquals(restoredScript.HostCamera, restored),
                "the camera's scripts are saved and loaded with the scene");
        }
        finally { File.Delete(file); }

        editor!.RemoveCommand.Execute(null);
        Publish();
        Check(camera.Components.Count == 0 && component.HostCamera == null && !cameraVm.Components.Any(),
            "Remove Component detaches the script from the camera");

        void Publish()
        {
            for (int i = 0; i < 6; i++)
                typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bridge, null);
            BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
