// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using System.Text.Json;

namespace nanoFramework.Migrate.Cli;

/// <summary>Minimal GitHub REST client (BCL only) for listing org repositories.</summary>
internal static class GitHub
{
    internal sealed record Repo(string Name, string CloneUrl, bool Archived);

    public static async Task<List<Repo>> ListOrgReposAsync(string org, string? token, bool includeArchived, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nano-migrate", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var repos = new List<Repo>();
        for (int page = 1; ; page++)
        {
            var url = $"https://api.github.com/orgs/{org}/repos?per_page=100&page={page}&type=public";
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var hint = resp.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? " (rate limited — pass --token or set GITHUB_TOKEN)" : "";
                throw new UserError($"GitHub API returned {(int)resp.StatusCode} {resp.StatusCode}{hint}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            if (arr.GetArrayLength() == 0) break;

            foreach (var e in arr.EnumerateArray())
            {
                var archived = e.TryGetProperty("archived", out var ar) && ar.GetBoolean();
                if (archived && !includeArchived) continue;
                repos.Add(new Repo(
                    e.GetProperty("name").GetString()!,
                    e.GetProperty("clone_url").GetString()!,
                    archived));
            }
            if (arr.GetArrayLength() < 100) break;
        }
        return repos;
    }
}
