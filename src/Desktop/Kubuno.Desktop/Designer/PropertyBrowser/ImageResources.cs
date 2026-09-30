using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>
    /// Image attributes (<c>Image</c>, <c>BackgroundImage</c>, the view's <c>Icon</c>) hold a path RELATIVE to the view file,
    /// with forward slashes (<c>resources/logo.png</c>) - the runtime resolves it the same way. What the image editor needs:
    /// the project's images, relative paths, and where an outside file is copied (the view folder's <c>resources</c>).
    /// </summary>
    public static class ImageResources
    {
        /// <summary>The file extensions offered as images.</summary>
        public static IReadOnlyList<string> Extensions { get; } = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" };

        /// <summary>The folder a copied image goes to, under the view's folder.</summary>
        public const string ResourcesFolder = "resources";

        private static readonly string[] SkippedFolders = { "target", "bin", "obj", ".git", ".vs", "node_modules" };

        public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path ?? string.Empty).ToLowerInvariant());

        /// <summary><paramref name="file"/> relative to the folder of <paramref name="viewFile"/>, with forward slashes (<c>../assets/a.png</c> when outside).</summary>
        public static string RelativePath(string viewFile, string file)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(viewFile)) ?? string.Empty;
            var baseUri = new Uri(AppendSeparator(folder));
            var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(Path.GetFullPath(file))).ToString());
            return relative.Replace('\\', '/');
        }

        /// <summary>Whether <paramref name="file"/> is inside the folder of <paramref name="viewFile"/> (or below it).</summary>
        public static bool IsBesideView(string viewFile, string file)
        {
            var folder = AppendSeparator(Path.GetDirectoryName(Path.GetFullPath(viewFile)) ?? string.Empty);
            return Path.GetFullPath(file).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Where <paramref name="file"/> is copied: <c>&lt;view folder&gt;\resources\&lt;name&gt;</c>, numbered when that name is taken by another file.</summary>
        public static string CopyTarget(string viewFile, string file, Func<string, bool>? exists = null)
        {
            exists ??= File.Exists;
            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(viewFile)) ?? string.Empty, ResourcesFolder);
            var name = Path.GetFileNameWithoutExtension(file);
            var extension = Path.GetExtension(file);
            var target = Path.Combine(folder, name + extension);
            for (var i = 2; exists(target); i++)
            {
                target = Path.Combine(folder, name + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + extension);
            }

            return target;
        }

        /// <summary>The full path of attribute value <paramref name="path"/> (relative to the view), or null when it does not exist.</summary>
        public static string? Resolve(string? viewFile, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(viewFile) || Properties.BindingExpressionParser.IsBindingExpression(path))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(viewFile) ?? string.Empty, path!.Replace('/', Path.DirectorySeparatorChar)));
                return File.Exists(full) ? full : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// The images of the project holding <paramref name="viewFile"/> (the nearest folder with a <c>Cargo.toml</c>, else the
        /// view's folder), as paths relative to the view, build output folders skipped; at most <paramref name="limit"/>.
        /// </summary>
        public static IReadOnlyList<string> ProjectImages(string? viewFile, int limit = 400)
        {
            if (string.IsNullOrEmpty(viewFile))
            {
                return Array.Empty<string>();
            }

            var viewFolder = Path.GetDirectoryName(Path.GetFullPath(viewFile)) ?? string.Empty;
            var root = viewFolder;
            for (var dir = new DirectoryInfo(viewFolder); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Cargo.toml")))
                {
                    root = dir.FullName;
                    break;
                }
            }

            var found = new List<string>();
            void Walk(string folder, int depth)
            {
                if (found.Count >= limit || depth > 6)
                {
                    return;
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(folder).Where(IsImage).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        if (found.Count >= limit)
                        {
                            return;
                        }

                        found.Add(RelativePath(viewFile!, file));
                    }

                    foreach (var sub in Directory.EnumerateDirectories(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!SkippedFolders.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                        {
                            Walk(sub, depth + 1);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            Walk(root, 0);
            return found;
        }

        private static string AppendSeparator(string folder) =>
            folder.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? folder : folder + Path.DirectorySeparatorChar;
    }
}
