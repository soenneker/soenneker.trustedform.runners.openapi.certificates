using Soenneker.Tests.HostedUnit;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class TrustedFormOpenApiRunnerTests : HostedUnitTest
{

    public TrustedFormOpenApiRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
