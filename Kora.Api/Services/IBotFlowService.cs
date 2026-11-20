using Kora.Api.Models;

namespace Kora.Api.Services;

public interface IBotFlowService
{
    Task<string?> ProcessMessageAsync(CustomerSession session, string incomingText);
}
