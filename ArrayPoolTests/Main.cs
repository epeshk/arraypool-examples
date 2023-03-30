using System.Buffers;
using ArrayPoolTests;
using ArrayPoolTests.ObjectPools;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.ObjectPool;
using static Params;

BenchmarkRunner.Run<PoolingBenchmark>();
// BenchmarkRunner.Run<OverheadMeasurement>();

public static class Params
{
  public const int ArraySize = 1024;
  public const int MaxSharding = 4; // set to small value to reduce sharding of custom pools
  
  public const int Threads = 16;
  public const int Iterations = 64*1024;
}

[MemoryDiagnoser(), ThreadingDiagnoser]
public class PoolingBenchmark
{
  private static ArrayPool<byte> mypool = new MyTlsOverPerCoreLockedStacksArrayPool<byte>();
  private static ArrayPool<byte> pool = ArrayPool<byte>.Create(int.MaxValue, 100);
  private static ArrayPool<byte> concurrentqueuepool = new ConcurrentQueuePool<byte>();
  private static ArrayPool<byte> wrappedconcurrentqueuepool = new WrappedConcurrentQueuePool<byte>();
  private static ArrayPool<byte> boundedqueuepool = new BoundedQueuePool<byte>();
  private static ArrayPool<byte> noreturn = new NoReturnWrapper<byte>(ArrayPool<byte>.Shared);
  private static ArrayPool<byte> allocator = new Allocator<byte>();
  
  private static ArrayPool<byte> lockedstack = new LockedStackPool<byte>();
  private static ArrayPool<byte> threadlocalstack = new ThreadLocalStackPool<byte>();
  private static ArrayPool<byte> threadlocalstack_nfi = new LockedStackPoolNoFastItem<byte>();
  private static ArrayPool<byte> defaulobjpool = new DefaultObjPool<byte>();
  private static ArrayPool<byte> cqueue = new ConcurrentQueueObjPool<byte>();
  private static ArrayPool<byte> cstack = new ConcurrentStackPool<byte>();
  private static ArrayPool<byte> cbag = new ConcurrentBagPool<byte>();
  private static ArrayPool<byte> tlocaldef = new ThreadLocalAspNetCorePool<byte>();

  public NamedParam[] Pools { get; } = {
    // array pools
    new NamedParam(ArrayPool<byte>.Shared, "Shared"),
    new NamedParam(mypool, "Limited"),
    new NamedParam(pool, "Custom"),
    new NamedParam(concurrentqueuepool, "ConcurrentQueue"),
    new NamedParam(boundedqueuepool, "BoundedQueue"),
    new NamedParam(wrappedconcurrentqueuepool, "CQueue+Counter"),
    new NamedParam(noreturn, "NoReturn"),
    new NamedParam(allocator, "AllocatorPool"),
    
    // object pools
    new NamedParam(threadlocalstack_nfi, "StackWithLock"),
    new NamedParam(lockedstack, "StackWithLock+Slot"),
    new NamedParam(threadlocalstack, "LocalStack"),
    new NamedParam(defaulobjpool, "DefaultObjPool"),
    new NamedParam(cqueue, "CQueueObj"),
    new NamedParam(cstack, "CStackObj"),
    new NamedParam(cbag, "CBagObj"),
    new NamedParam(tlocaldef, "TLObjPool"),
  };

  [ParamsSource(nameof(Pools))]
  public NamedParam Pool { get; set; }

  public ArrayPool<byte> ArrayPool => Pool.Value;

  // [Benchmark()]
  public void SingleArray()
  {
    var tasks = new Task[Threads];
    for (int i = 0; i < Threads; i++)
    {
      tasks[i] = Task.Run(() =>
      {
        for (int j = 0; j < Iterations; j++)
        {
          var arr = ArrayPool.Rent(ArraySize);
          Random.Shared.NextBytes(arr);
          ArrayPool.Return(arr);
        }
      });
    }

    Task.WaitAll(tasks);
  }

  [Benchmark()]
  public void TwoArrays()
  {
    var tasks = new Task[Threads];
    for (int i = 0; i < Threads; i++)
    {
      tasks[i] = Task.Run(() =>
      {
        for (int j = 0; j < Iterations; j++)
        {
          var arr1 = ArrayPool.Rent(ArraySize);
          var arr2 = ArrayPool.Rent(ArraySize);
          Random.Shared.NextBytes(arr1);
          Random.Shared.NextBytes(arr2);
          ArrayPool.Return(arr2);
          ArrayPool.Return(arr1);
        }
      });
    }

    Task.WaitAll(tasks);
  }
}

public class OverheadMeasurement
{
  [Benchmark()]
  public void SingleArray()
  {
    var tasks = new Task[Threads];
    for (int i = 0; i < Threads; i++)
    {
      tasks[i] = Task.Run(() =>
      {
        var arr = ArrayPool<byte>.Shared.Rent(ArraySize);
        for (int j = 0; j < Iterations; j++)
        {
          Random.Shared.NextBytes(arr);
        }
      });
    }

    Task.WaitAll(tasks);
  }

  [Benchmark()]
  public void NoPool_SingleArray()
  {
    var tasks = new Task[Threads];
    for (int i = 0; i < Threads; i++)
    {
      tasks[i] = Task.Run(() =>
      {
        var arr1 = ArrayPool<byte>.Shared.Rent(ArraySize);
        var arr2 = ArrayPool<byte>.Shared.Rent(ArraySize);

        for (int j = 0; j < Iterations; j++)
        {
          Random.Shared.NextBytes(arr1);
          Random.Shared.NextBytes(arr2);
        }
      });
    }

    Task.WaitAll(tasks);
  }
}

public class ArrayPooledObjectPolicy<T> : PooledObjectPolicy<T[]>
{
  /// <inheritdoc />
  public override T[] Create() => new T[ArraySize];

  /// <inheritdoc />
  public override bool Return(T[] obj) => true;
}

public record NamedParam(ArrayPool<byte> Value, string DisplayText)
{
  public override string ToString() => DisplayText;
}