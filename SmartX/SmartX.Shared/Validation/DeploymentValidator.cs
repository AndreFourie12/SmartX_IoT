using SmartX.Shared.Models;


namespace SmartX.Shared.Validation
{

    /// <summary>
    /// Goes throught the tree of a DeploymentNode to confirm if a device is safely configured
    /// within its facilit/zone/subzone hieracrhy 
    /// </summary>
    public static class DeploymentValidator
    {
        private const int RequiredDeviceDepth = 3;

        // Guards against cyclic input trees turning recursion into a stack overflow
        private const int MaxDepth = 16;



        /// <summary>
        /// Walks entire deployment tree from root down
        /// Collects every validation error it encounters 
        /// Returns a single pass/fail result plus the error list
        /// </summary>
        /// <returns></returns>
        public static DeploymentValidatorResult ValidateAll(IEnumerable<DeploymentNode> roots)
        {
            var errors = new List<string>();

            foreach (var root in roots)
            {
                ValidateRecursive(root, depth: 0, path: root.Name, errors);
            }

            return new DeploymentValidatorResult(errors.Count == 0, errors);
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Base cases added for the recursion
        /// Stops the recursion for infinitly calling itself and causing a stack overflow
        /// </summary>
        public static void ValidateRecursive(DeploymentNode node, int depth, string path, List<string> errors)
        {
            // Recursion gaurd, cant go deeper then 16
            if (depth > MaxDepth)
            {
                errors.Add($"{path}: exceeds maximum nesting depth of {MaxDepth}.");
                return;
            }

            // Checks if node is a leaf, stops descending if it is.
            if (node.NodeType == DeploymentNodeType.Device)
            {
                if (depth != RequiredDeviceDepth)
                    errors.Add($"{path}: device nodes must be at exactly 3 levels deep (Facility → Zone → SubZone → Device). {DescribeDepth(depth)}.");

                if (!DeviceIdentifier.IsValid(node.DeviceMacAddress))
                    errors.Add($"{path}: device node requires a valid MAC address or node identifier (e.g. 24:6F:28:AA:BB:CC or ESP32-ENV-04).");

                if (node.Children.Count > 0)
                    errors.Add($"{path}: device nodes cannot contain child nodes.");

                return;
            }

            // Organisational node (Facility, Zone, SubZone) must group at least one child
            if (node.Children.Count == 0)
            {
                errors.Add($"{path}: '{node.NodeType}' node must contain at least one child node.");
                return;
            }

            // Validate every child under this branch of the tree
            foreach (var child in node.Children)
            {
                ValidateRecursive(child, depth + 1, $"{path} -> {child.Name}", errors);
            }
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Turns a depth number into wording a person can read, such as at the root or directly under a zone.
        /// Used to make the device nesting error message clearer.
        /// </summary>
        private static string DescribeDepth(int depth) => depth switch
        {
            0 => "at the root",
            1 => "directly under a facility",
            2 => "directly under a zone",
            _ => $"{depth} levels deep"
        };
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//