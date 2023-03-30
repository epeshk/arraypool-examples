using System.Buffers;

namespace ArrayPoolTests.ObjectPools;

public class ThreadLocalStackPool<T> : ArrayPool<T>
{
  [ThreadStatic]
  private static Stack<T[]>? items;

  public override T[] Rent(int minimumLength)
  {
    items ??= new Stack<T[]>();
    
    if (items.TryPop(out var item)) return item;

    return new T[Params.ArraySize];
  }

  public override void Return(T[] array, bool clearArray = false)
  {
    if (clearArray) Array.Clear(array);
    items?.Push(array);
  }
}