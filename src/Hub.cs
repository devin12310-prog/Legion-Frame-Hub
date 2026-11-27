using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FrameHub;

internal sealed class Hub : Form
{
    const string Opti = "https://github.com/optiscaler/OptiScaler";
    const string LenovoScan = "https://support.lenovo.com/us/en/solutions/ht516319-how-to-use-legion-space-to-update-drivers-legion-go-8apu1";
    const string LenovoSupport = "https://pcsupport.lenovo.com/us/en/";
    const string AMD = "https://www.amd.com/en/products/software/adrenalin/afmf.html";
    const string Steam = "https://store.steampowered.com/app/993090/Lossless_Scaling/";
    static readonly Color Surface = Color.FromArgb(18, 23, 33), Card = Color.FromArgb(31, 40, 56),
        Ink = Color.FromArgb(239, 246, 255), Muted = Color.FromArgb(174, 192, 216), Accent = Color.FromArgb(91, 229, 195);
    static readonly HttpClient Http = MakeHttp();
    static HttpClient MakeHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LegionFrameHub/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    sealed record Release([property: JsonPropertyName("tag_name")] string Tag);

    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer controller = new() { Interval = 60 };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft };
    readonly Panel scroller = new() { AutoScroll = true, Dock = DockStyle.Fill, Padding = new(16, 12, 16, 12) };
    readonly TableLayoutPanel stack = new() { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
    readonly TextBox game = new() { ReadOnly = true, PlaceholderText = "Select the game’s main .exe" };
    readonly TextBox package = new() { ReadOnly = true, PlaceholderText = "Select the extracted OptiScaler folder" };
    readonly TextBox version = new() { PlaceholderText = "Release tag, e.g. v0.x.y (optional)", MaxLength = 80 };
    readonly ComboBox loader = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Rectangle fullMinimum = new(0, 0, 580, 420);
    string tab = "Frame gen", message = "Ready.";
    bool quick, quitting, busy, shortcutsActive;
    int previousChord;
    IntPtr returnWindow;
    string returnProcess = "";

    internal Hub()
    {
        Text = "Legion Frame Hub";
        BackColor = Surface; ForeColor = Ink; Font = new("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = fullMinimum.Size;
        Size = new(843, 680);
        StartPosition = FormStartPosition.CenterScreen;
        status.BackColor = Card; status.ForeColor = Muted;
        scroller.Controls.Add(stack); Controls.Add(scroller); Controls.Add(status);
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        loader.Items.AddRange(OptiFiles.LoaderNames.Cast<object>().ToArray()); loader.SelectedIndex = 0;

        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Legion Frame Hub", Visible = true, ContextMenuStrip = new() };
        tray.ContextMenuStrip.Items.Add("Quick panel", null, (_, _) => OpenMode(true));
        tray.ContextMenuStrip.Items.Add("Full dashboard", null, (_, _) => OpenMode(false));
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Exit());
        tray.DoubleClick += (_, _) => OpenMode(false);
        controller.Tick += (_, _) =>
        {
            int chord = Native.PadChord();
            if (chord != 0 && previousChord == 0) OpenMode(chord == 1);
            previousChord = chord;
        };
        controller.Start();
        FormClosing += (_, e) =>
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        FormClosed += (_, _) => { controller.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        Render();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.RegisterHotKey(Handle, 101, 0x4003, (uint)Keys.F10))
            Note("Ctrl+Alt+F10 is unavailable. Use the tray or controller chord.");
        if (!Native.RegisterHotKey(Handle, 102, 0x4003, (uint)Keys.F11))
            Note("Ctrl+Alt+F11 is unavailable. Use the tray or controller chord.");
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, 101);
        Native.UnregisterHotKey(Handle, 102);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() is 101 or 102)
        {
            OpenMode(m.WParam.ToInt32() == 101);
            return;
        }
        base.WndProc(ref m);
    }
    void Exit()
    {
        if (busy || shortcutsActive) { Note("Wait for the current operation to finish before exiting."); return; }
        quitting = true;
        Close();
    }
    void OpenMode(bool compact, bool toggle = true)
    {
        if (shortcutsActive) return;
        if (toggle && Visible && WindowState != FormWindowState.Minimized && ContainsFocus && compact == quick) { Hide(); return; }
        var foreground = Native.GetForegroundWindow();
        var process = Native.WindowProcess(foreground);
        if (foreground != Handle && process.Length > 0 && process is not ("explorer" or "ShellExperienceHost" or "StartMenuExperienceHost")
            && !process.Equals("LegionFrameHub", StringComparison.OrdinalIgnoreCase))
        {
            returnWindow = foreground;
            returnProcess = process;
        }
        else if (!ContainsFocus)
        {
            returnWindow = IntPtr.Zero;
            returnProcess = "";
        }
        WindowState = FormWindowState.Normal;
        if (quick != compact || !Visible)
        {
            var work = Screen.FromHandle(foreground == IntPtr.Zero ? Handle : foreground).WorkingArea;
            quick = compact;
            TopMost = quick;
            MinimumSize = quick ? new Size(Math.Min(340, work.Width), Math.Min(340, work.Height)) :
                new Size(Math.Min(fullMinimum.Width, work.Width), Math.Min(fullMinimum.Height, work.Height));
            int width = quick ? Math.Min(work.Width, Math.Max(360, work.Width / 2)) : Math.Min(work.Width, 930);
            int height = quick ? work.Height : Math.Min(work.Height, 760);
            Bounds = new(quick ? work.Right - width : work.Left + (work.Width - width) / 2,
                quick ? work.Top : work.Top + (work.Height - height) / 2, width, height);
        }
        if (quick) tab = "Frame gen";
        Render();
        Show();
        Activate();
    }

    void Note(string value)
    {
        message = value;
        status.Text = value;
    }
    void Clear()
    {
        foreach (var box in new Control[] { game, package, version, loader }) box.Parent?.Controls.Remove(box);
        while (stack.Controls.Count > 0) stack.Controls[0].Dispose();
        stack.RowCount = 0;
        stack.RowStyles.Clear();
    }
    Label TextLine(string body, int size = 10, bool strong = false)
    {
        var label = new Label
        {
            AutoSize = true, Text = body, ForeColor = strong ? Accent : Muted,
            Font = new Font(Font.FontFamily, size, strong ? FontStyle.Bold : FontStyle.Regular),
            Dock = DockStyle.Fill, Margin = new(4, 6, 4, 5)
        };
        label.MaximumSize = new(Math.Max(250, scroller.ClientSize.Width - 56), 0);
        return label;
    }
    Button ButtonFor(string label, Action action)
    {
        var button = new Button
        {
            Text = label, AutoSize = true, MinimumSize = new(112, 36),
            BackColor = Card, ForeColor = Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Padding = new(10, 3, 10, 3), Margin = new(4)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 86, 114);
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception e) { ShowError(e); }
        };
        return button;
    }
    void Add(Control control)
    {
        int row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(control, 0, row);
    }
    void Line(string text, int size = 10, bool strong = false) => Add(TextLine(text, size, strong));
    void Buttons(params (string Label, Action Click)[] actions)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new(0) };
        foreach (var action in actions) row.Controls.Add(ButtonFor(action.Label, action.Click));
        Add(row);
    }
    void Field(Control box)
    {
        box.Dock = DockStyle.Fill; box.Margin = new(4, 4, 12, 6);
        box.BackColor = Card; box.ForeColor = Ink;
        Add(box);
    }
    void Render()
    {
        stack.SuspendLayout();
        try
        {
            Clear();
            Line(quick ? "QUICK PANEL" : "LEGION FRAME HUB", quick ? 19 : 26, true);
            Line(quick ? "Return to your game with Esc or the panel shortcut." : "Windows companion for Lenovo Legion Go  •  Preview 0.1.0");
            if (quick)
                Buttons(("Full dashboard", () => OpenMode(false, false)), ("Close panel", Hide));
            else
                Buttons(("Frame gen", () => SelectTab("Frame gen")), ("OptiScaler", () => SelectTab("OptiScaler")),
                    ("Quick settings", () => SelectTab("Quick settings")), ("Drivers & apps", () => SelectTab("Drivers & apps")),
                    ("Quick panel", () => OpenMode(true, false)));
            if (quick)
            {
                FrameGeneration();
                QuickSettings();
            }
            else
            {
                switch (tab)
                {
                    case "OptiScaler": OptiPage(); break;
                    case "Quick settings": QuickSettings(); break;
                    case "Drivers & apps": Drivers(); break;
                    default: FrameGeneration(); Help(); break;
                }
            }
            status.Text = message;
        }
        finally { stack.ResumeLayout(true); }
    }
    void SelectTab(string value) { tab = value; Render(); scroller.AutoScrollPosition = Point.Empty; }
    void FrameGeneration()
    {
        Line("Frame generation", 14, true);
        Line("Target: " + (returnProcess.Length == 0 ? "none. Open Frame Hub while your game is focused." : returnProcess));
        Buttons(("Lossless Scaling • Toggle", () => Forward("Lossless Scaling", 0x11, 0x12, 0x53)),
            ("AMD AFMF • Toggle", () => Forward("AFMF", 0x12, 0x10, 0x47)));
        Buttons(("OptiScaler • Toggle FG", () => Forward("OptiScaler frame gen", 0x23)),
            ("OptiScaler • Open menu", () => Forward("OptiScaler overlay", 0x2D)));
        Line("These send default vendor shortcuts. The app cannot verify whether frame gen is on. Use one frame-generation method at a time.");
        if (!quick)
        {
            Line("Turn native FSR, XeSS or DLSS frame generation on or off in the game’s own graphics menu where supported. AMD graphics options stay in Adrenalin.");
            Buttons(("Launch Lossless Scaling", () => Open("steam://run/993090")), ("Lossless Scaling on Steam", () => Open(Steam)),
                ("AMD AFMF help", () => Open(AMD)), ("OptiScaler compatibility", () => Open(Opti + "/wiki")));
        }
    }
    void Help()
    {
        Line("Shortcuts", 14, true);
        Line("Quick panel: Ctrl+Alt+F10, or hold LB+RB and press View. Full dashboard: Ctrl+Alt+F11, or hold LB+RB and press Menu.");
        Line("Controller chords require XInput and may also reach the game. Lenovo button bindings remain unchanged. Use borderless/windowed mode for the smoothest overlay experience.");
    }
    async void Forward(string label, params ushort[] keys)
    {
        if (shortcutsActive) return;
        if (returnWindow == IntPtr.Zero || !Native.IsWindow(returnWindow) || Native.WindowProcess(returnWindow) != returnProcess)
        {
            Note("No game window captured. Focus your game, then open Frame Hub from there.");
            return;
        }
        shortcutsActive = true;
        try
        {
            Hide();
            Native.SetForegroundWindow(returnWindow);
            await Task.Delay(160);
            if (Native.GetForegroundWindow() != returnWindow || Native.WindowProcess(returnWindow) != returnProcess)
                throw new InvalidOperationException("Windows did not return focus to the same target. No shortcut was sent.");
            if (!Native.Send(keys))
                throw new InvalidOperationException("Windows blocked the shortcut. Run the game and Frame Hub at the same privilege level.");
            Note($"{label}: shortcut sent to {returnProcess}. Verify the result in the game or vendor overlay.");
        }
        catch (Exception e) { Show(); Activate(); ShowError(e); }
        finally { shortcutsActive = false; }
    }

    void QuickSettings()
    {
        Line("Quick settings", 14, true);
        Buttons(("Volume −", () => AdjustVolume(-10)), ("Volume +", () => AdjustVolume(10)),
            ("Brightness −", () => ChangeBrightness(-10)), ("Brightness +", () => ChangeBrightness(10)));
        Buttons(("Resolution / orientation", () => Open("ms-settings:display")),
            ("Refresh rate / VRR", () => Open("ms-settings:display-advanced")),
            ("Power & battery", () => Open("ms-settings:powersleep")));
        var power = SystemInformation.PowerStatus;
        Line($"Battery: {(power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Unknown) ? "unknown" : $"{power.BatteryLifePercent:P0}")}  •  AC: {power.PowerLineStatus}");
        Line("TDP, fan, controller, performance overlay and Lenovo-specific settings remain in the stock Lenovo sidebar. On the original Legion Go, Lenovo documents Legion L + Y for thermal modes.");
    }
    void AdjustVolume(int delta) => Note($"System volume: {Native.Volume(Math.Clamp(Native.Volume() + delta, 0, 100))}%.");
    void ChangeBrightness(int delta) => Run("Setting brightness", async () =>
    {
        string result = await Native.WindowsQuery("$d=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness | Where-Object Active | Select-Object -First 1; " +
            "if(!$d){throw 'No compatible internal brightness control; use Windows Display settings.'}; " +
            "$m=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods | Where-Object {$_.InstanceName -eq $d.InstanceName} | Select-Object -First 1; " +
            "if(!$m){throw 'No brightness method is available.'}; " +
            $"$v=[byte][Math]::Max(0,[Math]::Min(100,([int]$d.CurrentBrightness)+({delta}))); " +
            "$r=Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]1;Brightness=$v}; " +
            "if($r.ReturnValue -ne 0){throw 'Windows rejected the brightness change.'}; $v");
        return $"Windows accepted brightness request: {result}%.";
    });

    void OptiPage()
    {
        Line("OptiScaler package manager", 14, true);
        Line("For compatible offline games only. Do not use with anti-cheat or protected multiplayer titles. Close the game before making changes.");
        Buttons(("Official releases", () => Open(Opti + "/releases")), ("Check latest release", CheckRelease),
            ("Install & compatibility guide", () => Open(Opti + "/wiki/Manual-Installation")));
        Buttons(("Choose game .exe", ChooseGame), ("Choose extracted package", ChoosePackage));
        Field(game); Field(package);
        Line("Loader (match the upstream guide for your game)"); Field(loader);
        Line("Version label (for comparing with GitHub)"); Field(version);
        Buttons(("Install / replace", () => ChangeOpti(false)), ("Remove managed files", () => ChangeOpti(true)),
            ("Inspect installed receipt", InspectOpti));
        Line("Install from an extracted official release containing OptiScaler.dll. The manager refuses to overwrite unrelated files and leaves OptiScaler.ini in place on update or removal.");
    }
    void ChooseGame()
    {
        using var dialog = new OpenFileDialog { Filter = "Game executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) { game.Text = dialog.FileName; InspectOpti(); }
    }
    void ChoosePackage()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the extracted folder containing OptiScaler.dll", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) == DialogResult.OK) { package.Text = dialog.SelectedPath; Note("Extracted package selected. Check the version and the loader name before installing."); }
    }
    string GameDirectory()
    {
        if (!File.Exists(game.Text)) throw new InvalidOperationException("Select an existing game executable first.");
        return Path.GetDirectoryName(game.Text)!;
    }
    void InspectOpti()
    {
        var directory = GameDirectory();
        if (Directory.Exists(Path.Combine(directory, OptiFiles.Journal)))
            Note("A previous transaction needs inspection. See Recovery in the README.");
        else if (OptiFiles.Read(directory) is { } receipt)
        {
            Note($"Managed version: {receipt.Version}. {receipt.Files.Count} tracked files.");
            var activeLoader = OptiFiles.LoaderNames.FirstOrDefault(name => receipt.Files.ContainsKey(name));
            if (activeLoader is not null) loader.SelectedItem = activeLoader;
        }
        else Note("No Frame Hub-managed installation in this game folder.");
    }
    void CheckRelease() => Run("Checking official OptiScaler releases", async () =>
    {
        var release = await Http.GetFromJsonAsync<Release>("https://api.github.com/repos/optiscaler/OptiScaler/releases/latest");
        if (string.IsNullOrWhiteSpace(release?.Tag)) throw new IOException("GitHub did not return an official release tag.");
        var installed = File.Exists(game.Text) ? OptiFiles.Read(GameDirectory())?.Version : null;
        string info = $"Latest tag reported by GitHub: {release.Tag}.";
        if (installed is not null) info += $" Selected game’s managed label: {installed}.";
        MessageBox.Show(this, info + "\n\nUse Official releases to select and download a package. Nothing was downloaded or installed.", Text);
        return info;
    });
    void ChangeOpti(bool remove)
    {
        if (busy) { Note("Another operation is already running."); return; }
        var dir = GameDirectory();
        var source = remove ? null : package.Text;
        if (!remove && !Directory.Exists(source)) throw new InvalidOperationException("Select the extracted official package folder.");
        string name = Path.GetFileNameWithoutExtension(game.Text);
        using var running = new ProcessList(Process.GetProcessesByName(name));
        if (running.Items.Length != 0) throw new InvalidOperationException("Close the game before changing its files.");
        var proxy = loader.Text;
        string build = string.IsNullOrWhiteSpace(version.Text) ? "version not specified" : version.Text.Trim();
        string summary = remove ?
            $"Remove Frame Hub-managed OptiScaler files from:\n{dir}\n\nKeep OptiScaler.ini?" :
            $"Install or replace OptiScaler in:\n{dir}\n\nFrom: {source}\nLoader: {proxy}\n\nConfirm you trust this package and this is a compatible offline game without anti-cheat.";
        if (MessageBox.Show(this, summary, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        Run(remove ? "Removing managed files" : "Staging OptiScaler", async () =>
        {
            var receipt = await Task.Run(() => OptiFiles.Change(dir, source, proxy, build));
            return receipt is null ? "Managed files removed. Existing OptiScaler.ini was kept." : $"Installed {receipt.Version}: {receipt.Files.Count} tracked files.";
        });
    }
    sealed class ProcessList(Process[] items) : IDisposable
    {
        internal Process[] Items => items;
        public void Dispose() { foreach (var process in items) process.Dispose(); }
    }

    void Drivers()
    {
        Line("Driver and app maintenance", 14, true);
        Line("Read your device and display driver details here. For Lenovo graphics, chipset, BIOS and controller updates, use Lenovo’s updater and confirm your exact model before installing anything.");
        Buttons(("Show model & display drivers", DriverInventory),
            ("Scan Windows Update drivers", WindowsDriverScan),
            ("Lenovo update instructions", () => Open(LenovoScan)),
            ("Lenovo support", () => Open(LenovoSupport)));
        Buttons(("Windows Update", () => Open("ms-settings:windowsupdate")),
            ("Optional updates", () => Open("ms-settings:windowsupdate-optionalupdates")),
            ("Device Manager", () => Open("devmgmt.msc")));
        Line("For Lossless Scaling, Steam handles install, update and uninstall. This app does not bundle a Steam license or flash, replace, or remove drivers.");
        Buttons(("Open Steam library", () => Open("steam://open/games")), ("Lossless Scaling store", () => Open(Steam)),
            ("Windows installed apps", () => Open("ms-settings:appsfeatures")), ("OptiScaler manager", () => SelectTab("OptiScaler")));
    }
    void DriverInventory() => Run("Reading driver inventory", async () =>
    {
        var result = await Native.WindowsQuery("$s=Get-CimInstance Win32_ComputerSystem; $b=Get-CimInstance Win32_BIOS; " +
            "'System: '+$s.Manufacturer+' '+$s.Model+' ('+$s.SystemSKUNumber+')'; 'BIOS: '+$b.SMBIOSBIOSVersion; ''; " +
            "Get-CimInstance Win32_PnPSignedDriver -Filter \"DeviceClass='DISPLAY'\" | Select-Object DeviceName,DriverVersion,DriverProviderName,InfName | Format-List | Out-String");
        Details("Installed display drivers", result);
        return "Installed model and display driver details read.";
    });
    void WindowsDriverScan() => Run("Scanning the Windows Update catalog (read-only)", async () =>
    {
        var result = await Native.WindowsQuery("$s=New-Object -ComObject Microsoft.Update.Session; $u=$s.CreateUpdateSearcher(); " +
            "$r=$u.Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); if($r.ResultCode -ne 2){throw ('Windows Update scan did not complete cleanly: '+$r.ResultCode)}; " +
            "if($r.Updates.Count -eq 0){'No applicable drivers were reported by Windows Update.'}else{$r.Updates | ForEach-Object {$_.Title}}", 110);
        Details("Windows Update driver scan", result + "\r\n\r\nThis is not a Lenovo catalog scan. No changes were made.");
        return "Windows Update driver scan complete. No changes were made.";
    });
    void Details(string title, string value)
    {
        using var window = new Form { Text = title, Size = new Size(630, 385), StartPosition = FormStartPosition.CenterParent };
        var body = new TextBox { Text = value.Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        window.Controls.Add(body); window.ShowDialog(this);
    }
    async void Run(string description, Func<Task<string>> work)
    {
        if (busy) { Note("Another operation is already running."); return; }
        busy = true;
        Note(description + "…");
        try { Note(await work()); }
        catch (Exception e) { ShowError(e); }
        finally { busy = false; }
    }
    void ShowError(Exception e)
    {
        Note("Action not completed. See details.");
        Details("Action not completed", e.Message);
    }
    static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
