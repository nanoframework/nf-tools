//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace nanoFramework.Tools.GitHub
{
    /// <summary>
    /// Content to be shared on social networks.
    /// </summary>
    /// <param name="ShortText">Short text, suitable for networks with tight character limits (e.g. X). The URL is passed separately.</param>
    /// <param name="LongText">Long text, suitable for networks like LinkedIn. Already includes the URL.</param>
    /// <param name="Url">URL being shared (typically the PR).</param>
    internal sealed record ShareContent(string ShortText, string LongText, string Url);

    /// <summary>
    /// A social network where content can be shared through a pre-filled share link.
    /// </summary>
    /// <param name="Name">Name of the network, shown in the badge.</param>
    /// <param name="BadgeColor">Badge color (hex, without the #).</param>
    /// <param name="BuildUrl">Builds the share URL for the given content.</param>
    internal sealed record SocialShareTarget(string Name, string BadgeColor, Func<ShareContent, string> BuildUrl);

    /// <summary>
    /// A link to one of the project's community accounts.
    /// </summary>
    internal sealed record CommunityLink(string Name, string Url);

    /// <summary>
    /// Helpers to build the "spread the word" section added to comments celebrating contributions.
    /// </summary>
    internal static class SocialShare
    {
        internal const string XHandle = "nanoFramework";
        internal const string XUrl = "https://x.com/nanoFramework";
        internal const string LinkedInUrl = "https://www.linkedin.com/company/dotnet-nanoframework";
        internal const string LinkedInMention = "@.NET nanoFramework";
        internal const string ShortHashtags = "#dotnet #csharp #IoT";
        internal const string LongHashtags = "#dotnet #csharp #nanoFramework #IoT #embedded #OpenSource";

        /// <summary>
        /// Networks offered for sharing. To add a new one just add an entry here, for example:
        /// <list type="bullet">
        /// <item>Bluesky: https://bsky.app/intent/compose?text={text}</item>
        /// <item>Mastodon: https://mastodon.social/share?text={text}</item>
        /// </list>
        /// </summary>
        internal static readonly IReadOnlyList<SocialShareTarget> Targets = new List<SocialShareTarget>
        {
            // shareActive + text pre-fills the post editor (share-offsite ignores title and summary)
            new("LinkedIn", "0A66C2", c => $"https://www.linkedin.com/feed/?shareActive=true&text={Uri.EscapeDataString(c.LongText)}"),

            // "related" suggests following our account after posting
            new("X", "000000", c => $"https://x.com/intent/post?text={Uri.EscapeDataString(c.ShortText)}&url={Uri.EscapeDataString(c.Url)}&related={XHandle}"),
        };

        /// <summary>
        /// Project community accounts, listed in the "follow us" line.
        /// </summary>
        internal static readonly IReadOnlyList<CommunityLink> CommunityLinks = new List<CommunityLink>
        {
            new("X", XUrl),
            new("LinkedIn", LinkedInUrl),
            new("YouTube", "https://www.youtube.com/c/nanoFramework"),
            new("Discord", "https://discord.gg/gCyBu8T"),
        };

        /// <summary>
        /// Builds the texts celebrating a first code contribution.
        /// </summary>
        internal static ShareContent BuildFirstContributionContent(string prUrl)
        {
            string shortText = $"🎉 Just landed my first code contribution to .NET @{XHandle}! Writing C# for microcontrollers is seriously fun 🤖⚡ Come build with us! {ShortHashtags}";

            string longText =
                "🎉 I've just made my first code contribution to .NET nanoFramework, the open-source platform that runs C# on microcontrollers! 🚀\n\n" +
                "I'm having a great time coding in C# for tiny embedded and IoT devices. Blinking LEDs has never felt this good! 💡\n\n" +
                $"👉 Check out my PR: {prUrl}\n\n" +
                "Curious about C# on microcontrollers? Come join the community, your first PR is closer than you think! 💙\n\n" +
                $"Follow .NET nanoFramework: {LinkedInUrl}\n\n" +
                LongHashtags;

            return new ShareContent(shortText, longText, prUrl);
        }

        /// <summary>
        /// Builds the markdown section inviting the contributor to share the news on social media.
        /// </summary>
        internal static string BuildMarkdown(ShareContent content)
        {
            var badges = Targets.Select(t =>
                $"<a href=\"{t.BuildUrl(content)}\"><img alt=\"Share on {t.Name}\" src=\"https://img.shields.io/badge/Share%20on-{Uri.EscapeDataString(t.Name)}-{t.BadgeColor}?style=for-the-badge\"></a>");

            var followLinks = CommunityLinks.Select(l => $"[{l.Name}]({l.Url})");

            var sb = new StringBuilder();

            sb.Append("### 📣 Make some noise!\r\n\r\n");
            sb.Append("Your first code contribution just got merged, that's a big deal and totally worth celebrating! 🥳\r\n");
            sb.Append("Go ahead and brag about it on social media, and inspire others to give C# on microcontrollers a try. One click and the post is ready for you:\r\n\r\n");
            sb.Append(string.Join("&nbsp;", badges));
            sb.Append("\r\n\r\n");
            sb.Append($"💡 Tip: type `{LinkedInMention}` in your LinkedIn post to tag us, and don't forget to mention [@{XHandle}]({XUrl}) on X!\r\n\r\n");
            sb.Append("<details>\r\n<summary>✍️ Suggested post (copy & paste)</summary>\r\n\r\n");
            sb.Append("```text\r\n");
            sb.Append(content.LongText.Replace("\n", "\r\n"));
            sb.Append("\r\n```\r\n\r\n</details>\r\n\r\n");
            sb.Append($"Stay in touch and follow .NET nanoFramework on {string.Join(" · ", followLinks)} 💙");

            return sb.ToString();
        }
    }
}
