using XerifeBot.Api.Models;

namespace XerifeBot.Api.Services;

public interface IBotFlowService
{
    Task<string?> ProcessMessageAsync(CustomerSession session, string incomingText);
}
