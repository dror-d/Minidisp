namespace Minidisp.Companion.Services;

/// <summary>
/// Streams a theme folder (theme.json + PNGs) to the device over the serial
/// link using the fs.begin/fs.data/fs.end protocol (docs/PROTOCOL.md), then
/// switches the device to it. Each chunk waits for the device ack.
/// </summary>
public static class ThemeUploader
{
    private const int ChunkSize = 768; // base64 line stays well under the 4KB RX cap

    public static async Task<(bool Ok, string Message)> PushThemeAsync(
        SerialSender sender, string themeDir, IProgress<string>? progress)
    {
        var name = Path.GetFileName(themeDir.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) return (false, "Invalid theme folder");
        if (!sender.IsConnected) return (false, "No device connected");
        var themeJson = Path.Combine(themeDir, "theme.json");
        if (!File.Exists(themeJson)) return (false, "theme.json not found — save first");

        var files = new List<string> { themeJson };
        files.AddRange(Directory.GetFiles(themeDir, "*.png"));

        sender.PauseStats = true;
        try
        {
            foreach (var file in files)
            {
                var bytes = await File.ReadAllBytesAsync(file);
                var target = $"/themes/{name}/{Path.GetFileName(file)}";
                progress?.Report($"Uploading {target} ({bytes.Length:N0} bytes)...");

                var (ok, err) = await sender.SendCommandAsync(
                    new { cmd = "fs.begin", path = target, size = bytes.Length }, "fs.begin");
                if (!ok) return (false, $"Upload of {target} rejected: {err}");

                for (int offset = 0; offset < bytes.Length; offset += ChunkSize)
                {
                    var length = Math.Min(ChunkSize, bytes.Length - offset);
                    var b64 = Convert.ToBase64String(bytes, offset, length);
                    (ok, err) = await sender.SendCommandAsync(
                        new { cmd = "fs.data", b64 }, "fs.data", timeoutMs: 6000);
                    if (!ok) return (false, $"Upload of {target} failed: {err}");
                }

                (ok, err) = await sender.SendCommandAsync(new { cmd = "fs.end" }, "fs.end",
                    timeoutMs: 8000);
                if (!ok) return (false, $"Finalizing {target} failed: {err}");
            }

            progress?.Report("Switching device theme...");
            var (themeOk, themeErr) = await sender.SendCommandAsync(
                new { cmd = "theme", name }, "theme", timeoutMs: 6000);
            return themeOk
                ? (true, $"Pushed '{name}' to the device")
                : (false, $"Theme uploaded but switch failed: {themeErr}");
        }
        finally
        {
            sender.PauseStats = false;
        }
    }
}
