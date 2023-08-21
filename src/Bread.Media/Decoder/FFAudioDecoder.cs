using FFmpeg.AutoGen;

namespace Bread.Media;

internal unsafe class FFAudioDecoder : Decoder<AudioSample>
{
    public AudioInfo Output => _out;

    AudioInfo _out;
    int _bufferLength; //in bytes
    int _dataLength; //in bytes

    byte* _buffer;
    AVFrame* _frame;
    SwrContext* _swrCtx;
    AVCodecContext* _context;
    AudioSamplePool? _pool;

    public FFAudioDecoder(AudioInfo? output = null) : base()
    {
        if (output == null) _out = new AudioInfo() { };
        else _out = output.Value;
    }

    /// <inheritdoc/>
    public override bool Initialize(AVCodecParameters* args)
    {
        Log.Info($"AVCodecParameters: codec_id:{args->codec_id}, samplerate:{args->sample_rate}, channels:{args->channels}, format:{args->format}");

        var codec = ffmpeg.avcodec_find_decoder(args->codec_id);
        if (codec == null) {
            Log.Error($"avcodec_find_decoder: null, args->codec_id: {args->codec_id}", "[FFME]");
            return false;
        }

        _context = ffmpeg.avcodec_alloc_context3(codec);
        if (_context == null) {
            Log.Error("avcodec_alloc_context3: null", "[FFME]");
            return false;
        }

        int result = ffmpeg.avcodec_parameters_to_context(_context, args);
        if (result < 0) {
            Log.Error($"avcodec_parameters_to_context:{result}, {FFInterop.DecodeMessage(result)}", "[FFME]");
            return false;
        }

        result = ffmpeg.avcodec_open2(_context, codec, null);
        if (result < 0) {
            Log.Error($"avcodec_open2:{result}, {FFInterop.DecodeMessage(result)}", "[FFME]");
            return false;
        }

        if (_out.SampleRate == 0) _out.SampleRate = args->sample_rate;
        if (_out.ChannelCount == 0) _out.ChannelCount = args->channels;
        if (_out.Format == AudioSampleFormat.None) {
            _out.Format = AudioSampleFormat.Float32;
            _out.BytesPerSample = 2;
        }

        _frame = ffmpeg.av_frame_alloc();
        var layout = ffmpeg.av_get_default_channel_layout(_out.ChannelCount);

        if (_context->channel_layout == 0) {
            _context->channel_layout = (ulong)ffmpeg.av_get_default_channel_layout(_context->channels);
        }

        Log.Info($"input format: samplerate:{_context->sample_rate}, channels:{_context->channels}, layout:{(long)_context->channel_layout}, format:{(int)_context->sample_fmt}");
        Log.Info($"output format: samplerate:{_out.SampleRate}, channels:{_out.ChannelCount}, layout:{layout}, format:{(int)_out.Format}");

        _swrCtx = ffmpeg.swr_alloc_set_opts(null, layout, _out.Format.ToAVFormat(), _out.SampleRate,
            (long)_context->channel_layout, _context->sample_fmt, _context->sample_rate, 0, null);

        result = ffmpeg.swr_init(_swrCtx);
        if (result < 0) {
            Log.Error($"swr_init:{result}, {FFInterop.DecodeMessage(result)}", "[FFME]");
            return false;
        }

        _pool = new AudioSamplePool(Constants.AudioSampleLength, _out.SampleRate, _out.ChannelCount, _out.Format);
        _bufferLength = _out.SampleRate * 2 * _out.ChannelCount * _out.Format.GetBytesPerSample();
        _buffer = (byte*)ffmpeg.av_malloc((ulong)_bufferLength);
        _dataLength = 0;

        _isInited.Value = true;
        return true;
    }

    /// <inheritdoc/>
    public override bool Decode(AVPacket* pkt)
    {
        if (_isInited == false) throw new InvalidOperationException("Not initialized.");

        int result = ffmpeg.avcodec_send_packet(_context, pkt);
        if (result < 0) {
            Log.Error($"avcodec_send_packet:{result}, {FFInterop.DecodeMessage(result)}", "[FFME]");
            return false;
        }

        //TODO: Flush the last data
        while (true) {
            result = ffmpeg.avcodec_receive_frame(_context, _frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN)) break;
            if (result == ffmpeg.AVERROR_EOF) {
                Log.Error("end of decode");
                return false;
            }
            //if (result == ffmpeg.AVERROR_INPUT_CHANGED) break;
            if (result < 0) {
                Log.Error($"avcodec_receive_frame:{result}, {FFInterop.DecodeMessage(result)}", "[FFME]");
                return false;
            }

            int samples = ffmpeg.swr_get_out_samples(_swrCtx, _frame->nb_samples);
            if (samples < 0) {
                Log.Error($"swr_get_out_samples:{samples}, {FFInterop.DecodeMessage(samples)}", "[FFME]");
                return false;
            }

            int size = ffmpeg.av_samples_get_buffer_size(null, _out.ChannelCount, samples, AVSampleFormat.AV_SAMPLE_FMT_FLT, 1);
            if (size < 0) {
                Log.Error($"av_samples_get_buffer_size:{size}, {FFInterop.DecodeMessage(size)}", "[FFME]");
                return false;
            }

            if (_bufferLength - _dataLength < size) {
                throw new OutOfMemoryException("TODO: need more buffer space.");
            }

            var ptrOut = _buffer + _dataLength;
            samples = ffmpeg.swr_convert(_swrCtx, &ptrOut, samples, _frame->extended_data, _frame->nb_samples);
            if (samples < 0) {
                Log.Error($"swr_convert:{samples}, {FFInterop.DecodeMessage(samples)}", "[FFME]");
                return false;
            }

            size = ffmpeg.av_samples_get_buffer_size(null, _out.ChannelCount, samples, _out.Format.ToAVFormat(), 1);
            if (size < 0) {
                Log.Error($"av_samples_get_buffer_size:{size}, {FFInterop.DecodeMessage(size)}", "[FFME]");
                return false;
            }
            _dataLength += size;

            size = Constants.AudioSampleLength * _out.ChannelCount * _out.Format.GetBytesPerSample();
            if (_dataLength < size) continue;

            byte* ptr = _buffer;
            while (_dataLength >= size) {
                var sample = _pool!.Get();
                if (sample == null) throw new OutOfMemoryException($"sample's pool not enough.{nameof(FFAudioDecoder)}");
                if (sample.Data.BufferLength < size) throw new OutOfMemoryException("sample's buffer not enough.");
                var data = (byte*)sample.Data.Lock();
                Buffer.MemoryCopy(ptr, data, sample.Data.BufferLength, size);
                sample.Data.Unlock(size);
                _queue.Enqueue(sample);
                ptr += size;
                _dataLength -= size;
            }

            if (_dataLength > 0) {
                Buffer.MemoryCopy(ptr, _buffer, size, _dataLength);
            }
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

        if (_swrCtx != null) {
            var swr = _swrCtx;
            ffmpeg.swr_free(&swr);
            _swrCtx = null;
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

        if (_buffer != null) {
            ffmpeg.av_free(_buffer);
            _buffer = null;
        }

        base.Dispose(disposeManaged);
    }


}
