using SmartX.Api.Services;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Exposes the single route behind the Telemetry Pulse widget.
    /// Hands the request to EngagementService and returns the summary it builds. 
    /// </summary>
    public static class EngagementEndpoints
    {
        public static void MapEngagementEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/engagement/summary", (EngagementService engagement) => Results.Ok(engagement.BuildSummary()))
                .WithTags("Engagement");
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
