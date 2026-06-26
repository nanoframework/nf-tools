//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace nanoFramework.Tools.GitHub
{
    public static class GitHub_nfbot
    {
        // strings to be used in messages and comments
        private const string _fixRequestTagComment = "<!-- nfbot fix request DO NOT REMOVE -->";
        private const string _todoTagComment = "<!-- todo-tag DO NOT REMOVE -->";
        private const string _issueCommentInvalidDeviceCaps = ":disappointed: If that's relevant, make sure to include the complete Device Capabilities output.\r\n.If it isn't, just remove the section completely.\r\nAfter fixing that, feel free to reopen the issue.";
        private const string _issueCommentUnshureAboutIssueContent = "🤪 I couldn't figure out what type of issue you're trying to open...\r\nMake sure you're used one of the **templates** and have include all the required information. After doing that feel free to reopen the issue.\r\n\r\nIf you have a question, need clarification on something, need help on a particular situation or want to start a discussion, **DO NOT** open an issue here. It is best to start a conversation on one of our [Discord channels](https://discordapp.com/invite/gCyBu8T) or to ask the question on [Stack Overflow](https://stackoverflow.com/questions/tagged/nanoframework) using the `nanoframework` tag.";
        private const string _prCommentUserIgnoringTemplateContent = "😯 I'm afraid you'll have to use the PR template like the rest of us...\r\nMake sure you've used the **template** and have include all the required information and fill in the appropriate details. After doing that feel free to reopen the PR. If you have questions we are here to help.";
        private const string _prCommentChecklistWithOpenItemsTemplateContent = ":disappointed: I'm afraid you'll left some tasks behind...\r\nMake sure you've went through all the tasks in the list. If you have questions we are here to help.";
        private const string _prCommunityTargetMissingTargetContent = ":disappointed: You need to check which targets are affected in the list...\\r\\nMake sure you follow the PR template. After doing that feel free to reopen the PR.\\r\\nIf you have questions we are here to help.";
        private const string _fixCheckListComment = "I've fixed the checklist for you.\\r\\nFYI, the correct format is [x], no spaces inside brackets, no other chars.";
        private const string _missingProjectToReproduceComment = "please provide a minimal solution that reproduces the issue you’re reporting, preferably a link to a GitHub repository (or similar).\r\nWhy? Unless the code to reproduce the issue it’s just a couple of lines from the standard API, it takes time! 😯\r\nSetup a full project on Visual Studio, adding references to the required NuGets and/or whatever other projects you may be referencing, chasing the correct versions, copying, pasting, and adapting whatever code you may have provided, etc. All that takes time to the developer working on this. Just to get started. It’s not even working on the issue yet and has already wasted a lot of precious time.\r\nWe’ll help you, for sure! We want to help but, please, make our life easier, OK? 😅";

        // strings for issues content
        private const string _issueContentRemoveContentInstruction = ":exclamation: Remove the content above here and fill out details below. :exclamation:";
        private const string _issueContentBeforePosting = "**Before posting the issue**";
        private const string _issueContentAdditionalContext = "Add any other context";
        private const string _issueContentAttemptPRInstructions1 = "Attempt to submit a [Pull Request (PR)]";
        private const string _issueContentAttemptPRInstructions2 = "If you know how to add whatever you're suggesting";
        private const string _issueContentAttemptPRInstructions3 = "Even if the feature is a good idea and viable";
        private const string _issueContentExpectedBehaviour = "A clear and concise description of what you expected to happen.";
        private const string _issueContentBugDescription = "A clear and concise description of what the bug is.";
        private const string _issueContentDescribeAlternatives = "A clear and concise description of any alternative solutions or features you've considered.";
        private const string _issueSupportOptionsNotice = "\r\nIf you are a commercial user, time to market maybe be important to you. Know that [Professional Support](https://docs.nanoframework.net/content/support/professional-support.html) options are available.\r\nIf you are stuck on something, need your bug fixed in a hurry or would like to sponsor the feature that you're currently missing, feel free to reach out to us here or on the project's [Discord server](https://discordapp.com/invite/gCyBu8T).\r\nIf this it not of interest to you, that's fine too. This issue will get into the queue and will be eventually addressed.";
        private const string _issueTodoWihtouPermission = "\r\n🤨 Well... we've told you that this issue type was reserved to project Team Members. And we meant that. So, we're closing it now. Please choose an appropriate one.";

        private const string _issueArea = "nanoFramework area:";
        private const string _issueFeatureRequest = "### Is your feature request related to a problem?";
        private const string _issueTargetId = "**Target:";
        private const string _issueFwVersion = "**Firmware image version:";
        private const string _issueDeviceCaps = "**Device capabilities output:";
        private const string _issueDescription = "### Description";

        // strings for PR content
        private const string _prDescription = "## Description";
        private const string _prMotivationAndContext = "## Motivation and Context";
        private const string _prTypesOfChanges = "## Types of changes";
        private const string _prChecklist = "## Checklist";

        // marker placed (hidden) at the end of the PR template validation review
        // so subsequent runs can locate / update it. DO NOT change this string
        // without a migration plan, otherwise old reviews will be orphaned.
        private const string _prTemplateValidationMarker = "<!-- nfbot pr-template-validation DO NOT REMOVE -->";

        // link to the canonical org template (shown to the user on validation failure)
        private const string _prTemplateOrgUrl = "https://github.com/nanoframework/.github/blob/main/PULL_REQUEST_TEMPLATE.md";

        // labels
        private const string _labelConfigAndBuildName = "Area: Config-and-Build";
        private const string _labelDevContainersName = "Area: Dev-Containers";
        private const string _labelBreakingChangeName = "Breaking-change";

        private const string _labelCiUpdateDependentsName = "CI: Update Dependents";
        private const string _labelCiPublishReleaseName = "CI: Publish Release";
        private const string _labelDontMergeName = ":warning: DO NOT MERGE :warning:";
        private const string _labelCiSkipBuildName = "CI: SKIP BUILD";

        private const string _labelTypeDependenciesName = "Type: dependencies";
        private const string _labelTypeFeatureRequestName = "Type: Feature Request";
        private const string _labelTypeBugName = "Type: bug";
        private const string _labelTypeEnhancementName = "Type: enhancement";
        private const string _labelTypeUnitTestsName = "Type: Unit Tests";
        private const string _labelTypeChoresName = "Type: Chores";

        private const string _labelStatusWaitingTriageName = "Status: Waiting triage";
        private const string _labelDocumentationName = "Type: documentation";
        private const string _labelStatusMissingProjectToReproduce = "Status: missing project to reproduce";

        private const string _labelInvalidName = "invalid";
        private const string _labelUpForGrabs = "up-for-grabs";

        // this is the TOPIC that has to exist on repositories for nano tools
        private const string _topicNanoTool = "i-am-a-nano-tool";

        private const string _tagVersionUpdate = "[version update]";

        // if author is member or owner
        private static List<string> _rolesWithProjectOrgPermissions = new List<string>() { "COLLABORATOR", "MEMBER", "OWNER" };

        // DevOps client
        private const string _nfOrganizationUrl = "https://dev.azure.com/nanoframework";

        // GitHub stuff
        private const string _gitOwner = "nanoframework";

        static GitHubClient _octokitClient = new GitHubClient(new Octokit.ProductHeaderValue("nfbot"));

        [FunctionName("GitHub-nfbot")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("GitHub nfbot processing request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic payload = JsonConvert.DeserializeObject(requestBody);

            // setup OctoKit authentication
            _octokitClient.Credentials = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

            #region process PR events

            // process PR (make sure it's not a PR review)
            if (payload.pull_request != null && payload.review == null)
            {
                // PR opened or edited
                if (payload.action == "opened" ||
                    payload.action == "edited")
                {
                    log.LogInformation($"Processing PR #{payload.pull_request.number}:{payload.pull_request.title} submitted by {payload.pull_request.user.login}");

                    ////////////////////////////////////////////////////////////
                    // processing exceptions

                    // any BOT (check login suffix or user type)
                    if (payload.pull_request.user.login.ToString().EndsWith("[bot]") ||
                        payload.pull_request.user.type?.ToString().Equals("Bot", StringComparison.OrdinalIgnoreCase) == true ||
                        payload.pull_request.user.login.ToString().Equals("copilot", StringComparison.OrdinalIgnoreCase))
                    {
                        return new OkObjectResult("");
                    }
                    ////////////////////////////////////////////////////////////

                    // check for PR in release branch
                    // need to get PR head as JObject to access the 'ref' property because it's a C# keyword
                    JObject prHead = payload.pull_request.head;

                    // special processing for nfbot & github-actions[bot] commits
                    if (payload.pull_request.user.login == "nfbot" ||
                        payload.pull_request.user.login == "github-actions[bot]")
                    {
                        // get PR
                        Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                        if (pr.Body.Contains(_tagVersionUpdate))
                        {
                            // this is a [version update] commit

                            log.LogInformation($"Adding {_labelTypeDependenciesName} label to PR.");

                            // add the Type: dependency label
                            await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeDependenciesName });
                        }
                        else if (prHead["ref"].ToString().StartsWith("release-"))
                        {
                            // this is a release candidate PR

                            // add the Publish release label
                            await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelCiPublishReleaseName });
                        }
                    }
                    else
                    {
                        // get PR
                        Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                        // fix title if needed
                        if (pr.Title.EndsWith("."))
                        {
                            var fixedPr = new PullRequestUpdate
                            {
                                Title = pr.Title.Substring(0, pr.Title.Length - 1)
                            };

                            pr = await _octokitClient.PullRequest.Update(pr.Base.Repository.Id, pr.Number, fixedPr);
                        }

                        await FixCheckListAsync(payload, log);

                        // check for PR ignoring template
                        if (await ValidatePRContentAsync(payload, log))
                        {
                            // post comment with thank you message if this is a new PR
                            if (payload.action == "opened")
                            {
                                // check if author is 1st time contributor here
                                if (payload.pull_request.author_association == "FIRST_TIME_CONTRIBUTOR")
                                {
                                    log.LogInformation($"Comment with thank you note.");

                                    string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nA human will be reviewing it shortly. :wink:\" }}";

                                    await SendGitHubRequest(
                                        payload.pull_request.comments_url.ToString(),
                                        comment,
                                        log);
                                }

                                // add thumbs up reaction in PR main message
                                await SendGitHubRequest(
                                    $"{payload.pull_request.issue_url.ToString()}/reactions",
                                    "{ \"content\" : \"+1\" }",
                                    log,
                                    "application/vnd.github.squirrel-girl-preview");
                            }
                        }

                        bool linkedIssuesReference = await CheckLinkedIssuesAsync(pr, log);

                        await ManageLabelsAsync(pr, log);

                        if (linkedIssuesReference)
                        {
                            // everything looks OK, remove all comments from nfbot, if any
                            if (pr.Comments > 0)
                            {
                                await RemovenfbotCommentsAsync(
                                    pr,
                                    log);
                            }
                        }
                    }
                }

                // PR closed
                else if (payload.action == "closed")
                {
                    log.LogInformation($"Processing PR #{payload.pull_request.number} closed event...");

                    // get PR
                    Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                    // check for PR authored by nfbot or git-actions bot
                    if (pr.User.Login == "nfbot" ||
                        pr.User.Login == "github-actions[bot]")
                    {
                        // get origin branch
                        var originBranch = payload.pull_request.head.label.ToString().Replace("nanoframework:", "");

                        // delete this branch

                        log.LogInformation($"Deleting URL \"heads/{originBranch}\"");

                        await _octokitClient.Git.Reference.Delete(_gitOwner, payload.repository.name.ToString(), $"heads/{originBranch}");

                        // was this PR updating versions?
                        if (pr.Body.Contains(_tagVersionUpdate))
                        {
                            log.LogInformation($"PR for version update, check similar PRs");

                            // grab all other open PRs at this repo
                            IReadOnlyList<Octokit.PullRequest> openPrs = await _octokitClient.PullRequest.GetAllForRepository(
                                _gitOwner,
                                payload.repository.name.ToString(),
                                new PullRequestRequest() { State = ItemStateFilter.Open });

                            log.LogInformation($"Found {openPrs.Count} open PRs");

                            var currentBaseBranch = pr.Base.Ref;

                            // filter PRs created by our bots, only about version updates and earlier than the current PR
                            // include only PRs targeting the same base branch (main/develop/etc.)
                            foreach (var pull in openPrs.Where(
                                p => (p.User.Login == "nfbot"
                                || p.User.Login == "github-actions[bot]")
                                && p.Body.Contains(_tagVersionUpdate)
                                && p.Number < pr.Number
                                && string.Equals(p.Base.Ref, currentBaseBranch, StringComparison.OrdinalIgnoreCase)))
                            {
                                log.LogInformation($"Closing PR {pull.Number} targeting branch '{currentBaseBranch}'");

                                await _octokitClient.PullRequest.Update(
                                    _gitOwner,
                                    payload.repository.name.ToString(),
                                    pull.Number,
                                    new PullRequestUpdate() { State = ItemState.Closed });
                            }
                        }
                    }

                    // developer note: hang in there a few seconds before checking if the PR was merged
                    // Occasionally the merge information it's a bit delayed when pulled from the API causing the PR to be miss labeled
                    await Task.Delay(TimeSpan.FromSeconds(3));

                    // was the PR merged?
                    if (await _octokitClient.PullRequest.Merged(_gitOwner, payload.repository.name.ToString(), (int)payload.number))
                    {
                        // yes, check contributors list

                        // skip if user it's a bot or Copilot
                        if (pr.User.Login == "nfbot" ||
                            pr.User.Login.EndsWith("[bot]") ||
                            pr.User.Type.Value == AccountType.Bot ||
                            pr.User.Login.Equals("copilot", StringComparison.OrdinalIgnoreCase))
                        {
                            // nothing to do here
                        }
                        else
                        {
                            // grab contributors list
                            string contributorsContent = UTF8Encoding.UTF8.GetString(await _octokitClient.Repository.Content.GetRawContent(_gitOwner, "Home", "CONTRIBUTORS.md"));

                            // try to find the user there
                            if (!contributorsContent.Contains($"https://github.com/{pr.User.Login}"))
                            {
                                // get user name
                                var userDetails = await _octokitClient.User.Get(pr.User.Login);

                                // isn't there, send a message inviting to self add
                                var commentContent = $"\\r\\n@{pr.User.Login} thank you again for your contribution! :pray::smile:\\r\\n\\r\\n.NET nanoFramework is all about community involvement, and no contribution is too small.\\r\\nWe would like to invite you to join the project's [Contributors list](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md).\\r\\n\\r\\nPlease edit it and add an entry with your GitHub username in the appropriate location (names are sorted alphabetically):\\r\\n```text\\r\\n  <tr>\\r\\n    <td><img src=\\\"https://github.com/{pr.User.Login}.png?size=50\\\" height=\\\"50\\\" width=\\\"50\\\" ></td>\\r\\n    <td><a href=\\\"https://github.com/{pr.User.Login}\\\">{userDetails.Name}</a></td>\\r\\n  </tr>\\r\\n```\\r\\n\\r\\n(Feel free to adjust your name if it's not correct)";

                                string comment = $"{{ \"body\": \"{commentContent}\" }}";

                                await SendGitHubRequest(
                                    payload.pull_request.comments_url.ToString(),
                                    comment,
                                    log);
                            }
                        }
                    }
                    else
                    {
                        // this branch was deleted without being merged, mark as invalid

                        // clear all labels
                        await _octokitClient.Issue.Labels.RemoveAllFromIssue(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                        // add the invalid label
                        await _octokitClient.Issue.Labels.AddToIssue(_gitOwner, payload.repository.name.ToString(), (int)payload.number, new string[] { _labelInvalidName });
                    }

                    return new OkObjectResult("");
                }
                else if (payload.action == "synchronize")
                {
                    // get PR
                    Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                    // get commits for this PR
                    ReadOnlyCollection<Octokit.PullRequestCommit> prCommits = await _octokitClient.PullRequest.Commits(_gitOwner, payload.repository.name.ToString(), (int)payload.number);
                    var commit = prCommits.First(c => c.Sha == payload.after.ToString());

                    ReadOnlyCollection<Octokit.Branch> branches = await _octokitClient.Repository.Branch.GetAll(_gitOwner, payload.repository.name.ToString());
                    var branchesClangFix = branches.Where(b => b.Name.StartsWith("nfbot/clang-format-fix"));

                    // get PR commit at HEAD
                    GitHubCommit prCommitAtHead = await _octokitClient.Repository.Commit.Get((int)payload.pull_request.head.repo.id, commit.Sha);

                    foreach (var branch in branchesClangFix)
                    {
                        GitHubCommit commitForClangFix = await _octokitClient.Repository.Commit.Get(_gitOwner, payload.repository.name.ToString(), branch.Commit.Sha);

                        if (prCommitAtHead.Parents[0].Sha == commitForClangFix.Parents[0].Sha)
                        {
                            // found the branch
                            await _octokitClient.Git.Reference.Delete(_gitOwner, payload.repository.name.ToString(), $"heads/{branch.Name}");

                            // done here
                            break;
                        }
                    }

                    if (branchesClangFix.Any())
                    {
                        // remove nfbot comments with the code style fixes
                        if (pr.Comments > 0)
                        {
                            await RemovenfbotCommentsAsync(pr, log);
                        }
                    }
                }
            }

            #endregion

            #region process issues

            else if (payload.issue != null)
            {
                log.LogInformation($"Processing issue #{payload.issue.number} '{payload.issue.title}' submitted by {payload.issue.user.login}");

                // get issue
                Octokit.Issue issue = await _octokitClient.Issue.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.issue.number);
                long repositoryId = (long)payload.repository.id;

                if ((payload.action == "opened" ||
                      payload.action == "edited" ||
                      payload.action == "reopened") &&
                      payload.comment == null)
                {
                    return await ProcessOpenOrEditIssueAsync(
                        issue,
                        repositoryId,
                        payload,
                        log, _octokitClient);
                }
                else if (payload.action == "closed")
                {
                    return await ProcessClosedIssueAsync(
                        issue,
                        repositoryId,
                        payload,
                        log);
                }
                else if (
                    payload.action == "created" &&
                    payload.comment != null)
                {
                    log.LogInformation($"Processing command");

                    // this is a comment on an issue or PR
                    // check for command to nfbot
                    if (payload.comment.body.ToString().StartsWith("@nfbot"))
                    {
                        // sender if member
                        // flag if author is member or owner
                        if (payload.comment.author_association == "MEMBER" || payload.issue.author_association == "OWNER")
                        {
                            StartReleaseResult processResult = await ProcessCommandAsync(payload, log);

                            if (processResult == StartReleaseResult.Executed)
                            {
                                // add thumbs up reaction to comment
                                await _octokitClient.Reaction.IssueComment.Create(repositoryId, (long)payload.comment.id.Value, new NewReaction(ReactionType.Plus1));
                            }
                            else if (processResult == StartReleaseResult.Started)
                            {
                                // add rocket reaction to comment
                                await _octokitClient.Reaction.IssueComment.Create(repositoryId, (long)payload.comment.id.Value, new NewReaction(ReactionType.Rocket));
                            }
                            else if (processResult == StartReleaseResult.WatchoutConditions)
                            {
                                // add eyes reaction to comment
                                await _octokitClient.Reaction.IssueComment.Create(repositoryId, (long)payload.comment.id.Value, new NewReaction(ReactionType.Eyes));
                            }
                            else
                            {
                                // add confused reaction to comment
                                await _octokitClient.Reaction.IssueComment.Create(repositoryId, (long)payload.comment.id.Value, new NewReaction(ReactionType.Confused));
                            }
                        }
                        else
                        {
                            log.LogInformation($"User has no permission to execute command");

                            // add thumbs down reaction to comment
                            await _octokitClient.Reaction.IssueComment.Create(repositoryId, (long)payload.comment.id.Value, new NewReaction(ReactionType.Minus1));
                        }
                    }
                }
                else if (payload.action == "labeled")
                {
                    return await ProcessIssueLabeledAsync(
                        payload,
                        log);
                }
            }

            #endregion

            #region process review

            // process review
            else if (payload.review != null)
            {
                // submitted with approval
                if (payload.action == "submitted" && payload.review.state == "approved")
                {
                    // check for PR in release branch
                    // need to get PR head as JObject to access the 'ref' property because it's a C# keyword
                    JObject prHead = payload.pull_request.head;

                    if (prHead["ref"].ToString().StartsWith("release-"))
                    {
                        // get PR combined status 
                        var prStatus = await GetGitHubRequest($"{payload.pull_request.head.repo.url.ToString()}/commits/{payload.review.commit_id}/status", log);

                        // get status checks for PR
                        var checkSatus = await GetGitHubRequest($"{payload.pull_request.head.repo.url.ToString()}/commits/{payload.pull_request.head.sha}/check-runs", log);

                        bool allChecksSuccessfull = false;

                        // iterate through all check status
                        foreach (dynamic cr in (JArray)checkSatus.check_runs)
                        {
                            if (cr.conclusion == "success")
                            {
                                // check pass
                                allChecksSuccessfull = true;
                            }
                            else
                            {
                                // check failed, don't bother check others
                                allChecksSuccessfull = false;
                                break;
                            }
                        }

                        if (allChecksSuccessfull)
                        {
                            // check if the CI-PublishRelease is set 
                            JArray prLabels = payload.pull_request.labels;

                            if (prLabels.Count(l => l["name"].ToString() == _labelCiPublishReleaseName) > 0)
                            {
                                // all checks are successful
                                // PR flaged to Publish Release
                                // merge PR
                                await MergePR(payload.pull_request, log);

                                // need to pause a bit to allow merge to settle
                                await Task.Delay(5000);

                                // get repository
                                string repositoryName = payload.repository.name.ToString();
                                // clear known prefixes
                                repositoryName = repositoryName.Replace("lib-", "");

                                // after merge to main need to queue a build
                                await QueueBuildAsync(repositoryName, "main", log);
                            }
                        }
                    }
                }
            }

            #endregion

            #region process check run

            else if (
                (payload.check_run != null && payload.check_run.conclusion == "success") ||
                (payload.state != null && payload.state == "success"))
            {
                string prSha = null;

                if (payload.check_run != null)
                {
                    // serious candidate of a PR check
                    log.LogInformation($"Processing check success event...");

                    // get SHA
                    prSha = payload.check_run.head_sha.ToString();
                }
                else if (payload.state != null)
                {
                    // serious candidate of a PR state
                    log.LogInformation($"Processing state success event...");

                    // get SHA
                    prSha = payload.sha.ToString();
                }

                // list all open PRs...
                IReadOnlyList<Octokit.PullRequest> openPRs = await _octokitClient.PullRequest.GetAllForRepository(
                    _gitOwner,
                    payload.repository.name.ToString(),
                    new PullRequestRequest());

                // ... filter the ones from nfbot
                Octokit.PullRequest matchingPr = openPRs.FirstOrDefault(
                    p => p.User.Login == "nfbot" &&
                    p.Head.Sha == prSha);

                if (matchingPr == null)
                {
                    // ... try now with github-actions 
                    matchingPr = openPRs.FirstOrDefault(
                    p => p.User.Login == "github-actions[bot]" &&
                    p.Head.Sha == prSha);
                }

                // DEBUG helper
#if DEBUG
                var prToForceTesting = 20;
                matchingPr = await _octokitClient.PullRequest.Get(
                    _gitOwner,
                    payload.repository.name.ToString(),
                    prToForceTesting);
#endif

                if (matchingPr != null)
                {
                    // get PR
                    Octokit.PullRequest pr = await _octokitClient.PullRequest.Get((long)payload.repository.id, matchingPr.Number);

                    bool isVersionUpdate = pr.Body.Contains(_tagVersionUpdate);
                    bool isReleaseCandidate = pr.Body.Contains("[release candidate]");

                    // check if PR it's a version update
                    if ((pr.User.Login == "nfbot"
                         || pr.User.Login == "github-actions[bot]") &&
                         (isVersionUpdate || isReleaseCandidate))
                    {
                        bool skipCIBuild = false;

                        // get required check runs for this branch
                        BranchProtectionRequiredStatusChecks statusChecks = await _octokitClient.Repository.Branch.GetRequiredStatusChecks((long)payload.repository.id, pr.Base.Ref);

                        // get status checks for PR
                        CheckRunsResponse checkRunStatus = await _octokitClient.Check.Run.GetAllForReference(_gitOwner, payload.repository.name.ToString(), pr.Head.Sha);

                        if (!checkRunStatus.CheckRuns.Any(cr => cr.Conclusion.Value != CheckConclusion.Success))
                        {
                            // all check runs are successful
                            if (checkRunStatus.TotalCount >= statusChecks.Contexts.Count)
                            {
                                int checksCount = 0;

                                foreach (var statusCheck in statusChecks.Contexts)
                                {
                                    if (checkRunStatus.CheckRuns.Any(c => c.Name == statusCheck))
                                    {
                                        checksCount++;
                                    }
                                    else
                                    {
                                        log.LogInformation($"Status check {statusCheck} not reported as successful");
                                    }
                                }

                                if (checksCount >= statusChecks.Contexts.Count)
                                {
                                    // get labels for this PR
                                    List<Label> prLabels = (List<Label>)pr.Labels;

                                    // check if this is running on samples repo
                                    if (!pr.HtmlUrl.ToString().Contains("nanoframework/Samples"))
                                    {
                                        // other repository

                                        // default is TO PUBLISH a new release 
                                        bool publishReleaseFlag = true;

                                        // check if this was a dependencies update OR a version update
                                        var dependenciesLabel = prLabels.FirstOrDefault(l => l.Name.ToString() == _labelTypeDependenciesName);
                                        if (dependenciesLabel != null
                                            || isVersionUpdate)
                                        {
                                            // this is a dependencies/version update PR
                                            var repo = await _octokitClient.Repository.Get(matchingPr.Head.Repository.Id);

                                            if (repo.Topics.Contains(_topicNanoTool))
                                            {
                                                // this PR belongs to a tool repository
                                                // nothing to do here
                                                log.LogInformation($"PR belongs to nano tool, no more processing required.");
                                            }
                                            else
                                            {
                                                // !! for libraries only!!

                                                // get files changed in this PR
                                                IReadOnlyList<PullRequestFile> prFiles = await _octokitClient.PullRequest.Files(_gitOwner, payload.repository.name.ToString(), (int)pr.Number);

                                                // check if nuspec was changed (reason to publish a new release)
                                                // otherwise skip as they are either in the Test or Benchmark or development dependencies
                                                var nuspecWasChanged = prFiles.Any(f => f.FileName.EndsWith(".nuspec"));

                                                if (!nuspecWasChanged)
                                                {
                                                    log.LogInformation($"nuspec wasn't changed skipping CI build.");

                                                    // DON'T publish a new release
                                                    publishReleaseFlag = false;

                                                    // skip build
                                                    skipCIBuild = true;
                                                }
                                            }
                                        }

                                        // add publish release, unless labeled with SKIP BUILD
                                        if (publishReleaseFlag &&
                                            !prLabels.Any(l => l.Name.ToString() == _labelCiSkipBuildName))
                                        {
                                            // add publish release label

                                            log.LogInformation($"Adding 'Publish release flag to PR.");

                                            // add the Publish release label
                                            await _octokitClient.Issue.Labels.AddToIssue(_gitOwner, payload.repository.name.ToString(), (int)pr.Number, new string[] { _labelCiPublishReleaseName });
                                        }

                                        // check for SKIP BUILD label
                                        if (prLabels.Any(l => l.Name.ToString() == _labelCiSkipBuildName))
                                        {
                                            // set this, no matter what
                                            skipCIBuild = true;
                                        }
                                    }

                                    // all checks completed successfully
                                    // merge PR, unless labeled with DON'T MERGE
                                    if (prLabels.Any(l => l.Name.ToString() == _labelDontMergeName))
                                    {
                                        log.LogInformation("NOT merging PR because it's labeled with DON'T MERGE.");
                                    }
                                    else
                                    {
                                        // check if there is information about the mergeability of the PR
                                        // and, if there is, if it can be merged
                                        if (pr.Mergeable.HasValue && !pr.Mergeable.Value)
                                        {
                                            log.LogInformation($"PR can't be merged: {pr.MergeableState}");
                                        }
                                        else
                                        {
                                            // SQUASH if it's a regular PR
                                            // MERGE if it's a release candidate merge
                                            await MergePrWithStrategy(
                                                pr,
                                                skipCIBuild,
                                                log,
                                                isReleaseCandidate ? PullRequestMergeMethod.Merge : PullRequestMergeMethod.Squash);
                                        }
                                    }
                                }
                                else
                                {
                                    // not all required status check are missing, quit
                                    log.LogInformation($"Check runs still missing, no point checking any further...");
                                }
                            }
                            else
                            {
                                // still some missing, quit
                                log.LogInformation($"Check runs still missing, no point checking any further...");
                            }
                        }
                        else
                        {
                            // some are not success, quit
                            log.LogInformation("Some check runs are NOT success, no point checking any further...");

                        }
                    }
                    else if (pr.Base.Ref.ToString().StartsWith("release-"))
                    {
                        // PR it's a release branch

                        // get reuired check runs for this branch
                        BranchProtectionRequiredStatusChecks statusChecks = await _octokitClient.Repository.Branch.GetRequiredStatusChecks((long)payload.repository.id, pr.Base.Ref);

                        // get status checks for PR
                        CheckRunsResponse checkRunStatus = await _octokitClient.Check.Run.GetAllForReference(_gitOwner, payload.repository.name.ToString(), pr.Head.Sha);

                        // check if ALL check runs are in place
                        if (checkRunStatus.TotalCount == statusChecks.Contexts.Where(c => !c.Contains("license/cla")).Count())
                        {
                            // need an APPROVED review

                            // list PR reviews
                            var prReviews = await _octokitClient.PullRequest.Review.GetAll(_gitOwner, payload.repository.name.ToString(), (int)pr.Number);

                            bool prApproved = false;

                            // iterate through all reviews
                            foreach (dynamic review in prReviews)
                            {
                                if (review.State == "APPROVED")
                                {
                                    // approved
                                    prApproved = true;
                                    break;
                                }
                            }

                            if (prApproved)
                            {
                                // check if the CI-PublishRelease is set 
                                List<Label> prLabels = (List<Label>)pr.Labels;

                                if (prLabels.Any(l => l.Name.ToString() == _labelCiPublishReleaseName))
                                {
                                    // all checks are successful
                                    // PR flaged to Publish Release
                                    // merge PR
                                    await MergePR(pr, log);

                                    // need to pause a bit to allow merge to settle
                                    await Task.Delay(5000);

                                    // get repository
                                    string repositoryName = payload.repository.name.ToString();
                                    // clear known prefixes
                                    repositoryName = repositoryName.Replace("lib-", "");

                                    // after merge to main need to queue a build
                                    await QueueBuildAsync(repositoryName, "main", log);
                                }
                            }
                        }
                        else
                        {
                            // still some missing, quit
                            log.LogInformation($"Check runs still missing, no point checking any further...");
                        }
                    }
                }
            }

            #endregion

            #region process push event

            // process push
            else if (payload.commits != null && payload.pusher != null)
            {
                log.LogInformation($"Processing push event...");
            }

            #endregion

            #region process fork event 

            else if (payload.forkee != null)
            {
                // we have a new fork
                // send message to Slack channel

                log.LogInformation($"Processing new fork event from {payload.forkee.owner.login.ToString()}");

                //log.LogInformation($">> : {payload.forkee.name.ToString()}");

                var slackPayload = new
                {
                    text = $"GitHub user <{payload.forkee.owner.html_url.ToString()}|{payload.forkee.owner.login.ToString()}> has :fork_and_knife: {payload.forkee.name.ToString()}!",
                    icon_url = payload.forkee.owner.avatar_url.ToString(),
                };

                // Add the DISCORD_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
                // this is possible because Discord webhooks API supports Slack compatible webhooks
                // see https://discordapp.com/developers/docs/resources/webhook#execute-slackcompatible-webhook
                using (var client = new HttpClient())
                {
                    var res = await client.PostAsync(
                        Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL"),
                        new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
                        })
                    );
                }
            }

            #endregion

            #region process stared event 

            else if (payload.repository != null && payload.action == "created" && payload.starred_at != null)
            {
                // we have a user staring the repo
                // send message to Slack channel

                log.LogInformation($"Processing new repo starred event from {payload.sender.login.ToString()}");

                var slackPayload = new
                {
                    text = $"GitHub user <{payload.sender.html_url.ToString()}|{payload.sender.login.ToString()}> has :star: {payload.repository.name.ToString()}!",
                    icon_url = payload.sender.avatar_url.ToString(),
                };

                // Add the DISCORD_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
                // this is possible because Discord webhooks API supports Slack compatible webhooks
                // see https://discordapp.com/developers/docs/resources/webhook#execute-slackcompatible-webhook
                using var client = new HttpClient();
                var res = await client.PostAsync(
                Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL"),
                new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
                    })
                );
            }

            #endregion

            #region process sponsorship event 

            else if (payload.sponsorship != null
                     && payload.action == "created"
                     && payload.sponsorship.privacy_level == "public")
            {
                // we have a new PUBLIC sponsor
                // send message to Discord channel

                log.LogInformation($"Processing new sponsor contribution from {payload.sponsorship.sponsor.login.ToString()}");

                var slackPayload = new
                {
                    text = $"GitHub user <{payload.sender.html_url.ToString()}|{payload.sender.login.ToString()}> just sponsored the project with a {payload.sponsorship.tier.name.ToString()} contribution. Well done and thank you very much! :clap:",
                    icon_url = payload.sender.avatar_url.ToString(),
                };

                // Add the DISCORD_CONTRIBUTIONS_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
                // this is possible because Discord webhooks API supports Slack compatible webhooks
                // see https://discordapp.com/developers/docs/resources/webhook#execute-slackcompatible-webhook
                using HttpClient client = new();
                using HttpResponseMessage res = await client.PostAsync(
                    Environment.GetEnvironmentVariable("DISCORD_CONTRIBUTIONS_WEBHOOK_URL"),
                    new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
                        })
                );
            }

            #endregion

            return new OkObjectResult("");
        }

        private static async Task<IActionResult> ProcessIssueLabeledAsync(
            dynamic payload,
            ILogger log)
        {
            // check if label is "up-for-grabs"
            if (payload.label.name.ToString() == _labelUpForGrabs)
            {
                // compose message for Discord channel

                var slackPayload = new
                {
                    text = $":notepad_spiral: **{payload.issue.title.ToString()}** :notepad_spiral: \nThere's a new issue up for grabs! Please take a look:\nhttps://github.com/nanoframework/Home/issues/{payload.issue.number.ToString()} :wink:",
                    icon_url = "https://avatars.githubusercontent.com/u/25073645?v=4",
                };

                // Add the DISCORD_UP_FOR_GRABS_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
                // this is possible because Discord webhooks API supports Slack compatible webhooks
                // see https://docs.discord.com/developers/resources/webhook#execute-slackcompatible-webhook
                using var client = new HttpClient();
                var res = await client.PostAsync(
                    Environment.GetEnvironmentVariable("DISCORD_UP_FOR_GRABS_WEBHOOK_URL"),
                    new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
                        })
                );

                log.LogInformation($"Result from request to Discord API: {res.StatusCode}");

                if (!res.IsSuccessStatusCode)
                {
                    log.LogInformation($"Error message received: {res.ReasonPhrase}");
                }
            }
            else if (payload.label.name.ToString() == _labelStatusMissingProjectToReproduce)
            {
                log.LogInformation("Requesting project to reproduce the issue");

                // add comment to issue
                _ = await _octokitClient.Issue.Comment.Create(
                    (int)payload.repository.id,
                    (int)payload.issue.number,
                    $"@{payload.issue.user.login} {_missingProjectToReproduceComment}.");
            }
            else
            {
                log.LogInformation("Skipping event as this is NOT up-for-grabs");
            }

            return new OkObjectResult("");
        }

        private static async Task<StartReleaseResult> ProcessCommandAsync(dynamic payload, ILogger log)
        {
            // content has to follow this pattern:
            // @nfbot ccccc a1 a2 a3
            // start with nfbot
            // a single space
            // command
            // arguments (optional)

            // remove start of message
            string command = payload.comment.body.ToString().Substring("@nfbot ".Length);
            command = command.Trim();
            // clean up trailing CR and LF
            command = command.Replace("\r", "").Replace("\n", "");

            // get repository
            string repositoryName = payload.repository.name.ToString();
            // clear known prefixes
            repositoryName = repositoryName.Replace("lib-", "");

            // check commands
            if (command.EndsWith("startrelease"))
            {
                log.LogInformation($"Processing command");

                return await StartReleaseCandidateAsync(repositoryName, log);
            }
            else if (command.StartsWith("updatedependents"))
            {
                return await UpdateDependentsAsync(repositoryName, log);
            }
            else if (command.EndsWith("updatedependencies all"))
            {
                // add thumbs up reaction to comment to flag start
                await _octokitClient.Reaction.IssueComment.Create((long)payload.repository.id, (uint)payload.comment.id.Value, new NewReaction(ReactionType.Plus1));

                return await UpdateDependenciesAllReposAsync(log);
            }
            else if (command.EndsWith("updatedependencies"))
            {
                return await UpdateDependenciesAsync(
                    payload.repository.url.ToString(),
                    false,
                    log);
            }
            else if (command.EndsWith("updatedependencies develop"))
            {
                return await UpdateDependenciesAsync(
                    payload.repository.url.ToString(),
                    true,
                    log);
            }
            else if (command.StartsWith("runpipeline"))
            {
                // get branch name, if any

                string branchName = command.Substring("runpipeline".Length);

                if (!string.IsNullOrEmpty(branchName))
                {
                    // remove leading and trailing white spaces
                    branchName = branchName.Trim();
                }
                else
                {
                    // set to default branch name: 'main'
                    branchName = "main";
                }

                try
                {
                    return await QueueBuildAsync(repositoryName, branchName, log);
                }
                catch (Exception ex)
                {
                    log.LogError($"Error queuing build: {ex.Message}.");

                    return StartReleaseResult.Failed;
                }
            }
            else if (command.EndsWith("closeupdateprs"))
            {
                // add thumbs up reaction to comment to flag start
                await _octokitClient.Reaction.IssueComment.Create((long)payload.repository.id, (uint)payload.comment.id.Value, new NewReaction(ReactionType.Plus1));

                return await CloseUpdatePrsAsync(log);
            }

            // unknown or invalid command
            return StartReleaseResult.Unknwon;
        }

        private static async Task<StartReleaseResult> StartReleaseCandidateAsync(string repositoryName, ILogger log)
        {
            // check if nuspec has preview versions
            SearchCodeRequest nuspecQuery = new SearchCodeRequest();
            nuspecQuery.FileName = "*.nuspec";
            nuspecQuery.Repos.Add(_gitOwner, repositoryName);

            var nuspecFilesInRepo = await _octokitClient.Search.SearchCode(nuspecQuery);

            // sanity checks            
            if (nuspecFilesInRepo.TotalCount < 1)
            {
                return StartReleaseResult.Failed;
            }

            // filter out any DELIVERABLES nuspec
            var nuspecFiles = nuspecFilesInRepo.Items.Where(f => !f.Name.Contains("DELIVERABLES"));

            if (nuspecFiles.Count() < 1)
            {
                return StartReleaseResult.Failed;
            }

            foreach (var nuspecFile in nuspecFiles)
            {
                // get content of nuspec
                string nuspecContent = Encoding.UTF8.GetString(await _octokitClient.Repository.Content.GetRawContent(_gitOwner, repositoryName, nuspecFile.Path));

                if (nuspecContent.Contains("-preview"))
                {
                    // still have preview references
                    return StartReleaseResult.WatchoutConditions;
                }
            }

            var personalAccessToken = Environment.GetEnvironmentVariable("DEVOPS_PATOKEN", EnvironmentVariableTarget.Process);

            Uri nfOrganizationUri = new Uri(_nfOrganizationUrl);

            // Create a connection
            VssConnection connection = new VssConnection(
                nfOrganizationUri,
                new VssBasicCredential(
                    string.Empty,
                    personalAccessToken)
                );

            // Get an instance of the build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            var buildDefs = await buildClient.GetDefinitionsAsync(repositoryName);

            // we have a couple of projects with more than one pipeline so grab the 1st with name closest to the repository name
            if (buildDefs.Count() > 0)
            {
                // compose build request
                var buildRequest = new Build
                {
                    Definition = new DefinitionReference
                    {
                        Id = buildDefs[0].Id
                    },
                    Project = buildDefs[0].Project,
                    Parameters = "{\"StartReleaseCandidate\":\"true\"}"
                };

                try
                {
                    await buildClient.QueueBuildAsync(buildRequest);
                }
                catch (Exception ex)
                {
                    log.LogError($"Error queuing build: {ex.Message}.");

                    return StartReleaseResult.Failed;
                }

                return StartReleaseResult.Started;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return StartReleaseResult.Failed;
        }

        private static async Task<StartReleaseResult> UpdateDependentsAsync(string repositoryName, ILogger log)
        {
            var personalAccessToken = Environment.GetEnvironmentVariable("DEVOPS_PATOKEN", EnvironmentVariableTarget.Process);

            Uri nfOrganizationUri = new Uri(_nfOrganizationUrl);

            // Create a connection
            VssConnection connection = new VssConnection(
                nfOrganizationUri,
                new VssBasicCredential(
                    string.Empty,
                    personalAccessToken)
                );

            // Get an instance of the build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            var buildDefs = await buildClient.GetDefinitionsAsync(repositoryName);

            // so far we only have projects with a single build definitio so check this and take the 1st one
            if (buildDefs.Count == 1)
            {
                // compose build request
                var buildRequest = new Build
                {
                    Definition = new DefinitionReference
                    {
                        Id = buildDefs[0].Id
                    },
                    Project = buildDefs[0].Project,
                    Parameters = "{\"UPDATE_DEPENDENTS\":\"true\"}"
                };

                try
                {
                    await buildClient.QueueBuildAsync(buildRequest);
                }
                catch (Exception ex)
                {
                    log.LogError($"Error queuing build: {ex.Message}.");

                    return StartReleaseResult.Failed;
                }

                return StartReleaseResult.Started;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return StartReleaseResult.Failed;
        }

        private static async Task<StartReleaseResult> UpdateDependenciesAsync(
            string repoUrl,
            bool isDevelopBranch,
            ILogger log)
        {
            const string _regularUpdate = "update-dependencies";
            const string _developUpdate = "update-dependencies-develop";

            string requestContent = $"{{ \"event_type\": \"{(isDevelopBranch ? _developUpdate : _regularUpdate)}\" }}";

            int result = await SendGitHubRequest(
                repoUrl + "/dispatches",
                requestContent,
                log,
                "application/vnd.github.v3+json",
                "POST");

            if (result == 204)
            {
                return StartReleaseResult.Started;
            }

            return StartReleaseResult.Failed;
        }

        private static async Task<StartReleaseResult> UpdateDependenciesAllReposAsync(ILogger log)
        {
            string requestContent = $"{{ \"event_type\": \"update-dependencies\" }}";

            bool failFlag = false;

            // get all repos for all libraries
            var allRepos = await _octokitClient.Repository.GetAllForOrg(_gitOwner);

            // filter out:
            // samples, ST packages, ChibiOS, 
            var reposToProcess = allRepos.Where(r => r.Name.StartsWith("nanoFramework.")
                                                     || r.Name.StartsWith("System."));

            foreach (var repo in reposToProcess)
            {
                var result = await SendGitHubRequest(
                    repo.Url + "/dispatches",
                    requestContent,
                    log,
                    "application/vnd.github.v3+json",
                    "POST");

                if (result != 204)
                {
                    failFlag = false;
                }
            }

            if (failFlag)
            {
                return StartReleaseResult.Failed;
            }
            else
            {
                return StartReleaseResult.Started;
            }
        }

        private static async Task<StartReleaseResult> CloseUpdatePrsAsync(ILogger log)
        {
            bool failFlag = false;

            // get all repos for all libraries
            var allRepos = await _octokitClient.Repository.GetAllForOrg(_gitOwner);

            // filter out:
            // samples, ST packages, ChibiOS, 
            var reposToProcess = allRepos.Where(r => r.Name.StartsWith("nanoFramework.")
                                                     || r.Name.StartsWith("System."));

            foreach (var repo in reposToProcess)
            {
                try
                {
                    // get open PRs
                    var openPRs = await _octokitClient.PullRequest.GetAllForRepository(_gitOwner, repo.Name, new PullRequestRequest() { State = ItemStateFilter.Open });

                    var updatePRs = openPRs.Where(pr => pr.Labels.Any(l => l.Name == _labelTypeDependenciesName) && pr.Body.Contains(_tagVersionUpdate));

                    if (updatePRs.Any())
                    {
                        var prNumber = updatePRs.First().Number;

                        _ = await _octokitClient.PullRequest.Update(
                                      _gitOwner,
                                      repo.Name,
                                      prNumber,
                                      new PullRequestUpdate() { State = ItemState.Closed });
                    }
                }
                catch
                {
                    // something went wrong!
                    log.LogError($"Error when trying to delete update PRs in {repo.Name}");

                    failFlag = true;
                }
            }

            if (failFlag)
            {
                return StartReleaseResult.Failed;
            }
            else
            {
                return StartReleaseResult.Started;
            }
        }

        private static async Task<StartReleaseResult> QueueBuildAsync(string repositoryName, string branchName, ILogger log)
        {
            var personalAccessToken = Environment.GetEnvironmentVariable("DEVOPS_PATOKEN", EnvironmentVariableTarget.Process);

            Uri nfOrganizationUri = new Uri(_nfOrganizationUrl);

            // Create a connection
            VssConnection connection = new VssConnection(
                nfOrganizationUri,
                new VssBasicCredential(
                    string.Empty,
                    personalAccessToken)
                );

            // Get an instance of the build client
            BuildHttpClient buildClient = connection.GetClient<BuildHttpClient>();

            var buildDefs = await buildClient.GetDefinitionsAsync(repositoryName);

            // so far we only have projects with a single build definitio so check this and take the 1st one
            if (buildDefs.Count == 1)
            {
                // compose build request
                var buildRequest = new Build
                {
                    Definition = new DefinitionReference
                    {
                        Id = buildDefs[0].Id
                    },
                    Project = buildDefs[0].Project,
                    SourceBranch = branchName
                };

                await buildClient.QueueBuildAsync(buildRequest);

                return StartReleaseResult.Started;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return StartReleaseResult.Failed;
        }

        private static async Task ManageLabelsAsync(Octokit.PullRequest pr, ILogger log)
        {
            // go through the check list and add the respective labels

            if (pr.Body.Contains("[x] Bug fix", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeBugName });
            }

            if (
                pr.Body.Contains("[x] Improvement", StringComparison.InvariantCultureIgnoreCase) ||
                pr.Body.Contains("[x] New feature", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeEnhancementName });
            }

            if (pr.Body.Contains("[x] Breaking change", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelBreakingChangeName });
            }

            if (pr.Body.Contains("[x] Config and build", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelConfigAndBuildName });
            }

            if (pr.Body.Contains("[x] Dev Containers", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelDevContainersName });
            }

            if (pr.Body.Contains("[x] Dependencies", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeDependenciesName });
            }

            if (pr.Body.Contains("[x] Unit Tests", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeUnitTestsName });
            }

            if (pr.Body.Contains("[x] Documentation", StringComparison.InvariantCultureIgnoreCase))
            {
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelDocumentationName });
            }
        }

        private static async Task<bool> CheckLinkedIssuesAsync(Octokit.PullRequest pr, ILogger log)
        {
            string commentContent = string.Empty;

            // check for invalid link to issues
            if (pr.Body.Contains("- Fixes/Closes/Resolves nanoFramework/Home#NNNN"))
            {
                commentContent = ":disappointed: If this PR does address any issue, you have to remove the content *Fixes/Closes/Resolves(...)* under 'Motivation and Context'";
            }
            else if (pr.Body.Contains("Fixes/Closes/Resolves"))
            {
                commentContent = "🤪 You have to make up your mind on how this PR addresses the issue. It either **fixes**, **closes** or **resolves** it. Can't have them all...";
            }
            else
            {
                // Define the regex pattern to match the GitHub link verbs and the pattern KEYWORD #NNNN
                string pattern = @"\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+#\d+\b";
                string validPattern = @"\b(close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved)\s+nanoFramework/Home#\d+\b";

                // Find all matches of the invalid pattern
                var matches = Regex.Matches(pr.Body, pattern);

                foreach (Match match in matches)
                {
                    // Check if the match is not a valid pattern
                    if (!Regex.IsMatch(match.Value, validPattern))
                    {
                        commentContent = ":disappointed: All our issues are tracked in Home repo. If this PR addresses an issue, make sure the reference to it follows the correct pattern: `nanoFramework/Home#NNNN`.";
                        break;
                    }
                }

                // any comment content?
                if (string.IsNullOrEmpty(commentContent))
                {
                    // got here, so all is good
                    return true;
                }
            }

            await _octokitClient.Issue.Comment.Create(pr.Base.Repository.Id, pr.Number, $"Hi @{pr.User.Login},\r\n\r\n{commentContent}.{_fixRequestTagComment}");

            return false;
        }

        private static async Task FixCheckListAsync(dynamic payload, ILogger log)
        {
            // get PR body
            string prBody = payload.pull_request.body;

            // save hash for PR body
            var prBodyHash = prBody.GetHashCode();

            // fix any typos in check lists
            // Define a regex pattern to match any character inside the brackets, including optional spaces
            string badCheckBoxesPattern = @"\[\s*[^\s\]]\s*\]";

            // Replace all matches with "[x]"
            string prBodyFixed = Regex.Replace(prBody, badCheckBoxesPattern, "[x]");

            if (prBodyHash != prBodyFixed.GetHashCode())
            {
                dynamic requestContent = new { body = prBodyFixed };

                await SendGitHubRequest(
                    payload.pull_request.url.ToString(),
                    JsonConvert.SerializeObject(requestContent),
                    log,
                    null,
                    "PATCH");

                // add comment with information to the user
                string comment = $"{{ \"body\": \"@{payload.pull_request.user.login} {_fixCheckListComment}\" }}";

                await SendGitHubRequest(
                    payload.pull_request.comments_url.ToString(),
                    comment,
                    log);
            }
        }

        private static async Task<bool> ValidatePRContentAsync(dynamic payload, ILogger log)
        {
            // get PR body
            string prBody = payload.pull_request.body ?? string.Empty;
            string repoName = payload.repository.name.ToString();
            long repoId = (long)payload.repository.id;
            int prNumber = (int)payload.pull_request.number;
            string author = payload.pull_request.user.login.ToString();

            // documentation repo is not using template
            if (repoName == "nanoframework.github.io")
            {
                // don't perform any template check here
                return true;
            }

            // community targets uses a per-target checkbox flow (legacy behavior preserved)
            if (repoName == "nf-Community-Targets")
            {
                // community targets need to have ALL or at least one target selected for build
                if (
                    prBody.Contains("[x] MBN_QUAIL") ||
                    prBody.Contains("[x] GHI_FEZ_CERB40_NF") ||
                    prBody.Contains("[x] I2M_ELECTRON_NF") ||
                    prBody.Contains("[x] I2M_OXYGEN_NF") ||
                    prBody.Contains("[x] ST_NUCLEO64_F401RE_NF") ||
                    prBody.Contains("[x] ST_NUCLEO64_F411RE_NF") ||
                    prBody.Contains("[x] ST_STM32F411_DISCOVERY") ||
                    prBody.Contains("[x] ST_NUCLEO144_F412ZG_NF") ||
                    prBody.Contains("[x] ST_NUCLEO144_F746ZG") ||
                    prBody.Contains("[x] ST_STM32F4_DISCOVERY") ||
                    prBody.Contains("[x] ST_NUCLEO144_F439ZI") ||
                    prBody.Contains("[x] TI_CC1352P1_LAUNCHXL") ||
                    prBody.Contains("[x] ALL"))
                {
                    // should be OK
                    return true;
                }
                else
                {
                    string myComment = $"{{ \"body\": \"{_prCommunityTargetMissingTargetContent} {_fixCheckListComment}\" }}";

                    await SendGitHubRequest(
                        payload.pull_request.comments_url.ToString(),
                        myComment,
                        log);

                    // legacy behavior preserved for nf-Community-Targets (do NOT close)
                    return false;
                }
            }

            // community contributions uses a checklist-only template
            if (repoName == "nf-Community-Contributions")
            {
                if (prBody.Contains(_prChecklist))
                {
                    // check for missing check boxes (un-ticked items)
                    if (prBody.Contains("[ ]"))
                    {
                        await _octokitClient.Issue.Comment.Create(
                            (int)repoId,
                            prNumber,
                            $"Hi @{author},\r\n\r\n{_prCommentChecklistWithOpenItemsTemplateContent}.{_fixRequestTagComment}");
                    }

                    return true;
                }

                // checklist section missing -> fall through to org-default-style review below
            }

            // dispatch to the descriptor matching this repo (org default fallback)
            PrTemplate template = PrTemplate.ForRepo(repoName);

            PrTemplateValidationResult result = ValidateAgainstTemplate(prBody, template);

            if (result.IsValid)
            {
                // if a previous validation review exists, mark it resolved (edit body)
                await ResolveTemplateValidationReviewIfAnyAsync(repoName, prNumber, log);
                return true;
            }

            log.LogInformation($"PR template validation failed for #{prNumber} in {repoName}: {result.ProblemSummary()}");

            string reviewBody = BuildTemplateValidationReviewBody(author, template, result);

            await PostOrUpdateTemplateValidationReviewAsync(repoName, prNumber, reviewBody, log);

            // NOTE: per policy, we do NOT close the PR on template-validation failure.
            return false;
        }

        /// <summary>
        /// PR template validation: descriptors, parsing helpers, review post/update.
        /// The org has several PR templates in use.
        /// Each one has different required "Types of changes" / "Checklist"
        /// checkbox lines, so we model them as descriptors and dispatch per repo.
        /// </summary>
        private sealed class PrTemplate
        {
            public string Name { get; }
            public string[] RequiredSections { get; }
            public string[] OptionalSections { get; }
            // Identifying prefix of each canonical "Types of changes" checkbox line.
            // We match the prefix (the text before the first `(`) case-insensitively so
            // the descriptor is robust to small wording tweaks in the parenthesis.
            public string[] TypesOfChangesLines { get; }
            // Same idea for the "Checklist" section.
            public string[] ChecklistLines { get; }
            // If true, at least one "Types of changes" checkbox must be ticked.
            public bool RequireAtLeastOneTypeTicked { get; }

            private PrTemplate(
                string name,
                string[] requiredSections,
                string[] optionalSections,
                string[] typesOfChangesLines,
                string[] checklistLines,
                bool requireAtLeastOneTypeTicked)
            {
                Name = name;
                RequiredSections = requiredSections;
                OptionalSections = optionalSections;
                TypesOfChangesLines = typesOfChangesLines;
                ChecklistLines = checklistLines;
                RequireAtLeastOneTypeTicked = requireAtLeastOneTypeTicked;
            }

            // Canonical org template (nanoframework/.github/PULL_REQUEST_TEMPLATE.md).
            // Used as the fallback for every repo that does not ship its own template.
            public static readonly PrTemplate OrgDefault = new PrTemplate(
                name: "OrgDefault",
                requiredSections: new[] { "Description", "Motivation and Context", "Types of changes", "Checklist" },
                optionalSections: new[] { "How Has This Been Tested?", "Screenshots" },
                typesOfChangesLines: new[]
                {
                    "Improvement",
                    "Bug fix",
                    "New feature",
                    "Breaking change",
                    "Config and build",
                    "Dependencies",
                    "Unit Tests",
                    "Documentation",
                },
                checklistLines: new[]
                {
                    "My code follows the code style of this project",
                    "My changes require an update to the documentation",
                    "I have updated the documentation accordingly",
                    "I have read the",
                    "I have tested everything locally",
                    "I have added new tests to cover my changes",
                },
                requireAtLeastOneTypeTicked: true);

            // nf-interpreter ships its own local template
            public static readonly PrTemplate NfInterpreter = new PrTemplate(
                name: "NfInterpreter",
                requiredSections: new[] { "Description", "Motivation and Context", "Types of changes", "Checklist" },
                optionalSections: new[] { "How Has This Been Tested?", "Screenshots" },
                typesOfChangesLines: new[]
                {
                    "Improvement",
                    "Bug fix",
                    "New feature",
                    "Breaking change",
                    "Config and build",
                    "Dev Containers",
                    "Dependencies/declarations",
                    "Documentation",
                },
                checklistLines: new[]
                {
                    "My code follows the code style of this project",
                    "My changes require an update to the documentation",
                    "I have updated the documentation accordingly",
                    "I have read the",
                    "I have tested everything locally",
                },
                requireAtLeastOneTypeTicked: true);

            // Home repo template
            public static readonly PrTemplate Home = new PrTemplate(
                name: "Home",
                requiredSections: new[] { "Description", "Motivation and Context", "Types of changes", "Checklist" },
                optionalSections: new string[0],
                typesOfChangesLines: new[]
                {
                    "Improvement",
                    "New Content",
                    "Config and build",
                },
                checklistLines: new[]
                {
                    "My doc follows the code style of this project",
                    "I have read the",
                },
                requireAtLeastOneTypeTicked: true);

            // Samples repo template
            public static readonly PrTemplate Samples = new PrTemplate(
                name: "Samples",
                requiredSections: new[] { "Description", "Motivation and Context", "Types of changes", "Checklist" },
                optionalSections: new[] { "How Has This Been Tested?", "Screenshots" },
                typesOfChangesLines: new[]
                {
                    "Improvement",
                    "Bug fix",
                    "New Sample",
                    "Config and build",
                    "Documentation/comment",
                },
                checklistLines: new[]
                {
                    "My code follows the code style of this project",
                    "My change requires a change to the documentation",
                    "I have updated the documentation accordingly",
                    "I have read the",
                    "I have added tests to cover my changes",
                    "All new and existing tests passed",
                },
                requireAtLeastOneTypeTicked: true);

            // Repo -> descriptor dispatch. Repos not in the map use OrgDefault.
            private static readonly IReadOnlyDictionary<string, PrTemplate> _byRepo =
                new Dictionary<string, PrTemplate>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nf-interpreter"] = NfInterpreter,
                    ["Home"] = Home,
                    ["Samples"] = Samples,
                };

            public static PrTemplate ForRepo(string repoName)
            {
                if (!string.IsNullOrEmpty(repoName) && _byRepo.TryGetValue(repoName, out PrTemplate t))
                {
                    return t;
                }

                return OrgDefault;
            }
        }

        private sealed class PrTemplateValidationResult
        {
            public List<string> MissingSections { get; } = new List<string>();
            public List<string> EmptySections { get; } = new List<string>();
            public List<string> MissingTypesOfChangesLines { get; } = new List<string>();
            public List<string> MissingChecklistLines { get; } = new List<string>();
            public bool NoTypesOfChangesTicked { get; set; }
            public bool BodyLooksTruncated { get; set; }

            public bool IsValid =>
                MissingSections.Count == 0 &&
                EmptySections.Count == 0 &&
                MissingTypesOfChangesLines.Count == 0 &&
                MissingChecklistLines.Count == 0 &&
                !NoTypesOfChangesTicked;

            public string ProblemSummary()
            {
                List<string> parts = new List<string>();
                if (MissingSections.Count > 0) parts.Add($"missing sections: {string.Join(", ", MissingSections)}");
                if (EmptySections.Count > 0) parts.Add($"empty sections: {string.Join(", ", EmptySections)}");
                if (MissingTypesOfChangesLines.Count > 0) parts.Add($"missing types-of-changes lines: {MissingTypesOfChangesLines.Count}");
                if (MissingChecklistLines.Count > 0) parts.Add($"missing checklist lines: {MissingChecklistLines.Count}");
                if (NoTypesOfChangesTicked) parts.Add("no types-of-changes item ticked");
                if (BodyLooksTruncated) parts.Add("body looks truncated");
                return parts.Count == 0 ? "ok" : string.Join("; ", parts);
            }
        }

        private static PrTemplateValidationResult ValidateAgainstTemplate(
            string prBody,
            PrTemplate template)
        {
            var result = new PrTemplateValidationResult();

            string body = StripHtmlComments(prBody ?? string.Empty);

            // section presence + body lookups
            foreach (string section in template.RequiredSections)
            {
                if (!TryFindSectionBody(body, section, template, out string sectionBody))
                {
                    result.MissingSections.Add(section);
                    continue;
                }

                // Description must have actual content (after comment strip).
                // Motivation and Context is allowed to be empty: it's occasionally legitimate
                // (e.g. trivial fixes where there is no extra context to add).
                if (section == "Description"
                    && string.IsNullOrWhiteSpace(StripTemplateLinkPlaceholder(sectionBody)))
                {
                    result.EmptySections.Add(section);
                }
            }

            // canonical checkbox lines for Types of changes
            if (TryFindSectionBody(body, "Types of changes", template, out string typesBody))
            {
                foreach (string canonical in template.TypesOfChangesLines)
                {
                    if (!ContainsCanonicalCheckboxLine(typesBody, canonical))
                    {
                        result.MissingTypesOfChangesLines.Add(canonical);
                    }
                }

                if (template.RequireAtLeastOneTypeTicked)
                {
                    // any line of the form "- [x] <text>" inside the Types of changes section
                    if (!Regex.IsMatch(typesBody, @"^\s*[-*]\s*\[\s*[xX]\s*\]", RegexOptions.Multiline))
                    {
                        result.NoTypesOfChangesTicked = true;
                    }
                }
            }

            // canonical checkbox lines for Checklist
            if (TryFindSectionBody(body, "Checklist", template, out string checklistBody))
            {
                foreach (string canonical in template.ChecklistLines)
                {
                    if (!ContainsCanonicalCheckboxLine(checklistBody, canonical))
                    {
                        result.MissingChecklistLines.Add(canonical);
                    }
                }
            }

            // soft truncation heuristic: body has a Types of changes section but not the
            // Checklist that should follow it (in templates that require Checklist).
            if (Array.IndexOf(template.RequiredSections, "Checklist") >= 0
                && result.MissingSections.Contains("Checklist")
                && !result.MissingSections.Contains("Types of changes"))
            {
                result.BodyLooksTruncated = true;
            }

            return result;
        }

        // Removes HTML comments (the template ships with many of them as guidance).
        private static string StripHtmlComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            return Regex.Replace(text, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        }

        private static string StripTemplateLinkPlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = Regex.Replace(text, @"^\s*-\s*Fixes/Closes/Resolves\s+nanoFramework/Home#N+\s*$", string.Empty, RegexOptions.Multiline);
            return s;
        }

        // Tolerant section header match
        private static bool TryFindSectionBody(
            string body,
            string sectionTitle,
            PrTemplate template,
            out string sectionBody)
        {
            sectionBody = string.Empty;
            if (string.IsNullOrEmpty(body)) return false;

            // build a tolerant header regex
            string escaped = Regex.Escape(sectionTitle);
            string headerPattern = $@"(?im)^\s{{0,3}}#{{1,6}}\s+{escaped}\s*:?\s*$";
            Match header = Regex.Match(body, headerPattern);
            if (!header.Success) return false;

            int start = header.Index + header.Length;
            // find next header (any level) starting on its own line after `start`
            Match next = Regex.Match(body.Substring(start), @"(?m)^\s{0,3}#{1,6}\s+");
            int end = next.Success ? start + next.Index : body.Length;

            sectionBody = body.Substring(start, end - start);
            return true;
        }

        // Validate if the section body contains a checkbox line (- [ ] / - [x]) whose
        // text starts with the canonical prefix (case-insensitive).
        private static bool ContainsCanonicalCheckboxLine(
            string sectionBody,
            string canonicalLinePrefix)
        {
            if (string.IsNullOrEmpty(sectionBody) || string.IsNullOrEmpty(canonicalLinePrefix)) return false;

            string escaped = Regex.Escape(canonicalLinePrefix);
            // - [ ] <prefix> ... | - [x] <prefix> ...
            string pattern = $@"(?im)^\s*[-*]\s*\[\s*[ xX]?\s*\]\s*\**\s*{escaped}";
            return Regex.IsMatch(sectionBody, pattern);
        }

        private static string BuildTemplateValidationReviewBody(
            string author,
            PrTemplate template,
            PrTemplateValidationResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"🤖 Hey @{author}, your PR description doesn't match our [PR template]({_prTemplateOrgUrl}) and that breaks our auto-labeling.");
            sb.AppendLine();
            sb.AppendLine("Here's what's off:");

            foreach (string section in result.MissingSections)
            {
                sb.AppendLine($"- 🚫 Missing section: `## {section}`");
            }

            foreach (string section in result.EmptySections)
            {
                sb.AppendLine($"- ✏️ Section `## {section}` is empty — please fill it in.");
            }

            if (result.MissingTypesOfChangesLines.Count > 0)
            {
                sb.AppendLine("- 🧩 `## Types of changes` is missing these checkbox lines (keep them all, tick only the ones that apply):");
                foreach (string line in result.MissingTypesOfChangesLines)
                {
                    sb.AppendLine($"    - `- [ ] {line} (...)`");
                }
            }

            if (result.NoTypesOfChangesTicked)
            {
                sb.AppendLine("- ☑️ At least one item under `## Types of changes` must be ticked (`[x]`).");
            }

            if (result.MissingChecklistLines.Count > 0)
            {
                sb.AppendLine("- 📋 `## Checklist` is missing these checkbox lines (keep them all, tick only the ones that apply):");
                foreach (string line in result.MissingChecklistLines)
                {
                    sb.AppendLine($"    - `- [ ] {line} ...`");
                }
            }

            if (result.BodyLooksTruncated)
            {
                sb.AppendLine("- ✂️ The body looks like it was truncated before the end of the template. Please paste the full template.");
            }

            sb.AppendLine();
            sb.AppendLine($"Please paste the template from the [link above]({_prTemplateOrgUrl}) and fill it in — keep all the checkbox lines even if they don't apply (just leave them unticked). 🙏");
            sb.AppendLine();

            // collapsed AI-fixing prompt
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>🤖 Prompt for fixing with AI agents</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("You are fixing a GitHub pull request description that failed the nanoFramework PR template check.");
            sb.AppendLine($"Rewrite the PR body so that it strictly matches the template at {_prTemplateOrgUrl} (variant: \"{template.Name}\").");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine($"1. Keep these top-level headings, in this order: {string.Join(", ", template.RequiredSections.Select(s => "## " + s))}.");
            sb.AppendLine($"2. The `## Types of changes` section MUST contain ALL of these checkbox lines (verbatim), ticked only when they apply:");
            foreach (string line in template.TypesOfChangesLines)
            {
                sb.AppendLine($"   - [ ] {line} (...)");
            }
            sb.AppendLine($"3. The `## Checklist` section MUST contain ALL of these checkbox lines (verbatim), ticked only when they apply:");
            foreach (string line in template.ChecklistLines)
            {
                sb.AppendLine($"   - [ ] {line} ...");
            }
            sb.AppendLine("4. `## Description` must be non-empty. `## Motivation and Context` may be empty when there's nothing extra to add.");
            sb.AppendLine("5. Do NOT invent new facts — preserve whatever real content the author already wrote.");
            sb.AppendLine("6. Do NOT remove checkbox lines just because they don't apply; leave them unticked.");
            sb.AppendLine();
            sb.AppendLine("Specifically, fix these issues:");
            foreach (string s in result.MissingSections) sb.AppendLine($"- Missing section: ## {s}");
            foreach (string s in result.EmptySections) sb.AppendLine($"- Empty section: ## {s}");
            foreach (string s in result.MissingTypesOfChangesLines) sb.AppendLine($"- Missing Types-of-changes line: {s}");
            if (result.NoTypesOfChangesTicked) sb.AppendLine("- No Types-of-changes item is ticked; tick the applicable ones.");
            foreach (string s in result.MissingChecklistLines) sb.AppendLine($"- Missing Checklist line: {s}");
            if (result.BodyLooksTruncated) sb.AppendLine("- Body appears truncated; restore the missing tail of the template.");
            sb.AppendLine();
            sb.AppendLine("Output ONLY the new PR body in markdown — no fences, no commentary.");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
            sb.AppendLine(_prTemplateValidationMarker);

            return sb.ToString();
        }

        // Creates a new PR review of type COMMENT, or edits the existing one
        private static async Task PostOrUpdateTemplateValidationReviewAsync(
            string repoName,
            int prNumber,
            string reviewBody,
            ILogger log)
        {
            try
            {
                var existing = await FindTemplateValidationReviewAsync(repoName, prNumber, log);

                if (existing != null)
                {
                    string url = $"https://api.github.com/repos/{_gitOwner}/{repoName}/pulls/{prNumber}/reviews/{existing.Id}";
                    string payload = JsonConvert.SerializeObject(new { body = reviewBody });

                    await SendGitHubRequest(url, payload, log, null, "PUT");
                    log.LogInformation($"Updated existing PR template validation review {existing.Id} on {repoName}#{prNumber}.");
                }
                else
                {
                    string url = $"https://api.github.com/repos/{_gitOwner}/{repoName}/pulls/{prNumber}/reviews";
                    string payload = JsonConvert.SerializeObject(new { body = reviewBody, @event = "COMMENT" });

                    await SendGitHubRequest(url, payload, log, null, "POST");
                    log.LogInformation($"Posted new PR template validation review on {repoName}#{prNumber}.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Failed to post/update PR template validation review on {repoName}#{prNumber}.");
            }
        }

        // When the PR becomes valid, edit any existing validation review body to a short acknowledgement 
        private static async Task ResolveTemplateValidationReviewIfAnyAsync(
            string repoName,
            int prNumber,
            ILogger log)
        {
            try
            {
                var existing = await FindTemplateValidationReviewAsync(repoName, prNumber, log);
                if (existing == null) return;

                // already resolved? avoid pointless edits
                if (existing.Body != null && existing.Body.Contains("template looks good now"))
                {
                    return;
                }

                string url = $"https://api.github.com/repos/{_gitOwner}/{repoName}/pulls/{prNumber}/reviews/{existing.Id}";
                string newBody = $"✅ The PR template looks good now. Thanks!\r\n\r\n{_prTemplateValidationMarker}";
                string payload = JsonConvert.SerializeObject(new { body = newBody });

                await SendGitHubRequest(url, payload, log, null, "PUT");
                log.LogInformation($"Resolved PR template validation review {existing.Id} on {repoName}#{prNumber}.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Failed to resolve PR template validation review on {repoName}#{prNumber}.");
            }
        }

        private sealed class ExistingReview
        {
            public long Id { get; set; }
            public string Body { get; set; }
        }

        private static async Task<ExistingReview> FindTemplateValidationReviewAsync(
            string repoName,
            int prNumber,
            ILogger log)
        {
            var reviews = await _octokitClient.PullRequest.Review.GetAll(_gitOwner, repoName, prNumber);

            foreach (var r in reviews)
            {
                if (r.User != null
                    && string.Equals(r.User.Login, "nfbot", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(r.Body)
                    && r.Body.Contains(_prTemplateValidationMarker))
                {
                    return new ExistingReview { Id = r.Id, Body = r.Body };
                }
            }

            return null;
        }

        private static async Task<IActionResult> ProcessClosedIssueAsync(
            Octokit.Issue issue,
            long repositoryId,
            dynamic payload,
            ILogger log)
        {
            // get timeline of issue
            var issueTimeLine = await _octokitClient.Issue.Timeline.GetAllForIssue(repositoryId, issue.Number);

            // get all cross referenced PRs
            var crossRefPrs = issueTimeLine.Where(t => t.Event == EventInfoState.Crossreferenced && t.Source.Issue?.PullRequest != null).OrderByDescending(t => t.CreatedAt);

            if (crossRefPrs.Any())
            {
                // there are PRs referenced, check if they are all closed
                if (crossRefPrs.Count(pr => pr.Source.Issue.State.Value == ItemState.Closed) == crossRefPrs.Count())
                {
                    // this issue was linked to PRs that are all closed now
                    // it's safe to assume that it was just closed by it

                    // clear all labels that don't belong here anymore
                    foreach (var label in issue.Labels)
                    {
                        if (label.Name == "up-for-grabs" ||
                           label.Name == "good first issue" ||
                           label.Name == "FOR DISCUSSION" ||
                           label.Name == "HELP WANTED" ||
                           label.Name.StartsWith("Status") ||
                           label.Name.Contains("trivial") ||
                           label.Name.Contains("Priority") ||
                           label.Name.Contains("pinned"))
                        {
                            _ = await _octokitClient.Issue.Labels.RemoveFromIssue(repositoryId, issue.Number, label.Name);
                        }
                    }

                    // set the appropriate label after the issue closure
                    foreach (var label in issue.Labels)
                    {
                        if (label.Name == "Type: Bug")
                        {
                            _ = await _octokitClient.Issue.Labels.AddToIssue(repositoryId, issue.Number, new string[] { "Status: FIXED" });
                        }
                        else if (label.Name == "Type: Chores"
                                 || label.Name == "Type: Enhancement"
                                 || label.Name == "Type: Feature request")
                        {
                            _ = await _octokitClient.Issue.Labels.AddToIssue(repositoryId, issue.Number, new string[] { "Status: DONE" });
                        }
                    }
                }
            }
            else
            {
                // this issue has no link to any PRs

                // clear all labels that don't belong here anymore
                foreach (var label in issue.Labels)
                {
                    if (label.Name == "up-for-grabs" ||
                       label.Name == "good first issue" ||
                       label.Name == "FOR DISCUSSION" ||
                       label.Name == "HELP WANTED" ||
                       label.Name.StartsWith("Status") ||
                       label.Name.Contains("trivial") ||
                       label.Name.Contains("Priority") ||
                       label.Name.Contains("pinned"))
                    {
                        _ = await _octokitClient.Issue.Labels.RemoveFromIssue(repositoryId, issue.Number, label.Name);
                    }
                }
            }

            return new OkObjectResult("");
        }

        private static async Task<IActionResult> ProcessOpenOrEditIssueAsync(
            Octokit.Issue issue,
            long repositoryId,
            dynamic payload,
            ILogger log,
            GitHubClient _octokitClient)
        {

            // check for content that shouldn't be there and shows that the author hadn't read the instructions or is being lazy

            // flag that this is a "open issue" event
            bool isOpenAction = payload.action == "opened";
            string authorAssociation = (string)payload.issue.author_association;

            log.LogInformation($"Processing issue #{issue.Number}");

            // fix title if needed
            if (issue.Title.EndsWith("."))
            {
                var fixedIssue = new IssueUpdate
                {
                    Title = issue.Title.Substring(0, issue.Title.Length - 1)
                };

                issue = await _octokitClient.Issue.Update(
                    repositoryId,
                    issue.Number,
                    fixedIssue);
            }

            // check for expected/mandatory content
            bool issueIsTodo = false;

            if (issue.Labels.Any(l => l.Name == _labelTypeChoresName) || issue.Body.Contains(_todoTagComment))
            {
                issueIsTodo = true;
            }

            if (issueIsTodo)
            {
                // users outside members team can't open TODOs
                // need to proceed with the author check

                if (!_rolesWithProjectOrgPermissions.Contains(authorAssociation))
                {
                    // comment on issue
                    _ = await _octokitClient.Issue.Comment.Create(
                                            repositoryId,
                                            (int)payload.issue.number,
                                            $"{_issueTodoWihtouPermission}.");

                    // close issue
                    _ = await _octokitClient.Issue.Update(
                        (long)payload.repository.id,
                        (int)payload.issue.number,
                        new IssueUpdate() { State = ItemState.Closed });

                    // done here
                    return new OkObjectResult("");
                }
            }

            if (isOpenAction)
            {
                // is author 
                if (!_rolesWithProjectOrgPermissions.Contains(authorAssociation))
                {
                    // add comment about support options
                    var newComment = await _octokitClient.Issue.Comment.Create(
                        (long)payload.repository.id,
                        (int)payload.issue.number,
                        $"Hey @{payload.issue.user.login}! 👋 {_issueSupportOptionsNotice}");

                    // hang here for a while to let the comment flow through GitHub
                    await Task.Delay(TimeSpan.FromSeconds(10));

                    // delete comment
                    await _octokitClient.Issue.Comment.Delete((long)payload.repository.id, newComment.Id);
                }

                return new OkObjectResult("");
            }
            else
            {
                // everything looks OK, remove all comments from nfbot, if any
                if (issue.Comments > 0)
                {
                    await RemovenfbotCommentsAsync(
                        repositoryId,
                        issue.Number,
                        log);
                }
            }

            return new OkObjectResult("");
        }

        private static async Task RemovenfbotCommentsAsync(Octokit.PullRequest pr, ILogger log)
        {
            await RemovenfbotCommentsAsync(pr.Base.Repository.Id, pr.Number, log);
        }

        private static async Task RemovenfbotCommentsAsync(long repoId, int id, ILogger log)
        {
            // list all comments from nfbot
            var commentList = await _octokitClient.Issue.Comment.GetAllForIssue(repoId, id);

            var commentsToRemove = commentList.Where(c => c.User.Login == "nfbot");


            foreach (var comment in commentsToRemove)
            {
                // check for fix request comment, remove only the ones that have it
                // (need to remove the leading \r\n from the label string
                if (comment.Body.Contains(_fixRequestTagComment.Replace("\\r", "").Replace("\\n", "")))
                {
                    await _octokitClient.Issue.Comment.Delete(repoId, comment.Id);
                }
            }
        }

        public static async Task<Tuple<int, int, bool>> CheckCommitMessages(
            dynamic commitsCollection,
            ILogger log)
        {
            int signedOffCount = 0;
            int obviousFixCount = 0;
            bool checkPass = true;

            foreach (dynamic item in commitsCollection)
            {
                // get commit message
                log.LogInformation($"Commit sha: [{item.sha.ToString()}]");
                log.LogInformation($"Commit message: >>{item.commit.message.ToString()}<<");
                log.LogInformation($"Commit author name: >>{item.commit.author.name.ToString()}<<");
                log.LogInformation($"Commit author email: >>{item.commit.author.email.ToString()}<<");

                // check for sign-off message 
                if (item.commit.message.ToString().Contains($"Signed-off-by: {item.commit.author.name.ToString()} <{item.commit.author.email.ToString()}>"))
                {
                    log.LogInformation($"CheckCommitMessages: Signed-off-by comment verified");

                    signedOffCount++;

                    // set status to DCO checked
                    // need to hack this URL because the API is not exposing the URL for setting individual commit status
                    await SendGitHubRequest(
                        $"{item.url.ToString().Replace("/commits/", "/statuses/")}",
                        "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit has a DCO Signed-off-by.\" }",
                        log);
                }
                else
                {
                    // this commit message isn't signed-off 
                    // check for obvious fix message variations (this has to be a single line, to clear use of this on a sentence)
                    if (item.commit.message.ToString().Contains("\nobvious fix") ||
                        item.commit.message.ToString().Contains("obvious fix\n") ||
                        item.commit.message.ToString().Contains("obvious fix.\n") ||
                        item.commit.message.ToString().Contains("\nObvious fix") ||
                        item.commit.message.ToString().Contains("Obvious fix\n") ||
                        item.commit.message.ToString().Contains("Obvious fix.\n"))
                    {
                        log.LogInformation($"CheckCommitMessages: Obvious fix comment verified");

                        obviousFixCount++;

                        // set status to DCO checked
                        // need to hack this URL because the API is not exposing the URL for setting individual commit status
                        await SendGitHubRequest(
                            $"{item.url.ToString().Replace("/commits/", "/statuses/")}",
                            "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit is an obvious fix.\" }",
                            log);
                    }
                    else
                    {
                        // no obvious fix message either
                        log.LogInformation($"CheckCommitMessages: no valid comment found");

                        // flag to NOT checked if not already...
                        checkPass &= false;

                        // set status to DCO required
                        // need to hack this URL because the API is not exposing the URL for setting individual commit status
                        await SendGitHubRequest(
                            $"{item.url.ToString().Replace("/commits/", "/statuses/")}",
                            "{ \"context\" : \"DCO\" , \"state\" : \"failure\" , \"description\" : \"This commit is missing either the DCO Signed-off-by or the obvious fix statement.\" }",
                            log);
                    }
                }
            }

            return Tuple.Create(signedOffCount, obviousFixCount, checkPass);
        }

        public static async Task<int> SendGitHubRequest(
            string url,
            string requestBody,
            ILogger log,
            string acceptHeader = null,
            string verb = "POST")
        {
            using (var client = new HttpClient())
            {
                HttpResponseMessage response = null;

                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nanoframework_nfbot", "2.0"));

                // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
                // "Username:Password" or "Username:PersonalAccessToken"
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

                // if specified, add Accept HTTP header for GitHub preview APIs
                if (string.IsNullOrEmpty(acceptHeader))
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.squirrel-girl-preview"));
                }
                else
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));
                }

                log.LogInformation($"Request URL {url}");

                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                log.LogInformation($"Request content >>{await content.ReadAsStringAsync()}<<");

                if (verb == "POST")
                {
                    response = await client.PostAsync(url, content);
                }
                else if (verb == "PUT")
                {
                    response = await client.PutAsync(url, content);
                }
                else if (verb == "DELETE")
                {
                    response = await client.DeleteAsync(url);
                }
                else if (verb == "PATCH")
                {
                    response = await client.PatchAsync(url, content);
                }
                else
                {
                    log.LogInformation($"Unknown verb when executing SendGitHubRequest.");
                }

                log.LogInformation($"Request result {response.StatusCode}");
                log.LogInformation($"Request result {response.StatusCode} content >>{await response.Content.ReadAsStringAsync()}<< .");

                return (int)response.StatusCode;
            }
        }

        public static async Task<dynamic> GetGitHubRequest(
            string url,
            ILogger log)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.antiope-preview+json"));

                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("username", "version"));

                /////////////////////////////////////////////////////////////////////////////////////////////////
                // commented code to generate the encoded string to put at GITHUB_CREDENTIALS
                //var plainTextBytes = Encoding.UTF8.GetBytes("nfbot:GITHUB-TOKEN-HERE");
                //string base64Encoded = Convert.ToBase64String(plainTextBytes);
                /////////////////////////////////////////////////////////////////////////////////////////////////

                // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
                // "Username:Password" or "Username:PersonalAccessToken"
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

                log.LogInformation($"Request URL {url}");

                HttpResponseMessage response = await client.GetAsync(url);

                dynamic responseContent = JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());

                log.LogInformation($"Reply {responseContent.ToString()}");

                return responseContent;
            }
        }

        public static async Task MergePR(
            dynamic pull_request,
            ILogger log)
        {
            log.LogInformation($"Merge PR {pull_request.title}");

            // place holder for commit message (if any)
            string commitMessage = "";

            // get labels for this PR
            JArray prLabels = (JArray)pull_request.labels;

            var updateDependentsLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiUpdateDependentsName);
            if (updateDependentsLabel != null)
            {
                commitMessage += "\r\n***UPDATE_DEPENDENTS***";
            }

            var publishReleaseLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiPublishReleaseName);
            if (publishReleaseLabel != null)
            {
                commitMessage += "\r\n***PUBLISH_RELEASE***";
            }

            string mergeRequest = $"{{ \"commit_title\": \"{pull_request.title}\", \"commit_message\": \"{commitMessage}\", \"sha\": \"{pull_request.head.sha}\", \"merge_method\": \"merge\" }}";

            // request need to be a PUT
            await SendGitHubRequest(
                $"{pull_request.url.ToString()}/merge",
                mergeRequest,
                log,
                "application/vnd.github.squirrel-girl-preview", "PUT");
        }

        public static async Task SquashAndMergePR(
            dynamic pull_request,
            ILogger log)
        {
            log.LogInformation($"Squash and merge PR {pull_request.title}");

            // place holder for commit message (if any)
            string commitMessage = "";

            // get labels for this PR
            JArray prLabels = (JArray)pull_request.labels;

            var updateDependentsLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiUpdateDependentsName);
            if (updateDependentsLabel != null)
            {
                commitMessage += "\r\n***UPDATE_DEPENDENTS***";
            }

            var publishReleaseLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiPublishReleaseName);
            if (publishReleaseLabel != null)
            {
                commitMessage += "\r\n***PUBLISH_RELEASE***";
            }

            string mergeRequest = $"{{ \"commit_title\": \"{pull_request.title}\", \"commit_message\": \"{commitMessage}\", \"sha\": \"{pull_request.head.sha}\", \"merge_method\": \"squash\" }}";

            // request need to be a PUT
            await SendGitHubRequest(
                $"{pull_request.url.ToString()}/merge",
                mergeRequest,
                log,
                "application/vnd.github.squirrel-girl-preview", "PUT");
        }

        public static async Task MergePrWithStrategy(
            Octokit.PullRequest pull_request,
            bool skipCIBuild,
            ILogger log,
            PullRequestMergeMethod mergeMethod)
        {
            log.LogInformation($"Squash and merge PR {pull_request.Title}");

            // place holder for commit message (if any)
            string commitMessage = "";

            // check PR labels
            if (pull_request.Labels.Any(l => l.Name == _labelCiUpdateDependentsName))
            {
                commitMessage += "\r\n***UPDATE_DEPENDENTS***";
            }

            if (pull_request.Labels.Any(l => l.Name == _labelCiPublishReleaseName))
            {
                commitMessage += "\r\n***PUBLISH_RELEASE***";
            }

            if (skipCIBuild)
            {
                commitMessage += "\r\n***NO_CI***";
            }

            await _octokitClient.PullRequest.Merge(
                pull_request.Base.Repository.Id,
                pull_request.Number,
                new MergePullRequest()
                {
                    MergeMethod = mergeMethod,
                    CommitTitle = pull_request.Title,
                    CommitMessage = commitMessage,
                });
        }

        public static async Task CloseIssue(
            int repoId,
            Octokit.Issue issue,
            ILogger log)
        {
            log.LogInformation($"Closing Issue #{issue.Number} \"{issue.Title}\"");

            // close issue
            var issueUpdate = new IssueUpdate
            {
                State = ItemState.Closed
            };
            issueUpdate.ClearLabels();
            issueUpdate.ClearAssignees();
            issueUpdate.AddLabel(_labelInvalidName);

            await _octokitClient.Issue.Update(repoId, issue.Number, issueUpdate);
        }

        public static async Task ClosePR(
            dynamic pr,
            ILogger log)
        {
            log.LogInformation($"Close PR {pr.title}");

            string closeRequest = $"{{ \"state\": \"close\" }}";

            // request need to be a PATCH
            await SendGitHubRequest(
                $"{pr.url.ToString()}",
                closeRequest,
                log,
                "",
                "PATCH");
        }
    }
}
