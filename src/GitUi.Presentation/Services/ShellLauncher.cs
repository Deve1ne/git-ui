using System.Diagnostics;

namespace GitUi.Presentation.Services;

/// <summary>Hands files to Windows: open with the default application, or show them in Explorer.</summary>
public static class ShellLauncher
{
    public static void OpenFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"'{fullPath}' does not exist in the working directory.");
        }

        Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    /// <summary>Opens a web page in the default browser (Windows, macOS and Linux).</summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException($"'{url}' is not a web address.");
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        else
        {
            var opener = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            opener.ArgumentList.Add(uri.AbsoluteUri);
            Process.Start(opener);
        }
    }

    /// <summary>Opens Explorer on the file's folder with the file selected (or the closest existing folder).</summary>
    public static void RevealInFolder(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
            return;
        }

        var folder = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        while (folder is not null && !Directory.Exists(folder))
        {
            folder = Path.GetDirectoryName(folder);
        }

        if (folder is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }
}
