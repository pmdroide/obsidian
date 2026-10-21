using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Engine.Recources
{
    /// <summary>
    /// A texgen output folder (the cache entry <c>Tools/TexGen/cache/&lt;id&gt;</c>): the PNG maps and <c>texgen.json</c>
    /// (schema 1, PLAN.md 2.3). <see cref="TryRead"/> only accepts a complete set: five maps (Metallic is left out when
    /// the material has no metal) and a readable description.
    /// </summary>
    public sealed class GeneratedMaterialSet
    {
        public static readonly string[] Suffixes = { "BaseColor", "Normal", "Roughness", "Metallic", "AO", "Height" };

        public string Directory { get; private set; }
        public string Id { get; private set; }
        public string Slug { get; private set; }
        /// <summary>Absolute map files by suffix (BaseColor, Normal, Roughness, Metallic, AO, Height); Metallic may be absent.</summary>
        public Dictionary<string, string> Maps { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Prompt { get; private set; }
        public string Family { get; private set; }
        public string NormalConvention { get; private set; }
        public double Tiling { get; private set; } = 1.0;
        public string MaterialType { get; private set; } = "Basic";
        public double SeamScore { get; private set; }
        public List<TexGenEvent> Warnings { get; } = new List<TexGenEvent>();

        public bool HasMetallic => Maps.ContainsKey("Metallic");

        public static GeneratedMaterialSet TryRead(string directory, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
            {
                error = "the output folder doesn't exist";
                return null;
            }
            string json = Path.Combine(directory, "texgen.json");
            if (!File.Exists(json)) { error = "texgen.json is missing"; return null; }

            var set = new GeneratedMaterialSet { Directory = Path.GetFullPath(directory) };
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                var root = doc.RootElement;
                if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1)
                {
                    error = "texgen.json has an unknown schema";
                    return null;
                }
                set.Id = Str(root, "id");
                if (string.IsNullOrEmpty(set.Id)) { error = "texgen.json has no id"; return null; }
                set.NormalConvention = Str(root, "normal_convention") ?? "directx";
                if (root.TryGetProperty("seam_score", out var seam) && seam.ValueKind == JsonValueKind.Number) set.SeamScore = seam.GetDouble();
                if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object) set.Prompt = Str(p, "prompt");
                if (root.TryGetProperty("suggested", out var s) && s.ValueKind == JsonValueKind.Object)
                {
                    if (s.TryGetProperty("tiling", out var t) && t.ValueKind == JsonValueKind.Number && t.GetDouble() > 0) set.Tiling = t.GetDouble();
                    set.MaterialType = Str(s, "material_type") ?? "Basic";
                    set.Family = Str(s, "family");
                }
                if (root.TryGetProperty("validation", out var v) && v.ValueKind == JsonValueKind.Object &&
                    v.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in w.EnumerateArray())
                        set.Warnings.Add(new TexGenEvent { Kind = TexGenEventKind.Warning, Code = Str(item, "code"), Message = Str(item, "message") });
                }
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException)
            {
                error = "texgen.json is damaged: " + e.Message;
                return null;
            }

            string baseColor = System.IO.Directory.EnumerateFiles(directory, "*_BaseColor.png").FirstOrDefault();
            if (baseColor == null) { error = "the base colour map is missing"; return null; }
            string fileName = Path.GetFileName(baseColor);
            set.Slug = fileName.Substring(0, fileName.Length - "_BaseColor.png".Length);
            foreach (string suffix in Suffixes)
            {
                string file = Path.Combine(directory, set.Slug + "_" + suffix + ".png");
                if (File.Exists(file)) set.Maps[suffix] = Path.GetFullPath(file);
                else if (suffix != "Metallic") { error = $"the {suffix} map is missing"; return null; }
            }
            return set;
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
