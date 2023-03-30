using System.Buffers;
using System.Collections.Concurrent;

namespace ArrayPoolTests.ObjectPools;

public class ConcurrentQueueObjPool<T> : ArrayPool<T>
{
  private static readonly ConcurrentQueue<T[]> collection = new();

  public override T[] Rent(int minimumLength)
  {
    return collection.TryDequeue(out var obj) ? obj : new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    collection.Enqueue(array);
  }
}
public class ConcurrentStackPool<T> : ArrayPool<T>
{
  private static readonly ConcurrentStack<T[]> collection = new ();

  public override T[] Rent(int minimumLength)
  {
    return collection.TryPop(out var obj) ? obj : new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    collection.Push(array);
  }
}
public class ConcurrentBagPool<T> : ArrayPool<T>
{
  private static readonly ConcurrentBag<T[]> collection = new ();

  public override T[] Rent(int minimumLength)
  {
    return collection.TryTake(out var obj) ? obj : new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    collection.Add(array);
  }
}