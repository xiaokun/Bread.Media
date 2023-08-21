using System.Collections.Concurrent;
using System.Threading;
using Bread.Utility.Threading;

namespace Bread.Media;

public class VideoReader : MediaWorker, IListener
{
    public event Action? Completed;

    public bool IsCompleted { get; private set; } = false;

    public VideoEncodeParams? VideoInfo => Reader?.GetVideoParams();

    public double Duration => Reader?.Source?.Duration ?? 0;

    SourceReader<VideoSample> Reader;
    Action<VideoSample> _processor;

    public VideoReader(string input, Action<VideoSample> videoprocessor) :
        base("VideoReader")
    {
        _processor = videoprocessor;

        Reader = new SourceReader<VideoSample>(input, new FFVideoDecoder(), null);
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

    ConcurrentQueue<Pooled<VideoSample>> queues = new ConcurrentQueue<Pooled<VideoSample>>();

    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (queues.Count > 0) {
            if (queues.TryDequeue(out Pooled<VideoSample>? sample)) {
                _processor?.Invoke(sample.Data);
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


    private readonly AtomicBoolean _preCompleted = new AtomicBoolean(false);
    public bool QueueEvent(object data)
    {
        if (data is Pooled<VideoSample> sample && _processor != null) {
            if (queues.Count >= Constants.VideoCacheCount) {
                return false;
            }
            queues.Enqueue(sample.Clone());
            return true;
        }
        else if (data is EndOfStreamException ex) {
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
                if (queues.TryDequeue(out Pooled<VideoSample>? sample)) {
                    sample.Dispose();
                }
            }
        }
        queues?.Clear();
    }


}
