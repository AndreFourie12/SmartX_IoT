using SmartX.Shared.Collections;

namespace SmartX.Tests
{
    public class AvlTreeTests
    {
        private static AvlTree<int, string> Build(IEnumerable<int> keys)
        {
            var tree = new AvlTree<int, string>();
            foreach (var key in keys)
                tree.Add(key, "v" + key);
            return tree;
        }

        private static List<int> RangeKeys(AvlTree<int, string> tree, int from, int to, out int visited)
        {
            var found = new List<KeyValuePair<int, string>>();
            visited = tree.Range(from, to, found);
            return found.Select(pair => pair.Key).ToList();
        }
        //----------------------------------------------------------//


        [Fact]
        public void Range_IsSorted_WhateverTheInsertOrder()
        {
            var tree = Build(new[] { 50, 10, 90, 30, 70, 20, 80, 60, 40, 100 });

            Assert.Equal(new[] { 30, 40, 50, 60, 70 }, RangeKeys(tree, 30, 70, out _));
        }
        //----------------------------------------------------------//


        [Theory]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Height_StaysWithinAvlBound_ForSortedInserts(int count)
        {
            // Sorted input is the worst case for a plain BST (height n); AVL must stay near log2(n).
            var tree = Build(Enumerable.Range(0, count));

            Assert.Equal(count, tree.Count);
            Assert.True(tree.Height <= 1.45 * Math.Log2(count + 2), $"height {tree.Height} too tall for {count} nodes");
        }
        //----------------------------------------------------------//


        [Fact]
        public void Height_StaysWithinAvlBound_ForRandomInserts()
        {
            var random = new Random(7);
            var tree = Build(Enumerable.Range(0, 4000).OrderBy(_ => random.Next()));

            Assert.True(tree.Height <= 1.45 * Math.Log2(4002));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Add_ReplacesTheValue_WhenTheKeyExists()
        {
            var tree = Build(new[] { 1, 2, 3 });
            tree.Add(2, "changed");

            var found = new List<KeyValuePair<int, string>>();
            tree.Range(2, 2, found);

            Assert.Equal(3, tree.Count);
            Assert.Equal("changed", Assert.Single(found).Value);
        }
        //----------------------------------------------------------//


        [Fact]
        public void RemoveMin_RemovesTheSmallestKeyEachTime()
        {
            var tree = Build(new[] { 5, 3, 8, 1, 4, 9 });

            Assert.True(tree.TryGetMin(out int first));
            Assert.Equal(1, first);

            Assert.True(tree.RemoveMin());
            Assert.True(tree.TryGetMin(out int second));
            Assert.Equal(3, second);
            Assert.Equal(5, tree.Count);
        }
        //----------------------------------------------------------//


        [Fact]
        public void RemoveMin_KeepsTheTreeBalanced_AndEmptiesCleanly()
        {
            var tree = Build(Enumerable.Range(0, 2000));

            for (int i = 0; i < 1500; i++)
                tree.RemoveMin();

            Assert.Equal(500, tree.Count);
            Assert.True(tree.Height <= 1.45 * Math.Log2(502));

            for (int i = 0; i < 500; i++)
                tree.RemoveMin();

            Assert.Equal(0, tree.Count);
            Assert.False(tree.RemoveMin());
            Assert.False(tree.TryGetMin(out _));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Range_IncludesBothBounds_AndHandlesEmptyWindows()
        {
            var tree = Build(new[] { 10, 20, 30, 40, 50 });

            Assert.Equal(new[] { 20, 30, 40 }, RangeKeys(tree, 20, 40, out _));
            Assert.Empty(RangeKeys(tree, 21, 29, out _));
            Assert.Empty(RangeKeys(tree, 100, 200, out _));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Range_VisitsFarFewerNodesThanItStores()
        {
            var tree = Build(Enumerable.Range(0, 5000));

            var keys = RangeKeys(tree, 4000, 4039, out int visited);

            Assert.Equal(40, keys.Count);
            Assert.True(visited < 100, $"visited {visited} of 5000 nodes");
        }
        //----------------------------------------------------------//


        [Fact]
        public void TryGet_And_Contains_FindOnlyKeysThatWereAdded()
        {
            var tree = Build(new[] { 40, 10, 30, 20 });

            Assert.True(tree.TryGet(30, out var value));
            Assert.Equal("v30", value);
            Assert.True(tree.Contains(10));
            Assert.False(tree.Contains(25));
            Assert.False(tree.TryGet(99, out _));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Enumerating_GivesEveryEntryInKeyOrder()
        {
            var random = new Random(3);
            var keys = Enumerable.Range(0, 500).OrderBy(_ => random.Next()).ToList();
            var tree = Build(keys);

            var walked = tree.Select(pair => pair.Key).ToList();

            Assert.Equal(Enumerable.Range(0, 500), walked);
            Assert.Empty(new AvlTree<int, string>());
        }
        //----------------------------------------------------------//


        [Fact]
        public void Remove_HandlesLeafOneChildAndTwoChildNodes()
        {
            var tree = Build(new[] { 50, 30, 70, 20, 40, 60, 80, 65 });

            Assert.True(tree.Remove(20));    // a leaf
            Assert.True(tree.Remove(60));    // one child (65)
            Assert.True(tree.Remove(50));    // two children, and the root

            Assert.Equal(new[] { 30, 40, 65, 70, 80 }, tree.Select(pair => pair.Key));
            Assert.Equal(5, tree.Count);
            Assert.False(tree.Contains(50));
            Assert.False(tree.Remove(50));
            Assert.False(tree.Remove(1234));
        }
        //----------------------------------------------------------//


        [Fact]
        public void Remove_AtRandom_KeepsTheTreeSortedAndBalanced()
        {
            var random = new Random(21);
            var keys = Enumerable.Range(0, 4000).OrderBy(_ => random.Next()).ToList();
            var tree = Build(keys);
            var remaining = new HashSet<int>(keys);

            for (int i = 0; i < 3000; i++)
            {
                int key = keys[i];

                Assert.True(tree.Remove(key));
                remaining.Remove(key);

                if (i % 250 == 0)
                {
                    Assert.Equal(remaining.Count, tree.Count);
                    Assert.True(tree.Height <= 1.45 * Math.Log2(tree.Count + 2), $"height {tree.Height} with {tree.Count} nodes");
                    Assert.Equal(remaining.Order(), tree.Select(pair => pair.Key));
                }
            }

            Assert.Equal(1000, tree.Count);
            Assert.True(tree.Height <= 1.45 * Math.Log2(1002));
            Assert.Equal(remaining.Order(), tree.Select(pair => pair.Key));
        }
        //----------------------------------------------------------//


        [Fact]
        public void HoldsSixtyThousandPoints_AndStaysFast()
        {
            const int Count = 60_000;
            var random = new Random(5);
            var tree = Build(Enumerable.Range(0, Count).OrderBy(_ => random.Next()));

            // A late window is still found by going straight to it.
            var keys = RangeKeys(tree, 59_000, 59_099, out int visited);

            Assert.Equal(Count, tree.Count);
            Assert.True(tree.Height <= 1.45 * Math.Log2(Count + 2), $"height {tree.Height}");
            Assert.Equal(100, keys.Count);
            Assert.True(visited < 250, $"visited {visited} of {Count} nodes");
            Assert.True(tree.Contains(0) && tree.Contains(Count - 1) && !tree.Contains(Count));

            // Evicting the oldest ten thousand, as the timeline store does, leaves it balanced.
            for (int i = 0; i < 10_000; i++)
                tree.RemoveMin();

            Assert.Equal(50_000, tree.Count);
            Assert.True(tree.Height <= 1.45 * Math.Log2(50_002));
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//
