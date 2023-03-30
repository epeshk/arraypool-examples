using System.Buffers;

namespace ArrayPoolTests;

public class Allocator<T> : ArrayPool<T>
{
  public override T[] Rent(int minimumLength) => GC.AllocateUninitializedArray<T>(minimumLength);

  public override void Return(T[] array, bool clearArray = false) { }
}