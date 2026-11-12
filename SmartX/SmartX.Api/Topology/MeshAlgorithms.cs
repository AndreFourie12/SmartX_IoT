using SmartX.Shared.Models;

namespace SmartX.Api.Topology
{
    /// <summary>
    /// What a breadth-first search found: the order nodes were reached in, how many hops each one is
    /// from the root, and which node each was reached from.
    /// </summary>
    public sealed record BfsResult(List<string> Order, Dictionary<string, int> Hops, Dictionary<string, string> Parent);
    //----------------------------------------------------------//


    /// <summary>
    /// A minimum spanning tree: the links chosen, their total cost, the node each one reaches the root
    /// through, and the live nodes it could not reach because a failure cut them off.
    /// </summary>
    public sealed record PrimResult(List<MeshLink> Edges, double TotalCost, Dictionary<string, string> Parent, List<string> Unreachable);
    //----------------------------------------------------------//


    /// <summary>
    /// The cheapest total power from the root to each node it can reach, and the node each one is
    /// reached through on that cheapest route.
    /// </summary>
    public sealed record DijkstraResult(Dictionary<string, double> Cost, Dictionary<string, string> Parent);
    //----------------------------------------------------------//


    /// <summary>
    /// The graph algorithms behind the Network Topology page. Each takes an 'isDown' test: a down node
    /// is never visited, so the links touching it are skipped too.
    /// </summary>
    public static class MeshAlgorithms
    {
        /// <summary>
        /// Breadth-first search from the root.
        ///
        /// A queue means every node at distance 1 is taken before any at distance 2, so the first time a
        /// node is reached is by a path with the fewest hops. That is why Hops is the shortest hop count
        /// and Parent holds the fewest-hop route back to the root.
        /// </summary>
        public static BfsResult Bfs(MeshGraph graph, string root, Func<string, bool> isDown)
        {
            var order = new List<string>();
            var hops = new Dictionary<string, int>(DeviceIdentifier.Comparer);
            var parent = new Dictionary<string, string>(DeviceIdentifier.Comparer);

            if (isDown(root))
                return new BfsResult(order, hops, parent);

            var queue = new Queue<string>();
            hops[root] = 0;
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                order.Add(current);

                foreach (var link in graph.LinksOf(current))
                {
                    if (isDown(link.To) || hops.ContainsKey(link.To))
                        continue;

                    hops[link.To] = hops[current] + 1;
                    parent[link.To] = current;
                    queue.Enqueue(link.To);
                }
            }

            return new BfsResult(order, hops, parent);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Depth-first search from the root.
        ///
        /// A stack makes it follow one branch as far as it goes before backing up, so the visit order
        /// differs from BFS. The page only uses it for the BFS/DFS toggle: both reach the same nodes.
        /// Neighbours are pushed in reverse so the first-listed neighbour is explored first.
        /// </summary>
        public static List<string> Dfs(MeshGraph graph, string root, Func<string, bool> isDown)
        {
            var order = new List<string>();

            if (isDown(root))
                return order;

            var seen = new HashSet<string>(DeviceIdentifier.Comparer);
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string current = stack.Pop();

                if (!seen.Add(current))
                    continue;

                order.Add(current);

                var links = graph.LinksOf(current);
                for (int i = links.Count - 1; i >= 0; i--)
                {
                    if (!isDown(links[i].To) && !seen.Contains(links[i].To))
                        stack.Push(links[i].To);
                }
            }

            return order;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Prim's minimum spanning tree, grown outward from the root.
        ///
        /// The priority queue always hands back the cheapest link leaving the tree built so far, so each
        /// step adds the cheapest way to bring one more node in. The result spans every node with the
        /// lowest total power cost. If a failure splits the mesh, only the root's piece can be reached:
        /// the rest are reported as unreachable rather than throwing.
        /// </summary>
        public static PrimResult Prim(MeshGraph graph, string root, Func<string, bool> isDown)
        {
            var edges = new List<MeshLink>();
            var parent = new Dictionary<string, string>(DeviceIdentifier.Comparer);
            var inTree = new HashSet<string>(DeviceIdentifier.Comparer);
            double total = 0;

            if (!isDown(root))
            {
                var queue = new PriorityQueue<(string From, string To), double>();
                inTree.Add(root);
                QueueLinks(graph, root, inTree, isDown, queue);

                while (queue.TryDequeue(out var step, out double cost))
                {
                    // A cheaper link may already have brought this node in.
                    if (!inTree.Add(step.To))
                        continue;

                    edges.Add(new MeshLink(step.From, step.To, cost));
                    parent[step.To] = step.From;
                    total += cost;

                    QueueLinks(graph, step.To, inTree, isDown, queue);
                }
            }

            var unreachable = new List<string>();
            foreach (var node in graph.Nodes)
            {
                if (!isDown(node.Id) && !inTree.Contains(node.Id))
                    unreachable.Add(node.Id);
            }

            return new PrimResult(edges, total, parent, unreachable);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Follows the parent map from a node up to the root and returns the ids on the way, starting
        /// with the node itself. A node with no parent (the root, or one that could not be reached)
        /// comes back as a path of just itself.
        /// </summary>
        public static List<string> RouteTo(Dictionary<string, string> parent, string node)
        {
            var path = new List<string> { node };

            while (parent.TryGetValue(node, out var next))
            {
                path.Add(next);
                node = next;
            }

            return path;
        }
        //----------------------------------------------------------//


        private static void QueueLinks(MeshGraph graph, string from, HashSet<string> inTree, Func<string, bool> isDown, PriorityQueue<(string From, string To), double> queue)
        {
            foreach (var link in graph.LinksOf(from))
            {
                if (!isDown(link.To) && !inTree.Contains(link.To))
                    queue.Enqueue((from, link.To), link.CostMw);
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Dijkstra's least-power route from the gateway to every node.
        ///
        /// Prim minimises the cost of the whole network, so one node's route through that tree can cost
        /// more than its own cheapest way home. Dijkstra answers the per-node question: the queue always
        /// hands back the node with the lowest total cost so far, and once a node comes out its cost is
        /// final (link costs are never negative). Same priority queue and parent map as Prim.
        /// </summary>
        public static DijkstraResult Dijkstra(MeshGraph graph, string root, Func<string, bool> isDown)
        {
            var cost = new Dictionary<string, double>(DeviceIdentifier.Comparer);
            var parent = new Dictionary<string, string>(DeviceIdentifier.Comparer);

            if (isDown(root))
                return new DijkstraResult(cost, parent);

            var queue = new PriorityQueue<string, double>();
            cost[root] = 0;
            queue.Enqueue(root, 0);

            while (queue.TryDequeue(out var current, out double spent))
            {
                // A cheaper way here was found after this entry was queued.
                if (spent > cost[current])
                    continue;

                foreach (var link in graph.LinksOf(current))
                {
                    if (isDown(link.To))
                        continue;

                    double through = spent + link.CostMw;

                    if (!cost.TryGetValue(link.To, out double known) || through < known)
                    {
                        cost[link.To] = through;
                        parent[link.To] = current;
                        queue.Enqueue(link.To, through);
                    }
                }
            }

            return new DijkstraResult(cost, parent);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
