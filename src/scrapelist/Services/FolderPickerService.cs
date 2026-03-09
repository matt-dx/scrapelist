using CommunityToolkit.Maui.Storage;

namespace Scrapelist.Maui.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync()
    {
#if ANDROID
        // Request storage permissions
        var statusRead = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        var statusWrite = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();

        if (statusRead != PermissionStatus.Granted || statusWrite != PermissionStatus.Granted)
        {
            statusRead = await Permissions.RequestAsync<Permissions.StorageRead>();
            statusWrite = await Permissions.RequestAsync<Permissions.StorageWrite>();

            if (statusRead != PermissionStatus.Granted || statusWrite != PermissionStatus.Granted)
            {
                // Permission denied - show error or return null
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
            folderPath = result.Folder.Path;
        }
        return folderPath;
/*
#if WINDOWS
        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
        folderPicker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows[0];
        var nativeWindow = window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is not null)
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, windowHandle);
        }

        var folder = await folderPicker.PickSingleFolderAsync();
        return folder?.Path;
#elif ANDROID

        // Request storage permissions
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.StorageRead>();
        }

        if (status == PermissionStatus.Granted)
        {
            // On Android, use the Downloads directory as a sensible default.
            // SAF content URIs are not directly usable as file paths for our download pipeline.
            var downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
            return await Task.FromResult(downloads);
        }
        
        // Permission denied - show error or return null
        await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Permission Denied", 
            "Storage access is required to select a download folder.", 
            "OK");
        return null;
#else
        return await Task.FromResult<string?>(null);
#endif
*/
    }
}
