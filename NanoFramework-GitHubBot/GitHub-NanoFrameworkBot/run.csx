//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

#r "Newtonsoft.Json"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

// strings to be used in messages and comments
private const string allCommitsHaveSignOffString = "All commits have a DCO Signed-off-by.";
private const string allCommitsHaveObviousFixString = "All commits have an obvious fix statement.";
private const string allCommitsHaveSignOffOrObviousFixString = "All commits have a DCO Signed-off-by or an obvious fix statement.";
private const string commitsMissingSignOffOrObviousFixString = "Some commits are missing the DCO Signed-off-by or obvious fix statement.";

public static async Task Run(dynamic payload, TraceWriter log)
{

    #region process PR events

    // process PR opened
    if (payload.pull_request != null && payload.action == "opened")
    {
        log.Info($"Processing new PR #{payload.pull_request.number}:{payload.pull_request.title} submitted by {payload.pull_request.user.login}");

        ////////////////////////////////////////////////////////////
        // processing exceptions

        // dependabot BOT
        if (payload.pull_request.user.login == "dependabot[bot]")
        {
            return;
        }
        ////////////////////////////////////////////////////////////


        // post comment with thank you message, except if it's from nfbot
        if (payload.pull_request.user.login != "nfbot")
        {
            log.Info($"Comment with thank you note.");

            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nA human will be reviewing it shortly. :wink:\" }}";
            await SendGitHubRequest(payload.pull_request.comments_url.ToString(), comment, log);

            // add thumbs up reaction in PR main message
            await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/reactions", "{ \"content\" : \"+1\" }", log, "application/vnd.github.squirrel-girl-preview");
        }

        // special processing for nfbot commits
        if (payload.pull_request.user.login == "nfbot")
        {
            // this is a [version update] commit
            string prBody = payload.pull_request.body;
            if (prBody.Contains("[version update]"))
            {
                log.Info($"Adding 'Type: dependencies' label to PR.");

                // add the Type: dependency label
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"Type: dependencies\" ]", log, "application/vnd.github.squirrel-girl-preview");
            }
        }
    }

    // process PR closed
    else if (payload.pull_request != null && payload.action == "closed")
    {
        log.Info($"Processing PR closed event...");

        // check for PR related with [version update] authored by nfbot
        if (payload.pull_request.body.ToString().Contains("[version update]") && payload.pull_request.user.login == "nfbot")
        {
            // get origin branch
            var originBranch = payload.pull_request.head.label.ToString().Replace("nanoframework:", "");

            if (originBranch.Contains("develop-nfbot/update-version") ||
                originBranch.Contains("develop-nfbot/update-dependencies"))
            {
                // delete this branch
                await SendGitHubDeleteRequest($"{payload.pull_request.head.repo.url.ToString()}/git/refs/heads/{originBranch}", log);
            }
        }
    }

    #endregion


    #region process review

    // process review submitted
    else if (payload.review != null && payload.action == "submitted")
    {
        // check for PR related with [version update] authored by nfbot
        if (payload.pull_request.body.ToString().Contains("[version update]") && payload.pull_request.user.login == "nfbot")
        {
            log.Info($"Processing review submitted event...");

            // get PR combined status 
            var prStatus = await GetGitHubRequest($"{payload.pull_request.head.repo.url.ToString()}/commits/{payload.review.commit_id}/status", log);

            if (prStatus.state == "success")
            {
                // PR is now approved and checks are all successful
                // merge PR with squash
                await MergePR(payload.pull_request, log);
            }
        }
    }

    #endregion

    # region process check state

    else if (payload.state == "success" && payload.commit.commit.author.name == "nfbot" && payload.commit.commit.message.ToString().Contains("[version update]"))
    {
        // serious candidate of a PR check
        log.Info($"Processing check success event...");

        // list all open PRs from nfbot
        JArray openPrs = (JArray)(await GetGitHubRequest($"{payload.repository.url.ToString()}/pulls?user=nfbot", log));

        var matchingPr = openPrs.FirstOrDefault(p => p["head"]["sha"] == payload.commit.sha);
        //var pr = from p in openPrs.Children()["head"].sha select p == payload.commit.sha;

        if (matchingPr != null)
        {
            // get PR
            var pr = await GetGitHubRequest($"{matchingPr["url"].ToString()}", log);

            // list reviews for this PR
            JArray prReviews = (JArray)(await GetGitHubRequest($"{matchingPr["url"].ToString()}/reviews", log));

            // list APPROVED reviews
            var approvedReviews = prReviews.Where(r => r["state"].ToString() == "APPROVED");

            if (approvedReviews.Count() >= 1)
            {
                // PR is approved and checks are all successful
                // merge PR with squash
                await MergePR(pr, log);
            }
        }
    }

    #endregion

    #region process push event

    // process push
    else if (payload.commits != null && payload.pusher != null)
    {
        log.Info($"Processing push event...");
    }

    #endregion


    #region process fork event 

    else if (payload.forkee != null)
    {
        // we have a new fork
        // send message to Slack channel

        log.Info($"Processing new fork event from {payload.forkee.owner.login.ToString()}");

        //log.Info($">> : {payload.forkee.name.ToString()}");

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
            var res = await client.PostAsync(Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL"), new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
            }));
        }
    }

    #endregion


    #region process stared event 

    else if (payload.repository != null && payload.action == "started")
    {
        // we have a user staring the repo
        // send message to Slack channel

        log.Info($"Processing new repo stared event from {payload.sender.login.ToString()}");

        var slackPayload = new
        {
            text = $"GitHub user <{payload.sender.html_url.ToString()}|{payload.sender.login.ToString()}> has :star: {payload.repository.name.ToString()}!",
            icon_url = payload.sender.avatar_url.ToString(),
        };

        // Add the DISCORD_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
        // this is possible because Discord webhooks API supports Slack compatible webhooks
        // see https://discordapp.com/developers/docs/resources/webhook#execute-slackcompatible-webhook
        using (var client = new HttpClient())
        {
            var res = await client.PostAsync(Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL"), new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("payload", JsonConvert.SerializeObject(slackPayload))
            }));
        }
    }

    #endregion

}

public static async Task<Tuple<int, int, bool>> CheckCommitMessages(dynamic commitsCollection, TraceWriter log)
{
    int signedOffCount = 0;
    int obviousFixCount = 0;
    bool checkPass = true;

    foreach (dynamic item in commitsCollection)
    {
        // get commit message
        log.Info($"Commit sha: [{item.sha.ToString()}]");
        log.Info($"Commit message: >>{item.commit.message.ToString()}<<");
        //log.Info($"Commit message lenght: {item.commit.message.ToString().Length}");
        //log.Info($"Commit message '{item.commit.message.ToString()[0].ToString()}' + '{item.commit.message.ToString()[1].ToString()}' + '{item.commit.message.ToString()[2].ToString()}'");
        log.Info($"Commit author name: >>{item.commit.author.name.ToString()}<<");
        log.Info($"Commit author email: >>{item.commit.author.email.ToString()}<<");

        // check for sign-off message 
        if (item.commit.message.ToString().Contains($"Signed-off-by: {item.commit.author.name.ToString()} <{item.commit.author.email.ToString()}>"))
        {
            log.Info($"CheckCommitMessages: Signed-off-by comment verified");

            signedOffCount++;

            // set status to DCO checked
            // need to hack this URL because the API is not exposing the URL for setting individual commit status
            await SendGitHubRequest($"{item.url.ToString().Replace("/commits/", "/statuses/")}", "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit has a DCO Signed-off-by.\" }", log);
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
                log.Info($"CheckCommitMessages: Obvious fix comment verified");

                obviousFixCount++;

                // set status to DCO checked
                // need to hack this URL because the API is not exposing the URL for setting individual commit status
                await SendGitHubRequest($"{item.url.ToString().Replace("/commits/", "/statuses/")}", "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit is an obvious fix.\" }", log);
            }
            else
            {
                // no obvious fix message either
                log.Info($"CheckCommitMessages: no valid comment found");

                // flag to NOT checked if not already...
                checkPass &= false;

                // set status to DCO required
                // need to hack this URL because the API is not exposing the URL for setting individual commit status
                await SendGitHubRequest($"{item.url.ToString().Replace("/commits/", "/statuses/")}", "{ \"context\" : \"DCO\" , \"state\" : \"failure\" , \"description\" : \"This commit is missing either the DCO Signed-off-by or the obvious fix statement.\" }", log);
            }
        }
    }

    return Tuple.Create(signedOffCount, obviousFixCount, checkPass);
}

public static async Task SendGitHubDeleteRequest(string url, TraceWriter log)
{
    using (var client = new HttpClient())
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("username", "version"));

        // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
        // "Username:Password" or "Username:PersonalAccessToken"
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

        log.Info($"Delete URL {url}");

        HttpResponseMessage response = await client.DeleteAsync(url);

        log.Info($"Delete result {response.StatusCode} content {await response.Content.ReadAsStringAsync()} .");
    }
}

public static async Task SendGitHubRequest(string url, string requestBody, TraceWriter log, string acceptHeader = null, string verb = "POST")
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

        log.Info($"Request URL {url}");

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        log.Info($"Request content >>{await content.ReadAsStringAsync()}<<");

        if (verb == "POST")
        {
            response = await client.PostAsync(url, content);
        }
        else if (verb == "PUT")
        {
            response = await client.PutAsync(url, content);
        }

        log.Info($"Request result {response.StatusCode}");
        log.Info($"Request result {response.StatusCode} content >>{await response.Content.ReadAsStringAsync()}<< .");
    }
}

public static async Task<dynamic> GetGitHubRequest(string url, TraceWriter log)
{
    using (var client = new HttpClient())
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("username", "version"));

        // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
        // "Username:Password" or "Username:PersonalAccessToken"
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS"));

        log.Info($"Request URL {url}");

        HttpResponseMessage response = await client.GetAsync(url);

        return JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
    }
}

public static async Task MergePR(dynamic pull_request, TraceWriter log)
{
    log.Info($"Merge PR {pull_request.title}");

    // if the repo is nf-interpreter need to skip CI because a build will be manually triggered after the last version is updated
    // the message has to include ***NO_CI*** so 
    if (pull_request.head.repo.name == "nf-interpreter")
    {
        string mergeRequest = $"{{ \"commit_title\": \"{pull_request.title} ***NO_CI***\", \"commit_message\": \"\", \"sha\": \"{pull_request.head.sha}\", \"merge_method\": \"squash\" }}";

        // request need to be a PUT
        await SendGitHubRequest($"{pull_request.url.ToString()}/merge", mergeRequest, log, "application/vnd.github.squirrel-girl-preview", "PUT");
    }
    else
    {
        string mergeRequest = $"{{ \"commit_title\": \"{pull_request.title}\", \"commit_message\": \"\", \"sha\": \"{pull_request.head.sha}\", \"merge_method\": \"squash\" }}";

        // request need to be a PUT
        await SendGitHubRequest($"{pull_request.url.ToString()}/merge", mergeRequest, log, "application/vnd.github.squirrel-girl-preview", "PUT");
    }
}
