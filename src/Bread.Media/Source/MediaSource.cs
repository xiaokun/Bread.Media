using System.ComponentModel;
using Bread.Utility.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

/// <summary>
/// Read compressed data from stream
/// </summary>
internal unsafe class MediaSource : IDisposable, INotifyPropertyChanged
{
    #region Properties

    public string Uri { get; private set; } = string.Empty;

    public bool IsNetStream { get; private set; } = false;

    readonly AtomicBoolean _isValid = new AtomicBoolean(false);
    public bool IsValid
    {
        get => _isValid.Value;
        set {
            if (_isValid == value) return;
            _isValid.Value = value;
            OnPropertyChanged(nameof(IsValid));
        }
    }

    readonly AtomicBoolean _isTimeOut = new AtomicBoolean(false);
    public bool IsTimeOut
    {
        get => _isTimeOut.Value;
        set {
            if (_isTimeOut == value) return;
            _isTimeOut.Value = value;
            OnPropertyChanged(nameof(IsTimeOut));
        }
    }

    readonly AtomicBoolean _isEndofStream = new AtomicBoolean(false);
    public bool IsEndOfStream
    {
        get => _isEndofStream.Value;
        set {
            if (_isEndofStream == value) return;
            _isEndofStream.Value = value;
            OnPropertyChanged(nameof(IsEndOfStream));
        }
    }

    readonly AtomicBoolean _hasVideo = new AtomicBoolean(false);
    public bool HasVideo
    {
        get => _hasVideo.Value;
        set {
            if (_hasVideo == value) return;
            _hasVideo.Value = value;
            OnPropertyChanged(nameof(HasVideo));
        }
    }

    readonly AtomicBoolean _hasAduio = new AtomicBoolean(false);
    public bool HasAudio
    {
        get => _hasAduio.Value;
        set {
            if (_hasAduio == value) return;
            _hasAduio.Value = value;
            OnPropertyChanged(nameof(HasAudio));
        }
    }

    readonly AtomicInteger _videoCodecId = new AtomicInteger(0);
    public int VideoCodecId
    {
        get => _videoCodecId.Value;
        set {
            if (_videoCodecId == value) return;
            _videoCodecId.Value = value;
            OnPropertyChanged(nameof(VideoCodecId));
        }
    }

    readonly AtomicInteger _profile = new AtomicInteger(0);
    public int Profile
    {
        get => _profile.Value;
        set {
            if (_profile == value) return;
            _profile.Value = value;
            OnPropertyChanged(nameof(Profile));
        }
    }

    readonly AtomicInteger _level = new AtomicInteger(0);
    public int Level
    {
        get => _level.Value;
        set {
            if (_level == value) return;
            _level.Value = value;
            OnPropertyChanged(nameof(Level));
        }
    }


    readonly AtomicDouble _duration = new AtomicDouble(0);
    public double Duration
    {
        get => _duration.Value;
        set {
            if (Math.Abs(_duration.Value - value) < 0.00001) return;
            _duration.Value = value;
            OnPropertyChanged(nameof(Duration));
        }
    }


    readonly AtomicInteger _width = new AtomicInteger(0);
    public int Width
    {
        get => _width.Value;
        set {
            if (_width == value) return;
            _width.Value = value;
            OnPropertyChanged(nameof(Width));
        }
    }

    readonly AtomicInteger _height = new AtomicInteger(0);
    public int Height
    {
        get => _height.Value;
        set {
            if (_height == value) return;
            _height.Value = value;
            OnPropertyChanged(nameof(Height));
        }
    }


    readonly AtomicInteger _framerateNum = new AtomicInteger(0);
    readonly AtomicInteger _framerateDen = new AtomicInteger(0);
    public VideoFrameRate FrameRate
    {
        get => new VideoFrameRate(_framerateNum.Value, _framerateDen.Value);
        set {
            if (value.Num == _framerateNum.Value && value.Den == _framerateDen.Value) return;
            _framerateDen.Value = value.Den;
            _framerateNum.Value = value.Num;
            OnPropertyChanged(nameof(FrameRate));
        }
    }

    readonly AtomicInteger _videoFormat = new AtomicInteger(0);
    public VideoSampleFormat VideoFormat
    {
        get => (VideoSampleFormat)_videoFormat.Value;
        set {
            if (_videoFormat == (int)value) return;
            _videoFormat.Value = (int)value;
            OnPropertyChanged(nameof(VideoFormat));
        }
    }

    readonly AtomicInteger _videoBitrate = new AtomicInteger(0);
    public int VideoBitrate
    {
        get => _videoBitrate.Value;
        set {
            if (_videoBitrate == value) return;
            _videoBitrate.Value = value;
            OnPropertyChanged(nameof(VideoBitrate));
        }
    }

    readonly AtomicInteger _audioCodecId = new AtomicInteger(0);
    public int AudioCodecId
    {
        get => _audioCodecId.Value;
        set {
            if (_audioCodecId == value) return;
            _audioCodecId.Value = value;
            OnPropertyChanged(nameof(AudioCodecId));
        }
    }

    readonly AtomicInteger _sampleRate = new AtomicInteger(0);
    public int SampleRate
    {
        get => _sampleRate.Value;
        set {
            if (_sampleRate == value) return;
            _sampleRate.Value = value;
            OnPropertyChanged(nameof(SampleRate));
        }
    }

    readonly AtomicInteger _channelCount = new AtomicInteger(0);
    public int ChannelCount
    {
        get => _channelCount.Value;
        set {
            if (_channelCount == value) return;
            _channelCount.Value = value;
            OnPropertyChanged(nameof(ChannelCount));
        }
    }

    readonly AtomicInteger _audioBitrate = new AtomicInteger(0);
    public int AudioBitrate
    {
        get => _audioBitrate.Value;
        set {
            if (_audioBitrate == value) return;
            _audioBitrate.Value = value;
            OnPropertyChanged(nameof(AudioBitrate));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    #endregion


    internal AVCodecParameters* AudioParameters = null;
    internal AVCodecParameters* VideoParameters = null;

    #region private

    int VideoIndex = -1;  // video stream index
    int AudioIndex = -1;  // audio stream index

    DateTime ReadTime = DateTime.MinValue; //帧数据读取时间，用于监控超时

    AVFormatContext* FormatContext = null;
    readonly AVIOInterruptCB_callback InterruptCallback;
    StreamType StreamType = StreamType.Media;
    private readonly AtomicBoolean disposedValue = new AtomicBoolean(false);

    #endregion

    public MediaSource(string uri, StreamType streamType = StreamType.Media)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentNullException("Uri is null or empty.");

        Uri = uri;
        if (uri.StartsWith("rtsp") || uri.StartsWith("rtmp") ||
            uri.StartsWith("http")) {
            IsNetStream = true;
        }

        StreamType = streamType;
        InterruptCallback = Callback;
    }


    private int Callback(void* data)
    {
        return 0;
    }


    public bool Open()
    {
        if (FormatContext != null) {
            var f = FormatContext;
            ffmpeg.avformat_close_input(&f);
            ffmpeg.avformat_free_context(FormatContext);
            FormatContext = null;
        }

        var format = ffmpeg.avformat_alloc_context();
        if (format == null) {
            Log.Error("avcodec_find_decoder: null", Aspects.FFmpeg);
            return false;
        }
        FormatContext = format;

        FormatContext->interrupt_callback.callback = InterruptCallback;
        FormatContext->interrupt_callback.opaque = FormatContext;
        //TODO: rtsp rtmp read 2mb 5 seconds data.

        int result = ffmpeg.avformat_open_input(&format, Uri, null, null);
        if (result < 0) {

            Log.Error($"avformat_open_input:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        result = ffmpeg.avformat_find_stream_info(FormatContext, null);
        if (result < 0) {
            Log.Error($"avformat_find_stream_info:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        if ((StreamType & StreamType.Video) == StreamType.Video) InitVideo();
        if ((StreamType & StreamType.Audio) == StreamType.Audio) InitAudio();

        IsValid = true;
        return true;
    }


    private void InitVideo()
    {
        VideoIndex = ffmpeg.av_find_best_stream(FormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        if (VideoIndex < 0) return;

        var stream = FormatContext->streams[VideoIndex];
        var ps = stream->codecpar;
        VideoParameters = ps;

        if (stream->duration != 0) {
            Duration = stream->duration / (double)stream->time_base.den;
        }

        var rate = ffmpeg.av_guess_frame_rate(FormatContext, stream, null);
        _framerateNum.Value = rate.num;
        _framerateDen.Value = rate.den;

        VideoCodecId = (int)ps->codec_id;
        Profile = ps->profile;
        Level = ps->level;
        Width = ps->width;
        Height = ps->height;
        VideoBitrate = (int)ps->bit_rate;
    }


    private void InitAudio()
    {
        AudioIndex = ffmpeg.av_find_best_stream(FormatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
        if (AudioIndex < 0) return;

        var stream = FormatContext->streams[AudioIndex];
        var ps = stream->codecpar;
        AudioParameters = ps;

        if (stream->duration != 0 && Duration < 0.00001) {
            Duration = stream->duration / (double)stream->time_base.den;
        }

        AudioCodecId = (int)ps->codec_id;
        SampleRate = ps->sample_rate;
        ChannelCount = ps->ch_layout.nb_channels;
        AudioBitrate = (int)ps->bit_rate;
    }

    /// <summary>
    /// Read next AVPacket from stream.
    /// </summary>
    public bool ReadNext(AVPacket* pkt, ref bool isVideo)
    {
        int result = ffmpeg.av_read_frame(FormatContext, pkt);
        if (result == ffmpeg.AVERROR_EOF) {
            IsEndOfStream = true;
            throw new EndOfStreamException();
        }

        if (result < 0) {
            Log.Error($"av_read_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        if (pkt->stream_index == VideoIndex) {
            isVideo = true;
            ReadTime = DateTime.Now;
            return true;
        }

        if (pkt->stream_index == AudioIndex) {
            isVideo = false;
            ReadTime = DateTime.Now;
            return true;
        }

        return false;
    }


    public void Close()
    {
        if (FormatContext != null) {
            var format = FormatContext;
            ffmpeg.avformat_close_input(&format);
            FormatContext = null;
        }

        VideoIndex = -1;
        AudioIndex = -1;
    }

    protected void OnPropertyChanged(string name)
    {
        if (PropertyChanged != null) {
            PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue.Value) {
            Close();
            disposedValue.Value = true;
        }
    }

    ~MediaSource()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
