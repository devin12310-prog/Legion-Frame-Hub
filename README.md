# Legion Frame Hub

Legion Frame Hub is an unofficial Windows companion for Lenovo Legion Go. The home screen shows installed Steam games and manually added game shortcuts, with tabs for Scalers, Frame Gen and Settings. The quick panel can open the stock Legion Space app. Frame Hub does not edit Lenovo controller services or write its own fan curves.

## Install and open

1. On your Legion Go, open the [latest GitHub release](../../releases/latest) while signed into the account that owns the private repository.
2. Download `LegionFrameHub.exe` from Assets, put it in a folder you own and double-click it. No ZIP extraction, separate .NET installation, or administrator access is required for the dashboard. The ZIP is optional.
3. Use a **borderless** or **windowed** game. With the game focused, press **Ctrl+Alt+F10** to toggle the right-side panel or **Ctrl+Alt+F11** for the full dashboard. On an XInput controller, hold **LB+RB** and tap **View** for the panel or **Menu** for the dashboard. These chords can also reach the game. You can also open the app from the system tray, although you should summon it from inside the game to capture that game as a shortcut target.
4. Closing the window hides it to the tray; choose **Exit** in the tray to stop it. Remove the extracted app folder to uninstall Frame Hub itself. Remove any managed OptiScaler installations first if you no longer want them.

This preview is unsigned. Windows may show a SmartScreen warning. Verify the private repository and the SHA-256 checksum asset if you want to confirm the EXE matches the published build. Do not disable Windows security features. In PowerShell use `Get-FileHash .\LegionFrameHub.exe -Algorithm SHA256`.

## In-app updates

Under **Settings → Check & install Frame Hub update**, the app queries this private repo using [GitHub CLI](https://cli.github.com/). Install the CLI and run `gh auth login` once outside Frame Hub using the GitHub account with access to the repo. Frame Hub does not prompt for or store a GitHub token. It checks the EXE against GitHub’s SHA-256 release digest before replacing the portable copy and restarting. Keep the filename `LegionFrameHub.exe` and use a writable location. An update leaves the previous EXE as a rollback copy next to the app; it does not change your game library or OptiScaler files. **Open GitHub Releases** provides a browser alternative.

## Installed Games

Frame Hub scans local Steam app manifests, including other configured library drives. Not all launchers are auto-detected. Choose **Add a game** for compatible standalone or third-party games and select their `.exe`; use the vendor launcher for titles that require it. Removing a manually added entry removes only the shortcut. The local list is stored at `%LOCALAPPDATA%\LegionFrameHub\games.json`.

## What the controls do

| Feature | Behavior |
| --- | --- |
| Lossless Scaling / LSFG | Sends the default `Ctrl+Alt+S` shortcut after returning focus to your game. Install, update, and remove the paid app through [Steam](https://store.steampowered.com/app/993090/Lossless_Scaling/). Open Lossless Scaling and configure your preferred mode there first. |
| AMD AFMF | Sends AMD’s documented default [`Alt+Shift+G`](https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-24-1-1.html) shortcut. You need an AFMF-capable graphics driver and the feature enabled/available in [AMD Software](https://www.amd.com/en/products/software/adrenalin/afmf.html). Settings such as Radeon Super Resolution remain in AMD Software. |
| OptiScaler | Sends `End` to request frame-gen toggle and `Insert` to request the overlay. Match your settings to the default vendor hotkeys; the app does not verify that the requests took effect. See the [official instructions](https://github.com/optiscaler/OptiScaler/wiki/Manual-Installation). |
| Native FSR, XeSS, or DLSS frame generation | Change these from the game’s own graphics menu. There is no universal in-game toggle used by this app. Compatibility varies by the game, renderer, and hardware. |
| Quick settings | Changes default audio volume and, on compatible internal panels, brightness. Shows battery/AC status and opens Windows pages for display, refresh rate, and power. |
| Drivers & apps | Reads model, BIOS, and display driver inventory. Optional read-only scan queries Windows Update for applicable drivers; it does not query Lenovo’s catalog. Use [Lenovo’s driver updater](https://support.lenovo.com/us/en/solutions/ht516319-how-to-use-legion-space-to-update-drivers-legion-go-8apu1), Lenovo Support, Steam, or Windows to actually install or remove their respective packages. |

Use a single frame-generation method per game when testing. The app reports when it sent a shortcut, not whether the vendor applied it. A game running elevated may block input from a normal app instance; prefer running both without elevation. Games using exclusive fullscreen or input restrictions may not work with the panel.

### Legion buttons and Lenovo settings

In the [original Legion Go manual](https://download.lenovo.com/pccbbs/pubs/legion_go_8apu1/user_guide/en/index.html), Legion L opens Legion Space and Legion R opens Lenovo’s sidebar. Frame Hub does not rebind them by itself. In [Handheld Companion’s Legion Go setup](https://github.com/Valkirie/HandheldCompanion/wiki/Legion-Go), configure custom keystroke hotkeys that send **Ctrl+Alt+F10** from **Legion R** and **Ctrl+Alt+F11** from **Legion L**. Open Frame Hub first. Follow the remapper’s instructions for your exact model; simply closing the Lenovo window does not necessarily release these buttons. If the remapper conflicts with stock services, follow its own setup and rollback instructions rather than changing services from Frame Hub.

**Close Legion Space UI…** is optional and only runs after you approve it. It tries a normal close, then targets the verified `LegionSpace.exe` UI process in the current Windows session if needed. It does not stop background services or prevent Lenovo from relaunching its UI. The quick panel’s **Open Lenovo Legion Space** button uses the installed Windows shortcut or looks for the Lenovo executable in its standard install location.

For custom fan curves, open [Lenovo Legion Space](https://support.lenovo.com/us/en/solutions/ht517560) or the fan-curve page in Handheld Companion if your model supports it. Use only one fan controller. Direct fan control, embedded-controller access, and protective fallback behavior have not been tested on hardware and are not part of Frame Hub.

### Manage OptiScaler

1. Use this only for a compatible offline game without anti-cheat. Close the game first. Check the [upstream game compatibility list](https://github.com/optiscaler/OptiScaler/wiki/General-Compatibility-List).
2. From the **Scalers** tab, open **Official releases** and download the build you want from upstream. Extract it into a separate folder. Select the extracted folder that directly contains `OptiScaler.dll`. Frame Hub does not automatically download a DLL or run any upstream installer scripts.
3. Select the game’s actual `.exe` (often a `*-Win64-Shipping.exe` under `Binaries\Win64` for Unreal Engine), choose the proxy DLL name documented for that game, enter the release tag, and choose **Install / replace**.
4. **Check latest release** reads GitHub’s latest published release tag; it does not fetch prereleases or infer the installed version from DLL contents. To update, extract the new build separately, select it, and choose **Install / replace** again. **Remove managed files** removes the tracked payload, but keeps `OptiScaler.ini`. Existing settings are preserved, so new options or per-title dependencies may require work using the official guide.

The manager checks for pre-existing file conflicts, rejects linked folders, tracks installed files by hash, and refuses to remove or replace externally modified managed binaries. It does not adopt earlier manual installs or perform extra game-specific setup. Game stores or launchers may prevent writes to protected locations; Frame Hub does not bypass those restrictions.

#### Recovery

If the app or computer loses power during a file change, the game folder may contain `.framehub-optiscaler-transaction`. The app will block further modifications to that folder. Close the game, make a backup of the game folder and the transaction folder, then inspect `old` (previous files and receipt, if present) and `new` (staged files not yet moved). Do not delete the transaction folder before inspecting it. If the state is unclear, restore from your backup or use the launcher’s verify/repair option and check for leftover mod files before attempting another install.

## Build and verify

```powershell
dotnet run --project tests/OptiFiles.Tests.csproj -c Release
dotnet publish src/LegionFrameHub.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o artifacts/win-x64
```

The automated test harness uses dummy files, not a game or a mod download. The preview does not claim on-device testing or compatibility with every Legion Go model, driver, and game.

The executable icon uses the cropped and recolored Legion mark from [Lenovo’s Legion logo on Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Lenovo_Legion_Logo.png). Lenovo and Legion are Lenovo trademarks. This project is not affiliated with or endorsed by Lenovo.
