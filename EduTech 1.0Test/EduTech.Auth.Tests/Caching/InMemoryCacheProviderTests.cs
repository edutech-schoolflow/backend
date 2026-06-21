using EduTech.Shared.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace EduTech.Auth.Tests.Caching;

/// <summary>The no-Redis fallback cache: set/get/remove + TTL expiry behave as expected.</summary>
public class InMemoryCacheProviderTests
{
    private static InMemoryCacheProvider CreateCache()
    {
        return new InMemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task Set_Then_Get_ReturnsValue()
    {
        InMemoryCacheProvider cache = CreateCache();
        await cache.SetAsync("k", "v");
        Assert.Equal("v", await cache.GetAsync("k"));
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        InMemoryCacheProvider cache = CreateCache();
        Assert.Null(await cache.GetAsync("absent"));
    }

    [Fact]
    public async Task Remove_DeletesValue()
    {
        InMemoryCacheProvider cache = CreateCache();
        await cache.SetAsync("k", "v");
        await cache.RemoveAsync("k");
        Assert.Null(await cache.GetAsync("k"));
    }

    [Fact]
    public async Task Set_WithTtl_ExpiresAfterWindow()
    {
        InMemoryCacheProvider cache = CreateCache();
        await cache.SetAsync("k", "v", TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);
        Assert.Null(await cache.GetAsync("k"));
    }
}
