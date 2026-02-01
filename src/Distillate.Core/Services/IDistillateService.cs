using Distillate.Core.Models;

namespace Distillate.Core.Services;

public interface IDistillateService
{
    Task<ProcessResponse> ProcessAsync(ProcessRequest request, string traceId, CancellationToken cancellationToken);
}
