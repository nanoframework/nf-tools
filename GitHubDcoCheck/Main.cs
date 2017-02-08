//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
namespace GitHubDcoCheck
{
    using System;
    using System.Linq;
    using System.Windows.Forms;
    using GitHubDcoCheck.Properties;
    using Octokit;

    public partial class Main : Form
    {
        private GitHubClient gitHubClient;
        private long repositoryId;

        public Main()
        {
            InitializeComponent();
        }

        private PullRequest SelectedPullRequest
        {
            get
            {
                return SelectedItem<PullRequest>(listViewPullRequests);
            }
        }

        private PullRequestCommit SelectedCommit
        {
            get
            {
                return SelectedItem<PullRequestCommit>(listViewCommits);
            }
        }

        private void Main_Load(object sender, EventArgs e)
        {
            // TODO: History (user settings)
            textBoxUserName.Text = Environment.UserName;

            const string contextDCO = "DCO";

            comboBoxStatus.Items.AddRange(new [] {
                // Bot
                //new NewCommitStatus { Context = contextDCO, State = CommitState.Pending, Description = "This commit has a DCO Signed-off-by. Pending human verification." },
                //new NewCommitStatus { Context = contextDCO, State = CommitState.Pending, Description = "This commit declared that it is an obvious fix. Pending human verification." },
                // Human
                new NewCommitStatus { Context = contextDCO, State = CommitState.Error,   Description = "This commit does not have a DCO Signed-off-by." },
                new NewCommitStatus { Context = contextDCO, State = CommitState.Success, Description = "This commit has a DCO Signed-off-by." },
                new NewCommitStatus { Context = contextDCO, State = CommitState.Success, Description = "This commit declared that it is an obvious fix." },
            });

            UpdateControls();
        }

        private bool EnsureGitHubClient()
        {
            if(gitHubClient == null)
            {
                // Load git credentials from Windows credential management store (vault)
                var credential = new CredentialManagement.Credential { Target = "git:https://github.com" };
                if(!credential.Exists())
                {
                    MessageBox.Show(Resources.Error_CredentialNotFound.FormatWith(credential.Target));
                    return false;
                }
                if(!credential.Load())
                {
                    MessageBox.Show(Resources.Error_CredentialLoadFailed.FormatWith(credential.Target));
                    return false;
                }

                gitHubClient = new GitHubClient(new ProductHeaderValue(System.Windows.Forms.Application.ProductName));
                gitHubClient.Credentials = new Credentials(credential.Username, credential.Password);

                try
                {
                    var repo = gitHubClient.Repository.Get(textBoxOwner.Text, textBoxRepository.Text).Result;
                    repositoryId = repo.Id;

                    return true;
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
            return false;
        }

        private void buttonPullRequests_Click(object sender, EventArgs e)
        {
            if(!EnsureGitHubClient())
            {
                return;
            }

            try
            {
                listViewPullRequests.BeginUpdate();
                listViewPullRequests.Items.Clear();

                // TODO: Async
                var pullRequests = gitHubClient.PullRequest.GetAllForRepository(repositoryId).Result;
                foreach(var pr in pullRequests)
                {
                    var item = new ListViewItem(new[] {
                        pr.Number.ToString(), pr.Title, pr.State.ToString(),
                    });
                    listViewPullRequests.Items.Add(item).Tag = pr;
                }
            }
            finally
            {
                listViewPullRequests.EndUpdate();
                UpdateControls();
            }
        }

        private void listViewPullRequests_SelectedIndexChanged(object sender, EventArgs e)
        {
            var pullRequest = SelectedPullRequest;
            try
            {
                listViewCommits.BeginUpdate();
                listViewCommits.Items.Clear();

                if(pullRequest != null)
                {
                    foreach(var commit in gitHubClient.PullRequest.Commits(repositoryId, pullRequest.Number).Result)
                    {
                        var item = new ListViewItem(new[] {
                            commit.Sha, commit.Commit.Author.Name, commit.Commit.Author.Email });
                        item.Tag = commit;
                        listViewCommits.Items.Add(item);
                    }
                }
            }
            finally
            {
                listViewCommits.EndUpdate();
                UpdateControls();
            }
        }

        private void listViewCommits_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBoxMessage.Text = SelectedCommit?.Commit.Message ?? "";

            UpdateControls();
        }

        private void onControl_Changed(object sender, EventArgs e)
        {
            UpdateControls();
        }

        private void buttonSet_Click(object sender, EventArgs e)
        {
            var commit = SelectedCommit;
            System.Diagnostics.Debug.Assert(commit != null, "Selected commit cannot be null");

            var commitStatus = comboBoxStatus.SelectedItem as NewCommitStatus;
            System.Diagnostics.Debug.Assert(commitStatus != null);

            try
            {
                var sha = commit.Sha;
                var result = gitHubClient.Repository.Status.Create(repositoryId, sha, commitStatus).Result;

                MessageBox.Show("OK");
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void UpdateControls()
        {
            var enabled = (SelectedPullRequest != null);
            listViewCommits.Enabled = enabled;

            enabled = enabled && (SelectedCommit != null);
            comboBoxStatus.Enabled = enabled;

            buttonSet.Enabled = enabled && (comboBoxStatus.SelectedItem != null);
        }

        private static T SelectedItem<T>(ListView listView)
        {
            return listView.SelectedItems.Cast<ListViewItem>().Select(x => (T)x.Tag).FirstOrDefault();
        }
    }
}
