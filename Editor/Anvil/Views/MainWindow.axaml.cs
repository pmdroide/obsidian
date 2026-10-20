using System;
using System.IO;
using System.Linq;
using Anvil.Controls;
using Anvil.Models;
using Anvil.Services;
using Anvil.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Engine.Components;

namespace Anvil.Views;

public partial class MainWindow : Window
{
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && DataContext is MainWindowViewModel vm) vm.Mcp.BeginShutdown();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { if (DataContext is MainWindowViewModel vm) await vm.Mcp.ShutdownAsync(); }
        catch (Exception ex) { Engine.Editor.EditorBridge.Log("MCP shutdown: " + ex.GetType().Name); }
        finally { if (DataContext is MainWindowViewModel vm) vm.StopConsoleLogging(); }
    }

    private async void CopyMcpToken_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.Mcp.Token);
    }

    private void WorkspaceTabAdd_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainWindowViewModel vm) return;
        var menu = new MenuFlyout();
        foreach (var panel in vm.AvailableWorkspacePanels)
            menu.Items.Add(new MenuItem
            {
                Header = panel.Title,
                Command = vm.OpenWorkspaceTabCommand,
                CommandParameter = panel.Kind,
            });
        menu.ShowAt(button);
    }

    private async void ChooseSkybox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.Environment.CanChooseSkybox) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a skybox panorama",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Skybox panorama")
                { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.Environment.ImportSkybox(path);
    }

    public MainWindow()
    {
        InitializeComponent();

        // The MonoGameHost spins up the engine on its own thread and raises
        // BridgeReady on the UI thread once the bridge is constructed. Forward
        // that bridge into the view model so it can start reconciling snapshots.
        var host = this.FindControl<MonoGameHost>("ViewportHost");
        if (host != null)
        {
            host.BridgeReady += bridge =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.AttachBridge(bridge);
            };
        }

        // Asset → scene gestures. The TreeViewItem marks PointerPressed/DoubleTapped as
        // handled (for selection/expand), so an XAML-attribute handler — which ignores
        // handled events — never fires and the drag never starts. Register here with
        // handledEventsToo:true so our handlers run regardless. DoubleTapped is the
        // reliable "add to scene" path; drag-into-Hierarchy is the richer one.
        var assetsTree = this.FindControl<TreeView>("AssetsTreeView");
        if (assetsTree != null)
        {
            assetsTree.AddHandler(InputElement.PointerPressedEvent, AssetsTree_PointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
            assetsTree.AddHandler(InputElement.DoubleTappedEvent, AssetsTree_DoubleTapped,
                RoutingStrategies.Bubble, handledEventsToo: true);
            assetsTree.AddHandler(InputElement.KeyDownEvent, AssetsTree_KeyDown,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }

        // Run before TreeView selection: dragging a reference must keep its Inspector target visible.
        var hierarchyTree = this.FindControl<TreeView>("HierarchyTreeView");
        hierarchyTree?.AddHandler(InputElement.PointerPressedEvent, HierarchyTree_PointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);

        // Inspector focus tracking. When the user is typing in a NumericUpDown,
        // dragging a Slider, or interacting with a ColorPicker inside the inspector,
        // the reconciler must NOT push fresh engine values into the bound VM —
        // doing so steals focus from the control mid-edit. We listen for focus
        // changes window-wide and flag the reconciler whenever the focused
        // element is a descendant of the inspector ScrollViewer.
        AddHandler(InputElement.GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
        AddHandler(InputElement.LostFocusEvent, OnAnyLostFocus, RoutingStrategies.Bubble);
    }

    private void OnAnyGotFocus(object? sender, RoutedEventArgs e)
    {
        var inspector = this.FindControl<ScrollViewer>("InspectorScroll");
        BridgeReconciler.IsInspectorFocused =
            inspector != null && e.Source is Visual v && IsDescendantOf(v, inspector);
    }

    private void OnAnyLostFocus(object? sender, RoutedEventArgs e)
    {
        // LostFocus doesn't tell us where focus went, but the focused element on
        // the window does. If focus has truly left the inspector (window-level
        // focused element is no longer an inspector descendant), clear the flag
        // so the reconciler resumes writing values for the selected object.
        // Without this, leaving the inspector for the hierarchy or viewport
        // would leave the selected VM permanently frozen.
        var inspector = this.FindControl<ScrollViewer>("InspectorScroll");
        if (inspector == null) { BridgeReconciler.IsInspectorFocused = false; return; }

        // Defer to next dispatcher tick — the next GotFocus has not run yet when
        // LostFocus fires; reading FocusManager too early would still see the
        // outgoing element.
        Dispatcher.UIThread.Post(() =>
        {
            var focused = FocusManager?.GetFocusedElement() as Visual;
            BridgeReconciler.IsInspectorFocused =
                focused != null && IsDescendantOf(focused, inspector);
        });
    }

    private static bool IsDescendantOf(Visual node, Visual ancestor)
    {
        Visual? current = node;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current.GetVisualParent();
        }
        return false;
    }

    // ---- Asset import drag/drop ----

    // Avalonia 12 uses DataTransfer/DataFormat (the old DataObject/DataFormats are
    // deprecated). An "in-process" format carries arbitrary CLR values inside the
    // same Avalonia process — exactly what we need to ship a model key from the
    // Assets panel to the Hierarchy TreeView.
    private static readonly DataFormat<string> ModelKeyFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/modelKey");
    private static readonly DataFormat<string> AudioAssetFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/audioAsset");
    private static readonly DataFormat<string> TextureAssetFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/textureAsset");
    // Content-relative .prefab path (Assets -> Hierarchy places an instance).
    private static readonly DataFormat<string> PrefabAssetFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/prefabAsset");
    // Content-relative .mat path (Assets -> a Hierarchy row or the Material component assigns it).
    private static readonly DataFormat<string> MaterialAssetFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/materialAsset");

    private static readonly DataFormat<string> ScriptAssetFormat =
        DataFormat.CreateInProcessFormat<string>("obsidian/scriptAsset");
    // A Hierarchy gameobject's view model (reference assignment, or drop into Assets to create a prefab).
    private static readonly DataFormat<SceneObjectViewModel> GameObjectFormat =
        DataFormat.CreateInProcessFormat<SceneObjectViewModel>("obsidian/gameObject");

    private void MaterialTexture_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = MaterialComponent.IsTextureAsset(e.DataTransfer.TryGetValue(TextureAssetFormat))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MaterialTexture_Drop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetValue(TextureAssetFormat);
        if ((sender as Control)?.DataContext is MaterialTextureSlotViewModel slot &&
            MaterialComponent.IsTextureAsset(path)) slot.Assign(path!);
        e.Handled = true;
    }

    // ---- Material assets (Engine.Recources.MaterialAsset) ----

    private void MaterialAsset_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(MaterialAssetFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Drop on the Material component's asset slot.
    private void MaterialAsset_Drop(object? sender, DragEventArgs e)
    {
        if ((sender as Control)?.DataContext is MaterialInfo material &&
            e.DataTransfer.TryGetValue(MaterialAssetFormat) is { Length: > 0 } path) material.AssignAsset(path);
        e.Handled = true;
    }

    // The Material component's asset slot: a menu of every material in Content, built on each click.
    private void MaterialAssetPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MaterialInfo material } button) return;
        var menu = new MenuFlyout();
        var files = Engine.Recources.MaterialAsset.List();
        foreach (string path in files)
        {
            string file = path;
            var item = new MenuItem { Header = file };
            item.Click += (_, _) => material.AssignAsset(file);
            menu.Items.Add(item);
        }
        if (files.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No materials yet: Assets > right-click > Create Material", IsEnabled = false });
        menu.ShowAt(button);
    }

    // Opens the linked asset in the Inspector.
    private void MaterialAssetEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && (sender as Control)?.DataContext is MaterialInfo { IsLinked: true } material)
            vm.OpenMaterialAsset(material.MaterialPath);
    }

    // Assets context menu > Create Material (in the folder under the cursor).
    private void AssetCreateMaterial_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.CreateMaterialAsset((sender as Control)?.DataContext as AssetNode);
    }

    // Assets context menu > Assign to Selection (a material).
    private void AssetAssignMaterial_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && (sender as Control)?.DataContext is AssetNode { Kind: AssetKind.Material } node)
            vm.AssignMaterialAsset(null, node.RelativePath);
    }

    private async void MaterialTexture_Browse(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MaterialTextureSlotViewModel slot) return;
        if (await PickContentTextureAsync($"Select {slot.Name} texture from Content") is { } relative) slot.Assign(relative);
    }

    private void DecalTexture_Drop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetValue(TextureAssetFormat);
        if ((sender as Control)?.DataContext is SceneObjectViewModel decal &&
            MaterialComponent.IsTextureAsset(path)) decal.AssignDecalTexture(path!);
        e.Handled = true;
    }

    private async void DecalTexture_Browse(object? sender, RoutedEventArgs e)
    {
        // Captured before the picker opens, so a selection change meanwhile can't redirect the texture.
        if ((sender as Control)?.DataContext is not SceneObjectViewModel decal) return;
        if (await PickContentTextureAsync("Select decal texture from Content") is { } relative) decal.AssignDecalTexture(relative);
    }

    /// <summary>A texture file inside the engine's source Content folder, as a Content-relative path; null when cancelled.</summary>
    private async System.Threading.Tasks.Task<string?> PickContentTextureAsync(string title)
    {
        // The material asset Inspector has no selected gameobject: take the Content folder from the window.
        if (DataContext is not MainWindowViewModel { ContentSourceRoot: { Length: > 0 } root }) return null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(root),
            FileTypeFilter = new[] { new FilePickerFileType("Textures")
                { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.dds", "*.bmp" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } file) return null;
        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            Engine.Editor.EditorBridge.Log("Choose a texture inside Content. Import external textures into Assets first.");
            return null;
        }
        return relative;
    }

    // Flyout content joins the tree only when shown. Pin it to the control that opened it so a
    // picker left open keeps editing that component, never whatever is selected afterwards.
    private void OwnedFlyout_Opening(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target: { } target, Content: Control content })
            content.DataContext = target.DataContext;
    }

    // Add Component builds a new menu on every click from the gameobject the inspector shows.
    // A reused MenuFlyout kept its first MenuItems bound to the first gameobject's commands,
    // so every component landed on that object.
    private void AddComponentButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SceneObjectViewModel owner } button) return;
        var menu = new MenuFlyout();
        foreach (var type in owner.AddableComponents)
            menu.Items.Add(new MenuItem { Header = type.DisplayName, Command = type.AddCommand });
        menu.ShowAt(button);
    }

    private void AudioClip_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = AudioComponent.IsAudioAsset(e.DataTransfer.TryGetValue(AudioAssetFormat))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AudioClip_Drop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetValue(AudioAssetFormat);
        if ((sender as Control)?.DataContext is AudioComponentViewModel audio &&
            AudioComponent.IsAudioAsset(path)) audio.AssignClip(path!);
        e.Handled = true;
    }

    private void PrefabField_DragOver(object? sender, DragEventArgs e)
    {
        ReferenceDragOver(sender, e, e.DataTransfer.Contains(PrefabAssetFormat));
    }

    private void PrefabField_Drop(object? sender, DragEventArgs e)
    {
        (sender as ReferenceSlot)?.SetDropTarget(false);
        if ((sender as Control)?.DataContext is ScriptPrefabFieldViewModel field &&
            e.DataTransfer.TryGetValue(PrefabAssetFormat) is { Length: > 0 } path) field.Assign(path);
        e.Handled = true;
    }

    private static void ReferenceDragOver(object? sender, DragEventArgs e, bool accepts)
    {
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        (sender as ReferenceSlot)?.SetDropTarget(accepts);
        e.Handled = true;
    }

    private void Reference_DragLeave(object? sender, DragEventArgs e) =>
        (sender as ReferenceSlot)?.SetDropTarget(false);

    private SceneObjectViewModel? DroppedGameObject(object? sender, DragEventArgs e)
    {
        var obj = e.DataTransfer.TryGetValue(GameObjectFormat);
        if (obj is not { Kind: Engine.Editor.EditorObjectKind.BasicEntity, EngineId: not null } ||
            DataContext is not MainWindowViewModel vm || !vm.SceneObjects.Contains(obj)) return null;
        if ((sender as Control)?.DataContext is ParticleSubEmitterRowViewModel &&
            !obj.Components.Any(c => c.TypeId == ParticleSystemComponent.TypeId)) return null;
        return obj;
    }

    private void GameObjectReference_DragOver(object? sender, DragEventArgs e) =>
        ReferenceDragOver(sender, e, DroppedGameObject(sender, e) != null);

    private void GameObjectReference_Drop(object? sender, DragEventArgs e)
    {
        (sender as ReferenceSlot)?.SetDropTarget(false);
        if (DroppedGameObject(sender, e) is { } obj)
        {
            switch ((sender as Control)?.DataContext)
            {
                case ScriptGameObjectFieldViewModel field:
                    field.Assign(obj.EngineId!.Value);
                    break;
                case NpcComponentViewModel npc when (sender as Control)?.Tag as string == "PatrolPoints":
                    npc.PatrolPoints = string.IsNullOrWhiteSpace(npc.PatrolPoints) ? obj.Name : npc.PatrolPoints + ", " + obj.Name;
                    break;
                case NpcComponentViewModel npc:
                    npc.Target = obj.Name;
                    break;
                case ParticleSubEmitterRowViewModel emitter:
                    emitter.Target = obj.Name;
                    break;
            }
        }
        e.Handled = true;
    }

    private void ScriptAsset_DragOver(object? sender, DragEventArgs e) =>
        ReferenceDragOver(sender, e, ScriptBehaviourComponentViewModel.ScriptFromAsset(e.DataTransfer.TryGetValue(ScriptAssetFormat)) != null);

    private void ScriptAsset_Drop(object? sender, DragEventArgs e)
    {
        (sender as ReferenceSlot)?.SetDropTarget(false);
        if ((sender as Control)?.DataContext is ScriptBehaviourComponentViewModel script &&
            e.DataTransfer.TryGetValue(ScriptAssetFormat) is { } path) script.AssignAsset(path);
        e.Handled = true;
    }

    private void LodModel_DragOver(object? sender, DragEventArgs e) =>
        ReferenceDragOver(sender, e, e.DataTransfer.Contains(ModelKeyFormat));

    private void LodModel_Drop(object? sender, DragEventArgs e)
    {
        (sender as ReferenceSlot)?.SetDropTarget(false);
        if ((sender as Control)?.DataContext is LodLevelViewModel level &&
            e.DataTransfer.TryGetValue(ModelKeyFormat) is { Length: > 0 } model) level.Model = model;
        e.Handled = true;
    }

    private void Reference_Clear(object? sender, RoutedEventArgs e)
    {
        switch ((sender as Control)?.DataContext)
        {
            case ScriptGameObjectFieldViewModel field: field.Clear(); break;
            case ScriptPrefabFieldViewModel field: field.Clear(); break;
            case ScriptBehaviourComponentViewModel script: script.Clear(); break;
            case LodLevelViewModel level: level.Clear(); break;
            case NpcComponentViewModel npc when (sender as Control)?.Tag as string == "PatrolPoints": npc.PatrolPoints = ""; break;
            case NpcComponentViewModel npc: npc.Target = ""; break;
            case ParticleSubEmitterRowViewModel emitter: emitter.Target = ""; break;
        }
        e.Handled = true;
    }

    // Assets context menu > Place in Scene (a prefab).
    private void AssetPlacePrefab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && (sender as Control)?.DataContext is AssetNode { Kind: AssetKind.Prefab } node)
            vm.InstantiatePrefabAsset(node.RelativePath);
    }

    private void AssetsPanel_DragOver(object? sender, DragEventArgs e)
    {
        // Accept OS file drops (Windows Explorer etc.) and Hierarchy gameobjects (new prefab); reject anything else.
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(GameObjectFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".tga", ".dds", ".bmp" };

    private void AssetsPanel_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.DataTransfer.TryGetValue(GameObjectFormat) is { } gameObject)
        {
            // Saved into the folder under the cursor (a file's folder), or Prefabs when dropped on empty space.
            var target = e.Source is Visual tv ? FindDataContext<AssetNode>(tv) : null;
            vm.CreatePrefabIn(gameObject, MainWindowViewModel.FolderFor(target));
            e.Handled = true;
            return;
        }
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        // The node under the cursor decides where things go: an imported model's folder
        // binds dropped textures to it; any other folder (or a file's folder) receives
        // plain copies. Empty space means the Content root.
        var node = e.Source is Visual v ? FindDataContext<AssetNode>(v) : null;
        string? modelKey = vm.DeletableModelKeyFor(node);

        var images = new System.Collections.Generic.List<string>();
        var copies = new System.Collections.Generic.List<string>();
        foreach (var item in files)
        {
            string? path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (File.Exists(path) && (ext == ".fbx" || ext == ".obj"))
                vm.ImportFbxFromDisk(path); // importer places it under GameObjects/Models/{key}
            else if (modelKey != null && File.Exists(path) && Array.IndexOf(ImageExtensions, ext) >= 0)
                images.Add(path);
            else
                copies.Add(path);
        }

        if (images.Count > 0) vm.ImportTexturesFromDisk(modelKey!, images);
        if (copies.Count > 0) vm.CopyFilesIntoContent(node, copies);
        e.Handled = true;
    }

    // Delete from the Assets panel (context-menu "Delete"). Confirms first because this
    // removes files from disk and their Content.mgcb entries.
    private async void AssetDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        await DeleteAssetAsync(vm, (sender as Control)?.DataContext as AssetNode);
    }

    private async void AssetsTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (DataContext is not MainWindowViewModel vm) return;
        var assetsTree = this.FindControl<TreeView>("AssetsTreeView");
        if (assetsTree?.SelectedItem is not AssetNode node) return;
        e.Handled = true;
        await DeleteAssetAsync(vm, node);
    }

    private async System.Threading.Tasks.Task DeleteAssetAsync(MainWindowViewModel vm, AssetNode? node)
    {
        if (node == null) return;

        // An imported model (its mesh or its GameObjects/Models/{key} folder) is removed
        // as a whole so the engine unregisters it too.
        string? modelKey = vm.ModelKeyToDeleteFor(node);
        if (!string.IsNullOrEmpty(modelKey))
        {
            bool okModel = await ConfirmAsync($"Delete model \"{modelKey}\" and all its imported files from disk?\nThis cannot be undone.");
            if (okModel) vm.DeleteModelAsset(modelKey);
            return;
        }

        string what = node.Kind == AssetKind.Folder ? "folder" : "file";
        string message = $"Move {what} \"{node.RelativePath}\" to the Recycle Bin?\nIts Content.mgcb entries will be removed.";
        if (!string.IsNullOrEmpty(node.ModelKey))
            message += $"\n\nThe engine loads this as model \"{node.ModelKey}\" — removing it can break engine startup.";
        if (await ConfirmAsync(message)) vm.DeleteAssetFromDisk(node);
    }

    // Minimal modal yes/no dialog (Avalonia has no built-in MessageBox).
    private async System.Threading.Tasks.Task<bool> ConfirmAsync(string message)
    {
        var result = false;
        var dialog = new Window
        {
            Title = "Confirm delete",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        cancel.Click += (_, _) => dialog.Close();
        var delete = new Button { Content = "Delete", MinWidth = 80, IsDefault = true };
        delete.Click += (_, _) => { result = true; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, delete },
                },
            },
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async void AssetsTree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Avalonia 12's DragDrop.DoDragDropAsync requires the original
        // PointerPressedEventArgs (the new API dropped the pre-12 move-threshold
        // helper), so we kick off the drag immediately on a left-click that
        // lands on a mesh, texture, or playable audio AssetNode. Other clicks fall through to the TreeView's
        // normal selection behaviour.
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        if (e.Source is not Visual v) return;
        var node = FindDataContext<AssetNode>(v);
        var dt = new DataTransfer();
        if (node?.Kind == AssetKind.Audio && AudioComponent.IsAudioAsset(node.RelativePath))
            dt.Add(DataTransferItem.Create(AudioAssetFormat, node.RelativePath));
        else if (node?.Kind == AssetKind.Texture && MaterialComponent.IsTextureAsset(node.RelativePath))
            dt.Add(DataTransferItem.Create(TextureAssetFormat, node.RelativePath));
        else if (node?.Kind == AssetKind.Prefab)
            dt.Add(DataTransferItem.Create(PrefabAssetFormat, node.RelativePath));
        else if (node?.Kind == AssetKind.Script && ScriptBehaviourComponentViewModel.ScriptFromAsset(node.RelativePath) != null)
            dt.Add(DataTransferItem.Create(ScriptAssetFormat, node.RelativePath));
        else if (node?.Kind == AssetKind.Material)
        {
            // A click shows the material in the Inspector; dragging it onto a gameobject assigns it.
            (DataContext as MainWindowViewModel)?.OpenMaterialAsset(node.RelativePath);
            dt.Add(DataTransferItem.Create(MaterialAssetFormat, node.RelativePath));
        }
        else if (ModelKeyFromVisual(v) is { } modelKey)
            dt.Add(DataTransferItem.Create(ModelKeyFormat, modelKey));
        else return;
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SetAssetDragActive(true);
        try
        {
            await DragDrop.DoDragDropAsync(e, dt, DragDropEffects.Copy);
        }
        finally
        {
            // Cancel, drop, and failures must all release the viewport input guard.
            vm.SetAssetDragActive(false);
        }
    }

    private void AssetsTree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Reliable, drag-free path to add a mesh to the scene: double-click it.
        // Scenes open in the viewport; text assets (shaders, UI XML/CSS) open in an external editor.
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Source is not Visual v) return;
        string? modelKey = ModelKeyFromVisual(v);
        if (!string.IsNullOrEmpty(modelKey))
        {
            vm.AddEntityFromAsset(modelKey);
            e.Handled = true;
            return;
        }
        var node = FindDataContext<AssetNode>(v);
        if (node?.Kind == AssetKind.Prefab)
        {
            vm.InstantiatePrefabAsset(node.RelativePath);
            e.Handled = true;
            return;
        }
        if (node?.Kind == AssetKind.Material)
        {
            vm.AssignMaterialAsset(null, node.RelativePath);
            e.Handled = true;
            return;
        }
        if (vm.OpenSceneAsset(node) || vm.OpenInTextEditor(node))
            e.Handled = true;
    }

    private async void HierarchyTree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Delay selection until a click finishes, keeping the destination Inspector visible during a drag.
        // The row's lock/visibility buttons and expansion toggles keep their clicks.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual v || IsInside<Button>(v) || IsInside<Avalonia.Controls.Primitives.ToggleButton>(v)) return;
        if (FindDataContext<SceneObjectViewModel>(v) is not { Kind: Engine.Editor.EditorObjectKind.BasicEntity, EngineId: not null } obj) return;
        if (DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        var dt = new DataTransfer();
        dt.Add(DataTransferItem.Create(GameObjectFormat, obj));
        vm.SetAssetDragActive(true);
        try
        {
            var effect = await DragDrop.DoDragDropAsync(e, dt, DragDropEffects.Copy);
            if (effect == DragDropEffects.None && vm.SceneObjects.Contains(obj)) vm.SelectedSceneObject = obj;
        }
        finally
        {
            vm.SetAssetDragActive(false);
        }
    }

    private static bool IsInside<T>(Visual start) where T : Visual
    {
        for (Visual? cur = start; cur != null; cur = cur.GetVisualParent())
            if (cur is T) return true;
        return false;
    }

    // Resolves the model key from a mesh AssetNode under the given visual. Only mesh files
    // that a registered model was loaded from carry a ModelKey
    // (see MainWindowViewModel.RefreshAssetTree).
    private static string? ModelKeyFromVisual(Visual v)
    {
        var node = FindDataContext<AssetNode>(v);
        if (node is null || node.Kind != AssetKind.Mesh) return null;
        return string.IsNullOrEmpty(node.ModelKey) ? null : node.ModelKey;
    }

    private void Hierarchy_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ModelKeyFormat) || e.DataTransfer.Contains(PrefabAssetFormat) ||
                        e.DataTransfer.Contains(MaterialAssetFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Hierarchy_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        string? modelKey = e.DataTransfer.TryGetValue(ModelKeyFormat);
        if (!string.IsNullOrEmpty(modelKey))
        {
            vm.AddEntityFromAsset(modelKey);
            e.Handled = true;
            return;
        }
        string? prefab = e.DataTransfer.TryGetValue(PrefabAssetFormat);
        if (!string.IsNullOrEmpty(prefab))
        {
            vm.InstantiatePrefabAsset(prefab);
            e.Handled = true;
            return;
        }
        string? material = e.DataTransfer.TryGetValue(MaterialAssetFormat);
        if (!string.IsNullOrEmpty(material))
        {
            // The gameobject row under the cursor, else the selection.
            var target = e.Source is Visual v ? FindDataContext<SceneObjectViewModel>(v) : null;
            vm.AssignMaterialAsset(target, material);
            e.Handled = true;
        }
    }

    private static T? FindDataContext<T>(Visual start) where T : class
    {
        Visual? cur = start;
        while (cur != null)
        {
            if (cur is StyledElement se && se.DataContext is T t) return t;
            cur = cur.GetVisualParent();
        }
        return null;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 1. Only respond to standard left clicks
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // 2. Walk up the visual tree from the clicked element (e.Source)
            // to see if the user clicked inside the Menu structure
            var visual = e.Source as Visual;
            while (visual != null && visual != sender)
            {
                if (visual is Menu || visual is MenuItem)
                {
                    // Click is on the menu; abort dragging and let the menu work normally
                    return; 
                }
                visual = visual.GetVisualParent();
            }

            // 3. If the click was on empty titlebar space, native dragging begins
            this.BeginMoveDrag(e);
        }
    }
}
