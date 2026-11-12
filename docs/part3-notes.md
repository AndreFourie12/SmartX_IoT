# Part 3 notes for the report

Short notes on each structure: what it is, where the page uses it, how fast it is, and one measured number.
The numbers come from the running gateway (sample mesh, seeded history) and from the xUnit tests in `SmartX.Tests`.

## Self-balancing tree (AVL)

- **What:** `AvlTree<TKey,TValue>` in `SmartX.Shared/Collections/AvlTree.cs`, written by hand. Each node stores its height. After an insert or `RemoveMin`, any node whose two sides differ in height by more than one is fixed with a single or double rotation.
- **Where:** one tree per sensor inside `TelemetryTimelineStore`, keyed by `TimelineKey` (timestamp, then ingest sequence). It feeds the Historical telemetry card and the timelines on the Command Stream page.
- **Complexity:** `Add`, `TryGet`, `Contains`, `Remove` and `RemoveMin` are O(log n). `Range` is O(log n + k) for k matches, because a node whose key is not above `from` never goes left and a node whose key is not below `to` never goes right. Enumerating the tree walks every entry in key order in O(n), using a stack instead of recursion.
- **Remove:** a leaf or a one-child node is replaced by what hangs below it. A two-child node is replaced by the smallest node on its right, which `DetachMin` finds and detaches (the same helper `RemoveMin` uses). Heights are fixed on the way back up.
- **Measured:** a 7 minute window on ESP32-SOIL-02 returned 105 readings by visiting 117 of 4,047 stored nodes. The tree height was 12, against a limit of about 17 for an AVL tree of that size (1.44 x log2 n). The old `SortedDictionary` walk started at the smallest key and skipped every earlier reading, so a late window cost O(n).
- **At scale:** a test loads 60,000 points into one tree (the timeline cap stays at 5,000). Height was 19 against a limit of 22.9, and a 100-point window was found by visiting 116 nodes. Removing 20,000 of them left the height at 18.

## Graph

- **What:** `MeshGraph` in `SmartX.Api/Topology/MeshGraph.cs`. Nodes are vertices and wireless links are undirected weighted edges (cost in mW per packet). It uses adjacency lists, one list per node, and every link is stored at both ends.
- **Where:** `MeshSeed` builds the 16 nodes and 28 links. `MeshTopologyService` runs the algorithms on it for every request, and the topology page draws the result.
- **Complexity:** a node's neighbours cost O(degree) to walk, and the lists take O(V + E) space. A matrix would take O(V squared) however few links exist.
- **Validation:** `AddLink` refuses an unknown node, a negative or not-a-number cost, and a pair that is already linked (checked against one node's own list, so it costs the node's degree). A refused link stores nothing.
- **Measured:** 16 nodes, 28 links, 56 adjacency entries. All 28 links cost 99.2 mW together. A test also builds a random connected 2,000-node mesh: Prim returns 1,999 edges (V - 1) and BFS reaches all 2,000 nodes.

## BFS and DFS

- **What:** `MeshAlgorithms.Bfs` uses a `Queue<string>`, and `Dfs` uses a `Stack<string>`. A down node is skipped, so the links touching it are skipped too.
- **Where:** BFS gives the "Reached 14 of 14 live nodes" figure, the nodes cut off by a failure, and the fewest-hop route on the Route card. The BFS/DFS toggle shows the two visit orders.
- **Complexity:** both are O(V + E).
- **Measured:** with every node up, BFS reaches the gateway at level 0 and the furthest node (Climate Node 3) at level 4. With Pump A down it still reaches all 14 live nodes. Both searches reach the same nodes and differ only in order.

## Minimum spanning tree (Prim) and the priority queue

- **What:** `MeshAlgorithms.Prim` grows a tree from the gateway. A `PriorityQueue<(string From, string To), double>` always returns the cheapest link leaving the tree built so far. If a failure splits the mesh it spans only the gateway's piece and lists the rest as unreachable.
- **Where:** it picks the blue backbone, the hop counts in the node circles, the Route to gateway card and the MST power cost in the header.
- **Complexity:** O(E log V) with the binary heap behind `PriorityQueue`.
- **Measured:** all nodes up, the tree has 15 edges and costs 41.3 mW against 99.2 mW for every link. With Pump A down it has 14 edges and costs 40.0 mW against 83.8 mW for the 24 links still alive.
- **Worth explaining:** the tree minimises the power of the whole network, not of each node's own path, so a node's tree route is not always its cheapest possible route. With Pump A down, Moisture Sensor B's tree route is 5 hops at 15.7 mW, while its fewest-hop (BFS) route is 4 hops at 16.1 mW: fewer hops, more power.

## Dijkstra least-power route

- **What:** `MeshAlgorithms.Dijkstra` finds the cheapest total power from the gateway to every node. It uses the same `PriorityQueue` and parent-map pattern as Prim, but the queue holds the total cost so far, so each node comes out of the queue with its final cost (link costs are never negative).
- **Where:** the Route to gateway card lists each node's route three ways: spanning tree, fewest hops (BFS) and least power (Dijkstra), with the cheapest in green.
- **Complexity:** O(E log V), the same as Prim.
- **Measured:** with Pump A down, Moisture Sensor B costs 15.7 mW by the tree (5 hops), 16.1 mW by BFS (4 hops) and 13.8 mW by Dijkstra (4 hops, through Valve 2 and the 4.8 mW link to Moisture Sensor A). On the 2,000-node test mesh no node's Dijkstra cost is ever above its tree route cost.

## Self-healing demo

- Taking Pump A down loses 4 links and isolates no nodes. Five nodes change route: Valve 1 (3 to 2 hops), Moisture Sensor B (3 to 5), Pump B (4 to 4), Valve 2 (5 to 3) and Greenhouse Temp (4 to 6).
- Simulate node failure also starts a simulator outage for that node (and Restore ends it), so the Command Stream page shows the same node going offline once the health monitor notices the silence.
- Taking Moisture Sensor B and Pump B down together isolates Greenhouse Temp, which only has links to those two. Prim reports it as unreachable instead of failing.
