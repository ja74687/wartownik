using Avalonia;
using Avalonia.Media;
using Wartownik.Connections;

namespace Wartownik.ViewModels;

public enum ConnectionStatus
{
    Unknown,
    Checking,
    Connected,
    Disconnected,
}

public sealed class ConnectionProfileItemViewModel : ViewModelBase
{
    // Each entry is (dark, light) gradient stop pair — same hue, two shades.
    private static readonly (string Dark, string Light)[] AvatarPalette =
    {
        ("#1D4ED8", "#60A5FA"), // blue
        ("#B45309", "#FCD34D"), // amber
        ("#B91C1C", "#F87171"), // red
        ("#6D28D9", "#A78BFA"), // violet
        ("#047857", "#34D399"), // emerald
        ("#0E7490", "#22D3EE"), // cyan
        ("#BE185D", "#F472B6"), // pink
    };

    private ConnectionStatus _status = ConnectionStatus.Unknown;
    private int? _databaseCount;
    private int? _userCount;

    public ConnectionProfile Profile { get; }

    public ConnectionProfileItemViewModel(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    public Guid Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;
    public string Endpoint => $"{Profile.Host}:{Profile.Port} / {Profile.Database} / {Profile.Username}";
    public string Initials => ComputeInitials(DisplayName);

    public IBrush AvatarBrush
    {
        get
        {
            var (dark, light) = AvatarPalette[ColorIndex];
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(dark), 0),
                    new GradientStop(Color.Parse(light), 1),
                },
            };
        }
    }

    private int ColorIndex => Math.Abs(StableHash(DisplayName)) % AvatarPalette.Length;

    public ConnectionStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                RaisePropertyChanged(nameof(IsConnected));
                RaisePropertyChanged(nameof(IsDisconnected));
                RaisePropertyChanged(nameof(IsChecking));
                RaisePropertyChanged(nameof(IsStatusKnown));
            }
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected;
    public bool IsDisconnected => _status == ConnectionStatus.Disconnected;
    public bool IsChecking => _status == ConnectionStatus.Checking;
    public bool IsStatusKnown => _status != ConnectionStatus.Unknown;

    public int? DatabaseCount
    {
        get => _databaseCount;
        set
        {
            if (SetField(ref _databaseCount, value))
            {
                RaisePropertyChanged(nameof(CountersText));
                RaisePropertyChanged(nameof(HasCounters));
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
                RaisePropertyChanged(nameof(CountersText));
                RaisePropertyChanged(nameof(HasCounters));
            }
        }
    }

    public bool HasCounters => _databaseCount.HasValue;

    public string CountersText
    {
        get
        {
            if (!_databaseCount.HasValue) return "";
            var dbWord = _databaseCount.Value == 1 ? "db" : "dbs";
            var users = _userCount ?? 0;
            var userWord = users == 1 ? "user" : "users";
            return $"{_databaseCount} {dbWord} · {users} {userWord}";
        }
    }

    /// <summary>
    /// Relative-time stamp of the last save. Empty when the field is missing (older profile)
    /// or never edited — that way an unsaved profile doesn't show a misleading "just now".
    /// </summary>
    public string LastEditedText
    {
        get
        {
            if (!Profile.LastEditedAt.HasValue)
                return "";
            var delta = DateTimeOffset.UtcNow - Profile.LastEditedAt.Value.ToUniversalTime();
            if (delta.TotalSeconds < 60) return "edited just now";
            if (delta.TotalMinutes < 60) return $"edited {(int)delta.TotalMinutes} min ago";
            if (delta.TotalHours < 24) return $"edited {(int)delta.TotalHours} h ago";
            if (delta.TotalDays < 30) return $"edited {(int)delta.TotalDays} d ago";
            return "edited " + Profile.LastEditedAt.Value.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }

    public bool HasLastEdited => Profile.LastEditedAt.HasValue;

    internal static string ComputeInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(
            new[] { ' ', '_', '-', '.', '/', '\\', '(', ')' },
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToLowerInvariant(parts[1][0]));

        var first = parts.Length == 1 ? parts[0] : name.Trim();
        if (first.Length >= 2)
            return string.Concat(char.ToUpperInvariant(first[0]), char.ToLowerInvariant(first[1]));

        return char.ToUpperInvariant(first[0]).ToString();
    }

    internal static int StableHash(string value)
    {
        int hash = 17;
        foreach (var c in value)
            hash = hash * 31 + c;
        return hash;
    }
}
