using System;
using System.Linq;
using System.Reflection;

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>
    /// The icon picker's door to the project's resource files (<c>.kbres</c>, docs/RESOURCES.md): the resources editor's
    /// <c>Kubuno.Views.Designer.Resources.ResourcePicker.Pick(viewFile, current, ResourceKindFilter.Icons)</c>, which shows
    /// its Select Resource dialog and answers the attribute value to write (<c>{Res key}</c> or a relative path). Found by
    /// name, so the picker works whether or not that editor is part of this build; <see cref="IsAvailable"/> says whether it is.
    /// </summary>
    internal static class ProjectResourceBridge
    {
        private const string PickerType = "Kubuno.Views.Designer.Resources.ResourcePicker";

        private static readonly Lazy<MethodInfo?> Pick = new Lazy<MethodInfo?>(() =>
            typeof(ProjectResourceBridge).Assembly.GetType(PickerType, throwOnError: false)?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Pick" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType.IsEnum));

        public static bool IsAvailable => Pick.Value is not null;

        /// <summary>Shows the Select Resource dialog for icons; the value to write, or null when cancelled or unavailable.</summary>
        public static string? PickIcon(string viewFile, string? current)
        {
            var method = Pick.Value;
            if (method is null)
            {
                return null;
            }

            var filterType = method.GetParameters()[2].ParameterType;
            var filter = Enum.GetNames(filterType).Contains("Icons") ? Enum.Parse(filterType, "Icons") : Enum.ToObject(filterType, 0);
            try
            {
                return method.Invoke(null, new object?[] { viewFile, current ?? string.Empty, filter }) as string;
            }
            catch (TargetInvocationException ex)
            {
                Kubuno.Views.Logging.KubunoViewsLogHost.Current.WriteException("[designer] the Select Resource dialog failed", ex.InnerException ?? ex);
                return null;
            }
        }
    }
}
