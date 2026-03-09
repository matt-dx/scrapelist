using CommunityToolkit.Maui.Storage;

namespace Scrapelist.Maui.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync()
    {
#if ANDROID
        // On Android 11+, MANAGE_EXTERNAL_STORAGE is required to write to arbitrary
        // external storage paths with System.IO. The manifest declares it; we must
        // request it via Settings because it cannot be requested via Permissions API.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (!Android.OS.Environment.IsExternalStorageManager)
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                    Android.Net.Uri.Parse($"package:{Android.App.Application.Context.PackageName}"));
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
                    "Permission Required",
                    "Please enable 'All files access' for Scrapelist, then pick a folder again.",
                    "OK");
                return null;
            }
        }
        else
        {
            // Android 10 and below: request legacy write permission
            var statusWrite = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
            if (statusWrite != PermissionStatus.Granted)
                statusWrite = await Permissions.RequestAsync<Permissions.StorageWrite>();

            if (statusWrite != PermissionStatus.Granted)
            {
                await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
                    "Permission Denied",
                    "Storage access is required to select a download folder.",
                    "OK");
                return null;
            }
        }
#endif
        string? folderPath = null;
        var result = await FolderPicker.Default.PickAsync();
        if (result.IsSuccessful)
        {
#if ANDROID
            folderPath = ResolveAndroidSafPath(result.Folder.Path);
#else
            folderPath = result.Folder.Path;
#endif
        }
        return folderPath;
    }

#if ANDROID
    /// <summary>
    /// Converts a SAF content URI returned by FolderPicker on Android
    /// (e.g. content://com.android.externalstorage.documents/tree/primary%3ADownloads)
    /// to a real file system path (e.g. /storage/emulated/0/Downloads).
    /// Returns the original string unchanged if it is already a real path.
    /// </summary>
    private static string? ResolveAndroidSafPath(string? uriString)
    {
        if (string.IsNullOrEmpty(uriString)) return null;
        if (!uriString.StartsWith("content://", StringComparison.Ordinal)) return uriString;

        try
        {
            var uri = Android.Net.Uri.Parse(uriString)!;
            var docId = Android.Provider.DocumentsContract.GetTreeDocumentId(uri);
            if (docId == null) return null;

            // docId format: "primary:path/to/folder" or "{storageId}:path/to/folder"
            var colonIdx = docId.IndexOf(':');
            if (colonIdx < 0) return null;

            var storageId = docId[..colonIdx];
            var relativePath = docId[(colonIdx + 1)..];

            var basePath = storageId.Equals("primary", StringComparison.OrdinalIgnoreCase)
                ? Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath
                : $"/storage/{storageId}";

            return string.IsNullOrEmpty(relativePath)
                ? basePath
                : Path.Combine(basePath, relativePath);
        }
        catch
        {
            return null;
        }
    }
#endif
}
