using SmartX.Api.Services;
using SmartX.Api.Topology;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Endpoints
{
    /// <summary>
    /// Defines every HTTP route for the Network Topology page: the mesh snapshot, one node's route to
    /// the gateway, and taking a node down or bringing it back (which also cuts or restores that node in
    /// the simulated fleet, so Command Stream agrees). The service recomputes everything on each call,
    /// so these are thin wrappers.
    /// </summary>
    public static class TopologyEndpoints
    {
        public static void MapTopologyEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/topology").WithTags("Topology");

            group.MapGet("/", (MeshTopologyService topology) => Results.Ok(topology.Snapshot()))
                .Produces<TopologySnapshotDto>();


            group.MapGet("/route/{idOrMac}", (string idOrMac, MeshTopologyService topology) =>
                topology.RouteFor(idOrMac) is { } route
                    ? Results.Ok(route)
                    : Results.NotFound($"No mesh node with id or MAC '{idOrMac}'."))
                .Produces<RouteDto>()
                .Produces(StatusCodes.Status404NotFound);


            group.MapPost("/nodes/{id}/down", (string id, MeshTopologyService topology, SimulationControl simulation) => Failure(topology, simulation, id, down: true))
                .Produces<FailureResultDto>()
                .Produces(StatusCodes.Status404NotFound);


            group.MapPost("/nodes/{id}/up", (string id, MeshTopologyService topology, SimulationControl simulation) => Failure(topology, simulation, id, down: false))
                .Produces<FailureResultDto>()
                .Produces(StatusCodes.Status404NotFound);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Marks the node down or up, and keeps the Command Stream page in step with this one: a node
        /// marked failed goes silent in the simulated fleet (the health monitor then flags it offline),
        /// and a restored node starts reporting again on the next tick.
        /// </summary>
        private static IResult Failure(MeshTopologyService topology, SimulationControl simulation, string id, bool down)
        {
            if (topology.SetDown(id, down) is not { } result)
                return Results.NotFound($"No mesh node with id or MAC '{id}'.");

            if (TelemetrySimulationService.FleetIds.Contains(result.NodeId, DeviceIdentifier.Comparer))
            {
                if (result.WentDown)
                    simulation.ForceOutage(result.NodeId, TimeSpan.FromHours(1));
                else if (!down)
                    simulation.ForceOutage(result.NodeId, TimeSpan.Zero);
            }

            return Results.Ok(result);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
