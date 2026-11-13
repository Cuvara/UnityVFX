using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// Maps the paths agents pass (project-relative, absolute inside the project, or absolute inside a
    /// local package such as this repository) to the project-relative paths the AssetDatabase uses.
    /// </summary>
    public static class VfxProjectPaths
    {
        /// <summary>
        /// The project-relative path (<c>Assets/...</c> or <c>Packages/&lt;name&gt;/...</c>) of
        /// <paramref name="path"/>, or null when it is outside the project and every registered package.
        /// </summary>
        /// <remarks>
        /// A file under a local package is found by the nearest ancestor folder whose
        /// <c>package.json</c> names a registered package, so a path through a link (the Sandbox's
        /// <c>&lt;sandbox home&gt;/package</c>) and the path of the repository itself give the same
        /// result.
        /// </remarks>
        public static string ToProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var normalized = path.Trim().Replace('\\', '/');
            if (!Path.IsPathRooted(normalized))
            {
                normalized = normalized.StartsWith("./", StringComparison.Ordinal) ? normalized.Substring(2) : normalized;
                return normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized == "Assets" ||
                       normalized.StartsWith("Packages/", StringComparison.Ordinal)
                    ? normalized
                    : null;
            }

            var full = Path.GetFullPath(normalized).Replace('\\', '/');
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            if (full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = full.Substring(projectRoot.Length);
                if (relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.StartsWith("Packages/", StringComparison.Ordinal))
                    return relative;
            }

            var logical = FileUtil.GetLogicalPath(full);
            if (!string.IsNullOrEmpty(logical) && logical.StartsWith("Packages/", StringComparison.Ordinal))
                return logical;

            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            var directory = Path.GetDirectoryName(full);
            while (!string.IsNullOrEmpty(directory))
            {
                var manifest = Path.Combine(directory, "package.json");
                if (File.Exists(manifest))
                {
                    var name = ReadPackageName(manifest);
                    var package = packages.FirstOrDefault(p => p.name == name);
                    if (package != null)
                    {
                        var relative = full.Substring(directory.Replace('\\', '/').TrimEnd('/').Length).TrimStart('/');
                        return package.assetPath.TrimEnd('/') + (relative.Length > 0 ? "/" + relative : string.Empty);
                    }
                    return null;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }

        /// <summary>The absolute file-system path of a project-relative path.</summary>
        public static string ToFullPath(string projectPath) => Path.GetFullPath(projectPath).Replace('\\', '/');

        static string ReadPackageName(string manifest)
        {
            try
            {
                return (Json.Parse(File.ReadAllText(manifest)) as JsonObject)?.Get("name") as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
