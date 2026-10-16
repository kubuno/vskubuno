# Feasibility study - Kubuno mobile apps entirely in Rust, built from Visual Studio

> Status: **study / spike proposal**, pending owner direction. This is the "commence à étudier" deliverable
> (2026-10-14). It weighs writing and building the Kubuno mobile apps **entirely in Rust** (UI included),
> from Visual Studio, against today's Kotlin/Compose reality and the existing roadmap. See also
> [ARCHITECTURE.md](ARCHITECTURE.md) ("Roadmap - Kubuno mobile apps in the same VSIX" / "- Kubuno view
> rendering engine for mobile") and [SHARED-CORES.md](SHARED-CORES.md).

## 1. The question

Can Kubuno's mobile apps be **coded and compiled entirely in Rust** (no Kotlin/Compose, no Swift UI), the way
the desktop is native Rust, and **built from Visual Studio** like the `.rsproj` projects already are?

Short answer: **Android - yes, buildable from Windows/VS; iOS - yes in Rust but the build+sign step needs a Mac
(no way around it). The real cost is not the toolchain, it is the renderer: Kubuno has no portable GPU 2D
renderer today, and building one is the central work item (it also unblocks Linux/macOS desktop).**

**Owner constraint: no Tauri.** This study's direction is *already* not Tauri - Path B is **native GPU 2D
rendering** (the app draws its own pixels through `wgpu`/`vello`), not a system webview wrapped in a shell, which
is what Tauri (and its mobile support) is. The build tooling likewise avoids the Tauri ecosystem: we do **not**
use `cargo-mobile2` (a tauri-apps project). Android packaging uses the `rust-mobile` ecosystem (`android-activity`
+ `cargo-apk`/`cargo-ndk`, or `xbuild`) or Kubuno's own NDK/APK step inside the VS mobile layer; iOS uses the
Apple toolchain directly on the Mac. None of `android-activity`, `winit`, `wgpu`, `vello`, `cargo-apk`, `cargo-ndk`
or `xbuild` is a Tauri project.

## 2. Starting point (what exists vs. what is roadmap)

| Capability | Today |
|---|---|
| `.rsproj` cross-compiles to a chosen triple (`cargo --target`) | **Exists** - pass-through; but host is hard-wired x64 Windows `.exe` (`OutputType=Exe`, `Debug\|x64`/`Release\|x64` only), `CargoTargetTriple` from `rustup target list --installed`. No NDK linker, no iOS, no signing, no device deploy, no on-device debug. |
| `Kubuno.Mobile` VS layer | **Empty skeleton** (`MobileLayer` registered, no feature). |
| Desktop rendering behind a GPU-neutral trait | **No** - the `Canvas` trait + `Renderer` embed Direct2D/DirectWrite/D3D11 types (`D2D1_COLOR_F`, `ID2D1Bitmap1`, `IDWriteTextFormat`); the Win32 framework builds only on Windows. |
| OS-service abstraction (`Folders`/`SystemIntegration`/`UiHost`, portable defaults) | **Exists** (`kubuno-desktop-shell-common/platform.rs`); Linux/macOS desktop run **text-only**, no window/render. |
| Sans-IO `kubuno-<domain>-core` crates | **Exist**: `kubuno-drive-core`, `kubuno-office-docs-core` (+8 office cores), `kubuno-web-views-compiler-core`. |
| WASM binding | **Only** the views-compiler. |
| Mobile FFI (UniFFI/AAR/`.so`) | **None** (roadmap SC-7). |
| Mobile apps today | **Pure Kotlin/Jetpack-Compose** (Android), iOS = empty stub; consume shared cores only as *conformance vectors*, not compiled Rust. Published as `com.kubuno.mobile:*` (Maven). |
| `.kbview` on mobile | **None** (web + desktop only). |

## 3. The strategic fork

There are two different end-states, and they are not the same project:

- **Path A - "Kotlin UI + Rust cores" (the current roadmap).** Keep Jetpack-Compose (Android) / SwiftUI (iOS)
  for the UI; push shared logic into `kubuno-<domain>-core` crates exposed via **UniFFI/AAR**. The VS mobile
  roadmap is a `.ktproj` wrapper over Gradle (adb picker, Logcat, Pair-to-Mac, a JDWP/DAP debug spike).
  SHARED-CORES Q2 explicitly states *"Compose rendering stays Kotlin."* This is **not** "entirely in Rust".
- **Path B - "entirely in Rust" (this study).** The UI itself is rendered by a portable Rust engine (the
  roadmap's "Kubuno view rendering engine for mobile"), `.kbview` runs on mobile like on web/desktop, the app
  shell is Rust (NativeActivity on Android, UIKit+Metal on iOS), and VS builds the Rust artefacts directly.
  The ~46k lines of Kotlin + the Compose component library are retired over time.

Path B is the user's request and is consistent with Kubuno's "one view language, native renderers" identity.
It is strictly larger than Path A and subsumes the shared-cores work.

## 4. Feasibility of Path B, dimension by dimension

### 4a. Toolchain & "build from VS"

- **Android from Windows: feasible, no Java.** `rustc` targets `aarch64-linux-android` / `x86_64-linux-android`
  cross-compile on Windows with the **Android NDK** (the NDK clang as linker). A pure-Rust app shell uses
  **`android-activity` + NativeActivity** (no Java/Kotlin compiled). Packaging an APK (bundle the `.so`,
  generate `AndroidManifest.xml`, `zipalign`, sign) is done by `cargo-apk`/`cargo-ndk` or `xbuild` (all
  `rust-mobile`, not Tauri - see the owner constraint in §1), or by Kubuno's own NDK/APK step in the VS mobile
  layer. The `.rsproj` already passes `--target`; what is missing in VS is
  everything *after* `cargo build`: NDK linker wiring, APK packaging, signing, `adb` install/launch, Logcat,
  on-device debug - i.e. the roadmap's mobile layer, but pointed at **Rust** instead of Gradle.
- **iOS: Rust yes, but the build needs a Mac.** `aarch64-apple-ios` requires the iOS SDK from **Xcode**, which
  only exists on macOS - an intrinsic Apple constraint, independent of any build tool. So "entirely from VS on
  Windows" has a hard limit for iOS. Mitigation: **VS "Pair to Mac"** (the owner has a Mac) or a macOS CI runner
  drives the
  iOS build/sign/run/log remotely - exactly what the roadmap already names. Day-to-day dev + all Android stays
  on Windows.

### 4b. Rendering - the central work item

- **The desktop renderer is not portable.** Controls paint through a `Canvas` trait, but the trait's method
  signatures carry Direct2D/DirectWrite COM types (`fill_rounded(..., color: &D2D1_COLOR_F)`,
  `image(bitmap: &ID2D1Bitmap1)`, `text(..., format: &IDWriteTextFormat)`), and the `Renderer` is
  D3D11→DXGI→Direct2D→DirectComposition. There is a `DisplayList`/`Graphics` indirection, but the trait is
  "Direct2D-shaped", not GPU-neutral. So Path B requires first **refactoring the paint abstraction into a
  backend-neutral trait** (primitives only: rects, rounded rects, paths, images, text runs, clips, transforms),
  then implementing backends.
- **The cross-platform backend.** The Rust choice for GPU 2D vector drawing is **`vello` (+ `wgpu`)** - the
  modern equivalent of Direct2D (Metal on iOS, Vulkan/GL on Android, D3D/Vulkan on desktop, WebGPU on web) - or
  **Skia via `skia-safe`** (heavier, C++). Text: **`cosmic-text`/HarfBuzz**. (Slint 1.12, June 2025, shows the
  stack is viable: it added wgpu rendering and an iOS port.) Windows can keep its Direct2D backend or move to
  the portable one.
- **Strategic convergence (big upside).** The same portable renderer is what Linux and macOS **desktop** need
  (they run text-only today). One renderer effort delivers: Linux desktop, macOS desktop, Android, iOS - and
  keeps the web via WebGPU in reach. This is the highest-leverage investment in the whole platform matrix.
- Work order (matches ARCHITECTURE.md): **a Windows-hosted spike that reproduces the gallery pixel-for-pixel on
  the portable renderer**, then Android, then iOS.

### 4c. App shell & platform access

- **Shell:** Android = `android-activity`/NativeActivity owning a `SurfaceView` + a `wgpu` surface; iOS = a
  minimal UIKit `UIViewController` hosting a `CAMetalLayer`/`wgpu` surface (can be a tiny Swift/ObjC shim or
  pure `objc2`). `winit` can abstract both, or we own the shells (more control, matches desktop).
- **Platform APIs** (camera, notifications, files, background, push, keychain): reachable from Rust via
  `jni`/`ndk` (Android) and `objc2` (iOS), but this is **more verbose than Kotlin/Swift**, and is the part that
  grows the long tail of work. Today Kotlin gets Room/WorkManager/Coil/Hilt for free; Path B reimplements the
  needed slices in Rust (or keeps thin native shims).

### 4d. Shared cores & `.kbview`

- The sans-IO cores already exist and build for any target (wasm included); Path B consumes them **natively**
  (no UniFFI needed when the whole app is Rust - UniFFI is only for a Kotlin/Swift boundary, which Path B
  removes). The `.kbview` grammar/model/compiler-core are already portable; a **mobile `.kbview` renderer**
  is the same deliverable as 4b. The designer/view-registry were already moved into `Kubuno.Views` (WV-8) so a
  mobile design surface can reuse them.

### 4e. Debugging in Visual Studio

- Rust-on-Android/iOS **on-device debugging from VS is the weakest link** (VS has no JVM/ART or lldb-over-NDK
  story; the roadmap calls for a spike, and a documented hand-off to Android Studio / Xcode as fallback).
  Early development would likely debug the renderer on the **Windows host** (where VS debugging works today)
  and use logcat/console on device - acceptable, since the renderer is the same code on all backends.

## 5. Blockers & risks

1. **iOS can't be built on Windows** - a Mac is mandatory for the iOS build/sign (Pair-to-Mac mitigates, does
   not remove). "Entirely from VS *on Windows*" is therefore Android-only; iOS is "from VS, via a Mac".
2. **The renderer refactor is large** and is a prerequisite for everything visual. Until it exists, there is no
   mobile UI in Rust.
3. **VS tooling to be built** - NDK wiring, APK/app packaging, signing, device deploy, Logcat, debug - none
   exists; it is the roadmap's mobile layer, re-aimed at Rust.
4. **Platform-API long tail** - every OS capability Kotlin gets for free must be bridged in Rust.
5. **Throwing away ~46k lines of working Kotlin/Compose** (and the `com.kubuno.mobile` component library) - a
   real sunk cost; needs a migration plan, not a flag-day.
6. SHARED-CORES currently records the *opposite* decision (Q2: "Compose rendering stays Kotlin; native only as
   prebuilt AARs for offline-critical logic"). Path B **supersedes** that decision and should be recorded as
   such if adopted.

## 6. Strategic upside

- **One codebase, one view language** (`.kbview` + Rust cores) across web, desktop and mobile - the Kubuno
  identity, maximal sharing, no Kotlin/Swift UI drift.
- **The renderer work also delivers Linux & macOS desktop** (today text-only) - four platforms for one effort.
- **Leverages the sans-IO cores already written** and the WV-8 designer move.
- Builds mostly from Windows/VS (Android + the host-side renderer spike); only iOS needs the Mac.

## 7. Recommendation

Adopt Path B as a **direction to validate by a staged spike**, not a flag-day rewrite:

1. **Spike 0 (Windows, VS):** carve the paint abstraction out of `kubuno-desktop-ui`/`-controls` into a
   backend-neutral `Canvas`/display-list trait; implement a **`vello`+`wgpu` backend**; reproduce the desktop
   **gallery pixel-for-pixel on Windows** next to the Direct2D one. Deliverable: proof the portable renderer
   matches, and a measured perf/size comparison. (Also the first step toward Linux/macOS desktop.)
2. **Spike 1 (Android, from Windows):** `android-activity`/NativeActivity + the wgpu surface + the portable
   renderer; render one real `.kbview` screen; package an APK with `cargo-apk`/`xbuild`; `adb` install/run.
   Deliverable: the gallery (or a drive screen) running on an Android device, built from Windows.
3. **Spike 2 (iOS, via the Mac):** the UIKit+Metal shell; build/sign through Pair-to-Mac. Deliverable: the same
   screen on an iPhone.
4. **VS mobile layer (Rust-aimed):** only after the spikes confirm the renderer - NDK wiring, packaging,
   signing, device picker, Logcat, and the debug decision.

Each spike is a go/no-go gate; the renderer spike (0) is the one that de-risks the whole thing and is worth
doing first regardless of the mobile decision, because it also unblocks desktop Linux/macOS.

## 8. Open questions for the owner

- **O-M1 - scope:** full Path B (retire Kotlin/Compose over time), or hybrid (Rust renderer for the shared
  `.kbview` UI, keep a thin Kotlin shell for platform glue) as a stepping stone?
- **O-M2 - renderer backend:** `vello`+`wgpu` (pure Rust, newer) vs `skia-safe` (battle-tested, C++ build)?
- **O-M3 - iOS build host:** Pair-to-Mac from VS, or a macOS CI runner, or both?
- **O-M4 - order:** start the renderer spike now (it also serves Linux/macOS desktop), or settle O-M1/O-M2
  first?
- **O-M5 - SHARED-CORES Q2:** record that Path B supersedes "Compose rendering stays Kotlin"?
