using FFmpeg.AutoGen;

namespace Bread.Media;


internal abstract unsafe class FFSinker : Sinker
{
    protected AVFormatContext* m_pFormatCtx = null;

    protected int m_nVideoFrameCount = 0;
    protected int m_nAudioFrameCount = 0;
    protected int m_nVideoStreamIndex = 0;
    protected int m_nAudioStreamIndex = 1;

    VideoFrameRate m_frameRate;

    public FFSinker(string uri, FFSinkParams args, string? formatname, string classname) : base(uri, classname)
    {
        AVFormatContext* context = null;
        int result = ffmpeg.avformat_alloc_output_context2(&context, null, formatname, uri);
        if (result < 0) {
            Log.Error($"avformat_alloc_output_context2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return;
        }

        bool success = false;
        if (args.Video != null && args.Video.Width != 0) {
            success = Initialize(context, args, true);
        }
        if (args.Audio != null && args.Audio.SampleRate != 0) {
            success |= Initialize(context, args, false);
        }
        _isInited.Value = success;

        if (success)
            m_pFormatCtx = context;
    }


    private bool Initialize(AVFormatContext* context, FFSinkParams args, bool isVideo)
    {
        AVCodecContext* pContext = null;
        var pCodec = ffmpeg.avcodec_find_encoder(isVideo ? args.Video.CodeId : args.Audio.CodeId);
        if (pCodec == null) {
            Log.Error($"avcodec_find_decoder:null", Aspects.FFmpeg);
            return false;
        }

        pContext = ffmpeg.avcodec_alloc_context3(pCodec);
        if (pContext == null) {
            Log.Error($"avcodec_alloc_context3:null", Aspects.FFmpeg);
            return false;
        }

        if (isVideo) {
            pContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            pContext->codec_id = args.Video.CodeId;
            pContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            pContext->profile = args.Video.Profile;
            pContext->level = args.Video.Level;
            pContext->width = args.Video.Width;
            pContext->height = args.Video.Height;
            pContext->bit_rate = args.Video.BitRate;
            pContext->bits_per_raw_sample = 8;
            pContext->time_base.den = args.Video.FrameRate.Num;
            pContext->time_base.num = args.Video.FrameRate.Den;
            pContext->gop_size = (int)(args.Video.FrameRate.Value + 0.5);
            pContext->framerate = new AVRational() { num = args.Video.FrameRate.Num, den = args.Video.FrameRate.Den };
            pContext->max_b_frames = 0;
            pContext->thread_count = 1;
            pContext->chroma_sample_location = AVChromaLocation.AVCHROMA_LOC_LEFT;
            pContext->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            pContext->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
            pContext->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
            pContext->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            pContext->field_order = AVFieldOrder.AV_FIELD_PROGRESSIVE;
            pContext->extradata_size = args.Video.BufferLength;
            pContext->extradata = (byte*)args.Video.PPSSPSBufer.ToPointer();
            pContext->sample_aspect_ratio = new AVRational() { den = 1, num = 1 };
        }
        else {
            pContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            pContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            pContext->codec_id = args.Audio.CodeId;
            pContext->sample_rate = args.Audio.SampleRate;
            ffmpeg.av_channel_layout_default(&pContext->ch_layout, args.Audio.ChannelCount);
            pContext->ch_layout.nb_channels = args.Audio.ChannelCount;
            pContext->bit_rate = args.Audio.BitRate;
            pContext->time_base.den = args.Audio.SampleRate;
            pContext->time_base.num = 1;
            pContext->frame_size = Constants.AudioSampleLength;
        }

        if ((context->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) == ffmpeg.AVFMT_GLOBALHEADER) {
            pContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        }

        int result = ffmpeg.avcodec_open2(pContext, pCodec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        AVStream* stream = ffmpeg.avformat_new_stream(context, null);
        if (stream == null) {
            Log.Error($"avformat_new_stream return null.", Aspects.FFmpeg);
            return false;
        }

        stream->id = (int)(context->nb_streams - 1);
        if (isVideo) m_nVideoStreamIndex = stream->id;
        else m_nAudioStreamIndex = stream->id;

        result = ffmpeg.avcodec_parameters_from_context(stream->codecpar, pContext);
        if (result < 0) {
            Log.Error($"avcodec_parameters_from_context:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        if (isVideo) {
            pContext->extradata_size = args.Video.BufferLength;
            pContext->extradata = (byte*)args.Video.PPSSPSBufer.ToPointer();
            stream->time_base = new AVRational() { num = 1, den = Constants.VideoStreamTimebase };
            m_frameRate = args.Video.FrameRate;
        }
        else {
            stream->time_base = new AVRational() { num = 1, den = args.Audio.SampleRate };
        }

        //stream->codec = pContext;
        return true;
    }

    protected override bool Start()
    {
        if (base.Start() == false) return false;

        if (_isInited.Value == false) {
            Log.Error($"{Name} is not inited.");
        }

        m_nVideoFrameCount = 0;
        m_nAudioFrameCount = 0;

        ffmpeg.av_dump_format(m_pFormatCtx, 0, Uri, 1);

        if ((m_pFormatCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == ffmpeg.AVFMT_NOFILE) {
            ffmpeg.avformat_free_context(m_pFormatCtx);
            Log.Error($"format context oformat flags already has AVFMT_NOFILE flag", Aspects.FFmpeg);
            return false;
        }

        int result = ffmpeg.avio_open(&(m_pFormatCtx->pb), Uri, ffmpeg.AVIO_FLAG_WRITE);
        if (result < 0) {
            ffmpeg.avformat_free_context(m_pFormatCtx);
            Log.Error($"avio_open:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        result = ffmpeg.avformat_write_header(m_pFormatCtx, null);
        if (result < 0) {
            Log.Error($"{Name}\tavformat_write_header:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }
        return true;
    }

    protected override void Stop()
    {
        if (m_pFormatCtx != null) {
            var ctx = m_pFormatCtx;
            if (_isInited.Value) {
                ffmpeg.av_write_trailer(ctx);
            }

            ffmpeg.avformat_close_input(&ctx);
            ffmpeg.avformat_free_context(ctx);
            m_pFormatCtx = null;
        }
        base.Stop();
    }

    protected override bool Process(PacketSample sample)
    {
        var pkt = ffmpeg.av_packet_clone(sample.GetPointer());
        bool success = false;
        if (sample.IsAudio) success = ProcessAudio(pkt);
        else success = ProcessVideo(pkt);
        ffmpeg.av_packet_unref(pkt);
        return success;
    }


    private bool ProcessVideo(AVPacket* pkt)
    {
        pkt->stream_index = m_nVideoStreamIndex;
        pkt->pts = m_nVideoFrameCount;
        pkt->dts = m_nVideoFrameCount;
        pkt->duration = 1;
        m_nVideoFrameCount++;

        var stream = m_pFormatCtx->streams[m_nVideoStreamIndex];
        ffmpeg.av_packet_rescale_ts(pkt, new AVRational { num = m_frameRate.Den, den = m_frameRate.Num }, stream->time_base);

        int result = ffmpeg.av_interleaved_write_frame(m_pFormatCtx, pkt);
        if (result < 0) {
            Log.Error($"{Name}\tav_interleaved_write_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        //Log.Info("Write one frame");
        return true;
    }

    private bool ProcessAudio(AVPacket* pkt)
    {
        pkt->stream_index = m_nAudioStreamIndex;
        pkt->pts = m_nAudioFrameCount * Constants.AudioSampleLength;
        pkt->dts = m_nAudioFrameCount * Constants.AudioSampleLength;
        pkt->duration = Constants.AudioSampleLength;
        m_nAudioFrameCount++;

        int result = ffmpeg.av_interleaved_write_frame(m_pFormatCtx, pkt);
        if (result < 0) {
            Log.Error($"{Name}\tav_interleaved_write_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }
        return true;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();
        if (m_pFormatCtx != null) {
            ffmpeg.avformat_free_context(m_pFormatCtx);
            m_pFormatCtx = null;
        }
    }

}

internal class FileSinker : FFSinker
{
    public FileSinker(string uri, FFSinkParams args) : base(uri, args, null, "FileSinker")
    {
    }
}

internal class RtmpSinker : FFSinker
{
    public RtmpSinker(string uri, FFSinkParams args) : base(uri, args, "flv", "RtmpSinker")
    {
    }
}
