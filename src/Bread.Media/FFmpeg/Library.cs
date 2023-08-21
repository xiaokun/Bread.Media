namespace Bread.Media
{
	using FFmpeg.AutoGen;

	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Runtime.CompilerServices;
	using System.Threading.Tasks;

	/// <summary>
	/// Provides access to the underlying FFmpeg library information.
	/// </summary>
	public static partial class Library
	{
		private static readonly string NotInitializedErrorMessage =
			$"{nameof(FFmpeg)} library not initialized. Set the {nameof(FFmpegDirectory)} and call {nameof(LoadFFmpeg)}";

		private static readonly object SyncLock = new();
		private static int m_FFmpegLoadModeFlags = FFmpegLoadMode.FullFeatures;
		private static IReadOnlyList<string>? m_InputFormatNames;
		private static IReadOnlyList<string>? m_DecoderNames;
		private static IReadOnlyList<string>? m_EncoderNames;
		private static unsafe AVCodec*[]? m_AllCodecs;
		private static int m_FFmpegLogLevel = Debugger.IsAttached ? ffmpeg.AV_LOG_VERBOSE : ffmpeg.AV_LOG_WARNING;

		/// <summary>
		/// Gets or sets the FFmpeg path from which to load the FFmpeg binaries.
		/// You must set this path before setting the Source property for the first time on any instance of this control.
		/// Setting this property when FFmpeg binaries have been registered will have no effect.
		/// </summary>
		public static string FFmpegDirectory
		{
			get => ffmpeg.RootPath;
			set
			{
				if (FFInterop.IsInitialized)
					return;

				ffmpeg.RootPath = value;
			}
		}

		/// <summary>
		/// Gets the FFmpeg version information. Returns null
		/// when the libraries have not been loaded.
		/// </summary>
		public static string? FFmpegVersionInfo
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets or sets the bitwise library identifiers to load.
		/// See the <see cref="FFmpegLoadMode"/> constants.
		/// If FFmpeg is already loaded, the value cannot be changed.
		/// </summary>
		public static int FFmpegLoadModeFlags
		{
			get => m_FFmpegLoadModeFlags;
			set
			{
				if (FFInterop.IsInitialized)
					return;

				m_FFmpegLoadModeFlags = value;
			}
		}

		/// <summary>
		/// Gets or sets the FFmpeg log level.
		/// </summary>
		public static int FFmpegLogLevel
		{
			get
			{
				return IsInitialized
					? ffmpeg.av_log_get_level()
					: m_FFmpegLogLevel;
			}
			set
			{
				if (IsInitialized) ffmpeg.av_log_set_level(value);
				m_FFmpegLogLevel = value;
			}
		}

		/// <summary>
		/// Gets a value indicating whether the FFmpeg library has been initialized.
		/// </summary>
		public static bool IsInitialized => FFInterop.IsInitialized;

		/// <summary>
		/// Gets the registered FFmpeg input format names.
		/// </summary>
		/// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
		public static IReadOnlyList<string> InputFormatNames
		{
			get
			{
				lock (SyncLock) {
					if (!FFInterop.IsInitialized)
						throw new InvalidOperationException(NotInitializedErrorMessage);

					return m_InputFormatNames ??= FFInterop.RetrieveInputFormatNames();
				}
			}
		}


		/// <summary>
		/// Gets the registered FFmpeg decoder codec names.
		/// </summary>
		/// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
		public static unsafe IReadOnlyList<string> DecoderNames
		{
			get
			{
				lock (SyncLock) {
					if (!FFInterop.IsInitialized)
						throw new InvalidOperationException(NotInitializedErrorMessage);

					return m_DecoderNames ??= FFInterop.RetrieveDecoderNames(AllCodecs);
				}
			}
		}

		/// <summary>
		/// Gets the registered FFmpeg decoder codec names.
		/// </summary>
		/// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
		public static unsafe IReadOnlyList<string> EncoderNames
		{
			get
			{
				lock (SyncLock) {
					if (!FFInterop.IsInitialized)
						throw new InvalidOperationException(NotInitializedErrorMessage);

					return m_EncoderNames ??= FFInterop.RetrieveEncoderNames(AllCodecs);
				}
			}
		}

		/// <summary>
		/// Gets all registered encoder and decoder codecs.
		/// </summary>
		/// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
		internal static unsafe AVCodec*[] AllCodecs
		{
			get
			{
				lock (SyncLock) {
					if (!FFInterop.IsInitialized)
						throw new InvalidOperationException(NotInitializedErrorMessage);

					return m_AllCodecs ??= FFInterop.RetrieveCodecs();
				}
			}
		}

		/// <summary>
		/// Forces the pre-loading of the FFmpeg libraries according to the values of the
		/// <see cref="FFmpegDirectory"/> and <see cref="FFmpegLoadModeFlags"/>
		/// Also, sets the <see cref="FFmpegVersionInfo"/> property. Throws an exception
		/// if the libraries cannot be loaded.
		/// </summary>
		/// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
		public static bool LoadFFmpeg()
		{
			if (!FFInterop.Initialize(FFmpegLoadModeFlags))
				return false;

			// Set the folders and lib identifiers
			FFmpegDirectory = FFInterop.LibrariesPath;
			FFmpegLoadModeFlags = FFInterop.LibraryIdentifiers;
			FFmpegVersionInfo = ffmpeg.av_version_info();
			return true;
		}

		/// <summary>
		/// Provides an asynchronous version of the <see cref="LoadFFmpeg"/> call.
		/// </summary>
		/// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
		public static ConfiguredTaskAwaitable<bool> LoadFFmpegAsync() =>
			Task.Run(() => LoadFFmpeg()).ConfigureAwait(true);

		/// <summary>
		/// Unloads FFmpeg libraries from memory.
		/// </summary>
		/// <exception cref="NotImplementedException">Unloading FFmpeg libraries is not yet supported.</exception>
		public static void UnloadFFmpeg() =>
			throw new NotImplementedException("Unloading FFmpeg libraries is not yet supported");

	}
}
