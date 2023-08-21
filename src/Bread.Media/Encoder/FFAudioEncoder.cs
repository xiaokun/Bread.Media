using System.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

internal sealed unsafe class FFAudioEncoder : Encoder<AudioSample>
{
    public AudioEncodeParams Params => m_info;

    readonly AudioEncodeParams m_info;
    AVFrame* m_pFrame = null;
    AVCodecContext* m_pCodecContext = null;
    int m_nAudioSampleCount = 0;

    public FFAudioEncoder(AudioEncodeParams info) : base(nameof(FFAudioEncoder))
    {
        m_info = info;
        Initialize();
    }


    protected override bool Start()
    {
        m_nAudioSampleCount = 0;
        return true;
    }

    protected override void Stop()
    {
        if (_isInited.Value == true) {
            EncodeFrame(null);
        }
        DeInitialize();
    }

    protected override bool Initialize()
    {
        AVCodecContext* pContext = null;

        var pCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
        if (pCodec == null) {
            Log.Error($"avcodec_find_decoder:null", Aspects.FFmpeg);
            return false;
        }

        pContext = ffmpeg.avcodec_alloc_context3(pCodec);
        if (pContext == null) {
            Log.Error($"avcodec_alloc_context3:null", Aspects.FFmpeg);
            return false;
        }

        pContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        pContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        pContext->codec_id = m_info.CodeId;
        pContext->sample_rate = m_info.SampleRate;
        pContext->channel_layout = (ulong)ffmpeg.av_get_default_channel_layout(m_info.ChannelCount);
        pContext->channels = m_info.ChannelCount;
        pContext->bit_rate = m_info.BitRate;
        pContext->time_base.den = m_info.SampleRate;
        pContext->time_base.num = 1;
        pContext->frame_size = Constants.AudioSampleLength;
        pContext->compression_level = 12;
        pContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        int result = ffmpeg.avcodec_open2(pContext, pCodec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        m_pCodecContext = pContext;

        if (m_pFrame == null) {
            m_pFrame = ffmpeg.av_frame_alloc();
            if (null == m_pFrame) {
                Log.Error($"av_frame_alloc return null.", Aspects.FFmpeg);
                return false;
            }

            // 设置音频帧的相关信息
            m_pFrame->nb_samples = Constants.AudioSampleLength; //Number of samples per channel
            m_pFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            m_pFrame->channel_layout = m_pCodecContext->channel_layout;
            m_pFrame->channels = m_pCodecContext->channels;

            result = ffmpeg.av_frame_get_buffer(m_pFrame, 32);
            if (result < 0) {
                Log.Error($"av_frame_get_buffer:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                return false;
            }
        }

        return true;
    }

    protected override void DeInitialize()
    {
        if (m_pCodecContext != null) {
            var ctx = m_pCodecContext;
            ffmpeg.avcodec_free_context(&ctx);
            m_pCodecContext = null;
        }
    }


    protected override bool Encode(AudioSample sample, CancellationToken ct)
    {
        var pData = sample.Lock();
        FillBuffer((float*)pData);
        sample.Unlock();
        m_pFrame->pts = m_nAudioSampleCount;
        m_nAudioSampleCount += Constants.AudioSampleLength;
        EncodeFrame(m_pFrame);
        return true;
    }

    private void FillBuffer(float* pData)
    {
        var pLeft = (float*)m_pFrame->data[0];
        var pRight = (float*)m_pFrame->data[1];
        int size = Constants.AudioSampleLength;

        if (m_pFrame->channels == 2) {
            // 将原有的PCM数据分开存储到data中
            if ((int)AVSampleFormat.AV_SAMPLE_FMT_FLTP != m_pFrame->format) {
                throw new NotImplementedException("format must be float planer.");
            }

            for (int i = 0; i < size; i++) {
                *pLeft = *pData;
                pData++;
                *pRight = *pData;
                pData++;
                pLeft++;
                pRight++;
            }
        }
        else if (m_pFrame->channels == 1) {
            var length = Constants.AudioSampleLength * sizeof(float);
            Buffer.MemoryCopy(pData, pLeft, length, length);
        }
    }

    private bool EncodeFrame(AVFrame* pAudioFrame)
    {
        var result = ffmpeg.avcodec_send_frame(m_pCodecContext, pAudioFrame);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        while (result >= 0) {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            try {
                result = ffmpeg.avcodec_receive_packet(m_pCodecContext, pkt);
                if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF) {
                    return true;
                }
                else if (result < 0) {
                    Log.Error($"avcodec_receive_packet:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                    return false;
                }

                var sample = new PacketSample(pkt);
                if (Post(sample) == false) {
                    sample.Dispose();
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

        if (m_pFrame != null) {
            var frame = m_pFrame;
            ffmpeg.av_frame_free(&frame);
            m_pFrame = null;
        }
    }


}
