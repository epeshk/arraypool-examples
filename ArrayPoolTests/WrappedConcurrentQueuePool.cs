using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArrayPoolTests;

public class WrappedConcurrentQueuePool<T> : ArrayPool<T>
{
    /// <summary>The number of buckets (array sizes) in the pool, one for each array length, starting from length 16.</summary>
    private const int NumBuckets = 27; // Utilities.SelectBucketIndex(1024 * 1024 * 1024 + 1)
    /// <summary>Maximum number of per-core stacks to use per array size.</summary>
    private const int MaxPerCorePerArraySizeStacks = Params.MaxSharding; // selected to avoid needing to worry about processor groups
    /// <summary>The maximum number of buffers to store in a bucket's global queue.</summary>
    private const int MaxBuffersPerArraySizePerCore = 8;

    /// <summary>A per-thread array of arrays, to cache one array per array size per thread.</summary>
    [ThreadStatic]
    private static ThreadLocalArray[]? t_tlsBuckets;
    /// <summary>Used to keep track of all thread local buckets for trimming if needed.</summary>
    /// <summary>
    /// An array of per-core array stacks. The slots are lazily initialized to avoid creating
    /// lots of overhead for unused array sizes.
    /// </summary>
    private readonly PerCoreLockedStacks?[] _buckets = new PerCoreLockedStacks[NumBuckets];
    /// <summary>Whether the callback to trim arrays in response to memory pressure has been created.</summary>
    private int _trimCallbackCreated;

    /// <summary>Allocate a new PerCoreLockedStacks and try to store it into the <see cref="_buckets"/> array.</summary>
    private PerCoreLockedStacks CreatePerCoreLockedStacks(int bucketIndex)
    {
        var inst = new PerCoreLockedStacks();
        return Interlocked.CompareExchange(ref _buckets[bucketIndex], inst, null) ?? inst;
    }

    /// <summary>Gets an ID for the pool to use with events.</summary>
    private int Id => GetHashCode();

    public override T[] Rent(int minimumLength)
    {
        T[]? buffer;

        // Get the bucket number for the array length. The result may be out of range of buckets,
        // either for too large a value or for 0 and negative values.
        int bucketIndex = Utilities.SelectBucketIndex(minimumLength);

        // First, try to get an array from TLS if possible.
        ThreadLocalArray[]? tlsBuckets = t_tlsBuckets;
        if (tlsBuckets is not null && (uint)bucketIndex < (uint)tlsBuckets.Length)
        {
            buffer = tlsBuckets[bucketIndex].Array;
            if (buffer is not null)
            {
                tlsBuckets[bucketIndex].Array = null;
                return buffer;
            }
        }

        // Next, try to get an array from one of the per-core stacks.
        PerCoreLockedStacks?[] perCoreBuckets = _buckets;
        if ((uint)bucketIndex < (uint)perCoreBuckets.Length)
        {
            PerCoreLockedStacks? b = perCoreBuckets[bucketIndex];
            if (b is not null)
            {
                buffer = b.TryPop();
                if (buffer is not null)
                {
                    return buffer;
                }
            }

            // No buffer available.  Ensure the length we'll allocate matches that of a bucket
            // so we can later return it.
            minimumLength = Utilities.GetMaxSizeForBucket(bucketIndex);
        }
        else if (minimumLength == 0)
        {
            // We allow requesting zero-length arrays (even though pooling such an array isn't valuable)
            // as it's a valid length array, and we want the pool to be usable in general instead of using
            // `new`, even for computed lengths. But, there's no need to log the empty array.  Our pool is
            // effectively infinite for empty arrays and we'll never allocate for rents and never store for returns.
            return Array.Empty<T>();
        }
        else if (minimumLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        buffer = GC.AllocateUninitializedArray<T>(minimumLength);
        return buffer;
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        ArgumentNullException.ThrowIfNull(array);

        // Determine with what bucket this array length is associated
        int bucketIndex = Utilities.SelectBucketIndex(array.Length);

        // Make sure our TLS buckets are initialized.  Technically we could avoid doing
        // this if the array being returned is erroneous or too large for the pool, but the
        // former condition is an error we don't need to optimize for, and the latter is incredibly
        // rare, given a max size of 1B elements.
        ThreadLocalArray[] tlsBuckets = t_tlsBuckets ?? InitializeTlsBucketsAndTrimming();

        bool haveBucket = false;
        bool returned = true;
        if ((uint)bucketIndex < (uint)tlsBuckets.Length)
        {
            haveBucket = true;

            // Clear the array if the user requested it.
            if (clearArray)
            {
                Array.Clear(array);
            }

            // Check to see if the buffer is the correct size for this bucket.
            if (array.Length != Utilities.GetMaxSizeForBucket(bucketIndex))
            {
                throw new ArgumentException("ArgumentException_BufferNotFromPool", nameof(array));
            }

            // Store the array into the TLS bucket.  If there's already an array in it,
            // push that array down into the per-core stacks, preferring to keep the latest
            // one in TLS for better locality.
            ref ThreadLocalArray tla = ref tlsBuckets[bucketIndex];
            T[]? prev = tla.Array;
            tla = new ThreadLocalArray(array);
            if (prev is not null)
            {
                PerCoreLockedStacks stackBucket = _buckets[bucketIndex] ?? CreatePerCoreLockedStacks(bucketIndex);
                returned = stackBucket.TryPush(prev);
            }
        }
    }

    private ThreadLocalArray[] InitializeTlsBucketsAndTrimming()
    {
        Debug.Assert(t_tlsBuckets is null, $"Non-null {nameof(t_tlsBuckets)}");

        var tlsBuckets = new ThreadLocalArray[NumBuckets];
        t_tlsBuckets = tlsBuckets;

        return tlsBuckets;
    }

    /// <summary>Stores a set of stacks of arrays, with one stack per core.</summary>
    private sealed class PerCoreLockedStacks
    {
        /// <summary>Number of locked stacks to employ.</summary>
        private static readonly int s_lockedStackCount = Math.Min(Environment.ProcessorCount, MaxPerCorePerArraySizeStacks);
        /// <summary>The stacks.</summary>
        private readonly ConcurrentQueueWrapper<T[]>[] _perCoreStacks;

        /// <summary>Initializes the stacks.</summary>
        public PerCoreLockedStacks()
        {
            // Create the stacks.  We create as many as there are processors, limited by our max.
            var stacks = new ConcurrentQueueWrapper<T[]>[s_lockedStackCount];
            for (int i = 0; i < stacks.Length; i++)
            {
                stacks[i] = new ConcurrentQueueWrapper<T[]>(8);
            }
            _perCoreStacks = stacks;
        }

        /// <summary>Try to push the array into the stacks. If each is full when it's tested, the array will be dropped.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPush(T[] array)
        {
            // Try to push on to the associated stack first.  If that fails,
            // round-robin through the other stacks.
            ConcurrentQueueWrapper<T[]>[] stacks = _perCoreStacks;
            int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)s_lockedStackCount); // mod by constant in tier 1
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[index].TryEnqueue(array)) return true;
                if (++index == stacks.Length) index = 0;
            }
            return false;
        }

        /// <summary>Try to get an array from the stacks.  If each is empty when it's tested, null will be returned.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[]? TryPop()
        {
            // Try to pop from the associated stack first.  If that fails, round-robin through the other stacks.
            ConcurrentQueueWrapper<T[]>[] stacks = _perCoreStacks;
            int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)s_lockedStackCount); // mod by constant in tier 1
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[index].TryDequeue(out var arr)) return arr;
                if (++index == stacks.Length) index = 0;
            }
            return null;
        }
    }

    /// <summary>Wrapper for arrays stored in ThreadStatic buckets.</summary>
    private struct ThreadLocalArray
    {
        /// <summary>The stored array.</summary>
        public T[]? Array;

        public ThreadLocalArray(T[] array)
        {
            Array = array;
        }
    }
}