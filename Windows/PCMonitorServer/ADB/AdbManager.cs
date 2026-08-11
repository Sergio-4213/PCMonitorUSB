using System.Diagnostics;
using System.IO.Compression;
using PCMonitorUSB.Commands;
using PCMonitorUSB.Config;
using PCMonitorUSB.Server;

namespace PCMonitorUSB.ADB;

public enum AdbConnectionState
{
    AdbMissing,
    NoDevice,
    Unauthorized,
    Offline,
    Incompatible,
    ReverseFailed,
    AppMissing,
    Connected,
    Error
}

public sealed record AdbStatus(AdbConnectionState State, string Message, string? Serial = null, string? Model = null)
{
    public static AdbStatus Initial { get; } = new(AdbConnectionState.AdbMissing, "ADB ainda não verificado.");
}

public sealed record AdbDevice(
    string Serial,
    string State,
    string? Model,
    int? ApiLevel = null,
    bool AppInstalled = false);

public sealed class AdbManager : IAsyncDisposable
{
    public const string AndroidPackage = "com.pcmonitorusb";
    public const string AndroidActivity = "com.pcmonitorusb/.MainActivity";
    private const string LegacyAndroidPackage = "com.j4pcmonitor";
    public const int MinimumAndroidApi = 21;
    private readonly ConfigStore _config;
    private readonly LocalServer _server;
    private readonly string _adbPath;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private AdbStatus _status = AdbStatus.Initial;
    private string? _preparedSerial;
    private DateTimeOffset _nextRepair;

    public AdbManager(ConfigStore config, LocalServer server, string adbPath)
    {
        _config = config;
        _server = server;
        _adbPath = adbPath;
    }

    public event EventHandler<AdbStatus>? StatusChanged;
    public AdbStatus Status => _status;
    public string AdbPath => _adbPath;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => MonitorAsync(_cts.Token));
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetStatus(new AdbStatus(AdbConnectionState.Error, "Falha ao consultar o ADB."));
                SimpleLog.Error("Falha no monitor ADB.", ex);
            }

            try { await Task.Delay(2000, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_adbPath))
        {
            _preparedSerial = null;
            SetStatus(new AdbStatus(AdbConnectionState.AdbMissing, "Componentes ADB ainda não foram baixados."));
            return;
        }

        var result = await RunAdbAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            SetStatus(new AdbStatus(AdbConnectionState.Error, "ADB não respondeu corretamente."));
            return;
        }

        var devices = ParseDevices(result.Output);
        if (devices.Count == 0)
        {
            _preparedSerial = null;
            SetStatus(new AdbStatus(AdbConnectionState.NoDevice, "Nenhum dispositivo Android encontrado."));
            return;
        }

        var online = devices.Where(x => x.State.Equals("device", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (online.Length == 0)
        {
            _preparedSerial = null;
            var unauthorized = devices.FirstOrDefault(x => x.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase));
            if (unauthorized is not null)
            {
                SetStatus(new AdbStatus(AdbConnectionState.Unauthorized,
                    "Desbloqueie o Android e aceite a autorização de depuração USB.", unauthorized.Serial, unauthorized.Model));
                return;
            }
            SetStatus(new AdbStatus(AdbConnectionState.Offline, "Dispositivo ADB offline. Reconecte o cabo USB."));
            return;
        }

        var compatible = new List<AdbDevice>(online.Length);
        var foundNetworkDevice = false;
        var foundUnsupportedAndroid = false;
        foreach (var candidate in online)
        {
            if (!IsPhysicalUsbSerial(candidate.Serial))
            {
                foundNetworkDevice = true;
                continue;
            }

            var model = candidate.Model;
            var modelResult = await RunAdbAsync(["-s", candidate.Serial, "shell", "getprop", "ro.product.model"], cancellationToken).ConfigureAwait(false);
            if (modelResult.Success && !string.IsNullOrWhiteSpace(modelResult.Output)) model = modelResult.Output.Trim();
            model = NormalizeModel(model);

            var apiResult = await RunAdbAsync(["-s", candidate.Serial, "shell", "getprop", "ro.build.version.sdk"], cancellationToken).ConfigureAwait(false);
            int? apiLevel = apiResult.Success && int.TryParse(apiResult.Output.Trim(), out var parsedApi) ? parsedApi : null;
            if (apiLevel.HasValue && apiLevel.Value < MinimumAndroidApi)
            {
                foundUnsupportedAndroid = true;
                continue;
            }

            if (!IsCompatibleModel(model)) continue;
            var package = await RunAdbAsync(["-s", candidate.Serial, "shell", "pm", "path", AndroidPackage], cancellationToken).ConfigureAwait(false);
            var appInstalled = package.Success && package.Output.Contains("package:", StringComparison.OrdinalIgnoreCase);
            compatible.Add(candidate with { Model = model, ApiLevel = apiLevel, AppInstalled = appInstalled });
        }

        var selected = compatible
            .OrderByDescending(x => string.Equals(x.Serial, _preparedSerial, StringComparison.Ordinal))
            .ThenByDescending(x => x.AppInstalled)
            .ThenBy(x => x.Serial, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected is null)
        {
            _preparedSerial = null;
            var unauthorizedUsb = devices.FirstOrDefault(x =>
                x.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase) && IsPhysicalUsbSerial(x.Serial));
            if (unauthorizedUsb is not null)
            {
                SetStatus(new AdbStatus(AdbConnectionState.Unauthorized,
                    "Desbloqueie o Android e aceite a autorização de depuração USB.", unauthorizedUsb.Serial, unauthorizedUsb.Model));
                return;
            }
            var first = online[0];
            var reason = foundNetworkDevice && !foundUnsupportedAndroid
                ? "Somente dispositivos Android conectados por USB são aceitos; ADB por rede foi ignorado."
                : foundUnsupportedAndroid
                    ? $"O Android conectado é antigo demais. É necessário Android 5.0 / API {MinimumAndroidApi} ou superior."
                    : "Android conectado, mas bloqueado pela restrição opcional de modelos.";
            SetStatus(new AdbStatus(AdbConnectionState.Incompatible,
                reason, first.Serial, NormalizeModel(first.Model)));
            return;
        }

        var mustPrepare = !string.Equals(_preparedSerial, selected.Serial, StringComparison.Ordinal) ||
                          DateTimeOffset.UtcNow >= _nextRepair || !_server.Connection.IsPanelConnected;
        if (mustPrepare)
            await PrepareDeviceAsync(selected, cancellationToken).ConfigureAwait(false);
        else
            SetConnectedStatus(selected);
    }

    private async Task PrepareDeviceAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        var port = _config.Current.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reverse = await RunAdbAsync(["-s", device.Serial, "reverse", $"tcp:{port}", $"tcp:{port}"], cancellationToken).ConfigureAwait(false);
        if (!reverse.Success)
        {
            _preparedSerial = null;
            SetStatus(new AdbStatus(AdbConnectionState.ReverseFailed, "Não foi possível configurar o canal USB.", device.Serial, device.Model));
            SimpleLog.Warn($"adb reverse falhou: {TrimForLog(reverse.Output)}");
            return;
        }

        if (!device.AppInstalled)
        {
            var apkPath = Path.Combine(AppContext.BaseDirectory, "PCMonitorUSB.apk");
            if (_config.Current.AutoInstallApk && File.Exists(apkPath))
            {
                var install = await RunAdbAsync(["-s", device.Serial, "install", "-r", apkPath], cancellationToken).ConfigureAwait(false);
                if (!install.Success || !install.Output.Contains("Success", StringComparison.OrdinalIgnoreCase))
                {
                    _preparedSerial = device.Serial;
                    _nextRepair = DateTimeOffset.UtcNow.AddSeconds(10);
                    SetStatus(new AdbStatus(AdbConnectionState.AppMissing,
                        "Android conectado, mas a instalação automática do APK falhou. Use 'Instalar/atualizar APK'.", device.Serial, device.Model));
                    SimpleLog.Warn($"Instalação automática do APK falhou: {TrimForLog(install.Output)}");
                    return;
                }
                SimpleLog.Info($"APK instalado automaticamente em {device.Model ?? device.Serial}.");
            }
            else
            {
                _preparedSerial = device.Serial;
                _nextRepair = DateTimeOffset.UtcNow.AddSeconds(10);
                SetStatus(new AdbStatus(AdbConnectionState.AppMissing, "Android conectado; instale o APK para iniciar o painel.", device.Serial, device.Model));
                return;
            }
        }

        var package = await RunAdbAsync(["-s", device.Serial, "shell", "pm", "path", AndroidPackage], cancellationToken).ConfigureAwait(false);
        if (!package.Success || !package.Output.Contains("package:", StringComparison.OrdinalIgnoreCase))
        {
            _preparedSerial = device.Serial;
            _nextRepair = DateTimeOffset.UtcNow.AddSeconds(10);
            SetStatus(new AdbStatus(AdbConnectionState.AppMissing, "Android conectado; instale o APK para iniciar o painel.", device.Serial, device.Model));
            return;
        }

        var start = await RunAdbAsync([
            "-s", device.Serial, "shell", "am", "start", "-n", AndroidActivity,
            "--es", "api_token", _server.ApiToken, "--ei", "api_port", port
        ], cancellationToken).ConfigureAwait(false);

        if (!start.Success)
        {
            SetStatus(new AdbStatus(AdbConnectionState.Error, "Canal USB criado, mas o APK não pôde ser iniciado.", device.Serial, device.Model));
            SimpleLog.Warn($"Falha ao iniciar APK: {TrimForLog(start.Output)}");
            return;
        }

        await RemoveLegacyAndroidAppAsync(device.Serial, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(_preparedSerial, device.Serial, StringComparison.Ordinal))
            SimpleLog.Info($"Dispositivo conectado e autorizado: {device.Model}.");
        _preparedSerial = device.Serial;
        _nextRepair = DateTimeOffset.UtcNow.AddSeconds(10);
        SetConnectedStatus(device);
    }

    private void SetConnectedStatus(AdbDevice device)
    {
        var message = _server.Connection.IsPanelConnected
            ? "Android conectado; painel comunicando por USB."
            : "Canal USB pronto; aguardando resposta do painel.";
        SetStatus(new AdbStatus(AdbConnectionState.Connected, message, device.Serial, device.Model));
    }

    public async Task<CommandResult> InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(apkPath)) return new CommandResult(false, "Arquivo APK não encontrado ao lado do servidor.");
        var serial = _status.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return new CommandResult(false, "Conecte e autorize o dispositivo Android primeiro.");
        var result = await RunAdbAsync(["-s", serial, "install", "-r", apkPath], cancellationToken).ConfigureAwait(false);
        if (!result.Success || !result.Output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            return new CommandResult(false, "Falha ao instalar APK: " + TrimForLog(result.Output));
        _preparedSerial = null;
        await RemoveLegacyAndroidAppAsync(serial, cancellationToken).ConfigureAwait(false);
        SimpleLog.Info("APK instalado/atualizado pelo ADB.");
        return new CommandResult(true);
    }

    private async Task RemoveLegacyAndroidAppAsync(string serial, CancellationToken cancellationToken)
    {
        var legacy = await RunAdbAsync(["-s", serial, "shell", "pm", "path", LegacyAndroidPackage], cancellationToken).ConfigureAwait(false);
        if (!legacy.Success || !legacy.Output.Contains("package:", StringComparison.OrdinalIgnoreCase)) return;

        var uninstall = await RunAdbAsync(["-s", serial, "uninstall", LegacyAndroidPackage], cancellationToken).ConfigureAwait(false);
        if (uninstall.Success && uninstall.Output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            SimpleLog.Info("Aplicativo Android antigo removido após validar a nova instalação.");
        else
            SimpleLog.Warn("Não foi possível remover automaticamente o aplicativo Android antigo.");
    }

    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var devices = new List<AdbDevice>();
        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || rawLine.StartsWith('*')) continue;
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var modelPart = parts.FirstOrDefault(x => x.StartsWith("model:", StringComparison.OrdinalIgnoreCase));
            devices.Add(new AdbDevice(parts[0], parts[1], NormalizeModel(modelPart?[6..])));
        }
        return devices;
    }

    private bool IsCompatibleModel(string? model)
    {
        if (!_config.Current.RestrictAndroidModels) return true;
        var prefixes = _config.Current.AllowedModelPrefixes;
        if (prefixes.Count == 0) return true;
        return !string.IsNullOrWhiteSpace(model) && prefixes.Any(prefix =>
            model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeModel(string? model) => string.IsNullOrWhiteSpace(model)
        ? null
        : model.Trim().Replace('_', '-');

    public static bool IsPhysicalUsbSerial(string serial) =>
        !string.IsNullOrWhiteSpace(serial) &&
        !serial.Contains(':') &&
        !serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) &&
        !serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase);

    private async Task<(bool Success, string Output)> RunAdbAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _adbPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            return (false, "Tempo limite do ADB excedido.");
        }
        var output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
        return (process.ExitCode == 0, output.Trim());
    }

    private void SetStatus(AdbStatus status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private static string TrimForLog(string text)
    {
        var safe = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return safe.Length <= 240 ? safe : safe[..240];
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        if (File.Exists(_adbPath) && !string.IsNullOrWhiteSpace(_preparedSerial))
        {
            var port = _config.Current.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try { await RunAdbAsync(["-s", _preparedSerial, "reverse", "--remove", $"tcp:{port}"], CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
        _cts?.Dispose();
    }
}

public static class AdbProvisioner
{
    public const string OfficialDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    public static string GetManagedAdbPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMonitorUSB", "platform-tools", "adb.exe");

    public static string FindAdbPath()
    {
        var adjacent = Path.Combine(AppContext.BaseDirectory, "tools", "platform-tools", "adb.exe");
        if (File.Exists(adjacent)) return adjacent;
        var managed = GetManagedAdbPath();
        if (File.Exists(managed)) return managed;
        return managed;
    }

    public static async Task ProvisionAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCMonitorUSB");
        Directory.CreateDirectory(appData);
        var zipPath = Path.Combine(appData, "platform-tools.download.zip");
        var installRoot = Path.Combine(appData, "platform-tools");
        var staging = Path.Combine(appData, "platform-tools.staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var response = await client.GetAsync(OfficialDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    if (total > 0) progress?.Report((int)(copied * 100 / total.Value));
                }
            }

            Directory.CreateDirectory(staging);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (relative.StartsWith("platform-tools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        relative = relative[("platform-tools".Length + 1)..];
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    var destination = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!destination.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Entrada insegura no pacote Platform-Tools.");
                    if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, true);
                    }
                }
            }

            if (!File.Exists(Path.Combine(staging, "adb.exe")))
                throw new InvalidDataException("O pacote oficial não contém adb.exe.");
            if (Directory.Exists(installRoot)) Directory.Move(installRoot, installRoot + ".old-" + Guid.NewGuid().ToString("N"));
            Directory.Move(staging, installRoot);
            progress?.Report(100);
            SimpleLog.Info("Android Platform-Tools baixado da fonte oficial.");
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }
}
