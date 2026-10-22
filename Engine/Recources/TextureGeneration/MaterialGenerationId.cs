using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Engine.Recources
{
    /// <summary>What a generated material is made from; everything but <see cref="NormalConvention"/> goes into its id.</summary>
    public sealed class TexGenParams
    {
        public string Prompt { get; set; } = "";
        public string NegativePrompt { get; set; } = "";
        public long Seed { get; set; }
        public int Resolution { get; set; } = 1024;
        /// <summary>fake, classic, sdxl_classic or sdxl_chord.</summary>
        public string Backend { get; set; } = "fake";
        public string Style { get; set; } = "source2";
        /// <summary>A family name or "auto".</summary>
        public string Family { get; set; } = "auto";
        public bool ForceDielectric { get; set; }
        /// <summary>Photo / existing texture for the classic backend (a file on disk).</summary>
        public string InputRgb { get; set; }
        /// <summary>SHA-256 (lowercase hex) of <see cref="InputRgb"/>'s bytes; filled in from the file when null.</summary>
        public string InputRgbSha256 { get; set; }
        /// <summary>directx or opengl. Not part of the id: the contract fixes it per project.</summary>
        public string NormalConvention { get; set; } = "directx";
    }

    /// <summary>The material id of PLAN.md 2.4: the first 16 hex characters of the SHA-256 of the canonical string.</summary>
    public static class MaterialGenerationId
    {
        /// <summary>Must equal <c>PIPELINE_VERSION</c> in <c>Tools/TexGen/texgen/__init__.py</c>.</summary>
        public const string PipelineVersion = "6";

        public static string Canonical(TexGenParams p, string pipelineVersion = PipelineVersion) =>
            $"v{pipelineVersion}|{p.Backend}|{p.Style}|{p.Family}|{p.Resolution}|{p.Seed}|{(p.ForceDielectric ? 1 : 0)}|" +
            $"{p.InputRgbSha256 ?? ""}|{(p.Prompt ?? "").Trim()}|{(p.NegativePrompt ?? "").Trim()}";

        public static string Compute(TexGenParams p, string pipelineVersion = PipelineVersion)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(p, pipelineVersion)));
            return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
        }

        public static string HashFile(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        /// <summary>prompt -> lowercase [a-z0-9_], at most 32 characters, "material" when empty (same rule as texgen's <c>slugify</c>).</summary>
        public static string Slug(string prompt)
        {
            var sb = new StringBuilder();
            bool pendingSeparator = false;
            foreach (char c in (prompt ?? "").ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    if (pendingSeparator && sb.Length > 0) sb.Append('_');
                    pendingSeparator = false;
                    sb.Append(c);
                }
                else pendingSeparator = true;
            }
            string slug = sb.ToString();
            if (slug.Length > 32) slug = slug.Substring(0, 32).TrimEnd('_');
            return slug.Length == 0 ? "material" : slug;
        }
    }
}
