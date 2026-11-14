using System.Collections.Concurrent;
using FTP_WebApp.Models;
using Microsoft.Extensions.Options;

namespace FTP_WebApp.Services;

public class ThumbnailGenerationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _itemSlots = new();

    public ThumbnailGenerationCoordinator(IOptions<MediaOptions> options, ILogger<ThumbnailGenerationCoordinator> logger)
    {
        var configuredLimit = Math.Clamp(options.Value.MaxConcurrentGenerators, 1, 16);
        Capacity = Math.Min(configuredLimit, Math.Max(1, Environment.ProcessorCount));
        Slots = new SemaphoreSlim(Capacity, Capacity);
        logger.LogInformation("Thumbnail generation concurrency is {Capacity}. Configured limit: {ConfiguredLimit}; available processors: {ProcessorCount}.",
            Capacity, configuredLimit, Environment.ProcessorCount);
    }

    public SemaphoreSlim Slots { get; }

    public int Capacity { get; }

    public SemaphoreSlim GetItemSlot(string cacheKey)
    {
        return _itemSlots.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
    }
}
