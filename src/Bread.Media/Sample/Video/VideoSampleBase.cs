using System;
using System.Collections.Generic;
using System.Text;

namespace Bread.Media
{
	public abstract class VideoSampleBase : Sample
	{
		public int Width { get; protected set; } = 0;

		public int Height { get; protected set; } = 0;

		public int Stride { get; protected set; } = 0;

		public VideoSampleFormat Format { get; protected set; } = VideoSampleFormat.None;

		public VideoSampleBase(int width, int height, VideoSampleFormat format)
		{
			Width = width;
			Height = height;
			Format = format;
			Stride = format.GetDefaultStride(width);
		}

		public VideoInfo GetInfo()
		{
			return new VideoInfo() {
				Width = this.Width,
				Height = this.Height,
				Format = this.Format
			};
		}
	}
}
