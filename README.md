# Legion Frame Hub

Legion Frame Hub is an unofficial Windows companion for Lenovo Legion Go. This preview adds a right-side in-game panel and a full dashboard without replacing Legion Space or editing Lenovo controller services.

## Install and open

1. On your Legion Go, open the [latest GitHub release](../../releases/latest) while signed into the account that owns the private repository.
2. Download `LegionFrameHub-0.1.0-win-x64.zip` from Assets. Use **Extract All** and run `LegionFrameHub.exe` from the extracted folder. This portable x64 Windows build does not need a separate .NET install or administrator access for the dashboard.
3. Use a **borderless** or **windowed** game. With the game focused, press **Ctrl+Alt+F10** to toggle the right-side panel or **Ctrl+Alt+F11** for the full dashboard. On an XInput controller, hold **LB+RB** and tap **View** for the panel or **Menu** for the dashboard. These chords can also reach the game. You can also open the app from the system tray, although you should summon it from inside the game to capture that game as a shortcut target.
4. Closing the window hides it to the tray; choose **Exit** in the tray to stop it. Remove the extracted app folder to uninstall Frame Hub itself. Remove any managed OptiScaler installations first if you no longer want them.

This preview is unsigned. Windows may show a SmartScreen warning. Verify the private repository and the SHA-256 checksum asset if you want to confirm the ZIP matches the published build. Do not disable Windows security features. In PowerShell use `Get-FileHash .\LegionFrameHub-0.1.0-win-x64.zip -Algorithm SHA256`.

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

The physical Legion buttons are **not directly remapped**. In Lenovo’s [original Legion Go manual](https://download.lenovo.com/pccbbs/pubs/legion_go_8apu1/user_guide/en/index.html), Legion L opens Legion Space and Legion R opens Lenovo’s sidebar. Keep Legion Space for TDP, fan, performance overlay, controller configuration and other model-specific settings. The original Legion Go also has Lenovo’s Legion L plus Y thermal-mode shortcut. A remapper that supports your model and can emit both Frame Hub hotkeys can act as an optional bridge, but remapping and interoperability with Lenovo’s services are outside this preview.

### Manage OptiScaler

1. Use this only for a compatible offline game without anti-cheat. Close the game first. Check the [upstream game compatibility list](https://github.com/optiscaler/OptiScaler/wiki/General-Compatibility-List).
2. From the **OptiScaler** tab, open **Official releases** and download the build you want from upstream. Extract it into a separate folder. Select the extracted folder that directly contains `OptiScaler.dll`. Frame Hub does not automatically download a DLL or run any upstream installer scripts.
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
