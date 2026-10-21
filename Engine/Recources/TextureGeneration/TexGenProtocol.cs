using System;
using System.Text.Json;

namespace Engine.Recources
{
    public enum TexGenEventKind { Unknown, Progress, Warning, Done, Error }

    /// <summary>One JSON line of texgen's stdout.</summary>
    public sealed class TexGenEvent
    {
        public TexGenEventKind Kind { get; init; }
        /// <summary>load, diffusion, estimation, style, postprocess, validate or write (Progress).</summary>
        public string Stage { get; init; }
        public double Progress { get; init; }
        /// <summary>Warning/Error code, e.g. albedo_too_dark, model_missing.</summary>
        public string Code { get; init; }
        public string Message { get; init; }
        /// <summary>Output folder (Done of generate).</summary>
        public string Out { get; init; }
        /// <summary>The original line.</summary>
        public string Raw { get; init; }
    }

    public static class TexGenProtocol
    {
        public static TexGenEvent Parse(string line)
        {
            var unknown = new TexGenEvent { Kind = TexGenEventKind.Unknown, Raw = line };
            if (string.IsNullOrWhiteSpace(line)) return unknown;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var kind) ||
                    kind.ValueKind != JsonValueKind.String)
                    return unknown;
                switch (kind.GetString())
                {
                    case "progress":
                        return new TexGenEvent
                        {
                            Kind = TexGenEventKind.Progress, Raw = line, Stage = Text(root, "stage"),
                            Progress = root.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0,
                        };
                    case "warning":
                        return new TexGenEvent { Kind = TexGenEventKind.Warning, Raw = line, Code = Text(root, "code"), Message = Text(root, "message") };
                    case "error":
                        return new TexGenEvent { Kind = TexGenEventKind.Error, Raw = line, Code = Text(root, "code"), Message = Text(root, "message") };
                    case "done":
                        return new TexGenEvent { Kind = TexGenEventKind.Done, Raw = line, Out = Text(root, "out"), Message = Text(root, "message") };
                    default:
                        return unknown;
                }
            }
            catch (JsonException) { return unknown; }
        }

        private static string Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
