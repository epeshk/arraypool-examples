using System.Buffers;

namespace ArrayPoolTests.ObjectPools;

public class LockedStackPool<T> : ArrayPool<T>
{
  private readonly Stack<T[]> items = new Stack<T[]>();

  private T[]? _fastItem;

  public override T[] Rent(int minimumLength)
  {
    var item = Interlocked.Exchange(ref _fastItem, null);
    if (item != null) return item;
    
    if (items.Count != 0)
    {
      lock (items)
      {
        if (items.TryPop(out item)) return item;
      }
    }

    return new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    if (Interlocked.CompareExchange(ref _fastItem, array, null) == null) return;
    lock (items)
      items.Push(array);
  }
}
public class LockedStackPoolNoFastItem<T> : ArrayPool<T>
{
  private readonly Stack<T[]> items = new Stack<T[]>();

  private T[]? _fastItem;

  public override T[] Rent(int minimumLength)
  {
    var item = Interlocked.Exchange(ref _fastItem, null);
    if (item != null) return item;
    
    if (items.Count != 0)
    {
      lock (items)
      {
        if (items.TryPop(out item)) return item;
      }
    }

    return new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    if (Interlocked.CompareExchange(ref _fastItem, array, null) == null) return;
    lock (items)
      items.Push(array);
  }
}