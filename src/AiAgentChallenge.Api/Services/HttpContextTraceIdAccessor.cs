using System.Diagnostics;
using AiAgentChallenge.Api.Abstractions;
using Microsoft.AspNetCore.Http;

namespace AiAgentChallenge.Api.Services;

public sealed class HttpContextTraceIdAccessor : ITraceIdAccessor
{
    public string GetOrCreate(HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(httpContext.TraceIdentifier))
        {
            return httpContext.TraceIdentifier;
        }

        if (!string.IsNullOrWhiteSpace(Activity.Current?.Id))
        {
            return Activity.Current.Id;
        }

        return Guid.NewGuid().ToString("N");
    }
}
