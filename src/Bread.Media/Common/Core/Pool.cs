using System.Collections.Concurrent;
using Bread.Utility.Threading;

namespace Bread.Media;

public abstract class Pool<T> : IDisposable where T : IDisposable
{
    public bool IsDisposed => _isDisposed.Value;

    protected readonly AtomicInteger _total = new AtomicInteger(0);
    protected readonly AtomicBoolean _isDisposed = new AtomicBoolean(false);

    protected readonly ConcurrentBag<Pooled<T>> _items;

    protected int _maxCount;
    protected int _capcity;

    /// <summary>
    /// Initialize the Pool.
    /// </summary>
    /// <param name="capcity">the normal size of the pool.</param>
    /// <param name="max">the maximum size of the pool.</param>
    public Pool(int capcity = 3, int max = 100)
    {
        _maxCount = max;
        _capcity = capcity;
        _items = new ConcurrentBag<Pooled<T>>();
    }

    /// <summary>
    /// Allocate pool items and queue them to the pool.
    /// subclass must update the total count.
    /// </summary>
    /// <returns>true if success. otherwise false.</returns>
    protected abstract bool Allocate();

    /// <summary>
    /// Get item from the pool. 
    /// </summary>
    /// <returns>返回获取的 item，当标记为结束或超过最大许可数量时，返回 null</returns>
    public Pooled<T>? Get()
    {
        if (_isDisposed.Value) {
            return null;
        }

        if (_items.TryTake(out Pooled<T>? item)) {
            return item;
        }

        if (_maxCount != 0 && _total > _maxCount) return null;

        if (Allocate() == false) {
            throw new InvalidProgramException("Pool allocate fail.");
        }

        return Get();
    }

    /// <summary>
    ///  Return item back to pool.
    /// </summary>
    /// <param name="item">the pool item.</param>
    /// <returns>true if return to pool succeced. otherwise false.</returns>
    internal virtual bool Return(Pooled<T> item)
    {
        if (_isDisposed == true) {
            _total.Decrement();
            return false;
        }

        if (_maxCount > 0) {
            if (_items.Count > _capcity) {
                _total.Decrement();
                return false;
            }
        }

        _items.Add(item);
        return true;
    }


    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed.Value) return;
        while (_items.TryTake(out Pooled<T>? item)) {
            if (item == null) continue;
            item.Detech();
            item.Dispose();
        }
        _isDisposed.Value = true;
    }

    ~Pool()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
