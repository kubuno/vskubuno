using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;

namespace Kubuno.VisualStudio.Views.Options
{
    /// <summary>
    /// The Visual Studio service of Visual Studio 2026's unified settings (<c>SVsUnifiedSettingsManager</c>, an internal
    /// interop type): querying it by its GUID returns the public <see cref="ISettingsManager"/>.
    /// </summary>
    [Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
    internal interface SVsUnifiedSettingsManager
    {
    }

    /// <summary>
    /// Binds a <see cref="DialogPage"/> property to a setting of <c>UnifiedSettings/kubuno.registration.json</c>
    /// (Tools &gt; Options &gt; Kubuno, the document-style settings page of Visual Studio 2026).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UnifiedSettingAttribute : Attribute
    {
        public UnifiedSettingAttribute(string moniker)
        {
            Moniker = moniker;
        }

        /// <summary>The setting's moniker in the registration manifest (<c>kubuno.rust.inlayHints.show</c>).</summary>
        public string Moniker { get; }
    }

    /// <summary>
    /// Reads and writes the properties of an options page (marked with <see cref="UnifiedSettingAttribute"/>) in Visual
    /// Studio's unified settings store, and follows the changes made in the settings page or in the JSON file. Enums are
    /// stored as camelCase strings (<c>whilePressingAltF1</c>). Everything degrades to "not available" on Visual Studio
    /// versions without unified settings, where the pages keep using their classic storage.
    /// </summary>
    internal static class KubunoUnifiedSettings
    {
        private const string Requester = "Kubuno";

        private static readonly MethodInfo EnqueueChange = typeof(ISettingsWriter).GetMethods().First(m => m.Name == "EnqueueChange" && m.GetParameters().Length == 2);

        /// <summary>The unified settings manager, or null when this Visual Studio does not have one.</summary>
        internal static ISettingsManager? GetManager()
        {
            try
            {
                return Package.GetGlobalService(typeof(SVsUnifiedSettingsManager)) as ISettingsManager;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static IEnumerable<(PropertyInfo Property, string Moniker)> Bound(Type pageType) =>
            pageType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (Property: p, Attribute: (UnifiedSettingAttribute?)Attribute.GetCustomAttribute(p, typeof(UnifiedSettingAttribute))))
                .Where(x => x.Attribute is not null)
                .Select(x => (x.Property, x.Attribute!.Moniker));

        /// <summary>The camelCase name a unified setting uses for an enum member.</summary>
        internal static string EnumToString(Enum value)
        {
            string name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        internal static object? ToPropertyValue(Type type, object? stored)
        {
            if (stored is null)
            {
                return null;
            }

            if (type.IsEnum)
            {
                string text = Convert.ToString(stored, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                return Enum.GetNames(type).Where(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase))
                    .Select(n => Enum.Parse(type, n)).Cast<object?>().FirstOrDefault();
            }

            try
            {
                return Convert.ChangeType(stored, type, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static object ToStoredValue(object value) =>
            value is Enum e ? EnumToString(e) : value;

        /// <summary>
        /// Sets the page's bound properties from the store. Returns false (and touches nothing) when the store is not
        /// available or is in classic mode; a setting the store cannot give keeps the property's current value.
        /// </summary>
        internal static bool TryLoad(DialogPage page)
        {
            var manager = GetManager();
            if (manager is null)
            {
                return false;
            }

            ISettingsReader reader;
            try
            {
                reader = manager.GetReader();
            }
            catch (Exception)
            {
                return false;
            }

            bool any = false;
            foreach (var (property, moniker) in Bound(page.GetType()))
            {
                SettingRetrieval<object> result;
                try
                {
                    result = reader.GetValue(moniker, property.PropertyType.IsEnum ? typeof(string) : property.PropertyType, SettingReadOptions.NoRequirements);
                }
                catch (Exception)
                {
                    continue;
                }

                if (result.Outcome == SettingRetrievalOutcome.NotSupportedInClassicMode)
                {
                    return false;
                }

                if (result.Outcome != SettingRetrievalOutcome.Success)
                {
                    continue;
                }

                var value = ToPropertyValue(property.PropertyType, result.Value);
                if (value is not null)
                {
                    property.SetValue(page, value);
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Writes the given properties (all bound ones when <paramref name="onlyDifferentFromDefault"/> is false, else those whose value differs from their <see cref="DefaultValueAttribute"/>) to the store.</summary>
        internal static void Store(DialogPage page, bool onlyDifferentFromDefault)
        {
            var manager = GetManager();
            if (manager is null)
            {
                return;
            }

            try
            {
                var writer = manager.GetWriter(Requester);
                bool queued = false;
                foreach (var (property, moniker) in Bound(page.GetType()))
                {
                    var value = property.GetValue(page);
                    if (value is null)
                    {
                        continue;
                    }

                    if (onlyDifferentFromDefault)
                    {
                        var defaultAttribute = (DefaultValueAttribute?)Attribute.GetCustomAttribute(property, typeof(DefaultValueAttribute));
                        object defaultValue = defaultAttribute?.Value ?? (property.PropertyType == typeof(string) ? string.Empty : Activator.CreateInstance(property.PropertyType)!);
                        if (Equals(value, defaultValue))
                        {
                            continue;
                        }
                    }

                    var stored = ToStoredValue(value);
                    EnqueueChange.MakeGenericMethod(stored.GetType()).Invoke(writer, new[] { moniker, stored });
                    queued = true;
                }

                if (queued)
                {
                    writer.RequestCommit("Kubuno settings");
                }
            }
            catch (Exception)
            {
                // The store refused (classic mode, policy): the classic storage keeps the values.
            }
        }

        /// <summary>Calls <paramref name="changed"/> when one of the page's bound settings changes in the store (settings page, JSON file). Dispose to stop.</summary>
        internal static IDisposable? Subscribe(DialogPage page, Action changed)
        {
            var manager = GetManager();
            if (manager is null)
            {
                return null;
            }

            try
            {
                var monikers = Bound(page.GetType()).Select(b => b.Moniker).ToArray();
                return manager.GetReader().SubscribeToChanges(_ => changed(), monikers);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
