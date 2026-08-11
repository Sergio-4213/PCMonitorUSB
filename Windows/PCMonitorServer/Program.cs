using PCMonitorUSB.ADB;
using PCMonitorUSB.Commands;
using PCMonitorUSB.Config;
using PCMonitorUSB.Core;
using PCMonitorUSB.Server;
using PCMonitorUSB.UI;
using System.Diagnostics;

namespace PCMonitorUSB;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        WaitForPreviousInstance(args);
        if (IsLegacyInstanceRunning())
        {
            MessageBox.Show(
                "Feche a versão antiga do monitor que ainda está aberta na bandeja e tente novamente. Nenhuma reinicialização do computador é necessária.",
                "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dataRoot = MigrateLegacyData();
        Directory.CreateDirectory(dataRoot);
        SimpleLog.Initialize(Path.Combine(dataRoot, "logs", "app.log"));

        using var singleInstance = new Mutex(true, "Local\\PCMonitorUSBServer-SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("PC Monitor USB já está em execução na bandeja.", "PC Monitor USB",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = new ConfigStore(Path.Combine(dataRoot, "config.json"));
        if (config.Current.StartWithWindows)
        {
            try { MainForm.SetStartup(true); }
            catch (Exception ex) { SimpleLog.Warn("Não foi possível migrar a inicialização automática: " + ex.Message); }
        }
        using var hardware = new HardwareMonitor(config.Current.UpdateIntervalMs);
        hardware.Start();
        var commands = new CommandService(config);
        var server = new LocalServer(hardware, config, commands);
        try
        {
            server.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Não foi possível iniciar o servidor local.", ex);
            MessageBox.Show($"Não foi possível abrir a porta local {config.Current.Port}.\n\n{ex.Message}",
                "PC Monitor USB", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        var adb = new AdbManager(config, server, AdbProvisioner.FindAdbPath());
        adb.Start();
        try
        {
            using var form = new MainForm(config, hardware, server, adb, args.Contains("--minimized", StringComparer.OrdinalIgnoreCase));
            Application.Run(form);
        }
        finally
        {
            adb.DisposeAsync().AsTask().GetAwaiter().GetResult();
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SimpleLog.Info("PC Monitor USB encerrado.");
        }
    }

    private static void WaitForPreviousInstance(string[] args)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, "--wait-for-pid", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var processId)) return;
        try
        {
            using var previous = Process.GetProcessById(processId);
            previous.WaitForExit(10_000);
        }
        catch
        {
            // The previous process already exited.
        }
    }

    private static bool IsLegacyInstanceRunning()
    {
        try
        {
            using var legacy = Mutex.OpenExisting("Local\\J4PCMonitorServer-SingleInstance");
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static string MigrateLegacyData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(localAppData, "PCMonitorUSB");
        var legacy = Path.Combine(localAppData, "J4PCMonitor");
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return current;

        try
        {
            Directory.Move(legacy, current);
        }
        catch
        {
            Directory.CreateDirectory(current);
            foreach (var source in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(legacy, source);
                var destination = Path.Combine(current, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Copy(source, destination);
            }
        }
        return current;
    }
}
