// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace nanoFramework.Migrate.Tests;

public class VerifyTests
{
    // A fake build runner so the result/decision logic is testable without spawning a
    // real build: it returns a scripted (exit code, stdout, stderr) per target.
    private sealed class FakeRunner : IBuildRunner
    {
        private readonly Dictionary<string, (int, string, string)> _byName;
        public bool IsAvailable { get; }

        public FakeRunner(bool available, Dictionary<string, (int, string, string)>? byName = null)
        {
            IsAvailable = available;
            _byName = byName ?? new();
        }

        public (int exitCode, string stdout, string stderr) Build(string target)
        {
            var key = Path.GetFileName(target);
            return _byName.TryGetValue(key, out var r) ? r : (0, "ok", "");
        }
    }

    [Fact]
    public void Interpret_success_has_no_error_tail()
    {
        var outcome = SolutionBuilder.Interpret("Foo.sln", 0, "Build succeeded", "");
        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Skipped);
        Assert.Equal("", outcome.ErrorTail);
    }

    [Fact]
    public void Interpret_failure_captures_error_tail()
    {
        var outcome = SolutionBuilder.Interpret("Foo.sln", 1,
            "line1\nFoo.cs(3,5): error CS1002: ; expected\nBuild FAILED", "");
        Assert.False(outcome.Succeeded);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Contains("error CS1002", outcome.ErrorTail);
    }

    [Fact]
    public void Tail_prefers_error_lines_and_caps_length()
    {
        var stdout = string.Join("\n", Enumerable.Range(0, 50).Select(i => $"info line {i}"))
            + "\nProgram.cs(1,1): error CS0103: missing\n";
        var tail = SolutionBuilder.Tail(stdout, "", SolutionBuilder.ErrorTailLines);
        Assert.Contains("error CS0103", tail);
        Assert.True(tail.Split('\n').Length <= SolutionBuilder.ErrorTailLines);
    }

    [Fact]
    public void VerifyAll_when_dotnet_missing_marks_every_target_skipped_and_signals_once()
    {
        var builder = new SolutionBuilder(new FakeRunner(available: false));
        var signalled = 0;
        var outcomes = builder.VerifyAll(new[] { "A.sln", "B.sln" }, onSkippedToolMissing: () => signalled++);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Skipped));
        Assert.Equal(1, signalled);
        Assert.Equal(VerifyOutcome.NotRun, Verification.Evaluate(outcomes));
    }

    [Fact]
    public void VerifyAll_mixed_results_evaluate_to_failed()
    {
        var runner = new FakeRunner(available: true, new()
        {
            ["Good.sln"] = (0, "Build succeeded", ""),
            ["Bad.sln"] = (1, "X.cs: error CS1: boom", ""),
        });
        var outcomes = new SolutionBuilder(runner).VerifyAll(new[] { "Good.sln", "Bad.sln" });

        Assert.True(outcomes[0].Succeeded);
        Assert.False(outcomes[1].Succeeded);
        Assert.Equal(VerifyOutcome.Failed, Verification.Evaluate(outcomes));
        Assert.Equal(1, Verification.FailedCount(outcomes));
    }

    [Fact]
    public void VerifyAll_all_pass_evaluates_to_passed()
    {
        var runner = new FakeRunner(available: true, new()
        {
            ["A.sln"] = (0, "ok", ""),
            ["B.sln"] = (0, "ok", ""),
        });
        var outcomes = new SolutionBuilder(runner).VerifyAll(new[] { "A.sln", "B.sln" });
        Assert.Equal(VerifyOutcome.Passed, Verification.Evaluate(outcomes));
    }

    // The failed-verify -> rollback DECISION path (pure policy, no prompt).
    [Theory]
    [InlineData(VerifyOutcome.Passed, true, true, RollbackDecision.None)]
    [InlineData(VerifyOutcome.NotRun, true, true, RollbackDecision.None)]
    [InlineData(VerifyOutcome.Failed, true, true, RollbackDecision.RollBack)]
    [InlineData(VerifyOutcome.Failed, true, false, RollbackDecision.KeepInteractive)]
    [InlineData(VerifyOutcome.Failed, false, null, RollbackDecision.KeepNonInteractive)]
    public void Decide_maps_outcome_and_answer_to_action(
        VerifyOutcome outcome, bool interactive, bool? userSaidYes, RollbackDecision expected)
    {
        Assert.Equal(expected, Verification.Decide(outcome, interactive, userSaidYes));
    }

    [Fact]
    public void NonInteractive_failed_verify_never_rolls_back_even_if_yes()
    {
        // Non-interactive runs must never auto-roll-back silently.
        Assert.Equal(RollbackDecision.KeepNonInteractive,
            Verification.Decide(VerifyOutcome.Failed, interactive: false, userSaidYes: true));
    }

    // A real dotnet build over a trivial buildable vs. an unbuildable project. Skipped
    // (asserted no-op) when the dotnet CLI is not on PATH so the suite is portable.
    [Fact]
    public void Real_build_passes_for_a_buildable_project_and_fails_for_a_broken_one()
    {
        var runner = new DotnetBuildRunner();
        if (!runner.IsAvailable) return; // no dotnet → nothing to assert (tolerated)

        var builder = new SolutionBuilder(runner);

        using var good = new TempDir();
        var goodProj = good.File("Good.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        good.File("Program.cs", "class P { static void M() { } }");
        var goodOutcome = builder.VerifyAll(new[] { goodProj });
        Assert.True(goodOutcome[0].Succeeded, goodOutcome[0].ErrorTail);

        using var bad = new TempDir();
        var badProj = bad.File("Bad.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        bad.File("Program.cs", "class P { this is not valid C# ;;; }");
        var badOutcome = builder.VerifyAll(new[] { badProj });
        Assert.False(badOutcome[0].Succeeded);
        Assert.NotEqual("", badOutcome[0].ErrorTail);
        Assert.Equal(VerifyOutcome.Failed, Verification.Evaluate(badOutcome));
    }
}
