using System.Threading;

namespace Bread.Media;

/// <summary>
///  <typeparamref name="T"/> 的池化元素<br/>
///  使用引用计数跟着对象的生命周期<br/>
///  需要显示使用Dispose()释放引用计数
/// </summary>
public sealed class Pooled<T> : IDisposable where T : IDisposable
{
    /// <summary>
    /// 用于调试
    /// </summary>
    public int Index { get; set; }

    public T Data => _data;

    T _data;
    int _refCount;
    Pool<T>? _pool;

    /// <summary>
    /// 需要显示使用Dispose()释放引用计数
    /// </summary>
    public Pooled(T data, Pool<T> pool, int index = 0)
    {
        _data = data;
        _refCount = 1;
        _pool = pool;
        Index = index;
    }

    /// <summary>
    /// Data 的释放控制权转移给新的实例, 接收者调用。<br/>
    /// 双方均需要显示使用Dispose()释放引用计数
    /// </summary>
    public Pooled<T> Clone()
    {
        if (Interlocked.Increment(ref _refCount) == 1) {
            throw new ObjectDisposedException(nameof(Pooled<T>));
        }
        return this;
    }

    public void Detech()
    {
        _pool = null;
    }


    private bool _disposed;

    /// <summary>
    /// 引用计数减一，引用数为0时重新放置回 Pool
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        var count = Interlocked.Decrement(ref _refCount);
        if (count < 0) {
            throw new ObjectDisposedException(nameof(Pooled<T>));
        }

        if (count == 0) {
            if (_pool != null) {
                if (_pool.Return(this)) {
                    Volatile.Write(ref _refCount, 1);
                    return;
                }
            }

            _data.Dispose();
            _disposed = true;
        }
    }
}
