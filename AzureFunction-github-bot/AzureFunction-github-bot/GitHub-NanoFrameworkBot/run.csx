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

        // get commit collection for this PR
        dynamic commitsForThisPull = await GetGitHubRequest(payload.pull_request.commits_url.ToString(), log);

        // check commit collection for DCO or obvious fix message
        var checkResult = await CheckCommitMessages(commitsForThisPull, log);

        if (checkResult.Item3)
        {
            // DCO check is valid

            // add comment with thank you note!
            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nEverything seems to be in order.\\r\\nA human will be reviewing it shortly. :wink:\" }}";
            // post comment with thank you message
            await SendGitHubRequest(payload.pull_request.comments_url.ToString(), comment, log);

            if (checkResult.Item1 > 0 && checkResult.Item2 == 0)
            {
                // commits with signed-off
                // set label to DCO check valid
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-check-valid\" ]", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveSignOffString}\" }}", log);
            }
            else if (checkResult.Item1 > 0 || checkResult.Item2 > 0)
            {
                // mixed commits with signed-off and obvious fix
                // set label to DCO check valid
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-check-valid\" ]", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveSignOffOrObviousFixString}\" }}", log);
            }
            else
            {
                // must be all obvious fix
                // set label to DCO not required
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-not-required\" ]", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveObviousFixString}\" }}", log);
            }
        }
        else
        {
            // DCO check failed

            // add comment with thank you note!
            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nI'm nanoFramework bot.\\r\\n Thank you for your contribution!\\r\\n\\r\\nIt seems that the DCO or 'obvious fix' mention are missing in the commit(s) message(s).\\r\\nPlease make sure that you've:\\r\\n 1. Followed the [Contribution Workflow](https://github.com/nanoframework/nf-interpreter/blob/master/docs/project-documentation/contributing-workflow.md).\\r\\n 2. Signed off the commit(s) following the instructions about the [Developer Certificate of Origin](https://github.com/nanoframework/nf-interpreter/blob/master/docs/project-documentation/contributing-workflow.md#developer-certificate-of-origin). \\r\\n\\r\\nA human will be reviewing it shortly. :wink:\" }}";
            // post comment with thank you message
            await SendGitHubRequest(payload.pull_request.comments_url.ToString(), comment, log);

            // set label to DCO required
            await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-required\" ]", log);

            // set status to DCO failed
            await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"failure\" , \"description\" : \"{commitsMissingSignOffOrObviousFixString}\" }}", log);
        }

        // add thumbs up reaction in PR main message
        await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/reactions", "{ \"content\" : \"+1\" }", log, "application/vnd.github.squirrel-girl-preview");

        //log.Info($"{payload.pull_request.user.login} submitted pull request #{payload.pull_request.number}:{payload.pull_request.title}. Comment with thank you note.");
    }
    else if (payload.pull_request != null && (payload.action == "edited" || payload.action == "reopened"))
    {
        // PR was edited or reopened 

        log.Info($"Processing PR #{payload.pull_request.number}:{payload.pull_request.title} changes");

        // get commit collection for this PR
        dynamic commitsForThisPull = await GetGitHubRequest(payload.pull_request.commits_url.ToString(), log);

        // check commit collection for DCO or obvious fix message
        var checkResult = await CheckCommitMessages(commitsForThisPull, log);

        if (checkResult.Item3)
        {
            // DCO check is valid

            if (checkResult.Item1 > 0 && checkResult.Item2 == 0)
            {
                // commits with signed-off
                
                // set label to DCO check valid
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-check-valid\" ]", log);

                // delete DCO required label, if there
                await SendGitHubDeleteRequest($"{payload.pull_request.issue_url.ToString()}/labels/DCO-required", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveSignOffString}\" }}", log);
            }
            else if (checkResult.Item1 > 0 || checkResult.Item2 > 0)
            {
                // mixed commits with signed-off and obvious fix
                
                // set label to DCO check valid
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-check-valid\" ]", log);

                // delete DCO required label, if there
                await SendGitHubDeleteRequest($"{payload.pull_request.issue_url.ToString()}/labels/DCO-required", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveSignOffOrObviousFixString}\" }}", log);
            }
            else
            {
                // must be all obvious fix
                
                // set label to DCO not required
                await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-not-required\" ]", log);

                // delete DCO required label, if there
                await SendGitHubDeleteRequest($"{payload.pull_request.issue_url.ToString()}/labels/DCO-required", log);

                // set status to DCO checked
                await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"{allCommitsHaveObviousFixString}\" }}", log);
            }
        }
        else
        {
            // DCO check failed

            // post comment with warning
            string comment = $"{{ \"body\": \"Hi @{payload.pull_request.user.login},\\r\\n\\r\\nIt seems that the DCO or 'obvious fix' mention are missing in some of the commit(s) message(s).\\r\\nPlease make sure that you've signed off the commit(s) following the instructions regarding the [Developer Certificate of Origin](https://github.com/nanoframework/nf-interpreter/blob/master/docs/project-documentation/contributing-workflow.md#developer-certificate-of-origin).\\r\\n\\r\\nA human will be reviewing this shortly. :wink:\" }}";
            await SendGitHubRequest(payload.pull_request.comments_url.ToString(), comment, log);

            // delete DCO not required label, if there
            await SendGitHubDeleteRequest($"{payload.pull_request.issue_url.ToString()}/labels/DCO-not-required", log);
            // delete DCO check valid label, if there
            await SendGitHubDeleteRequest($"{payload.pull_request.issue_url.ToString()}/labels/DCO-check-valid", log);

            // set label to DCO required
            await SendGitHubRequest($"{payload.pull_request.issue_url.ToString()}/labels", "[ \"DCO-required\" ]", log);

            // set status to DCO failed
            await SendGitHubRequest($"{payload.pull_request.statuses_url.ToString()}", $"{{ \"context\" : \"DCO\" , \"state\" : \"failure\" , \"description\" : \"{commitsMissingSignOffOrObviousFixString}\" }}", log);
        }
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
            channel = "#github-pulse",
            username = "nFbot on GitHub",
            text = $"GitHub user <{payload.forkee.owner.html_url.ToString()}|{payload.forkee.owner.login.ToString()}> has :fork_and_knife: {payload.forkee.name.ToString()}!",
            icon_url = payload.forkee.owner.avatar_url.ToString(),
        };

        // Add the SLACK_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
        using (var client = new HttpClient())
        {
            var res = await client.PostAsync(Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL"), new FormUrlEncodedContent(new[]
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
            channel = "#github-pulse",
            username = "nFbot on GitHub",
            text = $"GitHub user <{payload.sender.html_url.ToString()}|{payload.sender.login.ToString()}> has :star: {payload.repository.name.ToString()}!",
            icon_url = payload.sender.avatar_url.ToString(),
        };

        // Add the SLACK_WEBHOOK_URL as an app setting, Value for the app setting is the URL from Slack API integration
        using (var client = new HttpClient())
        {
            var res = await client.PostAsync(Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL"), new FormUrlEncodedContent(new[]
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

    foreach(dynamic item in commitsCollection)
    {
        // get commit message
        //log.Info($"Commit message: {item.commit.message.ToString()}");
        //log.Info($"Commit author name: {item.commit.author.name.ToString()}");
        //log.Info($"Commit author email: {item.commit.author.email.ToString()}");

        // check for sign-off message 
        if (item.commit.message.ToString().Contains($"Signed-off-by: {item.commit.author.name.ToString()} <{item.commit.author.email.ToString()}>"))
        {
            signedOffCount++;

            // set status to DCO checked
            // need to hack this URL because the API is not exposing the URL for setting individual commit status
            await SendGitHubRequest($"{item.url.ToString().Replace("/commits/", "/statuses/")}", "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit has a DCO Signed-off-by.\" }", log);
        }
        else
        {
            // this commit message isn't signed-off 
            // check for obvious fix message
            if((item.commit.message.ToString().Contains("obvious fix") ||
                item.commit.message.ToString().Contains("Obvious fix")))
            {
                obviousFixCount++;

                // set status to DCO checked
                // need to hack this URL because the API is not exposing the URL for setting individual commit status
                await SendGitHubRequest($"{item.url.ToString().Replace("/commits/", "/statuses/")}", "{ \"context\" : \"DCO\" , \"state\" : \"success\" , \"description\" : \"This commit is an obvious fix.\" }", log);
            }
            else
            {
                // no obvious fix message either, flag to NOT checked if not already...
                checkPass ^= false;

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

        //log.Info($"Request URL {url}");

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(url, content);

        //log.Info($"Request result {response.StatusCode} content {await response.Content.ReadAsStringAsync()} .");
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
