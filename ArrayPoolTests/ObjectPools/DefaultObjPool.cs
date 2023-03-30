using System.Buffers;
using Microsoft.Extensions.ObjectPool;

namespace ArrayPoolTests.ObjectPools;

public class DefaultObjPool<T> : ArrayPool<T>
{
  private ObjectPool<T[]> pool = new DefaultObjectPool<T[]>(new ArrayPooledObjectPolicy<T>(), 1024);

  public override T[] Rent(int minimumLength) => pool.Get();

  public override void Return(T[] array, bool clearArray = false) => pool.Return(array);
}