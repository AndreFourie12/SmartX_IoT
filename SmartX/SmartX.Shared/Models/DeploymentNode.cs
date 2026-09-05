using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.Shared.Models
{
    public class DeploymentNode
    {
        public string Name { get; set; } = string.Empty;

        public List<DeploymentNode> Children { get; set; } = new();
    }
}
