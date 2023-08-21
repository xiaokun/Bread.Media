using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bread.Utility.Threading;

namespace Bread.Media;

internal unsafe abstract class Encoder<TSample> : MediaWorker, IListener where TSample : Sample
{
    public bool IsInitialized => _isInited.Value;

    public bool IsEncoding => _isInited.Value && SampleList.Count > 0;

    protected readonly AtomicBoolean _isInited = new AtomicBoolean(false);
    private readonly ConcurrentQueue<Pooled<TSample>> SampleList;

    public Encoder(string name) : base(name)
    {
        SampleList = new ConcurrentQueue<Pooled<TSample>>();
    }

    /// <summary>
    /// init the encoder, or reinit it after fatal error especially when device lost.
    /// </summary>
    /// <returns></returns>
    protected abstract bool Initialize();

    /// <summary>
    /// deinit the encoder.
    /// </summary>
    /// <returns></returns>
    protected abstract void DeInitialize();


    public bool QueueEvent(object e)
    {
        if (e is Pooled<TSample> sample) {
            if (sample.Data is VideoSampleBase) {
                if (SampleList.Count >= Constants.VideoCacheCount)
                    return false;
            }
            else if (sample.Data is AudioSample) {
                if (SampleList.Count >= Constants.AudioCacheCount)
                    return false;
            }
            SampleList.Enqueue(sample.Clone());
            return true;
        }
        return false;
    }

    /// <summary>
    /// encode the sample.
    /// </summary>
    /// <param name="sample"> the raw uncompressed sample.</param>
    /// <param name="ct">cancel token.</param>
    protected abstract bool Encode(TSample sample, CancellationToken ct);


    private int _initCount = 0; // 重试次数
    private Stopwatch _sw = new Stopwatch();
    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return 0;

        if (SampleList.Count == 0) {
            return 5;
        }

        if (_isInited == false) {
            if (_initCount > 10) {
                throw new InvalidOperationException($"{Name} failed to initializing after 10 time try again.");
            }

            _isInited.Value = Initialize();
            if (_isInited == false) {
                DeInitialize();
                _initCount++;
                return 20;
            }
        }

        _initCount = 0;

        if (SampleList.TryPeek(out Pooled<TSample>? sample)) {

            if (sample == null) return 10;

            bool success = false;
            _sw.Restart();
            {
                try {
                    success = Encode(sample.Data, ct);
                }
                catch (DeviceLostException) {
                    DeInitialize();
                }
                catch (DeviceHangUpException) {
                    DeInitialize();
                    Task.Delay(100).Wait();
                }
                catch (Exception ex) {
                    Log.Exception(ex);
                }
            }
            _sw.Stop();

            //if (Name == nameof(FFVideoEncoder)) {
            //    Log.Info($"{Name} encode one sample use time: {_sw.ElapsedMilliseconds}");
            //}

            if (success == false) {
                Log.Error("Encode one sample fail.");
                return 10;
            }

            while (true) {
                if (SampleList.TryDequeue(out Pooled<TSample>? sample1)) {
                    sample1.Dispose();
                    break;
                }
                else {
                    Task.Delay(5).Wait();
                    Log.Error("Sink SampleList TryDequeue sample fail. Delay 10ms.");
                }
            }
            return 0;
        }
        return 5;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        while (SampleList.Count > 0) {
            if (SampleList.TryDequeue(out Pooled<TSample>? sample)) {
                sample.Dispose();
            }
            else {
                Task.Delay(5).Wait();
            }
        }

    }

}
