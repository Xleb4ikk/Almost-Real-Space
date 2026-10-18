using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Splits the assembly into islands by broken joints (pure C#, no Unity). The
    /// "part → parent" edge (ParentIndex) is intact until the joint breaks; a break is
    /// marked by the index of the CHILD part. The root island is the parts connected
    /// to any part without a parent (ParentIndex &lt; 0); the remaining connected
    /// components are detached islands (debris). The descendants of a broken part
    /// leave with it: connectivity is computed over the graph, not by depth.
    /// </summary>
    public static class ZoneIslands
    {
        public sealed class SplitResult
        {
            /// <summary>Indices of parts still connected to the assembly root.</summary>
            public readonly int[] Root;

            /// <summary>Detached islands: each holds the indices of its parts.</summary>
            public readonly IReadOnlyList<int[]> Detached;

            public SplitResult(int[] root, IReadOnlyList<int[]> detached)
            {
                Root = root;
                Detached = detached;
            }
        }

        public static SplitResult Split(IReadOnlyList<PartDefinition> parts, IReadOnlyCollection<int> brokenChildIndices)
        {
            int n = parts.Count;
            var cut = new bool[n];
            if (brokenChildIndices != null)
            {
                foreach (int child in brokenChildIndices)
                {
                    if (child >= 0 && child < n)
                    {
                        cut[child] = true;
                    }
                }
            }

            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                adjacency[i] = new List<int>();
            }

            for (int i = 0; i < n; i++)
            {
                int parent = parts[i].ParentIndex;
                if (parent >= 0 && parent < n && !cut[i])
                {
                    adjacency[i].Add(parent);
                    adjacency[parent].Add(i);
                }
            }

            bool[] inRoot = FloodFromRoots(parts, adjacency);
            var root = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (inRoot[i])
                {
                    root.Add(i);
                }
            }

            var detached = new List<int[]>();
            var seen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (inRoot[i] || seen[i])
                {
                    continue;
                }

                var island = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                seen[i] = true;
                while (queue.Count > 0)
                {
                    int v = queue.Dequeue();
                    island.Add(v);
                    for (int k = 0; k < adjacency[v].Count; k++)
                    {
                        int w = adjacency[v][k];
                        if (!seen[w] && !inRoot[w])
                        {
                            seen[w] = true;
                            queue.Enqueue(w);
                        }
                    }
                }

                detached.Add(island.ToArray());
            }

            return new SplitResult(root.ToArray(), detached);
        }

        private static bool[] FloodFromRoots(IReadOnlyList<PartDefinition> parts, List<int>[] adjacency)
        {
            int n = parts.Count;
            var inRoot = new bool[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (parts[i].ParentIndex < 0)
                {
                    inRoot[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                for (int k = 0; k < adjacency[v].Count; k++)
                {
                    int w = adjacency[v][k];
                    if (!inRoot[w])
                    {
                        inRoot[w] = true;
                        queue.Enqueue(w);
                    }
                }
            }

            return inRoot;
        }
    }
}
