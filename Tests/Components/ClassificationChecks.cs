using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using Anvil.Models;
using Anvil.Services;
using Anvil.ViewModels;
using Avalonia.Threading;
using Engine.Editor;
using Engine.Entities;
using Engine.Logic;
using Engine.Recources;
using Microsoft.Xna.Framework;

internal static class ClassificationChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "obsidian-classifications-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = ClassificationCatalog.Project;
        string? previousPrefabs = Prefab.SourceRootOverride;
        try
        {
            string path = Path.Combine(root, "Classifications.json");
            var catalog = new ClassificationCatalog(path);
            ClassificationCatalog.Project = catalog;
            Prefab.SourceRootOverride = root;
            Check(catalog.Entries.Count == 10 && catalog.Entries.All(e => e.IsSystem), "project classifications include the ten locked built-in layers, tags and roles");
            foreach (var entry in catalog.Entries)
                Check(!catalog.Rename(entry.Kind, entry.Id, "Changed", out _) && !catalog.Remove(entry.Kind, entry.Id, out _) &&
                      catalog.Find(entry.Kind, entry.Id) == entry, $"system {entry.Kind} '{entry.Name}' cannot be renamed or removed");
            Check(!catalog.Add(ClassificationKind.Tag, " player ", out _) && !catalog.Add(ClassificationKind.Tag, " ", out _) &&
                  !catalog.Add(ClassificationKind.Tag, "a\nb", out _) && !catalog.Add(ClassificationKind.Tag, new string('a', 65), out _),
                "classification names reject duplicate, blank, control-character and overlong input");

            var assets = new Assets { Cube = new ModelDefinition(null, new BoundingBox(-Vector3.One, Vector3.One)) };
            var logic = new MainSceneLogic();
            var entity = new BasicEntity(assets.Cube, null, Vector3.Zero, Matrix.Identity, Vector3.One) { Name = "Classified object" };
            logic.BasicEntities.Add(entity);
            var bridge = new EditorBridge();
            typeof(EditorBridge).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(bridge, new object[] { logic, new EditorLogic(), assets });
            var objects = new ObservableCollection<SceneObjectViewModel>();
            var manager = new ClassificationsViewModel();
            manager.AttachBridge(bridge);
            Publish();
            var vm = objects.Single();
            var main = new MainWindowViewModel();
            main.OpenWorkspaceTabCommand.Execute("Classifications");
            var tab = main.SelectedWorkspaceTab;
            main.OpenWorkspaceTabCommand.Execute("Classifications");
            Check(main.IsWorkspaceClassificationsView && ReferenceEquals(tab, main.SelectedWorkspaceTab), "the Layers workspace command opens and reuses the classification panel");

            foreach (var group in manager.Groups)
            {
                Check(group.Entries.All(e => !e.RenameCommand.CanExecute(null) && !e.RemoveCommand.CanExecute(null)),
                    $"the {group.Title} panel locks its system row commands");
                group.NewName = " Quest " + group.Title;
                group.AddCommand.Execute(null);
                Publish();
                Check(group.Error == "" && group.NewName == "" && group.Entries.Any(e => !e.IsSystem && e.Name == "Quest " + group.Title),
                    $"the {group.Title} Add command saves and refreshes custom entries");
            }
            string layerId = catalog.FindName(ClassificationKind.Layer, "Quest Layers").Id;
            string tagId = catalog.FindName(ClassificationKind.Tag, "Quest Tags").Id;
            string roleId = catalog.FindName(ClassificationKind.Role, "Quest Roles").Id;
            vm.Tag = "Quest Tags"; vm.Layer = "Quest Layers"; vm.RoleName = "Quest Roles";
            Publish();
            Check(entity.TagId == tagId && entity.LayerId == layerId && entity.RoleId == roleId && logic.ActiveScene.IsDirty,
                "Inspector classification selections reach the game thread and mark the scene dirty");
            Check(vm.Role == GameObjectRole.Default && vm.RoleName == "Quest Roles" && vm.AvailableRoles.Contains("Water"),
                "custom roles coexist with the built-in role API and Inspector options");
            vm.Tag = null!; vm.Layer = null!; vm.RoleName = null!;
            Publish();
            Check(entity.TagId == tagId && entity.LayerId == layerId && entity.RoleId == roleId, "transient cleared ComboBox selections do not reset classifications");
            var clone = (BasicEntity)entity.Clone;
            Check(clone.TagId == tagId && clone.LayerId == layerId && clone.RoleId == roleId, "duplicating a gameobject preserves its classification IDs");

            string scenePath = Path.Combine(root, "Scene.obsc");
            SceneSerialization.SaveToFile(logic.ActiveScene, scenePath, assets);
            string sceneBeforeRename = File.ReadAllText(scenePath);
            var prefabRecord = SceneSerialization.CaptureEntity(entity, assets);
            string prefabPath = Path.Combine(root, "Classified.prefab");
            File.WriteAllText(prefabPath, JsonSerializer.Serialize(new Prefab.Document { GameObject = prefabRecord }, SceneSerialization.JsonOptions));
            var prefab = Prefab.Load("Classified");
            Check(prefab != null && prefab.Record.TagId == tagId && prefab.Record.RoleId == roleId, "prefabs persist classification IDs");

            foreach (var group in manager.Groups)
            {
                var row = group.Entries.Single(e => !e.IsSystem);
                row.Name = "Renamed " + group.Title;
                row.RenameCommand.Execute(null);
                Publish();
                Check(group.Error == "" && catalog.Find(group.Kind, row.Id).Name == row.Name,
                    $"the {group.Title} Rename command preserves the entry ID");
            }
            var loaded = SceneSerialization.LoadFromFile(scenePath, assets).BasicEntities.Single();
            var prefabEntity = SceneSerialization.RestoreEntity(prefab!.Record, SceneSerialization.CurrentVersion, assets);
            Check(loaded.Tag == "Renamed Tags" && loaded.Layer == "Renamed Layers" && loaded.RoleName == "Renamed Roles" &&
                  prefabEntity.Tag == loaded.Tag && clone.Tag == loaded.Tag && File.ReadAllText(scenePath) == sceneBeforeRename,
                "renames follow stable IDs in live objects, copies, saved scenes and prefabs without rewriting files");
            Check(vm.Tag == loaded.Tag && vm.Layer == loaded.Layer && vm.RoleName == loaded.RoleName,
                "classification renames refresh Inspector selections and option names");
            var reloadedCatalog = new ClassificationCatalog(path);
            Check(reloadedCatalog.Find(ClassificationKind.Tag, tagId).Name == "Renamed Tags", "project classifications survive an editor restart");

            vm.RoleName = "Water";
            Publish();
            Check(entity.Role == GameObjectRole.Water && vm.IsWaterRole, "the protected Water role still activates existing engine behavior");
            vm.RoleName = "Renamed Roles";
            Publish();
            bridge.EnqueueSetRole(entity.Id, GameObjectRole.Default);
            Publish();
            Check(entity.RoleId == "Default", "the legacy role setter can reset a custom role to Default");
            vm.RoleName = "Renamed Roles";
            Publish();

            string? denied = null;
            bridge.EnqueueEditClassification(ClassificationEdit.Remove, ClassificationKind.Role, "Water", null!, error => denied = error);
            Publish();
            Check(denied != null && catalog.Find(ClassificationKind.Role, "Water").IsSystem,
                "the engine bridge rejects edits to system entries independently of UI controls");
            foreach (var group in manager.Groups)
            {
                group.Entries.Single(e => !e.IsSystem).RemoveCommand.Execute(null);
                Publish();
            }
            loaded = SceneSerialization.LoadFromFile(scenePath, assets).BasicEntities.Single();
            prefabEntity = SceneSerialization.RestoreEntity(prefab.Record, SceneSerialization.CurrentVersion, assets);
            Check(entity.Tag == "Untagged" && entity.Layer == "Default" && entity.RoleId == "Default" &&
                  loaded.Tag == entity.Tag && loaded.Layer == entity.Layer && loaded.RoleId == entity.RoleId && prefabEntity.Tag == "Untagged",
                "deleting custom entries resets live, saved-scene and prefab assignments to category defaults");
            Check(vm.Tag == "Untagged" && vm.Layer == "Default" && vm.RoleName == "Default", "deleted entries leave valid Inspector selections");

            // Files cannot replace system entries or promote a custom entry to a system default.
            string customId = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { new ClassificationDefinition("Water", "Changed", ClassificationKind.Role),
                new ClassificationDefinition(customId, "Editable", ClassificationKind.Tag, true) }));
            var protectedCatalog = new ClassificationCatalog(path);
            Check(protectedCatalog.Find(ClassificationKind.Role, "Water") is { Name: "Water", IsSystem: true } &&
                  !protectedCatalog.Find(ClassificationKind.Tag, customId).IsSystem,
                "system protection is derived from engine definitions rather than editable JSON flags");

            string blocked = Path.Combine(root, "Blocked.json");
            Directory.CreateDirectory(blocked);
            var failing = new ClassificationCatalog(blocked);
            Check(!failing.Add(ClassificationKind.Tag, "Not saved", out var saveError) && saveError != null &&
                  failing.Entries.All(e => e.IsSystem), "a failed catalog write leaves the project definitions unchanged");

            void Publish()
            {
                for (int i = 0; i < 6; i++)
                    typeof(EditorBridge).GetMethod("DrainAndPublish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, null);
                Dispatcher.UIThread.RunJobs();
                manager.Refresh();
                BridgeReconciler.Apply(bridge.Snapshot, objects, bridge);
            }
        }
        finally
        {
            ClassificationCatalog.Project = previous;
            Prefab.SourceRootOverride = previousPrefabs;
            Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
