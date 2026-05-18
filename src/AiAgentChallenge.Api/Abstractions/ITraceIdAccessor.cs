using Microsoft.AspNetCore.Http;

namespace AiAgentChallenge.Api.Abstractions;

public interface ITraceIdAccessor
{
    string GetOrCreate(HttpContext httpContext);
}
