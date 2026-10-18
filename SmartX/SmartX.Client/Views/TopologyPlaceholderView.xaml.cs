using System.Windows.Controls;

namespace SmartX.Client.Views
{
    /// <summary>
    /// Stand-in for the Network Topology pillar until the final PoE. Reachable from the navigation so the
    /// shell's routing covers all three pillars; it holds no state and polls nothing.
    /// </summary>
    public partial class TopologyPlaceholderView : UserControl
    {
        public TopologyPlaceholderView()
        {
            InitializeComponent();
        }
    }
}

//------------------------------------------- End Of File -------------------------------------------//
