namespace Scrapelist.Maui.Services;

public static class FolderPickerService
{
    public static async Task<string?> PickFolderAsync()
    {
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
        // On Android, use the Downloads directory as a sensible default.
        // SAF content URIs are not directly usable as file paths for our download pipeline.
        var downloads = Android.OS.Environment.GetExternalStoragePublicDirectory(
            Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
        return await Task.FromResult(downloads);
#else
        return await Task.FromResult<string?>(null);
#endif
    }
}
