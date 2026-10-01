<#
  Pre-release check of the "Create a new project" templates (docs/RSPROJ.md, "Template build check"):
  instantiates every project template of src/Rust/Kubuno.Rust/ProjectTemplates, src/Desktop/Kubuno.Desktop/ProjectTemplates and src/Web/Kubuno.Web/ProjectTemplates into a fresh folder on
  a local disk, the way Visual Studio does (same files, same $token$ replacements, including the ones
  Kubuno.Rust.TemplateWizard and Kubuno.Desktop.TemplateWizard add), then builds each one:

    1. `cargo build` in the generated project (own target directory), failing on any warning;
    2. with -Run: runs what the template produces (console: exit code 0; desktop app: its window must
       stay up for a few seconds with its page area at the view's DesignWidth x DesignHeight, launched with the PATH F5 computes - profile dir, deps, Rust std);
    3. `MSBuild -restore` on the generated .rsproj (the real Kubuno.Rust.Sdk build Visual Studio runs),
       with CARGO_TARGET_DIR set to a shared directory (like a machine-wide CARGO_TARGET_DIR) - the setup that made a
       fresh Kubuno Desktop Application fail with E0463 before the .rsproj gave it its own subfolder.

  A brand-new project must build out of the box: any failure here blocks a release.

  Usage (Windows PowerShell 5.1 or PowerShell 7):
    powershell -NoProfile -File tools\test-templates.ps1 [-Template KubunoDesktopApplication,...]
        [-WorkRoot C:\kubuno-build\template-tests] [-DesktopSrc <desktop\windows>] [-SkipMSBuild] [-Run] [-Keep]

  Needs cargo/rustc (rustup) and, unless -SkipMSBuild, Visual Studio's MSBuild with the Kubuno extension
  installed (it registers the Kubuno.Rust.Sdk NuGet feed). The Kubuno Module template fetches its
  git-tagged kubuno-seccomp dependency from github.com, so it needs network access.
#>
param(
    [string[]]$Template = @(),
    [string]$WorkRoot = 'C:\kubuno-build\template-tests',
    [string]$DesktopSrc = $env:KUBUNO_DESKTOP_SRC,
    [switch]$SkipMSBuild,
    [switch]$Run,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$repo = Split-Path -Parent $PSScriptRoot
# Each layer ships its own templates (docs/ARCHITECTURE.md, "Layers (as built)"): the Rust ones and the desktop ones.
$templatesRoots = @((Join-Path $repo 'src\Rust\Kubuno.Rust\ProjectTemplates'), (Join-Path $repo 'src\Desktop\Kubuno.Desktop\ProjectTemplates'), (Join-Path $repo 'src\Web\Kubuno.Web\ProjectTemplates'))

# The user's full PATH (machine + user), so cargo is found from any shell.
$env:PATH = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('PATH', 'User') + ';' + $env:PATH
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { throw 'cargo was not found on PATH (install Rust with rustup).' }

# ---- Template token values, computed exactly like Kubuno.Rust.TemplateWizard.CrateNameWizard ----

function Get-CrateName([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return 'app' }
    $sb = New-Object System.Text.StringBuilder
    $previous = [char]0
    foreach ($c in $name.ToLowerInvariant().ToCharArray()) {
        $mapped = if (($c -ge 'a' -and $c -le 'z') -or ($c -ge '0' -and $c -le '9') -or $c -eq '_' -or $c -eq '-') { $c } else { '-' }
        if ($mapped -eq '-' -and $previous -eq '-') { continue }
        [void]$sb.Append($mapped)
        $previous = $mapped
    }
    $result = $sb.ToString().Trim('-', '_')
    if ($result.Length -eq 0) { return 'app' }
    if ($result[0] -lt 'a' -or $result[0] -gt 'z') { $result = 'app-' + $result }
    return $result
}

function Resolve-DesktopSource([string]$configured) {
    $root = if ([string]::IsNullOrWhiteSpace($configured)) { 'Z:\src\desktop\windows' } else { $configured.Trim().Trim('"').TrimEnd('\', '/') }
    $probe = { param($r) Test-Path (Join-Path $r 'src\crates\kubuno-ui\Cargo.toml') }
    if (-not (& $probe $root) -and (& $probe (Join-Path $root 'windows'))) { $root = Join-Path $root 'windows' }
    return $root.Replace('\', '/')
}

# ---- Instantiation: copy the .vstemplate's items, replacing parameters where it says so ----

function Expand-Tokens([string]$text, [hashtable]$tokens) {
    foreach ($key in $tokens.Keys) { $text = $text.Replace($key, $tokens[$key]) }
    return $text
}

function Copy-TemplateItems($node, [string]$sourceDir, [string]$targetDir, [hashtable]$tokens) {
    foreach ($child in $node.ChildNodes) {
        if ($child.NodeType -ne 'Element') { continue }
        if ($child.LocalName -eq 'ProjectItem') {
            $source = Join-Path $sourceDir $child.InnerText.Trim()
            $targetName = if ($child.GetAttribute('TargetFileName')) { Expand-Tokens $child.GetAttribute('TargetFileName') $tokens } else { Split-Path -Leaf $source }
            Copy-TemplateFile $source (Join-Path $targetDir $targetName) ($child.GetAttribute('ReplaceParameters') -eq 'true') $tokens
        }
        elseif ($child.LocalName -eq 'Folder') {
            $subTarget = Join-Path $targetDir (Expand-Tokens $child.GetAttribute('TargetFolderName') $tokens)
            New-Item -ItemType Directory -Force $subTarget | Out-Null
            Copy-TemplateItems $child (Join-Path $sourceDir $child.GetAttribute('Name')) $subTarget $tokens
        }
    }
}

function Copy-TemplateFile([string]$source, [string]$target, [bool]$replace, [hashtable]$tokens) {
    if (-not $replace) { Copy-Item $source $target; return }
    $text = Expand-Tokens ([IO.File]::ReadAllText($source)) $tokens
    $left = [regex]::Matches($text, '\$[a-z][a-z0-9]*\$') | ForEach-Object Value | Select-Object -Unique
    if ($left) { throw "$(Split-Path -Leaf $target): unreplaced template parameter(s) $($left -join ', ') - Visual Studio would leave them as-is." }
    [IO.File]::WriteAllText($target, $text)
}

function New-FromTemplate([string]$vstemplatePath, [string]$projectName, [string]$solutionDir) {
    [xml]$xml = Get-Content -Raw $vstemplatePath
    $project = $xml.VSTemplate.TemplateContent.Project
    $crate = Get-CrateName $projectName
    $tokens = @{
        '$safeprojectname$'  = $projectName
        '$projectname$'      = $projectName
        '$cratename$'        = $crate
        '$moduleid$'         = $crate.Replace('-', '_')
        '$kubunodesktopsrc$' = (Resolve-DesktopSource $DesktopSrc)
    }
    # The web module template's wizard (Kubuno.Web.TemplateWizard / ModuleTemplateTokens): same rules and fallbacks.
    if ($xml.VSTemplate.WizardExtension.FullClassName -eq 'Kubuno.Web.TemplateWizard.ModuleWizard') {
        $id = (($projectName.ToLowerInvariant().ToCharArray() | Where-Object { ($_ -ge 'a' -and $_ -le 'z') -or ($_ -ge '0' -and $_ -le '9') }) -join '')
        if (-not $id) { $id = 'module' } elseif ($id[0] -lt 'a' -or $id[0] -gt 'z') { $id = 'm' + $id }
        $crate = "kubuno-$id"
        $tokens['$moduleid$'] = $id
        $tokens['$cratename$'] = $crate
        $tokens['$moduletitle$'] = $projectName
        $tokens['$kubunouiversion$'] = '0.1.12'
        $tokens['$kubunosdkversion$'] = '0.1.10'
        $tokens['$kubunodriveversion$'] = '0.1.7'
        $tokens['$seccomptag$'] = 'seccomp-v0.1.1'
        $tokens['$dbtag$'] = 'db-v0.9.0'
    }
    # The web module template's wizard (Kubuno.Web.TemplateWizard / ModuleTemplateTokens): same rules and fallbacks.
    if ($xml.VSTemplate.WizardExtension.FullClassName -eq 'Kubuno.Web.TemplateWizard.ModuleWizard') {
        $id = (($projectName.ToLowerInvariant().ToCharArray() | Where-Object { ($_ -ge 'a' -and $_ -le 'z') -or ($_ -ge '0' -and $_ -le '9') }) -join '')
        if (-not $id) { $id = 'module' } elseif ($id[0] -lt 'a' -or $id[0] -gt 'z') { $id = 'm' + $id }
        $crate = "kubuno-$id"
        $tokens['$moduleid$'] = $id
        $tokens['$cratename$'] = $crate
        $tokens['$moduletitle$'] = $projectName
        $tokens['$kubunouiversion$'] = '0.1.12'
        $tokens['$kubunosdkversion$'] = '0.1.10'
        $tokens['$kubunodriveversion$'] = '0.1.7'
        $tokens['$seccomptag$'] = 'seccomp-v0.1.1'
        $tokens['$dbtag$'] = 'db-v0.9.0'
    }
    $projectDir = Join-Path $solutionDir $projectName
    New-Item -ItemType Directory -Force $projectDir | Out-Null
    $sourceDir = Split-Path -Parent $vstemplatePath
    $rsproj = Join-Path $projectDir (Expand-Tokens $project.TargetFileName $tokens)
    Copy-TemplateFile (Join-Path $sourceDir $project.File) $rsproj ($project.ReplaceParameters -eq 'true') $tokens
    Copy-TemplateItems $project $sourceDir $projectDir $tokens
    return [pscustomobject]@{ Dir = $projectDir; Rsproj = $rsproj; Crate = $crate }
}

# ---- Item templates (docs/EVENTS.md EVT-7b): the Kubuno control templates, instantiated into the desktop
#      application the way ControlItemWizard does it (struct name, file name, `mod` in main.rs), used in its view,
#      and checked by a test of the generated crate that compiles the view against the registered controls ----

# The control item templates are the desktop layer's.
$itemTemplatesRoot = Join-Path $repo 'src\Desktop\Kubuno.Desktop\ItemTemplates'

# Same rules as Kubuno.Desktop.TemplateWizard.ControlItemNames.
function Get-ItemWords([string]$name) {
    $stem = [IO.Path]::GetFileNameWithoutExtension($name)
    $spaced = [regex]::Replace($stem, '([a-z0-9])([A-Z])', '$1 $2')
    $spaced = [regex]::Replace($spaced, '([A-Z]+)([A-Z][a-z])', '$1 $2')
    return @([regex]::Split($spaced, '[^A-Za-z0-9]+') | Where-Object { $_.Length -gt 0 })
}
function Get-ClassName([string]$name) { (Get-ItemWords $name | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join '' }
function Get-ModuleName([string]$name) { (Get-ItemWords $name | ForEach-Object { $_.ToLowerInvariant() }) -join '_' }

function Add-ModuleDeclaration([string]$mainRs, [string]$module) {
    $text = [IO.File]::ReadAllText($mainRs)
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $mods = [regex]::Matches($text, '(?m)^(pub(\([^)]*\))?\s+)?mod\s+\w+\s*;[^\n]*$')
    if ($mods.Count -eq 0) { throw "no mod declaration in $mainRs to add `mod $module;` after" }
    $last = $mods[$mods.Count - 1]
    $lineEnd = $text.IndexOf("`n", $last.Index + $last.Length)
    [IO.File]::WriteAllText($mainRs, $text.Substring(0, $lineEnd + 1) + "mod $module;" + $nl + $text.Substring($lineEnd + 1))
}

function Add-ControlItems($p) {
    $src = Join-Path $p.Dir 'src'
    $items = @(
        @{ Template = 'KubunoCustomControl'; Name = 'RoundButton'; Xml = '<RoundButton x:Name="round" Text="Round" CornerRadius="18" X="24" Y="84" Width="160" Height="40" Anchor="Top, Left"/>' },
        @{ Template = 'KubunoUserControl'; Name = 'RatingPanel'; Xml = '<RatingPanel x:Name="panel" Caption="Rate this" X="24" Y="140" Width="320" Height="48" Anchor="Top, Left"/>' },
        @{ Template = 'KubunoInheritedControl'; Name = 'CountingButton'; Base = 'Button'; Xml = '<CountingButton x:Name="counting" Text="Count" Variant="Secondary" X="200" Y="84" Width="120" Height="36" Anchor="Top, Left"/>' },
        @{ Template = 'KubunoComponent'; Name = 'Heartbeat'; Xml = '<Heartbeat x:Name="beat" Enabled="true"/>' },
        # A second form (docs/PROGRAMMING-MODEL.md), opened by the main one below.
        @{ Template = 'KubunoView'; Name = 'SettingsView'; Xml = $null }
    )
    $xml = @()
    foreach ($item in $items) {
        $dir = Join-Path $itemTemplatesRoot $item.Template
        [xml]$vst = Get-Content -Raw (Get-ChildItem $dir -Filter '*.vstemplate' | Select-Object -First 1).FullName
        $first = $vst.VSTemplate.TemplateContent.ProjectItem | Select-Object -First 1
        $ext = [IO.Path]::GetExtension($first.GetAttribute('TargetFileName'))
        $tokens = @{
            # The "Ajouter" commands pass the Rust module name as the file name (RoundButton -> round_button.rs).
            '$fileinputname$' = Get-ModuleName $item.Name
            '$rootname$'      = (Get-ModuleName $item.Name) + $ext
            '$safeitemname$'  = $item.Name
            '$classname$'     = Get-ClassName $item.Name
            '$modulename$'    = Get-ModuleName $item.Name
            '$baseclass$'     = $(if ($item.ContainsKey('Base')) { $item['Base'] } else { 'Button' })
        }
        foreach ($pi in $vst.VSTemplate.TemplateContent.ProjectItem) {
            $target = Join-Path $src (Expand-Tokens $pi.GetAttribute('TargetFileName') $tokens)
            Copy-TemplateFile (Join-Path $dir $pi.InnerText.Trim()) $target ($pi.GetAttribute('ReplaceParameters') -eq 'true') $tokens
            if ($target.EndsWith('.rs')) {
                Add-ModuleDeclaration (Join-Path $src 'main.rs') ([IO.Path]::GetFileNameWithoutExtension($target))
                # Like ControlItemNames.AdaptToProjectOnDisk: the application reaches kubuno_views through the kubuno facade.
                $rs = [IO.File]::ReadAllText($target)
                [IO.File]::WriteAllText($target, [regex]::Replace($rs, '(?<![\w:])kubuno_views::', 'kubuno::views::'))
            }
        }
        if ($item.Xml) { $xml += '  ' + $item.Xml }
        "   item template $($item.Template): $($tokens['$classname$']) ($($tokens['$modulename$']))"
    }

    # The controls in the starter view, and a test compiling it (their derives registered them); the main form
    # opens the second one.
    $view = Join-Path $src 'main_view.kbview'
    $text = [IO.File]::ReadAllText($view)
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    [IO.File]::WriteAllText($view, $text.Replace('</Panel>', ($xml -join $nl) + $nl + '</Panel>'))
    $code = Join-Path $src 'main_view.rs'
    $check = @(
        '', 'impl MainView {', '    #[allow(dead_code)]', '    fn open_settings(&mut self) -> DialogResult {',
        '        crate::settings_view::SettingsView::new().show_dialog(self)', '    }', '}',
        '', '#[cfg(test)]', 'mod kubuno_template_check {', '    #[test]', '    fn the_view_compiles_with_the_project_controls() {',
        '        if let Err(diagnostics) = kubuno::views::compile::compile(include_str!("main_view.kbview")) {',
        '            panic!("main_view.kbview does not compile: {diagnostics:?}");', '        }',
        '        for name in ["RoundButton", "RatingPanel", "CountingButton", "Heartbeat"] {',
        '            assert!(kubuno::views::registry::project_info(name).is_some(), "{name} is not registered");', '        }', '    }',
        '', '    #[test]', '    fn the_forms_link_their_controls() {',
        '        let main = super::MainView::new();', '        assert_eq!(main.hello.get_text(), "Say hello");',
        '        let settings = crate::settings_view::SettingsView::new();', '        assert_eq!(settings.get_text(), "SettingsView");', '    }', '}')
    [IO.File]::AppendAllText($code, ($check -join $nl) + $nl)
}

# ---- Build/run helpers ----

function Invoke-Logged([string]$file, [string]$arguments, [string]$workingDir, [string]$log, [hashtable]$environment) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $arguments
    $psi.WorkingDirectory = $workingDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($k in $environment.Keys) { $psi.EnvironmentVariables[$k] = $environment[$k] }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stderr = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    [IO.File]::WriteAllText($log, $stdout + $stderr.Result)
    return $proc.ExitCode
}

# The Subsystem field of a PE image's optional header: 2 = WINDOWS_GUI (no console), 3 = WINDOWS_CUI.
function Get-PeSubsystem([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x00004550) { throw "$path is not a PE image" }
    # PE signature (4) + COFF file header (20), then the optional header; Subsystem is at offset 68 in both PE32 and PE32+.
    return [BitConverter]::ToUInt16($bytes, $pe + 4 + 20 + 68)
}

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    return $null
}

if (-not ('TemplateTestWin32' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TemplateTestWin32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
}

$sysroot = (& rustc --print sysroot).Trim()
$hostTriple = ((& rustc -vV) | Where-Object { $_ -like 'host:*' }).Substring(5).Trim()
$stdDir = Join-Path $sysroot "lib\rustlib\$hostTriple\lib"
$msbuild = if ($SkipMSBuild) { $null } else { Find-MSBuild }
if (-not $SkipMSBuild -and -not $msbuild) { throw 'MSBuild.exe was not found (Visual Studio 2026). Use -SkipMSBuild to check cargo builds only.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $WorkRoot $stamp
$sharedTarget = Join-Path $runRoot 'shared-target'
New-Item -ItemType Directory -Force $runRoot | Out-Null
"Templates: $($templatesRoots -join ", ")"
"Work root: $runRoot"
"Desktop sources: $(Resolve-DesktopSource $DesktopSrc)"

$results = @()
foreach ($dir in Get-ChildItem $templatesRoots -Directory) {
    if ($Template.Count -and $Template -notcontains $dir.Name) { continue }
    $vstemplate = Get-ChildItem $dir.FullName -Filter '*.vstemplate' | Select-Object -First 1
    if (-not $vstemplate) { continue }
    $name = 'Tt' + $dir.Name
    $failures = @()
    $solutionDir = Join-Path $runRoot $dir.Name
    Write-Host "== $($dir.Name)" -ForegroundColor Cyan
    try {
        $p = New-FromTemplate $vstemplate.FullName $name $solutionDir
        # The item templates go into the desktop application (EVT-7b).
        if ($dir.Name -eq 'KubunoDesktopApplication') { Add-ControlItems $p }

        # 1. cargo build, own target directory, no warning allowed.
        $cargoTarget = Join-Path $runRoot "target-$($dir.Name)"
        $log = Join-Path $solutionDir 'cargo-build.log'
        $code = Invoke-Logged 'cargo' 'build' $p.Dir $log @{ CARGO_TARGET_DIR = $cargoTarget }
        $warnings = @(Select-String -Path $log -Pattern '^warning' | Where-Object { $_.Line -notmatch '^warning: .*generated \d+ warning' })
        if ($code -ne 0) { $failures += "cargo build failed (exit $code, see $log)" }
        elseif ($warnings.Count) { $failures += "cargo build: $($warnings.Count) warning(s) (see $log)" }
        else { "   cargo build: OK" }

        # 1b. A GUI application opens no console window, in Debug too (like a Windows Forms WinExe): its exe's
        #     PE subsystem must be WINDOWS_GUI (2); a console application's stays WINDOWS_CUI (3).
        if (-not $failures) {
            $expected = @{ 'KubunoDesktopApplication' = 2; 'RustConsoleApplication' = 3 }[$dir.Name]
            if ($expected) {
                $built = Join-Path $cargoTarget "debug\$($p.Crate).exe"
                $subsystem = Get-PeSubsystem $built
                if ($subsystem -ne $expected) { $failures += "PE subsystem of $built is $subsystem, expected $expected ($(if ($expected -eq 2) { 'GUI: no console window' } else { 'console' }))" }
                else { "   PE subsystem: $subsystem ($(if ($expected -eq 2) { 'GUI, no console window' } else { 'console' }))" }
            }
        }

        # 1c. The item templates' controls register themselves and the view using them compiles (EVT-7b).
        if (-not $failures -and $dir.Name -eq 'KubunoDesktopApplication') {
            $log = Join-Path $solutionDir 'cargo-test.log'
            $code = Invoke-Logged 'cargo' 'test' $p.Dir $log @{ CARGO_TARGET_DIR = $cargoTarget }
            if ($code -ne 0) { $failures += "cargo test (the view using the item templates' controls) failed (exit $code, see $log)" }
            else { "   cargo test: the item templates' controls compile, register and are used by the view" }
        }

        # 2. Run what the template produces.
        if ($Run -and -not $failures) {
            $exe = Join-Path $cargoTarget "debug\$($p.Crate).exe"
            switch ($dir.Name) {
                'RustConsoleApplication' {
                    $out = & $exe
                    if ($LASTEXITCODE -ne 0) { $failures += "run: exit $LASTEXITCODE" } else { "   run: OK ($out)" }
                }
                'KubunoDesktopApplication' {
                    $profileDir = Join-Path $cargoTarget 'debug'
                    # The exe imports its own build of the shared library, kubuno_ui-<hash>.dll (docs/DESIGNER.md
                    # section 16), which the build leaves in deps\ - never the plain kubuno_ui.dll (Cargo's alias).
                    $exeText = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($exe))
                    $uiName = [regex]::Match($exeText, 'kubuno_ui-[0-9a-f]{16}\.dll').Value
                    if (-not $uiName) { $failures += "run: $exe does not import a kubuno_ui-<hash>.dll" }
                    elseif (-not (Test-Path (Join-Path $profileDir "deps\$uiName"))) { $failures += "run: $uiName (imported by $exe) missing from $profileDir\deps" }
                    if (-not (Get-ChildItem $stdDir -Filter 'std-*.dll')) { $failures += "run: no std-*.dll in $stdDir" }
                    if (-not $failures) {
                        # Same PATH as F5 (Kubuno.Rust.Launch.RustDebugEnvironment): profile dir, deps, Rust std.
                        $savedPath = $env:PATH
                        $env:PATH = "$profileDir;$profileDir\deps;$stdDir;$env:PATH"
                        try { $proc = Start-Process -FilePath $exe -PassThru } finally { $env:PATH = $savedPath }
                        Start-Sleep -Seconds 6
                        $proc.Refresh()
                        if ($proc.HasExited) { $failures += "run: exited early (exit $($proc.ExitCode))" }
                        elseif ($proc.MainWindowHandle -eq [IntPtr]::Zero) { $failures += 'run: no main window'; Stop-Process -Id $proc.Id -Force; [void]$proc.WaitForExit(10000) }
                        else {
                            "   run: window '$($proc.MainWindowTitle)' up"
                            # Like a WinForms form, the window's page area (client area below the 34-DIP Kubuno
                            # caption) must be the view's DesignWidth x DesignHeight.
                            $view = [IO.File]::ReadAllText((Join-Path $p.Dir 'src\main_view.kbview'))
                            $designW = [double]([regex]::Match($view, 'DesignWidth="(\d+)"').Groups[1].Value)
                            $designH = [double]([regex]::Match($view, 'DesignHeight="(\d+)"').Groups[1].Value)
                            # Per-monitor aware, or Windows reports a DPI-virtualized client rect.
                            [void][TemplateTestWin32]::SetThreadDpiAwarenessContext([IntPtr]-4)
                            $rc = New-Object TemplateTestWin32+RECT
                            [void][TemplateTestWin32]::GetClientRect($proc.MainWindowHandle, [ref]$rc)
                            $scale = [TemplateTestWin32]::GetDpiForWindow($proc.MainWindowHandle) / 96.0
                            $pageW = $rc.Right / $scale
                            $pageH = $rc.Bottom / $scale - 34
                            if ([math]::Abs($pageW - $designW) -gt 1.5 -or [math]::Abs($pageH - $designH) -gt 1.5) {
                                $failures += "run: page area $([math]::Round($pageW,1)) x $([math]::Round($pageH,1)) DIP, expected the view's design size $designW x $designH"
                            }
                            else { "   run: page area = design size $designW x $designH" }
                            Stop-Process -Id $proc.Id -Force; [void]$proc.WaitForExit(10000)
                        }
                    }
                }
                default { "   run: nothing to run for this template" }
            }
        }

        if (-not $Keep -and (Test-Path $cargoTarget)) { Remove-Item -Recurse -Force $cargoTarget }

        # 3. MSBuild on the .rsproj, with a CARGO_TARGET_DIR that is not the project's own (the E0463 setup).
        if ($msbuild) {
            $log = Join-Path $solutionDir 'msbuild.log'
            $code = Invoke-Logged $msbuild "`"$($p.Rsproj)`" -restore -v:m -nologo -p:Configuration=Debug" $p.Dir $log @{ CARGO_TARGET_DIR = $sharedTarget }
            $msWarnings = @(Select-String -Path $log -Pattern ': warning ' -SimpleMatch)
            if ($code -ne 0) { $failures += "MSBuild failed (exit $code, see $log)" }
            elseif ($msWarnings.Count) { $failures += "MSBuild: $($msWarnings.Count) warning(s) (see $log)" }
            else { "   MSBuild .rsproj: OK" }
            # A dylib (kubuno_ui.dll) is named without a hash: it must never land in the shared directory.
            if (Test-Path (Join-Path $sharedTarget 'debug\kubuno_ui.dll')) { $failures += "MSBuild built kubuno_ui.dll into the SHARED target directory $sharedTarget (E0463 hazard: the project needs its own)" }
        }
    }
    catch { $failures += $_.Exception.Message }
    finally {
        # Target directories are large (a desktop build is over a gigabyte): drop them unless -Keep.
        if (-not $Keep) {
            foreach ($t in @((Join-Path $runRoot "target-$($dir.Name)"), $sharedTarget)) { if (Test-Path $t) { Remove-Item -Recurse -Force $t } }
        }
    }

    foreach ($f in $failures) { Write-Host "   FAIL: $f" -ForegroundColor Red }
    $results += [pscustomobject]@{ Template = $dir.Name; Result = $(if ($failures) { 'FAIL' } else { 'OK' }) }
}

$results | Format-Table -AutoSize | Out-String | Write-Host
if (-not $results) { throw 'No template matched.' }
if ($results | Where-Object Result -eq 'FAIL') { Write-Host "Logs kept in $runRoot"; exit 1 }
if ($Keep) { "All templates build. Output kept in $runRoot." }
else { Remove-Item -Recurse -Force $runRoot; 'All templates build.' }
