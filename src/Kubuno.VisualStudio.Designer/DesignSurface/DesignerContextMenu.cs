using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// Command ids of the design surface's context menus, in the package's command set
    /// (<c>guidKubunoCommandsPackageCmdSet</c>) - they MUST match <c>KubunoCommands.vsct</c> in the VSIX project.
    /// The clipboard, "View Code" and z-order items are Visual Studio's own standard commands
    /// (<c>guidVSStd97</c>: Cut/Copy/Paste/Delete/ViewCode/BringToFront/SendToBack), placed in these menus so they
    /// show VS's own localized text and shortcut.
    /// </summary>
    public static class DesignerCommandIds
    {
        public static readonly Guid CommandSet = new Guid("5A67B8C7-E9FD-4917-B844-F103C0485DE1");

        public const int ElementContextMenu = 0x1030;
        public const int ViewContextMenu = 0x1031;
        public const int CreateHandlerMenu = 0x1032;
        public const int SelectMenu = 0x1033;
        public const int WrapMenu = 0x1034;

        /// <summary>The Layout submenus (docs/DESIGNER.md §13): Align ›, Make Same Size ›, Horizontal Spacing ›, Vertical Spacing ›, Center in View ›.</summary>
        public const int AlignMenu = 0x1035;
        public const int SizeMenu = 0x1036;
        public const int HorizontalSpacingMenu = 0x1037;
        public const int VerticalSpacingMenu = 0x1038;
        public const int CenterMenu = 0x1039;

        /// <summary>The designer's Layout toolbar (shown while a .kbview designer is active).</summary>
        public const int LayoutToolbar = 0x1050;

        public const int Duplicate = 0x0200;
        public const int Properties = 0x0201;
        public const int Unwrap = 0x0202;
        public const int ViewProperties = 0x0203;
        public const int DesignSize = 0x0204;

        /// <summary>"Wrap in" › Stack, Panel, Card, GroupBox, ScrollArea (<see cref="DesignerStructurePlanner.WrapContainers"/> order).</summary>
        public const int WrapFirst = 0x0210;

        /// <summary>First dynamic item of "Create Handler" › (one per event of the element).</summary>
        public const int CreateHandlerFirst = 0x0220;

        /// <summary>First dynamic item of "Select" › (one per ancestor).</summary>
        public const int SelectFirst = 0x0240;

        /// <summary>How many dynamic items each list may show.</summary>
        public const int MaxDynamicItems = 32;
    }

    /// <summary>What the design surface's context menu shows for one element (or for the view itself) - pure, unit-tested.</summary>
    public sealed class DesignerMenuModel
    {
        private DesignerMenuModel(string? elementId)
        {
            ElementId = elementId;
        }

        /// <summary>The element (<c>""</c> = the root element), or null: the view menu (canvas background, frame title).</summary>
        public string? ElementId { get; }

        public bool IsView => ElementId is null;

        public string TagName { get; private set; } = string.Empty;

        /// <summary>The element's registry events (<c>OnClick</c>, ...), for "Create Handler" ›.</summary>
        public IReadOnlyList<string> Events { get; private set; } = Array.Empty<string>();

        /// <summary>The element's containers, nearest first, for "Select" ›.</summary>
        public IReadOnlyList<(string Id, string Label)> Ancestors { get; private set; } = Array.Empty<(string, string)>();

        /// <summary>"Wrap in" › entries the registry knows, each with whether it is allowed here.</summary>
        public IReadOnlyList<(string Container, bool Allowed)> WrapOptions { get; private set; } = Array.Empty<(string, bool)>();

        public bool CanCopy { get; private set; }

        /// <summary>Cut/Delete: any element but the root.</summary>
        public bool CanRemove { get; private set; }

        public bool CanDuplicate { get; private set; }

        public bool CanPaste { get; private set; }

        public bool CanUnwrap { get; private set; }

        public int? FrontIndex { get; private set; }

        public int? BackIndex { get; private set; }

        /// <summary>The elements the menu applies to (docs/DESIGNER.md §13): the whole multi-selection when the menu's element is part of it, else that element alone; empty for the view.</summary>
        public IReadOnlyList<string> SelectedIds { get; private set; } = Array.Empty<string>();

        /// <summary>Whether the menu applies to a multi-selection.</summary>
        public bool IsMultiple => SelectedIds.Count > 1;

        /// <summary>What the Layout commands (Align ›, Make Same Size ›, spacing, centering, z-order) can do with <see cref="SelectedIds"/>.</summary>
        public LayoutSelectionInfo Layout { get; private set; } = LayoutSelectionInfo.Empty;

        /// <summary>
        /// The model for <paramref name="elementId"/> (null = the view) against the current
        /// <paramref name="text"/>; <paramref name="clipboardTag"/> is the tag of the element on the clipboard, if any.
        /// <paramref name="selectedIds"/> is the current selection (primary first): when it holds
        /// <paramref name="elementId"/> and more, the menu applies to all of it (WinForms: a right-click on a
        /// selected control keeps the multi-selection).
        /// </summary>
        public static DesignerMenuModel Build(string text, string? elementId, ComponentRegistry registry, string? clipboardTag, IReadOnlyList<string>? selectedIds = null)
        {
            var model = new DesignerMenuModel(elementId);
            model.CanPaste = clipboardTag is not null && DesignerStructurePlanner.PlanPaste(text, elementId, clipboardTag, registry) is not null;
            if (elementId is null || ElementAttributeReader.Read(text, elementId) is not { } element)
            {
                return model;
            }

            model.SelectedIds = selectedIds is { Count: > 1 } && selectedIds.Contains(elementId) ? selectedIds : new[] { elementId };
            model.Layout = LayoutSelectionInfo.Build(text, model.SelectedIds, elementId, registry);
            if (model.IsMultiple)
            {
                // A multi-selection: the clipboard, delete, duplicate and layout commands apply to all of it; the
                // single-element gestures (handlers, wrap, unwrap, select a container) are not offered.
                model.TagName = element.TagName;
                model.CanCopy = true;
                model.CanRemove = MultiSelection.TopLevel(model.SelectedIds).Count > 0;
                model.CanDuplicate = DesignerStructurePlanner.PlanDuplicateMany(text, model.SelectedIds, elementId, registry) is not null;
                return model;
            }

            model.TagName = element.TagName;
            model.Events = registry.Find(element.TagName)?.Events.Select(e => e.Name).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
            model.Ancestors = DesignerStructurePlanner.Ancestors(text, elementId);
            model.WrapOptions = DesignerStructurePlanner.WrapContainers
                .Where(c => registry.Find(c) is not null)
                .Select(c => (c, DesignerStructurePlanner.CanWrap(text, elementId, c, registry)))
                .ToList();
            model.CanCopy = true;
            model.CanRemove = elementId.Length > 0;
            model.CanDuplicate = DesignerStructurePlanner.PlanDuplicate(text, elementId, registry) is not null;
            model.CanUnwrap = DesignerStructurePlanner.CanUnwrap(text, elementId, registry);
            model.FrontIndex = DesignerStructurePlanner.BringToFrontIndex(text, elementId);
            model.BackIndex = DesignerStructurePlanner.SendToBackIndex(text, elementId);
            return model;
        }
    }

    /// <summary>What the context menu's commands do - implemented by <see cref="DesignSurfaceEditingCoordinator"/>.</summary>
    public interface IDesignerMenuActions
    {
        void ViewCode();

        void CreateHandler(string elementId, string eventName);

        void Cut(string elementId);

        void Copy(string elementId);

        /// <summary>Pastes onto <paramref name="targetId"/> (null = the view's root).</summary>
        void Paste(string? targetId);

        void Duplicate(string elementId);

        void Delete(string elementId);

        void SelectElement(string elementId);

        void MoveWithinParent(string elementId, int index);

        void Wrap(string elementId, string container);

        void Unwrap(string elementId);

        /// <summary>Selects <paramref name="elementId"/> (null = the view) and shows the Properties window.</summary>
        void ShowProperties(string? elementId);

        void EditDesignSize();

        /// <summary>A Layout toolbar / Format menu command on the current selection (docs/DESIGNER.md §13).</summary>
        void RunLayoutCommand(DesignerLayoutCommand command);
    }

    /// <summary>
    /// The command target a design-surface context menu is shown with (<c>IVsUIShell.ShowContextMenu</c>): answers
    /// every item's state (and, for our own commands, its text in Visual Studio's UI language - the <c>.vsct</c>
    /// carries the English text) from a <see cref="DesignerMenuModel"/>, and runs the picked one through
    /// <see cref="IDesignerMenuActions"/>. The dynamic lists ("Create Handler" ›, "Select" ›) follow the
    /// <c>DynamicItemStart</c> protocol: item <c>first + i</c> is supported while <c>i</c> is in range.
    /// </summary>
    public sealed class DesignerContextMenuCommandTarget : OleInterop.IOleCommandTarget
    {
        // guidVSStd97 and its command ids (stdidcmd.h) - spelled out rather than taken from VSConstants, whose
        // assembly (Microsoft.VisualStudio.Shell.Framework) this type must not need, so it stays unit-testable.
        public static readonly Guid StandardCommandSet97 = new Guid("5EFC7975-14BC-11CF-9B2B-00AA00573819");
        public const uint Std97BringToFront = 11;
        public const uint Std97Copy = 15;
        public const uint Std97Cut = 16;
        public const uint Std97Delete = 17;
        public const uint Std97Paste = 26;
        public const uint Std97SendToBack = 33;
        public const uint Std97ViewCode = 333;
        private const int SOk = 0;

        private readonly DesignerMenuModel _model;
        private readonly IDesignerMenuActions _actions;

        public DesignerContextMenuCommandTarget(DesignerMenuModel model, IDesignerMenuActions actions)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OleInterop.OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (prgCmds is null || cCmds == 0)
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            for (var i = 0; i < cCmds && i < prgCmds.Length; i++)
            {
                var state = pguidCmdGroup == StandardCommandSet97 ? StandardState(prgCmds[i].cmdID)
                    : pguidCmdGroup == DesignerCommandIds.CommandSet ? OwnState((int)prgCmds[i].cmdID)
                    : null;
                if (state is not { } s)
                {
                    return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }

                var flags = OleInterop.OLECMDF.OLECMDF_SUPPORTED;
                if (s.Enabled)
                {
                    flags |= OleInterop.OLECMDF.OLECMDF_ENABLED;
                }

                if (!s.Visible)
                {
                    flags |= OleInterop.OLECMDF.OLECMDF_INVISIBLE | OleInterop.OLECMDF.OLECMDF_DEFHIDEONCTXTMENU;
                }

                prgCmds[i].cmdf = (uint)flags;
                if (s.Text is not null && i == 0)
                {
                    SetCommandText(pCmdText, s.Text);
                }
            }

            return SOk;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            var id = _model.ElementId;
            if (pguidCmdGroup == StandardCommandSet97)
            {
                if (StandardState(nCmdID) is not { Enabled: true })
                {
                    return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
                }

                switch (nCmdID)
                {
                    case Std97ViewCode: _actions.ViewCode(); break;
                    case Std97Cut: _actions.Cut(id!); break;
                    case Std97Copy: _actions.Copy(id!); break;
                    case Std97Paste: _actions.Paste(id); break;
                    case Std97Delete: _actions.Delete(id!); break;
                    case Std97BringToFront when !_model.IsMultiple: _actions.MoveWithinParent(id!, _model.FrontIndex!.Value); break;
                    case Std97SendToBack when !_model.IsMultiple: _actions.MoveWithinParent(id!, _model.BackIndex!.Value); break;
                    default:
                        if (DesignerLayoutCommands.TryFromStandardCommand(nCmdID, out var layout))
                        {
                            _actions.RunLayoutCommand(layout);
                        }

                        break;
                }

                return SOk;
            }

            if (pguidCmdGroup != DesignerCommandIds.CommandSet || OwnState((int)nCmdID) is not { Enabled: true, Visible: true })
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            var cmd = (int)nCmdID;
            switch (cmd)
            {
                case DesignerCommandIds.Duplicate: _actions.Duplicate(id!); break;
                case DesignerCommandIds.Properties: _actions.ShowProperties(id); break;
                case DesignerCommandIds.Unwrap: _actions.Unwrap(id!); break;
                case DesignerCommandIds.ViewProperties: _actions.ShowProperties(null); break;
                case DesignerCommandIds.DesignSize: _actions.EditDesignSize(); break;
                default:
                    if (Index(cmd, DesignerCommandIds.WrapFirst, DesignerStructurePlanner.WrapContainers.Count) is { } wrap)
                    {
                        _actions.Wrap(id!, DesignerStructurePlanner.WrapContainers[wrap]);
                    }
                    else if (Index(cmd, DesignerCommandIds.CreateHandlerFirst, _model.Events.Count) is { } handler)
                    {
                        _actions.CreateHandler(id!, _model.Events[handler]);
                    }
                    else if (Index(cmd, DesignerCommandIds.SelectFirst, _model.Ancestors.Count) is { } ancestor)
                    {
                        _actions.SelectElement(_model.Ancestors[ancestor].Id);
                    }

                    break;
            }

            return SOk;
        }

        /// <summary>State of a <c>guidVSStd97</c> command in these menus; null for one they do not contain.</summary>
        private CommandState? StandardState(uint cmdId)
        {
            switch (cmdId)
            {
                case Std97ViewCode: return new CommandState(true);
                case Std97Cut: return new CommandState(_model.CanRemove);
                case Std97Copy: return new CommandState(_model.CanCopy);
                case Std97Paste: return new CommandState(_model.CanPaste);
                case Std97Delete: return new CommandState(_model.CanRemove);
                case Std97BringToFront when !_model.IsMultiple: return new CommandState(_model.FrontIndex is not null);
                case Std97SendToBack when !_model.IsMultiple: return new CommandState(_model.BackIndex is not null);
                default:
                    // The Layout commands (docs/DESIGNER.md §13) - and z-order on a multi-selection.
                    return DesignerLayoutCommands.TryFromStandardCommand(cmdId, out var layout) ? new CommandState(!_model.IsView && _model.Layout.IsEnabled(layout)) : null;
            }
        }

        /// <summary>State (and localized text) of one of our own commands or submenus; null for an unknown id.</summary>
        private CommandState? OwnState(int cmdId)
        {
            switch (cmdId)
            {
                case DesignerCommandIds.CreateHandlerMenu: return new CommandState(true, _model.Events.Count > 0, DesignerText.MenuCreateHandler);
                case DesignerCommandIds.SelectMenu: return new CommandState(true, _model.Ancestors.Count > 0, DesignerText.MenuSelect);
                case DesignerCommandIds.WrapMenu: return new CommandState(_model.WrapOptions.Any(o => o.Allowed), _model.WrapOptions.Count > 0, DesignerText.MenuWrapIn);
                case DesignerCommandIds.Duplicate: return new CommandState(_model.CanDuplicate, true, DesignerText.MenuDuplicate);
                case DesignerCommandIds.Properties: return new CommandState(!_model.IsView, true, DesignerText.MenuProperties);
                case DesignerCommandIds.Unwrap: return new CommandState(_model.CanUnwrap, true, DesignerText.MenuUnwrap);
                case DesignerCommandIds.ViewProperties: return new CommandState(true, true, DesignerText.MenuViewProperties);
                case DesignerCommandIds.DesignSize: return new CommandState(true, true, DesignerText.MenuDesignSize);
                case DesignerCommandIds.AlignMenu: return LayoutMenu(DesignerText.MenuAlign, DesignerLayoutCommand.AlignLefts, DesignerLayoutCommand.AlignBottoms);
                case DesignerCommandIds.SizeMenu: return LayoutMenu(DesignerText.MenuMakeSameSize, DesignerLayoutCommand.MakeSameWidth, DesignerLayoutCommand.MakeSameSize);
                case DesignerCommandIds.HorizontalSpacingMenu: return LayoutMenu(DesignerText.MenuHorizontalSpacing, DesignerLayoutCommand.HorizontalSpacingEqual, DesignerLayoutCommand.HorizontalSpacingRemove);
                case DesignerCommandIds.VerticalSpacingMenu: return LayoutMenu(DesignerText.MenuVerticalSpacing, DesignerLayoutCommand.VerticalSpacingEqual, DesignerLayoutCommand.VerticalSpacingRemove);
                case DesignerCommandIds.CenterMenu: return LayoutMenu(DesignerText.MenuCenterInView, DesignerLayoutCommand.CenterHorizontally, DesignerLayoutCommand.CenterVertically);
            }

            if (Index(cmdId, DesignerCommandIds.WrapFirst, DesignerStructurePlanner.WrapContainers.Count) is { } wrap)
            {
                var container = DesignerStructurePlanner.WrapContainers[wrap];
                var option = _model.WrapOptions.FirstOrDefault(o => o.Container == container);
                return new CommandState(option.Allowed, option.Container is not null, container);
            }

            if (Index(cmdId, DesignerCommandIds.CreateHandlerFirst, DesignerCommandIds.MaxDynamicItems) is { } handler)
            {
                return DynamicItem(handler, _model.Events.Count, i => _model.Events[i]);
            }

            if (Index(cmdId, DesignerCommandIds.SelectFirst, DesignerCommandIds.MaxDynamicItems) is { } ancestor)
            {
                return DynamicItem(ancestor, _model.Ancestors.Count, i => _model.Ancestors[i].Label);
            }

            return null;
        }

        /// <summary>
        /// A <c>DynamicItemStart</c> list item: shown while in range; past the end, the first placeholder is hidden
        /// (an empty list) and later ids are unsupported, which ends the list.
        /// </summary>
        private static CommandState? DynamicItem(int index, int count, Func<int, string> text)
        {
            if (index < count)
            {
                return new CommandState(true, true, text(index));
            }

            return index == 0 ? new CommandState(false, false, string.Empty) : null;
        }

        /// <summary>A Layout submenu: shown on an element's menu, enabled when one of its commands (<paramref name="first"/>..<paramref name="last"/>) is.</summary>
        private CommandState LayoutMenu(string text, DesignerLayoutCommand first, DesignerLayoutCommand last) =>
            new CommandState(
                Enumerable.Range((int)first, (int)last - (int)first + 1).Any(c => _model.Layout.IsEnabled((DesignerLayoutCommand)c)),
                !_model.IsView,
                text);

        private static int? Index(int cmdId, int first, int count) => cmdId >= first && cmdId < first + count ? cmdId - first : null;

        /// <summary>Writes <paramref name="text"/> into an <c>OLECMDTEXT</c> asking for the command's name.</summary>
        private static void SetCommandText(IntPtr pCmdText, string text)
        {
            if (pCmdText == IntPtr.Zero)
            {
                return;
            }

            // OLECMDTEXT: DWORD cmdtextf; ULONG cwActual; ULONG cwBuf; WCHAR rgwz[cwBuf].
            var flags = (uint)Marshal.ReadInt32(pCmdText, 0);
            if ((flags & (uint)OleInterop.OLECMDTEXTF.OLECMDTEXTF_NAME) == 0)
            {
                return;
            }

            var capacity = Marshal.ReadInt32(pCmdText, 8);
            if (capacity <= 0)
            {
                return;
            }

            var chars = text.ToCharArray();
            var count = Math.Min(chars.Length, capacity - 1);
            var buffer = IntPtr.Add(pCmdText, 12);
            Marshal.Copy(chars, 0, buffer, count);
            Marshal.WriteInt16(buffer, count * 2, 0);
            Marshal.WriteInt32(pCmdText, 4, count + 1);
        }

        /// <summary>One command's state: enabled, visible, and (for our own commands) its text.</summary>
        private readonly struct CommandState
        {
            public CommandState(bool enabled, bool visible = true, string? text = null)
            {
                Enabled = enabled;
                Visible = visible;
                Text = text;
            }

            public bool Enabled { get; }

            public bool Visible { get; }

            public string? Text { get; }
        }
    }
}
