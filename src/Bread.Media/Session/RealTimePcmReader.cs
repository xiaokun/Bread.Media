using System.Collections.Concurrent;
using System.Threading;
using Bread.Utility.Threading;

namespace Bread.Media;

public class RealTimePcmReader : MediaWorker, IListener
{
    public event Action? Completed;

    public bool IsCompleted { get; private set; } = false;

    public AudioEncodeParams? AudioInfo => Reader?.GetAudioParams();

    public double Duration => Reader?.Source?.Duration ?? 0;


    /// <summary>
    /// 当前帧处理完后的时间
    /// </summary>
    public double Time { get; set; } = 0;

    AudioReader Reader;
    readonly Action<AudioSample> _processor;

    public RealTimePcmReader(string input, Action<AudioSample> processor, AudioInfo? info = null) :
        base("RealTimePcmReader")
    {
        _processor = processor;
        Reader = new AudioReader(input, info);
        Reader.Open();
        if (Reader.IsOpened == false) {
            return;
        }

        Reader.ConnectTo(this);
    }

    protected override bool Start()
    {
        if (Reader.IsOpened == false) return false;

        IsCompleted = false;
        Reader.StartAsync().Wait();
        return true;
    }

    protected override void Stop()
    {
        Reader?.StopAsync().Wait();
    }

    ConcurrentQueue<Pooled<AudioSample>> queues = new();

    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (Reader.IsOpened == false) {
            return 30;
        }

        if (queues.Count > 0) {
            if (queues.TryDequeue(out Pooled<AudioSample>? sample)) {
                _processor(sample.Data);
                Time = sample.Data.Time / (double)TimeSpan.TicksPerSecond;
                sample.Dispose();
                return 0;
            }
        }
        else {
            if (_preCompleted.Value) {
                IsCompleted = true;
                Completed?.Invoke();
                MarkAsStop();
                return 0;
            }
        }
        return 20;
    }


    private readonly AtomicBoolean _preCompleted = new(false);
    public bool QueueEvent(object data)
    {
        if (data is Pooled<AudioSample> sample && _processor != null) {
            if (queues.Count >= Constants.VideoCacheCount) {
                return false;
            }
            queues.Enqueue(sample.Clone());
            return true;
        }
        else if (data is EndOfStreamException) {
            _preCompleted.Value = true;
            return true;
        }
        else {
            return Post(data);
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        Reader?.Dispose();

        if (queues != null) {
            while (queues.Count > 0) {
                if (queues.TryDequeue(out Pooled<AudioSample>? sample)) {
                    sample.Dispose();
                }
            }
        }
    }
}
