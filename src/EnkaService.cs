using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GenshinWishTracker;

internal sealed record EnkaCharacterSummary(
    string Name,
    string Summary,
    string StatsText,
    string ArtifactSummary,
    string ArtifactDetails);

internal sealed record EnkaShowcase(
    string Nickname,
    int AdventureRank,
    int CacheSeconds,
    IReadOnlyList<EnkaCharacterSummary> Characters);

internal sealed class EnkaRequestException : HttpRequestException
{
    internal int RetryAfterSeconds { get; }

    internal EnkaRequestException(string message, int retryAfterSeconds)
        : base(message) => RetryAfterSeconds = retryAfterSeconds;
}

internal static class EnkaService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "GenshinWishTracker/1.0 (+https://github.com/greyfoxdevelopment/GenshinWishTracker)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    internal static async Task<EnkaShowcase> GetShowcaseAsync(string uid, string appDirectory)
    {
        string cacheDirectory = Path.Combine(appDirectory, "EnkaCache");
        Directory.CreateDirectory(cacheDirectory);
        string cachePath = Path.Combine(cacheDirectory, $"{uid}.json");

        if (File.Exists(cachePath))
        {
            EnkaCacheEntry? cached = JsonSerializer.Deserialize<EnkaCacheEntry>(
                await File.ReadAllTextAsync(cachePath), JsonOptions);
            if (cached == null || string.IsNullOrWhiteSpace(cached.Payload))
                throw new InvalidDataException("The saved Enka response cache is invalid. Remove the EnkaCache folder and retry.");
            if (cached.ExpiresUtc > DateTimeOffset.UtcNow)
                return ParseShowcase(cached.Payload, (int)Math.Ceiling((cached.ExpiresUtc - DateTimeOffset.UtcNow).TotalSeconds));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://enka.network/api/uid/{uid}/");
        using HttpResponseMessage response = await Http.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
            throw CreateRequestException(response);

        string payload = await response.Content.ReadAsStringAsync();
        EnkaShowcase showcase = ParseShowcase(payload, cacheSeconds: 60);
        int cacheSeconds = GetCacheSeconds(payload);
        showcase = showcase with { CacheSeconds = cacheSeconds };

        var entry = new EnkaCacheEntry
        {
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(cacheSeconds),
            Payload = payload
        };
        string temporaryPath = cachePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(entry, JsonOptions));
        File.Move(temporaryPath, cachePath, overwrite: true);
        return showcase;
    }

    private static EnkaShowcase ParseShowcase(string payload, int cacheSeconds)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement playerInfo = root.TryGetProperty("playerInfo", out JsonElement info)
            ? info
            : default;
        string nickname = GetString(playerInfo, "nickname") ?? "Player";
        int adventureRank = GetInt(playerInfo, "level") ?? 0;
        var characters = new List<EnkaCharacterSummary>();

        if (root.TryGetProperty("avatarInfoList", out JsonElement avatarList) &&
            avatarList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement avatar in avatarList.EnumerateArray())
                characters.Add(ParseCharacter(avatar));
        }

        return new EnkaShowcase(nickname, adventureRank, cacheSeconds, characters);
    }

    private static EnkaCharacterSummary ParseCharacter(JsonElement avatar)
    {
        string avatarId = GetString(avatar, "avatarId") ?? "Unknown";
        int level = GetCharacterLevel(avatar);
        int constellation = avatar.TryGetProperty("talentIdList", out JsonElement talents) &&
                            talents.ValueKind == JsonValueKind.Array
            ? talents.GetArrayLength()
            : 0;
        JsonElement fightProps = avatar.TryGetProperty("fightPropMap", out JsonElement props)
            ? props
            : default;

        double? critRate = GetFightProperty(fightProps, "20");
        double? critDamage = GetFightProperty(fightProps, "22");
        double? energyRecharge = GetFightProperty(fightProps, "23");
        double? elementalMastery = GetFightProperty(fightProps, "28");
        string stats = string.Join(" · ", new[]
        {
            FormatPercentStat("CRIT Rate", critRate),
            FormatPercentStat("CRIT DMG", critDamage),
            FormatPercentStat("Energy Recharge", energyRecharge),
            elementalMastery is double em ? $"EM {em:0}" : null
        }.Where(value => value != null));

        ArtifactAnalysis artifacts = AnalyzeArtifacts(avatar);
        string statsSummary = string.IsNullOrEmpty(stats) ? "Combat stats unavailable" : stats;
        string artifactSummary = artifacts.Count == 0
            ? "No artifact details available"
            : $"{artifacts.Count} artifacts · {artifacts.TotalCritValue:0.0} total CV · {artifacts.AverageLevel:0.#} average level";

        return new EnkaCharacterSummary(
            $"Character {avatarId}",
            $"Lv. {level} · C{constellation}",
            statsSummary,
            artifactSummary,
            artifacts.PieceSummaries);
    }

    private static ArtifactAnalysis AnalyzeArtifacts(JsonElement avatar)
    {
        if (!avatar.TryGetProperty("equipList", out JsonElement equips) ||
            equips.ValueKind != JsonValueKind.Array)
            return new ArtifactAnalysis(0, 0, 0, "");

        int artifactCount = 0;
        int totalLevel = 0;
        double totalCritValue = 0;
        var pieceSummaries = new List<string>();

        foreach (JsonElement equip in equips.EnumerateArray())
        {
            if (!equip.TryGetProperty("reliquary", out JsonElement reliquary)) continue;
            artifactCount++;
            int level = GetInt(reliquary, "level") ?? 0;
            totalLevel += level;
            double pieceCritValue = 0;

            if (!equip.TryGetProperty("flat", out JsonElement flat) ||
                !flat.TryGetProperty("reliquarySubstats", out JsonElement substats) ||
                substats.ValueKind != JsonValueKind.Array)
            {
                pieceSummaries.Add($"{GetArtifactSlot(flat)} +{level} · CV —");
                continue;
            }

            foreach (JsonElement substat in substats.EnumerateArray())
                pieceCritValue += GetCritValue(substat);

            totalCritValue += pieceCritValue;
            pieceSummaries.Add($"{GetArtifactSlot(flat)} +{level} · CV {pieceCritValue:0.0}");
        }

        return new ArtifactAnalysis(
            artifactCount,
            artifactCount == 0 ? 0 : (double)totalLevel / artifactCount,
            totalCritValue,
            string.Join(Environment.NewLine, pieceSummaries));
    }

    private static double GetCritValue(JsonElement substat)
    {
        string? property = GetString(substat, "appendPropId") ??
                           GetString(substat, "propertyType") ??
                           GetString(substat, "type");
        double? value = GetDouble(substat, "statValue") ??
                        GetDouble(substat, "propValue") ??
                        GetDouble(substat, "value");
        if (property == null || value == null) return 0;

        string normalized = property.ToUpperInvariant();
        double percentValue = value.Value <= 1 ? value.Value * 100 : value.Value;
        if (normalized.Contains("CRITICAL_HURT", StringComparison.Ordinal) ||
            normalized.Contains("CRIT_DMG", StringComparison.Ordinal) ||
            normalized.Contains("CRITICAL_DAMAGE", StringComparison.Ordinal))
            return percentValue;
        if (normalized.Contains("CRITICAL", StringComparison.Ordinal) ||
            normalized.Contains("CRIT_RATE", StringComparison.Ordinal))
            return percentValue * 2;
        return 0;
    }

    private static string GetArtifactSlot(JsonElement flat)
    {
        string? equipType = GetString(flat, "equipType");
        return equipType?.ToUpperInvariant() switch
        {
            "EQUIP_BRACER" => "Flower",
            "EQUIP_NECKLACE" => "Plume",
            "EQUIP_SHOES" => "Sands",
            "EQUIP_RING" => "Goblet",
            "EQUIP_DRESS" => "Circlet",
            _ => "Artifact"
        };
    }

    private static int GetCharacterLevel(JsonElement avatar)
    {
        if (!avatar.TryGetProperty("propMap", out JsonElement props) ||
            props.ValueKind != JsonValueKind.Object)
            return 0;

        foreach (JsonProperty property in props.EnumerateObject())
        {
            JsonElement type = property.Value;
            if (GetString(type, "type") == "4001")
                return GetInt(type, "val") ?? 0;
        }
        return 0;
    }

    private static double? GetFightProperty(JsonElement fightProps, string id)
    {
        if (fightProps.ValueKind != JsonValueKind.Object ||
            !fightProps.TryGetProperty(id, out JsonElement value) ||
            !value.TryGetDouble(out double result))
            return null;
        return result;
    }

    private static string? FormatPercentStat(string name, double? value) =>
        value is double number ? $"{name} {number:P1}" : null;

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static int GetCacheSeconds(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("ttl", out JsonElement ttl) &&
            ttl.TryGetInt32(out int seconds))
            return Math.Clamp(seconds, 1, 86400);
        return 60;
    }

    private static HttpRequestException CreateRequestException(HttpResponseMessage response)
    {
        HttpStatusCode statusCode = response.StatusCode;
        if ((int)statusCode == 429)
        {
            int retryAfter = GetRetryAfterSeconds(response);
            return new EnkaRequestException(
                $"Enka is rate-limiting requests. Wait about {retryAfter} seconds before trying this UID again.",
                retryAfter);
        }

        string message = statusCode switch
        {
            HttpStatusCode.BadRequest => "Enka rejected the UID format. Enter the 9-digit in-game UID.",
            HttpStatusCode.NotFound => "Enka could not find this player. Check the UID and try again.",
            (HttpStatusCode)424 => "Enka reports a game maintenance or data update issue. Try again later.",
            HttpStatusCode.ServiceUnavailable => "Enka is temporarily unavailable. Try again later.",
            _ => $"Enka request failed with HTTP {(int)statusCode} ({statusCode})."
        };
        return new HttpRequestException(message);
    }

    private static int GetRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delay)
            return Math.Clamp((int)Math.Ceiling(delay.TotalSeconds), 1, 3600);
        if (retryAfter?.Date is DateTimeOffset retryDate)
            return Math.Clamp((int)Math.Ceiling((retryDate - DateTimeOffset.UtcNow).TotalSeconds), 1, 3600);
        return 60;
    }

    private sealed class EnkaCacheEntry
    {
        [JsonPropertyName("expiresUtc")]
        public DateTimeOffset ExpiresUtc { get; set; }

        [JsonPropertyName("payload")]
        public string Payload { get; set; } = "";
    }

    private sealed record ArtifactAnalysis(
        int Count, double AverageLevel, double TotalCritValue, string PieceSummaries);
}
