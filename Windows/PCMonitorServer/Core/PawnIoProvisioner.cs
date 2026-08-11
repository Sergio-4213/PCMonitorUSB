using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PCMonitorUSB.Core;

public sealed record PawnIoInstallResult(bool Started, bool RebootRequired, string? Error = null);

public static class PawnIoProvisioner
{
    public const string Version = "2.2.0";
    public const string OfficialReleasePage = "https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0";
    private const string DownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    private const string ExpectedSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    public static async Task<PawnIoInstallResult> DownloadAndRunAsync(IProgress<int>? progress = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCMonitorUSB", "downloads");
        Directory.CreateDirectory(root);
        var installer = Path.Combine(root, $"PawnIO-{Version}-setup.exe");
        var temporary = installer + ".download";

        try
        {
            if (!File.Exists(installer) || !HasExpectedHash(installer))
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = true })
                {
                    Timeout = TimeSpan.FromMinutes(3)
                };
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PCMonitorUSB", "1.1"));
                using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous);
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    readTotal += read;
                    if (total is > 0) progress?.Report((int)Math.Clamp(readTotal * 100 / total.Value, 0, 100));
                }
                await output.FlushAsync().ConfigureAwait(false);
                if (!HasExpectedHash(temporary))
                    throw new InvalidDataException("A assinatura SHA-256 do instalador PawnIO não corresponde à publicação oficial.");
                File.Move(temporary, installer, true);
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                UseShellExecute = true,
                Verb = "runas"
            });
            if (process is null) return new PawnIoInstallResult(false, false, "O instalador não pôde ser aberto.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            var reboot = process.ExitCode == 3010;
            return process.ExitCode is 0 or 3010
                ? new PawnIoInstallResult(true, reboot)
                : new PawnIoInstallResult(false, false, $"O instalador terminou com o código {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            SimpleLog.Error("Falha ao preparar o suporte PawnIO.", ex);
            return new PawnIoInstallResult(false, false, ex.Message);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static bool HasExpectedHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
