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
    }
}

//------------------------------------------- End Of File -------------------------------------------//
