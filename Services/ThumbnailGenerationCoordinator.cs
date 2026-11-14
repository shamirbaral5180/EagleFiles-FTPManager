using FTP_WebApp.Models;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace FTP_WebApp.Services;

public class ThumbnailGenerationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _itemSlots = new();

    public ThumbnailGenerationCoordinator(IOptions<MediaOptions> options)
    {
        Slots = new SemaphoreSlim(Math.Clamp(options.Value.MaxConcurrentGenerators, 1, 4));
    }

    public SemaphoreSlim Slots { get; }

    public SemaphoreSlim GetItemSlot(string cacheKey)
    {
        return _itemSlots.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
    }
}
