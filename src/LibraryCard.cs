using System.Collections.Concurrent;
using System.Drawing.Drawing2D;

namespace FrameHub;

internal static class CoverArt
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly SemaphoreSlim Concurrent = new(4);
    static readonly ConcurrentDictionary<uint, byte[]> Cache = new();
    internal static async Task<byte[]?> Load(string id, CancellationToken cancellation)
    {
        if (!uint.TryParse(id, out var app) || app == 0) return null;
        if (Cache.TryGetValue(app, out var cached)) return cached;
        await Concurrent.WaitAsync(cancellation);
        try
        {
            if (Cache.TryGetValue(app, out cached)) return cached;
            using var response = await Http.GetAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{app}/library_600x900.jpg",
                HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "image/jpeg" ||
                response.Content.Headers.ContentLength > 1_000_000) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var result = new MemoryStream();
            var buffer = new byte[16384];
            int n;
            while ((n = await stream.ReadAsync(buffer, cancellation)) > 0)
            {
                if (result.Length + n > 1_000_000) return null;
                result.Write(buffer, 0, n);
            }
            var bytes = result.ToArray();
            if (Cache.Count < 128) Cache.TryAdd(app, bytes);
            return bytes;
        }
        finally { Concurrent.Release(); }
    }
}

internal sealed class LibraryCard : Button
{
    readonly GameShelf.Entry entry;
    readonly CancellationTokenSource lifetime = new();
    readonly Font title = new("Segoe UI Semibold", 9);
    readonly Font initials = new("Segoe UI Semibold", 26);
    readonly Font badge = new("Segoe UI", 8);
    Bitmap? art;
    Icon? gameIcon;
    bool hovered;

    internal LibraryCard(GameShelf.Entry game)
    {
        entry = game;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = game.Name; AccessibleName = $"{game.Name}, {(game.Steam ? "Steam" : "manually added")}";
        BackColor = Color.FromArgb(16, 19, 31); ForeColor = Color.White;
        Padding = new(0); Margin = new(6); Cursor = Cursors.Hand; Size = new(132, 220);
        if (entry.Steam) _ = LoadCover(lifetime.Token);
        else
        {
            try { if (File.Exists(entry.Location)) gameIcon = Icon.ExtractAssociatedIcon(entry.Location); }
            catch (Exception e) when (e is ArgumentException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
        }
    }
    async Task LoadCover(CancellationToken cancellation)
    {
        try
        {
            var bytes = await CoverArt.Load(entry.Location, cancellation);
            if (bytes is null || cancellation.IsCancellationRequested || IsDisposed) return;
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (image.Width > 1500 || image.Height > 2000) return;
            art = new Bitmap(image);
            Invalidate();
        }
        catch (Exception e) when (e is OperationCanceledException or ArgumentException or HttpRequestException or IOException or System.Runtime.InteropServices.ExternalException) { }
    }
    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = 2 * radius;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int footer = Math.Max(51, (int)(50 * DeviceDpi / 96.0));
        var outer = new Rectangle(1, 1, Width - 3, Height - 3);
        var cover = new Rectangle(2, 2, Width - 4, Height - footer);
        using var shape = Rounded(outer, 10);
        using var bg = new SolidBrush(Color.FromArgb(30, 35, 52));
        g.FillPath(bg, shape);
        var state = g.Save();
        g.SetClip(shape);
        if (art is not null)
        {
            float scale = Math.Max((float)cover.Width / art.Width, (float)cover.Height / art.Height);
            var imageRect = new RectangleF(cover.Left + (cover.Width - art.Width * scale) / 2,
                cover.Top + (cover.Height - art.Height * scale) / 2, art.Width * scale, art.Height * scale);
            g.SetClip(cover, CombineMode.Intersect);
            g.DrawImage(art, imageRect);
        }
        else
        {
            uint seed = 17;
            foreach (var c in entry.Name) seed = unchecked(seed * 31 + c);
            using var gradient = new LinearGradientBrush(cover, Color.FromArgb(40 + (int)(seed % 48), 36 + (int)(seed % 22), 104 + (int)(seed % 58)),
                Color.FromArgb(20, 27, 51), 55f);
            g.FillRectangle(gradient, cover);
            if (gameIcon is not null) g.DrawIcon(gameIcon, new Rectangle(cover.Left + (cover.Width - 40) / 2, cover.Top + (cover.Height - 40) / 2, 40, 40));
            else
            {
                string mark = string.Concat(entry.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[..1])).ToUpperInvariant();
                TextRenderer.DrawText(g, mark, initials, cover, Color.FromArgb(220, 220, 255),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        g.Restore(state);
        var titleRect = new Rectangle(10, cover.Bottom + 7, Width - 20, Math.Max(17, (int)(18 * DeviceDpi / 96.0)));
        TextRenderer.DrawText(g, entry.Name, title, titleRect, Color.White,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var tagRect = new Rectangle(10, titleRect.Bottom + 3, Width - 20, Height - titleRect.Bottom - 5);
        TextRenderer.DrawText(g, entry.Steam ? "STEAM" : "LOCAL GAME", badge, tagRect,
            Color.FromArgb(174, 166, 245), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        using var outline = new Pen(Focused || hovered ? Color.FromArgb(173, 157, 255) : Color.FromArgb(65, 70, 100), Focused ? 2 : 1);
        g.DrawPath(outline, shape);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lifetime.Cancel(); lifetime.Dispose();
            art?.Dispose(); gameIcon?.Dispose(); title.Dispose(); initials.Dispose(); badge.Dispose();
        }
        base.Dispose(disposing);
    }
}
