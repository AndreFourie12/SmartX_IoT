using SmartX.Client.Views;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SmartX.Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SensorIngestionView? _ingestionView;

        public MainWindow()
        {
            InitializeComponent();
            ShowLandingPage();
        }

        private void OnIngestionNavClick(object sender, RoutedEventArgs e)
        {
            _ingestionView ??= new SensorIngestionView();
            MainContent.Content = _ingestionView;
        }

        private void ShowLandingPage()
        {
            MainContent.Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
            {
                new TextBlock
                {
                    Text = "Welcome to the Smart-X Gateway",
                    FontSize = 28,
                    FontWeight = FontWeights.Bold
                },
                new TextBlock
                {
                    Text = "Select \"Sensor Data Ingestion & Telemetry\" from the left to get started.",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 8, 0, 0)
                }
            }
            };
        }
    }
}


//------------------------------------------- End Of File -------------------------------------------//