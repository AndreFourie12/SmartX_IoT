using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SmartX.Client.Navigation;
using SmartX.Client.Views;

namespace SmartX.Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    ///
    /// The window is the navigation shell. Each pillar's view is created the first time it is opened
    /// and then kept in a dictionary for the rest of the session, so moving between modules is just
    /// swapping which cached view is on screen: the operator's filters, selections, scroll positions
    /// and history are all still there when they return. Views implement IModuleView so that only the
    /// visible one polls the API; the others pause.
    /// </summary>
    public partial class MainWindow : Window, INavigator
    {
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly Dictionary<Pillar, FrameworkElement> _views = new();

        private Pillar? _current;
        private bool _syncingTabs;


        /// <summary>
        /// Sets up the window, opens the ingestion pillar once it has loaded, and starts a three second
        /// timer that keeps the gateway status strip up to date.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            _statusTimer.Tick += async (_, _) => await RefreshGatewayStatusAsync();
            Loaded += async (_, _) =>
            {
                await NavigateAsync(Pillar.Ingestion);
                await RefreshGatewayStatusAsync();
                _statusTimer.Start();
            };
        }
        //----------------------------------------------------------//



        /// <summary>
        /// A pillar tab was clicked. Ignored while the code itself is moving the tab selection.
        /// </summary>
        private async void OnPillarChecked(object sender, RoutedEventArgs e)
        {
            if (_syncingTabs || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<Pillar>(tag, out var pillar))
                return;

            await NavigateAsync(pillar);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Shows a pillar, pausing the one being left and resuming the one arriving. Optionally asks the
        /// arriving view to focus a device, which is how one module links into another.
        /// </summary>
        public async Task NavigateAsync(Pillar pillar, string? focusDeviceId = null)
        {
            var view = ViewFor(pillar);

            if (focusDeviceId is not null && view is IDeviceFocus focusable)
                focusable.FocusDevice(focusDeviceId);

            SelectTab(pillar);

            if (_current == pillar)
            {
                if (focusDeviceId is not null && view is IModuleView same)
                    await same.OnActivatedAsync();

                return;
            }

            if (_current is { } previous && _views[previous] is IModuleView leaving)
                leaving.OnDeactivated();

            _current = pillar;
            MainContent.Content = view;
            PlayEntrance();

            if (view is IModuleView arriving)
                await arriving.OnActivatedAsync();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// The cached view for a pillar, created on first use.
        /// </summary>
        private FrameworkElement ViewFor(Pillar pillar)
        {
            if (!_views.TryGetValue(pillar, out var view))
            {
                view = pillar switch
                {
                    Pillar.Ingestion => new SensorIngestionView { Navigator = this },
                    Pillar.CommandStream => new CommandStreamView(this),
                    _ => new TopologyPlaceholderView()
                };

                _views[pillar] = view;
            }

            return view;
        }
        //----------------------------------------------------------//



        private void SelectTab(Pillar pillar)
        {
            _syncingTabs = true;

            try
            {
                TabFor(pillar).IsChecked = true;
            }
            finally
            {
                _syncingTabs = false;
            }
        }
        //----------------------------------------------------------//


        private RadioButton TabFor(Pillar pillar) => pillar switch
        {
            Pillar.Ingestion => IngestionTab,
            Pillar.CommandStream => CommandStreamTab,
            _ => TopologyTab
        };



        /// <summary>
        /// A short fade and lift as a pillar appears, so the switch reads as a transition rather than a jump.
        /// </summary>
        private void PlayEntrance()
        {
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            MainContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            ContentSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Ctrl+1 / Ctrl+2 / Ctrl+3 jump straight to a pillar. A locked (disabled) pillar's shortcut does nothing.
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                Pillar? target = e.Key switch
                {
                    Key.D1 or Key.NumPad1 => Pillar.Ingestion,
                    Key.D2 or Key.NumPad2 => Pillar.CommandStream,
                    Key.D3 or Key.NumPad3 => Pillar.Topology,
                    _ => null
                };

                if (target is { } pillar)
                {
                    e.Handled = true;

                    if (TabFor(pillar).IsEnabled)
                        _ = NavigateAsync(pillar);

                    return;
                }
            }

            base.OnPreviewKeyDown(e);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Asks the API for its current status and updates the connection dot and throughput text.
        /// Also lights a red dot on the Command Stream tab while critical alerts are active, so the
        /// operator hears about them whichever pillar they are on.
        /// Any failure is treated as the gateway being unreachable, which is exactly what this check is meant to detect.
        /// </summary>
        private async Task RefreshGatewayStatusAsync()
        {
            try
            {
                var status = await App.ApiClient.GetGatewayStatusAsync();
                if (status is null)
                    return;

                ConnectionDot.Fill = (Brush)FindResource("StatusGreenDot");
                ConnectionText.Text = "Gateway connected";
                ThroughputText.Text = $"{status.MessagesPerSecond:F1} msg/s · up {status.Uptime:hh\\:mm\\:ss}";

                CommandStreamBadge.Visibility = status.CriticalAlerts > 0 ? Visibility.Visible : Visibility.Collapsed;
                CommandStreamBadge.ToolTip = $"{status.CriticalAlerts} critical alert{(status.CriticalAlerts == 1 ? "" : "s")} active";
            }
            catch
            {
                ConnectionDot.Fill = (Brush)FindResource("StatusRed");
                ConnectionText.Text = "Gateway unreachable";
                ThroughputText.Text = "start SmartX.Api";
                CommandStreamBadge.Visibility = Visibility.Collapsed;
            }
        }
        //----------------------------------------------------------//
    }
}


//------------------------------------------- End Of File -------------------------------------------//
