using Microsoft.Extensions.Logging;
using Minidisp.Companion.Logging;

namespace Minidisp.Companion;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\MinidispCompanion",
            out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Minidisp Companion is already running (check the tray).",
                "Minidisp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new FileLoggerProvider()).SetMinimumLevel(LogLevel.Information));
        var log = loggerFactory.CreateLogger("Main");
        log.LogInformation("Minidisp Companion starting");

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new TrayApp(loggerFactory));
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Fatal error");
            throw;
        }
        log.LogInformation("Minidisp Companion exited");
    }
}
