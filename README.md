# Swifter Browser

Ultra-optimized Windows 11 web browser built on .NET 8 WPF + Microsoft WebView2.

## Features

- **Shields**: EasyList/AdGuard trie engine, cosmetic DOM hider, WebRTC leak guard, canvas/audio/WebGL anti-fingerprinting, tracking-parameter stripper
- **Downloads**: IDM-grade 16-segment resumable downloader with per-file bandwidth throttle, HLS/DASH media sniffer, injected glass video overlay
- **Workspace**: horizontal/vertical tabs, 2-column and 2×2 split screen, tab groups, 5-minute hibernation to SQLite, isolated multi-account containers, web panels sidebar
- **Productivity**: Ctrl+K command palette, mouse gestures, reader mode with SpeechSynthesis narration, Google Lens integration, Picture-in-Picture
- **Storage**: SQLite unified repository (history, bookmarks, cookies, sessions, downloads), AES-256-GCM password vault
- **Protocols**: `swifter://newtab`, `swifter://downloads`, `swifter://history`, `swifter://bookmarks`, `swifter://settings`, `swifter://shields`
- **UI**: native Windows 11 Mica/Acrylic via dwmapi, accent-color provider, custom tab strip / address bar / status overlay controls

## Build

```powershell
# from Swifter/Installer/
.\build.ps1 -Configuration Release
.\build.ps1 -Publish -Installer
```

Or with the .NET SDK directly:

```bash
dotnet build Swifter/Swifter.csproj -c Release
```

Requirements: Windows 10 1809+ / Windows 11, .NET 8 SDK, WebView2 Runtime, optional Inno Setup 6 for installers.

## Layout

```
Swifter/
├── Theme/           DWM glass, accent provider, custom controls
├── Core/
│   ├── Engine/      working-set trim, shared WebView2 environment, low-end throttle
│   ├── Shield/      adblock trie, cosmetic injector, fingerprint/WebRTC guards
│   ├── Downloads/   segmented engine, stitcher, HLS sniffer, media overlay
│   ├── Workspace/   layouts, groups, hibernation, split grid, containers
│   ├── Storage/     SQLite repositories + password vault
│   ├── Productivity/ palette, gestures, reader, Lens, PiP, hotkeys
│   ├── Protocols/   swifter:// scheme + internal pages
│   └── Extensions/  MV3 manifest parser, background runner, host engine
├── UI/              view models + WPF views
└── Installer/       Inno Setup script + PowerShell build pipeline
```
