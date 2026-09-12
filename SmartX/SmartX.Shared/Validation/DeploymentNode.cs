using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Validation
{
    /// <summary>
    /// Enum that stores the 3 levels of the tree
    /// </summary>
    
    public enum DeploymentNodeType
    {
        Facility,
        Zone,
        SubZone,
        Device
    }
    //----------------------------------------------------------//



    /// <summary>
    /// One node in a multi-tier deployment tree, e.g Facility A -> Zone 1 -> Sub-Zone B 
    /// Done for organisational grouping, makes things easier to find
    /// </summary>
  
    public sealed class DeploymentNode
    {
        public required string Name { get; init; }
        public required DeploymentNodeType NodeType { get; init; }
        public string? DeviceMacAddress { get; init; }
        public int DeviceCount { get; set; }
        public List<DeploymentNode> Children { get; init; } = new();
    }
    //----------------------------------------------------------//

}

//------------------------------------------- End Of File -------------------------------------------//
