namespace SmartX.Client.Navigation
{
    /// <summary>
    /// The gateway's three architectural pillars, one per tab in the top navigation.
    /// </summary>
    public enum Pillar
    {
        Ingestion,
        CommandStream,
        Topology
    }
    //----------------------------------------------------------//


    /// <summary>
    /// A view that lives for the whole session and is shown and hidden by the navigation shell.
    ///
    /// Views are created once and cached, so everything on them - search text, filters, the selected
    /// device, scroll positions, the event feed - is still there when the operator comes back.
    /// Activation is where a view resumes its polling and refreshes; deactivation pauses it, so a
    /// hidden view never keeps hitting the API in the background.
    /// </summary>
    public interface IModuleView
    {
        Task OnActivatedAsync();

        void OnDeactivated();
    }
    //----------------------------------------------------------//


    /// <summary>
    /// A view that can be asked to bring one device into focus, so modules can link to each other
    /// ("open this device in the ingestion view").
    /// </summary>
    public interface IDeviceFocus
    {
        void FocusDevice(string deviceId);
    }
    //----------------------------------------------------------//


    /// <summary>
    /// Lets a view switch the active pillar without knowing about the window that hosts it.
    /// </summary>
    public interface INavigator
    {
        Task NavigateAsync(Pillar pillar, string? focusDeviceId = null);
    }
    //----------------------------------------------------------//
}

//------------------------------------------- End Of File -------------------------------------------//
