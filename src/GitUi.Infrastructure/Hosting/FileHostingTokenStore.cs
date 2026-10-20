using System.Security.Cryptography;
using System.Text;
using GitUi.Application.Hosting;

namespace GitUi.Infrastructure.Hosting;

/// <summary>
/// One file per account under the app's data folder. On Windows the token is encrypted with DPAPI
/// for the current Windows user; on Linux and macOS the file is readable by its owner only (0600),
/// like the GitHub CLI's fallback when no keyring is available.
/// </summary>
public sealed class FileHostingTokenStore : IHostingTokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GitUi.HostingToken.v1");
    private readonly string _directory;

    public FileHostingTokenStore(string directory) => _directory = directory;

    public async Task SaveAsync(string accountId, string token, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(accountId);
        var bytes = Encoding.UTF8.GetBytes(token);
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllBytesAsync(path, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser), cancellationToken);
            return;
        }

        File.Delete(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    public async Task<string?> GetAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(accountId);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // Encrypted by another Windows user or machine: as good as missing.
            return null;
        }
    }

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(accountId));
        return Task.CompletedTask;
    }

    private string PathFor(string accountId)
    {
        // Ids are generated GUIDs; refuse anything that could step out of the folder.
        if (accountId.Length == 0 || accountId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            throw new ArgumentException($"Invalid account id '{accountId}'.", nameof(accountId));
        }

        return Path.Combine(_directory, accountId + ".token");
    }
}
