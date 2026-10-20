using GitUi.Application.Abstractions;
using GitUi.Application.DTOs;
using GitUi.Application.Services;
using NSubstitute;
using Xunit;

namespace GitUi.Application.Tests;

public class ProfileAndSigningTests
{
    private readonly IGitRepositoryWriter _config = Substitute.For<IGitRepositoryWriter>();
    private readonly MemorySettings _settings = new();

    [Fact]
    public async Task Activating_WritesTheIdentityAndSigningToTheGlobalConfig()
    {
        var service = new ProfileService(_settings, _config);
        await service.SaveAsync(new GitProfile("w", "Work", "Amy Doe", "amy@corp.com", "ABCD1234", SignCommits: true));

        await service.ActivateAsync("w");

        await _config.Received(1).SetConfigValueAsync(null, "user.name", "Amy Doe", true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "user.email", "amy@corp.com", true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "user.signingkey", "ABCD1234", true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "commit.gpgsign", "true", true, Arg.Any<CancellationToken>());
        Assert.Equal("w", (await service.GetActiveAsync())?.Id);
    }

    [Fact]
    public async Task AProfileWithoutKey_StopsSigningWithThePreviousKey()
    {
        var service = new ProfileService(_settings, _config);
        await service.SaveAsync(new GitProfile("p", "Personal", "Amy", "amy@home.org"));

        await service.ActivateAsync("p");

        await _config.Received(1).SetConfigValueAsync(null, "user.signingkey", null, true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "commit.gpgsign", null, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditingTheActiveProfile_AppliesItAgain_AndDeletingItClearsTheActiveOne()
    {
        var service = new ProfileService(_settings, _config);
        await service.SaveAsync(new GitProfile("w", "Work", "Amy", "amy@corp.com"));
        await service.ActivateAsync("w");
        _config.ClearReceivedCalls();

        await service.SaveAsync(new GitProfile("w", "Work", "Amy D.", "amy@corp.com"));
        await _config.Received(1).SetConfigValueAsync(null, "user.name", "Amy D.", true, Arg.Any<CancellationToken>());

        await service.DeleteAsync("w");
        Assert.Null(await service.GetActiveAsync());
        Assert.Empty(await service.GetProfilesAsync());
    }

    [Fact]
    public async Task IncompleteProfiles_AreRefused()
    {
        var service = new ProfileService(_settings, _config);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new GitProfile("x", "X", "", "a@b.c")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new GitProfile("x", "X", "A", "a@b.c", null, SignCommits: true)));
    }

    [Fact]
    public async Task Signing_SavesSshSettings_AndRefusesSigningWithoutKey()
    {
        var service = new SigningService(_config, Substitute.For<IGitCommandRunner>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new SigningSettings(true, false, "ssh", " ")));
        await service.SaveAsync(new SigningSettings(true, false, "ssh", "~/.ssh/id_ed25519.pub"));

        await _config.Received(1).SetConfigValueAsync(null, "gpg.format", "ssh", true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "commit.gpgsign", "true", true, Arg.Any<CancellationToken>());
        await _config.Received(1).SetConfigValueAsync(null, "tag.gpgsign", null, true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("G\u001famy@corp.com\u001fABCD", 'G', true, "amy@corp.com")]
    [InlineData("E\u001f\u001fABCD", 'E', false, null)]
    [InlineData("N\u001f\u001f", 'N', false, null)]
    public void Signature_Parsing(string output, char code, bool verified, string? signer)
    {
        var signature = SigningService.Parse(output);
        Assert.Equal(code, signature.Code);
        Assert.Equal(verified, signature.IsVerified);
        Assert.Equal(signer, signature.Signer);
    }

    [Fact]
    public void Reflog_Parsing_UsesTheMoveTimeAndIndex()
    {
        var entries = ReflogService.Parse("aaa\u001fHEAD@{2026-10-13T09:00:00+02:00}\u001freset: moving to HEAD~1\nbbb\u001fHEAD@{2026-10-12T18:30:00+02:00}\u001fcommit: Add login\n");

        Assert.Equal("HEAD@{1}", entries[1].Selector);
        Assert.Equal("reset", entries[0].Action);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 18, 30, 0, TimeSpan.FromHours(2)), entries[1].Date);
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private AppSettings _current = AppSettings.CreateDefault();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _current = settings;
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
        {
            _current = update(_current);
            return Task.FromResult(_current);
        }
    }
}
