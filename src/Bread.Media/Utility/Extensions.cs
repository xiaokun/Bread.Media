using System.Collections.Concurrent;

namespace Bread.Media;

internal static class Extensions
{
    public static void Clear<T>(this ConcurrentQueue<T> queue)
    {
        while (queue.TryDequeue(out T? item)) {
            // do nothing
        }
    }
}
