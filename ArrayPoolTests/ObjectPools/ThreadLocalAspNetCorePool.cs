using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace ArrayPoolTests.ObjectPools;


public class ThreadLocalAspNetCorePool<T> : ArrayPool<T>
{
    private ObjectPool<T[]> pool = new ThreadLocalAspNetCoreObjPool<T[]>(new ArrayPooledObjectPolicy<T>(), 1024);

    public override T[] Rent(int minimumLength) => pool.Get();

    public override void Return(T[] array, bool clearArray = false) => pool.Return(array);
}

public class ThreadLocalAspNetCoreObjPool<T> : ObjectPool<T> where T : class
{
    private readonly Func<T> _createFunc;
    private readonly Func<T, bool> _returnFunc;
    private readonly int _maxCapacity;
    private int _numItems;

    private protected readonly ConcurrentQueue<T> _items = new();
    private protected ThreadLocal<T?> _fastItem = new(() => null);

    /// <summary>
    /// Creates an instance of <see cref="DefaultObjectPool{T}"/>.
    /// </summary>
    /// <param name="policy">The pooling policy to use.</param>
    public ThreadLocalAspNetCoreObjPool(IPooledObjectPolicy<T> policy)
        : this(policy, Environment.ProcessorCount * 2)
    {
    }

    /// <summary>
    /// Creates an instance of <see cref="DefaultObjectPool{T}"/>.
    /// </summary>
    /// <param name="policy">The pooling policy to use.</param>
    /// <param name="maximumRetained">The maximum number of objects to retain in the pool.</param>
    public ThreadLocalAspNetCoreObjPool(IPooledObjectPolicy<T> policy, int maximumRetained)
    {
        // cache the target interface methods, to avoid interface lookup overhead
        _createFunc = policy.Create;
        _returnFunc = policy.Return;
        _maxCapacity = maximumRetained - 1;  // -1 to account for _fastItem
    }

    /// <inheritdoc />
    public override T Get()
    {
        var item = _fastItem.Value;
        if (item != null)
        {
            _fastItem.Value = null;
            return item;
        }
        if (_items.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _numItems);
            return item;
        }

        // no object available, so go get a brand new one
        return _createFunc();
    }

    /// <inheritdoc />
    public override void Return(T obj)
    {
        ReturnCore(obj);
    }

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <returns>true if the object was returned to the pool</returns>
    private protected bool ReturnCore(T obj)
    {
        if (!_returnFunc(obj))
        {
            // policy says to drop this object
            return false;
        }

        if (_fastItem.Value == null)
        {
            _fastItem.Value = obj;
            return true;
        }

        if (Interlocked.Increment(ref _numItems) <= _maxCapacity)
        {
            _items.Enqueue(obj);
            return true;
        }

        // no room, clean up the count and drop the object on the floor
        Interlocked.Decrement(ref _numItems);
        return false;
    }
}