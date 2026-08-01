using System.IO.Ports;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Minidisp.Companion.Services;

namespace Minidisp.Companion.Services;

/// <summary>
/// Finds the Minidisp device (ping -> hello handshake, preferring known
/// ESP32 USB bridge VID:PIDs) and pushes stats lines at the configured rate.
/// </summary>
public sealed class SerialSender : IDisposable
{
    private static readonly string[] PreferredVidPids =
    [
        "VID_1A86&PID_7523", // CH340 (CYD)
        "VID_303A&PID_1001", // Espressif native USB CDC (ESP32-C6/S3)
        "VID_10C4&PID_EA60", // CP210x
    ];

    private readonly ILogger _log;
    private readonly Func<ISensorSource> _sourceProvider;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private SerialPort? _port;

    public event Action<string>? StatusChanged;
    public event Action<DeviceInfo>? DeviceConnected;

    public string Status { get; private set; } = "Searching for device...";
    public DeviceInfo? Device { get; private set; }
    public bool IsConnected => _port is not null;

    /// <summary>While true the stats push is suspended (used during uploads).</summary>
    public bool PauseStats { get; set; }

    private readonly object _writeGate = new();
    private string? _pendingCmd;
    private TaskCompletionSource<(bool Ok, string? Error)>? _pendingTcs;

    public sealed record DeviceInfo(string Port, string Board, string Version,
        string[] Themes, string CurrentTheme);

    public SerialSender(ILogger<SerialSender> log, Func<ISensorSource> sourceProvider,
        double updateHz = 2.0)
    {
        _log = log;
        _sourceProvider = sourceProvider;
        _interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(updateHz, 0.2, 10));
        _thread = new Thread(Run) { IsBackground = true, Name = "minidisp-serial" };
        _thread.Start();
    }

    /// <summary>Sends a protocol command, e.g. {"cmd":"theme","name":"gauges"}.</summary>
    public void SendCommand(object command)
    {
        try
        {
            lock (_writeGate) _port?.WriteLine(JsonSerializer.Serialize(command));
        }
        catch (Exception ex)
        {
            _log.LogWarning("Command send failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Sends a command and waits for the device's matching {"ack"}/{"err"}
    /// (drained by the sender thread). Used by theme uploads for flow control.
    /// </summary>
    public async Task<(bool Ok, string? Error)> SendCommandAsync(object command,
        string cmdName, int timeoutMs = 4000)
    {
        var port = _port;
        if (port is null) return (false, "not connected");
        var tcs = new TaskCompletionSource<(bool, string?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_writeGate)
        {
            _pendingCmd = cmdName;
            _pendingTcs = tcs;
        }
        try
        {
            lock (_writeGate) port.WriteLine(JsonSerializer.Serialize(command));
            var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return winner == tcs.Task ? await tcs.Task : (false, "timeout");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            lock (_writeGate)
            {
                _pendingCmd = null;
                _pendingTcs = null;
            }
        }
    }

    private void TryCompleteAck(string line)
    {
        TaskCompletionSource<(bool, string?)>? tcs;
        string? cmd;
        lock (_writeGate)
        {
            tcs = _pendingTcs;
            cmd = _pendingCmd;
        }
        if (tcs is null || cmd is null) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("ack", out var ack) &&
                ack.GetString() == cmd)
            {
                tcs.TrySetResult((true, null));
            }
            else if (doc.RootElement.TryGetProperty("err", out var err) &&
                     err.TryGetProperty("cmd", out var errCmd) &&
                     errCmd.GetString() == cmd)
            {
                tcs.TrySetResult((false,
                    err.TryGetProperty("msg", out var msg) ? msg.GetString() : "device error"));
            }
        }
        catch (JsonException)
        {
            // non-JSON noise on the wire — ignore
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void Run()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_port is null)
                {
                    Connect();
                    if (_port is null)
                    {
                        _cts.Token.WaitHandle.WaitOne(5000);
                        continue;
                    }
                }

                if (!PauseStats)
                {
                    var snapshot = _sourceProvider().GetSnapshot();
                    if (snapshot is not null)
                    {
                        lock (_writeGate) _port.WriteLine(snapshot.ToProtocolLine());
                    }
                }
                DrainIncoming(_port);
                // Poll fast while an upload is waiting on acks.
                _cts.Token.WaitHandle.WaitOne(
                    PauseStats ? TimeSpan.FromMilliseconds(20) : _interval);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException
                or UnauthorizedAccessException or TimeoutException)
            {
                _log.LogWarning("Serial connection lost: {Error}", ex.Message);
                Disconnect();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected sender error");
                Disconnect();
                _cts.Token.WaitHandle.WaitOne(2000);
            }
        }
    }

    private void Connect()
    {
        foreach (var portName in RankedPortNames())
        {
            if (_cts.IsCancellationRequested) return;
            SetStatus($"Probing {portName}...");
            var port = TryHandshake(portName);
            if (port is not null)
            {
                _port = port;
                _log.LogInformation("Connected to {Board} v{Version} on {Port}",
                    Device!.Board, Device.Version, portName);
                SetStatus($"Connected: {Device.Board} on {portName}");
                DeviceConnected?.Invoke(Device);
                return;
            }
        }
        SetStatus("Searching for device...");
    }

    private SerialPort? TryHandshake(string portName)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName, 115200)
            {
                NewLine = "\n",
                ReadTimeout = 250,
                WriteTimeout = 1000,
                DtrEnable = false, // avoid holding the ESP32 in reset via CH340
                RtsEnable = false,
            };
            port.Open();
            port.DiscardInBuffer();
            port.WriteLine("{\"cmd\":\"ping\"}");

            // Opening the port may have auto-reset the device; allow time for
            // either the ping reply or the boot-time hello.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                string? line = TryReadLine(port);
                if (line is null)
                {
                    Thread.Sleep(50);
                    continue;
                }
                var device = ParseHello(portName, line);
                if (device is not null)
                {
                    Device = device;
                    return port;
                }
            }
            port.Dispose();
            return null;
        }
        catch (Exception)
        {
            port?.Dispose();
            return null;
        }
    }

    private static string? TryReadLine(SerialPort port)
    {
        try { return port.ReadLine(); }
        catch (TimeoutException) { return null; }
    }

    private static DeviceInfo? ParseHello(string portName, string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("hello", out var hello)) return null;
            if (hello.GetProperty("fw").GetString() != "minidisp") return null;
            var themes = hello.TryGetProperty("themes", out var t)
                ? t.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : [];
            return new DeviceInfo(
                portName,
                hello.GetProperty("board").GetString() ?? "?",
                hello.GetProperty("ver").GetString() ?? "?",
                themes,
                hello.TryGetProperty("theme", out var ct) ? ct.GetString() ?? "" : "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void DrainIncoming(SerialPort port)
    {
        while (port.BytesToRead > 0)
        {
            string? line = TryReadLine(port);
            if (line is null) break;
            TryCompleteAck(line);
            _log.LogDebug("Device: {Line}", line.Trim());
        }
    }

    private void Disconnect()
    {
        try { _port?.Dispose(); } catch { /* already gone */ }
        _port = null;
        Device = null;
        SetStatus("Disconnected — searching...");
    }

    /// <summary>Known-bridge ports first (via WMI VID/PID), then the rest.</summary>
    private List<string> RankedPortNames()
    {
        var all = SerialPort.GetPortNames().Distinct().ToList();
        var preferred = new List<string>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";
                if (!PreferredVidPids.Any(v => pnpId.Contains(v, StringComparison.OrdinalIgnoreCase)))
                    continue;
                int start = name.LastIndexOf("(COM", StringComparison.Ordinal);
                if (start < 0) continue;
                var com = name[(start + 1)..].TrimEnd(')');
                if (all.Contains(com)) preferred.Add(com);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("WMI port ranking unavailable: {Error}", ex.Message);
        }
        return preferred.Concat(all.Except(preferred)).ToList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(2000);
        _port?.Dispose();
        _cts.Dispose();
    }
}
