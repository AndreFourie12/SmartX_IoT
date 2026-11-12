using SmartX.Api.Streaming;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Tests
{
    public class TimelineStoreTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 12, 8, 30, 0, TimeSpan.Zero);

        private static DeviceSnapshot Device(string id) => new(
            id, id, SensorCategory.SoilMoisture, "test", DeviceAlertState.Normal,
            null, false, null, null, null, null, null);
        //----------------------------------------------------------//


        [Fact]
        public void Range_ReturnsReadingsInTimeOrder_EvenWhenAppendedOutOfOrder()
        {
            var store = new TelemetryTimelineStore();
            store.Append("ESP32-SOIL-02", Now.AddSeconds(8), 3, 30);
            store.Append("ESP32-SOIL-02", Now.AddSeconds(0), 1, 10);
            store.Append("ESP32-SOIL-02", Now.AddSeconds(4), 2, 20);

            var points = store.Range("ESP32-SOIL-02", Now, Now.AddSeconds(8));

            Assert.Equal(new[] { 10.0, 20.0, 30.0 }, points.Select(p => p.Value));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Append_EvictsTheOldestReading_PastTheCap()
        {
            var store = new TelemetryTimelineStore();

            for (int i = 0; i < TelemetryTimelineStore.MaxPointsPerDevice + 10; i++)
                store.Append("ESP32-SOIL-02", Now.AddSeconds(i), i, i);

            Assert.Equal(TelemetryTimelineStore.MaxPointsPerDevice, store.CountFor("ESP32-SOIL-02"));
            Assert.Empty(store.Range("ESP32-SOIL-02", Now, Now.AddSeconds(9)));
        }
        //----------------------------------------------------------//


        [Fact]
        public void BuildRange_VisitsFarFewerNodesThanAreStored()
        {
            var store = new TelemetryTimelineStore();
            store.SeedHistory(Now);

            var from = Now.AddMinutes(-60);
            var (timeline, visited, height) = store.BuildRange(Device("ESP32-SOIL-02"), from, from.AddMinutes(10));

            Assert.True(timeline.Points.Count > 0);
            Assert.Equal(timeline.Points.OrderBy(p => p.TimestampUtc).Select(p => p.TimestampUtc), timeline.Points.Select(p => p.TimestampUtc));
            Assert.True(visited < timeline.TotalStored / 10, $"visited {visited} of {timeline.TotalStored}");
            Assert.True(height <= 18);
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
