using SmartX.Api.Topology;

namespace SmartX.Tests
{
    public class MeshGraphTests
    {
        private static Func<string, bool> Down(params string[] ids)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            return set.Contains;
        }

        private static List<string> Level(Dictionary<string, int> hops, int level) =>
            hops.Where(pair => pair.Value == level).Select(pair => pair.Key).OrderBy(id => id).ToList();
        //----------------------------------------------------------//


        [Fact]
        public void Seed_HasSixteenNodes_TwentyEightLinks_AndTheExpectedTotal()
        {
            var graph = MeshSeed.Build();

            Assert.Equal(16, graph.Nodes.Count);
            Assert.Equal(28, graph.Links.Count);
            Assert.Equal(99.2, graph.Links.Sum(link => link.CostMw), 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void EveryLink_IsStoredAtBothEnds()
        {
            var graph = MeshSeed.Build();

            int stored = graph.Nodes.Sum(node => graph.LinksOf(node.Id).Count);

            Assert.Equal(56, stored);
            Assert.Contains(graph.LinksOf("ESP32-SOIL-02"), link => link.To == "ESP32-PMP-01");
            Assert.Contains(graph.LinksOf("ESP32-PMP-01"), link => link.To == "ESP32-SOIL-02");
        }
        //----------------------------------------------------------//


        [Fact]
        public void Nodes_CanBeFoundByIdOrByMacInAnySpelling()
        {
            var graph = MeshSeed.Build();

            Assert.True(graph.TryGetNode("esp32-soil-02", out var byId));
            Assert.True(graph.TryGetNode("24-6f-28-a1-4c-0c", out var byMac));
            Assert.Equal("24:6F:28:A1:4C:0C", byId.Mac);
            Assert.Equal(byId, byMac);
            Assert.True(graph.TryGetNode("24:6F:28:A1:4C:00", out var gateway));
            Assert.Equal("GW-01", gateway.Id);
            Assert.False(graph.TryGetNode("ESP32-NOPE-99", out _));
        }
        //----------------------------------------------------------//


        [Fact]
        public void AddLink_RefusesAnUnknownNode_ANegativeCost_AndADuplicate()
        {
            var graph = MeshSeed.Build();

            Assert.Throws<ArgumentException>(() => graph.AddLink("ESP32-ENV-01", "ESP32-NOPE-99", 2.0));
            Assert.Throws<ArgumentException>(() => graph.AddLink("ESP32-NOPE-99", "ESP32-ENV-01", 2.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.AddLink("ESP32-ENV-01", "ESP32-PMP-02", -0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => graph.AddLink("ESP32-ENV-01", "ESP32-PMP-02", double.NaN));

            // Already linked, in either direction and whatever the case of the ids.
            Assert.Throws<InvalidOperationException>(() => graph.AddLink("GW-01", "ESP32-ENV-01", 9.9));
            Assert.Throws<InvalidOperationException>(() => graph.AddLink("esp32-env-01", "gw-01", 9.9));
        }
        //----------------------------------------------------------//


        [Fact]
        public void AddLink_StoresNothing_WhenItRefusesALink()
        {
            var graph = MeshSeed.Build();

            Assert.ThrowsAny<Exception>(() => graph.AddLink("ESP32-ENV-01", "ESP32-PMP-02", -1));
            Assert.ThrowsAny<Exception>(() => graph.AddLink("GW-01", "ESP32-ENV-01", 1));

            Assert.Equal(28, graph.Links.Count);
            Assert.Equal(56, graph.Nodes.Sum(node => graph.LinksOf(node.Id).Count));
            Assert.DoesNotContain(graph.LinksOf("ESP32-ENV-01"), link => link.To == "ESP32-PMP-02");

            graph.AddLink("ESP32-ENV-01", "ESP32-PMP-02", 0);
            Assert.Equal(29, graph.Links.Count);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Bfs_GivesTheExpectedHopLevels_WhenEverythingIsUp()
        {
            var graph = MeshSeed.Build();

            var bfs = MeshAlgorithms.Bfs(graph, MeshSeed.GatewayId, Down());

            Assert.Equal(16, bfs.Order.Count);
            Assert.Equal(new[] { "GW-01" }, Level(bfs.Hops, 0));
            Assert.Equal(new[] { "ESP32-ENV-01", "ESP32-ENV-02", "ESP32-PMP-01", "ESP32-SOIL-01" }, Level(bfs.Hops, 1));
            Assert.Equal(new[] { "ESP32-ACT-01", "ESP32-ACT-02", "ESP32-PWR-01", "ESP32-SOIL-02", "ESP32-VLV-01", "ESP32-VLV-02" }, Level(bfs.Hops, 2));
            Assert.Equal(new[] { "ESP32-PMP-02", "ESP32-PWR-02", "ESP32-PWR-03", "ESP32-TMP-01" }, Level(bfs.Hops, 3));
            Assert.Equal(new[] { "ESP32-ENV-03" }, Level(bfs.Hops, 4));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Dfs_ReachesTheSameNodesAsBfs_InADifferentOrder()
        {
            var graph = MeshSeed.Build();
            var down = Down("ESP32-PMP-01");

            var bfs = MeshAlgorithms.Bfs(graph, MeshSeed.GatewayId, down).Order;
            var dfs = MeshAlgorithms.Dfs(graph, MeshSeed.GatewayId, down);

            Assert.Equal(15, dfs.Count);
            Assert.Equal(bfs.OrderBy(id => id), dfs.OrderBy(id => id));
            Assert.NotEqual(bfs, dfs);
            Assert.DoesNotContain("ESP32-PMP-01", dfs);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
