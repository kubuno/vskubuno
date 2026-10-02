using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Desktop.Designer.Selection;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Designer.DesignSurface
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

        /// <summary>"Convert to Typed Handlers" (docs/EVENTS.md §5.4, EVT-5): the view menu's <c>kubuno/convertHandlers</c>.</summary>
        public const int ConvertHandlers = 0x0205;

        /// <summary>"Choose Toolbox Items" (docs/EVENTS.md EVT-7b): the view menu's choice of other crates' controls for the Toolbox's project tab.</summary>
        public const int ChooseToolboxItems = 0x0206;

        /// <summary>"Wrap in" › Stack, Panel, Card, GroupBox, ScrollArea (<see cref="DesignerStructurePlanner.WrapContainers"/> order).</summary>
        public const int WrapFirst = 0x0210;

        /// <summary>First dynamic item of "Create Handler" › (one per event of the element).</summary>
        public const int CreateHandlerFirst = 0x0220;

        /// <summary>First dynamic item of "Select" › (one per ancestor).</summary>
        public const int SelectFirst = 0x0240;

        /// <summary>How many dynamic items each list may show.</summary>
        public const int MaxDynamicItems = 32;

        /// <summary>A ribbon element's smart tag menu, and the menu of a ribbon's "+" glyph (docs/RIBBON.md section 9).</summary>
        public const int RibbonTasksMenu = 0x1100;

        /// <summary>"Add" › on a ribbon element's context menu and smart tag.</summary>
        public const int RibbonAddMenu = 0x1101;

        public const int RibbonAddContextMenu = 0x1102;

        /// <summary>"Create Command from this Button": a <c>Command</c> holding the element's label, icons and screen tip.</summary>
        public const int RibbonCreateCommand = 0x0600;

        /// <summary>"Edit Items..." / "Edit Groups..." / "Edit Tabs...": the element's polymorphic collection editor.</summary>
        public const int RibbonEditItems = 0x0601;

        /// <summary>"Choose Icon...": the icon picker on the element's icon attribute.</summary>
        public const int RibbonChooseIcon = 0x0602;

        /// <summary>"Edit Label...": the element's Label (Header) in a small input box at the element.</summary>
        public const int RibbonEditLabel = 0x0603;

        /// <summary>"Size" › Large / Small.</summary>
        public const int RibbonSizeLarge = 0x0604;

        public const int RibbonSizeSmall = 0x0605;

        /// <summary>The "Size" › submenu.</summary>
        public const int RibbonSizeMenu = 0x1106;

        /// <summary>First dynamic item of "Add" › (one per element kind that can be added).</summary>
        public const int RibbonAddFirst = 0x0620;
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

        /// <summary>A ribbon element: the kinds of element "Add" › can add into it (docs/RIBBON.md section 9).</summary>
        public IReadOnlyList<string> AddChoices { get; private set; } = Array.Empty<string>();

        /// <summary>A ribbon element's polymorphic collection ("Edit Items..."), or null.</summary>
        public Ribbon.RibbonCollection? Collection { get; private set; }

        /// <summary>Whether "Create Command from this Button" applies.</summary>
        public bool CanCreateCommand { get; private set; }

        /// <summary>The icon attribute "Choose Icon..." edits, or null.</summary>
        public string? IconAttribute { get; private set; }

        /// <summary>The text attribute "Edit Label..." edits (Label or Header), or null.</summary>
        public string? LabelAttribute { get; private set; }

        /// <summary>The element's Size (Large / Small) when it has one, else null.</summary>
        public string? Size { get; private set; }

        /// <summary>The menu shows only the "Add" choices (a ribbon's "+" glyph).</summary>
        public bool AddOnly { get; private set; }

        /// <summary>An element laid out by a ribbon: the Layout submenus (Align, Make Same Size...) do not apply.</summary>
        public bool InRibbon { get; private set; }

        /// <summary><see cref="Build"/>, then the ribbon element's tasks (its smart tag or "+" glyph: <paramref name="addOnly"/>).</summary>
        public static DesignerMenuModel BuildRibbon(string text, string elementId, ComponentRegistry registry, string? clipboardTag, IReadOnlyList<string>? selectedIds, bool addOnly)
        {
            var model = Build(text, elementId, registry, clipboardTag, selectedIds);
            model.AddOnly = addOnly;
            return model;
        }

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
            // Create Handler ›: the default event first, then the component's own events - the events every control
            // raises (mouse, keys, focus...) stay in the Properties window's Events tab, which lists them by category.
            var component = registry.Find(element.TagName);
            var isRoot = string.IsNullOrEmpty(elementId);
            var defaultEvent = component?.DefaultEventFor(isRoot);
            model.Events = component is null
                ? Array.Empty<string>()
                : new[] { defaultEvent }
                    .Concat(component.Events.Where(e => e.Browsable && !e.Common && (isRoot || !e.RootOnly)))
                    .Where(e => e is not null)
                    .Select(e => e!.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Take(DesignerCommandIds.MaxDynamicItems)
                    .ToList();
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
            if (Ribbon.RibbonDesignerTasks.IsRibbon(component))
            {
                model.AddChoices = Ribbon.RibbonDesignerTasks.AddChoices(text, elementId, registry);
                model.Collection = Ribbon.RibbonDesignerTasks.CollectionOf(component!);
                model.CanCreateCommand = Ribbon.RibbonDesignerTasks.CanCreateCommand(text, elementId);
                model.IconAttribute = Ribbon.RibbonDesignerTasks.IconAttribute(text, elementId, registry);
                model.LabelAttribute = Ribbon.RibbonDesignerTasks.LabelAttribute(text, elementId, registry);
                model.Size = Ribbon.RibbonDesignerTasks.SizeOf(text, elementId, registry);
                model.InRibbon = component!.Name != "Ribbon";
            }

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

        /// <summary>Converts the view's <c>handlers!</c> table to typed handlers (EVT-5, <c>kubuno/convertHandlers</c>).</summary>
        void ConvertHandlers();

        /// <summary>"Choose Toolbox Items" (EVT-7b): which controls of the project's other crates its Toolbox tab lists.</summary>
        void ChooseToolboxItems();

        /// <summary>A Layout toolbar / Format menu command on the current selection (docs/DESIGNER.md §13).</summary>
        void RunLayoutCommand(DesignerLayoutCommand command);

        /// <summary>Adds a new <paramref name="tag"/> into ribbon element <paramref name="parentId"/> (docs/RIBBON.md section 9) and selects it.</summary>
        void AddChild(string parentId, string tag);

        /// <summary>"Create Command from this Button" on <paramref name="elementId"/>.</summary>
        void CreateCommandFrom(string elementId);

        /// <summary>Opens the collection editor on <paramref name="elementId"/>'s <paramref name="collection"/>.</summary>
        void EditCollection(string elementId, Ribbon.RibbonCollection collection);

        /// <summary>"Choose Icon...": the icon picker on <paramref name="elementId"/>'s <paramref name="attribute"/>.</summary>
        void ChooseIcon(string elementId, string attribute);

        /// <summary>"Edit Label...": a new value for <paramref name="elementId"/>'s <paramref name="attribute"/> (Label or Header).</summary>
        void EditLabel(string elementId, string attribute);

        /// <summary>"Size" › Large / Small: sets <paramref name="elementId"/>'s Size.</summary>
        void SetRibbonSize(string elementId, string size);
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
                case DesignerCommandIds.ConvertHandlers: _actions.ConvertHandlers(); break;
                case DesignerCommandIds.ChooseToolboxItems: _actions.ChooseToolboxItems(); break;
                case DesignerCommandIds.RibbonCreateCommand: _actions.CreateCommandFrom(id!); break;
                case DesignerCommandIds.RibbonEditItems when _model.Collection is { } collection: _actions.EditCollection(id!, collection); break;
                case DesignerCommandIds.RibbonChooseIcon when _model.IconAttribute is { } icon: _actions.ChooseIcon(id!, icon); break;
                case DesignerCommandIds.RibbonEditLabel when _model.LabelAttribute is { } label: _actions.EditLabel(id!, label); break;
                case DesignerCommandIds.RibbonSizeLarge: _actions.SetRibbonSize(id!, "Large"); break;
                case DesignerCommandIds.RibbonSizeSmall: _actions.SetRibbonSize(id!, "Small"); break;
                default:
                    if (Index(cmd, DesignerCommandIds.RibbonAddFirst, _model.AddChoices.Count) is { } add)
                    {
                        _actions.AddChild(id!, _model.AddChoices[add]);
                        break;
                    }

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
                case DesignerCommandIds.ConvertHandlers: return new CommandState(true, _model.IsView, DesignerText.MenuConvertHandlers);
                case DesignerCommandIds.ChooseToolboxItems: return new CommandState(true, _model.IsView, DesignerText.MenuChooseToolboxItems);
                case DesignerCommandIds.AlignMenu: return LayoutMenu(DesignerText.MenuAlign, DesignerLayoutCommand.AlignLefts, DesignerLayoutCommand.AlignBottoms);
                case DesignerCommandIds.SizeMenu: return LayoutMenu(DesignerText.MenuMakeSameSize, DesignerLayoutCommand.MakeSameWidth, DesignerLayoutCommand.MakeSameSize);
                case DesignerCommandIds.HorizontalSpacingMenu: return LayoutMenu(DesignerText.MenuHorizontalSpacing, DesignerLayoutCommand.HorizontalSpacingEqual, DesignerLayoutCommand.HorizontalSpacingRemove);
                case DesignerCommandIds.VerticalSpacingMenu: return LayoutMenu(DesignerText.MenuVerticalSpacing, DesignerLayoutCommand.VerticalSpacingEqual, DesignerLayoutCommand.VerticalSpacingRemove);
                case DesignerCommandIds.CenterMenu: return LayoutMenu(DesignerText.MenuCenterInView, DesignerLayoutCommand.CenterHorizontally, DesignerLayoutCommand.CenterVertically);
                case DesignerCommandIds.RibbonAddMenu: return new CommandState(_model.AddChoices.Count > 0, _model.AddChoices.Count > 0 && !_model.IsMultiple, DesignerText.MenuRibbonAdd);
                case DesignerCommandIds.RibbonCreateCommand: return new CommandState(true, _model.CanCreateCommand && !_model.AddOnly && !_model.IsMultiple, DesignerText.MenuRibbonCreateCommand);
                case DesignerCommandIds.RibbonEditItems:
                    return new CommandState(true, _model.Collection is not null && !_model.AddOnly && !_model.IsMultiple, DesignerText.MenuRibbonEditCollection(_model.Collection?.Row ?? "Items"));
                case DesignerCommandIds.RibbonChooseIcon: return new CommandState(true, _model.IconAttribute is not null && !_model.AddOnly && !_model.IsMultiple, DesignerText.MenuRibbonChooseIcon);
                case DesignerCommandIds.RibbonEditLabel: return new CommandState(true, _model.LabelAttribute is not null && !_model.AddOnly && !_model.IsMultiple, DesignerText.MenuRibbonEditLabel);
                case DesignerCommandIds.RibbonSizeMenu: return new CommandState(true, _model.Size is not null && !_model.AddOnly && !_model.IsMultiple, DesignerText.MenuRibbonSize);
                case DesignerCommandIds.RibbonSizeLarge: return new CommandState(_model.Size != "Large", _model.Size is not null, "Large");
                case DesignerCommandIds.RibbonSizeSmall: return new CommandState(_model.Size != "Small", _model.Size is not null, "Small");
            }

            if (Index(cmdId, DesignerCommandIds.RibbonAddFirst, DesignerCommandIds.MaxDynamicItems) is { } addChoice)
            {
                return DynamicItem(addChoice, _model.AddChoices.Count, i => _model.AddChoices[i]);
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
                !_model.IsView && !_model.InRibbon,
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
