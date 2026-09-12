using SmartX.Shared.Validation;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// The client builds the deployment tree from the sensors it has already loaded and 
    /// posts it here, so the recursive validator runs against the real deployment rather
    /// than a hardcoded sample. A list, because each facility is its own root.
    /// </summary>
    public static class DeploymentEndpoints
    {
        public static void MapDeploymentEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapPost("/api/deployment/validate", (List<DeploymentNode> roots) =>
                    Results.Ok(DeploymentValidator.ValidateAll(roots)))
                .WithTags("Deployment");
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//