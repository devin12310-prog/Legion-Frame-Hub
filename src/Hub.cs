using System.Diagnostics;

namespace FrameHub;

internal sealed class Hub : Form
{
    const string Opti = "https://github.com/optiscaler/OptiScaler";
    const string LenovoScan = "https://support.lenovo.com/us/en/solutions/ht516319-how-to-use-legion-space-to-update-drivers-legion-go-8apu1";
    const string LenovoSupport = "https://pcsupport.lenovo.com/us/en/";
    const string AMD = "https://www.amd.com/en/products/software/adrenalin/afmf.html";
    const string Steam = "https://store.steampowered.com/app/993090/Lossless_Scaling/";
    static readonly Color Surface = Color.FromArgb(16, 19, 31), Card = Color.FromArgb(32, 36, 56),
        Ink = Color.FromArgb(245, 244, 255), Muted = Color.FromArgb(177, 182, 207), Accent = Color.FromArgb(176, 159, 255);
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer controller = new() { Interval = 60 };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft };
    readonly ProgressBar operation = new() { Dock = DockStyle.Bottom, Height = 5, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false };
    readonly Panel scroller = new() { AutoScroll = true, Dock = DockStyle.Fill, Padding = new(16, 12, 16, 12) };
    readonly TableLayoutPanel stack = new() { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
    readonly TextBox game = new() { ReadOnly = true, PlaceholderText = "Select the game’s main .exe" };
    readonly TextBox package = new() { ReadOnly = true, PlaceholderText = "Select the extracted OptiScaler folder" };
    readonly TextBox version = new() { PlaceholderText = "Release tag, e.g. v0.x.y (optional)", MaxLength = 80 };
    readonly ComboBox loader = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Rectangle fullMinimum = new(0, 0, 580, 420);
    string tab = "Installed Games", message = "Ready.";
    List<GameShelf.Entry> localGames = [];
    GameShelf.Entry[] steamGames = [];
    bool quick, quitting, busy, shortcutsActive, advancedScaler;
    GameShelf.Entry? selectedGame;
    int libraryTag, libraryPage, gameTool, scalerView, frameView, settingsView;
    string libraryQuery = "";
    int previousChord;
    IntPtr returnWindow;
    string returnProcess = "";
    readonly bool fixture;

    internal Hub(bool fixture = false)
    {
        this.fixture = fixture;
        Text = "Legion Frame Hub";
        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null) Icon = appIcon;
        BackColor = Surface; ForeColor = Ink; Font = new("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = fullMinimum.Size;
        Size = new(990, 735);
        StartPosition = FormStartPosition.CenterScreen;
        status.BackColor = Card; status.ForeColor = Muted;
        scroller.Controls.Add(stack); Controls.Add(scroller); Controls.Add(operation); Controls.Add(status);
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        loader.Items.AddRange(OptiFiles.LoaderNames.Cast<object>().ToArray()); loader.SelectedIndex = 0;

        tray = new NotifyIcon { Icon = appIcon ?? SystemIcons.Application, Text = "Legion Frame Hub", Visible = !fixture, ContextMenuStrip = new() };
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
        if (!fixture) controller.Start();
        FormClosing += (_, e) =>
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        FormClosed += (_, _) => { controller.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); appIcon?.Dispose(); };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        if (fixture)
        {
            localGames = new[] { "Neon Circuit", "Echoes of the Wild", "Midnight Drift", "Cobalt Dunes", "Skyline Zero",
                "Darkwater", "Northern Lights", "Orbit Runner", "Starline", "The Last Outpost" }.Select(title => new GameShelf.Entry(title, "", false)).ToList();
            Note("Library sample · no games scanned");
        }
        else
        {
            try { localGames = GameShelf.Load(); }
            catch (Exception) { Note("The saved game list couldn’t be read. Review games.json before adding or removing entries."); }
            try
            {
                var selected = GameShelf.LoadScalerGame();
                if (File.Exists(selected)) { game.Text = selected; InspectOpti(); }
            }
            catch (Exception) { Note("The last scaler game could not be restored. Choose the game again."); }
            Shown += (_, _) => RefreshGames();
        }
        Render();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
        if (fixture) return;
        if (!Native.RegisterHotKey(Handle, 101, 0x4003, (uint)Keys.F10))
            Note("Ctrl+Alt+F10 is unavailable. Use the tray or controller chord.");
        if (!Native.RegisterHotKey(Handle, 102, 0x4003, (uint)Keys.F11))
            Note("Ctrl+Alt+F11 is unavailable. Use the tray or controller chord.");
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!fixture)
        {
            Native.UnregisterHotKey(Handle, 101);
            Native.UnregisterHotKey(Handle, 102);
        }
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
            int width = quick ? Math.Min(work.Width, Math.Max(360, work.Width / 2)) : Math.Min(work.Width, 1180);
            int height = quick ? work.Height : Math.Min(work.Height, 760);
            Bounds = new(quick ? work.Right - width : work.Left + (work.Width - width) / 2,
                quick ? work.Top : work.Top + (work.Height - height) / 2, width, height);
        }
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
            Text = label, AutoSize = true, MinimumSize = new(112, 44),
            BackColor = Card, ForeColor = Ink, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Padding = new(10, 3, 10, 3), Margin = new(4)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(61, 63, 95);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(53, 51, 84);
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
    void Section(string title)
    {
        Add(new Panel { Height = 1, Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 59, 79), Margin = new(4, 12, 4, 3) });
        Line(title, 15, true);
    }
    void Navigation()
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true };
        foreach (var name in new[] { "Installed Games", "Scalers", "Frame Gen", "Settings" })
        {
            var button = ButtonFor(name, () => SelectTab(name));
            if (tab == name) { button.BackColor = Accent; button.ForeColor = Surface; button.FlatAppearance.MouseOverBackColor = Accent; }
            row.Controls.Add(button);
        }
        row.Controls.Add(ButtonFor("Quick panel", () => OpenMode(true, false)));
        Add(row);
    }
    void Actions(bool primary, params (string Label, Action Click)[] actions)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new(0) };
        foreach (var action in actions) row.Controls.Add(ButtonFor(action.Label, action.Click));
        if (primary && row.Controls.Count > 0 && row.Controls[0] is Button button)
        {
            button.BackColor = Accent; button.ForeColor = Surface;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(195, 182, 255);
        }
        Add(row);
    }
    void Buttons(params (string Label, Action Click)[] actions) => Actions(false, actions);
    ComboBox Picker(string name, int index, Action<int> changed, params string[] labels)
    {
        var box = new ComboBox
        {
            AccessibleName = name, Width = 216, DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30, FlatStyle = FlatStyle.Flat, IntegralHeight = false,
            BackColor = Card, ForeColor = Ink, DropDownHeight = 200, Margin = new(4, 7, 8, 6)
        };
        box.Items.AddRange(labels);
        box.SelectedIndex = index;
        box.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool active = (e.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(active ? Color.FromArgb(59, 53, 100) : Card);
            e.Graphics.FillRectangle(brush, e.Bounds);
            var text = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 15, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, box.Items[e.Index]?.ToString() ?? "", Font, text, Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        box.SelectionChangeCommitted += (_, _) => changed(box.SelectedIndex);
        return box;
    }
    void Drop(string name, int index, Action<int> changed, params string[] labels)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        row.Controls.Add(Picker(name, index, changed, labels)); Add(row);
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
            Line(quick ? "Quick panel" : "Legion Frame Hub", quick ? 18 : 19, true);
            if (quick)
                Buttons(("Full dashboard", () => OpenMode(false, false)), ("Close panel", Hide));
            else Navigation();
            if (quick)
            {
                Buttons(("Open Lenovo Legion Space", OpenLegionSpace));
                FrameGeneration();
                QuickSettings();
            }
            else
            {
                switch (tab)
                {
                    case "Scalers": ScalersPage(); break;
                    case "Frame Gen": FrameGenPage(); break;
                    case "Settings": SettingsPage(); break;
                    default: if (selectedGame is { } gameCard) GamePage(gameCard); else GamesPage(); break;
                }
            }
            status.Text = message;
        }
        finally { stack.ResumeLayout(true); }
    }
    void SelectTab(string value)
    {
        tab = value;
        if (value == "Installed Games") selectedGame = null;
        Render(); scroller.AutoScrollPosition = Point.Empty;
    }
    void OpenLegionSpace()
    {
        LegionSpace.Launch();
        if (quick) Hide();
        Note("Asked Windows to open Lenovo Legion Space.");
    }
    void RefreshGames() => Run("Reading Steam libraries", async () =>
    {
        steamGames = await Task.Run(GameShelf.SteamGames);
        if (tab == "Installed Games" && !quick) Render();
        return $"Found {steamGames.Length} installed Steam entries and {localGames.Count} added games.";
    });
    void GamesPage()
    {
        Line("Your library", 26, true);
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var search = new TextBox
        {
            Width = 218, Text = libraryQuery, PlaceholderText = "Search games", AccessibleName = "Search games",
            Font = new Font(Font.FontFamily, 12), BackColor = Card, ForeColor = Ink, Margin = new(4, 9, 7, 6)
        };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            BackColor = Surface, Padding = new(0, 2, 4, 2), AccessibleName = "Game card grid" };
        var stats = TextLine("");
        var prev = ButtonFor("Previous", () => { });
        var next = ButtonFor("Next", () => { });
        prev.Click += (_, _) => { libraryPage--; Populate(); };
        next.Click += (_, _) => { libraryPage++; Populate(); };
        void Populate()
        {
            var games = GameShelf.Filter(steamGames.Concat(localGames), libraryQuery, libraryTag);
            const int perPage = 18;
            int pages = Math.Max(1, (games.Length + perPage - 1) / perPage);
            libraryPage = Math.Clamp(libraryPage, 0, pages - 1);
            prev.Visible = pages > 1; next.Visible = pages > 1;
            prev.Enabled = libraryPage > 0; next.Enabled = libraryPage + 1 < pages;
            stats.Text = games.Length == 0 ? "No matching games" : $"{games.Length} games · Page {libraryPage + 1} of {pages}";
            grid.SuspendLayout();
            try
            {
                while (grid.Controls.Count > 0) grid.Controls[0].Dispose();
                grid.AutoScrollPosition = Point.Empty;
                if (games.Length == 0)
                    grid.Controls.Add(TextLine("Scan Steam or add a game manually. If you’re filtering, try All games."));
                foreach (var entry in games.Skip(libraryPage * perPage).Take(perPage))
                {
                    var tile = new LibraryCard(entry);
                    tile.Click += (_, _) => { selectedGame = entry; gameTool = 0; Render(); scroller.AutoScrollPosition = Point.Empty; };
                    grid.Controls.Add(tile);
                }
            }
            finally { grid.ResumeLayout(); }
        }
        var searchLabel = new Label { Text = "Search", AutoSize = true, ForeColor = Muted,
            Margin = new(8, 16, 0, 0) };
        toolbar.Controls.Add(searchLabel);
        toolbar.Controls.Add(search);
        toolbar.Controls.Add(Picker("Game tag", libraryTag, value => { libraryTag = value; libraryPage = 0; Populate(); },
            "All games", "Steam", "Added manually"));
        toolbar.Controls.Add(ButtonFor("Scan Steam", RefreshGames));
        toolbar.Controls.Add(ButtonFor("+ Add game", AddGame));
        Add(toolbar);
        var paging = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        paging.Controls.Add(stats); paging.Controls.Add(prev); paging.Controls.Add(next); Add(paging);
        var shelf = new Panel { Height = Math.Max(245, ClientSize.Height - HeightBudget()), Dock = DockStyle.Fill, Margin = new(0) };
        shelf.Controls.Add(grid); Add(shelf);
        search.TextChanged += (_, _) => { libraryQuery = search.Text; libraryPage = 0; Populate(); };
        Populate();
        int HeightBudget() => (int)(221 * DeviceDpi / 96.0);
    }
    void PlayGame(GameShelf.Entry entry)
    {
        if (entry.Steam) { if (uint.TryParse(entry.Location, out var id)) Open("steam://run/" + id); }
        else
        {
            if (!File.Exists(entry.Location)) throw new FileNotFoundException("This game executable has moved. Add its current location.");
            using var started = Process.Start(new ProcessStartInfo(entry.Location)
            { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(entry.Location)! });
        }
    }
    void GamePage(GameShelf.Entry entry)
    {
        Buttons(("‹ Back to library", () => SelectTab("Installed Games")));
        Line(entry.Name, 23, true);
        Line(entry.Steam ? "STEAM" : "LOCAL GAME");
        Actions(true, ("Play game", () => PlayGame(entry)));
        Section("Enhance this game");
        Drop("Recommendation", gameTool, choice => { gameTool = choice; Render(); }, "Recommended", "OptiScaler", "Magpie", "Frame generation");
        switch (gameTool)
        {
            case 1:
                Line("For compatible offline games. Check the compatibility guide before modifying game files.");
                Actions(true, ("Set up OptiScaler", () => SetupGame(entry, false)));
                break;
            case 2:
                Line("A free way to sharpen a windowed game without modifying its files.");
                Actions(true, ("Open Magpie tools", () => { scalerView = 2; SelectTab("Scalers"); }));
                break;
            case 3:
                Line("Start with the game’s own Graphics menu. For compatible offline games, the Hub can also manage OptiScaler.");
                Actions(true, ("Open Frame Gen", () => { frameView = 0; SelectTab("Frame Gen"); }), ("Set up OptiScaler", () => SetupGame(entry, true)));
                break;
            default:
                Line("Start with the game’s built-in Graphics settings. Want sharper windowed play without changing files? Try Magpie.");
                Line("Use OptiScaler only after checking that the game is compatible and has no anti-cheat.");
                Actions(true, ("Try Magpie", () => { scalerView = 2; SelectTab("Scalers"); }), ("Set up OptiScaler", () => SetupGame(entry, false)));
                break;
        }
        if (!entry.Steam)
            Buttons(("Remove shortcut…", () =>
            {
                if (MessageBox.Show(this, "Remove this shortcut? Game files will not be deleted.", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                var next = localGames.Where(item => item != entry).ToList();
                GameShelf.Save(next); localGames = next;
                SelectTab("Installed Games"); Note("Removed the shortcut. No game files were deleted.");
            }));
    }
    void SetupGame(GameShelf.Entry entry, bool frame)
    {
        if (entry.Steam)
        {
            var known = GameShelf.LinkedSteamExe(entry.Location);
            if (File.Exists(known) && InsideGame(known!, entry.InstallFolder)) SetScalerGame(known!);
            else if (!File.Exists(game.Text) || !InsideGame(game.Text, entry.InstallFolder))
            {
                using var dialog = new OpenFileDialog { Title = $"Choose the main EXE for {entry.Name}", Filter = "Game executable (*.exe)|*.exe",
                    CheckFileExists = true, InitialDirectory = entry.InstallFolder ?? "" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (entry.InstallFolder is not null && !InsideGame(dialog.FileName, entry.InstallFolder))
                    throw new IOException("Choose an EXE inside the selected Steam game’s folder.");
                SetScalerGame(dialog.FileName);
            }
            GameShelf.LinkSteamExe(entry.Location, game.Text);
        }
        else SetScalerGame(entry.Location);
        scalerView = 1; frameView = 1;
        SelectTab(frame ? "Frame Gen" : "Scalers");
    }
    static bool InsideGame(string file, string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder) &&
        Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal void CaptureFixture(string folder)
    {
        if (!fixture) return;
        Directory.CreateDirectory(folder);
        Show();
        Application.DoEvents();
        void Capture(string name)
        {
            Application.DoEvents();
            using var image = new Bitmap(Width, Height);
            DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
            image.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Capture("library");
        selectedGame = localGames[0]; gameTool = 0; Render(); Capture("recommendation");
        SelectTab("Scalers"); Capture("scalers");
        quitting = true;
        Close();
    }
    void AddGame()
    {
        using var dialog = new OpenFileDialog { Title = "Choose a game executable", Filter = "Game executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (localGames.Any(item => item.Location.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase))) { Note("That game is already in the list."); return; }
        List<GameShelf.Entry> next = [.. localGames, new(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, false)];
        GameShelf.Save(next); localGames = next;
        selectedGame = null; libraryTag = 0; libraryQuery = ""; libraryPage = 0;
        Render(); Note("Game shortcut added.");
    }
    void SettingsPage()
    {
        Line("Settings", 25, true);
        Drop("Settings category", settingsView, choice => { settingsView = choice; Render(); },
            "Updates", "Legion buttons & fan", "Quick settings", "Drivers & apps");
        switch (settingsView)
        {
            case 1:
                Section("Legion buttons and fan");
                Buttons(("Open Lenovo Legion Space", OpenLegionSpace), ("Close Legion Space UI…", CloseSpace), ("Lenovo downloads", () => Open(LegionSpace.Support)));
                Buttons(("Open Handheld Companion", LegionSpace.OpenCompanion), ("Setup guide", () => Open(LegionSpace.Guide)),
                    ("Get Handheld Companion", () => Open(LegionSpace.Companion)));
                Line("In Handheld Companion, map Legion R to Ctrl+Alt+F10 and Legion L to Ctrl+Alt+F11. Set fan curves in Lenovo Legion Space or Handheld Companion, not both. Frame Hub does not change Lenovo services or fan curves.");
                Help();
                break;
            case 2: QuickSettings(); break;
            case 3: Drivers(); break;
            default:
                Section($"Frame Hub  ·  {HubUpdate.Current.ToString(3)}");
                Actions(true, ("Check for Hub update", UpdateApp), ("GitHub releases", () => Open(HubUpdate.Releases)), ("Get GitHub CLI", () => Open("https://cli.github.com/")));
                Line("The private Hub updater uses a signed-in GitHub CLI. The releases page also works in your browser.");
                Section("Free tool updates");
                Buttons(("Install / update Magpie", InstallMagpie), ("Install / update selected game", InstallOpti),
                    ("Check Magpie", () => CheckTool(ToolCatalog.MagpieRepo)), ("Check OptiScaler", () => CheckTool(ToolCatalog.OptiRepo)));
                break;
        }
    }
    void CloseSpace()
    {
        if (MessageBox.Show(this,
            "Close the Lenovo Legion Space window? Frame Hub will try a normal close first, then force-close only the verified UI process if needed. Unsaved Legion Space changes may be lost. No services or fan settings are changed.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        Run("Closing Lenovo Legion Space UI", LegionSpace.CloseUi);
    }
    void UpdateApp() => Run("Checking GitHub for a Frame Hub update", async () =>
    {
        var update = await HubUpdate.Find();
        if (update is null) return "This is the latest released version.";
        var answer = MessageBox.Show(this, $"Install Frame Hub {update.Tag} from GitHub? The app will restart after it verifies the download. Your saved game list and OptiScaler files will not be changed.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.OK) return "Update canceled.";
        Note($"Downloading and verifying Frame Hub {update.Tag}…");
        var path = await HubUpdate.Download(update);
        HubUpdate.ReplaceWhenClosed(path, update.Digest);
        quitting = true;
        BeginInvoke(Close);
        return "Verified. Restarting for update.";
    });
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
        }
    }
    void FrameGenPage()
    {
        Line("Frame Gen", 25, true);
        Drop("Frame generation tool", frameView, choice => { frameView = choice; Render(); },
            "Recommended", "OptiScaler", "In-game controls", "AMD AFMF", "Lossless Scaling");
        switch (frameView)
        {
            case 1: OptiPage(); break;
            case 2: FrameGeneration(); break;
            case 3:
                Section("AMD Fluid Motion Frames");
                Line("Use AMD Software and a supported driver. This is not a separate Hub download.");
                Actions(true, ("AMD setup guide", () => Open(AMD)), ("Send AFMF shortcut", () => Forward("AFMF", 0x12, 0x10, 0x47)));
                break;
            case 4:
                Section("Lossless Scaling  ·  Paid");
                Line("Steam handles installation and updates.");
                Actions(true, ("Open on Steam", () => Open(Steam)), ("Launch", () => Open("steam://run/993090")));
                break;
            default:
                Section("Start with the game’s settings");
                Line("If the game offers frame generation, try its built-in option first. For a compatible offline game, OptiScaler is the free Hub-managed option.");
                Actions(true, ("Set up OptiScaler", () => { frameView = 1; Render(); }), ("In-game controls", () => { frameView = 2; Render(); }));
                Line("Use only one frame-generation method at a time.");
                break;
        }
    }
    void ScalersPage()
    {
        Line("Scalers", 25, true);
        Drop("Scaling tool", scalerView, choice => { scalerView = choice; Render(); }, "Recommended", "OptiScaler", "Magpie");
        switch (scalerView)
        {
            case 1: OptiPage(); break;
            case 2: MagpiePage(); break;
            default:
                Section("Scale without changing game files");
                Line("Start with the game’s built-in upscaler if it has one. If you want to upscale an ordinary game window, try Magpie.");
                Actions(true, ("Try Magpie", () => { scalerView = 2; Render(); }), ("Browse OptiScaler", () => { scalerView = 1; Render(); }));
                Line("OptiScaler is a separate option for compatible offline games. Check compatibility first.");
                break;
        }
    }
    void Help()
    {
        Line("Shortcuts", 14, true);
        Line("Quick panel: Ctrl+Alt+F10, or hold LB+RB and press View. Full dashboard: Ctrl+Alt+F11, or hold LB+RB and press Menu.");
        Line("Controller chords require XInput and may also reach the game. For optional physical Legion button mapping, see Settings. Use borderless/windowed mode for the smoothest overlay experience.");
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
        Section("OptiScaler  •  Free");
        Line("Compatible offline games only. Don’t use with anti-cheat.");
        var installed = OptiVersion();
        var hasGame = File.Exists(game.Text);
        Line(hasGame ? $"{Path.GetFileNameWithoutExtension(game.Text)}  ·  {installed ?? "Not installed"}" : "No game selected", 11, true);
        if (hasGame)
            Actions(true, (installed is null ? "Install from GitHub" : "Update from GitHub", InstallOpti), ("Change game", ChooseGame));
        else Actions(true, ("Choose game", ChooseGame), ("Compatibility guide", () => Open(Opti + "/wiki")));
        Buttons((advancedScaler ? "Hide options" : "More options", () => { advancedScaler = !advancedScaler; Render(); }));
        if (advancedScaler)
        {
            Field(game);
            Line("Loader: change only if the game’s compatibility guide calls for another DLL."); Field(loader);
            Buttons(("Check latest release", () => CheckTool(ToolCatalog.OptiRepo)), ("Remove managed files", () => ChangeOpti(true)),
                ("Official release notes", () => Open(Opti + "/releases")));
            Buttons(("Choose extracted package", ChoosePackage), ("Manual install / replace", () => ChangeOpti(false)),
                ("Install guide", () => Open(Opti + "/wiki/Manual-Installation")));
            Field(package); Field(version);
        }
    }
    string? OptiVersion()
    {
        if (!File.Exists(game.Text)) return null;
        try { return OptiFiles.Read(GameDirectory())?.Version; }
        catch (Exception) { return "Unknown; inspect this game folder"; }
    }
    static bool UpToDate(string? installed, string tag)
    {
        if (string.Equals(installed, tag, StringComparison.OrdinalIgnoreCase)) return true;
        return Version.TryParse(installed?.TrimStart('v', 'V'), out var current) &&
            Version.TryParse(tag.TrimStart('v', 'V'), out var latest) && current >= latest;
    }
    void CheckTool(string repo) => Run("Checking the official GitHub release", async () =>
    {
        var asset = await ToolCatalog.Latest(repo);
        var name = repo == ToolCatalog.OptiRepo ? "OptiScaler" : "Magpie";
        var installed = repo == ToolCatalog.OptiRepo ? OptiVersion() : ToolCatalog.MagpieInstalled();
        var result = UpToDate(installed, asset.Tag) ? "Installed version is up to date." : "Install or update from the tool’s card.";
        var message = $"{name}: GitHub {asset.Tag}  •  Managed: {installed ?? "Not installed"}. {result}";
        MessageBox.Show(this, message, Text);
        return message;
    });
    void InstallOpti()
    {
        if (busy) { Note("Wait for the current operation to finish."); return; }
        var dir = GameDirectory();
        var exe = game.Text;
        var proxy = loader.Text;
        EnsureGameClosed(exe);
        var previous = OptiFiles.Read(dir)?.Version;
        Run("Checking official OptiScaler releases", async () =>
        {
            var asset = await ToolCatalog.Latest(ToolCatalog.OptiRepo);
            if (UpToDate(previous, asset.Tag)) return $"OptiScaler {previous} is up to date for this game.";
            if (MessageBox.Show(this,
                $"Download and {(previous is null ? "install" : "update to")} OptiScaler {asset.Tag} from its official GitHub release?\n\nGame: {Path.GetFileName(exe)}\nLoader: {proxy}\n\nConfirm this game is offline, compatible and does not use anti-cheat.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return "OptiScaler installation canceled.";
            using var staged = await ToolCatalog.Fetch(asset, "OptiScaler.dll", Note);
            EnsureGameClosed(exe);
            Note("Installing verified OptiScaler files…");
            await Task.Run(() => OptiFiles.Change(dir, staged.Payload, proxy, asset.Tag));
            Render();
            return $"OptiScaler {asset.Tag} is installed for {Path.GetFileNameWithoutExtension(exe)}.";
        });
    }
    void MagpiePage()
    {
        Section("Magpie  •  Free");
        Line("Upscale a windowed game without changing its files. This is a scaler, not frame generation.");
        var version = ToolCatalog.MagpieInstalled();
        Line($"Managed version: {version ?? "Not installed"}", 11, true);
        if (version is null) Actions(true, ("Install Magpie", InstallMagpie), ("Release notes", () => Open("https://github.com/" + ToolCatalog.MagpieRepo + "/releases")));
        else
        {
            Actions(true, ("Open Magpie", ToolCatalog.LaunchMagpie), ("Update Magpie", InstallMagpie));
            Buttons(("Scale game window", () => Forward("Magpie windowed scaling", 0x12, 0x10, 0x51)), ("Remove Magpie", RemoveMagpie));
            Line("Open Magpie first, then summon Frame Hub while your windowed game is focused.");
        }
    }
    void InstallMagpie() => Run("Checking official Magpie releases", async () =>
    {
        var asset = await ToolCatalog.Latest(ToolCatalog.MagpieRepo);
        var previous = ToolCatalog.MagpieInstalled();
        if (UpToDate(previous, asset.Tag)) return $"Magpie {previous} is up to date.";
        if (MessageBox.Show(this, $"Download and {(previous is null ? "install" : "update to")} Magpie {asset.Tag} from its official GitHub release? Close Magpie first if it is running.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return "Magpie installation canceled.";
        using var staged = await ToolCatalog.Fetch(asset, "Magpie.exe", Note);
        await Task.Run(() => ToolCatalog.InstallMagpie(staged.Payload, asset.Tag));
        Render();
        return $"Magpie {asset.Tag} is installed. Select Open Magpie to launch it.";
    });
    void RemoveMagpie()
    {
        if (busy) return;
        if (MessageBox.Show(this, "Remove the Hub-managed Magpie files? This does not remove your games.", Text,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        Run("Removing managed Magpie files", async () =>
        {
            await Task.Run(ToolCatalog.RemoveMagpie);
            Render();
            return "Hub-managed Magpie files removed.";
        });
    }
    void ChooseGame()
    {
        using var dialog = new OpenFileDialog { Filter = "Game executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) { SetScalerGame(dialog.FileName); Render(); }
    }
    void SetScalerGame(string path)
    {
        GameShelf.SaveScalerGame(path);
        game.Text = path;
        loader.SelectedIndex = 0;
        InspectOpti();
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
    void ChangeOpti(bool remove)
    {
        if (busy) { Note("Another operation is already running."); return; }
        var dir = GameDirectory();
        var source = remove ? null : package.Text;
        if (!remove && !Directory.Exists(source)) throw new InvalidOperationException("Select the extracted official package folder.");
        EnsureGameClosed(game.Text);
        var proxy = loader.Text;
        string build = string.IsNullOrWhiteSpace(version.Text) ? "version not specified" : version.Text.Trim();
        string summary = remove ?
            $"Remove Frame Hub-managed OptiScaler files from:\n{dir}\n\nKeep OptiScaler.ini?" :
            $"Install or replace OptiScaler in:\n{dir}\n\nFrom: {source}\nLoader: {proxy}\n\nConfirm you trust this package and this is a compatible offline game without anti-cheat.";
        if (MessageBox.Show(this, summary, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        Run(remove ? "Removing managed files" : "Staging OptiScaler", async () =>
        {
            var receipt = await Task.Run(() => OptiFiles.Change(dir, source, proxy, build));
            Render();
            return receipt is null ? "Managed files removed. Existing OptiScaler.ini was kept." : $"Installed {receipt.Version}: {receipt.Files.Count} tracked files.";
        });
    }
    static void EnsureGameClosed(string file)
    {
        var matches = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(file));
        foreach (var process in matches) process.Dispose();
        if (matches.Length != 0) throw new InvalidOperationException("Close the game before changing its files.");
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
            ("Windows installed apps", () => Open("ms-settings:appsfeatures")), ("OptiScaler manager", () => SelectTab("Scalers")));
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
        operation.Visible = true;
        Note(description + "…");
        try { Note(await work()); }
        catch (Exception e) { ShowError(e); }
        finally { busy = false; operation.Visible = false; }
    }
    void ShowError(Exception e)
    {
        Note("Action not completed. See details.");
        Details("Action not completed", e.Message);
    }
    static void Open(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
