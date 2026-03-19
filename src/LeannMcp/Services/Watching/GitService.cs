using System.Diagnostics;
using CSharpFunctionalExtensions;

namespace LeannMcp.Services.Watching;

/// <summary>
/// Thin, static wrapper around git CLI operations.
/// All methods return <see cref="Result{T}"/> for railway-oriented error handling.
/// </summary>
public static class GitService
{
    public static async Task<Result<string>> FetchAsync(string repoPath, string branch)
    {
        var result = await RunGitCommandAsync(repoPath, $"fetch origin {branch}");
        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Failure<string>($"git fetch failed: {result.Error}");
    }

    public static Result<string> GetHeadHash(string repoPath)
    {
        var result = RunGitCommand(repoPath, "rev-parse HEAD");
        return result.Map(s => s.Trim());
    }

    public static Result<string> GetRemoteHash(string repoPath, string branch)
    {
        var result = RunGitCommand(repoPath, $"rev-parse origin/{branch}");
        return result.Map(s => s.Trim());
    }

    public static async Task<Result> PullAsync(string repoPath, string branch)
    {
        var result = await RunGitCommandAsync(repoPath, $"pull origin {branch}");
        return result.IsSuccess ? Result.Success() : Result.Failure($"git pull failed: {result.Error}");
    }

    public static Result<bool> HasGitDirectory(string repoPath) =>
        Directory.Exists(Path.Combine(repoPath, ".git"))
            ? Result.Success(true)
            : Result.Success(false);

    private static Result<string> RunGitCommand(string workingDir, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0
                ? Result.Success(stdout)
                : Result.Failure<string>(stderr.Trim());
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to run git {arguments}: {ex.Message}");
        }
    }

    private static async Task<Result<string>> RunGitCommandAsync(string workingDir, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return process.ExitCode == 0
                ? Result.Success(stdout)
                : Result.Failure<string>(stderr.Trim());
        }
        catch (Exception ex)
        {
            return Result.Failure<string>($"Failed to run git {arguments}: {ex.Message}");
        }
    }
}