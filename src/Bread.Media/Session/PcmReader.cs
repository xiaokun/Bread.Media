using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;


namespace Bread.Media;

public class NotSeekableException : Exception { }

public unsafe class PcmReader : IDisposable
{
    #region Properties

    volatile int m_nChannelCount = 0;
    readonly int m_nSampleRate = 0;
    volatile bool m_bInitialized = false;
    volatile int m_nStreamIndex = 0;
    volatile float m_duration = -1;

    public bool IsLoaded => m_bInitialized;
    public string? MediaPath { get; private set; }
    public int ChannelCount => m_nChannelCount;
    public int SampleRate => m_nSampleRate;
    public double Duration => (double)m_duration;
    public int Stride { get; private set; } = 1;

    #endregion

    #region Fields

    AVCodec* m_pCodec = null;
    AVFrame* m_pRawFrame = null;
    AVPacket* m_pPacket = null;
    SwrContext* m_pSwrCtx = null;
    AVFormatContext* m_pFormatCtx = null;
    AVCodecContext* m_pCodecCtx = null;

    /// <summary>
    /// 缓存数据
    /// </summary>
    short* m_pBuffer = null;

    /// <summary>
    /// m_pBuffer 的长度 
    /// </summary>
    int m_nBufferLength = 0;

    /// <summary>
    /// 剩余有效数据中音频sample的个数
    /// </summary>
    int m_nBufferSampleCount = 0;
    #endregion


    public PcmReader(int outputSampleRate = 16000)
    {
        m_nSampleRate = outputSampleRate;
    }

    public bool Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException("path is null or empty.");

        if (m_bInitialized) {
            Close();
        }

        MediaPath = path;
        m_bInitialized = Initialize(path);
        return m_bInitialized;
    }


    void Close()
    {
        if (m_pPacket != null) {
            var pkt = m_pPacket;
            ffmpeg.av_packet_free(&pkt);
            m_pPacket = null;
        }

        if (m_pSwrCtx != null) {
            var sctx = m_pSwrCtx;
            ffmpeg.swr_free(&sctx);
            m_pSwrCtx = null;
        }

        if (m_pCodecCtx != null) {
            ffmpeg.avcodec_close(m_pCodecCtx);
            m_pCodecCtx = null;
        }

        if (m_pFormatCtx != null) {
            var fctx = m_pFormatCtx;
            ffmpeg.avformat_close_input(&fctx);
            m_pFormatCtx = null;
        }
    }

    bool Initialize(string filePath)
    {
        int result = 0;
        m_pFormatCtx = ffmpeg.avformat_alloc_context();

        var context = m_pFormatCtx;
        result = ffmpeg.avformat_open_input(&context, filePath, null, null);
        if (result < 0) {
            Log.Error($"avformat_open_input:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        result = ffmpeg.avformat_find_stream_info(m_pFormatCtx, null);
        if (result < 0) {
            Log.Error($"avformat_find_stream_info:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        //get audio  stream index
        m_nStreamIndex = -1;
        m_nStreamIndex = ffmpeg.av_find_best_stream(context, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
        if (m_nStreamIndex < 0) {
            Log.Error($"av_find_best_stream:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        RetrieveDuration();

        //find and open codec
        var par = m_pFormatCtx->streams[m_nStreamIndex]->codecpar;
        m_pCodec = ffmpeg.avcodec_find_decoder(par->codec_id);
        if (m_pCodec == null) {
            Log.Error($"avcodec_find_decoder:null, can't find decoder :{m_pCodecCtx->codec_id}", Aspects.FFmpeg);
            return false;
        }

        m_pCodecCtx = ffmpeg.avcodec_alloc_context3(m_pCodec);
        if (m_pCodecCtx == null) {
            Log.Error($"can't find codec context from stream {m_nStreamIndex}");
            return false;
        }
        ffmpeg.avcodec_parameters_to_context(m_pCodecCtx, par);

        result = ffmpeg.avcodec_open2(m_pCodecCtx, m_pCodec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        //init packet
        m_pPacket = ffmpeg.av_packet_alloc();

        //set audio buffer
        m_nChannelCount = par->ch_layout.nb_channels;

        m_nBufferLength = ffmpeg.av_samples_get_buffer_size(null, m_nChannelCount, 2048, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
        m_pBuffer = (short*)ffmpeg.av_malloc((ulong)m_nBufferLength);
        m_pRawFrame = ffmpeg.av_frame_alloc();

        return InitSwr();
    }


    void RetrieveDuration()
    {
        m_duration = (float)(m_pFormatCtx->streams[m_nStreamIndex]->duration * ffmpeg.av_q2d(m_pFormatCtx->streams[m_nStreamIndex]->time_base));
        if (m_duration >= 0) return;


        var videoIndex = ffmpeg.av_find_best_stream(m_pFormatCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
        if (videoIndex >= 0) {
            m_duration = (float)(m_pFormatCtx->streams[videoIndex]->duration *
                    ffmpeg.av_q2d(m_pFormatCtx->streams[videoIndex]->time_base));
        }
        if (m_duration >= 0) return;

        m_duration = m_pFormatCtx->duration / (float)ffmpeg.AV_TIME_BASE;
    }

    bool InitSwr()
    {
        if (m_pSwrCtx != null) {
            ffmpeg.swr_close(m_pSwrCtx);
            var sctx = m_pSwrCtx;
            ffmpeg.swr_free(&sctx);
            m_pSwrCtx = null;
        }

        m_pSwrCtx = ffmpeg.swr_alloc();

        ffmpeg.av_opt_set_chlayout(m_pSwrCtx, "in_chlayout", &m_pCodecCtx->ch_layout, 0);
        ffmpeg.av_opt_set_int(m_pSwrCtx, "in_sample_rate", m_pCodecCtx->sample_rate, 0);
        ffmpeg.av_opt_set_sample_fmt(m_pSwrCtx, "in_sample_fmt", m_pCodecCtx->sample_fmt, 0);

        ffmpeg.av_opt_set_chlayout(m_pSwrCtx, "out_chlayout", &m_pCodecCtx->ch_layout, 0);
        ffmpeg.av_opt_set_int(m_pSwrCtx, "out_sample_rate", m_nSampleRate, 0);
        ffmpeg.av_opt_set_sample_fmt(m_pSwrCtx, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);

        if (m_pSwrCtx == null) return false;
        if (ffmpeg.swr_init(m_pSwrCtx) != 0) return false;
        return true;
    }


    private bool _farmeCached = false; // 可能还有frame没有取完
    int ReadNextAudioPacket()
    {
        int result = 0;
        int index = 0;

        while (true) {
            if (index >= 200) {
                result = -1;
                break;
            }

            index++;

            if (_farmeCached == false) {
                result = ffmpeg.av_read_frame(m_pFormatCtx, m_pPacket);
                if (result < 0) {
                    Log.Error($"av_read_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                    goto done;
                }

                if (m_pPacket->stream_index != m_nStreamIndex) {
                    ffmpeg.av_packet_unref(m_pPacket);
                    continue;
                }

                result = ffmpeg.avcodec_send_packet(m_pCodecCtx, m_pPacket);
                if (result < 0) {
                    Log.Error($"avcodec_send_packet:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                    goto done;
                }
            }

            result = ffmpeg.avcodec_receive_frame(m_pCodecCtx, m_pRawFrame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN)) {
                _farmeCached = false;
                continue;
            }

            if (result == ffmpeg.AVERROR_EOF) {
                // read the end of file
                goto done;
            }

            if (result < 0) {
                Log.Error($"avcodec_receive_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                goto done;
            }

            // read frame successfully
            _farmeCached = true;
            break;
        }

done:
        ffmpeg.av_packet_unref(m_pPacket);
        return result;
    }

    public byte[]? ReadData(double start, double duration, bool isLeftChannel)
    {
        bool result = Seek(start);
        if (result == false) return null;

        int count = (int)(duration * SampleRate);
        if (count < 10) return null;

        try {
            int length = 0;
            IntPtr ptrBuffer = BeginRead(count, ref length, isLeftChannel);
            if (length == 0) return null;

            byte[] buffer = new byte[length * sizeof(short)];
            Marshal.Copy(ptrBuffer, buffer, 0, length * 2);
            EndRead(ptrBuffer);
            return buffer;
        }
        catch (Exception) {
            return null;
        }
    }


    public bool Seek(double time)
    {
        if (time < 0) return false;

        long frame = (long)(time / ffmpeg.av_q2d(m_pFormatCtx->streams[m_nStreamIndex]->time_base));

        var result = ffmpeg.av_seek_frame(m_pFormatCtx, m_nStreamIndex, frame, ffmpeg.AVSEEK_FLAG_FRAME);
        if (result < 0) {
            Log.Error($"av_seek_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        _farmeCached = false;
        m_nBufferSampleCount = 0;
        return true;
    }


    public IntPtr BeginRead(int sampleCount, ref int length, bool isLeftChannel)
    {
        int result = 0;
        int size = 0;
        int realLength = 0;
        int convertCount = 0;

        if (m_bInitialized == false) throw new InvalidOperationException("init pcm reader first.");

        short* pBuffer = (short*)ffmpeg.av_malloc((ulong)(sampleCount * sizeof(short))); ; //单声道
        short* ptrBuffer = pBuffer;
        int delta = isLeftChannel ? 0 : 1;
        if (m_nChannelCount == 1) delta = 0;

        if (m_nBufferSampleCount > 0) {
            size = Math.Min(sampleCount, m_nBufferSampleCount);
            Unsafe.CopyBlock((void*)pBuffer, (void*)m_pBuffer, (uint)(size * sizeof(short)));

            ptrBuffer += size;
            sampleCount -= size;
            realLength += size;
            m_nBufferSampleCount -= size;

            if (m_nBufferSampleCount > 0) {
                short* temp = (short*)ffmpeg.av_malloc((ulong)(m_nBufferSampleCount * sizeof(short)));
                Unsafe.CopyBlock((void*)temp, (void*)(m_pBuffer + size), (uint)(m_nBufferSampleCount * sizeof(short)));
                Unsafe.CopyBlock((void*)m_pBuffer, (void*)(temp), (uint)(m_nBufferSampleCount * sizeof(short)));
                ffmpeg.av_free(temp);
            }
        }

        if (sampleCount == 0) goto done;

        // the m_pBuffer is emtpy now and m_nBufferSampleCount is 0
        if (m_nBufferSampleCount != 0) throw new InvalidProgramException();

        while (true) {
            result = ReadNextAudioPacket();
            if (result < 0) {
                goto done;
            }

            if (m_pRawFrame->nb_samples == 0) {
                continue;
            }

            int out_count = ffmpeg.swr_get_out_samples(m_pSwrCtx, m_pRawFrame->nb_samples);
            size = out_count * m_nChannelCount * sizeof(short);

            if (m_nBufferLength < size) {
                ffmpeg.av_free(m_pBuffer);
                m_pBuffer = (short*)ffmpeg.av_malloc((ulong)size);
                m_nBufferLength = size;
            }

            //数据格式转换
            var outputbytes = (byte*)m_pBuffer;
            var inputbytes = m_pRawFrame->data[0];
            convertCount = ffmpeg.swr_convert(m_pSwrCtx, &outputbytes, out_count,
                &inputbytes, m_pRawFrame->nb_samples);

            if (convertCount < 0) {
                result = -1;
                goto done;
            }

            size = Math.Min(sampleCount, convertCount);
            for (int i = 0; i < size; i++) {
                ptrBuffer[i] = m_pBuffer[i * m_nChannelCount + delta];
            }

            ptrBuffer += size;
            sampleCount -= size;
            realLength += size;
            if (sampleCount > 0) {
                m_pRawFrame->nb_samples = 0;
                continue;
            }

            if (convertCount - size > 0) {
                short* temp = (short*)ffmpeg.av_malloc((ulong)(convertCount - size) * sizeof(short));
                for (int i = size; i < convertCount; i++) {
                    temp[i - size] = m_pBuffer[i * m_nChannelCount + delta];
                }

                Unsafe.CopyBlock(m_pBuffer, temp, (uint)((convertCount - size) * sizeof(short)));

                m_nBufferSampleCount = convertCount - size;
                ffmpeg.av_free(temp);
            }

            break;
        }

done:
        length = realLength;
        if (result < 0 || realLength == 0) {
            if (pBuffer != null) {
                ffmpeg.av_free(pBuffer);
            }
            return IntPtr.Zero;
        }

        return new(pBuffer);
    }


    public void EndRead(IntPtr data)
    {
        if (data == IntPtr.Zero) return;
        ffmpeg.av_free((void*)data);
    }



    #region IDisposable

    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
            }
            Close();
            disposedValue = true;
        }
    }

    ~PcmReader()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }



    #endregion
}
