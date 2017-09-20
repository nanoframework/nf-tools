# GitHub DCO Check

This is a simple tool that allows setting [Developer Certificate of Origin](https://developercertificate.org/)
status checks that must pass before branches can be merged. It enables a maintainer
to examines pull request commit messages, check presence of `Signed-off-by` line,
validate that real user name is present, or the commit meets 'obvious fix' rule.

## Usage

* In Windows Credentials Manager create a Generic Windows credential for `git:https://github.com`,
 with your GitHub username and password. Depending on your git client, it might
 already be there.
* Launch `GitHubDcoCheck.exe`, enter repository owner and name,
* Press **Pull Requests** button to retrieve pull requests,
* Select a pull request and then a commit to examine its message,
* Select an appropriate DCO status
* Press **Set** button to set the status for the selected commit.

## License

This project is licensed under the [Apache 2.0 license](http://www.apache.org/licenses/LICENSE-2.0).