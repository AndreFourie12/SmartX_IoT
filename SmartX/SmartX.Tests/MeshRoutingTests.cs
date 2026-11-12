using SmartX.Api.Topology;

namespace SmartX.Tests
{
    public class MeshRoutingTests
    {
        private static Func<string, bool> Down(params string[] ids)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        private static double CostOf(MeshGraph graph, List<string> path)
        {
            double total = 0;
            for (int i = 0; i < path.Count - 1; i++)
                total += graph.LinksOf(path[i]).First(link => link.To == path[i + 1]).CostMw;
            return total;
        }
        //----------------------------------------------------------//


        [Fact]
        public void Prim_SpansAllSixteenNodes_AtTheExpectedCost()
        {
            var graph = MeshSeed.Build();

            var mst = MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, Down());

            Assert.Equal(15, mst.Edges.Count);
            Assert.Equal(41.3, mst.TotalCost, 6);
            Assert.Empty(mst.Unreachable);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Prim_StillReachesEveryLiveNode_WhenPumpAIsDown()
        {
            var graph = MeshSeed.Build();
            var down = Down("ESP32-PMP-01");

            var mst = MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, down);
            var liveLinks = graph.Links.Where(link => !down(link.From) && !down(link.To)).ToList();

            Assert.Equal(24, liveLinks.Count);
            Assert.Equal(83.8, liveLinks.Sum(link => link.CostMw), 6);
            Assert.Equal(14, mst.Edges.Count);
            Assert.Equal(40.0, mst.TotalCost, 6);
            Assert.Empty(mst.Unreachable);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Route_ForMoistureSensorB_BeforeTheFailure()
        {
            var graph = MeshSeed.Build();
            var mst = MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, Down());

            var path = MeshAlgorithms.RouteTo(mst.Parent, "ESP32-SOIL-02");

            Assert.Equal(new[] { "ESP32-SOIL-02", "ESP32-PMP-01", "ESP32-SOIL-01", "GW-01" }, path);
            Assert.Equal(9.2, CostOf(graph, path), 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Route_ForMoistureSensorB_WhenPumpAIsDown()
        {
            var graph = MeshSeed.Build();
            var mst = MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, Down("ESP32-PMP-01"));

            var path = MeshAlgorithms.RouteTo(mst.Parent, "ESP32-SOIL-02");

            Assert.Equal(new[] { "ESP32-SOIL-02", "ESP32-PMP-02", "ESP32-VLV-02", "ESP32-VLV-01", "ESP32-SOIL-01", "GW-01" }, path);
            Assert.Equal(15.7, CostOf(graph, path), 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void FewestHopRoute_IsShorterButCostsMore_ThanTheTreeRoute()
        {
            var graph = MeshSeed.Build();
            var bfs = MeshAlgorithms.Bfs(graph, MeshSeed.GatewayId, Down("ESP32-PMP-01"));

            var path = MeshAlgorithms.RouteTo(bfs.Parent, "ESP32-SOIL-02");

            Assert.Equal(new[] { "ESP32-SOIL-02", "ESP32-PMP-02", "ESP32-VLV-01", "ESP32-SOIL-01", "GW-01" }, path);
            Assert.Equal(16.1, CostOf(graph, path), 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void ThreeRoutes_ForMoistureSensorB_WithPumpADown_DifferInHopsAndPower()
        {
            var graph = MeshSeed.Build();
            Func<string, bool> down = Down("ESP32-PMP-01");

            var treePath = MeshAlgorithms.RouteTo(MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, down).Parent, "ESP32-SOIL-02");
            var fewestPath = MeshAlgorithms.RouteTo(MeshAlgorithms.Bfs(graph, MeshSeed.GatewayId, down).Parent, "ESP32-SOIL-02");
            var dijkstra = MeshAlgorithms.Dijkstra(graph, MeshSeed.GatewayId, down);
            var cheapestPath = MeshAlgorithms.RouteTo(dijkstra.Parent, "ESP32-SOIL-02");

            // Spanning tree: the cheapest network overall, but not this node's cheapest way home.
            Assert.Equal(5, treePath.Count - 1);
            Assert.Equal(15.7, CostOf(graph, treePath), 6);

            // Breadth-first: fewest hops, and here it costs the most power.
            Assert.Equal(4, fewestPath.Count - 1);
            Assert.Equal(16.1, CostOf(graph, fewestPath), 6);

            // Dijkstra: also four hops, but by the cheaper link between Valve 2 and Moisture Sensor A.
            Assert.Equal(new[] { "ESP32-SOIL-02", "ESP32-PMP-02", "ESP32-VLV-02", "ESP32-SOIL-01", "GW-01" }, cheapestPath);
            Assert.Equal(13.8, CostOf(graph, cheapestPath), 6);
            Assert.Equal(13.8, dijkstra.Cost["ESP32-SOIL-02"], 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Dijkstra_SkipsDownNodes_AndLeavesCutOffNodesOut()
        {
            var graph = MeshSeed.Build();

            var result = MeshAlgorithms.Dijkstra(graph, MeshSeed.GatewayId, Down("ESP32-SOIL-02", "ESP32-PMP-02"));

            Assert.DoesNotContain("ESP32-TMP-01", result.Cost.Keys);
            Assert.DoesNotContain("ESP32-SOIL-02", result.Cost.Keys);
            Assert.Equal(0, result.Cost["GW-01"]);
            Assert.Equal(13, result.Cost.Count);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Prim_ReportsACutOffNode_InsteadOfThrowing()
        {
            var graph = MeshSeed.Build();

            // Greenhouse Temp only has two links, to Moisture Sensor B and Pump B. Take both down.
            var down = Down("ESP32-SOIL-02", "ESP32-PMP-02");

            var mst = MeshAlgorithms.Prim(graph, MeshSeed.GatewayId, down);
            var bfs = MeshAlgorithms.Bfs(graph, MeshSeed.GatewayId, down);

            Assert.Equal(new[] { "ESP32-TMP-01" }, mst.Unreachable);
            Assert.Equal(12, mst.Edges.Count);
            Assert.DoesNotContain("ESP32-TMP-01", bfs.Order);
            Assert.Equal(new[] { "ESP32-TMP-01" }, MeshAlgorithms.RouteTo(mst.Parent, "ESP32-TMP-01"));
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
