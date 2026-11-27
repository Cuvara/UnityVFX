using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cuvara.VFX.Editor.Bridge;
using UnityEditor;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// Builds a <c>.vfx</c> from a recipe (<c>build</c>) and applies recipe ops to an existing graph
    /// (<c>apply</c>), always on a temporary copy that replaces the real asset only when it compiles.
    /// </summary>
    /// <remarks>
    /// <para>The temporary copy lives in <see cref="TempFolder"/> and is deleted afterwards. A failed
    /// build or apply never writes the real asset, so it stays byte-identical. A replace copies the
    /// compiled file over the real one and re-imports it; the real asset's <c>.meta</c> is not touched,
    /// so its GUID stays the same.</para>
    /// <para>The graph is changed only through <see cref="VfxGraphBridge.GraphEdit"/>; no <c>.vfx</c>
    /// text is written or patched.</para>
    /// </remarks>
    public static class VfxRecipeBuilder
    {
        /// <summary>The project folder of the temporary copies.</summary>
        public const string TempFolder = "Assets/CuvaraVfxBuildTemp";

        /// <summary>The file name suffix of a recipe.</summary>
        public const string RecipeSuffix = ".vfxrecipe.json";

        static readonly Regex IdPattern = new Regex("^[A-Za-z_][A-Za-z0-9_-]*$");

        /// <summary><c>build</c>: reads the recipe, builds its graph and creates or replaces the <c>.vfx</c> next to it.</summary>
        public static void Build(string recipeArgument, VfxResult result)
        {
            var recipePath = VfxProjectPaths.ToProjectPath(recipeArgument);
            if (recipePath == null)
            {
                result.Error($"recipe '{recipeArgument}' is not inside the project or a package of it (expected Assets/..., Packages/<package>/... or an absolute path to one)");
                return;
            }
            if (!recipePath.EndsWith(RecipeSuffix, StringComparison.Ordinal))
            {
                result.Error($"recipe '{recipePath}' does not end with {RecipeSuffix}");
                return;
            }
            var recipeFile = VfxProjectPaths.ToFullPath(recipePath);
            if (!File.Exists(recipeFile))
            {
                result.Error($"recipe '{recipePath}' does not exist ({recipeFile})");
                return;
            }

            var bytes = File.ReadAllBytes(recipeFile);
            var name = Path.GetFileName(recipePath);
            name = name.Substring(0, name.Length - RecipeSuffix.Length);
            var target = recipePath.Substring(0, recipePath.Length - Path.GetFileName(recipePath).Length) + name + ".vfx";

            result.Set("recipe", recipePath);
            result.Set("recipe_sha256", Sha256(bytes));

            JsonObject recipe;
            try
            {
                recipe = Json.Parse(new UTF8Encoding(false).GetString(bytes).TrimStart((char)0xFEFF)) as JsonObject;
            }
            catch (FormatException exception)
            {
                result.Error($"recipe '{recipePath}' is not valid JSON: {exception.Message}");
                return;
            }
            if (recipe == null)
            {
                result.Error($"recipe '{recipePath}' is not a JSON object");
                return;
            }

            var ops = ReadRecipe(recipe, result);
            if (ops == null)
                return;
            result.Set("recipe_id", recipe.Get("id"));

            Run(target, ops, edit: null, result);
        }

        /// <summary><c>apply</c>: applies ops to an existing <c>.vfx</c> through a temporary copy.</summary>
        public static void Apply(string assetArgument, object opsValue, VfxResult result)
        {
            var asset = VfxProjectPaths.ToProjectPath(assetArgument);
            if (asset == null || !asset.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
            {
                result.Error($"asset '{assetArgument}' is not a .vfx inside the project or a package of it");
                return;
            }
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(asset, AssetPathToGUIDOptions.OnlyExistingAssets)))
            {
                result.Error($"asset '{asset}' does not exist");
                return;
            }
            if (!(opsValue is List<object> ops))
            {
                result.Error("'ops' must be an array of op objects");
                return;
            }
            Run(asset, ops, edit: asset, result);
        }

        /// <summary>
        /// Checks the top level of a recipe and returns its ops, or null after adding errors.
        /// </summary>
        static List<object> ReadRecipe(JsonObject recipe, VfxResult result)
        {
            foreach (var pair in recipe)
                if (!VfxRecipeOps.TopLevelFields.Contains(pair.Key))
                    result.Error(UnknownMessage("recipe field", pair.Key, VfxRecipeOps.TopLevelFields));

            var schema = recipe.Get("schema") as string;
            if (schema != VfxRecipeOps.Schema)
                result.Error($"recipe 'schema' must be \"{VfxRecipeOps.Schema}\", got {(schema == null ? "nothing" : "\"" + schema + "\"")}");
            if (!(recipe.Get("id") is string id) || id.Length == 0)
                result.Error("recipe 'id' must be a non-empty string");
            if (recipe.Has("vfx_graph"))
            {
                var wanted = recipe.Get("vfx_graph") as string;
                if (wanted == null)
                    result.Error("recipe 'vfx_graph' must be a string such as \"17.6\"");
                else if (VfxGraphBridge.MajorMinor(wanted) != VfxGraphBridge.MajorMinor(VfxGraphBridge.InstalledVersion))
                    result.Warning($"the recipe was written for VFX Graph {wanted}; the project has {VfxGraphBridge.InstalledVersion}");
            }
            if (recipe.Has("meta") && !(recipe.Get("meta") is JsonObject))
                result.Error("recipe 'meta' must be an object");

            var ops = recipe.Get("ops") as List<object>;
            if (ops == null)
                result.Error("recipe 'ops' must be an array of op objects");
            return result.Ok ? ops : null;
        }

        static void Run(string target, List<object> ops, string edit, VfxResult result)
        {
            var name = Path.GetFileNameWithoutExtension(target);
            var temp = TempFolder + "/" + name + ".vfx";
            var targetFile = VfxProjectPaths.ToFullPath(target);
            var existedBefore = File.Exists(targetFile);
            var guidBefore = existedBefore ? AssetDatabase.AssetPathToGUID(target) : null;
            var hashBefore = existedBefore ? Sha256(File.ReadAllBytes(targetFile)) : null;
            result.Set("asset", new JsonObject { { "path", target }, { "guid", guidBefore } });

            try
            {
                ResetTempFolder();

                VfxGraphBridge.GraphEdit graph;
                if (edit == null)
                {
                    graph = VfxGraphBridge.GraphEdit.Create(temp);
                }
                else
                {
                    if (!AssetDatabase.CopyAsset(edit, temp))
                    {
                        result.Error($"could not copy '{edit}' to the temporary '{temp}'");
                        return;
                    }
                    AssetDatabase.ImportAsset(temp, ImportAssetOptions.ForceSynchronousImport);
                    graph = VfxGraphBridge.GraphEdit.Open(temp);
                    if (graph.UpgradedFromResourceVersion is int upgradedFrom)
                        result.Warning($"'{edit}' was saved by an older VFX Graph (resource version {upgradedFrom}); it was upgraded to resource version " +
                                       $"{VfxGraphBridge.CurrentResourceVersion} and graph version {VfxGraphBridge.CurrentGraphVersion} before the first op" +
                                       (upgradedFrom < 1 ? ", and VFX Graph converted its existing gradient colour keys with Color.linear" : string.Empty));
                }

                if (!ApplyOps(graph, ops, result))
                    return;
                result.Set("ops_applied", ops.Count);

                graph.Save();
                var report = VfxGraphBridge.CompileReport(temp);
                var compileErrors = ((List<object>)report.Get("errors")).Cast<string>().ToList();
                if (edit == null && report.Get("upgraded") is bool upgraded && upgraded)
                    compileErrors.Add("VFX Graph upgraded the new graph at its first compile, so the stored values are not the recipe's " +
                                      "(a new graph must be at the current graph and resource versions before the first op)");
                foreach (var warning in ((List<object>)report.Get("warnings")).Cast<string>())
                    result.Warning("compile: " + warning);
                result.Set("compile_errors", compileErrors.Cast<object>().ToList());
                if (!(report.Get("compiled") is bool compiled) || !compiled || compileErrors.Count > 0)
                {
                    foreach (var error in compileErrors)
                        result.Error("compile: " + error);
                    if (compileErrors.Count == 0)
                        result.Error("compile: the graph did not compile");
                    return;
                }

                Replace(temp, target, existedBefore);

                var guidAfter = AssetDatabase.AssetPathToGUID(target);
                result.Set("asset", new JsonObject { { "path", target }, { "guid", guidAfter } });
                result.Set("created", !existedBefore);
                result.Set("guid_stable", existedBefore ? (object)(guidBefore == guidAfter) : null);
                result.Set("previous_sha256", hashBefore);
                result.Set("sha256", Sha256(File.ReadAllBytes(targetFile)));
                if (existedBefore && guidBefore != guidAfter)
                    result.Error($"the GUID of '{target}' changed from {guidBefore} to {guidAfter}");

                var describe = VfxCli.Run("describe", Json.Write(new JsonObject { { "asset", target } }));
                result.Set("describe_sha256", Sha256(new UTF8Encoding(false).GetBytes(describe)));
                result.Set("systems", VfxGraphBridge.Systems(target));
            }
            catch (VfxGraphBridge.EditException exception)
            {
                result.Error(exception.Message);
            }
            finally
            {
                DeleteTempFolder();
            }
        }

        static void Replace(string temp, string target, bool existedBefore)
        {
            var targetFile = VfxProjectPaths.ToFullPath(target);
            var directory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Copy(VfxProjectPaths.ToFullPath(temp), targetFile, true);
            if (existedBefore)
                VfxGraphBridge.ForgetResource(target);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        static void ResetTempFolder()
        {
            DeleteTempFolder();
            AssetDatabase.CreateFolder(Path.GetDirectoryName(TempFolder).Replace('\\', '/'), Path.GetFileName(TempFolder));
        }

        static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        // ------------------------------------------------------------------ ops

        /// <summary>Applies the ops in order; stops at the first failing op, adds its error and returns false.</summary>
        static bool ApplyOps(VfxGraphBridge.GraphEdit graph, List<object> ops, VfxResult result)
        {
            for (int index = 0; index < ops.Count; index++)
            {
                var op = ops[index] as JsonObject;
                string name = op?.Get("op") as string;
                string label = $"op {index}";
                try
                {
                    if (op == null)
                        throw new VfxGraphBridge.EditException("an op must be a JSON object");
                    if (name == null)
                        throw new VfxGraphBridge.EditException("the op has no 'op' name");
                    var spec = VfxRecipeOps.Find(name);
                    if (spec == null)
                        throw Unknown("op", name, VfxRecipeOps.Names);
                    label = $"op {index} ({name}{(op.Get("id") is string opId ? " '" + opId + "'" : string.Empty)})";

                    foreach (var pair in op)
                        if (!spec.AllFields.Contains(pair.Key))
                            throw Unknown($"field of {name}", pair.Key, spec.AllFields);
                    foreach (var field in spec.Required)
                        if (!op.Has(field))
                            throw new VfxGraphBridge.EditException($"missing required field '{field}' (fields of {name}: required {string.Join(", ", spec.Required)}; optional {(spec.Optional.Count == 0 ? "none" : string.Join(", ", spec.Optional))})");

                    ApplyOp(graph, name, op);
                }
                catch (VfxGraphBridge.EditException exception)
                {
                    Fail(result, index, name, label, exception.Message, exception.ValidNames);
                    return false;
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    Fail(result, index, name, label, $"{exception.GetType().Name}: {exception.Message}", Array.Empty<string>());
                    return false;
                }
            }
            return true;
        }

        static void Fail(VfxResult result, int index, string name, string label, string message, IReadOnlyList<string> valid)
        {
            result.Error($"{label}: {message}");
            result.Set("op_error", new JsonObject
            {
                { "index", index },
                { "op", name },
                { "message", message },
                { "valid_names", valid.Cast<object>().ToList() },
            });
        }

        static void ApplyOp(VfxGraphBridge.GraphEdit graph, string name, JsonObject op)
        {
            switch (name)
            {
                case "add_parameter":
                {
                    var id = NewId(op);
                    var exposed = op.Has("exposed") ? Bool(op, "exposed") : true;
                    graph.AddParameter(id, String(op, "type"), String(op, "name"), op.Get("value"), exposed, OptionalString(op, "category"), OptionalString(op, "tooltip"));
                    break;
                }
                case "add_context":
                {
                    var id = NewId(op);
                    graph.AddContext(id, String(op, "type"), OptionalString(op, "variant"), OptionalString(op, "label"));
                    ApplySettingsAndSlots(graph, id, op);
                    break;
                }
                case "add_block":
                {
                    var id = NewId(op);
                    graph.AddBlock(String(op, "context"), id, String(op, "type"), OptionalString(op, "variant"));
                    ApplySettingsAndSlots(graph, id, op);
                    break;
                }
                case "add_operator":
                {
                    var id = NewId(op);
                    graph.AddOperator(id, String(op, "type"), OptionalString(op, "variant"));
                    ApplySettingsAndSlots(graph, id, op);
                    break;
                }
                case "set_setting":
                    graph.SetSetting(String(op, "node"), String(op, "name"), op.Get("value"));
                    break;
                case "set_slot":
                    if (!op.Has("value") && !op.Has("space"))
                        throw new VfxGraphBridge.EditException("set_slot needs 'value', 'space' or both");
                    graph.SetSlot(String(op, "slot"), op.Get("value"), op.Has("value"), OptionalString(op, "space"));
                    break;
                case "link_slots":
                    graph.LinkSlots(String(op, "from"), String(op, "to"));
                    break;
                case "link_flow":
                    graph.LinkFlow(String(op, "from"), String(op, "to"), Index(op, "from_slot"), Index(op, "to_slot"));
                    break;
                case "use_subgraph":
                {
                    var id = NewId(op);
                    graph.UseSubgraph(id, String(op, "asset"), OptionalString(op, "context"));
                    ApplySettingsAndSlots(graph, id, op);
                    break;
                }
                case "set_output":
                    SetOutput(graph, op);
                    break;
                case "comment":
                    graph.AddComment(OptionalString(op, "title"), String(op, "text"));
                    break;
                default:
                    throw Unknown("op", name, VfxRecipeOps.Names);
            }
        }

        static void ApplySettingsAndSlots(VfxGraphBridge.GraphEdit graph, string id, JsonObject op)
        {
            if (op.Has("settings"))
            {
                if (!(op.Get("settings") is JsonObject settings))
                    throw new VfxGraphBridge.EditException("'settings' must be an object of setting name to value");
                foreach (var pair in settings)
                    graph.SetSetting(id, pair.Key, pair.Value);
            }
            if (op.Has("slots"))
            {
                if (!(op.Get("slots") is JsonObject slots))
                    throw new VfxGraphBridge.EditException("'slots' must be an object of slot path to value");
                foreach (var pair in slots)
                    graph.SetSlot(id + "." + pair.Key, pair.Value, true, null);
            }
        }

        static void SetOutput(VfxGraphBridge.GraphEdit graph, JsonObject op)
        {
            var node = String(op, "node");
            if (graph.Kind(node) != "context")
                throw new VfxGraphBridge.EditException($"set_output needs an output context, and '{node}' is a {graph.Kind(node)}");
            if (!op.Has("texture") && !op.Has("blend_mode") && !op.Has("color_mapping") && !op.Has("gradient"))
                throw new VfxGraphBridge.EditException("set_output needs at least one of texture, blend_mode, color_mapping, gradient");

            if (op.Has("blend_mode"))
                graph.SetSetting(node, "blendMode", op.Get("blend_mode"));
            if (op.Has("color_mapping"))
                graph.SetSetting(node, "colorMapping", op.Get("color_mapping"));
            if (op.Has("texture"))
            {
                var slots = graph.InputSlots(node);
                var slot = new[] { "mainTexture", "baseColorMap" }.FirstOrDefault(slots.Contains);
                if (slot == null)
                {
                    var textures = slots.Where(s => !s.Contains('.') && (graph.InputSlotType(node + "." + s) ?? string.Empty).StartsWith("Texture", StringComparison.Ordinal)).ToList();
                    if (textures.Count != 1)
                        throw new VfxGraphBridge.EditException($"'{node}' has no mainTexture or baseColorMap input; texture inputs: {(textures.Count == 0 ? "none" : string.Join(", ", textures))}; use set_slot", textures);
                    slot = textures[0];
                }
                graph.SetSlot(node + "." + slot, op.Get("texture"), true, null);
            }
            if (op.Has("gradient"))
            {
                if (!graph.InputSlots(node).Contains("gradient"))
                    throw new VfxGraphBridge.EditException($"'{node}' has no gradient input; set color_mapping to GradientMapped first");
                graph.SetSlot(node + ".gradient", op.Get("gradient"), true, null);
            }
        }

        // ------------------------------------------------------------------ field readers

        static string NewId(JsonObject op)
        {
            var id = String(op, "id");
            if (!IdPattern.IsMatch(id))
                throw new VfxGraphBridge.EditException($"id '{id}' must match {IdPattern} (letters, digits, '_' and '-', not starting with a digit)");
            return id;
        }

        static string String(JsonObject op, string key)
        {
            if (op.Get(key) is string value && value.Length > 0)
                return value;
            throw new VfxGraphBridge.EditException($"'{key}' must be a non-empty string");
        }

        static string OptionalString(JsonObject op, string key)
        {
            if (!op.Has(key) || op.Get(key) == null)
                return null;
            if (op.Get(key) is string value)
                return value;
            throw new VfxGraphBridge.EditException($"'{key}' must be a string");
        }

        static bool Bool(JsonObject op, string key)
        {
            if (op.Get(key) is bool value)
                return value;
            throw new VfxGraphBridge.EditException($"'{key}' must be true or false");
        }

        static int Index(JsonObject op, string key)
        {
            if (!op.Has(key))
                return 0;
            switch (op.Get(key))
            {
                case long l when l >= 0 && l < 64: return (int)l;
                case double d when d >= 0 && d < 64 && Math.Abs(d - Math.Round(d)) < 1e-9: return (int)d;
                default: throw new VfxGraphBridge.EditException($"'{key}' must be a small non-negative integer");
            }
        }

        static VfxGraphBridge.EditException Unknown(string what, string wanted, IEnumerable<string> valid)
        {
            var ordered = VfxGraphBridge.NearestFirst(wanted, valid);
            return new VfxGraphBridge.EditException(UnknownMessage(what, wanted, ordered), ordered);
        }

        static string UnknownMessage(string what, string wanted, IEnumerable<string> valid)
        {
            var ordered = VfxGraphBridge.NearestFirst(wanted, valid);
            return VfxGraphBridge.UnknownNameMessage(what, wanted, ordered);
        }

        /// <summary>Lower-case hex SHA-256 of bytes.</summary>
        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
