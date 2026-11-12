using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Api.Topology
{
    /// <summary>
    /// Owns the mesh graph and the set of nodes an operator has marked failed, and turns them into the
    /// snapshots the Network Topology page draws.
    ///
    /// Nothing is cached. The mesh has sixteen nodes, so BFS and Prim run again on every request: that
    /// is a few microseconds, and it means the page can never show a stale route.
    /// A node counts as down if it was marked failed here OR the health monitor has flagged it
    /// disconnected (AlertTracker); either way the algorithms skip it and the links touching it.
    /// </summary>
    public sealed class MeshTopologyService(AlertTracker alerts, LiveDeviceRegistry registry)
    {
        /// <summary>
        /// The result of running the algorithms once against one moment's set of down nodes.
        /// </summary>
        private sealed record MeshState(HashSet<string> Down, BfsResult Bfs, List<string> Dfs, PrimResult Mst);

        private readonly MeshGraph _graph = MeshSeed.Build();
        private readonly HashSet<string> _failed = new(DeviceIdentifier.Comparer);
        private readonly Lock _gate = new();


        /// <summary>
        /// The whole page in one object: nodes, links and the figures behind the header counters.
        /// </summary>
        public TopologySnapshotDto Snapshot()
        {
            var state = Compute();

            var nodes = _graph.Nodes.Select(node => ToDto(node, state)).ToList();
            var treeLinks = state.Mst.Edges.Select(edge => LinkKey(edge.From, edge.To)).ToHashSet();

            var links = new List<MeshLinkDto>();
            int active = 0;
            double fullMeshCost = 0;

            foreach (var link in _graph.Links)
            {
                MeshLinkState linkState;

                if (state.Down.Contains(link.From) || state.Down.Contains(link.To))
                {
                    linkState = MeshLinkState.Down;
                }
                else
                {
                    active++;
                    fullMeshCost += link.CostMw;
                    linkState = treeLinks.Contains(LinkKey(link.From, link.To)) ? MeshLinkState.Mst : MeshLinkState.Standby;
                }

                links.Add(new MeshLinkDto(link.From, link.To, link.CostMw, linkState));
            }

            // The gateway is the root, not one of the fifteen nodes the header counts.
            int nodesTotal = _graph.Nodes.Count - 1;

            return new TopologySnapshotDto(
                nodes,
                links,
                nodesTotal - state.Down.Count,
                nodesTotal,
                active,
                _graph.Links.Count,
                state.Mst.TotalCost,
                fullMeshCost,
                state.Bfs.Order,
                state.Dfs,
                state.Mst.Unreachable);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Runs BFS, DFS and Prim from the gateway, with the down nodes worked out once so all three
        /// see the same picture. 'treatAsUp' names one node to count as up even if the health monitor
        /// still has it flagged disconnected: that flag only clears when the node's next packet arrives,
        /// which is a moment after an operator restores it.
        /// </summary>
        private MeshState Compute(string? treatAsUp = null)
        {
            var down = new HashSet<string>(DeviceIdentifier.Comparer);

            lock (_gate)
                down.UnionWith(_failed);

            foreach (var node in _graph.Nodes)
            {
                if (alerts.IsDisconnected(node.Id))
                    down.Add(node.Id);
            }

            if (treatAsUp is not null)
                down.Remove(treatAsUp);

            // The gateway is the root of every search and is never treated as down.
            down.Remove(MeshSeed.GatewayId);

            var bfs = MeshAlgorithms.Bfs(_graph, MeshSeed.GatewayId, down.Contains);
            var dfs = MeshAlgorithms.Dfs(_graph, MeshSeed.GatewayId, down.Contains);
            var mst = MeshAlgorithms.Prim(_graph, MeshSeed.GatewayId, down.Contains);

            return new MeshState(down, bfs, dfs, mst);
        }
        //----------------------------------------------------------//


        private MeshNodeDto ToDto(MeshNode node, MeshState state)
        {
            bool isDown = state.Down.Contains(node.Id);
            bool reached = node.Id == MeshSeed.GatewayId || state.Mst.Parent.ContainsKey(node.Id);

            // A node that is up but reporting a threshold breach shows amber.
            var status = isDown ? MeshNodeStatus.Down : HasFault(node.Id) ? MeshNodeStatus.Warning : MeshNodeStatus.Up;

            int? hops = null;
            double? cost = null;

            if (reached && !isDown)
            {
                var path = MeshAlgorithms.RouteTo(state.Mst.Parent, node.Id);
                hops = path.Count - 1;
                cost = PathCost(path);
            }

            return new MeshNodeDto(node.Id, node.Name, node.Mac, node.X, node.Y, status, hops, cost);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// One node's route to the gateway, by its id or MAC address. Null if the node is unknown.
        /// A node that is down or cut off comes back as not reachable, with empty paths.
        /// </summary>
        public RouteDto? RouteFor(string idOrMac)
        {
            if (!_graph.TryGetNode(idOrMac, out var node))
                return null;

            var state = Compute();
            var tree = TreeRoute(state, node.Id);

            if (tree is null)
                return new RouteDto(node.Id, node.Name, node.Mac, false, [], 0, 0, [], 0, [], 0);

            // Same node, two other answers: the breadth-first route (fewest hops, not least power) and
            // Dijkstra's route (least power for this node, whatever the hops).
            var fewest = MeshAlgorithms.RouteTo(state.Bfs.Parent, node.Id);
            var cheapest = MeshAlgorithms.RouteTo(MeshAlgorithms.Dijkstra(_graph, MeshSeed.GatewayId, state.Down.Contains).Parent, node.Id);

            return new RouteDto(
                node.Id, node.Name, node.Mac, true,
                ToHops(tree), tree.Count - 1, PathCost(tree),
                ToHops(fewest), PathCost(fewest),
                ToHops(cheapest), PathCost(cheapest));
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Marks a node failed (or brings it back), then compares every other node's route before and
        /// after to find who had to hop a different way. Null if the node is unknown.
        /// </summary>
        public FailureResultDto? SetDown(string idOrMac, bool down)
        {
            if (!_graph.TryGetNode(idOrMac, out var node))
                return null;

            MeshState before;
            MeshState after;

            lock (_gate)
            {
                before = Compute();

                if (down)
                    _failed.Add(node.Id);
                else
                    _failed.Remove(node.Id);

                // A restored node counts as up straight away, so the result shows the links and routes it brings back.
                after = Compute(down ? null : node.Id);
            }

            var rerouted = new List<RerouteDto>();

            foreach (var other in _graph.Nodes)
            {
                if (other.Id == MeshSeed.GatewayId || string.Equals(other.Id, node.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                var oldRoute = TreeRoute(before, other.Id);
                var newRoute = TreeRoute(after, other.Id);

                if (oldRoute is null && newRoute is null)
                    continue;

                if (oldRoute is null || newRoute is null || !oldRoute.SequenceEqual(newRoute, DeviceIdentifier.Comparer))
                    rerouted.Add(new RerouteDto(other.Name, oldRoute?.Count - 1, newRoute?.Count - 1));
            }

            int linksBefore = ActiveLinks(before);
            int linksAfter = ActiveLinks(after);
            int isolated = after.Mst.Unreachable.Count;

            return new FailureResultDto(
                node.Id,
                node.Name,
                down,
                DateTimeOffset.UtcNow,
                Math.Max(0, linksBefore - linksAfter),
                Math.Max(0, linksAfter - linksBefore),
                isolated,
                isolated == 0,
                rerouted);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The ids from a node to the gateway through the spanning tree, or null if the node is down or
        /// the tree never reached it.
        /// </summary>
        private static List<string>? TreeRoute(MeshState state, string id)
        {
            bool reached = id == MeshSeed.GatewayId || state.Mst.Parent.ContainsKey(id);

            return reached && !state.Down.Contains(id)
                ? MeshAlgorithms.RouteTo(state.Mst.Parent, id)
                : null;
        }


        private int ActiveLinks(MeshState state) =>
            _graph.Links.Count(link => !state.Down.Contains(link.From) && !state.Down.Contains(link.To));


        /// <summary>
        /// Turns a list of node ids into route stops, each carrying the cost of the link into it.
        /// </summary>
        private List<RouteHopDto> ToHops(List<string> path)
        {
            var hops = new List<RouteHopDto>();

            for (int i = 0; i < path.Count; i++)
            {
                _graph.TryGetNode(path[i], out var stop);
                hops.Add(new RouteHopDto(stop.Id, stop.Name, stop.Mac, i == 0 ? 0 : LinkCost(path[i - 1], path[i])));
            }

            return hops;
        }


        private bool HasFault(string id) => registry.TryGetSnapshot(id, out var device) && device.Fault is not null;


        /// <summary>
        /// Total cost of walking a path, summing the link between each pair of neighbours.
        /// </summary>
        private double PathCost(IReadOnlyList<string> path)
        {
            double total = 0;

            for (int i = 0; i < path.Count - 1; i++)
                total += LinkCost(path[i], path[i + 1]);

            return total;
        }


        private double LinkCost(string a, string b)
        {
            foreach (var link in _graph.LinksOf(a))
            {
                if (string.Equals(link.To, b, StringComparison.OrdinalIgnoreCase))
                    return link.CostMw;
            }

            return 0;
        }


        // The same pair of nodes gives the same key whichever way round it is written.
        private static (string, string) LinkKey(string a, string b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
                ? (a.ToUpperInvariant(), b.ToUpperInvariant())
                : (b.ToUpperInvariant(), a.ToUpperInvariant());
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
