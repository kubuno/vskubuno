using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Core.DevAssistant.UI
{
    /// <summary>
    /// View &gt; Other Windows &gt; « Assistant de développement Kubuno » (docs/AI-ASSISTANT.md section 5.1): a single-instance
    /// tool window docked with Solution Explorer, registered by <c>KubunoPackage</c>'s <c>[ProvideToolWindow]</c>.
    /// </summary>
    [Guid(DevAssistantGuids.ToolWindowString)]
    public sealed class DevAssistantToolWindow : ToolWindowPane
    {
        private AssistantControl? _control;

        public DevAssistantToolWindow()
            : base(null)
        {
            Caption = AssistantText.WindowTitle;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _control = new AssistantControl(ThreadHelper.JoinableTaskFactory);
            Content = _control;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _control?.Dispose();
                _control = null;
            }

            base.Dispose(disposing);
        }
    }
}
