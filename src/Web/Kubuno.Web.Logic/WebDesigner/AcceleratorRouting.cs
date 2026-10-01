namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>Who handles a key pressed while the WebView2 design surface has the keyboard focus.</summary>
    public enum KeyRoute
    {
        /// <summary>The page (the key is not shown to Visual Studio).</summary>
        Page,

        /// <summary>Visual Studio's key bindings (<c>IVsFilterKeys2.TranslateAcceleratorEx</c>); the page gets it only when Visual Studio has no binding for it.</summary>
        VisualStudio,
    }

    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 1): which keys of the WebView2 design surface go to Visual Studio. A
    /// key typed in the WebView2 is read by the browser process's input thread and never enters Visual Studio's message
    /// loop, so Visual Studio's key bindings (Ctrl+S, F4, F7, Ctrl+Shift+B, Edit.Undo on Ctrl+Z...) would be dead while
    /// the page has the focus. WebView2 reports every accelerator key (a chord with Ctrl or Alt, or a key that types no
    /// character: F-keys, Del, arrows, Escape...) to the host before the page sees it (<c>AcceleratorKeyPressed</c>,
    /// raised by the WPF control as <c>PreviewKeyDown</c>); this table decides, from the key and whether the page is
    /// editing text, whether the host hands it to Visual Studio. Plain character keys are not accelerators: typing always
    /// reaches the page.
    /// </summary>
    public static class AcceleratorRouting
    {
        // Win32 virtual-key codes.
        private const int VkBack = 0x08;
        private const int VkTab = 0x09;
        private const int VkReturn = 0x0D;
        private const int VkEscape = 0x1B;
        private const int VkPrior = 0x21;
        private const int VkNext = 0x22;
        private const int VkEnd = 0x23;
        private const int VkHome = 0x24;
        private const int VkLeft = 0x25;
        private const int VkUp = 0x26;
        private const int VkRight = 0x27;
        private const int VkDown = 0x28;
        private const int VkInsert = 0x2D;
        private const int VkDelete = 0x2E;
        private const int VkF2 = 0x71;

        /// <summary>
        /// The route of a key-down. <paramref name="pageEditingText"/>: a text editor of the page (inline text editing
        /// of a label, a property field drawn by the page) has the focus - it then keeps the text-editing keys (Del,
        /// Backspace, Ctrl+Z/Y, Ctrl+A/C/X/V, caret moves, Enter, Escape, Tab). Otherwise the design surface keeps only
        /// its own navigation keys (Tab, arrows, Enter, Escape, F2) and everything else is Visual Studio's: Del is
        /// Edit.Delete, Ctrl+Z/Y are Edit.Undo/Redo (the document's text undo), Ctrl+C/X/V the designer's clipboard.
        /// Chords with Alt (menu access keys) and the function keys other than F2 always go to Visual Studio.
        /// </summary>
        public static KeyRoute Route(int virtualKey, bool control, bool shift, bool alt, bool pageEditingText)
        {
            if (alt)
            {
                return KeyRoute.VisualStudio;
            }

            if (pageEditingText)
            {
                return IsTextEditingKey(virtualKey, control) ? KeyRoute.Page : KeyRoute.VisualStudio;
            }

            if (control)
            {
                // Ctrl+Tab / Ctrl+Shift+Tab: Visual Studio's window switcher; Ctrl+arrows: nothing of the page's.
                return KeyRoute.VisualStudio;
            }

            switch (virtualKey)
            {
                case VkTab:
                case VkReturn:
                case VkEscape:
                case VkLeft:
                case VkUp:
                case VkRight:
                case VkDown:
                case VkF2:
                    return KeyRoute.Page;
                default:
                    return KeyRoute.VisualStudio;
            }
        }

        private static bool IsTextEditingKey(int virtualKey, bool control)
        {
            if (control)
            {
                switch (virtualKey)
                {
                    case 'A':
                    case 'C':
                    case 'V':
                    case 'X':
                    case 'Y':
                    case 'Z':
                    case VkBack:
                    case VkDelete:
                    case VkLeft:
                    case VkRight:
                    case VkHome:
                    case VkEnd:
                    case VkInsert:
                        return true;
                    default:
                        return false;
                }
            }

            switch (virtualKey)
            {
                case VkBack:
                case VkTab:
                case VkReturn:
                case VkEscape:
                case VkPrior:
                case VkNext:
                case VkEnd:
                case VkHome:
                case VkLeft:
                case VkUp:
                case VkRight:
                case VkDown:
                case VkInsert:
                case VkDelete:
                    return true;
                default:
                    return false;
            }
        }
    }
}
