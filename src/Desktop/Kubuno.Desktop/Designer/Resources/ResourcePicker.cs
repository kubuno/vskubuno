using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kubuno.Desktop.Logic.Resources;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Resources
{
    /// <summary>Which resources a picker offers.</summary>
    public enum ResourceKindFilter
    {
        /// <summary>Image and Icon entries (an <c>Image</c>/<c>BackgroundImage</c> property).</summary>
        Images,

        /// <summary>Icon entries first, then images (an <c>Icon</c> property).</summary>
        Icons,

        /// <summary>Every entry.</summary>
        Any,
    }

    /// <summary>
    /// The entry points of the resource pickers (docs/RESOURCES.md), used by the image editor of the Properties window and
    /// by the icon picker's "Project resources" tab: pick a value (<see cref="Pick"/>, the WinForms "Select Resource"
    /// dialog), list the project's image resources (<see cref="ProjectImageResources"/>) and preview a value - a
    /// <c>{Res key}</c> reference or a path relative to the view (<see cref="LoadPreview"/>).
    /// </summary>
    public static class ResourcePicker
    {
        /// <summary>
        /// Shows the Select Resource dialog for an image/icon property of the view <paramref name="viewFile"/>; returns the
        /// attribute value to write (<c>{Res key}</c>, a relative path, or empty to clear), null when cancelled.
        /// </summary>
        public static string? Pick(string? viewFile, string? currentValue, ResourceKindFilter filter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new SelectResourceDialog(viewFile, currentValue, filter);
            return dialog.Ask() ? dialog.Result : null;
        }

        /// <summary>The Image and Icon resources of the project holding <paramref name="viewFile"/>.</summary>
        public static IReadOnlyList<ResourceItem> ProjectImageResources(string? viewFile) =>
            string.IsNullOrEmpty(viewFile) ? Array.Empty<ResourceItem>() : ProjectResources.Items(viewFile!).Where(i => i.Kind is ResourceKind.Image or ResourceKind.Icon).ToList();

        /// <summary>A preview of an image property value (a <c>{Res …}</c> reference, or a path relative to the view); null when there is none (or for an SVG).</summary>
        public static ImageSource? LoadPreview(string? viewFile, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(viewFile))
            {
                return null;
            }

            if (ResourceNames.ParseReference(value) is not null)
            {
                return ProjectResources.Find(ProjectResources.Items(viewFile!), value) is { } item ? Decode(item.ReadBytes()) : null;
            }

            var full = PropertyBrowser.ImageResources.Resolve(viewFile, value);
            if (full is null)
            {
                return null;
            }

            try
            {
                return Decode(File.ReadAllBytes(full));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The image in <paramref name="bytes"/> (the largest frame of an icon), frozen; null when WPF cannot decode it (SVG).</summary>
        public static BitmapSource? Decode(byte[]? bytes)
        {
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                using var stream = new MemoryStream(bytes);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
                frame?.Freeze();
                return frame;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>Whether <paramref name="bytes"/> look like an SVG document.</summary>
        public static bool IsSvg(byte[]? bytes)
        {
            if (bytes is null)
            {
                return false;
            }

            var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('﻿', ' ', '\t', '\r', '\n');
            return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
