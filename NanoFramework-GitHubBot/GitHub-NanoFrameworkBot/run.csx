//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

#r "Newtonsoft.Json"

using Newtonsoft.Json;
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
        if(payload.pull_request.user.login == "dependabot[bot]")
        {
            return;
        }
        ////////////////////////////////////////////////////////////

        // post comment with thank you message
        string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nA human will be reviewing it shortly. :wink:\" }}";
        await SendGitHubRequest(payload.pull_request.comments_url.ToString(), comment, log);

        // add thumbs up reaction in PR main message
        await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/reactions", "{ \"content\" : \"+1\" }", log, "application/vnd.github.squirrel-girl-preview");

        //log.Info($"{payload.pull_request.user.login} submitted pull request #{payload.pull_request.number}:{payload.pull_request.title}. Comment with thank you note.");
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

        //log.Info($"Delete URL {url}");

        HttpResponseMessage response = await client.DeleteAsync(url);

        //log.Info($"Delete result {response.StatusCode} content {await response.Content.ReadAsStringAsync()} .");
    }
}

public static async Task SendGitHubRequest(string url, string requestBody, TraceWriter log, string acceptHeader = null)
{
    using (var client = new HttpClient())
    {
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
        log.Info($"Request content >>{content.ReadAsStringAsync()}<<");

        HttpResponseMessage response = await client.PostAsync(url, content);


        log.Info($"Request result {response.StatusCode}");
        //log.Info($"Request result {response.StatusCode} content >>{await response.Content.ReadAsStringAsync()}<< .");
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

        //log.Info($"comment: {requestBody}to {url} ");

        HttpResponseMessage response = await client.GetAsync(url);

        return JsonConvert.DeserializeObject(await response.Content.ReadAsStringAsync());
    }
}
