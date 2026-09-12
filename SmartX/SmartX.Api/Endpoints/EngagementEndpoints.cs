using SmartX.Api.Services;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// 
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
