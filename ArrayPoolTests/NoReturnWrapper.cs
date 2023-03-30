using System.Buffers;

namespace ArrayPoolTests;

public class NoReturnWrapper<T> : ArrayPool<T>
{
  private readonly ArrayPool<T> pool;

  public NoReturnWrapper(ArrayPool<T> pool) => this.pool = pool;

  public override T[] Rent(int minimumLength) => pool.Rent(minimumLength);

  public override void Return(T[] array, bool clearArray = false)
  {
    if (Random.Shared.NextDouble() < 0.001)
      pool.Return(array, clearArray);
  }
}