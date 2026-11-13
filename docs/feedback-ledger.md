# Smart-X Feedback and Update Ledger (Part 3)

**Student:** Andre Fourie (ST10438312)
**Module:** PROG7312 POE
**Date:** 13 November 2026

This file lists the updates made to Smart-X after the lecturer's feedback on Parts 1 and 2, and the refinements and bug fixes made while building Part 3. Each entry says where the change lives and how it was checked.

## 1. Feedback received

| Part | Mark | Feedback |
|---|---|---|
| Part 1 | 100% | None given. |
| Part 2 | 95% | "Engine execution path is mostly synchronous; rubric's explicit async proactive processing pattern is not strongly represented in engine internals." |

## 2. Lecturer feedback and what was done

### 2.1 Part 2: asynchronous, proactive processing

**What the feedback said.** The prediction engine (`ActionRecommendationEngine`) ran mostly as ordinary synchronous code, so the asynchronous, proactive pattern in the rubric was not clear inside it.

**Before.** Two background workers already fed the engine, so learning happened off the request path:

| Worker | Pattern | What it does for the engine |
|---|---|---|
| `TelemetryProcessingService` | `BackgroundService` loop that waits on `WaitForAlertAsync` (a `SemaphoreSlim`), so an alert wakes it at once and routine readings are drained on a timer | Calls `OnConditionRaised`, `OnConditionUpdated` and `OnConditionCleared` as readings are processed |
| `DeviceHealthMonitorService` | `BackgroundService` loop on a `PeriodicTimer` (`WaitForNextTickAsync`, every 2 seconds) | Raises an offline condition on the engine when a device goes silent |

The analysis step itself (`Analyse`, which fits a trend line to each device and ranks the suggestions) was different. It ran on the request thread, once for every Command Stream snapshot, about once a second.

**Change made.** The analysis now runs proactively in the background:

- A new `InsightRefreshService` (`SmartX.Api/Streaming/InsightRefreshService.cs`) is a `BackgroundService` that loops on a `PeriodicTimer` of about 1 second. Each tick it runs the analysis on the current device snapshot and stores the resulting report. An error on one tick is logged and the loop carries on, and the loop stops cleanly when the host shuts down.
- `CommandStreamService` keeps the latest report in a single field that is swapped as a whole (the report is an immutable record), so readers never see a half-built result.
- `Snapshot`, and the one endpoint that also used the analysis, now read the stored report instead of computing it. If the first second after start-up has produced no report yet, they compute one once so the page is never empty.
- The service is registered in `Program.cs` with `AddHostedService<InsightRefreshService>()`, next to the two existing workers.
- The engine's logic, its results and the JSON the client receives are unchanged.

**Effect.** The Command Stream request no longer does the analysis work. All three engine paths (learning on alerts, offline detection and the analysis itself) now run on background `async` loops, and the page reads their last result. Suggestions can be up to about one second old, and the page already refreshes once a second.

**Checked by:** a clean build, the 42 existing tests still passing, and the snapshot endpoint still returning insights, patterns and predicted searches.

**Status:** addressed.

The new Network Topology page follows the same pattern on the client: `TopologyView` loads and refreshes through `async` methods (`OnActivatedAsync`, `RefreshAsync` and the async click handlers), so the interface thread never waits on the API, and its three-second poll only runs while the tab is visible.

## 3. Bugs found and fixed during Part 3

| # | Problem | Fix | Where | Checked by |
|---|---|---|---|---|
| 1 | Restoring a failed node could report no links restored, because `AlertTracker` still listed the node as disconnected until its next packet, so the algorithms kept treating it as down. | The recompute after a restore counts that node as up (`Compute(treatAsUp)`). | `MeshTopologyService.cs` | `MeshTopologyServiceTests`: a restore while the node is still flagged disconnected expects 4 links restored and 5 nodes re-routed. |
| 2 | A simulated failure only changed the topology page; the Command Stream page still showed the node reporting. | Failing a node also starts a simulator outage for it, and restoring it ends the outage. | `SimulationControl.cs`, `TopologyEndpoints.cs` | Run in the app: Pump A goes offline on both pages. |
| 3 | A split mesh (Moisture Sensor B and Pump B down) would have broken a spanning tree that assumes the mesh is connected. | Prim spans only what the gateway can reach and returns the rest as an unreachable list. | `MeshAlgorithms.cs` | `MeshRoutingTests`, `MeshScaleTests` |
| 4 | The project failed to build in Visual Studio when the repository was downloaded as a ZIP, because nested folders passed Windows' 260-character path limit. | `Directory.Build.props` moves the intermediate `obj` folder to a short path under `LOCALAPPDATA`, only when the project path is over 140 characters. | `Directory.Build.props` | Normal clones build as before; the README describes it. |
| 5 | The README still described the project as Part 2 and the topology tab as a locked placeholder. | README rewritten for Part 3, with a Part 3 walkthrough and requirements table. | `README.md` | Read against the running app. |

## 4. Refinements made during Part 3

| # | Refinement | Reason | Where |
|---|---|---|---|
| 1 | The device timeline moved from `SortedDictionary` to a hand-written `AvlTree<TKey,TValue>`, and the per-device cap rose from 1,500 to 5,000 readings. | Part 2's `Range` walked from the smallest key, so late windows cost O(n). The tree skips whole subtrees. | `AvlTree.cs`, `TelemetryTimelineStore.cs` |
| 2 | The store's public methods (`Append`, `Range`, `CountFor`, `BuildTimeline`) were left unchanged. | Parts 1 and 2 had to keep working without edits. | `TelemetryTimelineStore.cs` |
| 3 | Dijkstra's least-power route was added beside Prim. | The spanning tree minimises the whole network, not each node's own path. With Pump A down, Moisture Sensor B costs 15.7 mW by the tree, 16.1 mW by BFS and 13.8 mW by Dijkstra. | `MeshAlgorithms.cs`, Route options card |
| 4 | `TryGet`, `Contains`, `Remove` and an in-order enumerator were added to the AVL tree. | A complete tree type needs lookup and deletion, not only insert and range. `Remove` handles leaf, one-child and two-child nodes. | `AvlTree.cs`, `AvlTreeTests` |
| 5 | The 60,000-point test also evicts the oldest 10,000 with `RemoveMin` and checks the tree is still within its height bound. | Eviction is what the timeline store does at its cap. | `AvlTreeTests.cs` |
| 6 | A random 2,000-node mesh test was added. | To show Prim, BFS and Dijkstra still behave at a larger size. | `MeshScaleTests.cs` |
| 7 | The Network Topology page was reworked to fit the window: lists scroll inside their cards, node names are placed clear of links, scroll bars were slimmed, and the route card was simplified. | Nothing on the page should sit below the fold, and the page should look like the other two pillars. | `TopologyView.xaml`, `TopologyView.xaml.cs`, `Theme.xaml` |
| 8 | The test project now holds 42 xUnit test methods across six files. | Every Part 3 feature has expected values fixed in a test. | `SmartX.Tests` |

## 5. Still open

- The Part 3 video link is added to the README once the video is uploaded.
