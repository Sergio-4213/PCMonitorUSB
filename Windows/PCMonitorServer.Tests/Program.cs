using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using PCMonitorUSB;
using PCMonitorUSB.ADB;
using PCMonitorUSB.Commands;
using PCMonitorUSB.Config;
using PCMonitorUSB.Core;
using PCMonitorUSB.Server;
using PCMonitorUSB.UI;
using PCMonitorUSB.Localization;
using System.Text.Json;

AppLanguage.Configure("pt");

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Parser ADB distingue autorizado e unauthorized", TestAdbParser),
    ("Seleção de sensores usa tipo, nome e prioridade", TestSensorSelection),
    ("GPU principal é escolhida sem misturar vídeo integrado e dedicado", TestPrimaryGpuSelection),
    ("Configuração normaliza porta e intervalo", TestConfigNormalization),
    ("Idioma alterna entre português e inglês", TestLocalization),
    ("Inicialização elevada exige pasta protegida", TestStartupSecurity),
    ("Lista de comandos nega comando arbitrário", TestCommandAllowlist),
    ("API local exige token em todos os endpoints e limita abuso", TestLocalApi),
    ("Janela mantém título e botão Salvar dentro da área visível", TestWindowLayout),
    ("ADB genérico conecta Android USB real quando disponível", TestRealAdbCompatibility),
    ("LibreHardwareMonitor coleta no computador real", TestRealHardwareMonitor)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"RESULTADO: {tests.Count - failed}/{tests.Count} testes passaram.");
return failed == 0 ? 0 : 1;

static Task TestAdbParser()
{
    var devices = AdbManager.ParseDevices("""
        List of devices attached
        R58M123 unauthorized usb:1-2 transport_id:1
        192.0.2.1:5555 device product:j4core model:SM_J410F device:j4core transport_id:2
        """);
    Require(devices.Count == 2, "Quantidade incorreta.");
    Require(devices[0].State == "unauthorized", "Estado unauthorized perdido.");
    Require(devices[1].Model == "SM-J410F", "Modelo não normalizado.");
    Require(AdbManager.IsPhysicalUsbSerial("R58M123"), "Dispositivo USB físico foi rejeitado.");
    Require(!AdbManager.IsPhysicalUsbSerial("192.0.2.1:5555"), "ADB por rede não pode ser tratado como USB.");
    return Task.CompletedTask;
}

static Task TestSensorSelection()
{
    RawSensor[] sensors =
    [
        new("AMD Ryzen", "Cpu", "Core #1", "Temperature", 51, "/cpu/0/temp/1"),
        new("AMD Ryzen", "Cpu", "CPU Package", "Temperature", 63, "/cpu/0/temp/0"),
        new("AMD Ryzen", "Cpu", "CPU Total", "Load", 42, "/cpu/0/load/0")
    ];
    var package = HardwareMonitor.Pick(sensors, "Temperature", x =>
        x.Sensor.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) ? 100 : 10);
    Require(package == 63, "CPU Package não foi priorizado.");
    var missing = HardwareMonitor.Pick(sensors, "Power", _ => 100);
    Require(!missing.HasValue, "Sensor inexistente não pode virar valor falso.");

    RawSensor[] motherboardSensors =
    [
        new("Nuvoton", "SuperIO", "System", "Temperature", 38, "/lpc/temp/0"),
        new("Nuvoton", "SuperIO", "CPU Core", "Temperature", 59, "/lpc/temp/1")
    ];
    var fallback = HardwareMonitor.PickCpuTemperature([], motherboardSensors, out var source);
    Require(fallback == 59 && source.Contains("placa-mãe", StringComparison.OrdinalIgnoreCase),
        "Fallback seguro da temperatura da CPU não foi selecionado.");
    return Task.CompletedTask;
}

static Task TestConfigNormalization()
{
    var config = new AppConfig { Port = 1, UpdateIntervalMs = 1300, CpuElevatedTemperature = 85, CpuCriticalTemperature = 80, Language = "inválido" };
    config.Normalize();
    Require(config.Port == 1024, "Porta mínima não aplicada.");
    Require(config.UpdateIntervalMs == 1000, "Intervalo não normalizado.");
    Require(config.CpuCriticalTemperature > config.CpuElevatedTemperature, "Limites térmicos inconsistentes.");
    Require(!config.RestrictAndroidModels && config.AutoInstallApk, "Compatibilidade Android ampla não está ativa por padrão.");
    Require(config.Language == "auto", "Idioma inválido não voltou ao modo automático.");

    var legacyRoot = Path.Combine(Path.GetTempPath(), "PCMonitorUSBTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(legacyRoot);
    var legacyPath = Path.Combine(legacyRoot, "config.json");
    File.WriteAllText(legacyPath, "{\"AllowedModelPrefixes\":[\"SM-J410\"]}");
    var migrated = new ConfigStore(legacyPath).Current;
    Require(!migrated.RestrictAndroidModels, "Configuração antiga continuou bloqueada ao Galaxy J4.");
    return Task.CompletedTask;
}

static Task TestLocalization()
{
    AppLanguage.Configure("en");
    Require(AppLanguage.CurrentCode == "en" && AppLanguage.T("Português", "English") == "English", "Idioma inglês não foi aplicado.");
    Require(AppLanguage.BuiltInButtonLabel("media_next", "fallback") == "NEXT", "Botão interno não foi traduzido para inglês.");
    AppLanguage.Configure("pt");
    Require(AppLanguage.CurrentCode == "pt" && AppLanguage.T("Português", "English") == "Português", "Idioma português não foi restaurado.");
    return Task.CompletedTask;
}

static Task TestStartupSecurity()
{
    var protectedPath = MainForm.GetSecureStartupExecutablePath();
    Require(MainForm.IsSecureStartupLocation(protectedPath), "O destino protegido de inicialização não foi reconhecido.");
    var writablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PCMonitorServer.exe");
    Require(!MainForm.IsSecureStartupLocation(writablePath), "Uma cópia portátil gravável foi aceita como destino elevado.");
    return Task.CompletedTask;
}

static Task TestPrimaryGpuSelection()
{
    RawSensor[] sensors =
    [
        new("Intel UHD Graphics", "GpuIntel", "GPU Core", "Load", 12, "/gpu-intel/0/load/0", "/gpu-intel/0"),
        new("Intel UHD Graphics", "GpuIntel", "GPU Core", "Clock", 700, "/gpu-intel/0/clock/0", "/gpu-intel/0"),
        new("NVIDIA GeForce RTX", "GpuNvidia", "GPU Core", "Load", 2, "/gpu-nvidia/0/load/0", "/gpu-nvidia/0"),
        new("NVIDIA GeForce RTX", "GpuNvidia", "GPU Core", "Temperature", 45, "/gpu-nvidia/0/temp/0", "/gpu-nvidia/0"),
        new("NVIDIA GeForce RTX", "GpuNvidia", "GPU Core", "Clock", 1500, "/gpu-nvidia/0/clock/0", "/gpu-nvidia/0"),
        new("NVIDIA GeForce RTX", "GpuNvidia", "GPU Memory Total", "SmallData", 8192, "/gpu-nvidia/0/data/0", "/gpu-nvidia/0")
    ];
    var selected = HardwareMonitor.SelectPrimaryGpuSensors(sensors, null, out var key);
    Require(key == "/gpu-nvidia/0", "GPU dedicada com VRAM não foi priorizada.");
    Require(selected.All(x => x.Hardware == "NVIDIA GeForce RTX"), "Sensores de GPUs diferentes foram misturados.");
    return Task.CompletedTask;
}

static Task TestCommandAllowlist()
{
    var store = NewStore(FindFreePort());
    var service = new CommandService(store);
    foreach (var payload in new[]
    {
        "powershell -enc qualquer-coisa", "cmd.exe /c whoami", "../../Windows/System32/cmd.exe",
        "open_program", "media_play_pause;calc.exe", new string('A', 1024), "custom_1"
    })
    {
        var result = service.Execute(payload);
        Require(!result.Success, "Comando arbitrário foi aceito: " + payload[..Math.Min(payload.Length, 40)]);
    }
    return Task.CompletedTask;
}

static async Task TestLocalApi()
{
    var port = FindFreePort();
    var store = NewStore(port);
    var commands = new CommandService(store);
    await using var server = new LocalServer(new FakeStatsProvider(), store, commands);
    await server.StartAsync();
    using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    var root = await client.GetAsync("/");
    Require(root.StatusCode == HttpStatusCode.OK, "Painel local não respondeu 200.");
    Require(!root.Headers.Contains("Server"), "O servidor revelou sua implementação no cabeçalho Server.");
    Require(root.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Any(), "Content-Security-Policy ausente.");
    Require(!root.Headers.Contains("Access-Control-Allow-Origin"), "CORS foi aberto indevidamente.");
    var statsWithoutToken = await client.GetAsync("/api/stats");
    Require(statsWithoutToken.StatusCode == HttpStatusCode.Unauthorized, "Stats sem token não foi negado.");
    client.DefaultRequestHeaders.Add("X-PCMonitor-Token", "token-incorreto");
    var statsWithWrongToken = await client.GetAsync("/api/stats");
    Require(statsWithWrongToken.StatusCode == HttpStatusCode.Unauthorized, "Stats com token incorreto não foi negado.");
    client.DefaultRequestHeaders.Remove("X-PCMonitor-Token");
    client.DefaultRequestHeaders.Add("X-PCMonitor-Token", server.ApiToken);
    var stats = await client.GetAsync("/api/stats");
    Require(stats.StatusCode == HttpStatusCode.OK, "Stats autenticado não respondeu 200.");
    var system = await client.GetFromJsonAsync<SystemProfile>("/api/system");
    Require(system?.Cpu == "AMD Ryzen 7 3800XT", "Configuração exata do PC não foi publicada.");
    var panelConfig = await client.GetFromJsonAsync<JsonElement>("/api/config");
    Require(panelConfig.GetProperty("language").GetString() == "pt", "A API não publicou o idioma selecionado para o Android.");

    using var duplicateTokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/stats");
    duplicateTokenRequest.Headers.TryAddWithoutValidation("X-PCMonitor-Token", new[] { server.ApiToken, server.ApiToken });
    var duplicateToken = await client.SendAsync(duplicateTokenRequest);
    Require(duplicateToken.StatusCode == HttpStatusCode.Unauthorized, "Cabeçalho de token duplicado não foi negado.");

    using var invalidContent = new StringContent("command=cmd.exe");
    var invalidContentResponse = await client.PostAsync("/api/command", invalidContent);
    Require(invalidContentResponse.StatusCode == HttpStatusCode.BadRequest, "Content-Type inválido não foi negado.");
    using var oversized = new StringContent("{\"command\":\"" + new string('A', 9000) + "\"}", System.Text.Encoding.UTF8, "application/json");
    var oversizedResponse = await client.PostAsync("/api/command", oversized);
    Require(oversizedResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Corpo acima do limite não foi negado.");

    await Task.Delay(100);
    var rejected = await client.PostAsJsonAsync("/api/command", new { command = "cmd.exe" });
    Require(rejected.StatusCode == HttpStatusCode.BadRequest, "Comando fora da lista não foi rejeitado.");
    var flood = await client.PostAsJsonAsync("/api/command", new { command = "cmd.exe" });
    Require((int)flood.StatusCode == 429, "Limite de comandos em sequência não foi aplicado.");
    await server.StopAsync();
}

static async Task TestRealHardwareMonitor()
{
    using var monitor = new HardwareMonitor(1000);
    monitor.Start();
    await Task.Delay(2500);
    var snapshot = monitor.Current;
    Require(snapshot.Ram.Total > 0, "RAM física não foi lida.");
    Require(monitor.DetectedSensors.Count > 0, "LibreHardwareMonitor não enumerou sensores.");
    Console.WriteLine($"      CPU={snapshot.Cpu.Name}; Temp={snapshot.Cpu.Temperature?.ToString() ?? "--"}; Uso={snapshot.Cpu.Usage?.ToString() ?? "--"}");
    Console.WriteLine($"      GPU={snapshot.Gpu.Name}; Temp={snapshot.Gpu.Temperature?.ToString() ?? "--"}; Uso={snapshot.Gpu.Usage?.ToString() ?? "--"}; Sensores={monitor.DetectedSensors.Count}");
}

static Task TestWindowLayout()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            AppLanguage.Configure("pt");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetDefaultFont(new Font("Segoe UI", 10f));
            var store = NewStore(FindFreePort());
            using var hardware = new HardwareMonitor(1000);
            var commands = new CommandService(store);
            var server = new LocalServer(hardware, store, commands);
            var dummyAdb = Path.Combine(Path.GetTempPath(), "PCMonitorUSBTests", "adb-dummy.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(dummyAdb)!);
            File.WriteAllBytes(dummyAdb, []);
            var adb = new AdbManager(store, server, dummyAdb);
            using var form = new MainForm(store, hardware, server, adb, false)
            {
                Size = new Size(947, 844), StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            };
            var tabs = Descendants(form).OfType<TabControl>().Single();
            tabs.SelectedIndex = 3;
            form.Show();
            Application.DoEvents();

            var title = Descendants(form).OfType<Label>().Single(x => x.Text == "PC MONITOR USB");
            var save = Descendants(form).OfType<Button>().Single(x => x.Text == "Salvar configurações");
            var serverToggle = Descendants(form).OfType<Button>().Single(x => x.Text == "Ligar servidor");
            Require(serverToggle.Enabled, "O botão para ligar o servidor não está disponível quando ele está parado.");
            var output = Path.Combine(Path.GetTempPath(), "PCMonitorUSBTests", "settings-layout-2.1.1.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(output);
            Console.WriteLine("      Captura=" + output);
            var saveBounds = new Rectangle(form.PointToClient(save.Parent!.PointToScreen(save.Location)), save.Size);
            Console.WriteLine($"      Janela={form.ClientSize}; Título={title.Bounds}; Salvar={saveBounds}");
            Require(title.Height >= title.PreferredHeight, "O nome do aplicativo ficou cortado no cabeçalho.");
            Require(saveBounds.Left >= 0 && saveBounds.Top >= 0 &&
                    saveBounds.Right <= form.ClientSize.Width && saveBounds.Bottom <= form.ClientSize.Height,
                "O botão Salvar ficou fora da janela.");

            tabs.SelectedIndex = 0;
            Application.DoEvents();
            var dashboardOutput = Path.Combine(Path.GetTempPath(), "PCMonitorUSBTests", "dashboard-server-toggle-2.1.1.png");
            using var dashboardBitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(dashboardBitmap, form.ClientRectangle);
            dashboardBitmap.Save(dashboardOutput);
            Console.WriteLine("      Captura do servidor=" + dashboardOutput);
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            adb.DisposeAsync().AsTask().GetAwaiter().GetResult();
            form.Dispose();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    return Task.CompletedTask;
}

static IEnumerable<Control> Descendants(Control root)
{
    foreach (Control child in root.Controls)
    {
        yield return child;
        foreach (var descendant in Descendants(child)) yield return descendant;
    }
}


static async Task TestRealAdbCompatibility()
{
    var adbPath = AdbProvisioner.FindAdbPath();
    if (!File.Exists(adbPath))
    {
        Console.WriteLine("      SKIP: ADB gerenciado não está disponível nesta máquina.");
        return;
    }

    var port = FindFreePort();
    var store = NewStore(port);
    var config = store.Current;
    config.AutoInstallApk = false;
    config.RestrictAndroidModels = false;
    store.Save(config);
    var commands = new CommandService(store);
    await using var server = new LocalServer(new FakeStatsProvider(), store, commands);
    await server.StartAsync();
    await using var adb = new AdbManager(store, server, adbPath);
    await adb.CheckAsync();
    Require(adb.Status.State is AdbConnectionState.Connected or AdbConnectionState.NoDevice,
        "ADB genérico entrou em estado inesperado: " + adb.Status.Message);
    if (adb.Status.State == AdbConnectionState.Connected)
        Console.WriteLine($"      Android USB={adb.Status.Model ?? adb.Status.Serial}; status={adb.Status.Message}");
    await server.StopAsync();
}

static ConfigStore NewStore(int port)
{
    var root = Path.Combine(Path.GetTempPath(), "PCMonitorUSBTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    SimpleLog.Initialize(Path.Combine(root, "test.log"));
    var store = new ConfigStore(Path.Combine(root, "config.json"));
    var config = store.Current;
    config.Port = port;
    store.Save(config);
    return store;
}

static int FindFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeStatsProvider : IStatsProvider
{
    public SystemProfile Profile { get; } = new(
        "PC-TESTE", "Windows 11", "MSI B550M PRO-VDH WIFI", "AMD Ryzen 7 3800XT",
        "AMD Radeon RX 7600", ["AMD Radeon RX 7600"], 32);
    public StatsSnapshot Current { get; } = new(
        DateTimeOffset.UtcNow,
        new CpuStats("AMD Ryzen 7 3800XT", 62, 41, 4300, 71),
        new GpuStats("AMD Radeon RX 7600", 66, 78, 98, 2650, 2250, 5.4f, 8, 155, 1850, 52),
        new RamStats(14.2f, 32, 44),
        new NetworkStats(18.5f, 1.4f),
        new DiskStats(7, 55),
        null);
    public IReadOnlyList<RawSensor> DetectedSensors { get; } = Array.Empty<RawSensor>();
}
