using SmartX.Api.Topology;

namespace SmartX.Tests
{
    public class MeshScaleTests
    {
        private const int Nodes = 2000;

        /// <summary>
        /// A random mesh that is connected by construction: every node links to one earlier node (a
        /// spanning tree), then twice as many random extra links are added, skipping pairs already linked.
        /// </summary>
        private static MeshGraph RandomConnectedMesh(int count, int seed)
        {
            var random = new Random(seed);
            var graph = new MeshGraph();

            for (int i = 0; i < count; i++)
            {
                string mac = $"24:6F:28:{(i >> 16) & 0xFF:X2}:{(i >> 8) & 0xFF:X2}:{i & 0xFF:X2}";
                graph.AddNode(new MeshNode(Id(i), $"Node {i}", mac, random.Next(900), random.Next(540)));
            }

            var linked = new HashSet<(int, int)>();

            void Link(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);

                if (a != b && linked.Add(key))
                    graph.AddLink(Id(a), Id(b), Math.Round(1 + random.NextDouble() * 9, 1));
            }

            for (int i = 1; i < count; i++)
                Link(i, random.Next(i));

            for (int i = 0; i < count * 2; i++)
                Link(random.Next(count), random.Next(count));

            return graph;
        }

        private static string Id(int index) => $"N{index:D4}";
        //----------------------------------------------------------//


        [Fact]
        public void Prim_And_Bfs_CoverEveryNode_OfARandom2000NodeMesh()
        {
            var graph = RandomConnectedMesh(Nodes, seed: 11);

            var mst = MeshAlgorithms.Prim(graph, "N0000", _ => false);
            var bfs = MeshAlgorithms.Bfs(graph, "N0000", _ => false);

            Assert.Equal(Nodes, graph.Nodes.Count);
            Assert.Equal(Nodes - 1, mst.Edges.Count);
            Assert.Empty(mst.Unreachable);
            Assert.Equal(Nodes, bfs.Order.Count);
            Assert.Equal(Nodes, bfs.Hops.Count);
            Assert.Equal(Nodes, new HashSet<string>(bfs.Order).Count);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Dijkstra_NeverCostsMoreThanTheSpanningTreeRoute_OnALargeMesh()
        {
            var graph = RandomConnectedMesh(Nodes, seed: 29);

            var mst = MeshAlgorithms.Prim(graph, "N0000", _ => false);
            var cheapest = MeshAlgorithms.Dijkstra(graph, "N0000", _ => false);

            var treeCost = new Dictionary<string, double> { ["N0000"] = 0 };
            foreach (var edge in mst.Edges)
                treeCost[edge.To] = edge.CostMw;

            Assert.Equal(Nodes, cheapest.Cost.Count);

            foreach (var node in graph.Nodes)
            {
                double viaTree = MeshAlgorithms.RouteTo(mst.Parent, node.Id).Sum(id => id == "N0000" ? 0 : treeCost[id]);
                Assert.True(cheapest.Cost[node.Id] <= viaTree + 1e-9, $"{node.Id}: dijkstra {cheapest.Cost[node.Id]} vs tree {viaTree}");
            }
        }
        //----------------------------------------------------------//


        [Fact]
        public void Prim_OnALargeMesh_SpansOnlyTheRootsPiece_WhenAHubFails()
        {
            var graph = RandomConnectedMesh(Nodes, seed: 3);

            // Take every neighbour of the root down: the root is alone, and nothing else can be reached.
            var isolated = new HashSet<string>(graph.LinksOf("N0000").Select(link => link.To));

            var mst = MeshAlgorithms.Prim(graph, "N0000", isolated.Contains);
            var bfs = MeshAlgorithms.Bfs(graph, "N0000", isolated.Contains);

            Assert.Empty(mst.Edges);
            Assert.Equal(new[] { "N0000" }, bfs.Order);
            Assert.Equal(Nodes - 1 - isolated.Count, mst.Unreachable.Count);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
