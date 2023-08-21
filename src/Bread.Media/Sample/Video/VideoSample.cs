using FFmpeg.AutoGen;



using System;
using System.Threading;

namespace Bread.Media
{
	public unsafe class VideoSample : VideoSampleBase
	{
		byte* m_pBuffer = null;
		readonly int m_nLength = 0;
		readonly object _locker = new();

		public VideoSample(int width, int height, VideoSampleFormat format)
			: base(width, height, format)
		{
			m_nLength = Stride * height;
			m_pBuffer = (byte*)ffmpeg.av_malloc((ulong)m_nLength);
		}

		public byte* Lock()
		{
			Monitor.Enter(_locker);
			return m_pBuffer;
		}

		public IntPtr LockHandle()
		{
			Monitor.Enter(_locker);
			return new IntPtr(m_pBuffer);
		}

		public void Unlock()
		{
			Monitor.Exit(_locker);
		}

		protected override void Dispose(bool disposeManageBuffer)
		{
			base.Dispose(disposeManageBuffer);

			if (m_pBuffer != null) {
				ffmpeg.av_free(m_pBuffer);
				m_pBuffer = null;
			}
		}
	}


	internal class VideoSamplePool : Pool<VideoSample>
	{
		readonly int _width;
		readonly int _height;
		readonly VideoSampleFormat _format;

		public VideoSamplePool(int width, int height, VideoSampleFormat format)
			: base(Constants.VideoCacheCount)
		{
			_width = width;
			_height = height;
			_format = format;

			if (Allocate() == false) {
				throw new InvalidProgramException("Pool allocate fail.");
			}
		}

		protected override bool Allocate()
		{
			try {
				for (int i = 0; i < _capcity; i++) {
					var sample = new VideoSample(_width, _height, _format);
					_items.Add(new Pooled<VideoSample>(sample, this, _total.Value));
					_total.Increment();
				}
				return true;
			}
			catch (Exception ex) {
				Log.Info($"Failed to allocate audio samples. {ex.Message}", Aspects.FFmpeg);
				Log.Exception(ex);
				return false;
			}
		}
	}
}
