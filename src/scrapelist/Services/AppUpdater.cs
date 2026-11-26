using System.Diagnostics;
using System.IO.Compression;
using Scrapelist.Services;

namespace Scrapelist.Maui.Services;

/// <summary>
/// Checks GitHub releases for a newer build and installs it. Singleton; UI observes <see cref="Changed"/>.
/// Windows: extracts the release zip, then a helper script swaps files after this process exits and relaunches.
/// Android: downloads the signed APK and hands it to the system package installer.
/// </summary>
public sealed class AppUpdater
{
    private readonly UpdateChecker _checker = new();

    public UpdateInfo? Available { get; private set; }
    public bool Busy { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public static string AssetName =>
#if ANDROID
        UpdateChecker.AndroidAsset;
#else
        UpdateChecker.WindowsAsset;
#endif

    public async Task CheckAsync()
    {
        if (Busy) return;
        try
        {
            Error = null;
            if (!Version.TryParse(AppInfo.Current.VersionString, out var current)) return;
            Available = await _checker.CheckAsync(current, AssetName);
        }
        catch (Exception ex)
        {
            // Offline or rate-limited: never bother the user on a background check.
            Debug.WriteLine($"Update check failed: {ex.Message}");
        }
        Changed?.Invoke();
    }

    public async Task InstallAsync()
    {
        if (Available is not { } info || Busy) return;
        Busy = true; Progress = 0; Error = null; Changed?.Invoke();
        try
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "update");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, info.AssetName);
            var progress = new Progress<double>(p => { Progress = p; Changed?.Invoke(); });
            await _checker.DownloadAsync(info.DownloadUrl, file, progress);

#if ANDROID
            InstallAndroid(file);
#elif WINDOWS
            InstallWindows(file, dir);
#endif
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false; Changed?.Invoke();
        }
    }

#if WINDOWS
    private static void InstallWindows(string zipPath, string workDir)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate running executable.");
        var appDir = Path.GetDirectoryName(exe)!;

        var staging = Path.Combine(workDir, "staged");
        ZipFile.ExtractToDirectory(zipPath, staging);
        if (!File.Exists(Path.Combine(staging, Path.GetFileName(exe))))
            throw new InvalidOperationException("Downloaded update does not contain the application executable.");

        // Fail early if we can't write to the install folder (e.g. Program Files).
        var probe = Path.Combine(appDir, ".update-probe");
        try { File.WriteAllText(probe, ""); File.Delete(probe); }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"No write access to {appDir}. Install Scrapelist in a user-writable folder to enable updates.");
        }

        var pid = Environment.ProcessId;
        var script = Path.Combine(workDir, "apply-update.cmd");
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >NUL",
            "  goto wait",
            ")",
            $"robocopy \"{staging}\" \"{appDir}\" /E /IS /IT /R:10 /W:1 >NUL",
            $"start \"\" \"{exe}\"",
            ""));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
        Application.Current?.Quit();
    }
#endif

#if ANDROID
    private static void InstallAndroid(string apkPath)
    {
        var ctx = Android.App.Application.Context;

        // "Install unknown apps" is a per-app toggle the user must enable once.
        if (!ctx.PackageManager!.CanRequestPackageInstalls())
        {
            var settings = new Android.Content.Intent(
                Android.Provider.Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse($"package:{ctx.PackageName}"));
            settings.AddFlags(Android.Content.ActivityFlags.NewTask);
            ctx.StartActivity(settings);
            throw new InvalidOperationException("Allow Scrapelist to install apps in the settings screen that just opened, then tap Update again.");
        }

        // MAUI Essentials registers a FileProvider ("<applicationId>.fileProvider") that covers the cache dir.
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, $"{ctx.PackageName}.fileProvider", new Java.IO.File(apkPath))!;
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.GrantReadUriPermission);
        ctx.StartActivity(intent);
    }
#endif
}
