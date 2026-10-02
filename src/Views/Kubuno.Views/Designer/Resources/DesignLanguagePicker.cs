using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Resources;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.Resources
{
    /// <summary>
    /// The design-time language of the designer (docs/RESOURCES.md), in the Design | XML | Split strip: "(Default)" (the
    /// neutral values) or one of the cultures of the project's resource files - the view is previewed with that culture's
    /// <c>{Res …}</c> values and pictures. Like the Language property of a Windows Forms form in the designer, except that
    /// it changes nothing in the files: it is a preview.
    /// </summary>
    internal static class DesignLanguagePicker
    {
        /// <summary>Adds the picker at the end of <paramref name="strip"/> when <paramref name="host"/> is a real design surface.</summary>
        public static void Attach(Panel strip, IDesignSurfaceHost host)
        {
            if (host is not IProtocolDesignSurfaceHost surface)
            {
                return;
            }

            var label = new TextBlock { Text = ResourceText.DesignLanguage, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            var combo = new ComboBox { MinWidth = 110, Margin = new Thickness(0, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = ResourceText.DesignLanguageTip };
            combo.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ComboBoxStyleKey);
            System.Windows.Automation.AutomationProperties.SetName(combo, ResourceText.DesignLanguage.TrimEnd(':', ' '));

            var updating = false;
            void Fill()
            {
                updating = true;
                var current = surface.DesignCulture;
                combo.Items.Clear();
                combo.Items.Add(new ComboBoxItem { Content = ResourceText.DefaultLanguage, Tag = string.Empty });
                foreach (var culture in surface.ProjectCultures())
                {
                    combo.Items.Add(new ComboBoxItem { Content = Display(culture), Tag = culture });
                }

                combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? combo.Items[0];
                updating = false;
            }

            Fill();
            combo.DropDownOpened += (_, _) => Fill();
            // Raised on the UI thread (RustDesignSurfaceHost switches to it).
            surface.ResourcesChanged += (_, _) => Fill();
            combo.SelectionChanged += (_, _) =>
            {
                if (!updating && combo.SelectedItem is ComboBoxItem { Tag: string culture })
                {
                    surface.SetDesignCulture(culture);
                }
            };

            strip.Children.Add(label);
            strip.Children.Add(combo);
        }

        /// <summary><c>fr-FR</c> → <c>fr-FR · Français (France)</c> when .NET knows the culture.</summary>
        private static string Display(string culture)
        {
            try
            {
                var info = System.Globalization.CultureInfo.GetCultureInfo(culture);
                return culture + " · " + info.NativeName;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                return culture;
            }
        }
    }
}
