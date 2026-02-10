using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Extensions.String;
using Soenneker.Extensions.ValueTask;
using Soenneker.Git.Util.Abstract;
using Soenneker.Playwright.Installation.Abstract;
using Soenneker.Playwrights.Extensions.Stealth;
using Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Json;
using Soenneker.Utils.Path.Abstract;
using Soenneker.Utils.Process.Abstract;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils;

///<inheritdoc cref="IFileOperationsUtil"/>
public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IProcessUtil _processUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IPlaywrightInstallationUtil _playwrightInstallationUtil;
    private readonly IPathUtil _pathUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IGitUtil gitUtil, IDotnetUtil dotnetUtil, IProcessUtil processUtil, IFileUtil fileUtil,
        IPlaywrightInstallationUtil playwrightInstallationUtil, IPathUtil pathUtil)
    {
        _logger = logger;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _processUtil = processUtil;
        _fileUtil = fileUtil;
        _playwrightInstallationUtil = playwrightInstallationUtil;
        _pathUtil = pathUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        await _playwrightInstallationUtil.EnsureInstalled(cancellationToken);

        using IPlaywright playwright = await Microsoft.Playwright.Playwright.CreateAsync();

        await using IBrowser browser = await playwright.LaunchStealthChromium();

        IBrowserContext context = await browser.CreateStealthContext();

        IPage page = await context.NewPageAsync();

        await page.GotoAsync("https://activeprospect.redoc.ly/docs/trustedform/api/v4.0/overview/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60000
        });

        IDownload download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            // Adjust selector if needed
            await page.ClickAsync("a[download='swagger.json']");
        });

        string tempFilePath1 = await _pathUtil.GetRandomTempFilePath("json", cancellationToken);

        _ = await download.PathAsync();

        await download.SaveAsAsync(tempFilePath1);

        string content = await _fileUtil.Read(tempFilePath1, cancellationToken: cancellationToken).NoSync();

        string formatted = JsonUtil.Format(content, false);

        string tempFilePath2 = await _pathUtil.GetRandomTempFilePath("json", cancellationToken);

        await _fileUtil.Write(tempFilePath2, formatted, cancellationToken: cancellationToken);

        string gitDirectory = await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}",
            cancellationToken: cancellationToken);

        string targetFilePath = Path.Combine(gitDirectory, "swagger.json");

        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);

        await _fileUtil.Move(tempFilePath2, targetFilePath, cancellationToken: cancellationToken);

        await _processUtil.Start("dotnet", null, "tool update --global Microsoft.OpenApi.Kiota", waitForExit: true, cancellationToken: cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src");

        await DeleteAllExceptCsproj(srcDirectory, cancellationToken);

        await _processUtil.Start("kiota", gitDirectory,
                              $"kiota generate -l CSharp -d \"{targetFilePath}\" -o src -c TrustedFormCertificatesOpenApiClient -n {Constants.Library} --ebc --cc",
                              waitForExit: true,
                              cancellationToken: cancellationToken)
                          .NoSync();

        await BuildAndPush(gitDirectory, cancellationToken).NoSync();
    }

    public async ValueTask DeleteAllExceptCsproj(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory does not exist: {DirectoryPath}", directoryPath);
            return;
        }

        try
        {
            // Delete all files except .csproj
            foreach (string file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _fileUtil.Delete(file, ignoreMissing: true, log: false, cancellationToken);
                        _logger.LogInformation("Deleted file: {FilePath}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete file: {FilePath}", file);
                    }
                }
            }

            // Delete all empty subdirectories
            foreach (string dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories)
                                            .OrderByDescending(d => d.Length)) // Sort by depth to delete from deepest first
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, recursive: false);
                        _logger.LogInformation("Deleted empty directory: {DirectoryPath}", dir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete directory: {DirectoryPath}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while cleaning the directory: {DirectoryPath}", directoryPath);
        }
    }

    private async ValueTask BuildAndPush(string gitDirectory, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
        {
            _logger.LogError("Build was not successful, exiting...");
            return;
        }

        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");

        await _gitUtil.CommitAndPush(gitDirectory, "Automated Update", gitHubToken, "Jake Soenneker", "jake@soenneker.com", cancellationToken);
    }
}