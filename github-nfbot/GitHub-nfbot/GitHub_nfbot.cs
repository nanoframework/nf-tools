//
// Copyright (c) 2020 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        // labels
        private const string _updateDependentsLabelName = "CI: Update Dependents";
        private const string _publishReleaseLabelName = "CI: Publish Release";

        [FunctionName("GitHub-nfbot")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("GitHub nfbot processing request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic payload = JsonConvert.DeserializeObject(requestBody);

            #region process PR events

            // process PR opened
            if (payload.pull_request != null && payload.action == "opened")
            {
                log.LogInformation($"Processing new PR #{payload.pull_request.number}:{payload.pull_request.title} submitted by {payload.pull_request.user.login}");

                ////////////////////////////////////////////////////////////
                // processing exceptions

                // dependabot BOT
                if (payload.pull_request.user.login == "dependabot[bot]")
                {
                    return new OkObjectResult(""); ;
                }
                ////////////////////////////////////////////////////////////

                // post comment with thank you message, except if it's from nfbot
                if (payload.pull_request.user.login != "nfbot")
                {
                    log.LogInformation($"Comment with thank you note.");

                    string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nA human will be reviewing it shortly. :wink:\" }}";
                    await SendGitHubRequest(
                        payload.pull_request.comments_url.ToString(),
                        comment,
                        log);

                    // add thumbs up reaction in PR main message
                    await SendGitHubRequest(
                        $"{payload.pull_request.issue_url.ToString()}/reactions",
                        "{ \"content\" : \"+1\" }",
                        log,
                        "application/vnd.github.squirrel-girl-preview");
                }

                // special processing for nfbot commits
                if (payload.pull_request.user.login == "nfbot")
                {
                    // this is a [version update] commit
                    string prBody = payload.pull_request.body;
                    if (prBody.Contains("[version update]"))
                    {
                        log.LogInformation($"Adding 'Type: dependencies' label to PR.");

                        // add the Type: dependency label
                        await SendGitHubRequest(
                            $"{payload.pull_request.issue_url.ToString()}/labels", "[ \"Type: dependencies\" ]",
                            log,
                            "application/vnd.github.squirrel-girl-preview");
                    }
                }
            }

            // process PR closed
            else if (payload.pull_request != null && payload.action == "closed")
            {
                log.LogInformation($"Processing PR closed event...");

                // check for PR related with [version update] authored by nfbot
                if (payload.pull_request.body.ToString().Contains("[version update]") && payload.pull_request.user.login == "nfbot")
                {
                    // get origin branch
                    var originBranch = payload.pull_request.head.label.ToString().Replace("nanoframework:", "");

                    if (originBranch.Contains("develop-nfbot/update-version") ||
                        originBranch.Contains("develop-nfbot/update-dependencies"))
                    {
                        // delete this branch
                        await SendGitHubDeleteRequest(
                            $"{payload.pull_request.head.repo.url.ToString()}/git/refs/heads/{originBranch}",
                            log);
                    }
                }
            }

            #endregion

            #region process review

            // process review submitted
            else if (payload.review != null && payload.action == "submitted")
            {
                // serious candidate of a PR check
                log.LogInformation($"Processing review submitted event...");
            }

            #endregion

            #region process check run

            else if (payload.check_run != null && payload.check_run.conclusion == "success")
            {
                // serious candidate of a PR check
                log.LogInformation($"Processing check success event...");

                // list all open PRs from nfbot
                JArray openPrs = (JArray)(await GetGitHubRequest(
                    $"{payload.repository.url.ToString()}/pulls?user=nfbot",
                    log));

                var matchingPr = openPrs.FirstOrDefault(p => p["head"]["sha"] == payload.check_run.head_sha);

                if (matchingPr != null)
                {
                    // get PR
                    var pr = await GetGitHubRequest($"{matchingPr["url"]}", log);

                    if (pr.user.login == "nfbot" && pr.body.ToString().Contains("[version update]"))
                    {
                        // get status checks for PR
                        var checkSatus = await GetGitHubRequest(
                            $"{pr.head.repo.url.ToString()}/commits/{pr.head.sha}/check-runs",
                            log);

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
                                break;
                            }
                        }

                        if (allChecksSuccessfull)
                        {
                            log.LogInformation($"Adding 'Publish release flag to PR.");

                            // add the Publish release label
                            await SendGitHubRequest(
                                $"{pr.issue_url.ToString()}/labels", $"[ \"{_publishReleaseLabelName}\" ]",
                                log,
                                "application/vnd.github.squirrel-girl-preview");

                            // list labels for repository
                            JArray repoLabels = (JArray)(await GetGitHubRequest(
                                $"{payload.repository.url.ToString()}/labels",
                                log));

                            var updateDependentsLabel = repoLabels.FirstOrDefault(l => l["name"].ToString() == _updateDependentsLabelName);

                            if (updateDependentsLabel != null)
                            {
                                // this repo has dependents, add label
                                log.LogInformation($"Adding 'update dependents flag to PR.");

                                // add the Type: dependency label
                                await SendGitHubRequest(
                                    $"{pr.issue_url.ToString()}/labels", $"[ \"{_updateDependentsLabelName}\" ]",
                                    log,
                                    "application/vnd.github.squirrel-girl-preview");
                            }

                            // checks are all successful
                            // merge PR with squash
                            await MergePR(pr, log);
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

            else if (payload.repository != null && payload.action == "started")
            {
                // we have a user staring the repo
                // send message to Slack channel

                log.LogInformation($"Processing new repo stared event from {payload.sender.login.ToString()}");

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

            return new OkObjectResult("");
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

        public static async Task SendGitHubDeleteRequest(
            string url,
            ILogger log)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("username", "version"));

                // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
                // "Username:Password" or "Username:PersonalAccessToken"
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

                log.LogInformation($"Delete URL {url}");

                HttpResponseMessage response = await client.DeleteAsync(url);

                log.LogInformation($"Delete result {response.StatusCode} content {await response.Content.ReadAsStringAsync()} .");
            }
        }

        public static async Task SendGitHubRequest(
            string url,
            string requestBody,
            ILogger log,
            string acceptHeader = null,
            string verb = "POST")
        {
            using (var client = new HttpClient())
            {
                HttpResponseMessage response = null;

                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("username", "version"));

                // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
                // "Username:Password" or "Username:PersonalAccessToken"
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

                // if specified, add Accept HTTP header for GitHub preview APIs
                if (acceptHeader != null)
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.squirrel-girl-preview"));
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

                log.LogInformation($"Request result {response.StatusCode}");
                log.LogInformation($"Request result {response.StatusCode} content >>{await response.Content.ReadAsStringAsync()}<< .");
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

                // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
                // "Username:Password" or "Username:PersonalAccessToken"
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

                log.LogInformation($"Request URL {url}");

                HttpResponseMessage response = await client.GetAsync(url);

                return JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
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

            var updateDependentsLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _updateDependentsLabelName);
            if (updateDependentsLabel != null)
            {
                commitMessage += "\\r\\n***UPDATE_DEPENDENTS***";
            }

            var publishReleaseLabel = prLabels.FirstOrDefault(l => l["name"].ToString() == _publishReleaseLabelName);
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
    }
}
