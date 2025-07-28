using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.TrustedForm.Runners.OpenApi.Certificates.Utils.Abstract;

public interface IFileOperationsUtil
{
    ValueTask Process(CancellationToken cancellationToken = default);
}
