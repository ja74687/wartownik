using System.Globalization;
using Wartownik.Connections;
using Wartownik.Localization;

namespace Wartownik.ViewModels;

public sealed class DatabaseItemViewModel : ViewModelBase
{
    private readonly ILocalizationService? _localization;

    private int? _schemaCount;
    private int? _userCount;
    private int? _grantCount;
    private int? _pendingCount;
    private DateTimeOffset? _lastSyncAt;

    public DatabaseSummary Summary { get; }

    public DatabaseItemViewModel(DatabaseSummary summary, ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
        _localization = localization;
        if (_localization is not null)
            _localization.PropertyChanged += (_, _) => RaiseLocalizedProperties();
    }

    public string Name => Summary.Name;
    public string? Owner => Summary.Owner;
    public string? ServerVersion => Summary.ServerVersion;

    public string SizeText => FormatSize(Summary.SizeBytes);

    /// <summary>
    /// Combines owner / server version / size into a single subtitle line.
    /// Skips parts that are null or unknown so the card never shows "Owner: ".
    /// </summary>
    public string MetadataLine
    {
        get
        {
            var parts = new List<string>(3);

            if (!string.IsNullOrEmpty(Owner))
                parts.Add($"{LocalizedOr("Databases.OwnerPrefix", "Owner")}: {Owner}");

            if (!string.IsNullOrEmpty(ServerVersion))
                parts.Add($"PostgreSQL {ServerVersion}");

            if (Summary.SizeBytes.HasValue)
                parts.Add($"{LocalizedOr("Databases.SizePrefix", "Size")}: {SizeText}");

            return string.Join(" · ", parts);
        }
    }

    public int? SchemaCount
    {
        get => _schemaCount;
        set
        {
            if (SetField(ref _schemaCount, value))
            {
                RaisePropertyChanged(nameof(SchemaCountText));
                RaisePropertyChanged(nameof(HasSchemaCount));
            }
        }
    }

    public int? UserCount
    {
        get => _userCount;
        set
        {
            if (SetField(ref _userCount, value))
            {
                RaisePropertyChanged(nameof(UserCountText));
                RaisePropertyChanged(nameof(HasUserCount));
            }
        }
    }

    public int? GrantCount
    {
        get => _grantCount;
        set
        {
            if (SetField(ref _grantCount, value))
            {
                RaisePropertyChanged(nameof(GrantCountText));
                RaisePropertyChanged(nameof(HasGrantCount));
            }
        }
    }

    public int? PendingCount
    {
        get => _pendingCount;
        set
        {
            if (SetField(ref _pendingCount, value))
            {
                RaisePropertyChanged(nameof(PendingCountText));
                RaisePropertyChanged(nameof(HasPendingCount));
            }
        }
    }

    public DateTimeOffset? LastSyncAt
    {
        get => _lastSyncAt;
        set
        {
            if (SetField(ref _lastSyncAt, value))
            {
                RaisePropertyChanged(nameof(LastSyncText));
                RaisePropertyChanged(nameof(HasLastSync));
            }
        }
    }

    public bool HasSchemaCount => _schemaCount.HasValue;
    public bool HasUserCount => _userCount.HasValue;
    public bool HasGrantCount => _grantCount.HasValue;
    public bool HasPendingCount => _pendingCount.HasValue && _pendingCount.Value > 0;
    public bool HasLastSync => _lastSyncAt.HasValue;

    public string SchemaCountText =>
        FormatCount(_schemaCount, "Databases.SchemasOne", "Databases.SchemasMany", "schema", "schemas");
    public string UserCountText =>
        FormatCount(_userCount, "Databases.UsersOne", "Databases.UsersMany", "user", "users");
    public string GrantCountText =>
        FormatCount(_grantCount, "Databases.GrantsOne", "Databases.GrantsMany", "grant", "grants");
    public string PendingCountText =>
        _pendingCount.HasValue
            ? string.Format(CultureInfo.CurrentCulture, LocalizedOr("Databases.PendingMany", "{0} pending"), _pendingCount.Value)
            : "";

    public string LastSyncText =>
        _lastSyncAt.HasValue
            ? string.Format(
                CultureInfo.CurrentCulture,
                LocalizedOr("Databases.LastSync", "last sync · {0}"),
                FormatRelative(_lastSyncAt.Value))
            : LocalizedOr("Databases.NeverSynced", "never synced");

    private string FormatCount(int? value, string oneKey, string manyKey, string oneFallback, string manyFallback)
    {
        if (!value.HasValue)
            return "";
        var template = value.Value == 1
            ? LocalizedOr(oneKey, "{0} " + oneFallback)
            : LocalizedOr(manyKey, "{0} " + manyFallback);
        return string.Format(CultureInfo.CurrentCulture, template, value.Value);
    }

    private string LocalizedOr(string key, string fallback)
    {
        if (_localization is null)
            return fallback;
        var value = _localization[key];
        return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }

    private void RaiseLocalizedProperties()
    {
        RaisePropertyChanged(nameof(MetadataLine));
        RaisePropertyChanged(nameof(SchemaCountText));
        RaisePropertyChanged(nameof(UserCountText));
        RaisePropertyChanged(nameof(GrantCountText));
        RaisePropertyChanged(nameof(PendingCountText));
        RaisePropertyChanged(nameof(LastSyncText));
    }

    private static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue)
            return "";
        var b = bytes.Value;
        if (b < 1024) return $"{b} B";
        double kb = b / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        double gb = mb / 1024.0;
        if (gb < 1024) return $"{gb:0.##} GB";
        double tb = gb / 1024.0;
        return $"{tb:0.##} TB";
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} h ago";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} d ago";
        return when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);
    }
}
