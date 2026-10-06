using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using Microsoft.Win32;

namespace Kubuno.Views.Designer.EditorFactory
{
    /// <summary>
    /// The one-time clean-up that lets the Kubuno View Designer become the default editor of views for users who already
    /// had Kubuno (docs/DESIGNER.md section 11, "Default editor").
    /// <para>
    /// Before this version the Source Code (Text) Editor was the default editor of <c>.kbview</c> / <c>.kbcontrol</c>, and
    /// testing the designer meant "Open With... &gt; Set as Default" back and forth. Visual Studio 2026 keeps that choice per
    /// user in its unified settings (verified live): the <c>textEditor.fileExtensions.openWith</c> array of the hive's
    /// <c>settings.json</c> (<c>{ "editor": "{editor GUID}:{logical view}", "extension": "kbview" }</c>), mirrored under the
    /// user registry root as <c>Default Editors\&lt;ext&gt;</c> (<c>Custom</c> = the editor's GUID, <c>Type</c>) and
    /// <c>FileExtensionMapping\&lt;ext&gt;</c> (default value = the editor's GUID, <c>LogViewID</c>). The settings entry is the
    /// source of truth (the registry alone, removed, came back from it), and it beats every registered priority, so such a
    /// leftover would keep the text editor the default forever.
    /// </para>
    /// <para>
    /// So, once per user (<see cref="DoneProperty"/>), a per-user default that names the Source Code (Text) Editor - the
    /// editor the older versions already opened by default, hence a choice made by them or by tests rather than a
    /// different preference - is removed, and logged in the Kubuno pane. Any other per-user default (the XML editor, another
    /// editor) is the user's own choice and is kept, and so is any choice made after this run, the text editor included.

    /// </para>
    /// </summary>
    public static class DefaultEditorMigration
    {
        /// <summary>The user settings property recording the migration (<see cref="DesignerConstants"/>' collection).</summary>
        public const string DoneProperty = "DefaultEditorMigrationVersion";

        /// <summary>Bump to run a new clean-up once more.</summary>
        public const int Version = 1;

        /// <summary>The view extensions the designer is the default editor of (no leading dot, as the registry keys spell them).</summary>
        public static readonly IReadOnlyList<string> Extensions = new[] { "kbview", "kbcontrol" };

        /// <summary>The Source Code (Text) Editor (<c>VSConstants.VsEditorFactoryGuid.TextEditor_guid</c>).</summary>
        public static readonly Guid TextEditor = new Guid("8B382828-6202-11d1-8870-0000F87579D2");

        /// <summary>
        /// Whether a per-user default is the leftover this clean-up removes: at least one of the two values is present, and
        /// every present one names the Source Code (Text) Editor.
        /// </summary>
        /// <param name="defaultEditorsCustom"><c>Default Editors\&lt;ext&gt;</c>'s <c>Custom</c> value, or null.</param>
        /// <param name="extensionMapping"><c>FileExtensionMapping\&lt;ext&gt;</c>'s default value, or null.</param>
        public static bool IsLeftover(string? defaultEditorsCustom, string? extensionMapping)
        {
            var values = new[] { defaultEditorsCustom, extensionMapping };
            var present = 0;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                present++;
                if (EditorOf(value!) != TextEditor)
                {
                    return false;
                }
            }

            return present > 0;
        }

        /// <summary>The editor GUID of a per-user default: <c>{guid}</c> (registry) or <c>{guid}:{logical view}</c> (settings), else empty.</summary>
        public static Guid EditorOf(string value)
        {
            var text = value.Trim();
            var colon = text.IndexOf(':');
            return Guid.TryParse(colon >= 0 ? text.Substring(0, colon) : text, out var editor) ? editor : Guid.Empty;
        }

        /// <summary>Runs the clean-up if it never ran for this user (package load, before any view can open).</summary>
        public static void RunOnce()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            WritableSettingsStore store;
            try
            {
                store = new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
                if (store.CollectionExists(DesignerConstants.UserSettingsCollection) &&
                    store.GetInt32(DesignerConstants.UserSettingsCollection, DoneProperty, 0) >= Version)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                KubunoLog.WriteException("[designer] default editor clean-up skipped (user settings store unavailable)", ex);
                return;
            }

            CleanUnifiedSettings();

            try
            {
                using var userRoot = VSRegistry.RegistryRoot(__VsLocalRegistryType.RegType_UserSettings, writable: true);
                if (userRoot is not null)
                {
                    foreach (var extension in Extensions)
                    {
                        Clean(userRoot, extension);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException or ArgumentException)
            {
                KubunoLog.WriteException("[designer] default editor clean-up failed", ex);
            }

            if (!store.CollectionExists(DesignerConstants.UserSettingsCollection))
            {
                store.CreateCollection(DesignerConstants.UserSettingsCollection);
            }

            store.SetInt32(DesignerConstants.UserSettingsCollection, DoneProperty, Version);
        }

        private static void Clean(RegistryKey userRoot, string extension)
        {
            var defaultEditorsPath = @"Default Editors\" + extension;
            var mappingPath = @"FileExtensionMapping\" + extension;
            string? custom;
            string? mapping;
            using (var key = userRoot.OpenSubKey(defaultEditorsPath))
            {
                custom = key?.GetValue("Custom") as string;
            }

            using (var key = userRoot.OpenSubKey(mappingPath))
            {
                mapping = key?.GetValue(string.Empty) as string;
            }

            if (custom is null && mapping is null)
            {
                return;
            }

            if (!IsLeftover(custom, mapping))
            {
                KubunoLog.WriteLine($"[designer] .{extension}: your per-user default editor ({custom ?? mapping}, Open With > Set as Default) is kept; the Kubuno View Designer stays available through Open With.");
                return;
            }

            userRoot.DeleteSubKeyTree(defaultEditorsPath, throwOnMissingSubKey: false);
            userRoot.DeleteSubKeyTree(mappingPath, throwOnMissingSubKey: false);
            KubunoLog.WriteLine($"[designer] .{extension}: removed the per-user default editor left by an older Kubuno version or a test (Source Code (Text) Editor, registry) - the Kubuno View Designer is the default; Open With > Source Code (Text) Editor > Set as Default brings the text editor back (this clean-up runs once).");
        }

        /// <summary>The unified setting holding the per-user "Open With > Set as Default" choices.</summary>
        public const string OpenWithSetting = "textEditor.fileExtensions.openWith";

        private static void CleanUnifiedSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (Package.GetGlobalService(typeof(SVsUnifiedSettingsManager)) is not Microsoft.VisualStudio.Utilities.UnifiedSettings.ISettingsManager manager)
                {
                    return;
                }

                var read = manager.GetReader().GetArray<IReadOnlyDictionary<string, object>>(OpenWithSetting, SettingReadOptions.NoRequirements);
                if (read.Outcome != SettingRetrievalOutcome.Success || read.Value is null)
                {
                    if (read.Outcome != SettingRetrievalOutcome.NotPersisted)
                    {
                        KubunoLog.WriteLine($"[designer] default editor clean-up: {OpenWithSetting} not read ({read.Outcome}).");
                    }

                    return;
                }

                var kept = new List<IReadOnlyDictionary<string, object>>();
                var removed = new List<string>();
                foreach (var entry in read.Value)
                {
                    var extension = entry.TryGetValue("extension", out var e) ? (e as string ?? string.Empty).TrimStart('.') : string.Empty;
                    var editor = entry.TryGetValue("editor", out var v) ? v as string : null;
                    var isView = Enumerable.Contains(Extensions, extension, StringComparer.OrdinalIgnoreCase);
                    if (isView && editor is not null && IsLeftover(editor, null))
                    {
                        removed.Add(extension);
                        continue;
                    }

                    if (isView)
                    {
                        KubunoLog.WriteLine($"[designer] .{extension}: your per-user default editor ({editor}, Open With > Set as Default) is kept; the Kubuno View Designer stays available through Open With.");
                    }

                    kept.Add(entry);
                }

                if (removed.Count == 0)
                {
                    return;
                }

                var writer = manager.GetWriter("Kubuno");
                var change = writer.EnqueueArrayChange(OpenWithSetting, kept);
                var commit = writer.RequestCommit("Kubuno: the Kubuno View Designer is the default editor of views");
                foreach (var extension in removed)
                {
                    KubunoLog.WriteLine($"[designer] .{extension}: removed the per-user default editor left by an older Kubuno version or a test (Source Code (Text) Editor, {OpenWithSetting}: {change.Outcome}/{commit.Outcome}) - the Kubuno View Designer is the default; Open With > Source Code (Text) Editor > Set as Default brings the text editor back (this clean-up runs once).");
                }
            }
            catch (Exception ex)
            {
                // Unified settings are an evolving Visual Studio 2026 API: never let it break the package load.
                KubunoLog.WriteException("[designer] default editor clean-up of " + OpenWithSetting + " failed", ex);
            }
        }

        /// <summary>Visual Studio 2026's unified settings service (its interop type is internal; the GUID returns <c>ISettingsManager</c>).</summary>
        [System.Runtime.InteropServices.Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
        private interface SVsUnifiedSettingsManager
        {
        }    }
}
