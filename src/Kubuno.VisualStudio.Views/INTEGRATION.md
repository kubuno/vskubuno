# Integrating Kubuno.VisualStudio.Views into the Kubuno.VisualStudio VSIX

`Kubuno.VisualStudio.Views` is a standalone net48 class library: it builds and its tests run on
their own (see the repo README's "Tests" section - the same `COMPLUS_LoadFromRemoteSources=1`
workaround for a mapped network drive applies here too), but it produces no VSIX of its own. This
document is the checklist for wiring it into `src/Kubuno.VisualStudio` (the VSIX project) once that
project's own current work is done. Nothing in this document has been applied to the VSIX project -
by design, this task must not touch `src/Kubuno.VisualStudio/`, `Kubuno.VisualStudio.sln`,
`README.md` or `CHANGELOG.md`.

## 1. csproj reference

Add a `ProjectReference` to `Kubuno.VisualStudio.csproj`, in the same style already used there for
`Kubuno.VisualStudio.Core`/`Kubuno.Cargo`/`Kubuno.Launch` (see that file's existing `ItemGroup`):

```xml
<ProjectReference Include="..\Kubuno.VisualStudio.Views\Kubuno.VisualStudio.Views.csproj">
  <Project>{PUT-A-NEW-GUID-HERE}</Project>
  <Name>Kubuno.VisualStudio.Views</Name>
  <IncludeOutputGroupsInVSIX>BuiltProjectOutputGroup%3bBuiltProjectOutputGroupDependencies%3bGetCopyToOutputDirectoryItems%3bSatelliteDllsProjectOutputGroup%3b</IncludeOutputGroupsInVSIX>
  <IncludeOutputGroupsInVSIXLocalOnly>DebugSymbolsProjectOutputGroup%3b</IncludeOutputGroupsInVSIXLocalOnly>
</ProjectReference>
```

`Kubuno.VisualStudio.Views.csproj` already references the exact same
`Microsoft.VisualStudio.SDK`/`Microsoft.VisualStudio.LanguageServer.Client`/
`Microsoft.VisualStudio.Workspace.VSIntegration` package versions `Kubuno.VisualStudio.csproj` does
(verified by reading that file before this library was written) - no version reconciliation should
be needed. Add the new project to `Kubuno.VisualStudio.sln` (a new `Project(...)` entry plus the
matching `Debug|AnyCPU`/`Release|AnyCPU` lines in `ProjectConfigurationPlatforms`, following the
existing entries for `Kubuno.VisualStudio.Core` etc.) and to `tests/Kubuno.VisualStudio.Views.Tests`
likewise, so `dotnet test`/the solution build cover it going forward.

## 2. MEF pickup (Asset entries)

`Kubuno.VisualStudio.csproj`'s own `source.extension.vsixmanifest` already declares one
`Microsoft.VisualStudio.MefComponent` asset sourced from `%CurrentProject%` (the VSIX project
itself). Once `Kubuno.VisualStudio.Views.dll` is copied into the VSIX output directory by the
`ProjectReference`'s output groups (step 1), MEF composition picks up its `[Export]`s
(`LanguageService/ContentDefinition.cs`'s content type + file extension, and
`LanguageService/KubunoViewsLanguageClient.cs`'s `ILanguageClient`) automatically from any assembly
present in the VSIX - **no separate `Asset` entry is required** for the DLL itself, exactly as
`Kubuno.VisualStudio.Core`/`Kubuno.Cargo`/`Kubuno.Launch` do not get one today (check
`source.extension.vsixmanifest`: only one `MefComponent` asset exists, pointing at
`%CurrentProject%`, i.e. the VSIX project's own output folder, which by then contains every
referenced assembly). Verify this in the experimental instance after integrating (Tools > Options,
or `Ctrl+.` on a `.kbview` file, or the "Kubuno" pane logging kubuno-views-ls's own startup line) -
if composition does not pick it up, the fallback is an explicit second `MefComponent` asset:

```xml
<Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="Kubuno.VisualStudio.Views" Path="|Kubuno.VisualStudio.Views|" />
```

## 3. pkgdef / grammar inclusion

This library ships a **standalone** pkgdef fragment, `kbview-languages.pkgdef` (repo root of this
project), and its `Grammars/` folder (`kbview.tmLanguage.json`, `kbview-language-configuration.json`,
plus `LICENSE-vscode.txt`, `LICENSE-atom-language-xml.txt`, `THIRD-PARTY-NOTICES.md`). Neither is
consumed directly by this library - they must be merged into the VSIX the same way the Rust grammar
already is:

1. **Merge the registry keys.** Copy the three keys from `kbview-languages.pkgdef` into
   `src/Kubuno.VisualStudio/languages.pkgdef`, alongside the existing Rust ones (different key names
   already chosen to avoid collision: `"KubunoViews"` vs `"Kubuno"` under `TextMate\Repositories`,
   `"text.xml.kbview"`/`"kbview"` vs `"source.rust"`/`"rust"` under the two `LanguageConfiguration`
   subkeys). The `$PackageFolder$\Grammars\...` paths only need to resolve inside the VSIX's own
   install folder, so either ship this library's grammar files under the VSIX's existing `Grammars\`
   folder (flat, alongside the Rust ones - update the merged pkgdef's paths to just
   `$PackageFolder$\Grammars\kbview.tmLanguage.json` etc.) or under a new `Grammars\Kbview\` subfolder
   (update the paths accordingly) - either works, pick whichever keeps the two grammars' license
   files unambiguous (the Rust ones and these ones are under different upstream licenses/attribution
   even though both end up MIT-compatible; keeping `THIRD-PARTY-NOTICES.md` per-grammar, i.e. the
   subfolder option, is the safer choice for that reason).
2. **Add `Content` items** in `Kubuno.VisualStudio.csproj` for whichever of this library's
   `Grammars\*` files end up copied in (mirror the existing `Content Include="Grammars\rust.tmLanguage.json"` block: `IncludeInVSIX=true`, `CopyToOutputDirectory=PreserveNewest`), pointing at this
   library's files via a relative `..\Kubuno.VisualStudio.Views\Grammars\...` path and a `<Link>`
   that places them where the merged pkgdef (step 1) expects them.
3. **Do not copy `kbview-languages.pkgdef` itself into the VSIX** - only its *keys*, merged into the
   existing `languages.pkgdef` (a VSIX package can register exactly one `Microsoft.VisualStudio.VsPkgUndockedTypeIndex`/pkgdef asset the way this project is currently set up; merging keys is simpler and matches the existing precedent for `languages.pkgdef` itself already being one file for the Rust grammar).

## 4. Options page registration

`Options/KbviewOptionsPage.cs` (in this library) is a real `DialogPage`, but a `DialogPage` only
becomes a Tools > Options page once a VSIX package class declares it. Add to `KubunoPackage.cs`
(alongside the existing `RustOptionsPage` attributes):

```csharp
[ProvideOptionPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, supportsAutomation: true)]
[ProvideProfile(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage), Constants.OptionsCategoryName, "Views", 0, 0, isToolsOptionPage: true)]
```

(`Constants.OptionsCategoryName` is already `"Kubuno"`, shared with the Rust page - no change needed
there; `Kubuno.VisualStudio.Views.KbviewConstants.OptionsCategoryName`/`OptionsViewsPageName` carry
the same two strings on this library's side, for reference in its own code and tests.)

Then, in `KubunoPackage.InitializeAsync` (after the package is sited, alongside where the Rust
options work today), publish the page into this library's static gateway so the MEF-constructed
`KubunoViewsLanguageClient` can read it without needing a reference back to `KubunoPackage`:

```csharp
Kubuno.VisualStudio.Views.Options.KubunoViewsOptionsHost.Current =
    (Kubuno.VisualStudio.Views.Options.KbviewOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Views.Options.KbviewOptionsPage));
```

## 5. Logging into the "Kubuno" Output pane

This library never references `Kubuno.VisualStudio.Logging.KubunoLog` directly (that would be the
circular reference this whole document exists to avoid - see this library's own
`Logging/IKubunoLog.cs` remarks). Instead, add a tiny adapter in the VSIX and install it once, in
`KubunoPackage.InitializeAsync`, right after `KubunoLog.Initialize(pane)` is called:

```csharp
// Adapter: forwards Kubuno.VisualStudio.Views.Logging.IKubunoLog calls to the existing "Kubuno" pane.
internal sealed class KubunoLogAdapter : Kubuno.VisualStudio.Views.Logging.IKubunoLog
{
    public void WriteLine(string message) => Kubuno.VisualStudio.Logging.KubunoLog.WriteLine(message);
    public void WriteException(string context, Exception exception) => Kubuno.VisualStudio.Logging.KubunoLog.WriteException(context, exception);
}
```

```csharp
Kubuno.VisualStudio.Views.Logging.KubunoViewsLogHost.Current = new KubunoLogAdapter();
```

Until this is wired up, `KubunoViewsLanguageClient` logs into a no-op logger
(`Kubuno.VisualStudio.Views.Logging.NullKubunoLog`) rather than throwing - safe, but silent; do not
skip this step, or kubuno-views-ls discovery/startup/errors will never appear anywhere visible.

## 6. Shipping `kubuno-views-ls.exe`

`Kubuno.VisualStudio.Views.Locating.KubunoViewsLanguageServerLocator` looks for the binary, in
order: the options override (step 4), then
`<extension install dir>\tools\kubuno-views-ls.exe`, then PATH, then the two local dev build
folders. The VSIX must ship the built `kubuno-views-ls.exe` (from
`Z:\src\desktop\windows\src\crates\kubuno-views-ls`, once that crate exists and builds -
it is being written in parallel, see the task this library was produced for) under a `tools\`
folder at the VSIX's own root, alongside `Grammars\`:

```xml
<Content Include="..\..\..\desktop\windows\...\kubuno-views-ls.exe">
  <IncludeInVSIX>true</IncludeInVSIX>
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>tools\kubuno-views-ls.exe</Link>
</Content>
```

(Exact source path depends on where `kubuno-views-ls`'s own release build output ends up - update
once that crate's build script/CI target directory is known.) This is what makes
`KubunoViewsLanguageServerSource.ExtensionToolsFolder` the path a packaged, installed VSIX actually
resolves through - the PATH and dev-build-folder fallbacks exist mainly for a developer iterating on
`kubuno-views-ls` itself without repackaging the VSIX each time.

## 7. Content type base (follow-up, not required to integrate)

`LanguageService/ContentDefinition.cs`'s "kbview" content type currently derives only from
`CodeRemoteContentDefinition.CodeRemoteContentTypeName` ("code") - the same base the existing Rust
content type uses, proven to compose. Visual Studio's in-box XML editor exposes a content type named
`"XML"` that would add bracket-matching/tag-outlining for free as an *additional* base
(`[BaseDefinition("XML")]` alongside the existing one) - this could not be verified from this library
alone (no VS instance was run for this task). Once integrated, try adding it in an experimental
instance; if MEF composition fails (an unresolved content-type base name breaks composition for the
whole catalog, not just this feature), remove it and keep the current "code"-only base.

## What NOT to change on this library's side

Everything under `src/Kubuno.VisualStudio.Views/` and `tests/Kubuno.VisualStudio.Views.Tests/` is
otherwise ready to reference as-is: no source file here needs editing to complete the integration,
only the VSIX-side wiring above.
