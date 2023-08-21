using System.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

internal unsafe class SourceReader<TVideoSample> : MediaWorker where TVideoSample : VideoSampleBase
{
    public MediaSource Source { get; private set; }

    public bool IsOpened { get; private set; } = false;

    /// <summary>
    /// true will Read sample according system time. otherwise read as quickly as possible.
    /// </summary>
    public bool RealTime { get; set; } = true;

    public Decoder<TVideoSample>? VideoDecoder { get; private set; }

    public Decoder<AudioSample>? AudioDecoder { get; private set; }

    int TryCreateCount = 0;
    long hnsVideoTime = 0;
    long hnsAudioTime = 0;
    long hnsVideoDuration = 0;
    long hnsAudioDuration = 0;

    DateTime startTime = DateTime.MinValue;
    Queue<Pooled<TVideoSample>> VideoSampleList = new Queue<Pooled<TVideoSample>>();
    Queue<Pooled<AudioSample>> AudioSampleList = new Queue<Pooled<AudioSample>>();

    public SourceReader(string url, Decoder<TVideoSample>? videoDecoder,
        Decoder<AudioSample>? audiodecoder, StreamType type = StreamType.Media)
        : base(nameof(SourceReader<TVideoSample>))
    {
        VideoDecoder = videoDecoder;
        AudioDecoder = audiodecoder;
        Source = new MediaSource(url, type);
    }

    /// <summary>
    /// open the source, and init all decoders.
    /// </summary>
    public void Open()
    {
        if (IsOpened) return;

        var success = Source.Open();
        if (success == false) return;

        if (VideoDecoder != null) {
            if (VideoDecoder.IsInitialized == false) {
                VideoDecoder.Initialize(Source.VideoParameters);
            }
        }

        if (AudioDecoder != null) {
            if (AudioDecoder.IsInitialized == false) {
                AudioDecoder.Initialize(Source.AudioParameters);
            }
        }

        var rate = Source.FrameRate;
        if (rate.Num != 0) {
            hnsVideoDuration = (long)((rate.Den / (double)rate.Num) * TimeSpan.TicksPerSecond + 0.5);
        }
        hnsAudioDuration = (long)(Constants.AudioSampleLength / (double)Source.SampleRate * TimeSpan.TicksPerSecond + 0.5);

        IsOpened = true;
    }

    public VideoEncodeParams GetVideoParams()
    {
        if (IsOpened == false)
            throw new InvalidOperationException("SourceReader has not opened.");

        return new VideoEncodeParams() {
            Width = Source.Width,
            Height = Source.Height,
            FrameRate = Source.FrameRate,
            BitRate = Source.VideoBitrate,
            CodeId = (AVCodecID)Source.VideoCodecId,
            Profile = Source.Profile,
            Level = Source.Level,
        };
    }

    public AudioEncodeParams GetAudioParams()
    {
        if (IsOpened == false)
            throw new InvalidOperationException("SourceReader has not opened.");

        return new AudioEncodeParams() {
            SampleRate = Source.SampleRate,
            ChannelCount = Source.ChannelCount,
            BitRate = Source.AudioBitrate,
            CodeId = (AVCodecID)Source.AudioCodecId,
        };
    }


    protected override bool Start()
    {
        if (base.Start() == false) return false;
        startTime = DateTime.MinValue;
        if (IsOpened == false) {
            Open();
        }
        return true;
    }

    protected override int ExecuteCycleLogic(CancellationToken ct)
    {
        if (IsOpened == false) {
            if (Source.IsNetStream == false) {
                MarkAsStop();
                return 0;
            }

            if (TryCreateCount > 20) {
                Log.Error($"{Name} init exceed max attempts count.");
                MarkAsStop();
                return 0;
            }

            TryCreateCount++;
            Source.Close();
            Open();
            return IsOpened ? 20 : 200;
        }

        if (startTime == DateTime.MinValue) {
            startTime = DateTime.Now;
            hnsAudioTime = 0;
            hnsVideoTime = 0;
        }

        if (VideoSampleList.Count >= Constants.VideoCacheCount ||
            AudioSampleList.Count >= Constants.AudioCacheCount) {
            if (RealTime) PostSamplesRealTime();
            else PostSamples();
            return 15;
        }

        var pkt = ffmpeg.av_packet_alloc();
        bool isVideo = false;

        try {
            bool success = Source.ReadNext(pkt, ref isVideo);
            if (success == false) {
                ffmpeg.av_packet_free(&pkt);
                return 10;
            }
        }
        catch (EndOfStreamException ex) {
            Post(ex);
            MarkAsStop();
            return 0;
        }

        Decode(pkt, isVideo);

        if (RealTime) PostSamplesRealTime();
        else PostSamples();

        ffmpeg.av_packet_free(&pkt);

        if (VideoSampleList.Count >= Constants.VideoCacheCount ||
            AudioSampleList.Count >= Constants.AudioCacheCount) {
            return 15;
        }
        return 0;
    }


    private void PostSamplesRealTime()
    {
        var time = DateTime.Now;
        while ((time - startTime).Ticks > hnsAudioTime) {
            if (AudioSampleList.Count == 0) break;

            var sample = AudioSampleList.Peek();
            sample.Data.Time = hnsAudioTime;
            if (Post(sample) == false) {
                break;
            }

            sample.Data.Duration = hnsAudioDuration;
            hnsAudioTime += sample.Data.Duration;
            AudioSampleList.Dequeue().Dispose();
        }

        while ((time - startTime).Ticks > hnsVideoTime) {
            if (VideoSampleList.Count == 0) break;

            var sample = VideoSampleList.Peek();
            sample.Data.Time = hnsVideoTime;
            if (Post(sample) == false) {
                break;
            }

            sample.Data.Duration = hnsVideoDuration;
            hnsVideoTime += sample.Data.Duration;
            VideoSampleList.Dequeue().Dispose();
        }
    }


    private void PostSamples()
    {
        while (AudioSampleList.Count > 0) {

            var sample = AudioSampleList.Peek();
            sample.Data.Time = hnsAudioTime;
            if (Post(sample) == false) {
                break;
            }

            sample.Data.Duration = hnsAudioDuration;
            hnsAudioTime += sample.Data.Duration;
            AudioSampleList.Dequeue().Dispose();
        }

        while (VideoSampleList.Count > 0) {

            var sample = VideoSampleList.Peek();
            sample.Data.Time = hnsVideoTime;
            if (Post(sample) == false) {
                break;
            }

            sample.Data.Duration = hnsVideoDuration;
            hnsVideoTime += sample.Data.Duration;
            VideoSampleList.Dequeue().Dispose();
        }
    }


    private void Decode(AVPacket* pkt, bool isVideo)
    {
        if (isVideo) {
            if (VideoDecoder != null) {
                VideoDecoder.Decode(pkt);
                while (true) {
                    var sample = VideoDecoder.GetSample();
                    if (sample == null) break;
                    VideoSampleList.Enqueue(sample);
                }
            }
        }
        else {
            if (AudioDecoder != null) {
                AudioDecoder.Decode(pkt);
                while (true) {
                    var sample = AudioDecoder.GetSample();
                    if (sample == null) break;
                    AudioSampleList.Enqueue(sample);
                }
            }
        }
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        Source.Dispose();
        VideoDecoder?.Dispose();
        AudioDecoder?.Dispose();

        foreach (var sample in VideoSampleList) {
            sample.Dispose();
        }
        VideoSampleList.Clear();

        foreach (var sample in AudioSampleList) {
            sample.Dispose();
        }
        AudioSampleList.Clear();
    }

}
