using Microsoft.Win32;
using SmartX.Shared.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Dialogs;
using SmartX.Shared.Models;
using SmartX.Shared.Telemetry;
using SmartX.Shared.Validation;
namespace SmartX.Client.Views
{
    /// <summary>
    /// Interaction logic for SensorIngestionView.xaml
    /// </summary>
    public partial class SensorIngestionView : UserControl
    {
        private readonly ObservableCollection<SensorDto> _rows = [];
        private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        private List<SensorDto> _allSensors = [];
        private List<DeploymentNode> _treeRoots = [];

        /// <summary>
        /// Path prefix the tree selection is filtering by; null means "show all".
        /// </summary>
        private string? _scopePath;

        /// <summary>
        /// Set only when a device leaf is picked, to narrow the table to that one sensor.
        /// Kept separate from <see cref="_selectedSensorId"/> so that selecting a device and then
        /// clicking back up to a zone widens the table again instead of staying on one row.
        /// </summary>
        private string? _deviceScopeId;


        /// <summary>
        /// Which row is highlighted, independent of what the table is filtered to
        /// .</summary>
        private string? _selectedSensorId;

        /// <summary>
        /// 
        /// </summary>
        public SensorIngestionView()
        {
            InitializeComponent();
            SensorsGrid.ItemsSource = _rows;

            _liveTimer.Tick += async (_, _) => await RefreshEngagementAsync();
            Loaded += OnLoadedAsync;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        public async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            await RefreshSensorsAsync();
            await RefreshEngagementAsync();
            _liveTimer.Start();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// 
        /// </summary>
        private async void OnRefreshClick(object sender, RoutedEventArgs e) 
            => await RefreshSensorsAsync();
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async Task RefreshSensorsAsync()
        {
            try
            {
                _allSensors = await App.ApiClient.GetSensorAsync();
                BuildTree();
                ApplyScope();
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Could not reach the gateway: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



        private void BuildTree()
        {
            _treeRoots = [];

            foreach (var sensor in _allSensors.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                var parts = sensor.Location.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length == 0)
                    continue;

                var level = _treeRoots;
                DeploymentNode? parent = null;

                for (int depth = 0; depth < parts.Length; depth++)
                {
                    var existing = level.FirstOrDefault(node => node.Name == parts[depth]);

                    if (existing is null)
                    {
                        existing = new DeploymentNode
                        {
                            Name = parts[depth],
                            NodeType = NodeTypeForDepth(depth)
                        };

                        level.Add(existing);

                    }

                    parent = existing;
                    level = existing.Children;
                }

                parent!.Children.Add(new DeploymentNode
                {
                    Name = sensor.Id,
                    NodeType = DeploymentNodeType.Device,
                    DeviceMacAddress = sensor.Id,
                    DeviceCount = 1
                });

            }

            foreach (var root in _treeRoots)
                CountDevices(root);

            HierarchyTree.ItemsSource = null;
            HierarchyTree.ItemsSource = _treeRoots;
        }



        /// <summary>
        /// 
        /// </summary>
        private static int CountDevices(DeploymentNode node)
        {
            if (node.NodeType == DeploymentNodeType.Device)
                return node.DeviceCount = 1;

            int total = 0;
            foreach (var child in node.Children)
                total += CountDevices(child);

            return node.DeviceCount = total;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private static DeploymentNodeType NodeTypeForDepth(int depth) => depth switch
        {
            0 => DeploymentNodeType.Facility,
            1 => DeploymentNodeType.Zone,
            _ => DeploymentNodeType.SubZone
        };
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void OnHierarchySelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not DeploymentNode node)
                return;

            if(node.NodeType == DeploymentNodeType.Device)
            {
                _deviceScopeId = node.Name;
                _scopePath = _allSensors.FirstOrDefault(s => s.Id == node.Name)?.Location;
                _selectedSensorId = node.Name;
            }
            else
            {
                _deviceScopeId = null;
                _scopePath = PathTo(node);
            }

            ApplyScope();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private string? PathTo(DeploymentNode target)
        {
            foreach (var root in _treeRoots)
            {
                var path = FindPath(root, target, root.Name);
                if (path is not null)
                    return path;
            }

            return null;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private static string? FindPath(DeploymentNode node, DeploymentNode target, string pathSoFar)
        {
            if (ReferenceEquals(node, target))
                return pathSoFar;

            foreach (var child in node.Children)
            {
                var found = FindPath(child, target, $"{pathSoFar} / {child.Name}");

                if (found is not null)
                    return found;
            }

            return null;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void OnShowAllClick(object sender, RoutedEventArgs e)
        {
            _scopePath = null;
            _deviceScopeId = null;
            ApplyScope();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void ApplyScope()
        {
            IEnumerable<SensorDto> visible = _allSensors;

            if(_scopePath is not null)
                visible = visible.Where(s => s.Location.StartsWith(_scopePath, StringComparison.OrdinalIgnoreCase));

            if (_deviceScopeId is not null)
                visible = visible.Where(s => s.Id == _deviceScopeId);

            var ordered = visible.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();

            _rows.Clear();

            foreach (var sensor in ordered)
                _rows.Add(sensor);

            string scopeLabel = _deviceScopeId is not null
            ? $"{_scopePath} / {_deviceScopeId}"
            : _scopePath ?? "All facilities";

            ScopeText.Text = $"· {scopeLabel} · {ordered.Count} " + (ordered.Count == 1 ? "device" : "devices");

            ShowAllButton.IsEnabled = _scopePath is not null;

            if (_selectedSensorId is not null)
                RestoreRowSelection();

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void SelectSensorRow(string sensorId)
        {
            _selectedSensorId = sensorId;
            RestoreRowSelection();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private void RestoreRowSelection()
        {
            var match = _rows.FirstOrDefault(row => row.Id == _selectedSensorId);
           
            if (match is not null && !ReferenceEquals(SensorsGrid.SelectedItem, match))
            {
                SensorsGrid.SelectedItem = match;
                SensorsGrid.ScrollIntoView(match);
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async void OnRegisterSensorClick()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async void OnSensorsSelectionChanged()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task RefreshTelemetryAndAttachmentAsync()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async void OnAttachFileClick()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async void OnSimulateBatchClick()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task LoadDeploymentSampleAsync()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        private async void OnValidateHierarchyClick()
        {

        }
        //----------------------------------------------------------//



        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task RefreshLiveWidgetsAsync()
        {

        }
        //----------------------------------------------------------//

        private async Task RefreshEngagementAsync()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task RefreshMeterAggregationAsync()
        {

        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//
