using System.Text.Json.Serialization;
using System.Windows.Media;

namespace GenshinWishTracker;

// ---------------------------------------------------------------------------
// API / storage models
// ---------------------------------------------------------------------------

/// <summary>One wish (pull) exactly as returned by the official getGachaLog API.</summary>
public sealed class WishItem
{
    [JsonPropertyName("uid")]        public string Uid { get; set; } = "";
    [JsonPropertyName("gacha_type")] public string GachaType { get; set; } = "";
    [JsonPropertyName("item_id")]    public string ItemId { get; set; } = "";
    [JsonPropertyName("count")]      public string Count { get; set; } = "1";
    [JsonPropertyName("time")]       public string Time { get; set; } = "";
    [JsonPropertyName("name")]       public string Name { get; set; } = "";
    [JsonPropertyName("lang")]       public string Lang { get; set; } = "";
    [JsonPropertyName("item_type")]  public string ItemType { get; set; } = "";
    [JsonPropertyName("rank_type")]  public string RankType { get; set; } = "";
    [JsonPropertyName("id")]         public string Id { get; set; } = "";

    /// <summary>Unique key used for de-duplication (wish ids are unique per account).</summary>
    [JsonIgnore] public string Key => $"{Uid}:{Id}";
}

public sealed class GachaApiResponse
{
    [JsonPropertyName("retcode")] public int Retcode { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")]    public GachaApiData? Data { get; set; }
}

public sealed class GachaApiData
{
    [JsonPropertyName("list")] public List<WishItem>? List { get; set; }
}

/// <summary>Contents of wish_history.json.</summary>
public sealed class WishStore
{
    public string? LastUid { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public List<WishItem> Items { get; set; } = new();
}

/// <summary>Contents of settings.json (only the optional custom game folder).</summary>
public sealed class AppSettings
{
    public string? GameFolder { get; set; }
}

// ---------------------------------------------------------------------------
// Banner definitions + dashboard view-model
// ---------------------------------------------------------------------------

/// <param name="FetchType">gacha_type sent to the API. NOTE: "301" also returns the
/// second character banner (gacha_type 400) - both share one pity counter.</param>
public sealed record BannerDef(
    string Key, string Title, string Subtitle,
    string FetchType, int HardPity, int SoftPity, Color Accent);

/// <summary>Everything one dashboard card needs. Bound from XAML.</summary>
public sealed class BannerStats
{
    private static readonly Brush SoftBrush = Make(0xFF, 0xB4, 0x54);
    private static readonly Brush NormalBrush = Make(0xF2, 0xF3, 0xF8);

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public Brush Accent { get; init; } = Brushes.White;

    public int Pity { get; init; }            // pulls since last 5★
    public int HardPity { get; init; }
    public int SoftPity { get; init; }
    public int FourStarPity { get; init; }    // pulls since last 4★ (guaranteed at 10)
    public int TotalPulls { get; init; }
    public int FiveStarCount { get; init; }
    public string LastFiveStar { get; init; } = "—";

    private bool InSoftPity => Pity + 1 >= SoftPity;   // the NEXT pull is at/after soft pity

    public Brush PityBrush => InSoftPity ? SoftBrush : NormalBrush;

    public string StateText =>
        Pity + 1 >= HardPity ? "Next pull is a guaranteed 5★" :
        InSoftPity ? "Soft pity — 5★ chance is rising" : "";

    private static Brush Make(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}

// ---------------------------------------------------------------------------
// Exceptions (so the UI can show friendly, specific messages)
// ---------------------------------------------------------------------------

public sealed class AuthKeyExpiredException : Exception
{
    public AuthKeyExpiredException() : base("The authkey has expired or is invalid.") { }
}

public sealed class CacheNotFoundException : Exception
{
    public CacheNotFoundException(string message) : base(message) { }
}

public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}
