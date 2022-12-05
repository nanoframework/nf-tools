using System;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.DependencyUpdater;

internal static class GitHubHelper
{
    internal static Match GetRepoNameFromInputString(string input)
    {
        if (GitUrlContainsPat(input))
        {
            return Regex.Match(input,
                "(?:https:\\/\\/(?'pat'\\S+)@github\\.com\\/(.*)\\/)(?'repoName'\\S+)(?:\\.git\\s\\(fetch\\)|\\s\\(fetch\\))");
        }


        return Regex.Match(input,
            "(?:https:\\/\\/github\\.com\\/(.*)\\/)(?'repoName'\\S+)(?:\\.git\\s\\(fetch\\)|\\s\\(fetch\\))");
    }

    internal static string GetRepoOwnerFromUrl(string url)
    {

        var regexResult = GetMatchForRepoOwnerFromUrl(url);
        return regexResult.Groups["repoOwner"].Value;
    }

    private static Match GetMatchForRepoOwnerFromUrl(string url)
    {
        if (GitUrlContainsPat(url))
        {
            var regexResultPat = Regex.Match(url,
                "(?:https:\\/\\/(?'pat'\\S+)@github\\.com\\/(?'repoOwner'.*)\\/)(?'repoName'\\S+)(?:\\.git\\s\\(fetch\\)|\\s\\(fetch\\))");

            if (!regexResultPat.Success)
            {
                throw new Exception($"Unable to find repository owner in {url}");
            }

            return regexResultPat;
        }

        var regexResult = Regex.Match(url,
            "(?:https:\\/\\/github\\.com\\/(?'repoOwner'.*)\\/)(?'repoName'\\S+)(?:\\.git\\s\\(fetch\\)|\\s\\(fetch\\))");

        if (!regexResult.Success)
        {
            throw new Exception($"Unable to find repository owner in {url}");
        }

        return regexResult;
    }

    private static bool GitUrlContainsPat(string url)
    {
        var match = Regex.Match(url, "origin(.)https://(.*)@github");
        return match.Success;
    }

    internal static string GetLibNameFromRegexMatch(Match match)
    {
        // need to remove .git from end of URL, if there
        return match.Groups["repoName"].Value.Replace(".git", "");
    }
}
