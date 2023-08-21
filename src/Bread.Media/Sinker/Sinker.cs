using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bread.Utility.Threading;

namespace Bread.Media;

internal unsafe abstract class Sinker : MediaWorker, IListener
{
    public bool IsInitialized => _isInited.Value;

    public string Uri { get; private set; }

    public bool IsSinking => _isInited.Value && SampleList.Count > 0;

    protected readonly AtomicBoolean _isInited = new AtomicBoolean(false);
    private readonly ConcurrentQueue<PacketSample> SampleList;

    public Sinker(string uri, string name) : base(name)
    {
        Uri = uri;
        SampleList = new ConcurrentQueue<PacketSample>();
    }

    protected abstract bool Process(PacketSample sample);

    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (SampleList.Count == 0) {
            return 10;
        }

        if (SampleList.TryPeek(out PacketSample? sample)) {
            if (sample == null) return 20;

            bool success = Process(sample);
            if (success == false) {
                //TODO: count the error, then reinit sinker
                Log.Error($"{Name} process sample fail.");
                return 10;
            }

            while (true) {
                if (SampleList.TryDequeue(out PacketSample? sample1)) {
                    sample1.Dispose();
                    break;
                }
                else {
                    Task.Delay(3).Wait();
                    Log.Error("Sink SampleList TryDequeue sample fail. Delay 10ms.");
                }
            }
            return 0;
        }
        return 10;
    }

    public bool QueueEvent(object data)
    {
        if (data is PacketSample sample) {
            if (sample.IsAudio) {
                if (SampleList.Count >= Constants.AudioCacheCount)
                    return false;
            }
            else {
                if (SampleList.Count >= Constants.VideoCacheCount) {
                    return false;
                }
            }
            SampleList.Enqueue(sample);
            return true;
        }
        return false;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        while (SampleList.Count > 0) {
            if (SampleList.TryDequeue(out PacketSample? sample)) {
                sample.Dispose();
            }
        }
    }

}
