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
    private CheckBox _startWindows = null!;
    private CheckBox _startMinimized = null!;
    private CheckBox _autoInstallApk = null!;
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
            Text = "DISPLAY USB  •  MONITORAMENTO E CONTROLE", Dock = DockStyle.Fill,
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
        var tab = NewTab("Visão geral");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(20), BackColor = Surface };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        AddStatusRow(panel, 0, "Servidor:", out _serverStatus);
        AddStatusRow(panel, 1, "ADB:", out _adbStatus);
        AddStatusRow(panel, 2, "Celular:", out _deviceStatus);
        AddStatusRow(panel, 3, "Sensores:", out _sensorStatus);
        AddStatusRow(panel, 4, "Temperatura da CPU:", out _cpuTemperatureStatus);
        _detailStatus = new Label { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        panel.Controls.Add(_detailStatus, 0, 5);
        panel.SetColumnSpan(_detailStatus, 2);

        var profileBox = new GroupBox
        {
            Text = "Configuração identificada neste PC", Dock = DockStyle.Fill, ForeColor = Cyan,
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
        _serverToggleButton = ActionButton("Ligar servidor", ToggleServerAsync);
        actions.Controls.Add(_serverToggleButton);
        actions.Controls.Add(ActionButton("Configurar celular", ConfigurePhoneAsync));
        panel.Controls.Add(actions, 0, 7);
        panel.SetColumnSpan(actions, 2);
        var privacyHint = new Label
        {
            Text = "O servidor escuta somente em 127.0.0.1. Nenhum dado é enviado para internet ou nuvem.",
            Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(privacyHint, 0, 8);
        panel.SetColumnSpan(privacyHint, 2);
        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage BuildSensorsTab()
    {
        var tab = NewTab("Sensores");
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
        _sensorGrid.Columns.Add("HardwareType", "Tipo de hardware");
        _sensorGrid.Columns.Add("HardwareIdentifier", "ID do hardware");
        _sensorGrid.Columns.Add("Sensor", "Sensor");
        _sensorGrid.Columns.Add("SensorType", "Tipo");
        _sensorGrid.Columns.Add("Value", "Valor");
        _sensorGrid.Columns.Add("Identifier", "Identificador");
        root.Controls.Add(_sensorGrid, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(ActionButton("Atualizar lista", (_, _) => RefreshSensorGrid()));
        actions.Controls.Add(ActionButton("Exportar sensors.txt", ExportSensors));
        actions.Controls.Add(ActionButton("Ampliar suporte aos sensores", InstallPawnIoAsync));
        root.Controls.Add(actions, 0, 1);
        tab.Controls.Add(root);
        tab.Enter += (_, _) => RefreshSensorGrid();
        return tab;
    }

    private TabPage BuildButtonsTab()
    {
        var tab = NewTab("Botões");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(16), BackColor = Surface };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = "Botões permitidos no celular", Dock = DockStyle.Fill, ForeColor = Foreground }, 0, 0);
        _builtInButtons = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Background, ForeColor = Foreground, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
        foreach (var button in _config.Current.Buttons.Where(x => x.BuiltIn))
            _builtInButtons.Items.Add(new ButtonListItem(button.Id, button.Label), button.Enabled);
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
        _customButtons.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Ativo", FillWeight = 35 });
        _customButtons.Columns.Add("Label", "Nome");
        _customButtons.Columns.Add(new DataGridViewTextBoxColumn { Name = "Icon", HeaderText = "Ícone", FillWeight = 35 });
        _customButtons.Columns.Add(new DataGridViewComboBoxColumn { Name = "Action", HeaderText = "Ação", DataSource = new[] { "none", "open_program", "open_url", "hotkey" } });
        _customButtons.Columns.Add("Target", "Programa, URL ou atalho (ex.: CTRL+ALT+F10)");
        foreach (var button in _config.Current.Buttons.Where(x => !x.BuiltIn))
            _customButtons.Rows.Add(button.Enabled, button.Label, button.Icon, button.Action, button.Target ?? "");
        root.Controls.Add(_customButtons, 0, 2);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill };
        bottom.Controls.Add(ActionButton("Salvar botões", SaveButtons));
        bottom.Controls.Add(new Label { Text = "O celular envia somente o ID; caminhos permanecem no PC.", AutoSize = true, ForeColor = Muted, Padding = new Padding(12, 10, 0, 0) });
        root.Controls.Add(bottom, 0, 3);
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage BuildSettingsTab()
    {
        var tab = NewTab("Configurações");
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
        AddSetting(root, ref row, "Porta local", _port);
        _interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _interval.Items.AddRange(["500 ms", "1000 ms", "2000 ms"]);
        _interval.SelectedItem = _config.Current.UpdateIntervalMs + " ms";
        AddSetting(root, ref row, "Intervalo de atualização", _interval);
        _startWindows = NewCheck("Iniciar com Windows", _config.Current.StartWithWindows);
        AddSetting(root, ref row, "Inicialização", _startWindows);
        _startMinimized = NewCheck("Iniciar já na bandeja", _config.Current.StartMinimized);
        AddSetting(root, ref row, "Janela", _startMinimized);
        _autoInstallApk = NewCheck("Instalar/atualizar o APK automaticamente ao conectar", _config.Current.AutoInstallApk);
        AddSetting(root, ref row, "Android USB", _autoInstallApk);
        AddVisibilitySetting(root, ref row, "CPU", "cpu", _config.Current.ShowCpu);
        AddVisibilitySetting(root, ref row, "GPU", "gpu", _config.Current.ShowGpu);
        AddVisibilitySetting(root, ref row, "RAM", "ram", _config.Current.ShowRam);
        AddVisibilitySetting(root, ref row, "VRAM", "vram", _config.Current.ShowVram);
        AddVisibilitySetting(root, ref row, "Rede", "network", _config.Current.ShowNetwork);
        AddVisibilitySetting(root, ref row, "Disco", "disk", _config.Current.ShowDisk);
        AddVisibilitySetting(root, ref row, "FPS (somente quando houver fonte real)", "fps", _config.Current.ShowFps);
        _cpuElevated = NewNumeric(30, 110, (decimal)_config.Current.CpuElevatedTemperature);
        _cpuCritical = NewNumeric(31, 120, (decimal)_config.Current.CpuCriticalTemperature);
        _gpuElevated = NewNumeric(30, 110, (decimal)_config.Current.GpuElevatedTemperature);
        _gpuCritical = NewNumeric(31, 120, (decimal)_config.Current.GpuCriticalTemperature);
        AddSetting(root, ref row, "CPU elevada (°C)", _cpuElevated);
        AddSetting(root, ref row, "CPU crítica (°C)", _cpuCritical);
        AddSetting(root, ref row, "GPU elevada (°C)", _gpuElevated);
        AddSetting(root, ref row, "GPU crítica (°C)", _gpuCritical);
        scrollHost.Controls.Add(root);
        page.Controls.Add(scrollHost, 0, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(18, 10, 18, 8), BackColor = Color.FromArgb(24, 27, 31)
        };
        var saveButton = ActionButton("Salvar configurações", SaveSettings);
        saveButton.AutoSize = false;
        saveButton.Size = new Size(190, 38);
        footer.Controls.Add(saveButton);
        page.Controls.Add(footer, 0, 1);
        tab.Controls.Add(page);
        return tab;
    }

    private void RefreshStatus()
    {
        SetStatusLabel(_serverStatus, _server.IsRunning ? "● ATIVO" : "● PARADO", _server.IsRunning ? Green : Red);
        _serverToggleButton.Text = _server.IsRunning ? "Desligar servidor" : "Ligar servidor";
        _serverToggleButton.Enabled = true;
        _serverToggleButton.BackColor = _server.IsRunning
            ? Color.FromArgb(91, 43, 47)
            : Color.FromArgb(36, 82, 62);
        _serverToggleButton.FlatAppearance.BorderColor = _server.IsRunning
            ? Color.FromArgb(156, 70, 76)
            : Color.FromArgb(66, 145, 103);
        SetStatusLabel(_adbStatus, File.Exists(_adb.AdbPath) ? "● ATIVO" : "● NÃO INSTALADO", File.Exists(_adb.AdbPath) ? Green : Orange);
        var status = _adb.Status;
        SetStatusLabel(_deviceStatus, status.State == AdbConnectionState.Connected ? "● CONECTADO" : "● DESCONECTADO", status.State == AdbConnectionState.Connected ? Green : status.State == AdbConnectionState.Unauthorized ? Orange : Red);
        _detailStatus.Text = status.Message;
        var snapshot = _hardware.Current;
        var cpuAvailable = snapshot.Cpu.Usage.HasValue || snapshot.Cpu.Temperature.HasValue || snapshot.Cpu.Clock.HasValue;
        var gpuDetected = !snapshot.Gpu.Name.Contains("não identificada", StringComparison.OrdinalIgnoreCase);
        var gpuAvailable = snapshot.Gpu.Usage.HasValue || snapshot.Gpu.Temperature.HasValue || snapshot.Gpu.Clock.HasValue;
        var essential = cpuAvailable && (!gpuDetected || gpuAvailable) && snapshot.Ram.Total > 0;
        SetStatusLabel(_sensorStatus, _hardware.DetectedSensors.Count == 0 ? "● AGUARDANDO" : essential ? "● OK" : "● PARCIAL", _hardware.DetectedSensors.Count == 0 ? Orange : essential ? Green : Orange);
        if (snapshot.Cpu.Temperature is { } cpuTemperature)
        {
            var cpuColor = cpuTemperature >= _config.Current.CpuCriticalTemperature ? Red :
                cpuTemperature >= _config.Current.CpuElevatedTemperature ? Orange : Cyan;
            SetStatusLabel(_cpuTemperatureStatus, $"{cpuTemperature:0} °C  •  {_hardware.CpuTemperatureSource}", cpuColor);
        }
        else
        {
            SetStatusLabel(_cpuTemperatureStatus, "INDISPONÍVEL  •  " + _hardware.CpuTemperatureSource, Orange);
        }
        var profile = _hardware.Profile;
        var additionalGpus = profile.Gpus
            .Where(x => !string.Equals(x, profile.PrimaryGpu, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _systemProfileStatus.Text =
            $"PC: {profile.ComputerName}    •    {profile.OperatingSystem}\r\n" +
            $"Placa-mãe: {profile.Motherboard}\r\n" +
            $"CPU: {profile.Cpu}\r\n" +
            $"GPU principal: {profile.PrimaryGpu}" +
            (additionalGpus.Length == 0 ? "" : $"    •    Outras GPUs: {string.Join(", ", additionalGpus)}") + "\r\n" +
            $"RAM instalada: {profile.RamTotal:0.##} GB";
        _tray.Text = status.State == AdbConnectionState.Connected ? "PC Monitor USB — conectado" : "PC Monitor USB — desconectado";
        _trayConnectionStatus.Text = status.State == AdbConnectionState.Connected ? "● Celular conectado" : "● Celular desconectado";
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
            "Os Android SDK Platform-Tools serão baixados diretamente de dl.google.com.\n\nAo continuar, você confirma que leu e aceita o Android SDK License Agreement. O pacote será usado localmente e não será redistribuído com este projeto.\n\nContinuar?",
            "Baixar ADB oficial", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes) return;
        try
        {
            UseWaitCursor = true;
            var progress = new Progress<int>(value => _detailStatus.Text = $"Baixando Android Platform-Tools... {value}%");
            await AdbProvisioner.ProvisionAsync(progress);
            _detailStatus.Text = "ADB instalado localmente. Conecte e autorize o dispositivo Android.";
            await _adb.CheckAsync();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao baixar Platform-Tools.", ex);
            MessageBox.Show("Falha ao baixar os componentes oficiais do ADB.\n\n" + ex.Message, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    private async void InstallApkAsync(object? sender, EventArgs e)
    {
        var result = await _adb.InstallApkAsync(Path.Combine(AppContext.BaseDirectory, "PCMonitorUSB.apk"));
        MessageBox.Show(result.Success ? "APK instalado/atualizado com sucesso." : result.Error, "PC Monitor USB", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
            AdbConnectionState.Connected => "Celular conectado e pronto para uso.",
            AdbConnectionState.Unauthorized => "Desbloqueie o celular e aceite a autorização de depuração USB.",
            _ => _adb.Status.Message
        };
        MessageBox.Show(message, "Configurar celular", MessageBoxButtons.OK,
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
                _detailStatus.Text = "Servidor desligado. O painel USB ficará indisponível até ser ligado novamente.";
            }
            else
            {
                await _server.StartAsync();
                _detailStatus.Text = "Servidor ligado e aguardando o painel USB.";
            }
            RefreshStatus();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Não foi possível alterar o estado do servidor local.", ex);
            MessageBox.Show($"Não foi possível alterar o servidor na porta {_config.Current.Port}.\n\n{ex.Message}",
                "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }
    }

    private async void InstallPawnIoAsync(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            $"Em alguns PCs AMD ou Intel, temperatura, clock e potência da CPU dependem do driver de acesso de hardware PawnIO {PawnIoProvisioner.Version}.\n\n" +
            "O PC Monitor USB baixará o instalador da publicação oficial no GitHub, verificará o SHA-256 e só então o abrirá. " +
            "A instalação é persistente, pode exigir reinicialização e pode ser incompatível com alguns anti-cheats, como FACEIT. " +
            "Nada será instalado silenciosamente.\n\nDeseja baixar e abrir o instalador oficial agora?",
            "Ampliar suporte aos sensores", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        try
        {
            UseWaitCursor = true;
            var progress = new Progress<int>(value => _detailStatus.Text = $"Baixando suporte PawnIO oficial... {value}%");
            var result = await PawnIoProvisioner.DownloadAndRunAsync(progress);
            if (!result.Started)
            {
                MessageBox.Show("Não foi possível instalar o suporte de sensores.\n\n" + result.Error,
                    "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var message = result.RebootRequired
                ? "PawnIO foi instalado. Reinicie o Windows posteriormente para ativar a leitura completa dos sensores."
                : "PawnIO foi instalado. Feche e abra novamente o PC Monitor USB; se alguns valores continuarem ausentes, uma reinicialização posterior pode ser necessária.";
            MessageBox.Show(message, "Suporte de sensores instalado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _detailStatus.Text = message;
        }
        finally { UseWaitCursor = false; }
    }

    private void ExportSensors(object? sender, EventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sensors.txt");
        var builder = new StringBuilder();
        builder.AppendLine("PC Monitor USB - Sensores detectados");
        builder.AppendLine($"Gerado em: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine("Hardware\tHardwareType\tHardwareIdentifier\tSensor\tSensorType\tValue\tIdentifier");
        foreach (var sensor in _hardware.DetectedSensors)
            builder.AppendLine($"{sensor.Hardware}\t{sensor.HardwareType}\t{sensor.HardwareIdentifier}\t{sensor.Sensor}\t{sensor.SensorType}\t{sensor.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--"}\t{sensor.Identifier}");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        MessageBox.Show("Sensores exportados para:\n" + path, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            customs[i].Label = Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? $"PERSONALIZADO {i + 1}";
            var icon = Convert.ToString(row.Cells["Icon"].Value)?.Trim() ?? "";
            customs[i].Icon = icon.Length <= 4 ? icon : icon[..4];
            customs[i].Action = Convert.ToString(row.Cells["Action"].Value) ?? "none";
            customs[i].Target = Convert.ToString(row.Cells["Target"].Value)?.Trim();
        }
        _config.Save(config);
        MessageBox.Show("Botões salvos. O painel atualizará a lista automaticamente.", "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        var config = _config.Current;
        var startupWasEnabled = config.StartWithWindows;
        config.Port = (int)_port.Value;
        config.UpdateIntervalMs = int.Parse((_interval.SelectedItem?.ToString() ?? "1000 ms").Split(' ')[0]);
        config.StartWithWindows = _startWindows.Checked;
        config.StartMinimized = _startMinimized.Checked;
        config.AutoInstallApk = _autoInstallApk.Checked;
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
                    "Para iniciar sozinho e conservar o acesso aos sensores do hardware, o PC Monitor USB criará uma tarefa agendada chamada 'PC Monitor USB', executada somente no seu login e com privilégios elevados.\n\nDeseja continuar?",
                    "Confirmar início com Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (consent != DialogResult.Yes)
                {
                    config.StartWithWindows = false;
                    _startWindows.Checked = false;
                }
            }
            SetStartup(config.StartWithWindows);
            _config.Save(config);
            MessageBox.Show("Configurações salvas. Porta e intervalo entram em vigor após reiniciar o servidor.", "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao salvar configurações.", ex);
            MessageBox.Show("Não foi possível salvar as configurações.\n\n" + ex.Message, "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            startInfo.ArgumentList.Add("/Create");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add("PC Monitor USB");
            startInfo.ArgumentList.Add("/TR");
            startInfo.ArgumentList.Add($"\"{Environment.ProcessPath}\" --minimized");
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
            throw new InvalidOperationException("O Windows não conseguiu criar a tarefa de inicialização.");
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
        _trayConnectionStatus = new ToolStripMenuItem("● Celular desconectado") { Enabled = false, ForeColor = Muted };
        menu.Items.Add(_trayConnectionStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => { _exitRequested = true; Close(); });
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
            ? $"Temperatura da CPU: {temperature:0} °C\nFonte: {_hardware.CpuTemperatureSource}\n\nA leitura está funcionando corretamente."
            : $"A temperatura da CPU ainda não está disponível.\n\nFonte: {_hardware.CpuTemperatureSource}\nAdministrador: {(IsAdministrator() ? "sim" : "não")}\n\nUse 'Ampliar suporte aos sensores'. O programa pedirá confirmação antes de baixar ou instalar o driver PawnIO. Depois, exporte sensors.txt se o valor continuar ausente.";
        MessageBox.Show(text, "Diagnóstico da temperatura da CPU", MessageBoxButtons.OK,
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
    private void AddVisibilitySetting(TableLayoutPanel panel, ref int row, string title, string key, bool value) { var check = NewCheck("Mostrar no painel", value); _visibility[key] = check; AddSetting(panel, ref row, title, check); }

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
