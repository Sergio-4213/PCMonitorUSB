using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using PCMonitorUSB.ADB;
using PCMonitorUSB.Config;
using PCMonitorUSB.Core;
using PCMonitorUSB.Server;
using Microsoft.Win32;
using PCMonitorUSB.Localization;
using static PCMonitorUSB.Localization.AppLanguage;

namespace PCMonitorUSB.UI;

public sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(18, 20, 23);
    private static readonly Color Surface = Color.FromArgb(29, 32, 37);
    private static readonly Color Foreground = Color.FromArgb(238, 240, 243);
    private static readonly Color Muted = Color.FromArgb(167, 173, 181);
    private static readonly Color Green = Color.FromArgb(70, 205, 125);
    private static readonly Color Cyan = Color.FromArgb(42, 199, 218);
    private static readonly Color Orange = Color.FromArgb(245, 167, 66);
    private static readonly Color Red = Color.FromArgb(238, 83, 83);

    private readonly ConfigStore _config;
    private readonly HardwareMonitor _hardware;
    private readonly LocalServer _server;
    private readonly AdbManager _adb;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly bool _forceMinimized;
    private readonly Image? _brandImage;
    private bool _exitRequested;
    private bool _adbOfferShown;

    private Label _serverStatus = null!;
    private Label _adbStatus = null!;
    private Label _deviceStatus = null!;
    private Label _sensorStatus = null!;
    private Label _cpuTemperatureStatus = null!;
    private Label _detailStatus = null!;
    private Label _systemProfileStatus = null!;
    private ToolStripMenuItem _trayConnectionStatus = null!;
    private Button _serverToggleButton = null!;
    private DataGridView _sensorGrid = null!;
    private CheckedListBox _builtInButtons = null!;
    private DataGridView _customButtons = null!;
    private NumericUpDown _port = null!;
    private ComboBox _interval = null!;
    private ComboBox _language = null!;
    private CheckBox _startWindows = null!;
    private CheckBox _startMinimized = null!;
    private CheckBox _autoInstallApk = null!;
    private CheckBox _enableWakeOnLan = null!;
    private readonly Dictionary<string, CheckBox> _visibility = new(StringComparer.OrdinalIgnoreCase);
    private NumericUpDown _cpuElevated = null!;
    private NumericUpDown _cpuCritical = null!;
    private NumericUpDown _gpuElevated = null!;
    private NumericUpDown _gpuCritical = null!;

    public MainForm(ConfigStore config, HardwareMonitor hardware, LocalServer server, AdbManager adb, bool forceMinimized)
    {
        _config = config;
        _hardware = hardware;
        _server = server;
        _adb = adb;
        _forceMinimized = forceMinimized;

        Text = "PC Monitor USB";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(820, 680);
        Size = new Size(960, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10f);
        Icon = LoadAppIcon();
        _brandImage = LoadBrandImage();

        var beginHidden = _forceMinimized || _config.Current.StartMinimized;
        if (beginHidden)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
        }

        Controls.Add(BuildMainLayout());
        _tray = BuildTrayIcon();
        _adb.StatusChanged += AdbOnStatusChanged;
        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => RefreshStatus();
        _uiTimer.Start();
        Shown += (_, _) =>
        {
            if (beginHidden) HideToTray();
            else Opacity = 1;
            RefreshStatus();
            if (!beginHidden && !File.Exists(_adb.AdbPath) && !_adbOfferShown)
            {
                _adbOfferShown = true;
                BeginInvoke(() => DownloadAdbAsync(this, EventArgs.Empty));
            }
        };
        Resize += OnResize;
        FormClosing += OnFormClosing;
    }

    private Control BuildMainLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(18), BackColor = Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Background };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill, Image = _brandImage, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 5, 12, 7)
        }, 0, 0);
        var titles = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Background };
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        titles.Controls.Add(new Label
        {
            Text = "PC MONITOR USB", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 19f),
            ForeColor = Foreground, TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        titles.Controls.Add(new Label
        {
            Text = T("DISPLAY USB  •  MONITORAMENTO E CONTROLE", "USB DISPLAY  •  MONITORING AND CONTROL"), Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 8.5f), ForeColor = Cyan, TextAlign = ContentAlignment.TopLeft
        }, 0, 1);
        header.Controls.Add(titles, 1, 0);
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill, Padding = new Point(18, 8), DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed, ItemSize = new Size(150, 38)
        };
        tabs.DrawItem += DrawTab;
        tabs.TabPages.Add(BuildDashboardTab());
        tabs.TabPages.Add(BuildSensorsTab());
        tabs.TabPages.Add(BuildButtonsTab());
        tabs.TabPages.Add(BuildSettingsTab());
        root.Controls.Add(tabs, 0, 1);
        return root;
    }

    private TabPage BuildDashboardTab()
    {
        var tab = NewTab(T("Visão geral", "Overview"));
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(20), BackColor = Surface };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        AddStatusRow(panel, 0, T("Servidor:", "Server:"), out _serverStatus);
        AddStatusRow(panel, 1, "ADB:", out _adbStatus);
        AddStatusRow(panel, 2, T("Celular:", "Phone:"), out _deviceStatus);
        AddStatusRow(panel, 3, T("Sensores:", "Sensors:"), out _sensorStatus);
        AddStatusRow(panel, 4, T("Temperatura da CPU:", "CPU temperature:"), out _cpuTemperatureStatus);
        _detailStatus = new Label { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(_detailStatus, 0, 5);
        panel.SetColumnSpan(_detailStatus, 2);

        var profileBox = new GroupBox
        {
            Text = T("Configuração identificada neste PC", "Configuration detected on this PC"), Dock = DockStyle.Fill, ForeColor = Cyan,
            Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 4, 0, 8)
        };
        _systemProfileStatus = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Foreground, Font = new Font("Segoe UI", 9.5f),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };
        profileBox.Controls.Add(_systemProfileStatus);
        panel.Controls.Add(profileBox, 0, 6);
        panel.SetColumnSpan(profileBox, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) };
        _serverToggleButton = ActionButton(T("Ligar servidor", "Turn server on"), ToggleServerAsync);
        actions.Controls.Add(_serverToggleButton);
        actions.Controls.Add(ActionButton(T("Configurar celular", "Set up phone"), ConfigurePhoneAsync));
        panel.Controls.Add(actions, 0, 7);
        panel.SetColumnSpan(actions, 2);
        var privacyHint = new Label
        {
            Text = T("O servidor escuta somente em 127.0.0.1. Nenhum dado é enviado para a internet ou para a nuvem.",
                "The server listens only on 127.0.0.1. No data is sent to the internet or the cloud."),
            Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(privacyHint, 0, 8);
        panel.SetColumnSpan(privacyHint, 2);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildSensorsTab()
    {
        var tab = NewTab(T("Sensores", "Sensors"));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Surface, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _sensorGrid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Background,
            ForeColor = Foreground, GridColor = Color.FromArgb(55, 60, 68), BorderStyle = BorderStyle.None,
            RowHeadersVisible = false, EnableHeadersVisualStyles = false
        };
        _sensorGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(42, 46, 52);
        _sensorGrid.ColumnHeadersDefaultCellStyle.ForeColor = Foreground;
        _sensorGrid.DefaultCellStyle.BackColor = Surface;
        _sensorGrid.DefaultCellStyle.ForeColor = Foreground;
        _sensorGrid.Columns.Add("Hardware", "Hardware");
        _sensorGrid.Columns.Add("HardwareType", T("Tipo de hardware", "Hardware type"));
        _sensorGrid.Columns.Add("HardwareIdentifier", "ID do hardware");
        _sensorGrid.Columns.Add("Sensor", "Sensor");
        _sensorGrid.Columns.Add("SensorType", T("Tipo", "Type"));
        _sensorGrid.Columns.Add("Value", T("Valor", "Value"));
        _sensorGrid.Columns.Add("Identifier", T("Identificador", "Identifier"));
        root.Controls.Add(_sensorGrid, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(ActionButton(T("Atualizar lista", "Refresh list"), (_, _) => RefreshSensorGrid()));
        actions.Controls.Add(ActionButton(T("Exportar sensors.txt", "Export sensors.txt"), ExportSensors));
        actions.Controls.Add(ActionButton(T("Ampliar suporte aos sensores", "Extend sensor support"), InstallPawnIoAsync));
        root.Controls.Add(actions, 0, 1);
        tab.Controls.Add(root);
        tab.Enter += (_, _) => RefreshSensorGrid();
        return tab;
    }

    private TabPage BuildButtonsTab()
    {
        var tab = NewTab(T("Botões", "Buttons"));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(16), BackColor = Surface };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = T("Botões permitidos no celular", "Buttons available on the phone"), Dock = DockStyle.Fill, ForeColor = Foreground }, 0, 0);
        _builtInButtons = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Background, ForeColor = Foreground, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
        foreach (var button in _config.Current.Buttons.Where(x => x.BuiltIn))
            _builtInButtons.Items.Add(new ButtonListItem(button.Id, AppLanguage.BuiltInButtonLabel(button.Id, button.Label)), button.Enabled);
        root.Controls.Add(_builtInButtons, 0, 1);

        _customButtons = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Background, ForeColor = Foreground, GridColor = Color.FromArgb(55, 60, 68),
            EnableHeadersVisualStyles = false
        };
        _customButtons.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(42, 46, 52);
        _customButtons.ColumnHeadersDefaultCellStyle.ForeColor = Foreground;
        _customButtons.DefaultCellStyle.BackColor = Surface;
        _customButtons.DefaultCellStyle.ForeColor = Foreground;
        _customButtons.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = T("Ativo", "Enabled"), FillWeight = 35 });
        _customButtons.Columns.Add("Label", T("Nome", "Name"));
        _customButtons.Columns.Add(new DataGridViewTextBoxColumn { Name = "Icon", HeaderText = T("Ícone", "Icon"), FillWeight = 35 });
        _customButtons.Columns.Add(new DataGridViewComboBoxColumn { Name = "Action", HeaderText = T("Ação", "Action"), DataSource = new[] { "none", "open_program", "open_url", "hotkey" } });
        _customButtons.Columns.Add("Target", T("Programa, URL ou atalho (ex.: CTRL+ALT+F10)", "Program, URL, or shortcut (for example: CTRL+ALT+F10)"));
        foreach (var button in _config.Current.Buttons.Where(x => !x.BuiltIn))
            _customButtons.Rows.Add(button.Enabled, button.Label, button.Icon, button.Action, button.Target ?? "");
        root.Controls.Add(_customButtons, 0, 2);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill };
        bottom.Controls.Add(ActionButton(T("Salvar botões", "Save buttons"), SaveButtons));
        bottom.Controls.Add(new Label { Text = T("O celular envia somente o ID; os caminhos permanecem no PC.", "The phone sends only the ID; paths remain on the PC."), AutoSize = true, ForeColor = Muted, Padding = new Padding(12, 10, 0, 0) });
        root.Controls.Add(bottom, 0, 3);
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage BuildSettingsTab()
    {
        var tab = NewTab(T("Configurações", "Settings"));
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(0), BackColor = Surface };
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Surface, Padding = new Padding(0) };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(22, 14, 38, 14), BackColor = Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;
        _port = NewNumeric(1024, 65535, _config.Current.Port);
        AddSetting(root, ref row, T("Porta local", "Local port"), _port);
        _interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _interval.Items.AddRange(["500 ms", "1000 ms", "2000 ms"]);
        _interval.SelectedItem = _config.Current.UpdateIntervalMs + " ms";
        AddSetting(root, ref row, T("Intervalo de atualização", "Update interval"), _interval);
        _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _language.Items.AddRange([T("Automático (Windows)", "Automatic (Windows)"), "Português", "English"]);
        _language.SelectedIndex = _config.Current.Language switch { "pt" => 1, "en" => 2, _ => 0 };
        AddSetting(root, ref row, T("Idioma", "Language"), _language);
        _startWindows = NewCheck(T("Iniciar com Windows", "Start with Windows"), _config.Current.StartWithWindows);
        AddSetting(root, ref row, T("Inicialização", "Startup"), _startWindows);
        _startMinimized = NewCheck(T("Iniciar já na bandeja", "Start in the notification area"), _config.Current.StartMinimized);
        AddSetting(root, ref row, T("Janela", "Window"), _startMinimized);
        _autoInstallApk = NewCheck(T("Instalar/atualizar o APK automaticamente ao conectar", "Install/update the APK automatically when connected"), _config.Current.AutoInstallApk);
        AddSetting(root, ref row, "Android USB", _autoInstallApk);
        _enableWakeOnLan = NewCheck(T("Mostrar 'Ligar computador' no celular quando desconectado", "Show 'Turn computer on' on the phone while disconnected"), _config.Current.EnableWakeOnLan);
        AddSetting(root, ref row, "Wake-on-LAN", _enableWakeOnLan);
        AddVisibilitySetting(root, ref row, "CPU", "cpu", _config.Current.ShowCpu);
        AddVisibilitySetting(root, ref row, "GPU", "gpu", _config.Current.ShowGpu);
        AddVisibilitySetting(root, ref row, "RAM", "ram", _config.Current.ShowRam);
        AddVisibilitySetting(root, ref row, "VRAM", "vram", _config.Current.ShowVram);
        AddVisibilitySetting(root, ref row, T("Rede", "Network"), "network", _config.Current.ShowNetwork);
        AddVisibilitySetting(root, ref row, T("Disco", "Disk"), "disk", _config.Current.ShowDisk);
        AddVisibilitySetting(root, ref row, T("FPS real do jogo (PresentMon)", "Real game FPS (PresentMon)"), "fps", _config.Current.ShowFps);
        _cpuElevated = NewNumeric(30, 110, (decimal)_config.Current.CpuElevatedTemperature);
        _cpuCritical = NewNumeric(31, 120, (decimal)_config.Current.CpuCriticalTemperature);
        _gpuElevated = NewNumeric(30, 110, (decimal)_config.Current.GpuElevatedTemperature);
        _gpuCritical = NewNumeric(31, 120, (decimal)_config.Current.GpuCriticalTemperature);
        AddSetting(root, ref row, T("CPU elevada (°C)", "CPU elevated (°C)"), _cpuElevated);
        AddSetting(root, ref row, T("CPU crítica (°C)", "CPU critical (°C)"), _cpuCritical);
        AddSetting(root, ref row, T("GPU elevada (°C)", "GPU elevated (°C)"), _gpuElevated);
        AddSetting(root, ref row, T("GPU crítica (°C)", "GPU critical (°C)"), _gpuCritical);
        scrollHost.Controls.Add(root);
        page.Controls.Add(scrollHost, 0, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(18, 10, 18, 8), BackColor = Color.FromArgb(24, 27, 31)
        };
        var saveButton = ActionButton(T("Salvar configurações", "Save settings"), SaveSettings);
        saveButton.AutoSize = false;
        saveButton.Size = new Size(190, 38);
        footer.Controls.Add(saveButton);
        page.Controls.Add(footer, 0, 1);
        tab.Controls.Add(page);
        return tab;
    }

    private void RefreshStatus()
    {
        SetStatusLabel(_serverStatus, _server.IsRunning ? T("● ATIVO", "● ACTIVE") : T("● PARADO", "● STOPPED"), _server.IsRunning ? Green : Red);
        _serverToggleButton.Text = _server.IsRunning ? T("Desligar servidor", "Turn server off") : T("Ligar servidor", "Turn server on");
        _serverToggleButton.Enabled = true;
        _serverToggleButton.BackColor = _server.IsRunning
            ? Color.FromArgb(91, 43, 47)
            : Color.FromArgb(36, 82, 62);
        _serverToggleButton.FlatAppearance.BorderColor = _server.IsRunning
            ? Color.FromArgb(156, 70, 76)
            : Color.FromArgb(66, 145, 103);
        SetStatusLabel(_adbStatus, File.Exists(_adb.AdbPath) ? T("● ATIVO", "● ACTIVE") : T("● NÃO INSTALADO", "● NOT INSTALLED"), File.Exists(_adb.AdbPath) ? Green : Orange);
        var status = _adb.Status;
        SetStatusLabel(_deviceStatus, status.State == AdbConnectionState.Connected ? T("● CONECTADO", "● CONNECTED") : T("● DESCONECTADO", "● DISCONNECTED"), status.State == AdbConnectionState.Connected ? Green : status.State == AdbConnectionState.Unauthorized ? Orange : Red);
        _detailStatus.Text = status.Message;
        var snapshot = _hardware.Current;
        var cpuAvailable = snapshot.Cpu.Usage.HasValue || snapshot.Cpu.Temperature.HasValue || snapshot.Cpu.Clock.HasValue;
        var gpuDetected = snapshot.Gpu.Name != "N/A" && !snapshot.Gpu.Name.Contains("não identificada", StringComparison.OrdinalIgnoreCase);
        var gpuAvailable = snapshot.Gpu.Usage.HasValue || snapshot.Gpu.Temperature.HasValue || snapshot.Gpu.Clock.HasValue;
        var essential = cpuAvailable && (!gpuDetected || gpuAvailable) && snapshot.Ram.Total > 0;
        SetStatusLabel(_sensorStatus, _hardware.DetectedSensors.Count == 0 ? T("● AGUARDANDO", "● WAITING") : essential ? "● OK" : T("● PARCIAL", "● PARTIAL"), _hardware.DetectedSensors.Count == 0 ? Orange : essential ? Green : Orange);
        if (snapshot.Cpu.Temperature is { } cpuTemperature)
        {
            var cpuColor = cpuTemperature >= _config.Current.CpuCriticalTemperature ? Red :
                cpuTemperature >= _config.Current.CpuElevatedTemperature ? Orange : Cyan;
            SetStatusLabel(_cpuTemperatureStatus, $"{cpuTemperature:0} °C  •  {_hardware.CpuTemperatureSource}", cpuColor);
        }
        else
        {
            SetStatusLabel(_cpuTemperatureStatus, T("INDISPONÍVEL", "UNAVAILABLE") + "  •  " + _hardware.CpuTemperatureSource, Orange);
        }
        var profile = _hardware.Profile;
        var additionalGpus = profile.Gpus
            .Where(x => !string.Equals(x, profile.PrimaryGpu, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _systemProfileStatus.Text =
            $"PC: {profile.ComputerName}    •    {profile.OperatingSystem}\r\n" +
            T($"Placa-mãe: {profile.Motherboard}\r\n", $"Motherboard: {profile.Motherboard}\r\n") +
            $"CPU: {profile.Cpu}\r\n" +
            T($"GPU principal: {profile.PrimaryGpu}", $"Primary GPU: {profile.PrimaryGpu}") +
            (additionalGpus.Length == 0 ? "" : T($"    •    Outras GPUs: {string.Join(", ", additionalGpus)}", $"    •    Other GPUs: {string.Join(", ", additionalGpus)}")) + "\r\n" +
            T($"RAM instalada: {profile.RamTotal:0.##} GB", $"Installed RAM: {profile.RamTotal:0.##} GB");
        var wake = WakeOnLanService.Detect(_config.Current.EnableWakeOnLan);
        _systemProfileStatus.Text += wake.Available
            ? T($"\r\nWake-on-LAN (Windows): pronto • {wake.AdapterName} • {wake.BroadcastAddress} • confirme Resume By PCI-E na BIOS",
                $"\r\nWake-on-LAN (Windows): ready • {wake.AdapterName} • {wake.BroadcastAddress} • confirm Resume By PCI-E in BIOS")
            : T("\r\nWake-on-LAN: conecte este PC ao roteador por cabo Ethernet", "\r\nWake-on-LAN: connect this PC to the router with Ethernet");
        if (_config.Current.ShowFps)
            _systemProfileStatus.Text += "\r\nFPS: " + _hardware.FpsStatus;
        _tray.Text = status.State == AdbConnectionState.Connected ? T("PC Monitor USB — conectado", "PC Monitor USB — connected") : T("PC Monitor USB — desconectado", "PC Monitor USB — disconnected");
        _trayConnectionStatus.Text = status.State == AdbConnectionState.Connected ? T("● Celular conectado", "● Phone connected") : T("● Celular desconectado", "● Phone disconnected");
        _trayConnectionStatus.ForeColor = status.State == AdbConnectionState.Connected ? Green : Muted;
    }

    private void RefreshSensorGrid()
    {
        _sensorGrid.SuspendLayout();
        _sensorGrid.Rows.Clear();
        foreach (var sensor in _hardware.DetectedSensors)
            _sensorGrid.Rows.Add(sensor.Hardware, sensor.HardwareType, sensor.HardwareIdentifier, sensor.Sensor, sensor.SensorType, sensor.Value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "--", sensor.Identifier);
        _sensorGrid.ResumeLayout();
    }

    private async void DownloadAdbAsync(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            T("Os Android SDK Platform-Tools serão baixados diretamente de dl.google.com.\n\nAo continuar, você confirma que leu e aceita o Android SDK License Agreement. O pacote será usado localmente e não será redistribuído com este projeto.\n\nContinuar?",
              "Android SDK Platform-Tools will be downloaded directly from dl.google.com.\n\nBy continuing, you confirm that you have read and accept the Android SDK License Agreement. The package will be used locally and will not be redistributed with this project.\n\nContinue?"),
            T("Baixar ADB oficial", "Download official ADB"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes) return;
        try
        {
            UseWaitCursor = true;
            var progress = new Progress<int>(value => _detailStatus.Text = T($"Baixando Android Platform-Tools... {value}%", $"Downloading Android Platform-Tools... {value}%"));
            await AdbProvisioner.ProvisionAsync(progress);
            _detailStatus.Text = T("ADB instalado localmente. Conecte e autorize o dispositivo Android.", "ADB installed locally. Connect and authorize the Android device.");
            await _adb.CheckAsync();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao baixar Platform-Tools.", ex);
            MessageBox.Show(T("Falha ao baixar os componentes oficiais do ADB.", "Failed to download the official ADB components.") + "\n\n" + ex.Message, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async void InstallApkAsync(object? sender, EventArgs e)
    {
        var result = await _adb.InstallApkAsync(Path.Combine(AppContext.BaseDirectory, "PCMonitorUSB.apk"));
        MessageBox.Show(result.Success ? T("APK instalado/atualizado com sucesso.", "APK installed/updated successfully.") : result.Error, "PC Monitor USB", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async void ConfigurePhoneAsync(object? sender, EventArgs e)
    {
        if (!File.Exists(_adb.AdbPath))
        {
            DownloadAdbAsync(sender, e);
            return;
        }

        if (_adb.Status.State == AdbConnectionState.AppMissing)
        {
            InstallApkAsync(sender, e);
            return;
        }

        await _adb.CheckAsync();
        var message = _adb.Status.State switch
        {
            AdbConnectionState.Connected => T("Celular conectado e pronto para uso.", "Phone connected and ready to use."),
            AdbConnectionState.Unauthorized => T("Desbloqueie o celular e aceite a autorização de depuração USB.", "Unlock the phone and accept USB debugging authorization."),
            _ => _adb.Status.Message
        };
        MessageBox.Show(message, T("Configurar celular", "Set up phone"), MessageBoxButtons.OK,
            _adb.Status.State == AdbConnectionState.Connected ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async void ToggleServerAsync(object? sender, EventArgs e)
    {
        try
        {
            _serverToggleButton.Enabled = false;
            if (_server.IsRunning)
            {
                await _server.StopAsync();
                _detailStatus.Text = T("Servidor desligado. O painel USB ficará indisponível até ser ligado novamente.", "Server stopped. The USB panel will remain unavailable until the server is turned on again.");
            }
            else
            {
                await _server.StartAsync();
                _detailStatus.Text = T("Servidor ligado e aguardando o painel USB.", "Server started and waiting for the USB panel.");
            }
            RefreshStatus();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Não foi possível alterar o estado do servidor local.", ex);
            MessageBox.Show(T($"Não foi possível alterar o servidor na porta {_config.Current.Port}.", $"Could not change the server state on port {_config.Current.Port}.") + $"\n\n{ex.Message}",
                "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }
    }

    private async void InstallPawnIoAsync(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            T($"Em alguns PCs AMD ou Intel, a temperatura, o clock e a potência da CPU dependem do driver de acesso ao hardware PawnIO {PawnIoProvisioner.Version}.\n\n" +
              "O PC Monitor USB baixará o instalador da publicação oficial no GitHub, verificará o SHA-256 e só então o abrirá. " +
              "A instalação é persistente, pode exigir uma reinicialização e pode ser incompatível com alguns sistemas antitrapaça, como o FACEIT. " +
              "Nada será instalado silenciosamente.\n\nDeseja baixar e abrir o instalador oficial agora?",
              $"On some AMD or Intel PCs, CPU temperature, clock, and power require the PawnIO hardware access driver {PawnIoProvisioner.Version}.\n\n" +
              "PC Monitor USB will download the installer from the official GitHub release, verify its SHA-256, and only then open it. " +
              "Installation is persistent, may require a later restart, and may be incompatible with some anti-cheat systems such as FACEIT. " +
              "Nothing will be installed silently.\n\nDo you want to download and open the official installer now?"),
            T("Ampliar suporte aos sensores", "Extend sensor support"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        try
        {
            UseWaitCursor = true;
            var progress = new Progress<int>(value => _detailStatus.Text = T($"Baixando o suporte oficial PawnIO... {value}%", $"Downloading official PawnIO support... {value}%"));
            var result = await PawnIoProvisioner.DownloadAndRunAsync(progress);
            if (!result.Started)
            {
                MessageBox.Show(T("Não foi possível instalar o suporte aos sensores.", "Could not install sensor support.") + "\n\n" + result.Error,
                    "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var message = result.RebootRequired
                ? T("O PawnIO foi instalado. Reinicie o Windows posteriormente para ativar a leitura completa dos sensores.", "PawnIO was installed. Restart Windows later to enable complete sensor readings.")
                : T("O PawnIO foi instalado. Feche e abra novamente o PC Monitor USB; se alguns valores continuarem ausentes, uma reinicialização posterior pode ser necessária.", "PawnIO was installed. Close and reopen PC Monitor USB; if some values are still missing, a later restart may be required.");
            MessageBox.Show(message, T("Suporte aos sensores instalado", "Sensor support installed"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            _detailStatus.Text = message;
        }
        finally { UseWaitCursor = false; }
    }

    private void ExportSensors(object? sender, EventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sensors.txt");
        var builder = new StringBuilder();
        builder.AppendLine(T("PC Monitor USB - Sensores detectados", "PC Monitor USB - Detected sensors"));
        builder.AppendLine(T($"Gerado em: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}", $"Generated at: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}"));
        builder.AppendLine("Hardware\tHardwareType\tHardwareIdentifier\tSensor\tSensorType\tValue\tIdentifier");
        foreach (var sensor in _hardware.DetectedSensors)
            builder.AppendLine($"{sensor.Hardware}\t{sensor.HardwareType}\t{sensor.HardwareIdentifier}\t{sensor.Sensor}\t{sensor.SensorType}\t{sensor.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--"}\t{sensor.Identifier}");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        MessageBox.Show(T("Sensores exportados para:\n", "Sensors exported to:\n") + path, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveButtons(object? sender, EventArgs e)
    {
        var config = _config.Current;
        foreach (var item in _builtInButtons.Items.Cast<ButtonListItem>())
            config.Buttons.First(x => x.Id == item.Id).Enabled = _builtInButtons.CheckedItems.Contains(item);
        var customs = config.Buttons.Where(x => !x.BuiltIn).ToArray();
        for (var i = 0; i < customs.Length && i < _customButtons.Rows.Count; i++)
        {
            var row = _customButtons.Rows[i];
            customs[i].Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false);
            customs[i].Label = Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? T($"PERSONALIZADO {i + 1}", $"CUSTOM {i + 1}");
            var icon = Convert.ToString(row.Cells["Icon"].Value)?.Trim() ?? "";
            customs[i].Icon = icon.Length <= 4 ? icon : icon[..4];
            customs[i].Action = Convert.ToString(row.Cells["Action"].Value) ?? "none";
            customs[i].Target = Convert.ToString(row.Cells["Target"].Value)?.Trim();
        }
        _config.Save(config);
        MessageBox.Show(T("Botões salvos. O painel atualizará a lista automaticamente.", "Buttons saved. The panel will refresh the list automatically."), "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        var config = _config.Current;
        var startupWasEnabled = config.StartWithWindows;
        config.Port = (int)_port.Value;
        config.UpdateIntervalMs = int.Parse((_interval.SelectedItem?.ToString() ?? "1000 ms").Split(' ')[0]);
        config.Language = _language.SelectedIndex switch { 1 => "pt", 2 => "en", _ => "auto" };
        config.StartWithWindows = _startWindows.Checked;
        config.StartMinimized = _startMinimized.Checked;
        config.AutoInstallApk = _autoInstallApk.Checked;
        config.EnableWakeOnLan = _enableWakeOnLan.Checked;
        config.ShowCpu = _visibility["cpu"].Checked;
        config.ShowGpu = _visibility["gpu"].Checked;
        config.ShowRam = _visibility["ram"].Checked;
        config.ShowVram = _visibility["vram"].Checked;
        config.ShowNetwork = _visibility["network"].Checked;
        config.ShowDisk = _visibility["disk"].Checked;
        config.ShowFps = _visibility["fps"].Checked;
        config.CpuElevatedTemperature = (float)_cpuElevated.Value;
        config.CpuCriticalTemperature = (float)_cpuCritical.Value;
        config.GpuElevatedTemperature = (float)_gpuElevated.Value;
        config.GpuCriticalTemperature = (float)_gpuCritical.Value;
        try
        {
            if (!startupWasEnabled && config.StartWithWindows)
            {
                var consent = MessageBox.Show(
                    T("Para iniciar automaticamente com acesso aos sensores, o PC Monitor USB copiará o EXE e o APK para uma pasta protegida em Arquivos de Programas e criará uma tarefa agendada chamada 'PC Monitor USB', executada somente no seu login e com privilégios elevados. Isso impede que um arquivo portátil gravável seja usado para obter elevação.\n\nDeseja continuar?",
                      "To start automatically with sensor access, PC Monitor USB will copy the EXE and APK to a protected Program Files folder and create a scheduled task named 'PC Monitor USB', which runs only when you sign in and with elevated privileges. This prevents a writable portable file from being used for elevation.\n\nDo you want to continue?"),
                    T("Confirmar início com Windows", "Confirm Windows startup"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (consent != DialogResult.Yes)
                {
                    config.StartWithWindows = false;
                    _startWindows.Checked = false;
                }
            }
            SetStartup(config.StartWithWindows);
            _config.Save(config);
            MessageBox.Show(T(
                "Configurações salvas. Porta, intervalo e coleta de FPS entram em vigor após fechar e abrir o aplicativo. Para aplicar outro idioma em toda a interface, também reabra o aplicativo.",
                "Settings saved. Port, interval, and FPS capture take effect after closing and reopening the application. Reopen the application to apply another language as well."),
                "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao salvar configurações.", ex);
            MessageBox.Show(T("Não foi possível salvar as configurações.", "Could not save the settings.") + "\n\n" + ex.Message, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key.DeleteValue("PCMonitorUSB", false);
        key.DeleteValue("J4PCMonitor", false);
        DeleteStartupTask("J4 PC Monitor");

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (enabled)
        {
            var startupExecutable = PrepareSecureStartupCopy();
            startInfo.ArgumentList.Add("/Create");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add("PC Monitor USB");
            startInfo.ArgumentList.Add("/TR");
            startInfo.ArgumentList.Add($"\"{startupExecutable}\" --minimized");
            startInfo.ArgumentList.Add("/SC");
            startInfo.ArgumentList.Add("ONLOGON");
            startInfo.ArgumentList.Add("/RL");
            startInfo.ArgumentList.Add("HIGHEST");
            startInfo.ArgumentList.Add("/F");
        }
        else
        {
            startInfo.ArgumentList.Add("/Delete");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add("PC Monitor USB");
            startInfo.ArgumentList.Add("/F");
        }

        using var process = Process.Start(startInfo);
        process?.WaitForExit(5000);
        if (enabled && process?.ExitCode != 0)
            throw new InvalidOperationException(T("O Windows não conseguiu criar a tarefa de inicialização.", "Windows could not create the startup task."));
    }

    internal static string GetSecureStartupExecutablePath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            throw new InvalidOperationException(T("A pasta Arquivos de Programas não foi encontrada.", "The Program Files folder could not be found."));
        return Path.Combine(programFiles, "PC Monitor USB", "PCMonitorServer.exe");
    }

    internal static bool IsSecureStartupLocation(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            return roots.Where(x => !string.IsNullOrWhiteSpace(x)).Any(root =>
                fullPath.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string PrepareSecureStartupCopy()
    {
        var sourceExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        var destinationExecutable = GetSecureStartupExecutablePath();
        var destinationDirectory = Path.GetDirectoryName(destinationExecutable)!;
        Directory.CreateDirectory(destinationDirectory);
        if ((File.GetAttributes(destinationDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(T("A pasta protegida de inicialização não pode ser um link ou ponto de junção.", "The protected startup folder cannot be a link or junction."));

        if (!string.Equals(Path.GetFullPath(sourceExecutable), Path.GetFullPath(destinationExecutable), StringComparison.OrdinalIgnoreCase))
        {
            var sourceVersion = FileVersionInfo.GetVersionInfo(sourceExecutable).FileVersion;
            var destinationVersion = File.Exists(destinationExecutable)
                ? FileVersionInfo.GetVersionInfo(destinationExecutable).FileVersion
                : null;
            if (!File.Exists(destinationExecutable) || !string.Equals(sourceVersion, destinationVersion, StringComparison.Ordinal))
                File.Copy(sourceExecutable, destinationExecutable, true);
        }

        var sourceApk = Path.Combine(AppContext.BaseDirectory, "PCMonitorUSB.apk");
        var destinationApk = Path.Combine(destinationDirectory, "PCMonitorUSB.apk");
        if (File.Exists(sourceApk) && !string.Equals(Path.GetFullPath(sourceApk), Path.GetFullPath(destinationApk), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourceApk, destinationApk, true);
        return destinationExecutable;
    }

    private static void DeleteStartupTask(string taskName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/Delete");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(taskName);
            startInfo.ArgumentList.Add("/F");
            using var process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
        catch
        {
            // A tarefa antiga pode não existir.
        }
    }

    private void RestartElevated(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!, Verb = "runas", UseShellExecute = true,
                Arguments = $"--wait-for-pid {Environment.ProcessId}"
            });
            _exitRequested = true;
            Close();
        }
        catch (Exception ex) { SimpleLog.Warn("Elevação cancelada ou indisponível: " + ex.Message); }
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        _trayConnectionStatus = new ToolStripMenuItem(T("● Celular desconectado", "● Phone disconnected")) { Enabled = false, ForeColor = Muted };
        menu.Items.Add(_trayConnectionStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Abrir", "Open"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Sair", "Exit"), null, (_, _) => { _exitRequested = true; Close(); });
        var tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "PC Monitor USB", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => RestoreFromTray();
        return tray;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _uiTimer.Stop();
        _brandImage?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && !_exitRequested)
            BeginInvoke(HideToTray);
    }

    private void HideToTray() { ShowInTaskbar = false; Hide(); Opacity = 1; }
    private void RestoreFromTray() { Opacity = 1; ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void AdbOnStatusChanged(object? sender, AdbStatus status) { if (!IsDisposed) { if (InvokeRequired) BeginInvoke(RefreshStatus); else RefreshStatus(); } }
    private static void OpenTarget(string target) { try { Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true }); } catch (Exception ex) { ShowError(ex.Message); } }
    private static void ShowError(string text) => MessageBox.Show(text, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static TabPage NewTab(string title) => new(title) { BackColor = Surface, ForeColor = Foreground, Padding = new Padding(8) };

    private static Button ActionButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 51, 58), ForeColor = Foreground, Margin = new Padding(4) };
        button.FlatAppearance.BorderColor = Color.FromArgb(75, 81, 91);
        button.Click += handler;
        return button;
    }

    private void ShowCpuDiagnostic(object? sender, EventArgs e)
    {
        var temperature = _hardware.Current.Cpu.Temperature;
        var text = temperature.HasValue
            ? T($"Temperatura da CPU: {temperature:0} °C\nFonte: {_hardware.CpuTemperatureSource}\n\nA leitura está funcionando corretamente.", $"CPU temperature: {temperature:0} °C\nSource: {_hardware.CpuTemperatureSource}\n\nThe reading is working correctly.")
            : T($"A temperatura da CPU ainda não está disponível.\n\nFonte: {_hardware.CpuTemperatureSource}\nAdministrador: {(IsAdministrator() ? "sim" : "não")}\n\nUse 'Ampliar suporte aos sensores'. O programa pedirá confirmação antes de baixar ou instalar o driver PawnIO. Depois, exporte sensors.txt se o valor continuar ausente.",
                $"CPU temperature is not available yet.\n\nSource: {_hardware.CpuTemperatureSource}\nAdministrator: {(IsAdministrator() ? "yes" : "no")}\n\nUse 'Extend sensor support'. The program will ask for confirmation before downloading or installing PawnIO. Then export sensors.txt if the value is still missing.");
        MessageBox.Show(text, T("Diagnóstico da temperatura da CPU", "CPU temperature diagnostics"), MessageBoxButtons.OK,
            temperature.HasValue ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static Icon? LoadAppIcon()
    {
        try { return Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null; }
        catch { return null; }
    }

    private static Image? LoadBrandImage()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PCMonitorUSB.Assets.pc-monitor-usb-brand.png");
            if (stream is null) return null;
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch { return null; }
    }

    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var selected = e.Index == tabs.SelectedIndex;
        using var background = new SolidBrush(selected ? Color.FromArgb(38, 45, 51) : Color.FromArgb(23, 26, 30));
        using var foreground = new SolidBrush(selected ? Cyan : Muted);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
            using (var accent = new SolidBrush(Cyan))
                e.Graphics.FillRectangle(accent, e.Bounds.Left + 8, e.Bounds.Bottom - 3, e.Bounds.Width - 16, 3);
        var text = tabs.TabPages[e.Index].Text;
        var size = e.Graphics.MeasureString(text, tabs.Font);
        e.Graphics.DrawString(text, tabs.Font, foreground,
            e.Bounds.Left + (e.Bounds.Width - size.Width) / 2,
            e.Bounds.Top + (e.Bounds.Height - size.Height) / 2);
    }

    private static void AddStatusRow(TableLayoutPanel panel, int row, string title, out Label value)
    {
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        value = new Label { Dock = DockStyle.Fill, ForeColor = Foreground, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 10f) };
        panel.Controls.Add(value, 1, row);
    }

    private static void SetStatusLabel(Label label, string text, Color color) { label.Text = text; label.ForeColor = color; }
    private static NumericUpDown NewNumeric(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 160 };
    private static CheckBox NewCheck(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, ForeColor = Foreground };
    private void AddVisibilitySetting(TableLayoutPanel panel, ref int row, string title, string key, bool value) { var check = NewCheck(T("Mostrar no painel", "Show on panel"), value); _visibility[key] = check; AddSetting(panel, ref row, title, check); }

    private static void AddSetting(TableLayoutPanel panel, ref int row, string title, Control control)
    {
        panel.RowCount = row + 1;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Anchor = AnchorStyles.Left;
        panel.Controls.Add(control, 1, row);
        row++;
    }

    private sealed record ButtonListItem(string Id, string Label) { public override string ToString() => Label; }
}
