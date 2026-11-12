using SmartX.Shared.Models;

namespace SmartX.Api.Topology
{
    /// <summary>
    /// One ESP32 node in the mesh. X and Y are its position on the page's 900 x 540 drawing area.
    /// </summary>
    public sealed record MeshNode(string Id, string Name, string Mac, double X, double Y);
    //----------------------------------------------------------//


    /// <summary>
    /// A wireless link between two nodes. CostMw is the power one packet costs to send across it, in
    /// milliwatts. In an adjacency list From is the node whose list holds the link and To is the neighbour.
    /// </summary>
    public sealed record MeshLink(string From, string To, double CostMw);
    //----------------------------------------------------------//


    /// <summary>
    /// The ESP32 mesh as an undirected weighted graph, stored as adjacency lists.
    ///
    /// Adjacency lists rather than a matrix: each node only hears a few neighbours, so a list per node
    /// holds just the links that exist, and walking a node's neighbours costs its degree, not the size
    /// of the whole mesh. Every link is stored at both ends, so it can be followed in either direction.
    /// </summary>
    public sealed class MeshGraph
    {
        private readonly Dictionary<string, MeshNode> _nodes = new(DeviceIdentifier.Comparer);
        private readonly Dictionary<string, string> _idByMac = new(DeviceIdentifier.Comparer);
        private readonly Dictionary<string, List<MeshLink>> _adjacency = new(DeviceIdentifier.Comparer);
        private readonly List<MeshLink> _links = [];


        public IReadOnlyCollection<MeshNode> Nodes => _nodes.Values;


        /// <summary>
        /// Each link once, as it was added (not once per end).
        /// </summary>
        public IReadOnlyList<MeshLink> Links => _links;
        //----------------------------------------------------------//


        public void AddNode(MeshNode node)
        {
            _nodes[node.Id] = node;
            _idByMac[node.Mac] = node.Id;
            _adjacency[node.Id] = [];
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Adds a link between two existing nodes, filed in both nodes' adjacency lists.
        /// Bad input is refused before anything is stored: an unknown node, a negative (or not-a-number)
        /// cost, or a pair of nodes that are already linked. A negative cost would break Prim and
        /// Dijkstra, and a duplicate would put the same link in a list twice.
        /// </summary>
        public void AddLink(string a, string b, double costMw)
        {
            if (!_adjacency.ContainsKey(a) || !_adjacency.ContainsKey(b))
                throw new ArgumentException($"Both ends of a link must be existing nodes ('{a}' to '{b}').");

            if (double.IsNaN(costMw) || costMw < 0)
                throw new ArgumentOutOfRangeException(nameof(costMw), costMw, "A link's cost cannot be negative.");

            if (IsLinked(a, b))
                throw new InvalidOperationException($"'{a}' and '{b}' are already linked.");

            _links.Add(new MeshLink(a, b, costMw));
            _adjacency[a].Add(new MeshLink(a, b, costMw));
            _adjacency[b].Add(new MeshLink(b, a, costMw));
        }
        //----------------------------------------------------------//


        // Looks through one node's own list, so the check costs its degree, not the size of the mesh.
        private bool IsLinked(string a, string b)
        {
            foreach (var link in _adjacency[a])
            {
                if (string.Equals(link.To, b, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// The links leaving one node. To is always the neighbour.
        /// </summary>
        public IReadOnlyList<MeshLink> LinksOf(string id) => _adjacency[id];
        //----------------------------------------------------------//


        /// <summary>
        /// Finds a node from its id or its MAC address, in any spelling.
        /// </summary>
        public bool TryGetNode(string idOrMac, out MeshNode node)
        {
            string key = DeviceIdentifier.Normalize(idOrMac);

            if (_nodes.TryGetValue(key, out node!))
                return true;

            if (_idByMac.TryGetValue(key, out var id))
            {
                node = _nodes[id];
                return true;
            }

            node = null!;
            return false;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
