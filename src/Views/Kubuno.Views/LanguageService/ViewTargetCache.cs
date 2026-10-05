using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Kubuno.Views.Logic;

namespace Kubuno.Views.LanguageService
{
    /// <summary>
    /// <see cref="ViewTargets.Detect"/> over the file system, remembered per folder for a few seconds: the completion
    /// sources ask it on every keystroke that may open a list.
    /// </summary>
    public static class ViewTargetCache
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
        private static readonly ConcurrentDictionary<string, (ViewTarget Target, string? Root, DateTime At)> Folders =
            new ConcurrentDictionary<string, (ViewTarget, string?, DateTime)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The target of the view at <paramref name="viewPath"/> and, for the web, its project root.</summary>
        public static (ViewTarget Target, string? Root) Detect(string? viewPath)
        {
            var folder = string.IsNullOrEmpty(viewPath) ? null : Path.GetDirectoryName(viewPath);
            if (folder is null)
            {
                return (ViewTarget.Desktop, null);
            }

            if (Folders.TryGetValue(folder, out var hit) && DateTime.UtcNow - hit.At < Lifetime)
            {
                return (hit.Target, hit.Root);
            }

            var found = ViewTargets.Detect(viewPath, File.Exists, ReadText, FilesIn);
            Folders[folder] = (found.Target, found.Root, DateTime.UtcNow);
            return found;
        }

        /// <summary>Whether the view at <paramref name="viewPath"/> belongs to a web project.</summary>
        public static bool IsWeb(string? viewPath) => Detect(viewPath).Target == ViewTarget.Web;

        private static string? ReadText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static IEnumerable<string> FilesIn(string folder)
        {
            try
            {
                return Directory.GetFiles(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
