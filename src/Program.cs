namespace FrameHub;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args is ["--ui-smoke", var output])
        {
            ApplicationConfiguration.Initialize();
            using var fixture = new Hub(fixture: true);
            fixture.CaptureFixture(Path.GetFullPath(output));
            return;
        }
        using var instance = new Mutex(true, @"Local\LegionFrameHub", out bool first);
        if (!first)
        {
            MessageBox.Show("Legion Frame Hub is already running. Open it from the tray or press Ctrl+Alt+F11.",
                "Legion Frame Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new Hub());
    }
}
