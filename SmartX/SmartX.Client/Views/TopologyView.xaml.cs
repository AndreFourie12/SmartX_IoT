using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SmartX.Client.Navigation;
using SmartX.Client.ViewModels;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Client.Views
{
    /// <summary>
    /// The Network Topology &amp; Mesh Routing pillar.
    ///
    /// While the page is on screen a three second poll fetches the whole mesh in one call, and the
    /// selected node's route in a second. The gateway recomputes BFS and Prim on every request, so
    /// whatever arrives is already current: the page only draws it. The view is cached by the
    /// navigation shell, so the selected node and the search text are still here when the operator
    /// comes back; while it is hidden it stops polling.
    /// </summary>
    public partial class TopologyView : UserControl, IModuleView, IDeviceFocus
    {
        private const string GatewayId = "GW-01";
        private const string DefaultNodeId = "ESP32-SOIL-02";

        // How the From and To boxes show a time, and the other spellings they accept.
        private const string StampFormat = "dd MMM yyyy '·' HH:mm:ss";
        private static readonly string[] StampFormats =
        [
            StampFormat, "dd MMM yyyy HH:mm:ss", "dd MMM yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "HH:mm:ss", "HH:mm"
        ];

        private readonly INavigator _navigator;
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly ObservableCollection<NodeRowViewModel> _rows = [];
        private readonly string _subtitle;

        // The canvas colour at 80% opacity: lets a label sit over a link without hiding it completely.
        private static readonly Brush LabelBackdrop = new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0xF6, 0xF9));

        // Which side of its circle each node's name sits on, chosen so the name clears the links around it.
        // Any node not listed here gets its name underneath.
        private enum LabelSide { Below, Above, Left, Right }

        private static readonly Dictionary<string, LabelSide> LabelSides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ESP32-ENV-01"] = LabelSide.Above,
            ["ESP32-ACT-01"] = LabelSide.Left,
            ["ESP32-PWR-01"] = LabelSide.Left,
            ["ESP32-ENV-02"] = LabelSide.Right,
            ["ESP32-PWR-02"] = LabelSide.Left,
            ["ESP32-SOIL-01"] = LabelSide.Left,
            ["ESP32-PMP-01"] = LabelSide.Right,
            ["ESP32-VLV-01"] = LabelSide.Right,
            ["ESP32-SOIL-02"] = LabelSide.Right,
            ["ESP32-TMP-01"] = LabelSide.Right
        };

        private string _visitKey = string.Empty;
        private TopologySnapshotDto? _snapshot;
        private FailureResultDto? _lastFailure;
        private string? _selectedId;
        private bool _active;
        private bool _refreshing;
        private bool _refreshQueued;
        private bool _syncingList;


        public TopologyView(INavigator navigator)
        {
            InitializeComponent();

            _navigator = navigator;
            _subtitle = StatusText.Text;
            NodeList.ItemsSource = _rows;

            // The history query starts on the demo sensor and the last quarter hour.
            HistorySensorBox.Text = DefaultNodeId;
            HistoryFromBox.Text = DateTime.Now.AddMinutes(-15).ToString(StampFormat, CultureInfo.InvariantCulture);
            HistoryToBox.Text = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture);

            _pollTimer.Tick += async (_, _) => await RefreshAsync();
        }
        //----------------------------------------------------------//



        // ================================ Lifecycle ================================ //

        /// <summary>
        /// Shown: start polling, and refresh straight away.
        /// </summary>
        public async Task OnActivatedAsync()
        {
            _active = true;
            _pollTimer.Start();
            await RefreshAsync();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Hidden: stop talking to the API. The selection and search text stay for when the operator returns.
        /// </summary>
        public void OnDeactivated()
        {
            _active = false;
            _pollTimer.Stop();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Selects a node by id or MAC, clearing the search if it would hide that node.
        /// </summary>
        public void FocusDevice(string deviceId)
        {
            var node = _snapshot?.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, deviceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(n.Mac, DeviceIdentifier.Normalize(deviceId), StringComparison.OrdinalIgnoreCase));

            string id = node?.Id ?? deviceId;

            if (_rows.All(row => !string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)))
                SearchBox.Clear();

            _selectedId = id;
            SelectRow(id);

            if (_active)
                _ = RefreshAsync();
        }
        //----------------------------------------------------------//



        // ================================ Refresh ================================ //

        /// <summary>
        /// Fetches and draws the mesh and the selected node's route. If a refresh is already running,
        /// another is queued behind it instead of overlapping, so a click made mid-request is not lost.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (_refreshing)
            {
                _refreshQueued = true;
                return;
            }

            _refreshing = true;

            try
            {
                do
                {
                    _refreshQueued = false;

                    var snapshot = await App.ApiClient.GetTopologyAsync();
                    if (snapshot is not null)
                    {
                        _snapshot = snapshot;
                        _selectedId ??= DefaultSelection(snapshot);
                        RenderHeader(snapshot);
                        RenderNodes();
                    }

                    var route = _selectedId is null ? null : await App.ApiClient.GetRouteAsync(_selectedId);
                    RenderRoute(route);
                    DrawMesh(route);
                    RenderConnectivity();
                    RenderFailureButton();
                    RenderHopOver(route);

                    StatusText.Text = _subtitle;
                }
                while (_refreshQueued && _active);
            }
            catch (Exception)
            {
                StatusText.Text = "Can't reach the gateway - retrying every 3 seconds. Is SmartX.Api running?";
            }
            finally
            {
                _refreshing = false;
            }
        }
        //----------------------------------------------------------//


        private static string? DefaultSelection(TopologySnapshotDto snapshot) =>
            snapshot.Nodes.Any(node => node.Id == DefaultNodeId)
                ? DefaultNodeId
                : snapshot.Nodes.FirstOrDefault(node => node.Id != GatewayId)?.Id;
        //----------------------------------------------------------//


        private void RenderHeader(TopologySnapshotDto snapshot)
        {
            int down = snapshot.NodesTotal - snapshot.NodesOnline;

            OnlineValue.Text = snapshot.NodesOnline.ToString();
            OnlineTotal.Text = $"of {snapshot.NodesTotal}";
            DownText.Text = $"{down} down";
            DownText.Foreground = Palette.Get(down > 0 ? "StatusRed" : "TextOnDarkMuted");
            IsolatedText.Text = $" · {snapshot.UnreachableIds.Count} isolated";

            LinksValue.Text = snapshot.LinksActive.ToString();
            LinksTotal.Text = $"of {snapshot.LinksTotal}";
            LinksCaption.Text = $"{snapshot.Links.Count(link => link.State == MeshLinkState.Mst)} in the MST";

            CostValue.Text = snapshot.MstCostMw.ToString("F1");
            CostCaption.Text = $"vs {snapshot.FullMeshCostMw:F1} mW on every link";
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Fills the node list from the snapshot, applying the search. Down nodes come first, then the
        /// rest by name. Rows are updated in place so the selection and hover are not disturbed.
        /// </summary>
        private void RenderNodes()
        {
            if (_snapshot is null)
                return;

            string text = SearchBox.Text.Trim();
            var meshNodes = _snapshot.Nodes.Where(node => node.Id != GatewayId).ToList();

            var shown = meshNodes
                .Where(node => Matches(node, text))
                .OrderBy(node => node.Status == MeshNodeStatus.Down ? 0 : 1)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _syncingList = true;

            try
            {
                CollectionSync.Sync(
                    _rows,
                    shown,
                    node => node.Id,
                    row => row.Id,
                    node =>
                    {
                        var row = new NodeRowViewModel(node.Id, node.Name, $"{node.Id}  ·  {node.Mac}");
                        row.Update(node);
                        return row;
                    },
                    (row, node) => row.Update(node));
            }
            finally
            {
                _syncingList = false;
            }

            NodeCountText.Text = text.Length == 0 ? $"· {meshNodes.Count} nodes" : $"· {shown.Count} of {meshNodes.Count} nodes";
            ClearFiltersButton.IsEnabled = text.Length > 0;

            SelectRow(_selectedId);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Matches the search text against a node's name, its id and its MAC address. The MAC match
        /// goes through DeviceIdentifier.Normalize so 24-6f-28-a1-4c-0c finds the same node as the colon form.
        /// </summary>
        private static bool Matches(MeshNodeDto node, string text)
        {
            if (text.Length == 0)
                return true;

            string mac = DeviceIdentifier.Normalize(text).Replace('-', ':');

            return node.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || node.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                || node.Mac.Contains(mac, StringComparison.OrdinalIgnoreCase);
        }
        //----------------------------------------------------------//


        private void RenderRoute(RouteDto? route)
        {
            OpenInStreamButton.IsEnabled = _selectedId is not null && _selectedId != GatewayId;

            if (route is null)
            {
                RouteList.ItemsSource = null;
                RouteOptionList.ItemsSource = null;
                CompareHeader.Visibility = Visibility.Collapsed;
                RouteHops.Text = RoutePower.Text = RouteVia.Text = "--";
                RouteNote.Text = "Pick a node to see how its packets reach the gateway.";
                return;
            }

            if (!route.Reachable)
            {
                bool offline = _snapshot?.Nodes.FirstOrDefault(node => node.Id == route.NodeId)?.Status == MeshNodeStatus.Down;

                RouteList.ItemsSource = null;
                RouteOptionList.ItemsSource = null;
                CompareHeader.Visibility = Visibility.Collapsed;
                RouteHops.Text = RoutePower.Text = "–";
                RouteVia.Text = "No route";
                RouteNote.Text = offline
                    ? $"{route.Name} is offline, so it has no route to the gateway."
                    : $"{route.Name} is cut off: every path to the gateway runs through a node that is down.";
                return;
            }

            var stops = new List<RouteStopRow>();

            for (int i = 0; i < route.Path.Count; i++)
            {
                var hop = route.Path[i];
                bool isGateway = i == route.Path.Count - 1 && route.Path.Count > 1;

                // The first stop is the node itself, filled amber; the gateway is dark; the stops between are rings.
                stops.Add(new RouteStopRow(
                    hop.Name,
                    i == 0 ? FontWeights.Bold : FontWeights.Normal,
                    $"{hop.CostMw:F1} mW",
                    i > 0,
                    i == 0,
                    i > 0,
                    i < route.Path.Count - 1,
                    i == 0 ? Palette.Get("StatusAmberDot") : isGateway ? Palette.Get("ShellDark") : Palette.Get("CardBackground"),
                    isGateway ? Palette.Get("ShellDark") : Palette.Get("StatusAmberDot"),
                    $"{hop.Name}  ·  {hop.Id}  ·  {hop.Mac}"));
            }

            RouteList.ItemsSource = stops;
            RouteHops.Text = route.TotalHops.ToString();
            RoutePower.Text = route.TotalCostMw.ToString("F1");
            RouteVia.Text = route.Path.Count > 1 ? "MST route" : "Gateway";

            RenderRouteOptions(route);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Lists the node's route three ways side by side: the spanning tree route shown above, the
        /// breadth-first route (fewest hops) and Dijkstra's route (least power). The cheapest in power is
        /// shown in green and tagged. When the tree route is not that cheapest one, a line says why: the
        /// tree minimises the power of the whole mesh, not of each node's own path.
        /// </summary>
        private void RenderRouteOptions(RouteDto route)
        {
            bool single = route.Path.Count <= 1;
            CompareHeader.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

            if (single)
            {
                RouteOptionList.ItemsSource = null;
                RouteNote.Text = string.Empty;
                return;
            }

            double best = Math.Min(route.TotalCostMw, Math.Min(route.FewestHopCostMw, route.LeastPowerCostMw));
            double saving = route.TotalCostMw - route.LeastPowerCostMw;

            RouteOptionRow Row(string label, string method, bool inUse, bool cheapest, int hops, double cost) =>
                new(label, method, inUse, cheapest, hops.ToString(), $"{cost:F1} mW", Palette.Get(cost - best < 0.05 ? "StatusGreen" : "TextPrimary"));

            // The tag only means something when the routes differ: three equal routes have no cheapest.
            bool tag = Math.Max(route.TotalCostMw, route.FewestHopCostMw) - route.LeastPowerCostMw >= 0.05;

            RouteOptionList.ItemsSource = new List<RouteOptionRow>
            {
                Row("Spanning tree", "· MST", true, false, route.TotalHops, route.TotalCostMw),
                Row("Fewest hops", "· BFS", false, false, route.FewestHopPath.Count - 1, route.FewestHopCostMw),
                Row("Least power", "· Dijkstra", false, tag, route.LeastPowerPath.Count - 1, route.LeastPowerCostMw)
            };

            RouteNote.Text = saving < 0.05
                ? $"The MST route is already the cheapest way for {route.Name}."
                : $"Least power would save {saving:F1} mW for this node. The MST keeps the whole mesh cheapest instead.";
        }
        //----------------------------------------------------------//



        // ================================ The mesh canvas ================================ //

        /// <summary>
        /// Redraws the whole mesh from the latest snapshot. At sixteen nodes that is cheap, and it means
        /// the canvas can never disagree with the data. Layers are drawn back to front: standby links,
        /// the spanning tree with each link's cost, the selected node's route, then links to a down node.
        /// </summary>
        private void DrawMesh(RouteDto? route)
        {
            MeshCanvas.Children.Clear();

            if (_snapshot is null)
                return;

            var nodes = _snapshot.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
            var routeLinks = RouteKeys(route);
            var treeLinks = _snapshot.Links.Where(link => link.State == MeshLinkState.Mst).ToList();

            foreach (var link in _snapshot.Links.Where(link => link.State == MeshLinkState.Standby))
                AddLink(nodes[link.From], nodes[link.To], Palette.Get("TextMuted"), 1.6, opacity: 0.6);

            foreach (var link in treeLinks)
                AddLink(nodes[link.From], nodes[link.To], Palette.Get("AccentBlue"), 3);

            foreach (var link in treeLinks.Where(link => routeLinks.Contains(LinkKey(link.From, link.To))))
                AddLink(nodes[link.From], nodes[link.To], Palette.Get("StatusAmberDot"), 5);

            foreach (var link in _snapshot.Links.Where(link => link.State == MeshLinkState.Down))
                AddLink(nodes[link.From], nodes[link.To], Palette.Get("StatusRed"), 1.6, dashed: true);

            foreach (var node in _snapshot.Nodes)
                AddNode(node, selected: string.Equals(node.Id, _selectedId, StringComparison.OrdinalIgnoreCase));

            // Costs go on last so a node's name never hides one.
            foreach (var link in treeLinks)
                AddCostChip(nodes[link.From], nodes[link.To], link.CostMw);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The Connectivity strip: how many live nodes a search from the gateway reached, which nodes are
        /// down or cut off, and the order the chosen search (BFS or DFS) visited them in. Both reach the
        /// same nodes, so the toggle only changes the order shown.
        /// </summary>
        private void RenderConnectivity()
        {
            if (_snapshot is null)
                return;

            bool depthFirst = DfsButton.IsChecked == true;
            var order = depthFirst ? _snapshot.DfsOrder : _snapshot.BfsOrder;
            var byId = _snapshot.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);

            // The big figure and the bar: how much of the live mesh the search reached.
            int reached = order.Count(id => id != GatewayId);
            int live = _snapshot.NodesOnline;
            ReachCount.Text = reached.ToString();
            ReachSuffix.Text = $"of {live} live nodes reached";
            ReachFill.Width = new GridLength(reached, GridUnitType.Star);
            ReachRest.Width = new GridLength(Math.Max(live - reached, live == 0 ? 1 : 0), GridUnitType.Star);

            // The line under the bar: what is down and what is cut off, or that nothing is.
            var down = _snapshot.Nodes.Where(node => node.Status == MeshNodeStatus.Down).ToList();
            var cutOff = _snapshot.UnreachableIds.Select(id => byId[id].Name).ToList();
            var mono = (FontFamily)FindResource("MonoFont");

            StateText.Inlines.Clear();

            if (down.Count == 0 && cutOff.Count == 0)
            {
                StateDot.Fill = Palette.Get("StatusGreenDot");
                StateText.Inlines.Add(new Run("All nodes up") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Get("StatusGreen") });
            }
            else
            {
                StateDot.Fill = Palette.Get(down.Count > 0 ? "StatusRed" : "StatusAmberDot");

                if (down.Count > 0)
                {
                    StateText.Inlines.Add(new Run($"{down.Count} down") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Get("StatusRedText") });
                    StateText.Inlines.Add(new Run($"  {string.Join(", ", down.Select(node => node.Name))}"));

                    if (down.Count == 1)
                        StateText.Inlines.Add(new Run($"  {down[0].Id}") { FontFamily = mono, FontSize = 11.5, Foreground = Palette.Get("TextSecondary") });
                }

                if (cutOff.Count > 0)
                {
                    StateText.Inlines.Add(new Run($"{(down.Count > 0 ? "   " : "")}{cutOff.Count} cut off") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Get("StatusAmber") });
                    StateText.Inlines.Add(new Run($"  {string.Join(", ", cutOff)}"));
                }
            }

            StateText.ToolTip = StateText.Text;

            // The visit order, numbered. Only replaced when the order changes so the list keeps its scroll position.
            OrderHint.Text = depthFirst ? "stack · deepest first" : "queue · nearest first";
            string key = string.Join('|', order);

            if (key != _visitKey)
            {
                _visitKey = key;
                OrderList.ItemsSource = order
                    .Select((id, index) => new VisitRow((index + 1).ToString(), id == GatewayId ? "Gateway" : byId[id].Name))
                    .ToList();
            }
        }
        //----------------------------------------------------------//


        private void OnTraversalChanged(object sender, RoutedEventArgs e) => RenderConnectivity();
        //----------------------------------------------------------//



        // ================================ Historical telemetry ================================ //

        /// <summary>
        /// Runs the range query on the gateway. It only runs when asked: the page does not poll history.
        /// A time with no date means today, and times are read as local time.
        /// </summary>
        private async void OnRunQueryClick(object sender, RoutedEventArgs e)
        {
            string sensor = HistorySensorBox.Text.Trim();

            if (sensor.Length == 0)
            {
                ShowHistoryError("Enter a sensor id or MAC address.");
                return;
            }

            if (!TryParseStamp(HistoryFromBox.Text, out var from) || !TryParseStamp(HistoryToBox.Text, out var to))
            {
                ShowHistoryError("I can't read that time. Try 12 Nov 2026 · 08:24:00, 2026-11-12 08:24 or just 08:24:00 for today.");
                return;
            }

            if (from > to)
            {
                ShowHistoryError("From must not be after To.");
                return;
            }

            RunQueryButton.IsEnabled = false;

            try
            {
                var result = await App.ApiClient.GetHistoryAsync(sensor, from, to);

                if (result is not null)
                    RenderHistory(result);
            }
            catch (InvalidOperationException ex)
            {
                ShowHistoryError(ex.Message);
            }
            catch (Exception)
            {
                ShowHistoryError("Can't reach the gateway. Is SmartX.Api running?");
            }
            finally
            {
                RunQueryButton.IsEnabled = true;
            }
        }
        //----------------------------------------------------------//


        private void OnHistoryKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && RunQueryButton.IsEnabled)
                OnRunQueryClick(sender, e);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Draws the result: the chart, the table (newest first) and the caption that says how many tree
        /// nodes the range query visited out of everything stored - the proof it did not scan them all.
        /// </summary>
        private void RenderHistory(TopologyHistoryDto result)
        {
            var timeline = result.Timeline;
            bool sameDay = timeline.FromUtc.ToLocalTime().Date == timeline.ToUtc.ToLocalTime().Date;
            string timeFormat = sameDay ? "HH:mm:ss" : "dd MMM HH:mm:ss";

            HistoryError.Visibility = Visibility.Collapsed;
            HistoryChart.Timeline = timeline;
            HistoryValueColumn.Header = $"{timeline.Category.DisplayName()} {timeline.Unit}".Trim().ToUpperInvariant();

            HistoryTable.ItemsSource = timeline.Points
                .OrderByDescending(point => point.TimestampUtc)
                .Select(point => new HistoryRow(point.TimestampUtc.ToLocalTime().ToString(timeFormat, CultureInfo.InvariantCulture), point.Display))
                .ToList();

            int count = timeline.Points.Count;
            HistoryCaption.Text = count == 0
                ? $"No readings in that window. The tree holds {timeline.TotalStored:N0} readings for {timeline.DisplayName}."
                : $"{count:N0} reading{(count == 1 ? "" : "s")} in timestamp order, found by visiting {result.NodesVisited:N0} of {timeline.TotalStored:N0} tree nodes (tree height {result.TreeHeight}).";
        }
        //----------------------------------------------------------//


        private void ShowHistoryError(string message)
        {
            HistoryError.Text = message;
            HistoryError.Visibility = Visibility.Visible;
        }


        private static bool TryParseStamp(string text, out DateTimeOffset utc)
        {
            if (DateTime.TryParseExact(text.Trim(), StampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local))
            {
                utc = new DateTimeOffset(local).ToUniversalTime();
                return true;
            }

            utc = default;
            return false;
        }
        //----------------------------------------------------------//



        // ================================ Failure and self-healing ================================ //

        /// <summary>
        /// Takes the selected node down, or brings it back if it is already down, then shows what the
        /// gateway says changed in the Self-healing card.
        /// </summary>
        private async void OnFailureClick(object sender, RoutedEventArgs e)
        {
            var node = SelectedNode();
            if (node is null || node.Id == GatewayId)
                return;

            FailureButton.IsEnabled = false;

            try
            {
                bool goDown = node.Status != MeshNodeStatus.Down;
                _lastFailure = await App.ApiClient.SetNodeDownAsync(node.Id, goDown);

                RenderHealing();
                await RefreshAsync();
            }
            catch (Exception)
            {
                StatusText.Text = "Can't reach the gateway - the node was not changed. Is SmartX.Api running?";
            }
            finally
            {
                RenderFailureButton();
            }
        }
        //----------------------------------------------------------//


        private MeshNodeDto? SelectedNode() =>
            _snapshot?.Nodes.FirstOrDefault(node => string.Equals(node.Id, _selectedId, StringComparison.OrdinalIgnoreCase));


        /// <summary>
        /// The button reads "Simulate node failure" for a live node and "Restore node" for a down one,
        /// and is disabled for the gateway, which is the root of every route.
        /// </summary>
        private void RenderFailureButton()
        {
            var node = SelectedNode();
            bool isDown = node?.Status == MeshNodeStatus.Down;

            FailureButton.IsEnabled = node is not null && node.Id != GatewayId;
            FailureLabel.Text = isDown ? "Restore node" : "Simulate node failure";
            FailureIcon.Text = isDown ? "" : "";
            FailureIcon.Foreground = Palette.Get(isDown ? "StatusGreen" : "StatusRed");
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The Self-healing card: what went down (or came back), links lost, nodes isolated, and the
        /// table of nodes whose route changed, with hops before and after.
        /// </summary>
        private void RenderHealing()
        {
            if (_lastFailure is not { } result)
                return;

            HealEmpty.Visibility = Visibility.Collapsed;
            HealSummary.Visibility = Visibility.Visible;
            HealBadge.Visibility = Visibility.Visible;

            string time = result.AtUtc.ToLocalTime().ToString("HH:mm:ss");
            HealText.Inlines.Clear();

            if (result.WentDown)
            {
                HealSummary.Background = Palette.Get("StatusRedSoft");
                HealSummary.BorderBrush = Palette.Get("StatusRedBorder");

                HealText.Inlines.Add(new Run($"{result.Name} went offline at {time}. ") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Get("StatusRedText") });
                HealText.Inlines.Add(new Run($"{result.LinksLost} link{(result.LinksLost == 1 ? "" : "s")} lost, {result.NodesIsolated} node{(result.NodesIsolated == 1 ? "" : "s")} isolated."));

                SetBadge(result.Healed ? "HEALED" : $"{result.NodesIsolated} ISOLATED", result.Healed ? "StatusGreen" : "StatusRedText", result.Healed ? "StatusGreenSoft" : "StatusRedSoft");
            }
            else
            {
                HealSummary.Background = Palette.Get("StatusGreenSoft");
                HealSummary.BorderBrush = Palette.Get("StatusGreenSoft");

                HealText.Inlines.Add(new Run($"{result.Name} came back online at {time}. ") { FontWeight = FontWeights.SemiBold, Foreground = Palette.Get("StatusGreen") });
                HealText.Inlines.Add(new Run($"{result.LinksRestored} link{(result.LinksRestored == 1 ? "" : "s")} restored."));

                SetBadge("RESTORED", "StatusGreen", "StatusGreenSoft");
            }

            HealListTitle.Visibility = result.Rerouted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            HealListTitle.Text = $"{result.Rerouted.Count} NODE{(result.Rerouted.Count == 1 ? "" : "S")} RE-ROUTED";

            HealList.ItemsSource = result.Rerouted
                .Select(node => new RerouteRow(node.Name, $"{HopsOrDash(node.HopsBefore)} → {HopsOrDash(node.HopsAfter)} hops"))
                .ToList();
        }
        //----------------------------------------------------------//


        private void SetBadge(string text, string foreground, string background)
        {
            HealBadgeText.Text = text;
            HealBadgeText.Foreground = Palette.Get(foreground);
            HealBadge.Background = Palette.Get(background);
        }


        private static string HopsOrDash(int? hops) => hops?.ToString() ?? "–";
        //----------------------------------------------------------//


        /// <summary>
        /// A one-line explanation under the Connectivity strip when the selected node's route changed
        /// because of the last failure: which node is down and which neighbour it now hops through.
        /// </summary>
        private void RenderHopOver(RouteDto? route)
        {
            var failed = _lastFailure is { WentDown: true } result && SelectedNodeIsDown(result.NodeId) ? result : null;

            if (failed is null || route is not { Reachable: true, Path.Count: > 1 } || failed.Rerouted.All(node => node.Name != route.Name))
            {
                HopOverNote.Visibility = Visibility.Collapsed;
                return;
            }

            HopOverText.Inlines.Clear();
            HopOverText.Inlines.Add(new Run("Hop-over") { FontWeight = FontWeights.SemiBold });
            HopOverText.Inlines.Add(new Run($" · {failed.Name} down, now via {route.Path[1].Name}"));
            HopOverNote.Visibility = Visibility.Visible;
        }


        // The note only makes sense while the node that failed is still down.
        private bool SelectedNodeIsDown(string nodeId) =>
            _snapshot?.Nodes.FirstOrDefault(node => node.Id == nodeId)?.Status == MeshNodeStatus.Down;
        //----------------------------------------------------------//


        private void AddLink(MeshNodeDto from, MeshNodeDto to, Brush stroke, double thickness, bool dashed = false, double opacity = 1)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = opacity,
                IsHitTestVisible = false
            };

            if (dashed)
                line.StrokeDashArray = [3, 2.5];

            MeshCanvas.Children.Add(line);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// A small label at the middle of a tree link showing what one packet costs across it.
        /// </summary>
        private void AddCostChip(MeshNodeDto from, MeshNodeDto to, double costMw)
        {
            var chip = new Border
            {
                Width = 34,
                Height = 18,
                CornerRadius = new CornerRadius(5),
                Background = Palette.Get("AccentBlueSoft"),
                BorderBrush = Palette.Get("SelectedRowBorder"),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = costMw.ToString("F1"),
                    FontFamily = (FontFamily)FindResource("MonoFont"),
                    FontSize = 11,
                    Foreground = Palette.Get("AccentBlue"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Canvas.SetLeft(chip, (from.X + to.X) / 2 - chip.Width / 2);
            Canvas.SetTop(chip, (from.Y + to.Y) / 2 - chip.Height / 2);
            MeshCanvas.Children.Add(chip);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// One node: a circle with its hop count inside, ringed green, amber or red for its status. The
        /// gateway is a dark rounded square, and a down node shows a cross instead of a count. A selected
        /// node gets a blue halo. Clicking any of them selects it.
        /// </summary>
        private void AddNode(MeshNodeDto node, bool selected)
        {
            const double Size = 54;
            bool isGateway = node.Id == GatewayId;
            bool isDown = node.Status == MeshNodeStatus.Down;

            var group = new Grid { Width = Size, Height = Size, Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = node.Id };
            group.MouseLeftButtonDown += OnMeshNodeClick;

            if (selected)
            {
                group.Children.Add(new Ellipse
                {
                    Fill = Palette.Get("AccentBlueSoft"),
                    Stroke = Palette.Get("AccentBlue"),
                    StrokeThickness = 1.5
                });
            }

            if (isGateway)
            {
                var square = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(9), Background = Palette.Get("ShellDark") };
                square.Child = new TextBlock
                {
                    Text = "",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 21,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                group.Children.Add(square);
            }
            else
            {
                var ring = node.Status switch
                {
                    MeshNodeStatus.Down => Palette.Get("StatusRed"),
                    MeshNodeStatus.Warning => Palette.Get("StatusAmberDot"),
                    _ => Palette.Get("StatusGreenDot")
                };

                var circle = new Ellipse
                {
                    Width = 38,
                    Height = 38,
                    Fill = Palette.Get(isDown ? "StatusRedSoft" : "CardBackground"),
                    Stroke = ring,
                    StrokeThickness = 3
                };

                if (isDown)
                    circle.StrokeDashArray = [1.4, 1.1];

                group.Children.Add(circle);
                group.Children.Add(new TextBlock
                {
                    Text = isDown ? "" : node.Hops?.ToString() ?? "-",
                    FontFamily = isDown ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") : new FontFamily("Segoe UI"),
                    FontSize = isDown ? 14 : 17,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Palette.Get(isDown ? "StatusRed" : "TextPrimary"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            Canvas.SetLeft(group, node.X - Size / 2);
            Canvas.SetTop(group, node.Y - Size / 2);
            MeshCanvas.Children.Add(group);

            AddNodeLabel(node, isGateway, isDown, selected);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The name beside a node, on the side that keeps clear of its links, with OFFLINE under the name
        /// of a down node.
        /// </summary>
        private void AddNodeLabel(MeshNodeDto node, bool isGateway, bool isDown, bool selected)
        {
            var side = isGateway ? LabelSide.Right : LabelSides.GetValueOrDefault(node.Id, LabelSide.Below);
            var align = side switch
            {
                LabelSide.Right => HorizontalAlignment.Left,
                LabelSide.Left => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center
            };

            var text = new StackPanel { IsHitTestVisible = false };

            text.Children.Add(new TextBlock
            {
                Text = isGateway ? $"Gateway {node.Id}" : node.Name,
                FontSize = 14.5,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Palette.Get(selected ? "AccentBlue" : "TextPrimary"),
                HorizontalAlignment = align
            });

            if (isDown)
            {
                text.Children.Add(new TextBlock
                {
                    Text = "OFFLINE",
                    FontFamily = (FontFamily)FindResource("MonoFont"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Palette.Get("StatusRed"),
                    HorizontalAlignment = align
                });
            }

            // A backing the colour of the canvas keeps the name readable where a link runs under it.
            var label = new Border { Background = LabelBackdrop, CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Child = text, IsHitTestVisible = false };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = label.DesiredSize;

            // Beside the node the first line sits level with its centre; above and below the label is centred on it.
            var (left, top) = side switch
            {
                LabelSide.Right => (node.X + 30, node.Y - 11),
                LabelSide.Left => (node.X - 30 - size.Width, node.Y - 11),
                LabelSide.Above => (node.X - size.Width / 2, node.Y - 28 - size.Height),
                _ => (node.X - size.Width / 2, node.Y + 26)
            };

            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            MeshCanvas.Children.Add(label);
        }
        //----------------------------------------------------------//


        private void OnMeshNodeClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string id })
                return;

            _selectedId = id;
            SelectRow(id);
            _ = RefreshAsync();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The links a route walks along, as pairs that read the same whichever way round they are written.
        /// </summary>
        private static HashSet<(string, string)> RouteKeys(RouteDto? route)
        {
            var keys = new HashSet<(string, string)>();

            if (route is not null)
            {
                for (int i = 0; i < route.Path.Count - 1; i++)
                    keys.Add(LinkKey(route.Path[i].Id, route.Path[i + 1].Id));
            }

            return keys;
        }


        private static (string, string) LinkKey(string a, string b) =>
            string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
                ? (a.ToUpperInvariant(), b.ToUpperInvariant())
                : (b.ToUpperInvariant(), a.ToUpperInvariant());
        //----------------------------------------------------------//



        // ================================ Selection and search ================================ //

        private void SelectRow(string? id)
        {
            _syncingList = true;

            try
            {
                NodeList.SelectedItem = id is null
                    ? null
                    : _rows.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _syncingList = false;
            }
        }
        //----------------------------------------------------------//


        private void OnNodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingList || NodeList.SelectedItem is not NodeRowViewModel row)
                return;

            _selectedId = row.Id;
            _ = RefreshAsync();
        }
        //----------------------------------------------------------//


        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderNodes();
        }
        //----------------------------------------------------------//


        private void OnClearFiltersClick(object sender, RoutedEventArgs e) => SearchBox.Clear();
        //----------------------------------------------------------//


        private void OnOpenInStreamClick(object sender, RoutedEventArgs e)
        {
            if (_selectedId is not null)
                _ = _navigator.NavigateAsync(Pillar.CommandStream, _selectedId);
        }
        //----------------------------------------------------------//
    }
    //----------------------------------------------------------//


    /// <summary>
    /// One row in the Mesh nodes list. Updated in place on every poll.
    /// </summary>
    public sealed class NodeRowViewModel(string id, string name, string detail) : ObservableObject
    {
        private bool _isDown;
        private string _hopsText = string.Empty;
        private Brush _statusBrush = Palette.Get("StatusGreenDot");
        private Brush _hopsBrush = Palette.Get("TextPrimary");

        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Detail { get; } = detail;

        public bool IsDown { get => _isDown; private set => Set(ref _isDown, value); }
        public string HopsText { get => _hopsText; private set => Set(ref _hopsText, value); }
        public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
        public Brush HopsBrush { get => _hopsBrush; private set => Set(ref _hopsBrush, value); }


        public void Update(MeshNodeDto node)
        {
            IsDown = node.Status == MeshNodeStatus.Down;

            StatusBrush = node.Status switch
            {
                MeshNodeStatus.Down => Palette.Get("StatusRed"),
                MeshNodeStatus.Warning => Palette.Get("StatusAmberDot"),
                _ => Palette.Get("StatusGreenDot")
            };

            if (IsDown)
            {
                HopsText = "offline";
                HopsBrush = Palette.Get("StatusRedText");
            }
            else if (node.Hops is null)
            {
                HopsText = "no route";
                HopsBrush = Palette.Get("StatusRedText");
            }
            else
            {
                HopsText = node.Hops == 1 ? "1 hop" : $"{node.Hops} hops";
                HopsBrush = Palette.Get("TextPrimary");
            }
        }
    }
    //----------------------------------------------------------//


    /// <summary>
    /// One line under Compare routes: a way of routing the node, whether it is the route in use or the
    /// cheapest, and its hops and power.
    /// </summary>
    public sealed record RouteOptionRow(string Label, string Method, bool InUse, bool Cheapest, string Hops, string Power, Brush PowerBrush);
    //----------------------------------------------------------//


    /// <summary>
    /// One line in the Connectivity visit order: its place in the search and the node's name.
    /// </summary>
    public sealed record VisitRow(string Number, string Name);
    //----------------------------------------------------------//


    /// <summary>
    /// One line in the Self-healing table: a node whose route changed, and its hop count before and after.
    /// </summary>
    public sealed record RerouteRow(string Name, string Change);
    //----------------------------------------------------------//


    /// <summary>
    /// One line in the history table: a reading's time and its formatted value.
    /// </summary>
    public sealed record HistoryRow(string Time, string Value);
    //----------------------------------------------------------//


    /// <summary>
    /// One stop on the Route to gateway card: a dot on a line, the node's name, and the cost of the link
    /// into it (the first stop, the node itself, says "selected" instead).
    /// </summary>
    public sealed record RouteStopRow(
        string Name,
        FontWeight NameWeight,
        string CostText,
        bool HasCost,
        bool IsOrigin,
        bool HasLineAbove,
        bool HasLineBelow,
        Brush MarkerFill,
        Brush MarkerStroke,
        string Tip);
}

//------------------------------------------- End Of File -------------------------------------------//
