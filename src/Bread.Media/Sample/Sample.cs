using Bread.Utility.Threading;

namespace Bread.Media;

public class Sample : IDisposable
{

    protected AtomicLong m_hnsTime = new AtomicLong();

    /// <summary>
    /// 时间，单位：100纳秒
    /// </summary>
    public long Time
    {
        get => m_hnsTime.Value;
        set => m_hnsTime.Value = value;
    }

    protected AtomicLong m_hnsDuration = new AtomicLong();
    /// <summary>
    /// 时长，单位：100纳秒
    /// </summary>
    public long Duration
    {
        get => m_hnsDuration.Value;
        set => m_hnsDuration.Value = value;
    }

    protected readonly AtomicBoolean _isDisposed = new AtomicBoolean(false);

    protected virtual void Dispose(bool disposeManageBuffer)
    {
        if (disposeManageBuffer) {

        }
    }

    ~Sample()
    {
        if (_isDisposed == false) {
            Dispose(disposeManageBuffer: false);
            _isDisposed.Value = true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed == false) {
            Dispose(disposeManageBuffer: true);
            _isDisposed.Value = true;
        }
        GC.SuppressFinalize(this);
    }
}
