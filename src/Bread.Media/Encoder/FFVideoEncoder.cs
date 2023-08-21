using System.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

internal sealed unsafe class FFVideoEncoder : Encoder<VideoSample>
{
    public VideoEncodeParams Params => m_info;

    VideoEncodeParams m_info;
    AVFrame* SrcFrame = null;
    AVFrame* DstFrame = null;
    SwsContext* SwsCtx = null;
    AVCodecContext* CodecContext = null;
    AVBufferRef* HWDeviceCtx = null;

    int m_nVideoSampleCount = 0;

    public FFVideoEncoder(VideoEncodeParams info)
        : base(nameof(FFVideoEncoder))
    {
        m_info = info;

        if (info.Profile <= 0) {
            m_info.Profile = 77;
            m_info.Level = 41;
        }

        Initialize();
    }

    protected override bool Start()
    {
        return true;
    }

    protected override void Stop()
    {
        if (_isInited.Value == true) {
            Encode(null, default);
        }
        DeInitialize();
    }

    protected override bool Initialize()
    {
        InitHwDevice();

        AVCodecContext* pContext = null;
        var pCodec = ffmpeg.avcodec_find_encoder_by_name(m_info.CodecName);
        if (pCodec == null) {
            Log.Error($"avcodec_find_decoder:null", Aspects.FFmpeg);
            return false;
        }

        pContext = ffmpeg.avcodec_alloc_context3(pCodec);
        if (pContext == null) {
            Log.Error($"avcodec_alloc_context3:null", Aspects.FFmpeg);
            return false;
        }

        pContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        pContext->codec_id = pCodec->id;
        pContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
        pContext->profile = m_info.Profile;
        pContext->level = m_info.Level;
        pContext->width = m_info.Width;
        pContext->height = m_info.Height;
        pContext->bit_rate = m_info.BitRate;
        pContext->bits_per_raw_sample = 8;
        pContext->time_base.den = m_info.FrameRate.Num;
        pContext->time_base.num = m_info.FrameRate.Den;
        pContext->gop_size = (int)(m_info.FrameRate.Value + 0.5);
        pContext->framerate = new AVRational() { num = m_info.FrameRate.Num, den = m_info.FrameRate.Den };
        pContext->max_b_frames = 4;
        pContext->thread_count = 1;
        pContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        pContext->colorspace = AVColorSpace.AVCOL_SPC_BT2020_NCL;
        pContext->color_range = AVColorRange.AVCOL_RANGE_JPEG;
        pContext->color_primaries = AVColorPrimaries.AVCOL_PRI_BT2020;
        pContext->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1;
        pContext->sample_aspect_ratio = new AVRational() { num = 1, den = 1 };
        pContext->field_order = AVFieldOrder.AV_FIELD_PROGRESSIVE;
        pContext->chroma_sample_location = AVChromaLocation.AVCHROMA_LOC_LEFT;
        pContext->bits_per_raw_sample = 8;
        pContext->compression_level = 12;

        if (HWDeviceCtx != null) {
            pContext->hw_device_ctx = ffmpeg.av_buffer_ref(HWDeviceCtx);
        }

        //ffmpeg.av_opt_set(pContext->priv_data, "preset", "ultrafast ", 0);

        int result = ffmpeg.avcodec_open2(pContext, pCodec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        CodecContext = pContext;

        m_info.PPSSPSBufer = new IntPtr(pContext->extradata);
        m_info.BufferLength = pContext->extradata_size;

        if (SrcFrame == null) {
            SrcFrame = ffmpeg.av_frame_alloc();
            if (null == SrcFrame) {
                Log.Error($"av_frame_alloc return null.", Aspects.FFmpeg);
                return false;
            }

            // 设置音频帧的相关信息
            SrcFrame->width = m_info.Width;
            SrcFrame->height = m_info.Height;
            SrcFrame->format = (int)Constants.VideoPixelFormat;
        }

        if (DstFrame == null) {
            DstFrame = ffmpeg.av_frame_alloc();
            if (null == SrcFrame) {
                Log.Error($"av_frame_alloc return null.", Aspects.FFmpeg);
                return false;
            }

            // 设置音频帧的相关信息
            DstFrame->width = m_info.Width;
            DstFrame->height = m_info.Height;
            DstFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;

            result = ffmpeg.av_frame_get_buffer(DstFrame, 0);
            if (result < 0) {
                Log.Error($"av_frame_get_buffer:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                return false;
            }
        }

        SwsCtx = ffmpeg.sws_getContext(pContext->width, pContext->height, Constants.VideoPixelFormat,
                                        pContext->width, pContext->height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                                        ffmpeg.SWS_POINT, null, null, null);

        _isInited.Value = true;
        return true;
    }

    bool InitHwDevice()
    {
        int ret = 0;
        AVBufferRef* ctx = null;
        if (m_info.CodecName.Contains("_qsv")) {
            ret = ffmpeg.av_hwdevice_ctx_create(&ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_QSV, null, null, 0);
        }
        else if (m_info.CodecName.Contains("_nvenc")) {
            ret = ffmpeg.av_hwdevice_ctx_create(&ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA, null, null, 0);
        }
        else if (m_info.CodecName.Contains("_amf")) {
            ret = ffmpeg.av_hwdevice_ctx_create(&ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0);
        }

        if (ret < 0) {
            Log.Error($"av_hwdevice_ctx_create:{ret}, {FFInterop.DecodeMessage(ret)}", Aspects.FFmpeg);
            return false;
        }

        if (ctx != null) {
            HWDeviceCtx = ctx;
        }
        return true;
    }

    protected override void DeInitialize()
    {
        if (CodecContext != null) {
            var ctx = CodecContext;
            ffmpeg.avcodec_free_context(&ctx);
            CodecContext = null;
        }
    }

    //Stopwatch _sw = new Stopwatch();
    protected override bool Encode(VideoSample? sample, CancellationToken ct)
    {
        int result = 0;
        if (sample != null) {
            var buffer = sample.Lock();

            SrcFrame->data[0] = buffer;
            SrcFrame->linesize[0] = sample.Stride;

            //_sw.Restart();
            result = ffmpeg.sws_scale(SwsCtx, SrcFrame->data, SrcFrame->linesize,
                                    0, m_info.Height,
                                    DstFrame->data, DstFrame->linesize);
            //_sw.Stop();
            //Log.Info($"sws_scale {m_nVideoSampleCount} video frame use time: {_sw.ElapsedMilliseconds}");

            sample.Unlock();

            DstFrame->pts = m_nVideoSampleCount;
            m_nVideoSampleCount++;
        }

        result = ffmpeg.avcodec_send_frame(CodecContext, sample != null ? DstFrame : null);
        if (result < 0) {
            Log.Error($"avcodec_send_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        while (result >= 0) {
            var pkt = ffmpeg.av_packet_alloc();
            try {
                result = ffmpeg.avcodec_receive_packet(CodecContext, pkt);
                if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF) {
                    return true;
                }
                else if (result < 0) {
                    Log.Error($"avcodec_receive_packet:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                    return false;
                }

                //Log.Info("Encode one frame");
                var pktSample = new PacketSample(pkt, false);
                if (Post(pktSample) == false) {
                    pktSample.Dispose();
                }
            }
            finally {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        return true;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        DeInitialize();

        if (DstFrame != null) {
            var frame = DstFrame;
            ffmpeg.av_frame_free(&frame);
            DstFrame = null;
        }

        if (SwsCtx != null) {
            ffmpeg.sws_freeContext(SwsCtx);
            SwsCtx = null;
        }
    }

}
