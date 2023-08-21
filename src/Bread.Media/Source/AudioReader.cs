using System.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

internal unsafe class AudioReader : MediaWorker
{
    public MediaSource Source { get; private set; }

    public bool IsOpened { get; private set; } = false;

    /// <summary>
    /// true will Read sample according system time. otherwise read as quickly as possible.
    /// </summary>
    public bool RealTime { get; set; } = true;

    public Decoder<AudioSample> AudioDecoder { get; private set; }

    int TryCreateCount = 0;
    long hnsAudioTime = 0;
    long hnsAudioDuration = 0;

    DateTime startTime = DateTime.MinValue;
    Queue<Pooled<AudioSample>> AudioSampleList = new Queue<Pooled<AudioSample>>();

    public AudioReader(string url, AudioInfo? output = null)
        : base(nameof(AudioReader))
    {
        AudioDecoder = new FFAudioDecoder(output);
        Source = new MediaSource(url, StreamType.Audio);
    }

    /// <summary>
    /// open the source, and init all decoders.
    /// </summary>
    public void Open()
    {
        if (IsOpened) return;

        var success = Source.Open();
        if (success == false) return;
        if (AudioDecoder.IsInitialized == false) {
            AudioDecoder.Initialize(Source.AudioParameters);
        }

        hnsAudioDuration = (long)(Constants.AudioSampleLength / (double)Source.SampleRate * TimeSpan.TicksPerSecond + 0.5);
        IsOpened = true;
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
        }

        if (AudioSampleList.Count >= Constants.AudioCacheCount) {
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

        if (isVideo == false) {
            Decode(pkt);
        }

        if (RealTime) PostSamplesRealTime();
        else PostSamples();

        ffmpeg.av_packet_free(&pkt);

        if (AudioSampleList.Count >= Constants.AudioCacheCount) {
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

            hnsAudioTime += sample.Data.Duration;
            AudioSampleList.Dequeue().Dispose();
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

            hnsAudioTime += sample.Data.Duration;
            AudioSampleList.Dequeue().Dispose();
        }
    }


    private void Decode(AVPacket* pkt)
    {
        AudioDecoder.Decode(pkt);
        while (true) {
            var sample = AudioDecoder.GetSample();
            if (sample == null) break;
            AudioSampleList.Enqueue(sample);
        }
    }


    protected override void OnDisposing()
    {
        base.OnDisposing();

        Source.Dispose();
        AudioDecoder.Dispose();

        foreach (var sample in AudioSampleList) {
            sample.Dispose();
        }
        AudioSampleList.Clear();
    }

}
