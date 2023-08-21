using System;
using System.Collections.Generic;
using System.Text;

namespace Bread.Media
{
	public class DX11Sample : VideoSampleBase
	{
		public ISurface Surface { get; private set; }

		public DX11Sample(ISurface surface, int width, int height, VideoSampleFormat format)
			: base(width, height, format)
		{
			Surface = surface;
		}

		protected override void Dispose(bool disposeManageBuffer)
		{
			base.Dispose(disposeManageBuffer);
			if (_isDisposed.Value) return;
			Surface?.Dispose();
		}
	}
}
