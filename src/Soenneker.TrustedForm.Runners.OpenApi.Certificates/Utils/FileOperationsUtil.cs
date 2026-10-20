using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.ValueTask;
using Soenneker.Kiota.Util.Abstract;
using Soenneker.Git.Util.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.Yaml.Abstract;
using Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Json;
using System.Collections.Generic;
using Soenneker.Utils.Path.Abstract;
using Soenneker.Utils.Process.Abstract;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IKiotaUtil _kiotaUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IFileDownloadUtil _fileDownloadUtil;
    private readonly IYamlUtil _yamlUtil;
    private readonly IPathUtil _pathUtil;
    private readonly IDirectoryUtil _directoryUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IGitUtil gitUtil, IDotnetUtil dotnetUtil, IFileUtil fileUtil,
        IFileDownloadUtil fileDownloadUtil, IYamlUtil yamlUtil, IPathUtil pathUtil, IDirectoryUtil directoryUtil, IKiotaUtil kiotaUtil)
    {
        _logger = logger;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _kiotaUtil = kiotaUtil;
        _fileUtil = fileUtil;
        _fileDownloadUtil = fileDownloadUtil;
        _yamlUtil = yamlUtil;
        _pathUtil = pathUtil;
        _directoryUtil = directoryUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string? yamlPath = await _fileDownloadUtil.Download(
            "https://developers.activeprospect.com/api-reference/claims_api-v4.yaml",
            fileExtension: "yaml", cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(yamlPath))
            throw new InvalidOperationException("The TrustedForm v4 OpenAPI specification could not be downloaded.");

        string tempFilePath1 = await _pathUtil.GetRandomTempFilePath("json", cancellationToken);
        await _yamlUtil.SaveAsJson(yamlPath, tempFilePath1, true, cancellationToken);
        string content = await _fileUtil.Read(tempFilePath1, cancellationToken: cancellationToken).NoSync();

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(content);
        string formatted = JsonUtil.Serialize(document, FormattingJsonContext.Default.JsonDocument);

        string tempFilePath2 = await _pathUtil.GetRandomTempFilePath("json", cancellationToken);

        await _fileUtil.Write(tempFilePath2, formatted, cancellationToken: cancellationToken);

        string gitDirectory = await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}",
            cancellationToken: cancellationToken);

        string targetFilePath = Path.Combine(gitDirectory, "swagger.json");

        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);

        await _fileUtil.Move(tempFilePath2, targetFilePath, cancellationToken: cancellationToken);

        await _kiotaUtil.EnsureInstalled(cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src", Constants.Library);

        await DeleteAllExceptCsproj(srcDirectory, cancellationToken);

        await _kiotaUtil.Generate(targetFilePath, "TrustedFormCertificatesOpenApiClient", Constants.Library, gitDirectory, cancellationToken).NoSync();

        await BuildAndPush(gitDirectory, cancellationToken).NoSync();
    }

    /// <summary>
    /// Deletes all except csproj.
    /// </summary>
    /// <param name="directoryPath">The directory path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DeleteAllExceptCsproj(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!(await _directoryUtil.Exists(directoryPath, cancellationToken)))
            throw new DirectoryNotFoundException($"The generated-client source directory does not exist: {directoryPath}");

        List<string> files = await _directoryUtil.GetFilesByExtension(directoryPath, "", true, cancellationToken);
        foreach (string file in files)
        {
            if (!file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await _fileUtil.Delete(file, ignoreMissing: true, log: false, cancellationToken);
                _logger.LogInformation("Deleted file: {FilePath}", file);
            }
        }

        List<string> dirs = await _directoryUtil.GetAllDirectoriesRecursively(directoryPath, cancellationToken);
        // Process children before parents without allocating LINQ sorting buffers.
        dirs.Sort(static (left, right) => right.Length.CompareTo(left.Length));
        foreach (string dir in dirs)
        {
            List<string> dirFiles = await _directoryUtil.GetFilesByExtension(dir, "", false, cancellationToken);
            List<string> subDirs = await _directoryUtil.GetAllDirectories(dir, cancellationToken);
            if (dirFiles.Count == 0 && subDirs.Count == 0)
            {
                await _directoryUtil.Delete(dir, cancellationToken);
                _logger.LogInformation("Deleted empty directory: {DirectoryPath}", dir);
            }
        }
    }

    private async ValueTask BuildAndPush(string gitDirectory, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", Constants.Library, $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
            throw new InvalidOperationException("The generated TrustedForm Certificate API client did not build successfully.");

        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        await _gitUtil.CommitAndPush(gitDirectory, $"Update {Constants.Library}", gitHubToken, name, email, cancellationToken);
    }
}
