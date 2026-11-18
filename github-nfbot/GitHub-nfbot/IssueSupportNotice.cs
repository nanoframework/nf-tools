//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Octokit;

namespace nanoFramework.Tools.GitHub
{
    internal sealed class IssueSupportNotice
    {
        internal const string Marker = "<!-- nfbot issue-support-options DO NOT REMOVE -->";

        private readonly GitHubClient _github;
        private readonly BlobContainerClient _container;
        private readonly ILogger _log;

        public IssueSupportNotice(GitHubClient github, BlobContainerClient container, ILogger log)
        {
            _github = github;
            _container = container;
            _log = log;
        }

        internal static bool ShouldSend(string action, string authorAssociation, bool isPullRequest)
        {
            return action == "opened"
                && !isPullRequest
                && !string.Equals(authorAssociation, "OWNER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(authorAssociation, "MEMBER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(authorAssociation, "COLLABORATOR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task PostAndMinimizeAsync(long repositoryId, int issueNumber, string author)
        {
            BlobClient lockBlob = _container.GetBlobClient($"support-notices/{repositoryId}-{issueNumber}");
            if (!await lockBlob.ExistsAsync())
            {
                try
                {
                    await lockBlob.UploadAsync(BinaryData.FromString(string.Empty), overwrite: false);
                }
                catch (RequestFailedException ex) when (ex.ErrorCode == "BlobAlreadyExists")
                {
                    // Another delivery created the lock blob first.
                }
            }

            BlobLeaseClient lease = lockBlob.GetBlobLeaseClient();
            await lease.AcquireAsync(TimeSpan.FromSeconds(60));

            using (var cancellation = new CancellationTokenSource())
            {
                Task renewal = RenewLeaseAsync(lease, cancellation.Token);
                try
                {
                    var comments = await _github.Issue.Comment.GetAllForIssue(repositoryId, issueNumber);
                    var comment = comments.FirstOrDefault(c =>
                        string.Equals(c.User?.Login, "nfbot", StringComparison.OrdinalIgnoreCase)
                        && c.Body?.Contains(Marker) == true);

                    // Do not create a comment if lease renewal has already failed.
                    if (renewal.IsCompleted)
                    {
                        await renewal;
                    }

                    bool existingComment = comment != null;
                    if (comment == null)
                    {
                        string body = $"Thanks for opening this issue, @{author}! Need this bug fixed or feature implemented sooner? "
                            + "[Explore professional support](https://nanoframework.net/partner-program/?ref=github-issue) to discuss sponsoring the work.\r\n\r\n"
                            + "[Join us on Discord](https://discord.gg/gCyBu8T) to discuss this issue and the available options. "
                            + $"Community triage remains available without paid support.\r\n\r\n{Marker}";

                        comment = await _github.Issue.Comment.Create(repositoryId, issueNumber, body);
                        _log.LogInformation($"Posted support notice {comment.Id} on repository {repositoryId} issue #{issueNumber}.");
                    }

                    await MinimizeAsync(comment.NodeId, existingComment);
                    _log.LogInformation($"Minimized support notice {comment.Id} on repository {repositoryId} issue #{issueNumber}.");
                }
                finally
                {
                    cancellation.Cancel();
                    try
                    {
                        await renewal;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    finally
                    {
                        await lease.ReleaseAsync();
                    }
                }
            }
        }

        private static async Task RenewLeaseAsync(BlobLeaseClient lease, CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                await lease.RenewAsync(cancellationToken: cancellationToken);
            }
        }

        private async Task MinimizeAsync(string nodeId, bool existingComment)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                throw new InvalidOperationException("The support notice has no GraphQL node ID.");
            }

            if (existingComment)
            {
                var existing = await _github.Connection.Post<MinimizeResponse>(
                    new Uri("graphql", UriKind.Relative),
                    new
                    {
                        query = "query($id: ID!) { node(id: $id) { ... on IssueComment { is_minimized: isMinimized } } }",
                        variables = new { id = nodeId }
                    },
                    "application/vnd.github+json",
                    "application/json");

                CheckResponse(existing, nodeId);
                if (existing.Body?.Data?.Node?.IsMinimized == null)
                {
                    throw new InvalidOperationException($"Could not read the minimized state of support notice {nodeId}.");
                }

                if (existing.Body.Data.Node.IsMinimized == true)
                {
                    return;
                }
            }

            // Aliases match Octokit's snake_case REST response mapping.
            var response = await _github.Connection.Post<MinimizeResponse>(
                new Uri("graphql", UriKind.Relative),
                new
                {
                    query = "mutation($id: ID!) { minimize_comment: minimizeComment(input: {subjectId: $id, classifier: RESOLVED}) { minimized_comment: minimizedComment { is_minimized: isMinimized } } }",
                    variables = new { id = nodeId }
                },
                "application/vnd.github+json",
                "application/json");

            CheckResponse(response, nodeId);
            if (response.Body?.Data?.MinimizeComment?.MinimizedComment?.IsMinimized != true)
            {
                throw new InvalidOperationException($"GitHub did not confirm minimization of support notice {nodeId}.");
            }
        }

        private static void CheckResponse(IApiResponse<MinimizeResponse> response, string nodeId)
        {
            if (response.HttpResponse.StatusCode != HttpStatusCode.OK || response.Body?.Errors?.Length > 0)
            {
                string errors = string.Join("; ", response.Body?.Errors?.Select(e => e.Message) ?? Array.Empty<string>());
                throw new InvalidOperationException($"Failed to minimize support notice {nodeId}: HTTP {(int)response.HttpResponse.StatusCode}. {errors}");
            }
        }

        public sealed class MinimizeResponse
        {
            public MinimizeData Data { get; set; }
            public GraphQlError[] Errors { get; set; }
        }

        public sealed class MinimizeData
        {
            public MinimizePayload MinimizeComment { get; set; }
            public MinimizedComment Node { get; set; }
        }

        public sealed class MinimizePayload
        {
            public MinimizedComment MinimizedComment { get; set; }
        }

        public sealed class MinimizedComment
        {
            public bool? IsMinimized { get; set; }
        }

        public sealed class GraphQlError
        {
            public string Message { get; set; }
        }
    }
}
