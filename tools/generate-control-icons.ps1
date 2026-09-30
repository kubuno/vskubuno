<#
  Generates the Kubuno control icons (one per kubuno-views component) used by the .kbview designer's
  Toolbox and by the .kbview element nodes in Solution Explorer (docs/DESIGNER.md section 11).

  Each icon is a Lucide glyph (the icon family of the Kubuno apps - https://lucide.dev, ISC license),
  drawn the Lucide way (24x24 grid, 2-unit stroke, round caps and joins, no fill), with a neutral ink
  and ONE accent detail in the Kubuno primary blue (core/frontend/src/theme.css --color-primary). The
  geometry is read verbatim from lucide-react's per-icon `__iconNode` data (the same source
  desktop's lucide-icons.txt was extracted from).

  Outputs (all checked in - run this script again after changing the table below):
    src/Desktop/Kubuno.Desktop.ProjectSystem/Resources/Icons/Controls/<Name>.<Light|Dark|HighContrast>.xaml
    src/Desktop/Kubuno.Desktop.ProjectSystem/Resources/KubunoControls.imagemanifest
    src/Desktop/Kubuno.Desktop.Logic/SolutionExplorer/ControlIcons.cs
  plus a review sheet (not checked in): C:\kubuno-build\icons-preview\controls-<variant>.png

  Usage (Windows PowerShell 5.1 - WPF rendering needs its STA default):
    powershell -NoProfile -File tools\generate-control-icons.ps1 [-LucideIcons <dir>] [-PreviewDir <dir>]
#>
param(
    [string]$LucideIcons = 'Z:\src\core\frontend\node_modules\lucide-react\dist\esm\icons',
    [string]$PreviewDir = 'C:\kubuno-build\icons-preview'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml

$repo = Split-Path -Parent $PSScriptRoot
$iconDir = Join-Path $repo 'src\Desktop\Kubuno.Desktop.ProjectSystem\Resources\Icons\Controls'
$manifestPath = Join-Path $repo 'src\Desktop\Kubuno.Desktop.ProjectSystem\Resources\KubunoControls.imagemanifest'
$csPath = Join-Path $repo 'src\Desktop\Kubuno.Desktop.Logic\SolutionExplorer\ControlIcons.cs'
$imagesGuid = '{3b6e1f24-9c7a-4d5e-8f21-6a0c4b9d2e71}'

# Component -> Lucide icon (+ which of its elements carries the accent: index, 'last' by default, or
# 'none'). The IDs are the 1-based positions in this list and are persisted in the image manifest:
# APPEND new components, never reorder. 'Control' is the fallback for unknown/custom elements.
$table = @(
    @{ Name = 'Control';          Icon = 'square-dashed';        Accent = 'none' }
    @{ Name = 'Button';           Icon = 'square-mouse-pointer'; Accent = 0 }
    @{ Name = 'Card';             Icon = 'panel-top';            Accent = 1 }
    @{ Name = 'Stack';            Icon = 'rows-3';               Accent = 1 }
    @{ Name = 'Switch';           Icon = 'toggle-right' }
    @{ Name = 'TextField';        Icon = 'text-cursor-input' }
    @{ Name = 'Badge';            Icon = 'badge' }
    @{ Name = 'Callout';          Icon = 'info';                 Accent = 1 }
    @{ Name = 'EmptyState';       Icon = 'inbox';                Accent = 0 }
    @{ Name = 'Icon';             Icon = 'image';                Accent = 1 }
    @{ Name = 'Label';            Icon = 'type';                 Accent = 'none' }
    @{ Name = 'LinkLabel';        Icon = 'link';                 Accent = 0 }
    @{ Name = 'ProgressBar';      Custom = @(
        @('rect', @{ x = '2'; y = '8'; width = '20'; height = '8'; rx = '2' }),
        @('path', @{ d = 'M6 12h7' })) }
    @{ Name = 'Separator';        Icon = 'separator-horizontal'; Accent = 0 }
    @{ Name = 'Spinner';          Icon = 'loader';               Accent = 0 }
    @{ Name = 'ColorField';       Icon = 'palette';              Accent = 0 }
    @{ Name = 'ComboBox';         Icon = 'text-select';          Accent = 'none' }
    @{ Name = 'DatePicker';       Icon = 'calendar' }
    @{ Name = 'Dropdown';         Icon = 'square-chevron-down';  Accent = 1 }
    @{ Name = 'MaskedField';      Icon = 'rectangle-ellipsis';   Accent = 1 }
    @{ Name = 'Option';           Icon = 'check' }
    @{ Name = 'SearchField';      Icon = 'search';               Accent = 0 }
    @{ Name = 'TextArea';         Icon = 'text';                 Accent = 0 }
    @{ Name = 'Accordion';        Icon = 'list-collapse';        Accent = 0 }
    @{ Name = 'AccordionSection'; Icon = 'chevrons-down-up' }
    @{ Name = 'Breadcrumb';       Icon = 'chevrons-right';       Accent = 1 }
    @{ Name = 'BreadcrumbItem';   Icon = 'chevron-right' }
    @{ Name = 'GroupBox';         Icon = 'group' }
    @{ Name = 'Panel';            Icon = 'square-dashed';        Accent = 'none' }
    @{ Name = 'ScrollArea';       Icon = 'scroll-text';          Accent = 1 }
    @{ Name = 'Splitter';         Icon = 'columns-2' }
    @{ Name = 'Step';             Icon = 'circle-check' }
    @{ Name = 'Stepper';          Icon = 'list-ordered';         Accent = 3 }
    @{ Name = 'TabItem';          Icon = 'app-window';           Accent = 1 }
    @{ Name = 'Tabs';             Icon = 'panels-top-left';      Accent = 1 }
    @{ Name = 'Toolbar';          Icon = 'panel-top-dashed';     Accent = 1 }
    @{ Name = 'ToolbarItem';      Icon = 'mouse-pointer-2' }
    @{ Name = 'CheckedListBox';   Icon = 'list-checks';          Accent = 0 }
    @{ Name = 'Column';           Icon = 'columns-3';            Accent = 1 }
    @{ Name = 'DataTable';        Icon = 'table';                Accent = 1 }
    @{ Name = 'Item';             Icon = 'list-start';           Accent = 0 }
    @{ Name = 'ListBox';          Icon = 'list';                 Accent = 0 }
    @{ Name = 'ListView';         Icon = 'layout-list';          Accent = 0 }
    @{ Name = 'MonthCalendar';    Icon = 'calendar-days' }
    @{ Name = 'TreeView';         Icon = 'folder-tree';          Accent = 0 }
    @{ Name = 'CheckBox';         Icon = 'square-check' }
    @{ Name = 'IconButton';       Icon = 'circle-plus' }
    @{ Name = 'NumericField';     Icon = 'chevrons-up-down';     Accent = 0 }
    @{ Name = 'RadioButton';      Icon = 'circle-dot' }
    @{ Name = 'Slider';           Icon = 'sliders-horizontal';   Accent = 0 }
    # EVT-7b: the project's own controls (Toolbox project tab, #[toolbox(icon = "…")]) and the non-visual components.
    @{ Name = 'UserControl';      Icon = 'layout-template';      Accent = 0 }
    @{ Name = 'Timer';            Icon = 'timer';                Accent = 1 }
    @{ Name = 'CustomControl';    Icon = 'pencil-ruler';         Accent = 0 }
    @{ Name = 'Component';        Icon = 'box';                  Accent = 1 }
    @{ Name = 'Circle';           Icon = 'circle';               Accent = 0 }
    @{ Name = 'Star';             Icon = 'star';                 Accent = 0 }
    @{ Name = 'Gauge';            Icon = 'gauge';                Accent = 0 }
    @{ Name = 'Shapes';           Icon = 'shapes';               Accent = 0 }
    # EVT-7c: the hover hint component, the context menu and its items.
    @{ Name = 'ToolTip';          Icon = 'message-square-text';  Accent = 1 }
    @{ Name = 'ContextMenu';      Icon = 'square-menu';          Accent = 0 }
    @{ Name = 'MenuItem';         Icon = 'align-left';           Accent = 0 }
    # Printing (docs/PRINTING.md): the Toolbox's Printing tab ("Impression").
    @{ Name = 'PrintDocument';    Icon = 'printer' }
    @{ Name = 'PrintPreviewControl'; Icon = 'file-search' }
    @{ Name = 'PrintPreviewDialog'; Icon = 'scan-eye' }
    @{ Name = 'PrintDialog';      Icon = 'printer-check';        Accent = 1 }
    @{ Name = 'PageSetupDialog';  Icon = 'file-sliders' }
    # The in-window FloatingWindow: a window drawn inside a view.
    @{ Name = 'FloatingWindow';   Icon = 'picture-in-picture-2'; Accent = 0 }
    # The docking family (kubuno-views registry/families/docking.rs): the Toolbox's Docking tab.
    @{ Name = 'DockArea';         Icon = 'panels-left-bottom' }
    @{ Name = 'DockPanel';        Icon = 'panel-right';          Accent = 1 }
    @{ Name = 'WorkspaceShell';   Icon = 'app-window-mac' }
)

# Ink / accent per Visual Studio background. Light: Kubuno text-primary + primary blue; Dark: the VS
# dark-theme icon grey + a lighter Kubuno blue; HighContrast: white + cyan (same as the lot-8 icons).
$variants = [ordered]@{
    Light        = @{ Ink = '#444444'; Accent = '#1A73E8'; Background = '#F5F5F5' }
    Dark         = @{ Ink = '#C5C5C5'; Accent = '#4D9BF0'; Background = '#252526' }
    HighContrast = @{ Ink = '#FFFFFF'; Accent = '#00FFFF'; Background = '#000000' }
}

function Read-LucideNode([string]$icon) {
    $file = Join-Path $LucideIcons "$icon.mjs"
    if (-not (Test-Path $file)) { throw "Lucide icon '$icon' not found in $LucideIcons" }
    $text = Get-Content $file -Raw
    $alias = [regex]::Match($text, "export \{ default \} from '\./([\w-]+)\.mjs'")
    if ($alias.Success -and $text.IndexOf('const __iconNode = [') -lt 0) { return Read-LucideNode $alias.Groups[1].Value }
    $start = $text.IndexOf('const __iconNode = [')
    $end = $text.IndexOf('];', $start)
    $body = $text.Substring($start, $end - $start)
    $nodes = @()
    foreach ($m in [regex]::Matches($body, '\[\s*"(\w+)",\s*\{([^}]*)\}')) {
        $attrs = @{}
        foreach ($a in [regex]::Matches($m.Groups[2].Value, '(\w+):\s*"([^"]*)"')) { $attrs[$a.Groups[1].Value] = $a.Groups[2].Value }
        $nodes += , @($m.Groups[1].Value, $attrs)
    }
    return , $nodes
}

# SVG path data -> something WPF's geometry mini-language parses: separate the arc flags that SVG
# allows to be written glued together ("a2 2 0 012 2") and check the result.
function ConvertTo-WpfPathData([string]$d) {
    try { [void][System.Windows.Media.Geometry]::Parse($d); return $d } catch { }
    $fixed = [regex]::Replace($d, '([aA])([^aAmMlLhHvVcCsSqQtTzZ]+)', {
        param($m)
        # Arc arguments come in groups of 7 (rx ry rotation large-arc sweep x y); the two flags are
        # single 0/1 characters that SVG lets touch the next number.
        $raw = $m.Groups[2].Value; $pos = 0; $k = 0; $tokens = @()
        while ($pos -lt $raw.Length) {
            if ($raw[$pos] -match '[\s,]') { $pos++; continue }
            if ($k % 7 -eq 3 -or $k % 7 -eq 4) { $tokens += [string]$raw[$pos]; $pos++ }
            else {
                $n = [regex]::Match($raw.Substring($pos), '^-?(?:\d+\.?\d*|\.\d+)(?:[eE]-?\d+)?')
                if (-not $n.Success) { throw "Cannot parse arc arguments '$raw'" }
                $tokens += $n.Value; $pos += $n.Length
            }
            $k++
        }
        $m.Groups[1].Value + ($tokens -join ' ') + ' '
    })
    [void][System.Windows.Media.Geometry]::Parse($fixed)
    return $fixed.Trim()
}

function New-ShapeXaml($node, [string]$color) {
    $kind = $node[0]; $a = $node[1]
    $stroke = "Stroke=""$color"" StrokeThickness=""2"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" StrokeLineJoin=""Round"""
    switch ($kind) {
        'path' { return "<Path Data=""$(ConvertTo-WpfPathData $a.d)"" $stroke />" }
        'circle' {
            $r = [double]$a.r
            return "<Ellipse Canvas.Left=""$([double]$a.cx - $r)"" Canvas.Top=""$([double]$a.cy - $r)"" Width=""$(2 * $r)"" Height=""$(2 * $r)"" $stroke />"
        }
        'ellipse' {
            return "<Ellipse Canvas.Left=""$([double]$a.cx - [double]$a.rx)"" Canvas.Top=""$([double]$a.cy - [double]$a.ry)"" Width=""$(2 * [double]$a.rx)"" Height=""$(2 * [double]$a.ry)"" $stroke />"
        }
        'rect' {
            $rx = if ($a.rx) { $a.rx } else { '0' }
            $ry = if ($a.ry) { $a.ry } else { $rx }
            return "<Rectangle Canvas.Left=""$($a.x)"" Canvas.Top=""$($a.y)"" Width=""$($a.width)"" Height=""$($a.height)"" RadiusX=""$rx"" RadiusY=""$ry"" $stroke />"
        }
        'line' { return "<Line X1=""$($a.x1)"" Y1=""$($a.y1)"" X2=""$($a.x2)"" Y2=""$($a.y2)"" $stroke />" }
        'polyline' { return "<Polyline Points=""$($a.points)"" $stroke />" }
        'polygon' { return "<Polygon Points=""$($a.points)"" $stroke />" }
        default { throw "Unsupported Lucide element '$kind'" }
    }
}

New-Item -ItemType Directory -Force $iconDir, $PreviewDir | Out-Null
Get-ChildItem $iconDir -Filter *.xaml | Remove-Item

$utf8 = New-Object System.Text.UTF8Encoding($true)
$entries = @()
for ($i = 0; $i -lt $table.Count; $i++) {
    $entry = $table[$i]
    $id = $i + 1
    $nodes = if ($entry.Custom) { , $entry.Custom } else { Read-LucideNode $entry.Icon }
    $accent = if ($entry.ContainsKey('Accent')) { $entry.Accent } else { 'last' }
    $accentIndex = if ($accent -eq 'none') { -1 } elseif ($accent -eq 'last') { $nodes.Count - 1 } else { [int]$accent }
    $source = if ($entry.Custom) { 'custom (Lucide style)' } else { "Lucide ""$($entry.Icon)""" }
    foreach ($variant in $variants.Keys) {
        $colors = $variants[$variant]
        $shapes = for ($n = 0; $n -lt $nodes.Count; $n++) {
            $color = if ($n -eq $accentIndex) { $colors.Accent } else { $colors.Ink }
            '    ' + (New-ShapeXaml $nodes[$n] $color)
        }
        $xaml = @"
<!-- Kubuno control icon "$($entry.Name)", $variant background: $source, Kubuno ink + accent. Generated by tools/generate-control-icons.ps1 - do not edit by hand. -->
<Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="16" Height="16">
  <Canvas Width="24" Height="24">
$($shapes -join "`r`n")
  </Canvas>
</Viewbox>
"@
        [System.IO.File]::WriteAllText((Join-Path $iconDir "$($entry.Name).$variant.xaml"), $xaml, $utf8)
    }
    $entries += @{ Name = $entry.Name; Id = $id; Source = $source }
}

# Image manifest (discovered by Visual Studio's image service at the VSIX root, like RustProject.imagemanifest).
$ids = ($entries | ForEach-Object { "    <ID Name=""$($_.Name)"" Value=""$($_.Id)"" />" }) -join "`r`n"
$images = ($entries | ForEach-Object {
@"
    <Image Guid="`$(ControlImagesGuid)" ID="`$($($_.Name))" AllowColorInversion="false">
      <Source Uri="`$(Controls)/$($_.Name).Light.xaml" Background="Light" />
      <Source Uri="`$(Controls)/$($_.Name).Dark.xaml" Background="Dark" />
      <Source Uri="`$(Controls)/$($_.Name).HighContrast.xaml" Background="HighContrast" />
    </Image>
"@ }) -join "`r`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<!--
  Kubuno control icons (docs/DESIGNER.md section 11): one Lucide-based vector icon per kubuno-views component,
  shown in Visual Studio's Toolbox for the .kbview designer and on the .kbview element nodes of Solution
  Explorer. Generated by tools/generate-control-icons.ps1 - do not edit by hand; the IDs must match
  Kubuno.Desktop.Logic's ControlIcons.cs (generated by the same script).
-->
<ImageManifest xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns="http://schemas.microsoft.com/VisualStudio/ImageManifestSchema/2014">
  <Symbols>
    <String Name="Controls" Value="/Kubuno.Desktop.ProjectSystem;Component/Resources/Icons/Controls" />
    <Guid Name="ControlImagesGuid" Value="$imagesGuid" />
$ids
  </Symbols>
  <Images>
$images
  </Images>
  <ImageLists />
</ImageManifest>
"@
[System.IO.File]::WriteAllText($manifestPath, $manifest, $utf8)

$csEntries = ($entries | Where-Object { $_.Name -ne 'Control' } | ForEach-Object { "            [""$($_.Name)""] = $($_.Id), // $($_.Source)" }) -join "`r`n"
$cs = @"
// <auto-generated>
// Generated by tools/generate-control-icons.ps1 - do not edit by hand (rerun the script instead).
// </auto-generated>
#nullable enable
using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Logic.SolutionExplorer
{
    /// <summary>
    /// The Kubuno control icons of <c>KubunoControls.imagemanifest</c> (docs/DESIGNER.md section 11): one
    /// Lucide-based icon per kubuno-views component, used by the .kbview designer's Toolbox AND the
    /// .kbview element nodes of Solution Explorer. Rust code symbols keep Visual Studio's own
    /// <c>KnownMonikers</c> (<see cref="SymbolMonikerNames"/>) so they look like the rest of the IDE.
    /// </summary>
    public static class ControlIcons
    {
        /// <summary>The manifest's <c>ControlImagesGuid</c>.</summary>
        public static readonly Guid ImagesGuid = new Guid("$($imagesGuid.Trim('{','}'))");

        /// <summary>The generic control icon, for an element the table does not know.</summary>
        public const int FallbackId = 1;

        private static readonly Dictionary<string, int> Ids = new Dictionary<string, int>(StringComparer.Ordinal)
        {
$csEntries
        };

        /// <summary>The image id for element <paramref name="tag"/> (<see cref="FallbackId"/> when unknown).</summary>
        public static int IdFor(string? tag) => tag != null && Ids.TryGetValue(tag, out var id) ? id : FallbackId;

        /// <summary>Every component that has its own icon.</summary>
        public static IEnumerable<string> KnownComponents => Ids.Keys;
    }
}
"@
[System.IO.File]::WriteAllText($csPath, $cs, $utf8)

# Review sheet: every icon at 16 and 32 px, per background.
foreach ($variant in $variants.Keys) {
    $colors = $variants[$variant]
    $cols = 6; $cellW = 250; $cellH = 56
    $rows = [math]::Ceiling($entries.Count / $cols)
    $panel = New-Object System.Windows.Controls.Canvas
    $panel.Width = $cols * $cellW; $panel.Height = $rows * $cellH + 40
    $panel.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($colors.Background))
    $title = New-Object System.Windows.Controls.TextBlock
    $title.Text = "Kubuno control icons - $variant"; $title.FontSize = 16
    $title.Foreground = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($colors.Ink))
    [System.Windows.Controls.Canvas]::SetLeft($title, 10); [System.Windows.Controls.Canvas]::SetTop($title, 8)
    [void]$panel.Children.Add($title)
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $x = ($i % $cols) * $cellW + 10; $y = [math]::Floor($i / $cols) * $cellH + 44
        foreach ($size in 16, 32) {
            $icon = [System.Windows.Markup.XamlReader]::Parse([System.IO.File]::ReadAllText((Join-Path $iconDir "$($entries[$i].Name).$variant.xaml")))
            $icon.Width = $size; $icon.Height = $size
            [System.Windows.Controls.Canvas]::SetLeft($icon, $(if ($size -eq 16) { $x } else { $x + 24 }))
            [System.Windows.Controls.Canvas]::SetTop($icon, $(if ($size -eq 16) { $y + 8 } else { $y }))
            [void]$panel.Children.Add($icon)
        }
        $label = New-Object System.Windows.Controls.TextBlock
        $label.Text = $entries[$i].Name; $label.FontSize = 13
        $label.Foreground = $title.Foreground
        [System.Windows.Controls.Canvas]::SetLeft($label, $x + 64); [System.Windows.Controls.Canvas]::SetTop($label, $y + 8)
        [void]$panel.Children.Add($label)
    }
    $panel.Measure((New-Object System.Windows.Size($panel.Width, $panel.Height)))
    $panel.Arrange((New-Object System.Windows.Rect(0, 0, $panel.Width, $panel.Height)))
    $panel.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap([int]$panel.Width, [int]$panel.Height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($panel)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create((Join-Path $PreviewDir "controls-$($variant.ToLowerInvariant()).png"))
    $encoder.Save($stream); $stream.Close()
}

"Generated $($entries.Count) icons x $($variants.Count) variants; manifest, ControlIcons.cs and previews in $PreviewDir."
