using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GenshinWishTracker;

/// <summary>One line of the dashboard changelog, built from a single commit.</summary>
public sealed class ChangelogEntry
{
    public string Title { get; init; } = "";
    public string Details { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTimeOffset? Date { get; init; }
    public string ShortSha { get; init; } = "";
    public Uri? Url { get; init; }

    public Visibility DetailsVisibility =>
        string.IsNullOrEmpty(Details) ? Visibility.Collapsed : Visibility.Visible;

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (Date is DateTimeOffset date) parts.Add(date.ToLocalTime().ToString("MMM d, yyyy"));
            if (!string.IsNullOrWhiteSpace(Author)) parts.Add(Author);
            if (!string.IsNullOrWhiteSpace(ShortSha)) parts.Add(ShortSha);
            return string.Join(" · ", parts);
        }
    }
}

internal sealed class ChangelogException(string message) : Exception(message);

/// <summary>Reads recent commits from this project's GitHub repository to show as a changelog.</summary>
internal static class ChangelogService
{
    internal const string Repository = "greyfoxdevelopment/GFDev-Genshin-Companion";
    internal const string CommitsPageUrl = "https://github.com/" + Repository + "/commits";

    private const string CommitsApiUrl = "https://api.github.com/repos/" + Repository + "/commits?per_page=40";
    private const int MaxEntries = 30;
    private const int MaxTitleLength = 200;
    private const int MaxDetailsLength = 600;

    // Lines that tools append to commit messages; they say nothing about what changed.
    private static readonly string[] TrailerPrefixes =
    [
        "co-authored-by:",
        "signed-off-by:",
        "claude-session:",
        "🤖 generated with"
    ];

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GenshinWishTracker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal static async Task<IReadOnlyList<ChangelogEntry>> GetRecentChangesAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http.GetAsync(CommitsApiUrl, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new ChangelogException(
                "GitHub is limiting requests right now. Try again in a little while, or open the full history in your browser.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ChangelogException(
                "The project's commit history could not be found. Open it in your browser instead.");
        response.EnsureSuccessStatusCode();

        List<GitHubCommit>? commits =
            await response.Content.ReadFromJsonAsync<List<GitHubCommit>>(cancellationToken: cancellationToken);

        var entries = new List<ChangelogEntry>();
        foreach (GitHubCommit commit in commits ?? [])
        {
            // Merge commits only repeat the commits they merge.
            if (commit.Parents is { Count: > 1 }) continue;

            string message = (commit.Commit?.Message ?? "").Replace("\r\n", "\n").Trim();
            if (message.Length == 0) continue;

            SplitMessage(message, out string title, out string details);
            if (title.Length == 0) continue;
            if (title.StartsWith("Merge pull request", StringComparison.Ordinal) ||
                title.StartsWith("Merge branch", StringComparison.Ordinal))
                continue;

            entries.Add(new ChangelogEntry
            {
                Title = title,
                Details = details,
                Author = commit.Commit?.Author?.Name?.Trim() ?? "",
                Date = commit.Commit?.Author?.Date,
                ShortSha = commit.Sha.Length >= 7 ? commit.Sha[..7] : commit.Sha,
                Url = TryGetCommitUri(commit.HtmlUrl)
            });

            if (entries.Count >= MaxEntries) break;
        }

        return entries;
    }

    private static void SplitMessage(string message, out string title, out string details)
    {
        string[] lines = message.Split('\n');
        title = Truncate(lines[0].Trim(), MaxTitleLength);

        IEnumerable<string> body = lines.Skip(1)
            .Select(line => line.TrimEnd())
            .Where(line => !IsTrailerLine(line));
        string joined = string.Join("\n", body).Trim();
        joined = Regex.Replace(joined, "\n{3,}", "\n\n");
        details = Truncate(joined, MaxDetailsLength);
    }

    private static bool IsTrailerLine(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("https://claude.ai/", StringComparison.OrdinalIgnoreCase)) return true;
        return TrailerPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";

    /// <summary>Only links that stay inside this repository on github.com are clickable.</summary>
    private static Uri? TryGetCommitUri(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/" + Repository + "/", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;

    private sealed class GitHubCommit
    {
        [JsonPropertyName("sha")]
        public string Sha { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("commit")]
        public GitHubCommitDetails? Commit { get; set; }

        [JsonPropertyName("parents")]
        public List<GitHubParent>? Parents { get; set; }
    }

    private sealed class GitHubParent
    {
        [JsonPropertyName("sha")]
        public string Sha { get; set; } = "";
    }

    private sealed class GitHubCommitDetails
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("author")]
        public GitHubCommitAuthor? Author { get; set; }
    }

    private sealed class GitHubCommitAuthor
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("date")]
        public DateTimeOffset? Date { get; set; }
    }
}
