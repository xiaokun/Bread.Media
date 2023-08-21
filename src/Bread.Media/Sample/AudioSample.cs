using System.Threading;
using Bread.Utility.Threading;
using FFmpeg.AutoGen;

namespace Bread.Media;

/// <summary>
/// audio sample with float32 buffer which ranges from -1.0 to 1.0.
/// </summary>
public unsafe class AudioSample : Sample
{
    public int ChannelCount { get; protected set; } = 0;

    public int SampleRate { get; protected set; } = 0;

    public bool IsWrapper { get; private set; } = false;

    public int SampleCount => m_nSampleCount.Value;

    public int DataLength => m_nDataLength.Value;

    public int BufferLength => m_nBufferLength;

    public AudioSampleFormat Format { get; protected set; } = AudioSampleFormat.None;


    object _locker = new object();
    byte* m_pBuffer = null;
    int m_nBufferLength = 0;
    readonly AtomicInteger m_nSampleCount = new AtomicInteger(0);
    readonly AtomicInteger m_nDataLength = new AtomicInteger(0);

    public AudioSample(int samplecount, int samplerate, int channels, AudioSampleFormat format)
    {
        ChannelCount = channels;
        SampleRate = samplerate;
        Format = format;

        if (samplecount == 0 || channels == 0 || samplerate == 0)
            throw new ArgumentException(); ;

        var bytes = format.GetBitsPerSample() / 8;
        m_nSampleCount.Value = samplecount;
        m_nDataLength.Value = samplecount * channels * bytes; //数据长度
        m_hnsDuration.Value = (long)(samplecount / (double)samplerate * TimeSpan.TicksPerSecond);

        m_nBufferLength = m_nDataLength.Value + 128 * bytes;
        m_pBuffer = (byte*)ffmpeg.av_mallocz((ulong)m_nBufferLength);
        if (m_pBuffer == null) {
            throw new OutOfMemoryException();
        }
    }

    public AudioSample(byte* pBuffer, int length, int samplerate, int channels, AudioSampleFormat format)
    {
        ChannelCount = channels;
        SampleRate = samplerate;
        Format = format;

        m_pBuffer = pBuffer;
        m_nBufferLength = length;
        m_nSampleCount.Value = length / format.GetBytesPerSample() / channels;
        m_nDataLength.Value = length;
        m_hnsDuration.Value = (long)(m_nSampleCount.Value / (double)samplerate * TimeSpan.TicksPerSecond);
        IsWrapper = true;
    }


    /// <summary>
    /// lock the audio sample, and return the buffer pointer, prevent it dispose.
    /// </summary>
    /// <param name="dataLength">data length in bytes</param>
    /// <param name="bufferLength">buffer length in bytes</param>
    /// <returns></returns>
    public IntPtr Lock()
    {
        if (_isDisposed == true) throw new ObjectDisposedException("The aduio buffer sample has already disposed.");
        Monitor.Enter(_locker);
        return new IntPtr(m_pBuffer);
    }

    /// <summary>
    /// unlock the audio sample, and update related audio info.
    /// </summary>
    /// <param name="dataLength">data length in bytes</param>
    public void Unlock(int dataLength = 0)
    {
        if (_isDisposed == true) throw new ObjectDisposedException("The aduio buffer sample has already disposed.");
        if (dataLength > m_nBufferLength) {
            throw new OutOfMemoryException("Data length must be large than buffer length.");
        }

        if (dataLength > 0) {
            var bytes = Format.GetBytesPerSample();
            m_nSampleCount.Value = dataLength / ChannelCount / bytes;
            m_nDataLength.Value = dataLength;
            m_hnsDuration.Value = (long)(dataLength / bytes / (double)ChannelCount / (double)SampleRate * TimeSpan.TicksPerSecond);
        }

        Monitor.Exit(_locker);
    }


    /// <summary>
    /// fill buffer with zero data, not delete.
    /// </summary>
    public void Clear()
    {
    }

    protected override void Dispose(bool disposeManageBuffer)
    {
        base.Dispose(disposeManageBuffer);

        if (IsWrapper == false && m_pBuffer != null) {
            lock (_locker) {
                ffmpeg.av_free(m_pBuffer);
                m_pBuffer = null;
            }
        }
    }
}


internal class AudioSamplePool : Pool<AudioSample>
{
    int _sampleCount;
    int _sampleRate;
    int _channels;
    AudioSampleFormat _format;

    public AudioSamplePool(int samplecount, int samplerate, int channels, AudioSampleFormat format)
        : base(Constants.AudioCacheCount)
    {
        _sampleCount = samplecount;
        _sampleRate = samplerate;
        _channels = channels;
        _format = format;

        if (Allocate() == false) {
            throw new InvalidProgramException("Pool allocate fail.");
        }
    }

    /// <inheritdoc/>
    protected override bool Allocate()
    {
        try {
            for (int i = 0; i < _capcity; i++) {
                var sample = new AudioSample(_sampleCount, _sampleRate, _channels, _format);
                _items.Add(new Pooled<AudioSample>(sample, this, _total.Value));
                _total.Increment();
            }
            return true;
        }
        catch (Exception ex) {
            Log.Info($"Failed to allocate audio samples. {ex.Message}", "[FFME]");
            Log.Exception(ex);
            return false;
        }
    }

}
