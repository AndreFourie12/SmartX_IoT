using System.Collections;

namespace SmartX.Shared.Collections
{
    /// <summary>
    /// Self-balancing binary search tree (AVL), written by hand for the telemetry timeline.
    ///
    /// Every node remembers its height. After each insert or delete the heights on the path back to the
    /// root are refreshed, and any node whose two sides differ in height by more than one is fixed with a
    /// single or double rotation. That keeps the tree's height under about 1.44 * log2(n), so Add, Remove
    /// and RemoveMin stay O(log n) however the keys arrive, and Range can jump straight to a window
    /// instead of walking from the smallest key. Enumerating it walks the entries in key order.
    /// </summary>
    public sealed class AvlTree<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> where TKey : IComparable<TKey>
    {
        private sealed class Node(TKey key, TValue value)
        {
            public TKey Key = key;
            public TValue Value = value;
            public Node? Left;
            public Node? Right;
            public int Height = 1;
        }

        private Node? _root;
        private bool _added;
        private bool _removed;


        /// <summary>
        /// How many entries the tree holds.
        /// </summary>
        public int Count { get; private set; }


        /// <summary>
        /// Height of the tree: 0 when empty, 1 for a single node.
        /// </summary>
        public int Height => HeightOf(_root);
        //----------------------------------------------------------//


        /// <summary>
        /// Inserts an entry, or replaces the value if the key is already there.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            _added = false;
            _root = Insert(_root, key, value);

            if (_added)
                Count++;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Looks a key up by walking down from the root, left or right at each node: O(log n).
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            var node = _root;

            while (node is not null)
            {
                int order = key.CompareTo(node.Key);

                if (order == 0)
                {
                    value = node.Value;
                    return true;
                }

                node = order < 0 ? node.Left : node.Right;
            }

            value = default!;
            return false;
        }
        //----------------------------------------------------------//


        public bool Contains(TKey key) => TryGet(key, out _);
        //----------------------------------------------------------//


        /// <summary>
        /// Walks every entry in key order. A stack holds the path down the left side, so the next entry
        /// is always on top once the walk has gone as far left as it can: the same left, self, right order
        /// as Range, without recursion.
        /// </summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            var path = new Stack<Node>();
            var node = _root;

            while (node is not null || path.Count > 0)
            {
                while (node is not null)
                {
                    path.Push(node);
                    node = node.Left;
                }

                node = path.Pop();
                yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
                node = node.Right;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        //----------------------------------------------------------//


        /// <summary>
        /// Reads the smallest key. False when the tree is empty.
        /// </summary>
        public bool TryGetMin(out TKey key)
        {
            if (_root is null)
            {
                key = default!;
                return false;
            }

            var node = _root;
            while (node.Left is not null)
                node = node.Left;

            key = node.Key;
            return true;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Removes the smallest entry (the oldest reading). False when the tree is empty.
        /// </summary>
        public bool RemoveMin()
        {
            if (_root is null)
                return false;

            _root = DetachMin(_root, out _);
            Count--;
            return true;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Removes one entry by key. False if the key is not in the tree.
        /// A leaf or a node with one child is simply replaced by what hangs below it. A node with two
        /// children is replaced by the smallest node on its right (found and detached with DetachMin),
        /// which keeps the ordering intact; the heights are fixed on the way back up.
        /// </summary>
        public bool Remove(TKey key)
        {
            _removed = false;
            _root = Delete(_root, key);

            if (_removed)
                Count--;

            return _removed;
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Adds every entry with from &lt;= key &lt;= to into the list, in key order, and returns how many
        /// tree nodes it had to look at.
        ///
        /// Why it is fast: a node whose key is not above 'from' cannot have a match on its left, and a
        /// node whose key is not below 'to' cannot have a match on its right, so those whole subtrees are
        /// never entered. The cost is O(log n + k) for k matches.
        /// </summary>
        public int Range(TKey from, TKey to, List<KeyValuePair<TKey, TValue>> into)
        {
            int visited = 0;
            Collect(_root, from, to, into, ref visited);
            return visited;
        }
        //----------------------------------------------------------//


        private static void Collect(Node? node, TKey from, TKey to, List<KeyValuePair<TKey, TValue>> into, ref int visited)
        {
            if (node is null)
                return;

            visited++;

            if (node.Key.CompareTo(from) > 0)
                Collect(node.Left, from, to, into, ref visited);

            if (node.Key.CompareTo(from) >= 0 && node.Key.CompareTo(to) <= 0)
                into.Add(new KeyValuePair<TKey, TValue>(node.Key, node.Value));

            if (node.Key.CompareTo(to) < 0)
                Collect(node.Right, from, to, into, ref visited);
        }
        //----------------------------------------------------------//


        private Node Insert(Node? node, TKey key, TValue value)
        {
            if (node is null)
            {
                _added = true;
                return new Node(key, value);
            }

            int order = key.CompareTo(node.Key);

            if (order < 0)
                node.Left = Insert(node.Left, key, value);
            else if (order > 0)
                node.Right = Insert(node.Right, key, value);
            else
                node.Value = value;

            return Rebalance(node);
        }
        //----------------------------------------------------------//


        private Node? Delete(Node? node, TKey key)
        {
            if (node is null)
                return null;

            int order = key.CompareTo(node.Key);

            if (order < 0)
            {
                node.Left = Delete(node.Left, key);
            }
            else if (order > 0)
            {
                node.Right = Delete(node.Right, key);
            }
            else
            {
                _removed = true;

                if (node.Left is null)
                    return node.Right;

                if (node.Right is null)
                    return node.Left;

                // Two children: the smallest node on the right takes this node's place.
                node.Right = DetachMin(node.Right, out var successor);
                successor.Left = node.Left;
                successor.Right = node.Right;
                node = successor;
            }

            return Rebalance(node);
        }
        //----------------------------------------------------------//


        // Walks down the left spine to the smallest node, drops it, and rebalances on the way back up.
        // The dropped node is handed back so a delete can reuse it.
        private static Node? DetachMin(Node node, out Node removed)
        {
            if (node.Left is null)
            {
                removed = node;
                return node.Right;
            }

            node.Left = DetachMin(node.Left, out removed);
            return Rebalance(node);
        }
        //----------------------------------------------------------//


        /// <summary>
        /// Refreshes a node's height and, if one side has grown two taller than the other, rotates.
        /// Left-heavy with a right-heavy left child needs a double rotation; otherwise a single one.
        /// </summary>
        private static Node Rebalance(Node node)
        {
            UpdateHeight(node);
            int balance = HeightOf(node.Left) - HeightOf(node.Right);

            if (balance > 1)
            {
                if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right))
                    node.Left = RotateLeft(node.Left);

                return RotateRight(node);
            }

            if (balance < -1)
            {
                if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left))
                    node.Right = RotateRight(node.Right);

                return RotateLeft(node);
            }

            return node;
        }
        //----------------------------------------------------------//


        private static Node RotateRight(Node node)
        {
            var pivot = node.Left!;
            node.Left = pivot.Right;
            pivot.Right = node;

            UpdateHeight(node);
            UpdateHeight(pivot);
            return pivot;
        }
        //----------------------------------------------------------//


        private static Node RotateLeft(Node node)
        {
            var pivot = node.Right!;
            node.Right = pivot.Left;
            pivot.Left = node;

            UpdateHeight(node);
            UpdateHeight(pivot);
            return pivot;
        }
        //----------------------------------------------------------//


        private static int HeightOf(Node? node) => node?.Height ?? 0;

        private static void UpdateHeight(Node node) => node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));
    }
}

//------------------------------------------- End Of File -------------------------------------------//
