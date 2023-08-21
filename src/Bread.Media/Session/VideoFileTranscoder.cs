using System.Collections.Concurrent;
using System.Threading;
using Bread.Utility.Threading;

namespace Bread.Media;

public class VideoFileTranscoder : MediaWorker, IListener
{
    public event Action? Completed;

    public bool IsOpened { get; private set; } = false;

    public bool IsCompleted { get; private set; } = false;

    SourceReader<VideoSample> Reader;
    Encoder<VideoSample>? VideoEncoder;
    Encoder<AudioSample>? AudioEncoder;

    FileSinker? Sinker;
    IProcessor? VideoProcessor;

    public VideoFileTranscoder(string input, string output, IProcessor? videoprocessor = null) :
        base("VideoFileProcessor")
    {
        Reader = new SourceReader<VideoSample>(input, new FFVideoDecoder(), new FFAudioDecoder());
        Reader.Open();
        if (Reader.IsOpened == false) {
            return;
        }

        var video = Reader.GetVideoParams();
        var audio = Reader.GetAudioParams();
        if (video.BitRate < video.Width * video.Height * 3) {
            video.BitRate = (long)(video.Width * video.Height * 3);
        }
        VideoEncoder = new FFVideoEncoder(video);
        AudioEncoder = new FFAudioEncoder(audio);

        var ps = new FFSinkParams((VideoEncoder as FFVideoEncoder)!.Params, (AudioEncoder as FFAudioEncoder)!.Params);
        Sinker = new FileSinker(output, ps);
        VideoProcessor = videoprocessor;

        Reader.ConnectTo(this);

        if (VideoEncoder != null) this.ConnectTo(VideoEncoder);
        if (AudioEncoder != null) this.ConnectTo(AudioEncoder);
        if (VideoEncoder != null) VideoEncoder.ConnectTo(Sinker);
        if (AudioEncoder != null) AudioEncoder.ConnectTo(Sinker);

        IsOpened = true;
    }

    protected override bool Start()
    {
        if (Reader.IsOpened == false) return false;

        IsCompleted = false;
        Sinker!.StartAsync().Wait();
        if (VideoEncoder != null) VideoEncoder.StartAsync().Wait();
        if (AudioEncoder != null) AudioEncoder.StartAsync().Wait();
        Reader.StartAsync().Wait();
        return true;
    }

    protected override void Stop()
    {
        Reader?.StopAsync().Wait();
        AudioEncoder?.StopAsync().Wait();
        VideoEncoder?.StopAsync().Wait();
        Sinker?.StopAsync().Wait();
    }

    ConcurrentQueue<Pooled<VideoSample>> queues = new ConcurrentQueue<Pooled<VideoSample>>();

    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (queues.Count > 0) {
            if (queues.TryDequeue(out Pooled<VideoSample>? sample)) {
                VideoProcessor?.Process(sample.Data);
                if (Post(sample) == false) {
                    MarkAsStop();
                    Log.Error("Post must be successfull.");
                }
                sample.Dispose();
                return 0;
            }
        }
        else {
            if (_preCompleted.Value) {

                if (AudioEncoder != null && AudioEncoder.IsEncoding) return 20;
                if (VideoEncoder != null && VideoEncoder.IsEncoding) return 20;
                if (Sinker!.IsSinking) return 20;

                IsCompleted = true;
                MarkAsStop();
                Completed?.Invoke();
                return 0;
            }
        }
        return 20;
    }


    private readonly AtomicBoolean _preCompleted = new AtomicBoolean(false);
    public bool QueueEvent(object data)
    {
        if (data is Pooled<VideoSample> sample && VideoProcessor != null) {
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
        VideoEncoder?.Dispose();
        VideoEncoder = null;
        AudioEncoder?.Dispose();
        AudioEncoder = null;
        VideoProcessor?.Dispose();
        VideoProcessor = null;

        if (queues != null) {
            while (queues.Count > 0) {
                if (queues.TryDequeue(out Pooled<VideoSample>? sample)) {
                    sample.Dispose();
                }
            }
            queues.Clear();
        }
    }
}
