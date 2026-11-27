using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FrameHub;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint mods, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint ReadPad(uint index, out Pad state);

    [StructLayout(LayoutKind.Sequential)]
    struct Pad { public uint packet; public ushort buttons; public byte left, right; public short lx, ly, rx, ry; }

    internal static int PadChord()
    {
        for (uint i = 0; i < 4; i++)
        {
            if (ReadPad(i, out var state) != 0 || (state.buttons & 0x00C0) != 0x00C0) continue;
            if ((state.buttons & 0x0010) != 0) return 2;
            if ((state.buttons & 0x0020) != 0) return 1;
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] struct Input { public uint kind; public Event value; }
    [StructLayout(LayoutKind.Explicit)]
    struct Event
    {
        [FieldOffset(0)] public KeyInput key;
        [FieldOffset(0)] public MouseInput mouse; // Ensures the union is the same size as Win32 INPUT.
    }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int x, y; public uint data, flags, time; public UIntPtr extra; }
    static Input Key(ushort vk, bool release) => new()
    {
        kind = 1,
        value = new Event { key = new KeyInput { vk = vk, flags = (release ? 2u : 0) | (vk is 0x23 or 0x2D ? 1u : 0) } }
    };
    internal static bool Send(params ushort[] keys)
    {
        var sequence = keys.Select(k => Key(k, false)).Concat(keys.Reverse().Select(k => Key(k, true))).ToArray();
        if (SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<Input>()) == sequence.Length) return true;
        var release = keys.Reverse().Select(k => Key(k, true)).ToArray();
        SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
        return false;
    }

    internal static string WindowProcess(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return "";
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch (ArgumentException) { return ""; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumeratorAudio
    {
        void EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEndpoint
    {
        void RegisterControlChangeNotify(IntPtr n);
        void UnregisterControlChangeNotify(IntPtr n);
        void GetChannelCount(out uint n);
        void SetMasterVolumeLevel(float value, IntPtr context);
        void SetMasterVolumeLevelScalar(float value, IntPtr context);
        void GetMasterVolumeLevel(out float value);
        void GetMasterVolumeLevelScalar(out float value);
    }
    internal static int Volume(int? value = null)
    {
        object enumerator = new Enumerator();
        IDevice? device = null;
        object? endpoint = null;
        try
        {
            ((IEnumeratorAudio)enumerator).GetDefaultAudioEndpoint(0, 1, out device);
            var iid = typeof(IEndpoint).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out endpoint);
            var audio = (IEndpoint)endpoint;
            if (value.HasValue) audio.SetMasterVolumeLevelScalar(value.Value / 100f, IntPtr.Zero);
            audio.GetMasterVolumeLevelScalar(out float actual);
            return (int)Math.Round(actual * 100);
        }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    internal static async Task<string> WindowsQuery(string command, int seconds = 25)
    {
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var info = new ProcessStartInfo(ps)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; " + command })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows PowerShell did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw new TimeoutException("Windows did not respond to the hardware query.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException((await error).Trim());
        return (await output).Trim();
    }
}
