using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Components;
using Engine.Entities;
using Engine.Recources;
using Microsoft.Xna.Framework;
using BasicEntityRecord = Engine.Logic.SceneSerialization.BasicEntityRecord;

namespace Engine.Logic
{
    /// <summary>
    /// Prefab instances (Unity-style). An instance is a gameobject linked to a <see cref="Prefab"/> by
    /// <see cref="BasicEntity.PrefabPath"/>. Its name, position and rotation are always its own; every other value
    /// (model, scale, material, role, components and their settings) follows the prefab unless the instance
    /// overrides it. Overrides are the values that differ from the prefab: scenes save them
    /// (<see cref="SceneSerialization.BasicEntityRecord.PrefabOverrides"/>) and take the rest from the prefab file on
    /// load, so editing a prefab updates its instances everywhere.
    ///
    /// Override paths: a record property ("Scale", "Role", "ModelKey"), a property inside one ("Material/Roughness"),
    /// a whole component the instance added or removed ("Components/physics#0", type id + index among that type),
    /// or a setting of one ("Components/physics#0/Mass", "Components/script-behaviour#1/Fields/speed").
    ///
    /// The live operations (create, apply, revert, unpack, instantiate) run on the game thread; Anvil reaches them
    /// through <see cref="Editor.IEditorBridge"/>.
    /// </summary>
    public static class PrefabUtility
    {
        /// <summary>Override list of an instance saved while its prefab was missing: it keeps all its own values.</summary>
        public const string AllOverridden = "*";

        private const string ComponentsKey = "Components";

        // Always the instance's own (never taken from the prefab, never overrides).
        private static readonly HashSet<string> InstanceKeys = new(StringComparer.Ordinal)
        {
            nameof(BasicEntityRecord.Id), nameof(BasicEntityRecord.Name), nameof(BasicEntityRecord.Position),
            nameof(BasicEntityRecord.Rotation), nameof(BasicEntityRecord.Prefab), nameof(BasicEntityRecord.PrefabOverrides),
            nameof(BasicEntityRecord.Physics),
        };

        // ------------------------------------------------------------------
        //  Records (pure data, any thread)
        // ------------------------------------------------------------------

        /// <summary>The values of <paramref name="instance"/> that differ from <paramref name="prefab"/>.</summary>
        internal static IReadOnlyList<string> Overrides(Prefab prefab, BasicEntityRecord instance)
        {
            var result = new List<string>();
            // Compare in the prefab's ID space, so script fields pointing at the gameobject itself match.
            JsonObject a = ToNode(WithId(instance, prefab.Record.Id));
            JsonObject b = ToNode(prefab.Record);
            foreach (string key in Keys(b, a))
            {
                if (InstanceKeys.Contains(key)) continue;
                if (key == ComponentsKey) DiffComponents(a[key] as JsonArray, b[key] as JsonArray, result);
                else Diff(a[key], b[key], key, result);
            }
            return result;
        }

        /// <summary>
        /// The instance record as it is now: the prefab's values with the instance's own (name, transform,
        /// <paramref name="overrides"/>) on top. Component attachments keep the instance's IDs.
        /// </summary>
        internal static BasicEntityRecord Sync(Prefab prefab, BasicEntityRecord instance, IEnumerable<string> overrides)
        {
            var set = new HashSet<string>(overrides ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (set.Contains(AllOverridden)) return Clone(instance);

            JsonObject source = ToNode(WithId(instance, prefab.Record.Id));
            JsonObject result = ToNode(prefab.Record);
            foreach (string key in InstanceKeys) SetPath(result, source, new[] { key });

            var prefabComponents = Keyed(result[ComponentsKey] as JsonArray);
            var instanceComponents = Keyed(source[ComponentsKey] as JsonArray).ToDictionary(c => c.Key, c => c.Node);
            var components = new JsonArray();
            foreach (var (key, node) in prefabComponents)
            {
                instanceComponents.TryGetValue(key, out var own);
                if (set.Contains(ComponentPath(key)))
                {
                    // Removed on the instance (or replaced as a whole).
                    if (own != null) components.Add(own.DeepClone());
                    continue;
                }
                var component = node.DeepClone().AsObject();
                // Keep the attachment ID the scene and the editor know it by; new prefab components get their own.
                component[nameof(ComponentRecord.InstanceId)] = own != null
                    ? own[nameof(ComponentRecord.InstanceId)]?.DeepClone()
                    : JsonValue.Create(Guid.NewGuid().ToString());
                if (own != null)
                {
                    string prefix = ComponentPath(key) + "/";
                    foreach (string path in set.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)))
                        SetPath(component, own, new[] { nameof(ComponentRecord.Data) }.Concat(Split(path.Substring(prefix.Length))).ToArray());
                }
                components.Add(component);
            }
            // Components the instance added.
            var prefabKeys = prefabComponents.Select(c => c.Key).ToHashSet();
            foreach (var (key, own) in Keyed(source[ComponentsKey] as JsonArray))
                if (!prefabKeys.Contains(key) && set.Contains(ComponentPath(key))) components.Add(own.DeepClone());
            result[ComponentsKey] = components;

            foreach (string path in set)
                if (!path.StartsWith(ComponentsKey + "/", StringComparison.Ordinal) && !InstanceKeys.Contains(path))
                    SetPath(result, source, Split(path));

            var merged = result.Deserialize<BasicEntityRecord>(SceneSerialization.JsonOptions);
            merged.PrefabOverrides = instance.PrefabOverrides;
            return WithId(merged, instance.Id);
        }

        /// <summary>A short label for an override path: "Physics > Mass", "Material > Roughness", "Script Behaviour 2".</summary>
        public static string Describe(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path == AllOverridden) return "Everything (the prefab was missing when the scene was saved)";
            var parts = Split(path).ToList();
            if (parts[0] == ComponentsKey && parts.Count > 1)
            {
                string[] key = parts[1].Split('#');
                string name = ComponentRegistry.Find(key[0])?.DisplayName ?? key[0];
                if (key.Length > 1 && int.TryParse(key[1], out int n) && n > 0) name += " " + (n + 1);
                parts = parts.Skip(2).ToList();
                // A script field reads better without the dictionary's name.
                if (parts.Count > 1 && parts[0] == nameof(ScriptBehaviourComponent.Fields)) parts.RemoveAt(0);
                return parts.Count == 0 ? name + " (component added or removed)" : name + " > " + string.Join(" > ", parts.Select(Nicify));
            }
            return string.Join(" > ", parts.Select(Nicify));
        }

        private static string Nicify(string name) => name switch
        {
            nameof(BasicEntityRecord.ModelKey) => "Model",
            nameof(BasicEntityRecord.IsEnabled) => "Enabled",
            _ => Scripting.ScriptFields.Nicify(name),
        };

        // ------------------------------------------------------------------
        //  Live gameobjects (game thread)
        // ------------------------------------------------------------------

        /// <summary>
        /// What an instance overrides right now (<see cref="Describe"/> labels them); empty for a plain gameobject,
        /// null when its prefab file is missing.
        /// </summary>
        public static IReadOnlyList<string> GetOverrides(BasicEntity entity, Assets assets)
        {
            if (entity?.IsPrefabInstance != true || assets == null) return Array.Empty<string>();
            Prefab prefab = Prefab.Load(entity.PrefabPath);
            if (prefab == null) return null;
            BasicEntityRecord record = Capture(entity, assets);
            return record == null ? Array.Empty<string>() : Overrides(prefab, record);
        }

        /// <summary>
        /// Saves <paramref name="entity"/> as a new prefab in <paramref name="folder"/> (Content-relative, "Prefabs" when
        /// null), named after it, and makes it an instance of that prefab. Null with an <paramref name="error"/> on failure.
        /// </summary>
        public static Prefab CreatePrefab(MainSceneLogic scene, BasicEntity entity, string folder, out string error)
        {
            error = null;
            BasicEntityRecord record = entity == null ? null : Capture(entity, scene.Assets);
            if (record == null)
            {
                error = entity == null ? "No gameobject." : $"'{entity.Name}' uses a model that isn't registered, so it can't be saved.";
                return null;
            }
            try
            {
                string path = Prefab.UniquePath(folder, entity.Name);
                record.Name = Path.GetFileNameWithoutExtension(path);
                // Placed copies get their own position; rotation is the default for new ones (Instantiate without one).
                record.Position = Vector3.Zero;
                Prefab prefab = Prefab.Write(path, record);
                if (prefab == null) { error = $"'{path}' was written but can't be read back."; return null; }
                entity.PrefabPath = prefab.Path;
                MarkDirty(scene);
                return prefab;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Writes an instance's values into its prefab file (Apply All), then updates the other instances in the open
        /// scene, keeping their own overrides. Returns how many of them changed, or -1 with an <paramref name="error"/>.
        /// </summary>
        public static int ApplyToPrefab(MainSceneLogic scene, BasicEntity entity, out string error)
        {
            error = null;
            Prefab prefab = entity?.IsPrefabInstance == true ? Prefab.Load(entity.PrefabPath) : null;
            if (prefab == null)
            {
                error = entity?.IsPrefabInstance == true ? $"Prefab '{entity.PrefabPath}' not found." : "Not a prefab instance.";
                return -1;
            }
            BasicEntityRecord record = Capture(entity, scene.Assets);
            if (record == null) { error = $"'{entity.Name}' uses a model that isn't registered."; return -1; }

            // The other instances' overrides are measured against the prefab before the change.
            var others = scene.BasicEntities
                .Where(e => !ReferenceEquals(e, entity) && Prefab.SamePath(e.PrefabPath, prefab.Path))
                .Select(e => (Entity: e, Record: Capture(e, scene.Assets)))
                .Where(o => o.Record != null)
                .Select(o => (o.Entity, o.Record, Overrides: Overrides(prefab, o.Record)))
                .ToList();

            BasicEntityRecord next = WithId(record, prefab.Record.Id);
            next.Name = prefab.Name;
            next.Position = Vector3.Zero;
            next.Rotation = prefab.Record.Rotation;
            Prefab written;
            try { written = Prefab.Write(prefab.Path, next); }
            catch (Exception ex) { error = ex.Message; return -1; }
            if (written == null) { error = $"'{prefab.Path}' was written but can't be read back."; return -1; }

            int changed = 0;
            foreach (var (other, own, overrides) in others)
            {
                BasicEntityRecord merged = Sync(written, own, overrides);
                if (SameValues(merged, own)) continue;
                if (scene.ReplaceGameObject(other, merged) != null) changed++;
            }
            if (changed > 0) MarkDirty(scene);
            return changed;
        }

        /// <summary>Drops every override of an instance (Revert All). Returns the rebuilt gameobject, or null.</summary>
        public static BasicEntity RevertToPrefab(MainSceneLogic scene, BasicEntity entity, out string error)
        {
            error = null;
            Prefab prefab = entity?.IsPrefabInstance == true ? Prefab.Load(entity.PrefabPath) : null;
            if (prefab == null)
            {
                error = entity?.IsPrefabInstance == true ? $"Prefab '{entity.PrefabPath}' not found." : "Not a prefab instance.";
                return null;
            }
            BasicEntityRecord record = Capture(entity, scene.Assets);
            if (record == null) { error = $"'{entity.Name}' uses a model that isn't registered."; return null; }
            BasicEntityRecord reverted = Sync(prefab, record, null);
            if (SameValues(reverted, record)) return entity;
            BasicEntity fresh = scene.ReplaceGameObject(entity, reverted);
            if (fresh != null) MarkDirty(scene);
            else error = $"Prefab '{prefab.Path}' uses an unknown model '{prefab.ModelKey}'.";
            return fresh;
        }

        /// <summary>Unlinks an instance from its prefab: it becomes a plain gameobject with its current values.</summary>
        public static bool Unpack(MainSceneLogic scene, BasicEntity entity)
        {
            if (entity?.IsPrefabInstance != true) return false;
            entity.PrefabPath = null;
            MarkDirty(scene);
            return true;
        }

        // The record a live instance saves as, with its prefab link (rebuilt instances must stay linked).
        private static BasicEntityRecord Capture(BasicEntity entity, Assets assets)
        {
            BasicEntityRecord record = SceneSerialization.CaptureEntity(entity, assets);
            if (record != null) record.Prefab = entity.PrefabPath;
            return record;
        }

        private static void MarkDirty(MainSceneLogic scene)
        {
            if (scene?.ActiveScene != null) scene.ActiveScene.IsDirty = true;
        }

        // ------------------------------------------------------------------
        //  JSON helpers
        // ------------------------------------------------------------------

        internal static BasicEntityRecord Clone(BasicEntityRecord record) =>
            ToNode(record).Deserialize<BasicEntityRecord>(SceneSerialization.JsonOptions);

        private static JsonObject ToNode(BasicEntityRecord record) =>
            JsonSerializer.SerializeToNode(record, SceneSerialization.JsonOptions).AsObject();

        private static bool SameValues(BasicEntityRecord a, BasicEntityRecord b) =>
            ToNode(a).ToJsonString() == ToNode(b).ToJsonString();

        /// <summary>
        /// A copy of <paramref name="record"/> with ID <paramref name="id"/>; script fields that pointed at the gameobject
        /// itself point at the new ID (like <see cref="BasicEntity.Clone"/>).
        /// </summary>
        private static BasicEntityRecord WithId(BasicEntityRecord record, int id)
        {
            BasicEntityRecord copy = Clone(record);
            int from = record.Id;
            copy.Id = id;
            if (from == id || from <= 0 || id <= 0 || copy.Components == null) return copy;
            for (int i = 0; i < copy.Components.Count; i++)
            {
                if (copy.Components[i].Type != ScriptBehaviourComponent.TypeId) continue;
                if (ComponentRegistry.Restore(copy.Components[i]) is not ScriptBehaviourComponent script) continue;
                script.RemapGameObjectReferences(from, id);
                copy.Components[i] = ComponentRegistry.Capture(script);
            }
            return copy;
        }

        private static IEnumerable<string> Keys(JsonObject first, JsonObject second)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in first) if (seen.Add(kv.Key)) yield return kv.Key;
            foreach (var kv in second) if (seen.Add(kv.Key)) yield return kv.Key;
        }

        // Objects are compared property by property; anything else (numbers, strings, [x,y,z] vectors) as a whole.
        private static void Diff(JsonNode a, JsonNode b, string path, List<string> result)
        {
            if (a is JsonObject ao && b is JsonObject bo)
            {
                foreach (string key in Keys(bo, ao)) Diff(ao[key], bo[key], path + "/" + key, result);
                return;
            }
            if ((a?.ToJsonString() ?? "null") != (b?.ToJsonString() ?? "null")) result.Add(path);
        }

        private static void DiffComponents(JsonArray instance, JsonArray prefab, List<string> result)
        {
            var own = Keyed(instance).ToDictionary(c => c.Key, c => c.Node);
            var shared = Keyed(prefab).ToDictionary(c => c.Key, c => c.Node);
            foreach (string key in shared.Keys.Concat(own.Keys).Distinct())
            {
                if (!own.TryGetValue(key, out var a) || !shared.TryGetValue(key, out var b))
                {
                    result.Add(ComponentPath(key));
                    continue;
                }
                const string data = nameof(ComponentRecord.Data);
                if (a[data] is JsonObject ad && b[data] is JsonObject bd)
                {
                    // Read-only properties (ClampedMass, ...) are derived from the settings; they're never overrides.
                    var settings = SettableProperties(key.Split('#')[0]);
                    foreach (string k in Keys(bd, ad))
                        if (settings == null || settings.Contains(k)) Diff(ad[k], bd[k], ComponentPath(key) + "/" + k, result);
                }
                else Diff(a[data], b[data], ComponentPath(key), result);
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> Settable = new();

        // Names of a component type's public settable properties (what the Inspector edits); null for an unknown type.
        private static HashSet<string> SettableProperties(string typeId)
        {
            Type type = ComponentRegistry.Find(typeId)?.ComponentType;
            if (type == null) return null;
            return Settable.GetOrAdd(typeId, _ => type
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal));
        }

        // Components matched across instance and prefab by type id + index among that type ("script-behaviour#1").
        private static List<(string Key, JsonObject Node)> Keyed(JsonArray components)
        {
            var list = new List<(string, JsonObject)>();
            if (components == null) return list;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in components)
            {
                if (item is not JsonObject node) continue;
                string type = node[nameof(ComponentRecord.Type)]?.GetValue<string>() ?? "";
                counts.TryGetValue(type, out int n);
                counts[type] = n + 1;
                list.Add(($"{type}#{n}", node));
            }
            return list;
        }

        private static string ComponentPath(string key) => ComponentsKey + "/" + key;

        private static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Copies the value at path from source into target (removing it when source has none), creating parents.
        private static void SetPath(JsonObject target, JsonObject source, string[] path)
        {
            JsonNode value = source;
            foreach (string part in path)
            {
                value = (value as JsonObject)?[part];
                if (value == null) break;
            }
            JsonObject parent = target;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (parent[path[i]] is not JsonObject next)
                {
                    if (value == null) return;
                    next = new JsonObject();
                    parent[path[i]] = next;
                }
                parent = next;
            }
            if (value == null) parent.Remove(path[^1]);
            else parent[path[^1]] = value.DeepClone();
        }
    }
}
