namespace Wintodo.Core.Presentation;

/// <summary>Maps a row insertion boundary to its index after removing the source.</summary>
public static class TaskReordering
{
    public static int GetDestinationIndex(int sourceIndex, int targetIndex, bool insertAfter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);
        int insertionIndex = targetIndex + (insertAfter ? 1 : 0);
        return insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
    }
}
