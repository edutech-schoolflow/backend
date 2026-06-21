using EduTech.Shared.Caching;
using EduTech.Shared.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EduTech.Auth.Tests.Features;

/// <summary>
/// Resolution precedence (per-school override → global) and the read-through cache — the bits that
/// decide whether a feature is live for a given school.
/// </summary>
public class FeatureFlagServiceTests
{
    private readonly Mock<IFeatureFlagRepository> _repo = new();
    private readonly Mock<ICacheProvider> _cache = new();

    private FeatureFlagService CreateService()
    {
        return new FeatureFlagService(_repo.Object, _cache.Object, NullLogger<FeatureFlagService>.Instance);
    }

    [Fact]
    public async Task SchoolOverride_On_BeatsGlobalOff()
    {
        Guid school = Guid.NewGuid();
        _repo.Setup(r => r.GetSchoolOverrideAsync(school, "fees", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetGlobalEnabledAsync("fees", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        bool enabled = await CreateService().IsEnabledAsync("fees", school);

        Assert.True(enabled);
        _repo.Verify(r => r.GetGlobalEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SchoolOverride_Off_BeatsGlobalOn()
    {
        Guid school = Guid.NewGuid();
        _repo.Setup(r => r.GetSchoolOverrideAsync(school, "fees", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.GetGlobalEnabledAsync("fees", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.False(await CreateService().IsEnabledAsync("fees", school));
    }

    [Fact]
    public async Task NoSchoolOverride_FallsBackToGlobal()
    {
        Guid school = Guid.NewGuid();
        _repo.Setup(r => r.GetSchoolOverrideAsync(school, "fees", It.IsAny<CancellationToken>())).ReturnsAsync((bool?)null);
        _repo.Setup(r => r.GetGlobalEnabledAsync("fees", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await CreateService().IsEnabledAsync("fees", school));
    }

    [Fact]
    public async Task UnknownFlag_DefaultsOff()
    {
        _repo.Setup(r => r.GetGlobalEnabledAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((bool?)null);

        Assert.False(await CreateService().IsEnabledAsync("nope"));
    }

    [Fact]
    public async Task CacheHit_AvoidsRepository()
    {
        _cache.Setup(c => c.GetAsync("ff:g:fees")).ReturnsAsync("1");

        Assert.True(await CreateService().IsEnabledAsync("fees"));
        _repo.Verify(r => r.GetGlobalEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GlobalResolution_PopulatesCache()
    {
        _repo.Setup(r => r.GetGlobalEnabledAsync("fees", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await CreateService().IsEnabledAsync("fees");

        _cache.Verify(c => c.SetAsync("ff:g:fees", "1", It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task RepositoryFailure_FailsClosed()
    {
        _repo.Setup(r => r.GetGlobalEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        Assert.False(await CreateService().IsEnabledAsync("fees"));
    }

    [Fact]
    public async Task InvalidateGlobal_RemovesCacheKey()
    {
        await CreateService().InvalidateGlobalAsync("fees");
        _cache.Verify(c => c.RemoveAsync("ff:g:fees"), Times.Once);
    }

    [Fact]
    public async Task InvalidateSchool_RemovesCacheKey()
    {
        Guid school = Guid.NewGuid();
        await CreateService().InvalidateSchoolAsync(school, "fees");
        _cache.Verify(c => c.RemoveAsync($"ff:s:{school}:fees"), Times.Once);
    }
}
