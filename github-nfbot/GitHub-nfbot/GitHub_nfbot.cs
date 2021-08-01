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
using System.Threading.Tasks;

namespace nanoFramework.Tools.GitHub
{
    public static class GitHub_nfbot
    {
        // strings to be used in messages and comments
        private const string _fixRequestTagComment = "\r\n<!-- nfbot fix request DO NOT REMOVE -->";
        private const string _bugReportForClassLibTagComment = "<!-- bug-report-clas-lib-tag DO NOT REMOVE -->";
        private const string _bugReportFirmwareTagComment = "<!-- bug-report-fw-tag DO NOT REMOVE -->";
        private const string _bugReportToolsTagComment = "<!-- bug-report-tools-tag DO NOT REMOVE -->";
        private const string _featureRequestTagComment = "<!-- feature-request-tag DO NOT REMOVE -->";
        private const string _todoTagComment = "<!-- todo-tag DO NOT REMOVE -->";
        private const string _issueCommentUnwantedContent = ":disappointed: Looks like you haven't read the instructions with enough care and forgot to cleanup the instructions. Please make sure to follow the template and remove only the instruction comments and any sections that are not relevant. After doing so, feel free to reopen the issue.";
        private const string _issueMissingAreaContent = ":disappointed: Information about the nanoFramework area is missing. Please make sure to follow the template and remove only the instruction comments and any sections that are not relevant. After doing so, feel free to reopen the issue.";
        private const string _issueCommentInvalidDeviceCaps = ":disappointed: If that's relevant, make sure to include the complete Device Capabilities output.\r\n.If it isn't, just remove the section completely.\r\nAfter fixing that, feel free to reopen the issue.";
        private const string _issueCommentUnshureAboutIssueContent = ":disappointed: I couldn't figure out what type of issue you're trying to open...\r\nMake sure you're used one of the **templates** and have include all the required information. After doing that feel free to reopen the issue.\r\n\r\nIf you have a question, need clarification on something, need help on a particular situation or want to start a discussion, **DO NOT** open an issue here. It is best to start a conversation on one of our [Discord channels](https://discordapp.com/invite/gCyBu8T) or to ask the question on [Stack Overflow](https://stackoverflow.com/questions/tagged/nanoframework) using the `nanoframework` tag.";
        private const string _prCommentUserIgnoringTemplateContent = ":disappointed: I'm afraid you'll have to use the PR template like the rest of us...\\r\\nMake sure you've used the **template** and have include all the required information and fill in the appropriate details. After doing that feel free to reopen the PR. If you have questions we are here to help.";
        private const string _prCommentChecklistWithOpenItemsTemplateContent = ":disappointed: I'm afraid you'll left some tasks behind...\\r\\nMake sure you've went through all the tasks in the list. If you have questions we are here to help.";
        private const string _prCommunityTargetMissingTargetContent = ":disappointed: You need to check which targets are affected in the list...\\r\\nMake sure you follow the PR template. After doing that feel free to reopen the PR.\\r\\nIf you have questions we are here to help.";
        private const string _fixCheckListComment = "I've fixed the checklist for you.\\r\\nFYI, the correct format is [x], no spaces inside brackets.";

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

        private const string _issueArea = "nanoFramework area:";
        private const string _issueFeatureRequest = "### Is your feature request related to a problem?";
        private const string _issueTargetId = "**Target:";
        private const string _issueFwVersion = "**Firmware image version:";
        private const string _issueDeviceCaps = "**Device capabilities output:";
        private const string _issueDescription = "### Description";

        // strings for PR content
        private const string _prDescription = "## Description";
        private const string _prTypesOfChanges = "## Types of changes";
        private const string _prChecklist = "## Checklist:";
        private const string _prChanges = "## Checklist:";

        // labels
        private const string _labelConfigAndBuildName = "Area: Config-and-Build";
        private const string _labelBreakingChangeName = "Breaking-change";

        private const string _labelCiUpdateDependentsName = "CI: Update Dependents";
        private const string _labelCiPublishReleaseName = "CI: Publish Release";

        private const string _labelTypeDependenciesName = "Type: dependencies";
        private const string _labelTypeFeatureRequestName = "Type: Feature Request";
        private const string _labelTypeBugName = "Type: bug";
        private const string _labelTypeEnhancementName = "Type: enhancement";
        private const string _labelTypeUnitTestsName = "Type: Unit Tests";

        private const string _labelStatusWaitingTriageName = "Status: Waiting triage";

        private const string _labelInvalidName = "invalid";

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

                    Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                    ////////////////////////////////////////////////////////////
                    // processing exceptions

                    // any BOT
                    if (payload.pull_request.user.login.ToString().EndsWith("[bot]"))
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
                        string prBody = payload.pull_request.body;

                        if (prBody.Contains("[version update]"))
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
                        await FixCheckListAsync(payload, log);

                        // check for PR ignoring template
                        if (await ValidatePRContentAsync(payload, log))
                        {
                            // post comment with thank you message if this is a new PR
                            if (payload.action == "opened")
                            {
                                log.LogInformation($"Comment with thank you note.");

                                string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nA human will be reviewing it shortly. :wink:{_fixRequestTagComment}\" }}";

                                // add thumbs up reaction in PR main message
                                await SendGitHubRequest(
                                    $"{payload.pull_request.issue_url.ToString()}/reactions",
                                    "{ \"content\" : \"+1\" }",
                                    log,
                                    "application/vnd.github.squirrel-girl-preview");
                            }
                        }

                        bool linkedIssuesReference = await CheckLinkedIssuesAsync(payload, log);

                        await ManageLabelsAsync(pr, log);

                        if (linkedIssuesReference)
                        {
                            // everything looks OK, remove all comments from nfbot
                            await RemovenfbotCommentsAsync(
                                payload.pull_request.comments_url.ToString(),
                                log);
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
                    }

                    // check merge status
                    if (!await _octokitClient.PullRequest.Merged(_gitOwner, payload.repository.name.ToString(), (int)payload.number))
                    {
                        // this branch was deleted without being merged, mark as invalid

                        // clear all labels
                        await _octokitClient.Issue.Labels.RemoveAllFromIssue(_gitOwner, payload.repository.name.ToString(), (int)payload.number);

                        // add the invalid label
                        await _octokitClient.Issue.Labels.AddToIssue(_gitOwner, payload.repository.name.ToString(), (int)payload.number, new string[] { _labelInvalidName });
                    }
                }
                else if (payload.action == "synchronize")
                {
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
                }
            }

            #endregion

            #region process issues

            else if (payload.issue != null)
            {
                log.LogInformation($"Processing issue #{payload.issue.number} '{payload.issue.title}' submitted by {payload.issue.user.login}");

                // get issue
                Octokit.Issue issue = await _octokitClient.Issue.Get(_gitOwner, payload.repository.name.ToString(), (int)payload.issue.number);

                if ( (payload.action == "opened" ||
                      payload.action == "edited" ||
                      payload.action == "reopened") &&
                      payload.comment == null)
                {
                    return await ProcessOpenOrEditIssueAsync(
                        issue, 
                        payload, 
                        log);
                }
                else if (payload.action == "closed")
                {
                    return await ProcessClosedIssueAsync(
                        issue,
                        payload,
                        log);
                }
                else if(
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
                        if(payload.comment.author_association == "MEMBER" || payload.issue.author_association == "OWNER")
                        {
                            if (await ProcessCommandAsync(payload, log))
                            {
                                // add thumbs up reaction to comment
                                await SendGitHubRequest(
                                    $"{payload.comment.url.ToString()}/reactions",
                                    "{ \"content\" : \"+1\" }",
                                    log,
                                    "application/vnd.github.squirrel-girl-preview");
                            }
                            else
                            {
                                // add confused reaction to comment
                                await SendGitHubRequest(
                                    $"{payload.comment.url.ToString()}/reactions",
                                    "{ \"content\" : \"confused\" }",
                                    log,
                                    "application/vnd.github.squirrel-girl-preview");
                            }
                        }
                        else
                        {
                            log.LogInformation($"User has no permission to execute command");

                            // add thumbs down reaction to comment
                            await SendGitHubRequest(
                                $"{payload.comment.url.ToString()}/reactions",
                                "{ \"content\" : \"-1\" }",
                                log,
                                "application/vnd.github.squirrel-girl-preview");
                        }
                    }
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

                            if( prLabels.Count(l => l["name"].ToString() == _labelCiPublishReleaseName) > 0 )
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
                else if(payload.state != null)
                {
                    // serious candidate of a PR state
                    log.LogInformation($"Processing state success event...");

                    // get SHA
                    prSha = payload.sha.ToString();
                }

                // list all open PRs...
                IReadOnlyList<Octokit.PullRequest>  openPRs = await _octokitClient.PullRequest.GetAllForRepository(
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

                if (matchingPr != null)
                {
                    // get PR
                    Octokit.PullRequest pr = await _octokitClient.PullRequest.Get(_gitOwner, payload.repository.name.ToString(), matchingPr.Number);

                    // check if PR it's a version update
                    if ((pr.User.Login == "nfbot" ||
                         pr.User.Login == "github-actions[bot]") && 
                         pr.Body.ToString().Contains("[version update]"))
                    {
                        bool skipCIBuild = false;

                        // get reuired check runs for this branch
                        BranchProtectionRequiredStatusChecks statusChecks = await _octokitClient.Repository.Branch.GetRequiredStatusChecks((long)payload.repository.id, pr.Base.Ref);

                        // get status checks for PR
                        CheckRunsResponse checkRunStatus = await _octokitClient.Check.Run.GetAllForReference(_gitOwner, payload.repository.name.ToString(), pr.Head.Sha);

                        // check if ALL check runs are in place
                        int requiredStatusChecks = statusChecks.Contexts.Where(c => !c.Contains("license/cla")).Count();

                        if (checkRunStatus.TotalCount >= requiredStatusChecks)
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
                                    if (statusCheck != "license/cla")
                                    {
                                        log.LogInformation($"Status check {statusCheck} not reported as successfull");
                                    }
                                }
                            }

                            if (checksCount >= requiredStatusChecks)
                            {

                                if (!checkRunStatus.CheckRuns.Any(cr => cr.Conclusion != CheckConclusion.Success))
                                {
                                    // check if this is running on samples repo
                                    if (!pr.HtmlUrl.ToString().Contains("nanoframework/Samples"))
                                    {
                                        // class lib repo

                                        // default is TO PUBLISH a new release 
                                        bool publishReleaseFlag = true;

                                        // get labels for this PR
                                        List<Label> prLabels = (List<Label>)pr.Labels;

                                        // check if this was a dependencies update
                                        var dependenciesLabel = prLabels.FirstOrDefault(l => l.Name.ToString() == _labelTypeDependenciesName);
                                        if (dependenciesLabel != null)
                                        {
                                            // this is a dependencies update PR

                                            // get which packages where updated
                                            IReadOnlyList<PullRequestFile> prFiles = await _octokitClient.PullRequest.Files(_gitOwner, payload.repository.name.ToString(), (int)pr.Number);

                                            var packageFile = prFiles.FirstOrDefault(f => f.FileName.Contains("/packages.config"));

                                            if (packageFile != null)
                                            {
                                                // get patch
                                                var diffs = packageFile.Patch.ToString().Split('\n');

                                                // get additions
                                                var newPackages = diffs.Where(p => p.StartsWith("+  <package id"));

                                                if ((newPackages.Count() == 1 &&
                                                    (newPackages.Any(p => p.Contains("nanoFramework.TestFramework")) ||
                                                     newPackages.Any(p => p.Contains("Nerdbank.GitVersioning")))) ||
                                                    (newPackages.Count() == 2 &&
                                                    newPackages.Any(p => p.Contains("nanoFramework.TestFramework")) &&
                                                     newPackages.Any(p => p.Contains("Nerdbank.GitVersioning"))))
                                                {
                                                    // update was for:
                                                    // Test Framework
                                                    // Nerdbank.GitVersioning

                                                    // DON'T publish a new release
                                                    publishReleaseFlag = false;

                                                    // skip build
                                                    skipCIBuild = true;
                                                }
                                            }
                                        }

                                        if (publishReleaseFlag)
                                        {
                                            // add publish release label

                                            log.LogInformation($"Adding 'Publish release flag to PR.");

                                            // add the Publish release label
                                            await _octokitClient.Issue.Labels.AddToIssue(_gitOwner, payload.repository.name.ToString(), (int)pr.Number, new string[] { _labelCiPublishReleaseName });
                                        }
                                    }

                                    // all checks completed successfully
                                    // merge PR with squash
                                    await SquashAndMergePR(
                                        pr,
                                        skipCIBuild,
                                        log);
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

            else if (payload.sponsorship != null && payload.action == "created")
            {
                // we have a new sponsor
                // send message to Discord channel

                log.LogInformation($"Processing new sponsor contribution from {payload.sponsorship.sponsor.login.ToString()}");

                var slackPayload = new
                {
                    text = $"GitHub user <{payload.sponsorship.sponsor.url.ToString()}|{payload.sponsorship.sponsor.login.ToString()}> just sponsored the project with a ${payload.sponsorship.tier.name.ToString()} contribution. Well done and thank you very much! :clap:",
                    icon_url = payload.sponsorship.sponsor.avatar_url.ToString(),
                };

                // Add the DISCORD_CONTRIBUTIONS_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
                // this is possible because Discord webhooks API supports Slack compatible webhooks
                // see https://discordapp.com/developers/docs/resources/webhook#execute-slackcompatible-webhook
                using var client = new HttpClient();
                var res = await client.PostAsync(
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

        private static async Task<bool> ProcessCommandAsync(dynamic payload, ILogger log)
        {
            // content has to follow this pattern:
            // @nfbot ccccc a1 a2 a3
            // start with nfbot
            // a single space
            // command
            // arguments (optional)

            // remove start of message
            var command = payload.comment.body.ToString().Substring("@nfbot ".Length);

            // get repository
            string repositoryName = payload.repository.name.ToString();
            // clear known prefixes
            repositoryName = repositoryName.Replace("lib-", "");

            // check commands
            if (command.StartsWith("startrelease"))
            {
                log.LogInformation($"Processing command");

                return await StartReleaseCandidateAsync(repositoryName, log);
            }
            else if (command.StartsWith("updatedependents"))
            {
                return await UpdateDependentsAsync(repositoryName, log);
            }
            else if (command.StartsWith("updatedependencies"))
            {
                return await UpdateDependenciesAsync(payload.repository.url.ToString(), log);
            }
            else if (command.StartsWith("runpipeline"))
            {
                // get branch name, if any

                string branchName = command.Substring("runpipeline".Length);

                if(!string.IsNullOrEmpty(branchName))
                {
                    // remove leading and trailing white spaces
                    branchName = branchName.Trim();
                }
                else
                {
                    // set to default branch name: 'develop'
                    branchName = "develop";
                }

                try
                {
                    return await QueueBuildAsync(repositoryName, branchName, log);
                }
                catch (Exception ex)
                {
                    log.LogError($"Error queuing build: {ex.Message}.");

                    return false;
                }
            }

            // unknown or invalid command
            return false;
        }

        private static async Task<bool> StartReleaseCandidateAsync(string repositoryName, ILogger log)
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
            if(buildDefs.Count == 1)
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

                    return false;
                }

                return true;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return false;
        }

        private static async Task<bool> UpdateDependentsAsync(string repositoryName, ILogger log)
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
                catch(Exception ex)
                {
                    log.LogError($"Error queuing build: {ex.Message}.");

                    return false;
                }

                return true;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return false;
        }

        private static async Task<bool> UpdateDependenciesAsync(string repoUrl, ILogger log)
        {
            string requestContent = $"{{ \"event_type\": \"update-dependencies\" }}";


            var result = await SendGitHubRequest(
                repoUrl + "/dispatches",
                requestContent,
                log,
                "application/vnd.github.v3+json",
                "POST");

            if(result == 204)
            {
                return true;
            }

            return false;
        }

        private static async Task<bool> QueueBuildAsync(string repositoryName, string branchName, ILogger log)
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

                return true;
            }
            else
            {
                log.LogError("Error processing DevOps build definition: more definition then expected");
            }

            return false;
        }

        private static async Task ManageLabelsAsync(Octokit.PullRequest pr, ILogger log)
        {
            if (pr.Body.Contains("[x] Bug fix", StringComparison.InvariantCultureIgnoreCase))
            {
                // add the Type: dependency label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeBugName });
            }

            if (
                pr.Body.Contains("[x] Improvement", StringComparison.InvariantCultureIgnoreCase) ||
                pr.Body.Contains("[x] New feature", StringComparison.InvariantCultureIgnoreCase) )
            {
                // add the Type: enhancement label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeEnhancementName });
            }

            if (pr.Body.Contains("[x] Breaking change", StringComparison.InvariantCultureIgnoreCase))
            {
                // add the Type: Breaking change label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelBreakingChangeName });
            }

            if (pr.Body.Contains("[x] Config and build", StringComparison.InvariantCultureIgnoreCase))
            {
                // add the Type: Breaking change label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelConfigAndBuildName });
            }

            if (pr.Body.Contains("[x] Dependencies", StringComparison.InvariantCultureIgnoreCase))
            {
                // add the Type: Breaking change label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeDependenciesName });
            }

            if (pr.Body.Contains("[x] Unit Tests", StringComparison.InvariantCultureIgnoreCase))
            {
                // add the Type: Unit Tests label
                await _octokitClient.Issue.Labels.AddToIssue(pr.Base.Repository.Id, pr.Number, new string[] { _labelTypeUnitTestsName });
            }
        }

        private static async Task<bool> CheckLinkedIssuesAsync(dynamic payload, ILogger log)
        {
            // get PR body
            string prBody = payload.pull_request.body;

            string commentContent;

            // check for invalid link to issues
            if (prBody.Contains("- Fixes/Closes/Resolves nanoFramework/Home#NNNN"))
            {
                commentContent = ":disappointed: If this PR does address any issue, you have to remove the content *Fixes/Closes/Resolves(...)* under 'Motivation and Context'";
            }
            else if ( prBody.Contains("Fixes/Closes/Resolves"))
            {
                commentContent = ":disappointed: You have to make up your mind on how this PR addresses the issue. It either **fixes**, **closes** or **resolves** it. Can't have them all...";
            }
            // TODO replace this with a regex
            //else if ( ( prBody.Contains("- Fixes") ||
            //            prBody.Contains("- Closes") ||
            //            prBody.Contains("- Resolves")) &&
            //            prBody.Contains(" #"))
            //{
            //    commentContent = ":disappointed: All our issues are tracked in Home repo. If this PR addresses an issue, make sure the reference to it follows the correct pattern: `nanoFramework/Home#NNNN`.";
            //}
            else
            {
                return true;
            }

            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\n{commentContent}{_fixRequestTagComment}\" }}";

            await SendGitHubRequest(
                payload.pull_request.comments_url.ToString(),
                comment,
                log);

            return false;
        }

        private static async Task FixCheckListAsync(dynamic payload, ILogger log)
        {
            // get PR body
            string prBody = payload.pull_request.body;

            // save hash for PR body
            var prBodyHash = prBody.GetHashCode();

            // fix any typos in check lists
            string prBodyFixed = prBody.
                Replace("[ x]", "[x]", StringComparison.InvariantCultureIgnoreCase).
                Replace("[x ]", "[x]", StringComparison.InvariantCultureIgnoreCase).
                Replace("[ X]", "[x]", StringComparison.InvariantCultureIgnoreCase).
                Replace("[X ]", "[x]", StringComparison.InvariantCultureIgnoreCase).
                Replace("[]", "[ ]", StringComparison.InvariantCultureIgnoreCase);

            if(prBodyHash != prBodyFixed.GetHashCode())
            {
                dynamic requestContent = new { body = prBodyFixed};

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
            string prBody = payload.pull_request.body;

            // check for expected/mandatory content

            // check for master PR template
            if (
                prBody.Contains(_prDescription) &&
                prBody.Contains(_prTypesOfChanges) &&
                prBody.Contains(_prChecklist))
            {
                // content looks good
                return true;
            }

            // community targets is not using template
            if (payload.repository.name == "nf-Community-Targets")
            {
                // community targets need to have ALL or at least one target selected for build
                if(
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
                    // 
                    string myComment = $"{{ \"body\": \"{_prCommunityTargetMissingTargetContent} {_fixCheckListComment}\" }}";

                    await SendGitHubRequest(
                        payload.pull_request.comments_url.ToString(),
                        myComment,
                        log);


                    // close PR
                    await ClosePR(
                        payload.pull_request.url.ToString(),
                        log);

                    return false;
                }
            }
            // documentation repo is not using template
            if (payload.repository.name == "nanoframework.github.io")
            {
                // don't perform any template check here
                return true;
            }
            else if (payload.repository.name == "nf-Community-Contributions")
            {
                // check content
                if ( prBody.Contains(_prChecklist))
                {
                    // check for missing check boxes
                    if(prBody.Contains("[ ]"))
                    {
                        // developer has left un-checked items in the to-do list

                        string myComment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\n{_prCommentChecklistWithOpenItemsTemplateContent}.{_fixRequestTagComment}\" }}";

                        await SendGitHubRequest(
                            payload.pull_request.comments_url.ToString(),
                            myComment,
                            log);

                        return true;
                    }
                    else
                    {
                        return true;
                    }
                }
            }

            // user seems to have ignored the template

            log.LogInformation($"User ignoring PR template. Adding comment before closing.");

            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\n{_prCommentUserIgnoringTemplateContent}.{_fixRequestTagComment}\" }}";

            await SendGitHubRequest(
                payload.pull_request.comments_url.ToString(),
                comment,
                log);

            // close PR
            await ClosePR(
                payload.pull_request.url.ToString(),
                log);

            return false;
        }

        private static async Task<IActionResult> ProcessClosedIssueAsync(
            Octokit.Issue issue,
            dynamic payload,
            ILogger log)
        {
            // get timeline of issue
            var issueTimeLine = await _octokitClient.Issue.Timeline.GetAllForIssue((int)payload.repository.id, issue.Number);
            var crossRefs = issueTimeLine.Where(t => t.Event == EventInfoState.Crossreferenced).OrderByDescending(t => t.CreatedAt);

            foreach(var eventInfo in crossRefs)
            {
                if(eventInfo.Source.Issue != null &&
                    eventInfo.Source.Issue.PullRequest != null &&
                    eventInfo.Source.Issue.State.Value == ItemState.Closed
                    )
                {
                    // this issue is linked to a PR that is closed
                    // it's safe to assume that it was just closed by it
                    
                    // clear all labels that don't belong here anymore
                    foreach(var label in issue.Labels)
                    {
                        if(label.Name == "up-for-grabs" ||
                           label.Name == "good first issue" ||
                           label.Name == "FOR DISCUSSION" ||
                           label.Name == "HELP WANTED" ||
                           label.Name.StartsWith("Status") ||
                           label.Name.Contains("trivial") ||
                           label.Name.Contains("Priority") ||
                           label.Name.Contains("pinned"))
                        {
                            _ = await _octokitClient.Issue.Labels.RemoveFromIssue((int)payload.repository.id, issue.Number, label.Name);
                        }
                    }

                    // set the appropriate label after the issue closure
                    foreach (var label in issue.Labels)
                    {
                        if (label.Name == "Type: Bug")
                        {
                            _ = await _octokitClient.Issue.Labels.AddToIssue((int)payload.repository.id, issue.Number, new string[] { "Status: FIXED" });
                        }
                        else if (label.Name == "Type: Chores"
                                 || label.Name == "Type: Enhancement"
                                 || label.Name == "Type: Feature request")
                        {
                            _ = await _octokitClient.Issue.Labels.AddToIssue((int)payload.repository.id, issue.Number, new string[] { "Status: DONE" });
                        }
                    }

                    // no need to process any other time line event
                    break;
                }
            }

            return new OkObjectResult("");
        }

        private static async Task<IActionResult> ProcessOpenOrEditIssueAsync(
            Octokit.Issue issue,
            dynamic payload,
            ILogger log)
        {
            // check for content that shouldn't be there and shows that the author hadn't read the instructions or is being lazy

            // flag that this is a open/reopen event
            bool isOpenAction = payload.action == "opened" || payload.action == "reopened";

            // flag if author is member or owner
            bool authorIsMemberOrOwner = payload.issue.author_association == "MEMBER" || payload.issue.author_association == "OWNER";

            log.LogInformation($"Processing issue #{issue.Number}");

            // check unwanted content
            if (issue.Body.Contains(_issueContentBeforePosting) ||
                    issue.Body.Contains(_issueContentRemoveContentInstruction))
            {
                log.LogInformation($"Unwanted content on issue. Adding comment before closing.");

                await _octokitClient.Issue.Comment.Create((int)payload.repository.id, issue.Number, $"Hi @{payload.issue.user.login},\r\n\r\n{_issueCommentUnwantedContent}.{_fixRequestTagComment}");

                // close issue
                await CloseIssue(
                    (int)payload.repository.id,
                    issue,
                    log);

                return new OkObjectResult("");
            }

            // check for expected/mandatory content
            bool issueIsFeatureRequest = false;
            bool issueIsBugReport = false;
            bool issueIsToolBugReport = false;
            bool issueIsClassLibBugReport = false;
            bool issueIsFwBugReport = false;
            bool issueIsTodo = false;

            if (issue.Body.Contains(_bugReportForClassLibTagComment))
            {
                issueIsClassLibBugReport = true;
            }
            if (issue.Body.Contains(_bugReportFirmwareTagComment))
            {
                issueIsFwBugReport = true;
            }
            if (issue.Body.Contains(_bugReportToolsTagComment))
            {
                issueIsToolBugReport = true;
            }
            if (issue.Body.Contains(_todoTagComment))
            {
                issueIsTodo = true;
            }

            if (issueIsClassLibBugReport ||
                issueIsFwBugReport ||
                issueIsToolBugReport)
            {
                // check for mandatory content
                if (issue.Body.Contains(_issueDescription))
                {
                    issueIsBugReport = true;

                    if ((issueIsClassLibBugReport ||
                            issueIsToolBugReport) &&
                        !issue.Body.Contains(_issueArea))
                    {
                        issueIsBugReport = false;

                        await _octokitClient.Issue.Comment.Create((int)payload.repository.id, issue.Number, $"Hi @{payload.issue.user.login},\r\n\r\n{_issueMissingAreaContent}\r\n{_fixRequestTagComment}");
                    }
                }  
            }
            else if(issue.Body.Contains(_featureRequestTagComment))
            {
                // check for mandatory content
                if (!issue.Body.Contains(_issueArea))
                {
                    await _octokitClient.Issue.Comment.Create((int)payload.repository.id, issue.Number, $"Hi @{payload.issue.user.login},\r\n\r\n{_issueMissingAreaContent}\r\n{_fixRequestTagComment}");
                }
                else
                {
                    // looks like a feature request
                    issueIsFeatureRequest = true;
                }                    
            }

            if (issueIsBugReport &&
                (issueIsFwBugReport ||
                    issueIsToolBugReport))
            {
                // fw and class lib bug reports have to include device caps

                if (issue.Body.Contains(_issueDeviceCaps))
                {
                    // check for valid content
                    if (issue.Body.Contains("System Information") &&
                        issue.Body.Contains("HAL build info:") &&
                        issue.Body.Contains("Firmware build Info:") &&
                        issue.Body.Contains("Assemblies:") &&
                        issue.Body.Contains("Native Assemblies:") &&
                        issue.Body.Contains("++ Memory Map ++") &&
                        issue.Body.Contains("++ Flash Sector Map ++") &&
                        issue.Body.Contains("++ Storage Usage Map ++"))
                    {
                        // device caps seems complete
                    }

                    log.LogInformation($"Incomplete or invalid device caps. Adding comment before closing.");

                    await _octokitClient.Issue.Comment.Create((int)payload.repository.id, issue.Number, $"Hi @{payload.issue.user.login},\r\n\r\n{_issueCommentInvalidDeviceCaps}\r\n{_fixRequestTagComment}");

                    // close issue
                    await CloseIssue(
                        (int)payload.repository.id,
                        issue,
                        log);

                    return new OkObjectResult("");
                }
            }

            if(issueIsTodo)
            {
                // users outside members team can't open TODOs
                // need to proceed with the author check
            }

            if (isOpenAction)
            {
                // does this issue look legit?
                if (issueIsFeatureRequest)
                {
                    // OK to label with feature request
                    log.LogInformation($"Adding 'feature request label.");

                    // add label
                    await _octokitClient.Issue.Labels.AddToIssue((int)payload.repository.id, issue.Number, new string[] { _labelTypeFeatureRequestName });
                }
                else if (issueIsBugReport)
                {
                    // OK to label with bug
                    log.LogInformation($"Adding 'bug label.");

                    // add label
                    await _octokitClient.Issue.Labels.AddToIssue((int)payload.repository.id, issue.Number, new string[] { _labelTypeBugName });

                    // OK to label for triage
                    log.LogInformation($"Adding 'triage label.");

                    // add the triage label
                    await _octokitClient.Issue.Labels.AddToIssue((int)payload.repository.id, issue.Number, new string[] { _labelStatusWaitingTriageName });
                }
                else
                {
                    if (!authorIsMemberOrOwner)
                    {
                        // process this only if author is NOT member or owner

                        // not sure what this is about...
                        log.LogInformation($"not sure what this issue is about. Adding comment before closing.");

                        await _octokitClient.Issue.Comment.Create((int)payload.repository.id, issue.Number, $"Hi @{payload.issue.user.login},\r\n\r\n{_issueCommentUnshureAboutIssueContent}\r\n{_fixRequestTagComment}");

                        // close issue
                        await CloseIssue(
                            (int)payload.repository.id,
                            issue,
                            log);
                    }

                    return new OkObjectResult("");
                }
            }
            else
            {
                // everything looks OK, remove all comments from nfbot
                await RemovenfbotCommentsAsync(
                    payload.issue.comments_url.ToString(),
                    log);
            }

            return new OkObjectResult("");
        }

        private static async Task RemovenfbotCommentsAsync(string comments_url, ILogger log)
        {
            // list all comments from nfbot
            JArray comments = (JArray)await GetGitHubRequest(
                                        comments_url,
                                        log);

            var commentsToRemove = comments.Where(c => c["user"]["login"].ToString() == "nfbot");

            foreach (var c in commentsToRemove)
            {
                // check for fix request comment, remove only the ones that have it
                if (c["body"].ToString().Contains(_fixRequestTagComment))
                {
                    await SendGitHubRequest(
                        c["url"].ToString(),
                        "",
                        log,
                        null,
                        "DELETE"
                        );
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

            return 0;
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
                commitMessage += "\\r\\n***UPDATE_DEPENDENTS***";
            }

            var publishReleaseLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiPublishReleaseName);
            if (publishReleaseLabel != null)
            {
                commitMessage += "\\r\\n***PUBLISH_RELEASE***";
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
                commitMessage += "\\r\\n***UPDATE_DEPENDENTS***";
            }

            var publishReleaseLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _labelCiPublishReleaseName);
            if (publishReleaseLabel != null)
            {
                commitMessage += "\\r\\n***PUBLISH_RELEASE***";
            }

            string mergeRequest = $"{{ \"commit_title\": \"{pull_request.title}\", \"commit_message\": \"{commitMessage}\", \"sha\": \"{pull_request.head.sha}\", \"merge_method\": \"squash\" }}";

            // request need to be a PUT
            await SendGitHubRequest(
                $"{pull_request.url.ToString()}/merge", 
                mergeRequest, 
                log, 
                "application/vnd.github.squirrel-girl-preview", "PUT");
        }

        public static async Task SquashAndMergePR(
            Octokit.PullRequest pull_request,
            bool skipCIBuild,
            ILogger log)
        {
            log.LogInformation($"Squash and merge PR {pull_request.Title}");

            // place holder for commit message (if any)
            string commitMessage = "";

            // check PR labels
            if (pull_request.Labels.Any(l => l.Name == _labelCiUpdateDependentsName))
            {
                commitMessage += "\\r\\n***UPDATE_DEPENDENTS***";
            }

            if (pull_request.Labels.Any(l => l.Name == _labelCiPublishReleaseName))
            {
                commitMessage += "\\r\\n***PUBLISH_RELEASE***";
            }

            if(skipCIBuild)
            {
                commitMessage += "\\r\\n***NO_CI***";
            }

            await _octokitClient.PullRequest.Merge(
                pull_request.Base.Repository.Id,
                pull_request.Number,
                new MergePullRequest() { 
                    MergeMethod = PullRequestMergeMethod.Squash, 
                    CommitTitle = pull_request.Title, 
                    CommitMessage = commitMessage });
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
