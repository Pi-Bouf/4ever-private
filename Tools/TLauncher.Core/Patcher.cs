using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;

namespace TLauncher;

public enum LaunchStatus { Started, StartedElevated, ElevationDeclined, NotFound, Failed }

/// <summary>Result of a client-launch attempt. <see cref="Process"/> is null unless a process started.</summary>
public sealed record LaunchResult(LaunchStatus Status, int Pid, string Message, Process? Process);

/// <summary>
/// The download + launch half of the launcher (the part after the <see cref="PatchClient"/> handshake).
/// Shared by the console (<c>TLauncher.Net</c>) and the WPF (<c>TLauncher.Wpf</c>) front-ends.
/// </summary>
public static class Patcher
{
    /// <summary>
    /// Downloads every file in the manifest under <paramref name="gameDir"/> and extracts any <c>.zip</c>
    /// into it. URL = <c>FtpUrl / [path/] name</c> (matching the C++ <c>Download</c>). Best-effort per file:
    /// a failure is reported through <paramref name="log"/> and does not abort the rest.
    /// </summary>
    /// <summary>Per-step progress: the file being fetched and how far through the list we are.</summary>
    public readonly record struct DownloadStep(string Name, int Done, int Count, long BytesReceived, long? BytesTotal);

    public static async Task DownloadAsync(PatchManifest m, string gameDir, IProgress<string>? log,
        IProgress<DownloadStep>? step, CancellationToken ct)
    {
        if (m.Files.Count == 0) { log?.Report("No files to patch."); return; }

        using var http = new HttpClient();
        int done = 0;
        foreach (var f in m.Files)
        {
            string baseUrl = m.FtpUrl.TrimEnd('/');
            string rel = (string.IsNullOrEmpty(f.Path) ? "" : f.Path.Trim('/') + "/") + f.Name;
            string url = $"{baseUrl}/{rel}";
            string dest = Path.Combine(gameDir, f.Name);
            done++;
            log?.Report($"[{done}/{m.Files.Count}] {f.Name} ← {url}");
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);

                await using (var srcStream = await resp.Content.ReadAsStreamAsync(ct))
                await using (var fs = File.Create(dest))
                {
                    var buf = new byte[81920];
                    long recv = 0;
                    int n;
                    while ((n = await srcStream.ReadAsync(buf, ct)) > 0)
                    {
                        await fs.WriteAsync(buf.AsMemory(0, n), ct);
                        recv += n;
                        step?.Report(new DownloadStep(f.Name, done, m.Files.Count, recv, total));
                    }
                }

                if (f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(dest, gameDir, overwriteFiles: true);
                    File.Delete(dest);
                    log?.Report($"      extracted {f.Name}");
                }
            }
            catch (Exception ex) { log?.Report($"      FAILED: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Launches the client as <c>exe &lt;loginIp&gt; &lt;loginPort&gt;</c>. <c>TClient.exe</c>'s manifest is
    /// <c>requireAdministrator</c>, so a plain (non-elevated) start fails with ERROR_ELEVATION_REQUIRED (740);
    /// this retries elevated via <c>ShellExecute</c>/<c>runas</c> (a UAC prompt). Running the launcher itself
    /// elevated skips the prompt (the child inherits the token, as the C++ launcher does).
    /// </summary>
    public static LaunchResult Launch(string exePath, string gameDir, string ip, int port)
    {
        if (!File.Exists(exePath))
            return new LaunchResult(LaunchStatus.NotFound, 0, $"Client not found: {exePath}", null);

        var psi = new ProcessStartInfo(exePath) { WorkingDirectory = gameDir, UseShellExecute = false };
        psi.ArgumentList.Add(ip);
        psi.ArgumentList.Add(port.ToString());
        try
        {
            var p = Process.Start(psi)!;
            return new LaunchResult(LaunchStatus.Started, p.Id, $"Started {Path.GetFileName(exePath)} (PID {p.Id}).", p);
        }
        catch (Win32Exception w) when (OperatingSystem.IsWindows() && w.NativeErrorCode == 740) // ERROR_ELEVATION_REQUIRED (Windows only)
        {
            var elevated = new ProcessStartInfo(exePath) { WorkingDirectory = gameDir, UseShellExecute = true, Verb = "runas" };
            elevated.ArgumentList.Add(ip);
            elevated.ArgumentList.Add(port.ToString());
            try
            {
                var p = Process.Start(elevated)!;
                return new LaunchResult(LaunchStatus.StartedElevated, p.Id, $"Started {Path.GetFileName(exePath)} elevated (PID {p.Id}).", p);
            }
            catch (Win32Exception c) when (c.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                return new LaunchResult(LaunchStatus.ElevationDeclined, 0, "The UAC elevation prompt was declined.", null);
            }
        }
        catch (Exception ex)
        {
            return new LaunchResult(LaunchStatus.Failed, 0, ex.Message, null);
        }
    }
}
