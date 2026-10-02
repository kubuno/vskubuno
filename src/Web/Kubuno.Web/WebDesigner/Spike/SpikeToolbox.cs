using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Kubuno.Shared.Logging;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 2): the spike's Toolbox tab, filled while at least one spike pane is open.
    /// Each item's data object carries the desktop Toolbox items' private format (<c>Kubuno.Views.ToolboxItem</c> = the
    /// element name in UTF-8, what the desktop designer's items carry) and, for the HTML5 channel, <c>CF_UNICODETEXT</c>
    /// <c>kubuno-toolbox:&lt;Name&gt;</c> (what Chromium exposes to the page as <c>text/plain</c>).
    /// </summary>
    internal static class SpikeToolbox
    {
        private static readonly List<OleDataObject> s_items = new List<OleDataObject>();
        private static int s_users;

        public static void AddUser()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (s_users++ > 0 || Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                return;
            }

            try
            {
                toolbox.AddTab(WebDesignSpikeConstants.ToolboxTab);
                foreach (var element in SpikeElementCatalog.Elements)
                {
                    var data = new OleDataObject();
                    data.SetData(WebDesignSpikeConstants.ToolboxItemFormat, new MemoryStream(Encoding.UTF8.GetBytes(element.Name)));
                    data.SetData(System.Windows.Forms.DataFormats.UnicodeText, SpikeElementCatalog.ToolboxText(element.Name));
                    var info = new TBXITEMINFO { bstrText = element.Name, dwFlags = (uint)__TBXITEMINFOFLAGS.TBXIF_DONTPERSIST };
                    if (ErrorHandler.Succeeded(toolbox.AddItem(data, new[] { info }, WebDesignSpikeConstants.ToolboxTab)))
                    {
                        s_items.Add(data);
                    }
                }

                toolbox.UpdateToolboxUI();
                KubunoLog.WriteLine($"[web-spike] Toolbox: {s_items.Count} item(s) in '{WebDesignSpikeConstants.ToolboxTab}'.");
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
            {
                KubunoLog.WriteException("[web-spike] Toolbox: could not add the spike items", ex);
            }
        }

        public static void RemoveUser()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (s_users == 0 || --s_users > 0 || Package.GetGlobalService(typeof(SVsToolbox)) is not IVsToolbox toolbox)
            {
                return;
            }

            try
            {
                foreach (var item in s_items)
                {
                    toolbox.RemoveItem(item);
                }

                s_items.Clear();
                toolbox.RemoveTab(WebDesignSpikeConstants.ToolboxTab);
                toolbox.UpdateToolboxUI();
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                KubunoLog.WriteException("[web-spike] Toolbox: could not remove the spike items", ex);
            }
        }
    }
}
