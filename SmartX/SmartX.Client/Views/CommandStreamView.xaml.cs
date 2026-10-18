using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartX.Client.Navigation;
using SmartX.Client.Services;
using SmartX.Client.ViewModels;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;
using SmartX.Shared.Streaming;

namespace SmartX.Client.Views
{
    /// <summary>
    /// The Real-Time Command Stream &amp; History pillar.
    ///
    /// A one-second poll keeps it live: each tick fetches the whole dashboard (queues, filtered fleet,
    /// alerts, suggestions, command stack, new events, the selected device's timeline) in a single
    /// round trip, so a critical alert shows up within a second of the gateway processing it.
    ///
    /// All rows are view models updated in place, which keeps selection, scroll position and hover
    /// steady while values change underneath them. The view is cached by the navigation shell; while it
    /// is hidden it stops polling, and picks up again when shown.
    /// </summary>
    public partial class CommandStreamView : UserControl, IModuleView, IDeviceFocus
    {
        private readonly INavigator _navigator;

        private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
        private readonly DispatcherTimer _searchLogDelay = new() { Interval = TimeSpan.FromMilliseconds(1200) };
        private readonly DispatcherTimer _bannerTimer = new() { Interval = TimeSpan.FromSeconds(10) };

        private readonly ObservableCollection<DeviceRowViewModel> _devices = [];
        private readonly ObservableCollection<InsightViewModel> _insights = [];
        private readonly ObservableCollection<EventRow> _events = [];
        private readonly HashSet<long> _seenEvents = [];


        // Every device as of the last unfiltered snapshot - lets the alert simulator pick a target
        // even while the list on screen is filtered down.
        private readonly Dictionary<string, DeviceStateDto> _knownDevices = new(StringComparer.OrdinalIgnoreCase);

        private const int MaxEvents = 150;

        // The header's Live pill: a dark green or red wash behind the dot and label.
        private static readonly Brush LiveGreenTint = FrozenBrush(0x16, 0x3B, 0x2C);
        private static readonly Brush LiveRedTint = FrozenBrush(0x3A, 0x1F, 0x27);

        private string? _selectedId;
        private DeviceStateDto? _selectedDevice;
        private string? _bannerDeviceId;
        private int _minutes = 5;
        private bool _showLog;
        private bool _refreshing;
        private bool _refreshQueued;
        private bool _syncingList;
        private bool _active;
        private bool _gatewayDown;
        private bool _connected;

        // False until the first snapshot's events are in. That first batch is up to 40 old events,
        // so it must not pop the critical banner for an alert that happened before the page opened.
        private bool _eventsLoaded;
        private string _lastLoggedSearch = string.Empty;
        private string _commandsSignature = string.Empty;
        private string _patternsSignature = string.Empty;
        private string _predictedSignature = string.Empty;
        private TimelineDto? _timeline;


        public CommandStreamView(INavigator navigator)
        {
            InitializeComponent();

            _navigator = navigator;

            CategoryFilter.ItemsSource = Enum.GetValues<SensorCategory>()
                .Select(category => new FilterChoice<SensorCategory>(category.DisplayName(), category))
                .Prepend(new FilterChoice<SensorCategory>("All", null))
                .ToList();

            AlertFilter.ItemsSource = new FilterChoice<DeviceAlertState>[]
            {
                new("All", null),
                new("Critical", DeviceAlertState.Critical),
                new("Warning", DeviceAlertState.Warning),
                new("Offline", DeviceAlertState.Offline),
                new("Normal", DeviceAlertState.Normal),
                new("No data yet", DeviceAlertState.AwaitingData)
            };

            CategoryFilter.SelectedIndex = 0;
            AlertFilter.SelectedIndex = 0;
            DeviceList.ItemsSource = _devices;
            InsightList.ItemsSource = _insights;
            EventList.ItemsSource = _events;

            _pollTimer.Tick += async (_, _) => await RefreshAsync();

            _searchDebounce.Tick += async (_, _) =>
            {
                _searchDebounce.Stop();
                await RefreshAsync();
            };

            _searchLogDelay.Tick += async (_, _) =>
            {
                _searchLogDelay.Stop();
                await LogSearchAsync();
            };

            _bannerTimer.Tick += (_, _) =>
            {
                _bannerTimer.Stop();
                CriticalBanner.Visibility = Visibility.Collapsed;
            };
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
        /// Hidden: stop talking to the API. Everything on screen stays as it was for when the operator returns.
        /// </summary>
        public void OnDeactivated()
        {
            _active = false;
            _pollTimer.Stop();
            _searchDebounce.Stop();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Brings one device into view and selects it, clearing any filter that would hide it.
        /// </summary>
        public void FocusDevice(string deviceId)
        {
            bool visible = _devices.Any(row => string.Equals(row.Id, deviceId, StringComparison.OrdinalIgnoreCase));

            if (!visible)
                ClearFilters();

            _selectedId = deviceId;
            SelectRow(deviceId);

            if (_active)
                _ = RefreshAsync();
        }
        //----------------------------------------------------------//



        // ================================ Refresh ================================ //

        /// <summary>
        /// Fetches and renders one snapshot. If a refresh is already in flight, another is queued to run
        /// straight after it rather than overlapping, so a filter change made mid-request is never lost.
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

                    var snapshot = await App.ApiClient.GetStreamSnapshotAsync(
                        SearchBox.Text,
                        Selected<SensorCategory>(CategoryFilter),
                        Selected<DeviceAlertState>(AlertFilter),
                        _selectedId,
                        _minutes);

                    if (snapshot is not null)
                        Render(snapshot);

                    if (!_connected)
                    {
                        _connected = true;
                        LiveDot.Fill = Palette.Get("StatusGreenDot");
                        LiveText.Text = "Live";
                        LiveText.Foreground = LiveDot.Fill;
                        LivePill.Background = LiveGreenTint;
                        LivePill.ToolTip = "Refreshed from the gateway every second.";
                    }

                    if (_gatewayDown)
                    {
                        _gatewayDown = false;
                        ActionStatusText.Text = "Reconnected to the gateway.";
                    }
                }
                while (_refreshQueued && _active);
            }
            catch (Exception)
            {
                _connected = false;
                _gatewayDown = true;
                LiveDot.Fill = Palette.Get("StatusRed");
                LiveText.Text = "Gateway unreachable";
                LiveText.Foreground = Palette.Get("StatusRed");
                LivePill.Background = LiveRedTint;
                ActionStatusText.Text = "Can't reach the gateway - retrying every second. Is SmartX.Api running?";
            }
            finally
            {
                _refreshing = false;
            }
        }
        //----------------------------------------------------------//


        private void Render(StreamSnapshotDto snapshot)
        {
            RenderPipeline(snapshot.Pipeline, snapshot.Alerts);
            RenderFleet(snapshot);
            RenderInsights(snapshot);
            RenderCommands(snapshot.Commands);
            MergeEvents(snapshot.RecentEvents);
            RenderDetail(snapshot.SelectedTimeline);
        }
        //----------------------------------------------------------//


        private void RenderPipeline(PipelineStatsDto pipeline, AlertSummaryDto alerts)
        {
            RoutineDepthText.Text = pipeline.RoutineDepth.ToString("N0");
            RoutineWaitText.Text = $"avg wait {FormatMs(pipeline.RoutineAvgWaitMs)}";

            AlertDepthText.Text = pipeline.AlertDepth.ToString("N0");
            AlertWaitText.Text = $"avg wait {FormatMs(pipeline.AlertAvgWaitMs)}";
            AlertWaitText.ToolTip = pipeline.AlertProcessed == 0
                ? "No alerts yet"
                : $"The last alert jumped {pipeline.LastBypassCount:N0} queued routine packets";

            ThroughputValue.Text = $"{pipeline.ProcessedPerSecond:F1}/s";
            BudgetText.Text = $"of {pipeline.RoutineBudgetPerSecond}";
            ProcessedText.Text = $"{pipeline.RoutineProcessed + pipeline.AlertProcessed:N0} processed";

            DuplicatesText.Text = alerts.DuplicatesSuppressed.ToString("N0");
            FaultsDot.Fill = Palette.Get(alerts.ActiveFaults > 0 ? "StatusRed" : "TextOnDarkMuted");
            FaultsText.Text = $"{alerts.ActiveFaults} active fault{(alerts.ActiveFaults == 1 ? "" : "s")}";
            FaultsText.Foreground = Palette.Get(alerts.ActiveFaults > 0 ? "StatusRed" : "TextOnDarkMuted");
            OfflineText.Text = $"{alerts.DisconnectedNodes} offline";
        }
        //----------------------------------------------------------//


        private void RenderFleet(StreamSnapshotDto snapshot)
        {
            bool filtered = HasFilters();

            if (!filtered)
            {
                _knownDevices.Clear();
                foreach (var device in snapshot.Devices)
                    _knownDevices[device.Id] = device;
            }

            _syncingList = true;
            try
            {
                CollectionSync.Sync(
                    _devices,
                    snapshot.Devices,
                    device => device.Id,
                    row => row.Id,
                    device => new DeviceRowViewModel(device, snapshot.GeneratedAtUtc),
                    (row, device) => row.Update(device, snapshot.GeneratedAtUtc));

                if (_selectedId is null && _devices.Count > 0)
                    _selectedId = _devices[0].Id;

                SelectRow(_selectedId);
            }
            finally
            {
                _syncingList = false;
            }

            var selectedRow = _devices.FirstOrDefault(row => string.Equals(row.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
            if (selectedRow is not null)
                _selectedDevice = selectedRow.Dto;
            else if (_selectedId is not null && _knownDevices.TryGetValue(_selectedId, out var known))
                _selectedDevice = known;

            FleetCountText.Text = filtered
                ? $"· {snapshot.Devices.Count} of {snapshot.TotalDevices} match"
                : $"· {snapshot.TotalDevices} devices";
            FleetEmptyText.Visibility = snapshot.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearFiltersButton.IsEnabled = filtered;

            string predicted = string.Join("|", snapshot.PredictedSearches);
            if (predicted != _predictedSignature)
            {
                _predictedSignature = predicted;
                PredictedList.ItemsSource = snapshot.PredictedSearches;
            }

            PredictedPanel.Visibility = snapshot.PredictedSearches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        //----------------------------------------------------------//


        private void RenderInsights(StreamSnapshotDto snapshot)
        {
            CollectionSync.Sync(
                _insights,
                snapshot.Insights,
                insight => insight.Id,
                row => row.Id,
                insight => new InsightViewModel(insight),
                (row, insight) => row.Update(insight));

            InsightsEmpty.Visibility = _insights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InsightsCountText.Text = $"· top {snapshot.Insights.Count}";
            InsightsSubtitle.Text =
                $"From {snapshot.OperatorActionsLearned:N0} operator searches and commands, plus live trends · top {snapshot.Insights.Count} ranked by a min-heap";

            string patterns = string.Join("|", snapshot.Patterns.Select(p => $"{p.Condition}{p.Response}{p.Count}{p.Episodes}{p.LiveCount}"));
            if (patterns != _patternsSignature)
            {
                _patternsSignature = patterns;
                PatternList.ItemsSource = snapshot.Patterns.Select(PatternRow.From).ToList();
            }
        }
        //----------------------------------------------------------//


        private void RenderCommands(CommandHistoryDto history)
        {
            string signature = $"{history.UndoCount}|{string.Join(",", history.UndoStack.Select(c => c.Id))}";

            UndoButton.IsEnabled = history.UndoCount > 0;
            UndoButton.ToolTip = history.UndoStack.Count > 0 ? $"Undo: {history.UndoStack[0].Label}" : "Nothing to undo";

            if (signature == _commandsSignature)
                return;

            _commandsSignature = signature;
            UndoStackList.ItemsSource = history.UndoStack.Select((command, index) => CommandRow.From(command, index == 0)).ToList();
            UndoCountText.Text = $"({history.UndoCount})";
            UndoEmptyText.Visibility = history.UndoCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Adds events not seen before, newest at the top. Every snapshot carries the latest 40 events,
        /// so most of them are repeats; the set of seen ids makes the merge idempotent - each event
        /// appears exactly once. A new critical alert or outage also raises the banner, except on the
        /// first load, whose events all predate the page being opened.
        /// </summary>
        private void MergeEvents(IEnumerable<StreamEventDto> incoming)
        {
            foreach (var item in incoming.OrderBy(e => e.Id))
            {
                if (!_seenEvents.Add(item.Id))
                    continue;

                int index = 0;
                while (index < _events.Count && _events[index].Id > item.Id)
                    index++;

                _events.Insert(index, EventRow.From(item));

                bool critical = item.Severity == AlertSeverity.Critical
                    && item.Kind is StreamEventKind.AlertRaised or StreamEventKind.AlertEscalated or StreamEventKind.DeviceOffline;

                if (critical && _eventsLoaded)
                    ShowBanner(item);
            }

            _eventsLoaded = true;

            while (_events.Count > MaxEvents)
                _events.RemoveAt(_events.Count - 1);

            EventCountText.Text = $"· {_events.Count} events, newest first";
        }
        //----------------------------------------------------------//


        private void RenderDetail(TimelineDto? timeline)
        {
            var device = _selectedDevice;
            _timeline = timeline;

            OpenInIngestionButton.IsEnabled = device is not null;

            if (device is null)
            {
                DetailName.Text = "No device selected";
                DetailId.Text = string.Empty;
                DetailValue.Text = "—";
                DetailUnit.Text = string.Empty;
                DetailState.Text = "Pick a device from the fleet list.";
                StyleStatePill(null);
                DetailDot.Fill = Palette.Get("TextMuted");
                Chart.Timeline = null;
                RenderOverride(null);
                return;
            }

            DetailDot.Fill = Palette.ForAlertState(device.AlertState);
            DetailName.Text = device.DisplayName;
            DetailId.Text = $"{device.Id} · {device.Category.DisplayName()} · {device.Location}";
            DetailValue.Text = device.LastValue ?? "—";
            DetailUnit.Text = device.LastValue is null ? string.Empty : device.Unit;
            DetailState.Text = device.AlertReason
                ?? (device.AlertState == DeviceAlertState.AwaitingData ? "No telemetry received yet." : "Within its normal range.");
            StyleStatePill(device.AlertState);

            Chart.Timeline = timeline;

            if (timeline is not null)
            {
                DetailFooter.Text =
                    $"{timeline.Points.Count} readings in the last {_minutes} min · " +
                    "read in timestamp order from the gateway's SortedDictionary, so late packets land in the right place";
                DetailFooter.ToolTip = $"{timeline.TotalStored:N0} readings stored for this device";

                if (_showLog)
                    LogList.ItemsSource = BuildLog(timeline);
            }

            RenderOverride(device);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Colours the pill next to the big reading: red for critical, amber for warning, green when normal,
        /// grey when there is no device or no data yet.
        /// </summary>
        private void StyleStatePill(DeviceAlertState? state)
        {
            var (background, foreground, glyph) = state switch
            {
                DeviceAlertState.Critical => ("StatusRedSoft", "StatusRedText", ""),
                DeviceAlertState.Warning => ("StatusAmberSoft", "StatusAmber", ""),
                DeviceAlertState.Normal => ("StatusGreenSoft", "StatusGreen", ""),
                _ => ("NeutralSoft", "TextSecondary", "")
            };

            DetailStatePill.Visibility = Visibility.Visible;
            DetailStatePill.Background = Palette.Get(background);
            DetailState.Foreground = DetailStateIcon.Foreground = Palette.Get(foreground);
            DetailStateIcon.Text = glyph;
        }
        //----------------------------------------------------------//


        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
        //----------------------------------------------------------//


        private static List<object> BuildLog(TimelineDto timeline)
        {
            var rows = new List<object>(timeline.Points.Count);

            for (int i = timeline.Points.Count - 1; i >= 0; i--)
            {
                var point = timeline.Points[i];
                string delta = i == 0 ? "" : (point.Value - timeline.Points[i - 1].Value).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);

                rows.Add(new
                {
                    Time = point.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    Value = $"{point.Display} {timeline.Unit}".Trim(),
                    Delta = timeline.Category.IsActuator() ? "" : delta
                });
            }

            return rows;
        }
        //----------------------------------------------------------//


        private void RenderOverride(DeviceStateDto? device)
        {
            if (device is { IsActuator: true })
            {
                string reported = device.ReportedOn is { } on ? device.Category.StateLabel(on) : "unknown";
                OverrideTarget.Text = (device.AlertState, device.CommandPending, device.DesiredOn) switch
                {
                    (DeviceAlertState.Offline, _, _) =>
                        $"{device.DisplayName} is offline (last reported {reported}). A command will wait in its device shadow until it reconnects.",
                    (_, true, { } desired) =>
                        $"{device.DisplayName} is reporting {reported} · {device.Category.StateLabel(desired)} sent, waiting for the device to confirm.",
                    _ =>
                        $"{device.DisplayName} is {reported}. Commands go to its device shadow and the device confirms by reporting back."
                };

                TurnOnButton.Content = device.Category == SensorCategory.Valve ? "Open" : "Turn ON";
                TurnOffButton.Content = device.Category == SensorCategory.Valve ? "Close" : "Turn OFF";
                DeviceCommandPanel.IsEnabled = true;
            }
            else
            {
                OverrideTarget.Text = device is null
                    ? "Select a pump, valve or relay to command it."
                    : $"{device.DisplayName} is a sensor. Select a pump, valve or relay to command it, or use a group command.";
                DeviceCommandPanel.IsEnabled = false;
            }
        }
        //----------------------------------------------------------//



        // ================================ Critical banner ================================ //

        private void ShowBanner(StreamEventDto streamEvent)
        {
            BannerTitle.Text = $"CRITICAL · {streamEvent.Title}";
            BannerDetail.Text = streamEvent.Detail;
            _bannerDeviceId = streamEvent.DeviceId;

            CriticalBanner.Visibility = Visibility.Visible;
            _bannerTimer.Stop();
            _bannerTimer.Start();
        }
        //----------------------------------------------------------//



        // ================================ Search & filters ================================ //

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            // Filter quickly as the operator types; only teach the engine once they pause.
            _searchDebounce.Stop();
            _searchDebounce.Start();
            _searchLogDelay.Stop();
            _searchLogDelay.Start();
        }
        //----------------------------------------------------------//


        private async void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchLogDelay.Stop();
                await LogSearchAsync();

                if (_devices.Count > 0)
                {
                    _selectedId = _devices[0].Id;
                    SelectRow(_selectedId);
                    await RefreshAsync();
                }
            }
            else if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
            }
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Sends the settled search to the engine, which learns what operators look for and when.
        /// </summary>
        private async Task LogSearchAsync()
        {
            string query = SearchBox.Text.Trim();

            if (query.Length < 2 || string.Equals(query, _lastLoggedSearch, StringComparison.OrdinalIgnoreCase))
                return;

            _lastLoggedSearch = query;

            try
            {
                await App.ApiClient.LogSearchAsync(query);
            }
            catch
            {
                // Learning from a search is best-effort; the search itself already worked.
            }
        }
        //----------------------------------------------------------//


        private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Clear();


        private async void OnPredictedSearchClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not string name)
                return;

            SearchBox.Text = name;
            SearchBox.CaretIndex = name.Length;
            _searchDebounce.Stop();
            _searchLogDelay.Stop();
            await LogSearchAsync();
            await RefreshAsync();
        }
        //----------------------------------------------------------//


        private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            // Setting the initial "All" choice in the constructor also lands here, before the page is shown.
            if (_active)
                await RefreshAsync();
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The filter a dropdown is set to: one value, or none for its "All" choice.
        /// </summary>
        private static List<T> Selected<T>(ComboBox filter) where T : struct =>
            filter.SelectedItem is FilterChoice<T> { Value: { } value } ? [value] : [];
        //----------------------------------------------------------//


        private async void OnClearFiltersClick(object sender, RoutedEventArgs e)
        {
            ClearFilters();
            await RefreshAsync();
        }
        //----------------------------------------------------------//


        private void ClearFilters()
        {
            CategoryFilter.SelectedIndex = 0;
            AlertFilter.SelectedIndex = 0;

            SearchBox.Clear();
            _searchDebounce.Stop();
            _searchLogDelay.Stop();
        }
        //----------------------------------------------------------//


        private bool HasFilters() =>
            !string.IsNullOrWhiteSpace(SearchBox.Text)
            || CategoryFilter.SelectedIndex > 0
            || AlertFilter.SelectedIndex > 0;



        // ================================ Selection & detail ================================ //

        private async void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // A row vanishing because a filter hid it is not the operator choosing a different device.
            if (_syncingList || DeviceList.SelectedItem is not DeviceRowViewModel row)
                return;

            if (string.Equals(row.Id, _selectedId, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedId = row.Id;
            _selectedDevice = row.Dto;
            RenderDetail(null);
            await RefreshAsync();
        }
        //----------------------------------------------------------//


        private void SelectRow(string? deviceId)
        {
            var row = _devices.FirstOrDefault(r => string.Equals(r.Id, deviceId, StringComparison.OrdinalIgnoreCase));

            if (row is not null && !ReferenceEquals(DeviceList.SelectedItem, row))
            {
                DeviceList.SelectedItem = row;
                DeviceList.ScrollIntoView(row);
            }
        }
        //----------------------------------------------------------//


        private async void OnRangeChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int minutes))
            {
                _minutes = minutes;

                if (IsLoaded)
                    await RefreshAsync();
            }
        }
        //----------------------------------------------------------//


        private void OnViewModeChecked(object sender, RoutedEventArgs e)
        {
            if (Chart is null || LogList is null)
                return;

            _showLog = ReferenceEquals(sender, LogModeButton);
            Chart.Visibility = _showLog ? Visibility.Collapsed : Visibility.Visible;
            LogList.Visibility = _showLog ? Visibility.Visible : Visibility.Collapsed;

            if (_showLog && _timeline is not null)
                LogList.ItemsSource = BuildLog(_timeline);
        }
        //----------------------------------------------------------//


        private async void OnOpenInIngestionClick(object sender, RoutedEventArgs e)
        {
            if (_selectedId is not null)
                await _navigator.NavigateAsync(Pillar.Ingestion, _selectedId);
        }
        //----------------------------------------------------------//



        // ================================ Commands ================================ //

        private async void OnDeviceCommandClick(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice is not { IsActuator: true } device || sender is not Button { Tag: string tag })
                return;

            await SendCommandAsync(new OverrideCommandRequest(Enum.Parse<OverrideAction>(tag), CommandScope.Device, device.Id));
        }
        //----------------------------------------------------------//


        private async void OnGroupCommandClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag })
                return;

            var parts = tag.Split('|');
            await SendCommandAsync(new OverrideCommandRequest(Enum.Parse<OverrideAction>(parts[1]), Enum.Parse<CommandScope>(parts[0])));
        }
        //----------------------------------------------------------//


        private async void OnUndoClick(object sender, RoutedEventArgs e) =>
            await RunCommandCallAsync(() => App.ApiClient.UndoAsync());

        private Task SendCommandAsync(OverrideCommandRequest request) =>
            RunCommandCallAsync(() => App.ApiClient.IssueCommandAsync(request));


        /// <summary>
        /// Runs one command call, reports the gateway's answer, and refreshes. A refused command (nothing
        /// to change, nothing to undo) shows the reason instead of failing silently.
        /// </summary>
        private async Task RunCommandCallAsync(Func<Task<CommandResultDto?>> call)
        {
            try
            {
                var result = await call();
                if (result is null)
                    return;

                ActionStatusText.Text = result.Applied ? result.Message : $"Not applied: {result.Message}";
                RenderCommands(result.History);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Command failed: {ex.Message}";
            }
        }
        //----------------------------------------------------------//



        // ================================ Suggested actions ================================ //

        private async void OnRunInsightClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InsightViewModel { Command: { } command })
                return;

            await SendCommandAsync(new OverrideCommandRequest(command.Action, command.Scope, command.DeviceId, CommandOrigin.Suggestion));
        }
        //----------------------------------------------------------//


        private void OnFocusInsightClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is InsightViewModel { DeviceId: { } deviceId })
                FocusDevice(deviceId);
        }
        //----------------------------------------------------------//



        // ================================ Test controls ================================ //

        private void OnInjectAlertClick(object sender, RoutedEventArgs e)
        {
            var menu = InjectAlertButton.ContextMenu;
            menu.PlacementTarget = InjectAlertButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Posts one out-of-range reading to the telemetry endpoint, exactly as a misbehaving device would.
        /// The receipt confirms which lane it was routed into; the live stream then shows it being processed.
        /// A healthy device is preferred as the target, so the alert is a fresh one rather than a duplicate
        /// the HashSet would (correctly) swallow.
        /// </summary>
        private async void OnInjectMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string kind })
                return;

            var (category, fallbackId) = kind switch
            {
                "moisture" => (SensorCategory.SoilMoisture, "ESP32-SOIL-01"),
                "heat" => (SensorCategory.Temperature, "ESP32-TMP-01"),
                _ => (SensorCategory.PowerConsumption, "ESP32-PWR-02")
            };

            var candidates = _knownDevices.Values.Where(device => device.Category == category).ToList();
            var healthy = candidates.Where(device => device.AlertState == DeviceAlertState.Normal).ToList();
            var pool = healthy.Count > 0 ? healthy : candidates;
            var target = pool.Count > 0 ? pool[Random.Shared.Next(pool.Count)] : null;

            string deviceId = target?.Id ?? fallbackId;
            string name = target?.DisplayName ?? fallbackId;

            var request = category switch
            {
                SensorCategory.SoilMoisture => new TelemetryIngestRequest(TelemetryDataType.Float, 8f + Random.Shared.Next(0, 70) / 10f, null, null, "%"),
                SensorCategory.Temperature => new TelemetryIngestRequest(TelemetryDataType.Float, 41f + Random.Shared.Next(0, 30) / 10f, null, null, "°C"),
                _ => new TelemetryIngestRequest(TelemetryDataType.Int, null, 650 + Random.Shared.Next(0, 300), null, "W")
            };

            try
            {
                var receipt = await App.ApiClient.IngestTelemetryAsync(deviceId, request);
                if (receipt is null)
                    return;

                string value = request.IntValue?.ToString() ?? request.FloatValue?.ToString("0.0", CultureInfo.InvariantCulture) ?? "?";
                ActionStatusText.Text =
                    $"Sent {value} {request.Unit} as {name} → {receipt.Lane.ToString().ToUpperInvariant()} lane (packet #{receipt.Sequence}, {receipt.RoutineQueueDepth:N0} routine packets were waiting).";
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Could not inject the alert: {ex.Message}";
            }
        }
        //----------------------------------------------------------//


        private async void OnStormClick(object sender, RoutedEventArgs e)
        {
            try
            {
                StormButton.IsEnabled = false;

                ActionStatusText.Text = await App.ApiClient.TriggerStormAsync()
                    ? "Reconnect storm requested - watch the routine queue fill and the hidden spike jump straight past it."
                    : "A storm is already running.";
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Could not start the storm: {ex.Message}";
            }
            finally
            {
                // Let the queue drain before another storm can be started.
                await Task.Delay(TimeSpan.FromSeconds(8));
                StormButton.IsEnabled = true;
            }
        }
        //----------------------------------------------------------//


        private void OnBannerFocusClick(object sender, RoutedEventArgs e)
        {
            if (_bannerDeviceId is not null)
                FocusDevice(_bannerDeviceId);
        }
        //----------------------------------------------------------//


        private void OnBannerCloseClick(object sender, RoutedEventArgs e)
        {
            _bannerTimer.Stop();
            CriticalBanner.Visibility = Visibility.Collapsed;
        }
        //----------------------------------------------------------//


        private static string FormatMs(double ms) => ms switch
        {
            <= 0 => "0 ms",
            < 1 => $"{ms:F2} ms",
            < 1000 => $"{ms:F0} ms",
            _ => $"{ms / 1000:F1} s"
        };
    }
}

//------------------------------------------- End Of File -------------------------------------------//
