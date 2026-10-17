using System.Windows;
using SmartX.Shared.Contracts;
using SmartX.Shared.Models;

namespace SmartX.Client.Dialogs
{
    /// <summary>
    /// Interaction logic for AddSensorDialog.xaml
    /// </summary>
    public partial class AddSensorDialog : Window
    {
        public SensorDto? RegisteredSensor { get; private set; }



        /// <summary>
        /// Builds the dialog and pre-selects the identifier text so the user can type straight over it.
        /// The category list comes straight from the SensorCategory enum, so a category added to the
        /// shared model can never be missing here.
        /// </summary>
        public AddSensorDialog()
        {
            InitializeComponent();

            CategoryCombo.ItemsSource = Enum.GetValues<SensorCategory>()
                .Select(category => new { Value = category, Label = category.DisplayName() })
                .ToList();
            CategoryCombo.SelectedIndex = 0;

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

            // Same rule the API applies (SmartX.Shared.Models.DeviceIdentifier), checked here first so a
            // typo is caught before any network call.
            if (!DeviceIdentifier.IsValid(identifier))
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

            string location = string.Join(" / ", segments);

            var category = (SensorCategory)CategoryCombo.SelectedValue;
            string? displayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? null : DisplayNameBox.Text.Trim();

            try
            {
                RegisterButton.IsEnabled = false;

                RegisteredSensor = await App.ApiClient.RegisterSensorAsync(
                    new SensorRegistrationRequest(identifier, location, category, displayName));

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
