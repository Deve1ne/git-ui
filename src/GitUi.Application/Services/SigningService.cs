using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

/// <summary>Global commit / tag signing settings (git config commit.gpgsign, tag.gpgsign, gpg.format, user.signingkey).</summary>
/// <param name="Format">openpgp (GPG), ssh or x509.</param>
/// <param name="SigningKey">A GPG key id, or for SSH the path to the public key (or the key itself).</param>
public sealed record SigningSettings(bool SignCommits, bool SignTags, string Format, string? SigningKey);

/// <summary>What <c>git log %G?</c> says about a commit's signature.</summary>
/// <param name="Code">G good, U good with unknown validity, X/Y expired signature/key, R revoked key, B bad, E cannot be checked, N none.</param>
public sealed record CommitSignature(char Code, string? Signer, string? Key)
{
    public static readonly CommitSignature None = new('N', null, null);

    public bool IsSigned => Code != 'N';

    public bool IsVerified => Code is 'G' or 'U';

    public string Description => Code switch
    {
        'G' => "Good signature",
        'U' => "Good signature (key of unknown trust)",
        'X' => "Good signature, now expired",
        'Y' => "Good signature by an expired key",
        'R' => "Signed by a revoked key",
        'B' => "Bad signature",
        'E' => "Signed, but the signature cannot be checked here (key or allowed signers missing)",
        _ => "Not signed",
    };
}

/// <summary>Commit signing (GPG or SSH): the global settings, and each commit's signature status.</summary>
public sealed class SigningService
{
    public static readonly IReadOnlyList<string> Formats = new[] { "openpgp", "ssh", "x509" };

    private readonly IGitRepositoryWriter _config;
    private readonly IGitCommandRunner _git;

    public SigningService(IGitRepositoryWriter config, IGitCommandRunner git)
    {
        _config = config;
        _git = git;
    }

    public async Task<SigningSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var signCommits = await _config.GetConfigValueAsync(null, "commit.gpgsign", global: true, cancellationToken);
        var signTags = await _config.GetConfigValueAsync(null, "tag.gpgsign", global: true, cancellationToken);
        var format = await _config.GetConfigValueAsync(null, "gpg.format", global: true, cancellationToken);
        var key = await _config.GetConfigValueAsync(null, "user.signingkey", global: true, cancellationToken);
        return new SigningSettings(IsTrue(signCommits), IsTrue(signTags), format ?? "openpgp", key);
    }

    /// <summary>Writes the settings to the global git config; signing without a key is refused.</summary>
    public async Task SaveAsync(SigningSettings settings, CancellationToken cancellationToken = default)
    {
        if ((settings.SignCommits || settings.SignTags) && string.IsNullOrWhiteSpace(settings.SigningKey))
        {
            throw new InvalidOperationException("Signing needs a key: a GPG key id, or for SSH the path of your public key (e.g. ~/.ssh/id_ed25519.pub).");
        }

        if (!Formats.Contains(settings.Format))
        {
            throw new InvalidOperationException($"Unknown signature format '{settings.Format}'.");
        }

        await _config.SetConfigValueAsync(null, "user.signingkey", settings.SigningKey, global: true, cancellationToken);
        // The default (openpgp) is left implicit, like git's own config.
        await _config.SetConfigValueAsync(null, "gpg.format", settings.Format == "openpgp" ? null : settings.Format, global: true, cancellationToken);
        await _config.SetConfigValueAsync(null, "commit.gpgsign", settings.SignCommits ? "true" : null, global: true, cancellationToken);
        await _config.SetConfigValueAsync(null, "tag.gpgsign", settings.SignTags ? "true" : null, global: true, cancellationToken);
    }

    public async Task<CommitSignature> GetSignatureAsync(string path, string sha, CancellationToken cancellationToken = default)
    {
        var result = await _git.TryRunAsync(path, new[] { "log", "-1", "--format=%G?%x1f%GS%x1f%GK", sha, "--" }, cancellationToken: cancellationToken);
        return result.Success ? Parse(result.Output) : CommitSignature.None;
    }

    public static CommitSignature Parse(string output)
    {
        var parts = output.Trim().Split('\u001f');
        if (parts.Length == 0 || parts[0].Length == 0)
        {
            return CommitSignature.None;
        }

        static string? Value(string[] parts, int index) => parts.Length > index && parts[index].Trim().Length > 0 ? parts[index].Trim() : null;
        return new CommitSignature(parts[0][0], Value(parts, 1), Value(parts, 2));
    }

    private static bool IsTrue(string? value) => value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value is "1" or "yes" or "on");
}
