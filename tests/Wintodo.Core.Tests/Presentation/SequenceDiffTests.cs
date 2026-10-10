using Wintodo.Core.Presentation;

namespace Wintodo.Core.Tests.Presentation;

public sealed class SequenceDiffTests
{
    [Fact]
    public void GeneratedSequences_ApplyToTarget_AndMoveOnlyOutsideLongestSubsequence()
    {
        var random = new Random(707);
        for (int sample = 0; sample < 600; sample++)
        {
            int[] before = Enumerable.Range(0, 20).OrderBy(_ => random.Next()).Take(random.Next(21)).ToArray();
            int[] after = Enumerable.Range(0, 25).OrderBy(_ => random.Next()).Take(random.Next(26)).ToArray();
            IReadOnlyList<SequenceChange<int>> changes = SequenceDiff.Build(before, after);
            var actual = before.ToList();
            foreach (SequenceChange<int> change in changes)
            {
                switch (change.Kind)
                {
                    case SequenceChangeKind.Insert: actual.Insert(change.Index, change.Key); break;
                    case SequenceChangeKind.Remove:
                        Assert.Equal(change.Key, actual[change.Index]);
                        actual.RemoveAt(change.Index); break;
                    case SequenceChangeKind.Move:
                        Assert.Equal(change.Key, actual[change.Index]);
                        actual.RemoveAt(change.Index);
                        actual.Insert(change.Destination, change.Key); break;
                }
            }
            Assert.Equal(after, actual);
            int[] retainedIndices = after.Where(before.Contains).Select(key => Array.IndexOf(before, key)).ToArray();
            int[] lengths = new int[retainedIndices.Length];
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = 1;
                for (int j = 0; j < i; j++)
                    if (retainedIndices[j] < retainedIndices[i]) lengths[i] = Math.Max(lengths[i], lengths[j] + 1);
            }
            int stable = lengths.Length > 0 ? lengths.Max() : 0;
            Assert.Equal(retainedIndices.Length - stable, changes.Count(c => c.Kind == SequenceChangeKind.Move));
        }
    }

    [Theory]
    [InlineData(0, 9999)]
    [InlineData(9999, 0)]
    [InlineData(4000, 4001)]
    public void SingleMove_InTenThousandRows_EmitsOneMove(int from, int to)
    {
        int[] before = Enumerable.Range(0, 10000).ToArray();
        var after = before.ToList();
        after.RemoveAt(from);
        after.Insert(to, from);
        Assert.Equal(SequenceChangeKind.Move, Assert.Single(SequenceDiff.Build(before, after)).Kind);
    }

    [Fact]
    public void UnchangedSequence_EmitsNothing() => Assert.Empty(SequenceDiff.Build(new[] { 1, 2 }, new[] { 1, 2 }));

    [Fact]
    public void InitialLoad_AppendsTenThousandRowsInOrder()
    {
        int[] target = Enumerable.Range(0, 10000).ToArray();
        IReadOnlyList<SequenceChange<int>> changes = SequenceDiff.Build(Array.Empty<int>(), target);
        Assert.Equal(target.Length, changes.Count);
        for (int i = 0; i < changes.Count; i++)
            Assert.Equal(new SequenceChange<int>(SequenceChangeKind.Insert, i, i), changes[i]);
    }
}
