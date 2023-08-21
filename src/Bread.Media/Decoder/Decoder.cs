using System.Collections.Concurrent;
using Bread.Utility.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

internal unsafe abstract class Decoder<T> : IDisposable where T : Sample
{
    public bool IsInitialized => _isInited.Value;

    protected readonly AtomicBoolean disposedValue = new AtomicBoolean(false);
    protected readonly AtomicBoolean _isInited = new AtomicBoolean(false);
    protected ConcurrentQueue<Pooled<T>> _queue;

    public Decoder()
    {
        _queue = new();
    }

    /// <summary>
    /// 初始化解码器
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public abstract bool Initialize(AVCodecParameters* args);

    /// <summary>
    /// Decode the packet
    /// </summary>
    /// <param name="pkt">pkt get from ffmpeg</param>
    /// <returns>true if scucessd. otherwise false.</returns>
    public abstract bool Decode(AVPacket* pkt);

    /// <summary>
    /// Get decoded sample
    /// </summary>
    /// <returns>return null if no sample in queue.</returns>
    public Pooled<T>? GetSample()
    {
        if (_queue.TryDequeue(out Pooled<T>? pSample)) {
            return pSample;
        }
        return null;
    }

    /// <summary>
    /// flush the decoder
    /// </summary>
    public virtual void Flush()
    {
        while (_queue.TryDequeue(out Pooled<T>? sample)) {
            sample.Dispose();
        }
    }


    protected virtual void Dispose(bool disposeManaged)
    {
        while (_queue.TryDequeue(out Pooled<T>? sample)) {
            sample.Dispose();
        }
        _queue.Clear();
    }

    ~Decoder()
    {
        if (disposedValue == false) {
            Dispose(disposeManaged: false);
            disposedValue.Value = true;
        }
    }

    public void Dispose()
    {
        if (disposedValue == false) {
            Dispose(disposeManaged: true);
            GC.SuppressFinalize(this);
            disposedValue.Value = true;
        }
    }
}
