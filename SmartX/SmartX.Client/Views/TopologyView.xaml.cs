using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        private readonly INavigator _navigator;
        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly ObservableCollection<NodeRowViewModel> _rows = [];
        private readonly string _subtitle;

        private TopologySnapshotDto? _snapshot;
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
                RouteName.Text = "--";
                RouteDetail.Text = string.Empty;
                RouteList.ItemsSource = null;
                RouteOptionList.ItemsSource = null;
                RouteOptionsTitle.Visibility = Visibility.Collapsed;
                RouteTotal.Text = "--";
                RouteNote.Text = "Pick a node to see how its packets reach the gateway.";
                return;
            }

            RouteName.Text = route.Name;
            RouteDetail.Text = $"{route.NodeId}  ·  {route.Mac}";

            if (!route.Reachable)
            {
                bool offline = _snapshot?.Nodes.FirstOrDefault(node => node.Id == route.NodeId)?.Status == MeshNodeStatus.Down;

                RouteList.ItemsSource = null;
                RouteOptionList.ItemsSource = null;
                RouteOptionsTitle.Visibility = Visibility.Collapsed;
                RouteTotalLabel.Text = "Total";
                RouteTotal.Text = "no route";
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

                stops.Add(new RouteStopRow(
                    hop.Name,
                    isGateway ? $"{hop.Id}  ·  root" : hop.Id,
                    $"+{hop.CostMw:F1} mW",
                    i > 0,
                    i > 0,
                    i < route.Path.Count - 1,
                    isGateway ? Palette.Get("ShellDark") : Palette.Get("CardBackground")));
            }

            RouteList.ItemsSource = stops;
            RouteTotalLabel.Text = "Total (MST path)";
            RouteTotal.Text = $"{Hops(route.TotalHops)}  ·  {route.TotalCostMw:F1} mW";

            RenderRouteOptions(route);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Lists the node's route three ways side by side: the spanning tree route shown above, the
        /// breadth-first route (fewest hops) and Dijkstra's route (least power). The cheapest in power is
        /// shown in green. When the tree route is not that cheapest one, a line says why: the tree
        /// minimises the power of the whole mesh, not of each node's own path.
        /// </summary>
        private void RenderRouteOptions(RouteDto route)
        {
            bool single = route.Path.Count <= 1;
            RouteOptionsTitle.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

            if (single)
            {
                RouteOptionList.ItemsSource = null;
                RouteNote.Text = string.Empty;
                return;
            }

            double best = Math.Min(route.TotalCostMw, Math.Min(route.FewestHopCostMw, route.LeastPowerCostMw));

            RouteOptionRow Row(string label, int hops, double cost) =>
                new(label, $"{Hops(hops)}  ·  {cost:F1} mW", Palette.Get(cost - best < 0.05 ? "StatusGreen" : "TextPrimary"));

            RouteOptionList.ItemsSource = new List<RouteOptionRow>
            {
                Row("MST route (above)", route.TotalHops, route.TotalCostMw),
                Row("Fewest hops (BFS)", route.FewestHopPath.Count - 1, route.FewestHopCostMw),
                Row("Least power (Dijkstra)", route.LeastPowerPath.Count - 1, route.LeastPowerCostMw)
            };

            RouteNote.Text = route.TotalCostMw - route.LeastPowerCostMw < 0.05
                ? string.Empty
                : $"The spanning tree minimises the power of the whole mesh, not of each node's own path, so {route.Name}'s cheapest route costs {route.TotalCostMw - route.LeastPowerCostMw:F1} mW less than its tree route.";
        }
        //----------------------------------------------------------//


        private static string Hops(int count) => count == 1 ? "1 hop" : $"{count} hops";
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

            foreach (var link in treeLinks)
                AddCostChip(nodes[link.From], nodes[link.To], link.CostMw);

            foreach (var link in _snapshot.Links.Where(link => link.State == MeshLinkState.Down))
                AddLink(nodes[link.From], nodes[link.To], Palette.Get("StatusRed"), 1.6, dashed: true);

            var down = _snapshot.Nodes.Count(node => node.Status == MeshNodeStatus.Down);
            MeshSubtitle.Text = down == 0
                ? $"{_snapshot.Nodes.Count} nodes · {_snapshot.LinksTotal} links"
                : $"{_snapshot.Nodes.Count} nodes · {_snapshot.LinksTotal} links · {_snapshot.LinksTotal - _snapshot.LinksActive} lost to the offline node{(down == 1 ? "" : "s")}";
        }
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
                Width = 28,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                Background = Palette.Get("CardBackground"),
                BorderBrush = Palette.Get("SelectedRowBorder"),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = costMw.ToString("F1"),
                    FontSize = 9.5,
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
    /// One line under Route options: a way of routing the node, and its hops and power.
    /// </summary>
    public sealed record RouteOptionRow(string Label, string Detail, Brush DetailBrush);
    //----------------------------------------------------------//


    /// <summary>
    /// One stop on the Route to gateway card: a dot on a line, the node's name, and the cost of the link into it.
    /// </summary>
    public sealed record RouteStopRow(
        string Name,
        string Detail,
        string CostText,
        bool HasCost,
        bool HasLineAbove,
        bool HasLineBelow,
        Brush MarkerFill);
}

//------------------------------------------- End Of File -------------------------------------------//
