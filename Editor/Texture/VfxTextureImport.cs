using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cuvara.VFX.Editor.Bridge;
using UnityEditor;
using UnityEngine;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// <c>import-texture</c>: reads a PNG, runs an ordered list of deterministic steps on its pixels,
    /// writes the result as a PNG asset and sets its <see cref="TextureImporter"/> settings.
    /// </summary>
    /// <remarks>
    /// <para>Steps and parameters: <see cref="VfxTextureSteps"/>; reference: <c>Documentation~/AUTHORING.md</c>,
    /// section "Texture import".</para>
    /// <para>The transform is checked completely, and every pixel step runs in memory, before the target is
    /// written. When writing or importing the target fails, its previous bytes and <c>.meta</c> are put back
    /// (or a new target is deleted), so a failed import leaves an existing target untouched.</para>
    /// </remarks>
    public static class VfxTextureImport
    {
        /// <summary>The import settings of an effect texture that no step chooses.</summary>
        public const TextureImporterCompression Compression = TextureImporterCompression.Compressed;

        /// <summary>A step of a transform, after its parameters were checked.</summary>
        sealed class Step
        {
            public int Index;
            public string Name;
            public JsonObject Fields;
            public string Label => $"transform[{Index}] ({Name})";
        }

        /// <summary>A failure that names the step and, for an unknown name, the valid names.</summary>
        sealed class StepException : Exception
        {
            public StepException(Step step, int index, string name, string message, IReadOnlyList<string> validNames = null)
                : base(message)
            {
                Index = step?.Index ?? index;
                Name = step?.Name ?? name;
                ValidNames = validNames ?? Array.Empty<string>();
            }

            public int Index { get; }
            public string Name { get; }
            public IReadOnlyList<string> ValidNames { get; }
        }

        /// <summary>Runs the verb on parsed arguments and fills <paramref name="result"/>.</summary>
        /// <param name="source">PNG path: project-relative (<c>Assets/...</c>, <c>Packages/...</c>) or absolute.</param>
        /// <param name="target">Project-relative <c>.png</c> asset path, or an absolute path inside the project or a local package.</param>
        /// <param name="transform">The steps (a JSON array of step names or step objects), or null for none.</param>
        public static void Import(string source, string target, object transform, VfxResult result)
        {
            List<Step> steps;
            try
            {
                steps = ParseTransform(transform);
            }
            catch (StepException exception)
            {
                Fail(result, exception);
                return;
            }

            var sourceFile = ResolveInputFile(source, "source", result);
            if (sourceFile == null)
                return;
            var targetPath = VfxProjectPaths.ToProjectPath(target);
            if (targetPath == null)
            {
                result.Error($"target '{target}' is not inside the project or a package of it (expected Assets/..., Packages/<package>/... or an absolute path to one)");
                return;
            }
            if (!targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                result.Error($"target '{targetPath}' must end with .png (import-texture writes PNG)");
                return;
            }

            var sourceBytes = File.ReadAllBytes(sourceFile);
            VfxPixels image;
            try
            {
                image = VfxPixels.DecodePng(sourceBytes);
            }
            catch (FormatException exception)
            {
                result.Error($"source '{source}': {exception.Message}");
                return;
            }

            result.Set("source", source);
            result.Set("source_sha256", VfxRecipeBuilder.Sha256(sourceBytes));
            result.Set("source_size", Size(image.Width, image.Height));
            result.Set("target", targetPath);

            var applied = new List<object>();
            var inputs = new List<object>();
            var settings = new Settings();
            try
            {
                foreach (var step in steps)
                    image = Run(step, image, settings, applied, inputs);
                if (settings.Flipbook != null)
                    CheckFlipbook(settings.Flipbook, image);
            }
            catch (StepException exception)
            {
                Fail(result, exception);
                return;
            }

            var outputBytes = image.EncodePng();
            result.Set("steps", applied);
            if (inputs.Count > 0)
                result.Set("inputs", inputs);
            result.Set("size", Size(image.Width, image.Height));
            if (settings.Flipbook != null)
                result.Set("flipbook", settings.Flipbook.ToJson(image));

            if (!WriteAndImport(targetPath, outputBytes, image, settings, result))
                return;

            var importer = (TextureImporter)AssetImporter.GetAtPath(targetPath);
            result.Set("output_sha256", VfxRecipeBuilder.Sha256(File.ReadAllBytes(VfxProjectPaths.ToFullPath(targetPath))));
            result.Set("import_settings", ImportSettings(importer, settings));
            if (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height))
                result.Warning($"the output is {image.Width}x{image.Height}, not a power of two on both axes; add resize_pot for mip maps and compression on every platform");
        }

        // ------------------------------------------------------------ transform

        static List<Step> ParseTransform(object transform)
        {
            var steps = new List<Step>();
            if (transform == null)
                return steps;
            if (!(transform is List<object> list))
                throw new StepException(null, -1, null, "'transform' must be an array of step names or step objects");

            var seen = new Dictionary<string, Step>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                JsonObject fields;
                string name;
                switch (list[i])
                {
                    case string s:
                        name = s;
                        fields = new JsonObject { { "step", s } };
                        break;
                    case JsonObject obj:
                        fields = obj;
                        name = obj.Get("step") as string;
                        if (name == null)
                            throw new StepException(null, i, null, $"transform[{i}]: a step object needs \"step\" (a step name)", VfxTextureSteps.Names);
                        break;
                    default:
                        throw new StepException(null, i, null, $"transform[{i}]: a step is a step name or an object with \"step\"", VfxTextureSteps.Names);
                }

                var spec = VfxTextureSteps.Find(name);
                if (spec == null)
                {
                    var valid = VfxGraphBridge.NearestFirst(name, VfxTextureSteps.Names);
                    throw new StepException(null, i, name, $"transform[{i}]: unknown step '{name}'; valid steps (nearest first): {string.Join(", ", valid)}", valid);
                }
                var step = new Step { Index = i, Name = name, Fields = fields };
                foreach (var pair in fields)
                {
                    if (!spec.AllFields.Contains(pair.Key))
                    {
                        var valid = VfxGraphBridge.NearestFirst(pair.Key, spec.AllFields);
                        throw new StepException(step, i, name, $"{step.Label}: unknown parameter '{pair.Key}'; valid parameters (nearest first): {string.Join(", ", valid)}", valid);
                    }
                }
                foreach (var required in spec.Required)
                    if (!fields.Has(required))
                        throw new StepException(step, i, name, $"{step.Label}: missing parameter '{required}'", spec.AllFields.ToList());
                if (name == "pack_channels" && !new[] { "r", "g", "b", "a" }.Any(fields.Has))
                    throw new StepException(step, i, name, $"{step.Label}: name at least one of r, g, b, a", new[] { "r", "g", "b", "a" });

                // Importer steps say one thing about the texture: once each, and linear or srgb, not both.
                var group = name == "srgb" ? "linear" : name;
                if (group == "flipbook" || group == "wrap" || group == "linear")
                {
                    if (seen.TryGetValue(group, out var earlier))
                        throw new StepException(step, i, name, $"{step.Label}: conflicts with {earlier.Label}; give it once");
                    seen[group] = step;
                }
                steps.Add(step);
            }
            return steps;
        }

        sealed class Flipbook
        {
            public int Columns;
            public int Rows;
            public Step Step;

            public JsonObject ToJson(VfxPixels image) => new JsonObject
            {
                { "columns", Columns },
                { "rows", Rows },
                { "frames", Columns * Rows },
                { "frame_width", image.Width / Columns },
                { "frame_height", image.Height / Rows },
            };
        }

        sealed class Settings
        {
            public bool Srgb = true;
            public bool Premultiplied;
            public TextureWrapMode WrapU = TextureWrapMode.Clamp;
            public TextureWrapMode WrapV = TextureWrapMode.Clamp;
            public Flipbook Flipbook;
        }

        static VfxPixels Run(Step step, VfxPixels image, Settings settings, List<object> applied, List<object> inputs)
        {
            var record = new JsonObject { { "step", step.Name } };
            switch (step.Name)
            {
                case "luminance_to_alpha":
                {
                    var color = Choice(step, "color", VfxTextureSteps.ColorModes, "keep");
                    image.LuminanceToAlpha(color);
                    settings.Premultiplied = false;
                    record.Add("color", color);
                    break;
                }
                case "premultiply":
                    image.Premultiply();
                    settings.Premultiplied = true;
                    break;
                case "pack_channels":
                    image = PackChannels(step, image, record, inputs);
                    if (step.Fields.Has("a"))
                        settings.Premultiplied = false;
                    break;
                case "resize_pot":
                {
                    var maxSize = Integer(step, "max_size", VfxTextureSteps.MaxSize, 1, VfxTextureSteps.MaxSize);
                    if (!IsPowerOfTwo(maxSize))
                        throw new StepException(step, step.Index, step.Name, $"{step.Label}: max_size {maxSize} is not a power of two");
                    var (width, height) = VfxPixels.PowerOfTwoSize(image.Width, image.Height, maxSize);
                    record.Add("max_size", maxSize);
                    record.Add("from", Size(image.Width, image.Height));
                    record.Add("to", Size(width, height));
                    image = image.Resize(width, height);
                    break;
                }
                case "flipbook":
                {
                    var columns = Integer(step, "columns", 1, 1, VfxTextureSteps.MaxSize);
                    var rows = Integer(step, "rows", 1, 1, VfxTextureSteps.MaxSize);
                    settings.Flipbook = new Flipbook { Columns = columns, Rows = rows, Step = step };
                    record.Add("columns", columns);
                    record.Add("rows", rows);
                    break;
                }
                case "wrap":
                {
                    var both = step.Fields.Has("mode") ? Choice(step, "mode", VfxTextureSteps.WrapModes, "clamp") : null;
                    if (both != null && (step.Fields.Has("u") || step.Fields.Has("v")))
                        throw new StepException(step, step.Index, step.Name, $"{step.Label}: give mode (both axes) or u and v, not both");
                    var u = both ?? Choice(step, "u", VfxTextureSteps.WrapModes, "clamp");
                    var v = both ?? Choice(step, "v", VfxTextureSteps.WrapModes, "clamp");
                    settings.WrapU = u == "repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                    settings.WrapV = v == "repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                    record.Add("u", u);
                    record.Add("v", v);
                    break;
                }
                case "linear":
                    settings.Srgb = false;
                    break;
                case "srgb":
                    settings.Srgb = true;
                    break;
                default:
                    throw new StepException(step, step.Index, step.Name, $"{step.Label}: step has no implementation", VfxTextureSteps.Names);
            }
            applied.Add(record);
            return image;
        }

        static VfxPixels PackChannels(Step step, VfxPixels image, JsonObject record, List<object> inputs)
        {
            var before = image.Clone();
            var files = new Dictionary<string, VfxPixels>(StringComparer.OrdinalIgnoreCase);
            var channelNames = new[] { "r", "g", "b", "a" };
            for (int c = 0; c < 4; c++)
            {
                var key = channelNames[c];
                if (!step.Fields.Has(key))
                    continue;
                var value = step.Fields.Get(key);
                var label = $"{step.Label}.{key}";
                var constant = value is long whole ? whole : value is double fraction ? fraction : double.NaN;
                switch (value)
                {
                    case long _:
                    case double _:
                    {
                        if (!(constant >= 0 && constant <= 1))
                            throw new StepException(step, step.Index, step.Name, $"{label}: a constant must be in [0, 1], got {Json.Write(value)}");
                        var b = VfxPixels.ConstantByte(constant);
                        for (int i = c; i < image.Rgba.Length; i += 4)
                            image.Rgba[i] = b;
                        record.Add(key, constant);
                        break;
                    }
                    case JsonObject source:
                    {
                        foreach (var pair in source)
                        {
                            if (!VfxTextureSteps.SourceFields.Contains(pair.Key))
                            {
                                var valid = VfxGraphBridge.NearestFirst(pair.Key, VfxTextureSteps.SourceFields);
                                throw new StepException(step, step.Index, step.Name, $"{label}: unknown parameter '{pair.Key}'; valid parameters (nearest first): {string.Join(", ", valid)}", valid);
                            }
                        }
                        if (!(source.Get("channel") is string channel))
                            throw new StepException(step, step.Index, step.Name, $"{label}: missing 'channel' ({string.Join(", ", VfxTextureSteps.Channels)})", VfxTextureSteps.Channels);
                        if (!VfxTextureSteps.Channels.Contains(channel))
                        {
                            var valid = VfxGraphBridge.NearestFirst(channel, VfxTextureSteps.Channels);
                            throw new StepException(step, step.Index, step.Name, $"{label}: unknown channel '{channel}'; valid channels (nearest first): {string.Join(", ", valid)}", valid);
                        }

                        var from = before;
                        var entry = new JsonObject();
                        if (source.Has("file"))
                        {
                            if (!(source.Get("file") is string file) || string.IsNullOrWhiteSpace(file))
                                throw new StepException(step, step.Index, step.Name, $"{label}: 'file' must be a path");
                            from = LoadInput(step, label, file, files, inputs);
                            if (from.Width != image.Width || from.Height != image.Height)
                                throw new StepException(step, step.Index, step.Name,
                                    $"{label}: '{file}' is {from.Width}x{from.Height}, the image is {image.Width}x{image.Height} at this step; the sizes must match");
                            entry.Add("file", file);
                        }
                        entry.Add("channel", channel);
                        for (int p = 0; p < image.Rgba.Length; p += 4)
                            image.Rgba[p + c] = from.Channel(p, channel);
                        record.Add(key, entry);
                        break;
                    }
                    default:
                        throw new StepException(step, step.Index, step.Name,
                            $"{label}: expected a constant in [0, 1] or {{\"file\" (optional), \"channel\": {string.Join(" | ", VfxTextureSteps.Channels)}}}");
                }
            }
            return image;
        }

        static VfxPixels LoadInput(Step step, string label, string file, Dictionary<string, VfxPixels> cache, List<object> inputs)
        {
            var probe = new VfxResult("import-texture");
            var full = ResolveInputFile(file, label, probe);
            if (full == null)
                throw new StepException(step, step.Index, step.Name, probe.Errors.FirstOrDefault() ?? $"{label}: '{file}' not found");
            if (cache.TryGetValue(full, out var cached))
                return cached;
            var bytes = File.ReadAllBytes(full);
            VfxPixels pixels;
            try
            {
                pixels = VfxPixels.DecodePng(bytes);
            }
            catch (FormatException exception)
            {
                throw new StepException(step, step.Index, step.Name, $"{label}: '{file}': {exception.Message}");
            }
            cache[full] = pixels;
            inputs.Add(new JsonObject
            {
                { "file", file },
                { "sha256", VfxRecipeBuilder.Sha256(bytes) },
                { "size", Size(pixels.Width, pixels.Height) },
            });
            return pixels;
        }

        static void CheckFlipbook(Flipbook flipbook, VfxPixels image)
        {
            if (image.Width % flipbook.Columns != 0 || image.Height % flipbook.Rows != 0)
            {
                throw new StepException(flipbook.Step, flipbook.Step.Index, flipbook.Step.Name,
                    $"{flipbook.Step.Label}: the output is {image.Width}x{image.Height}, which does not divide into {flipbook.Columns} columns and {flipbook.Rows} rows " +
                    $"({image.Width} % {flipbook.Columns} = {image.Width % flipbook.Columns}, {image.Height} % {flipbook.Rows} = {image.Height % flipbook.Rows})");
            }
        }

        static string Choice(Step step, string key, IReadOnlyList<string> valid, string fallback)
        {
            if (!step.Fields.Has(key))
                return fallback;
            if (step.Fields.Get(key) is string value && valid.Contains(value))
                return value;
            var wanted = step.Fields.Get(key) as string ?? Json.Write(step.Fields.Get(key));
            var ordered = VfxGraphBridge.NearestFirst(wanted, valid);
            throw new StepException(step, step.Index, step.Name, $"{step.Label}: unknown {key} '{wanted}'; valid values (nearest first): {string.Join(", ", ordered)}", ordered);
        }

        static int Integer(Step step, string key, int fallback, int min, int max)
        {
            if (!step.Fields.Has(key))
                return fallback;
            switch (step.Fields.Get(key))
            {
                case long l when l >= min && l <= max: return (int)l;
                case double d when d >= min && d <= max && Math.Abs(d - Math.Round(d)) < 1e-9: return (int)Math.Round(d);
                default: throw new StepException(step, step.Index, step.Name, $"{step.Label}: '{key}' must be an integer from {min} to {max}");
            }
        }

        // ------------------------------------------------------------ files and import

        /// <summary>The file-system path of an input PNG, or null with an error in <paramref name="result"/>.</summary>
        static string ResolveInputFile(string path, string label, VfxResult result)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                result.Error($"{label}: empty path");
                return null;
            }
            var normalized = path.Trim().Replace('\\', '/');
            string full;
            if (Path.IsPathRooted(normalized))
                full = Path.GetFullPath(normalized);
            else if (normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.StartsWith("Packages/", StringComparison.Ordinal))
                full = VfxProjectPaths.ToFullPath(normalized);
            else
            {
                result.Error($"{label} '{path}' must be project-relative (Assets/..., Packages/<package>/...) or absolute; Tools~/vfxcli.py resolves a path relative to the shell's directory");
                return null;
            }
            if (!File.Exists(full))
            {
                result.Error($"{label} '{path}' does not exist ({full})");
                return null;
            }
            return full;
        }

        static bool WriteAndImport(string targetPath, byte[] outputBytes, VfxPixels image, Settings settings, VfxResult result)
        {
            var targetFile = VfxProjectPaths.ToFullPath(targetPath);
            var metaFile = targetFile + ".meta";
            var existed = File.Exists(targetFile);
            var previousBytes = existed ? File.ReadAllBytes(targetFile) : null;
            var previousMeta = existed && File.Exists(metaFile) ? File.ReadAllBytes(metaFile) : null;
            var previousGuid = existed ? AssetDatabase.AssetPathToGUID(targetPath) : null;
            result.Set("created", !existed);
            result.Set("previous_sha256", previousBytes == null ? null : VfxRecipeBuilder.Sha256(previousBytes));

            try
            {
                EnsureFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/'));
                File.WriteAllBytes(targetFile, outputBytes);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                if (!(AssetImporter.GetAtPath(targetPath) is TextureImporter importer))
                    throw new InvalidOperationException($"'{targetPath}' has no TextureImporter after the import");
                Apply(importer, image, settings);
                importer.SaveAndReimport();
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath) == null)
                    throw new InvalidOperationException($"'{targetPath}' did not import as a Texture2D");
            }
            catch (Exception exception)
            {
                result.Error($"import of '{targetPath}' failed, the target is restored: {exception.GetType().Name}: {exception.Message}");
                Restore(targetPath, targetFile, metaFile, previousBytes, previousMeta, result);
                return false;
            }

            var guid = AssetDatabase.AssetPathToGUID(targetPath);
            result.Set("guid", guid);
            result.Set("guid_stable", previousGuid == null ? (object)null : previousGuid == guid);
            return true;
        }

        static void Restore(string targetPath, string targetFile, string metaFile, byte[] previousBytes, byte[] previousMeta, VfxResult result)
        {
            try
            {
                if (previousBytes != null)
                {
                    // Write back only what changed: when the write itself failed (a read-only or locked
                    // target), the file still holds the previous bytes and a second write would fail too.
                    var changed = false;
                    if (!SameBytes(targetFile, previousBytes))
                    {
                        File.WriteAllBytes(targetFile, previousBytes);
                        changed = true;
                    }
                    if (previousMeta != null && !SameBytes(metaFile, previousMeta))
                    {
                        File.WriteAllBytes(metaFile, previousMeta);
                        changed = true;
                    }
                    if (changed)
                        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                else if (!AssetDatabase.DeleteAsset(targetPath))
                {
                    if (File.Exists(targetFile))
                        File.Delete(targetFile);
                    if (File.Exists(metaFile))
                        File.Delete(metaFile);
                }
            }
            catch (Exception exception)
            {
                result.Error($"the target '{targetPath}' could not be restored: {exception.GetType().Name}: {exception.Message}");
            }
        }

        static bool SameBytes(string file, byte[] bytes) => File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(bytes);

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))))
                throw new IOException($"folder '{folder}' could not be created");
        }

        /// <summary>The import settings of an effect texture (AUTHORING.md, "Texture import").</summary>
        static void Apply(TextureImporter importer, VfxPixels image, Settings settings)
        {
            var hasAlpha = image.HasTranslucentAlpha();
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = settings.Srgb;
            importer.alphaSource = hasAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = hasAlpha && settings.Srgb && !settings.Premultiplied;
            importer.wrapModeU = settings.WrapU;
            importer.wrapModeV = settings.WrapV;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaxTextureSize(Math.Max(image.Width, image.Height));
            importer.textureCompression = Compression;
            importer.crunchedCompression = false;
            importer.isReadable = false;
        }

        static JsonObject ImportSettings(TextureImporter importer, Settings settings) => new JsonObject
        {
            { "texture_type", importer.textureType.ToString() },
            { "srgb", importer.sRGBTexture },
            { "alpha_source", importer.alphaSource.ToString() },
            { "alpha_is_transparency", importer.alphaIsTransparency },
            { "premultiplied", settings.Premultiplied },
            { "wrap_u", importer.wrapModeU.ToString() },
            { "wrap_v", importer.wrapModeV.ToString() },
            { "filter_mode", importer.filterMode.ToString() },
            { "mipmaps", importer.mipmapEnabled },
            { "npot_scale", importer.npotScale.ToString() },
            { "max_size", importer.maxTextureSize },
            { "compression", importer.textureCompression.ToString() },
            { "crunched", importer.crunchedCompression },
            { "readable", importer.isReadable },
        };

        /// <summary>The smallest importer max size (32 to 16384) that keeps <paramref name="size"/> pixels.</summary>
        public static int MaxTextureSize(int size)
        {
            var max = 32;
            while (max < size && max < VfxTextureSteps.MaxSize)
                max *= 2;
            return max;
        }

        static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        static List<object> Size(int width, int height) => new List<object> { width, height };

        static void Fail(VfxResult result, StepException exception)
        {
            result.Error(exception.Message);
            result.Set("step_error", new JsonObject
            {
                { "index", exception.Index },
                { "step", exception.Name },
                { "message", exception.Message },
                { "valid_names", exception.ValidNames.Cast<object>().ToList() },
            });
        }
    }
}
