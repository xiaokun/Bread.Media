using FFmpeg.AutoGen;

namespace Bread.Media;

internal unsafe class FFVideoDecoder : Decoder<VideoSample>
{
    internal VideoInfo Output => _out;

    VideoInfo _out;

    AVFrame* _frame;
    SwsContext* _swsCtx;
    AVCodecContext* _context;
    VideoSamplePool? _pool;

    public FFVideoDecoder() : base()
    {
        _out.Format = VideoSampleFormat.ARGB32;
    }


    public override bool Initialize(AVCodecParameters* args)
    {
        var codec = ffmpeg.avcodec_find_decoder(args->codec_id);
        if (codec == null) {
            Log.Error("avcodec_find_decoder: null", Aspects.FFmpeg);
            return false;
        }

        _context = ffmpeg.avcodec_alloc_context3(codec);
        if (_context == null) {
            Log.Error("avcodec_alloc_context3: null", Aspects.FFmpeg);
            return false;
        }

        int result = ffmpeg.avcodec_parameters_to_context(_context, args);
        if (result < 0) {
            Log.Error($"avcodec_parameters_to_context:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        result = ffmpeg.avcodec_open2(_context, codec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        _out.Width = _context->width;
        _out.Height = _context->height;

        _frame = ffmpeg.av_frame_alloc();
        _swsCtx = ffmpeg.sws_getContext(_context->width, _context->height, _context->pix_fmt,
                    _context->width, _context->height, Constants.VideoPixelFormat,
                    ffmpeg.SWS_POINT, null, null, null);

        _pool = new VideoSamplePool(_out.Width, _out.Height, _out.Format);
        _isInited.Value = true;
        return true;
    }


    /// <inheritdoc/>
    public override unsafe bool Decode(AVPacket* pkt)
    {
        if (_isInited == false) throw new InvalidOperationException("Not initialized.");
        int result = ffmpeg.avcodec_send_packet(_context, pkt);
        if (result < 0) {
            Log.Error($"avcodec_send_packet:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
            return false;
        }

        while (true) {
            result = ffmpeg.avcodec_receive_frame(_context, _frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN)) break;
            //if (result == ffmpeg.AVERROR_INPUT_CHANGED) break;
            if (result < 0) {
                Log.Error($"avcodec_receive_frame:{result}, {FFInterop.DecodeMessage(result)}", Aspects.FFmpeg);
                return false;
            }

            var sample = _pool!.Get();
            if (sample == null) throw new OutOfMemoryException($"{nameof(FFVideoDecoder)}");

            var dst = default(byte_ptrArray8);
            dst[0] = sample.Data.Lock();
            var dstStride = new[] { sample.Data.Stride };
            result = ffmpeg.sws_scale(
                _swsCtx,
                _frame->data, _frame->linesize,
                0, _context->height,
                dst, dstStride);
            sample.Data.Unlock();
            _queue.Enqueue(sample);
        }

        return true;
    }



    protected override void Dispose(bool disposeManaged)
    {
        if (_frame != null) {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_swsCtx != null) {
            ffmpeg.sws_freeContext(_swsCtx);
            _swsCtx = null;
        }

        if (_context != null) {
            var ctx = _context;
            ffmpeg.avcodec_free_context(&ctx);
            _context = null;
        }

        if (_pool != null) {
            _pool.Dispose();
            _pool = null;
        }

        base.Dispose(disposeManaged);
    }

}
