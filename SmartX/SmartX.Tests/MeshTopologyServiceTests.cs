using SmartX.Api.Streaming;
using SmartX.Api.Topology;
using SmartX.Shared.Contracts;

namespace SmartX.Tests
{
    public class MeshTopologyServiceTests
    {
        private static MeshTopologyService NewService(AlertTracker? alerts = null) =>
            new(alerts ?? new AlertTracker(), new LiveDeviceRegistry(), new TelemetryTimelineStore());
        //----------------------------------------------------------//


        [Fact]
        public void Snapshot_ShowsTheHeaderFigures_WhenEverythingIsUp()
        {
            var snapshot = NewService().Snapshot();

            Assert.Equal(15, snapshot.NodesOnline);
            Assert.Equal(15, snapshot.NodesTotal);
            Assert.Equal(28, snapshot.LinksActive);
            Assert.Equal(15, snapshot.Links.Count(link => link.State == MeshLinkState.Mst));
            Assert.Equal(41.3, snapshot.MstCostMw, 6);
            Assert.Equal(99.2, snapshot.FullMeshCostMw, 6);
            Assert.Empty(snapshot.UnreachableIds);
        }
        //----------------------------------------------------------//


        [Fact]
        public void SetDown_ForPumpA_ReroutesFiveNodes_AndTheMeshHeals()
        {
            var service = NewService();

            var result = service.SetDown("ESP32-PMP-01", down: true)!;
            var snapshot = service.Snapshot();

            Assert.True(result.WentDown);
            Assert.Equal(4, result.LinksLost);
            Assert.Equal(0, result.NodesIsolated);
            Assert.True(result.Healed);
            Assert.Equal(
                new[] { "Greenhouse Temp", "Moisture Sensor B", "Pump B", "Valve 1", "Valve 2" },
                result.Rerouted.Select(node => node.Name).Order());

            var sensorB = result.Rerouted.Single(node => node.Name == "Moisture Sensor B");
            Assert.Equal(3, sensorB.HopsBefore);
            Assert.Equal(5, sensorB.HopsAfter);

            Assert.Equal(14, snapshot.NodesOnline);
            Assert.Equal(24, snapshot.LinksActive);
            Assert.Equal(40.0, snapshot.MstCostMw, 6);
            Assert.Equal(83.8, snapshot.FullMeshCostMw, 6);
            Assert.Equal(MeshNodeStatus.Down, snapshot.Nodes.Single(node => node.Id == "ESP32-PMP-01").Status);
        }
        //----------------------------------------------------------//


        [Fact]
        public void SetDown_ThenUp_PutsTheMeshBack()
        {
            var service = NewService();
            service.SetDown("ESP32-PMP-01", down: true);

            var result = service.SetDown("24:6F:28:A1:4C:0A", down: false)!;

            Assert.False(result.WentDown);
            Assert.Equal(4, result.LinksRestored);
            Assert.Equal(5, result.Rerouted.Count);
            Assert.Equal(41.3, service.Snapshot().MstCostMw, 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void Restoring_ANodeTheMonitorStillFlagsDisconnected_ReportsTheLinksItBringsBack()
        {
            var alerts = new AlertTracker();
            var service = NewService(alerts);

            // Pump A fails, goes silent and is flagged by the health monitor; the operator then restores it
            // before its first packet has cleared that flag.
            service.SetDown("ESP32-PMP-01", down: true);
            alerts.RecordSilent(["ESP32-PMP-01"]);

            var result = service.SetDown("ESP32-PMP-01", down: false)!;

            Assert.False(result.WentDown);
            Assert.Equal(4, result.LinksRestored);
            Assert.Equal(5, result.Rerouted.Count);
        }
        //----------------------------------------------------------//


        [Fact]
        public void NodesTheHealthMonitorFlagsDisconnected_CountAsDown()
        {
            var alerts = new AlertTracker();
            var service = NewService(alerts);

            alerts.RecordSilent(["ESP32-PMP-01"]);

            var snapshot = service.Snapshot();
            Assert.Equal(14, snapshot.NodesOnline);
            Assert.Equal(40.0, snapshot.MstCostMw, 6);
        }
        //----------------------------------------------------------//


        [Fact]
        public void SetDown_ForTwoNodes_IsolatesGreenhouseTemp()
        {
            var service = NewService();
            service.SetDown("ESP32-SOIL-02", down: true);

            var result = service.SetDown("ESP32-PMP-02", down: true)!;
            var route = service.RouteFor("ESP32-TMP-01")!;

            Assert.Equal(1, result.NodesIsolated);
            Assert.False(result.Healed);
            Assert.False(route.Reachable);
            Assert.Empty(route.Path);
            Assert.Equal(new[] { "ESP32-TMP-01" }, service.Snapshot().UnreachableIds);
        }
        //----------------------------------------------------------//


        [Fact]
        public void RouteFor_AcceptsAMac_AndReportsBothRoutes()
        {
            var service = NewService();
            service.SetDown("ESP32-PMP-01", down: true);

            var route = service.RouteFor("24-6f-28-a1-4c-0c")!;

            Assert.Equal("ESP32-SOIL-02", route.NodeId);
            Assert.Equal(5, route.TotalHops);
            Assert.Equal(15.7, route.TotalCostMw, 6);
            Assert.Equal(4, route.FewestHopPath.Count - 1);
            Assert.Equal(16.1, route.FewestHopCostMw, 6);
            Assert.Equal(4, route.LeastPowerPath.Count - 1);
            Assert.Equal(13.8, route.LeastPowerCostMw, 6);
            Assert.Null(service.RouteFor("ESP32-NOPE-99"));
        }
        //----------------------------------------------------------//


        [Fact]
        public void TheGatewayCannotBeTakenDown()
        {
            var service = NewService();

            var result = service.SetDown("GW-01", down: true)!;

            Assert.False(result.WentDown);
            Assert.Equal(15, service.Snapshot().NodesOnline);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
