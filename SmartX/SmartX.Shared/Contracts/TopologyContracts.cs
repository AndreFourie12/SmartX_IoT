namespace SmartX.Shared.Contracts
{
    /// <summary>
    /// How a mesh node is doing: Up, Down (marked failed or silent), or Warning (up, but reporting a
    /// threshold breach).
    /// </summary>
    public enum MeshNodeStatus
    {
        Up,
        Down,
        Warning
    }
    //----------------------------------------------------------//


    /// <summary>
    /// What a link is doing: part of the minimum spanning tree, a spare that is not in it, or dead
    /// because a node at one end is down.
    /// </summary>
    public enum MeshLinkState
    {
        Mst,
        Standby,
        Down
    }
    //----------------------------------------------------------//


    /// <summary>
    /// One node on the topology page. Hops and CostToGatewayMw describe its route through the minimum
    /// spanning tree, and are null for a node that is down or cut off.
    /// API -> Client
    /// </summary>
    public sealed record MeshNodeDto(
        string Id,
        string Name,
        string Mac,
        double X,
        double Y,
        MeshNodeStatus Status,
        int? Hops,
        double? CostToGatewayMw);
    //----------------------------------------------------------//


    /// <summary>
    /// One wireless link and its cost in milliwatts per packet.
    /// </summary>
    public sealed record MeshLinkDto(string From, string To, double CostMw, MeshLinkState State);
    //----------------------------------------------------------//


    /// <summary>
    /// Everything the topology page draws, in one round trip. FullMeshCostMw is what every live link
    /// would cost to use; MstCostMw is what the spanning tree actually uses.
    /// API -> Client
    /// </summary>
    public sealed record TopologySnapshotDto(
        IReadOnlyList<MeshNodeDto> Nodes,
        IReadOnlyList<MeshLinkDto> Links,
        int NodesOnline,
        int NodesTotal,
        int LinksActive,
        int LinksTotal,
        double MstCostMw,
        double FullMeshCostMw,
        IReadOnlyList<string> BfsOrder,
        IReadOnlyList<string> DfsOrder,
        IReadOnlyList<string> UnreachableIds);
    //----------------------------------------------------------//


    /// <summary>
    /// One stop on a route. CostMw is the cost of the link from the previous stop, and 0 for the first.
    /// </summary>
    public sealed record RouteHopDto(string Id, string Name, string Mac, double CostMw);
    //----------------------------------------------------------//


    /// <summary>
    /// A node's route to the gateway, three ways. Path follows the minimum spanning tree; FewestHopPath
    /// is the breadth-first route, which can be shorter in hops but costs more power; LeastPowerPath is
    /// Dijkstra's cheapest route for this node alone. All are empty when the node is down or cut off.
    /// API -> Client
    /// </summary>
    public sealed record RouteDto(
        string NodeId,
        string Name,
        string Mac,
        bool Reachable,
        IReadOnlyList<RouteHopDto> Path,
        int TotalHops,
        double TotalCostMw,
        IReadOnlyList<RouteHopDto> FewestHopPath,
        double FewestHopCostMw,
        IReadOnlyList<RouteHopDto> LeastPowerPath,
        double LeastPowerCostMw);
    //----------------------------------------------------------//


    /// <summary>
    /// A node whose route to the gateway changed. A null count means the node had no route at that time.
    /// </summary>
    public sealed record RerouteDto(string Name, int? HopsBefore, int? HopsAfter);
    //----------------------------------------------------------//


    /// <summary>
    /// What happened when a node was taken down or brought back, and who had to find a new way round.
    /// API -> Client
    /// </summary>
    public sealed record FailureResultDto(
        string NodeId,
        string Name,
        bool WentDown,
        DateTimeOffset AtUtc,
        int LinksLost,
        int LinksRestored,
        int NodesIsolated,
        bool Healed,
        IReadOnlyList<RerouteDto> Rerouted);
    //----------------------------------------------------------//


    /// <summary>
    /// A sensor's readings between two instants, plus how many tree nodes the range query visited and
    /// the tree's height, which shows the query did not scan everything stored.
    /// API -> Client
    /// </summary>
    public sealed record TopologyHistoryDto(TimelineDto Timeline, int NodesVisited, int TreeHeight);
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
