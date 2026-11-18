//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.GitHub;
using Newtonsoft.Json.Linq;
using Octokit;
using Octokit.Internal;

namespace GitHub_nfbot.Tests
{
    [TestClass]
    public class IssueSupportNoticeTests
    {
        [TestMethod]
        [DataRow("OWNER")]
        [DataRow("owner")]
        [DataRow("MEMBER")]
        [DataRow("member")]
        [DataRow("COLLABORATOR")]
        public void ExcludesPrivilegedAuthorsFromWebhook(string association)
        {
            Assert.IsFalse(IssueSupportNotice.ShouldSend("opened", association, false));
        }

        [TestMethod]
        [DataRow("NONE")]
        [DataRow("CONTRIBUTOR")]
        [DataRow("FIRST_TIMER")]
        [DataRow("UNKNOWN")]
        [DataRow("")]
        [DataRow(null)]
        public void PostsForExternalOrUncertainAuthors(string association)
        {
            Assert.IsTrue(IssueSupportNotice.ShouldSend("opened", association, false));
        }

        [TestMethod]
        [DataRow("edited", false)]
        [DataRow("reopened", false)]
        [DataRow("opened", true)]
        public void DoesNotNotifyOnEditsReopensOrPullRequests(string action, bool isPullRequest)
        {
            Assert.IsFalse(IssueSupportNotice.ShouldSend(action, "NONE", isPullRequest));
        }

        [TestMethod]
        public async Task PostsAgreedGenericCopyAndMinimizesWithoutDeleting()
        {
            using (var fixture = new NoticeFixture())
            {
                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");

                Assert.AreEqual(1, fixture.GitHub.CreateCount);
                Assert.AreEqual(
                    "Thanks for opening this issue, @issue-author! Need this bug fixed or feature implemented sooner? "
                    + "[Explore professional support](https://nanoframework.net/partner-program/?ref=github-issue) to discuss sponsoring the work.\r\n\r\n"
                    + "[Join us on Discord](https://discord.gg/gCyBu8T) to discuss this issue and the available options. "
                    + $"Community triage remains available without paid support.\r\n\r\n{IssueSupportNotice.Marker}",
                    (string)fixture.GitHub.Comments.Single()["body"]);
                Assert.IsTrue(fixture.GitHub.IsMinimized);
                Assert.IsTrue(fixture.Storage.Exists);
                Assert.IsFalse(fixture.Storage.IsLeased);
                Assert.AreEqual(1, fixture.Storage.ReleaseCount);
            }
        }

        [TestMethod]
        public async Task FindsOnlyBotMarkedNoticeAcrossAllCommentPages()
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.Comments.Add(Comment(10, "someone-else", IssueSupportNotice.Marker));
                fixture.GitHub.Comments.Add(Comment(11, "nfbot", "An unrelated bot comment."));
                fixture.GitHub.Comments.Add(Comment(12, "NFBOT", IssueSupportNotice.Marker));
                fixture.GitHub.Paginate = true;

                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");

                Assert.AreEqual(0, fixture.GitHub.CreateCount);
                Assert.AreEqual(2, fixture.GitHub.ListCount);
                Assert.AreEqual("IC_12", fixture.GitHub.MinimizedNodeId);
                Assert.AreEqual(3, fixture.GitHub.Comments.Count);
            }
        }

        [TestMethod]
        [DataRow("{\"errors\":[{\"message\":\"Forbidden\"}]}")]
        [DataRow("{\"data\":{\"minimize_comment\":{\"minimized_comment\":{\"is_minimized\":true}}},\"errors\":[{\"message\":\"Partial failure\"}]}")]
        [DataRow("{\"data\":{\"minimize_comment\":{\"minimized_comment\":{\"is_minimized\":false}}}}")]
        [DataRow("{\"data\":null}")]
        public async Task MinimizationFailureRetainsCommentAndRetryReusesIt(string result)
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.MutationResult = result;
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                    () => fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author"));

                Assert.AreEqual(1, fixture.GitHub.Comments.Count);
                Assert.IsFalse(fixture.Storage.IsLeased);

                fixture.GitHub.MutationResult = null;
                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");

                Assert.AreEqual(1, fixture.GitHub.CreateCount);
                Assert.IsTrue(fixture.GitHub.IsMinimized);
            }
        }

        [TestMethod]
        public async Task HttpFailureIsPropagatedAndDoesNotDeleteComment()
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.MutationStatus = HttpStatusCode.Forbidden;
                await Assert.ThrowsExactlyAsync<ForbiddenException>(
                    () => fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author"));

                Assert.AreEqual(1, fixture.GitHub.Comments.Count);
                Assert.IsFalse(fixture.Storage.IsLeased);
            }
        }

        [TestMethod]
        public async Task LostCreateResponseIsReconciledOnRetry()
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.FailCreateAfterStoring = true;
                await Assert.ThrowsExactlyAsync<ApiException>(
                    () => fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author"));

                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");

                Assert.AreEqual(1, fixture.GitHub.CreateCount);
                Assert.IsTrue(fixture.GitHub.IsMinimized);
            }
        }

        [TestMethod]
        [DataRow("{\"errors\":[{\"message\":\"Forbidden\"}]}")]
        [DataRow("{\"data\":{\"node\":null}}")]
        [DataRow("{\"data\":{\"node\":{}}}")]
        public async Task UncertainExistingCommentStateFailsWithoutReposting(string result)
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.Comments.Add(Comment(12, "nfbot", IssueSupportNotice.Marker));
                fixture.GitHub.QueryResult = result;

                await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                    () => fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author"));

                Assert.AreEqual(0, fixture.GitHub.CreateCount);
                Assert.AreEqual(0, fixture.GitHub.MutationCount);
                Assert.IsFalse(fixture.Storage.IsLeased);
            }
        }

        [TestMethod]
        public async Task RetryDoesNotMutateAnAlreadyMinimizedNotice()
        {
            using (var fixture = new NoticeFixture())
            {
                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");
                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");

                Assert.AreEqual(1, fixture.GitHub.CreateCount);
                Assert.AreEqual(1, fixture.GitHub.MutationCount);
            }
        }

        [TestMethod]
        public async Task ConcurrentDeliveryRetriesAfterLeaseContentionWithoutDuplicating()
        {
            using (var fixture = new NoticeFixture())
            {
                fixture.GitHub.ListStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.GitHub.ContinueList = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task first = fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");
                await fixture.GitHub.ListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                try
                {
                    var error = await Assert.ThrowsExactlyAsync<RequestFailedException>(
                        () => fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author"));
                    Assert.AreEqual("LeaseAlreadyPresent", error.ErrorCode);
                }
                finally
                {
                    fixture.GitHub.ContinueList.TrySetResult(true);
                    await first;
                }

                await fixture.Notice.PostAndMinimizeAsync(1, 2, "issue-author");
                Assert.AreEqual(1, fixture.GitHub.CreateCount);
            }
        }

        private static JObject Comment(int id, string author, string body)
        {
            return new JObject
            {
                ["id"] = id,
                ["node_id"] = $"IC_{id}",
                ["user"] = new JObject { ["login"] = author },
                ["body"] = body
            };
        }

        private sealed class NoticeFixture : IDisposable
        {
            public GitHubHandler GitHub { get; } = new GitHubHandler();
            public StorageHandler Storage { get; } = new StorageHandler();
            public IssueSupportNotice Notice { get; }
            private readonly HttpClient _storageHttp;

            public NoticeFixture()
            {
                var connection = new Connection(new ProductHeaderValue("nfbot-tests"), new HttpClientAdapter(() => GitHub));
                var github = new GitHubClient(connection) { Credentials = new Credentials("test-token") };
                _storageHttp = new HttpClient(Storage);
                var options = new BlobClientOptions { Transport = new HttpClientTransport(_storageHttp) };
                options.Retry.MaxRetries = 0;
                var container = new BlobContainerClient(new Uri("https://storage.example/nfbot-webhooks"), options);
                Notice = new IssueSupportNotice(github, container, NullLogger.Instance);
            }

            public void Dispose()
            {
                GitHub.Dispose();
                _storageHttp.Dispose();
            }
        }

        private sealed class GitHubHandler : HttpMessageHandler
        {
            public List<JObject> Comments { get; } = new List<JObject>();
            public int CreateCount { get; private set; }
            public int ListCount { get; private set; }
            public int MutationCount { get; private set; }
            public bool IsMinimized { get; private set; }
            public string MinimizedNodeId { get; private set; }
            public string MutationResult { get; set; }
            public string QueryResult { get; set; }
            public HttpStatusCode MutationStatus { get; set; } = HttpStatusCode.OK;
            public bool FailCreateAfterStoring { get; set; }
            public bool Paginate { get; set; }
            public TaskCompletionSource<bool> ListStarted { get; set; }
            public TaskCompletionSource<bool> ContinueList { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.AreEqual("test-token", request.Headers.Authorization?.Parameter);
                if (request.RequestUri.AbsolutePath == "/repositories/1/issues/2/comments")
                {
                    if (request.Method == HttpMethod.Get)
                    {
                        ListCount++;
                        ListStarted?.TrySetResult(true);
                        if (ContinueList != null)
                        {
                            await ContinueList.Task.WaitAsync(cancellationToken);
                        }

                        bool firstPage = Paginate && !request.RequestUri.Query.Contains("page=2");
                        var page = firstPage ? Comments.Take(2) : Paginate ? Comments.Skip(2) : Comments;
                        var response = Json(HttpStatusCode.OK, new JArray(page).ToString());
                        if (firstPage)
                        {
                            response.Headers.Add("Link", "<https://api.github.com/repositories/1/issues/2/comments?page=2>; rel=\"next\"");
                        }
                        return response;
                    }

                    if (request.Method == HttpMethod.Post)
                    {
                        CreateCount++;
                        var body = JObject.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                        var comment = Comment(123, "nfbot", (string)body["body"]);
                        Comments.Add(comment);
                        if (FailCreateAfterStoring)
                        {
                            FailCreateAfterStoring = false;
                            return Json(HttpStatusCode.InternalServerError, "{\"message\":\"The comment was stored, but its response was lost.\"}");
                        }
                        return Json(HttpStatusCode.Created, comment.ToString());
                    }
                }

                if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/graphql")
                {
                    var body = JObject.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                    if (((string)body["query"]).StartsWith("query", StringComparison.Ordinal))
                    {
                        Assert.IsTrue(((string)body["query"]).Contains("is_minimized: isMinimized"));
                        if (QueryResult != null)
                        {
                            return Json(HttpStatusCode.OK, QueryResult);
                        }
                        return Json(HttpStatusCode.OK, new JObject
                        {
                            ["data"] = new JObject { ["node"] = new JObject { ["is_minimized"] = IsMinimized } }
                        }.ToString());
                    }

                    Assert.IsTrue(((string)body["query"]).Contains("classifier: RESOLVED"));
                    Assert.IsTrue(((string)body["query"]).Contains("minimize_comment: minimizeComment"));
                    Assert.IsTrue(((string)body["query"]).Contains("minimized_comment: minimizedComment"));
                    Assert.IsTrue(((string)body["query"]).Contains("is_minimized: isMinimized"));
                    MinimizedNodeId = (string)body["variables"]["id"];
                    MutationCount++;
                    if (MutationStatus != HttpStatusCode.OK)
                    {
                        return Json(MutationStatus, "{\"message\":\"Forbidden\"}");
                    }
                    if (MutationResult != null)
                    {
                        return Json(HttpStatusCode.OK, MutationResult);
                    }
                    IsMinimized = true;
                    return Json(HttpStatusCode.OK, "{\"data\":{\"minimize_comment\":{\"minimized_comment\":{\"is_minimized\":true}}}}");
                }

                Assert.Fail($"Unexpected GitHub request: {request.Method} {request.RequestUri}");
                return null;
            }
        }

        private sealed class StorageHandler : HttpMessageHandler
        {
            public bool Exists { get; private set; }
            public bool IsLeased { get; private set; }
            public int ReleaseCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.AreEqual("/nfbot-webhooks/support-notices/1-2", request.RequestUri.AbsolutePath);
                HttpResponseMessage response;
                lock (this)
                {
                    if (request.Method == HttpMethod.Head)
                    {
                        response = Json(Exists ? HttpStatusCode.OK : HttpStatusCode.NotFound, string.Empty);
                        if (!Exists)
                        {
                            response.Headers.Add("x-ms-error-code", "BlobNotFound");
                        }
                    }
                    else if (request.Method == HttpMethod.Put && request.RequestUri.Query == "?comp=lease")
                    {
                        string action = request.Headers.GetValues("x-ms-lease-action").Single();
                        if (action == "acquire" && IsLeased)
                        {
                            response = Json(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"LeaseAlreadyPresent\",\"message\":\"The blob is already leased.\"}}");
                            response.Headers.Add("x-ms-error-code", "LeaseAlreadyPresent");
                        }
                        else
                        {
                            IsLeased = action != "release";
                            if (action == "release")
                            {
                                ReleaseCount++;
                            }
                            response = Json(action == "acquire" ? HttpStatusCode.Created : HttpStatusCode.OK, string.Empty);
                            response.Headers.Add("x-ms-lease-id", "00000000-0000-0000-0000-000000000001");
                        }
                    }
                    else if (request.Method == HttpMethod.Put)
                    {
                        Assert.AreEqual("*", request.Headers.GetValues("If-None-Match").Single());
                        Exists = true;
                        response = Json(HttpStatusCode.Created, string.Empty);
                    }
                    else
                    {
                        Assert.Fail($"Unexpected storage request: {request.Method} {request.RequestUri}");
                        return null;
                    }
                }
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test\"");
                response.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                return Task.FromResult(response);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
