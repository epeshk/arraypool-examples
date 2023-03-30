using System.Collections.Concurrent;

namespace ArrayPoolTests;

public class ConcurrentQueueWrapper<T>
{
  private ConcurrentQueue<T> queue = new();
  private int capacity;

  public ConcurrentQueueWrapper(int capacity)
  {
    this.capacity = capacity;
  }

  public bool TryEnqueue(T t)
  {
    var curCap = capacity;

    if (curCap <= 0)
      return false;

    if (Interlocked.Decrement(ref capacity) >= 0)
    {
      queue.Enqueue(t);
      return true;
    }

    Interlocked.Increment(ref capacity);
    return false;

    // while (true)
    // {
    //   var old = Interlocked.CompareExchange(ref capacity, curCap - 1, curCap);
    //   if (old == curCap)
    //   {
    //     queue.Enqueue(t);
    //     return true;
    //   }
    //
    //   if (old <= 0)
    //     return false;
    //
    //   curCap = old;
    // }
  }

  public bool TryDequeue(out T t)
  {
    if (!queue.TryDequeue(out t))
      return false;
    Interlocked.Increment(ref capacity);
    return true;

  }
}