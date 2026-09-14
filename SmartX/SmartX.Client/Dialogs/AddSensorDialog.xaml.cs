using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Client.Dialogs
{
    /// <summary>
    /// Interaction logic for AddSensorDialog.xaml
    /// </summary>
    public partial class AddSensorDialog : Window
    {
        private static readonly Regex MacAddressPattern =
            new("^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

        private static readonly Regex NodeIdentifierPattern =
            new("^[A-Za-z0-9][A-Za-z0-9_-]{2,31}$", RegexOptions.Compiled);

        public SensorDto? RegisteredSensor { get; private set; }



        /// <summary>
        /// Builds the dialog and pre-selects the identifier text so the user can type straight over it.
        /// </summary>
        public AddSensorDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => IdentifierBox.SelectAll();
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Validates the identifier and location fields, then joins facility, zone and sub-zone into a single location path.
        /// Posts the new sensor to the API and closes the dialog on success, or shows the reason inline if it fails.
        /// </summary>
        private async void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            string identifier = IdentifierBox.Text.Trim();

            if(!MacAddressPattern.IsMatch(identifier) && !NodeIdentifierPattern.IsMatch(identifier))
            {
                ShowError("Enter a MAC address (24:6F:28:AA:BB:CC) or a node identifier (ESP32-ENV-04).");
                return;
            }

            if (string.IsNullOrWhiteSpace(FacilityBox.Text) || string.IsNullOrWhiteSpace(ZoneBox.Text))
            {
                ShowError("Facility and Zone are required.");
                return;
            }

            var segments = new[] { FacilityBox.Text.Trim(), ZoneBox.Text.Trim(), SubZoneBox.Text.Trim() }
                .Where(segment => !string.IsNullOrWhiteSpace(segment));

            string location = string.Join("/", segments);

            var category = Enum.Parse<SensorCategory>(((ComboBoxItem)CategoryCombo.SelectedItem).Content.ToString()!);

            try
            {
                RegisterButton.IsEnabled = false;

                RegisteredSensor = await App.ApiClient.RegisterSensorAsync(
                    new SensorRegistrationRequest(identifier, location, category));

                DialogResult = true;
            }
            catch (Exception ex)
            {
                RegisterButton.IsEnabled = true;
                ShowError(ex.Message);
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Puts a message into the dialog's error label and makes it visible.
        /// </summary>
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Closes the dialog without registering anything.
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//