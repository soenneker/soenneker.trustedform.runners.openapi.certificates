using Microsoft.Extensions.DependencyInjection;
using Soenneker.Managers.Runners.Registrars;
using Soenneker.Playwright.Installation.Registrars;
using Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils;
using Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils.Abstract;
using Soenneker.Utils.File.Download.Registrars;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.SetupIoC();
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddScoped<IFileOperationsUtil, FileOperationsUtil>()
                .AddRunnersManagerAsScoped()
                .AddFileDownloadUtilAsScoped()
                .AddPlaywrightInstallationUtilAsSingleton();

        return services;
    }
}