using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Wartownik.Localization;

public sealed class ResourceManagerStringResources : IStringResources
{
    private readonly ResourceManager _resourceManager;

    public ResourceManagerStringResources(ResourceManager resourceManager)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);
        _resourceManager = resourceManager;
    }

    public ResourceManagerStringResources(string baseName, Assembly assembly)
        : this(new ResourceManager(baseName, assembly))
    {
    }

    public string? Get(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(culture);
        return _resourceManager.GetString(key, culture);
    }

    public static ResourceManagerStringResources ForApplicationStrings()
    {
        var assembly = typeof(ResourceManagerStringResources).Assembly;
        return new ResourceManagerStringResources("Wartownik.Resources.Strings", assembly);
    }
}
