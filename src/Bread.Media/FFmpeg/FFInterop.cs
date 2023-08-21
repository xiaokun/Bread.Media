﻿namespace FFmpeg.AutoGen
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    
    using Bread.Media;

    /// <summary>
    /// Provides a set of utilities to perform logging, text formatting,
    /// conversion and other handy calculations.
    /// </summary>
    internal static unsafe class FFInterop
    {
        #region Private Declarations

        private static readonly object FFmpegLogBufferSyncLock = new();
        private static readonly List<string> FFmpegLogBuffer = new(1024);
        private static readonly IReadOnlyDictionary<int, MediaLogMessageType> FFmpegLogLevels =
            new Dictionary<int, MediaLogMessageType>
            {
                { ffmpeg.AV_LOG_DEBUG, MediaLogMessageType.Debug },
                { ffmpeg.AV_LOG_ERROR, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_FATAL, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_INFO, MediaLogMessageType.Info },
                { ffmpeg.AV_LOG_PANIC, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_TRACE, MediaLogMessageType.Trace },
                { ffmpeg.AV_LOG_WARNING, MediaLogMessageType.Warning }
            };

        private static readonly object SyncLock = new();
        private static readonly av_log_set_callback_callback FFmpegLogCallback = OnFFmpegMessageLogged;
        private static bool m_IsInitialized;
        private static int m_LibraryIdentifiers;

        #endregion

        #region Properties

        /// <summary>
        /// True when libraries were initialized correctly.
        /// </summary>
        public static bool IsInitialized
        {
            get { lock (SyncLock) { return m_IsInitialized; } }
        }

        /// <summary>
        /// Gets the libraries path. Only filled when initialized correctly.
        /// </summary>
        public static string LibrariesPath
        {
            get { lock (SyncLock) { return ffmpeg.RootPath; } }
        }

        /// <summary>
        /// Gets the bitwise FFmpeg library identifiers that were loaded.
        /// </summary>
        public static int LibraryIdentifiers
        {
            get { lock (SyncLock) { return m_LibraryIdentifiers; } }
        }

        #endregion

        #region FFmpeg Registration

        /// <summary>
        /// Registers FFmpeg library and initializes its streams.
        /// It only needs to be called once but calling it more than once
        /// has no effect. Returns a boolean to indicate if the load operation was successful.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="libIdentifiers">The bit-wise flag identifiers corresponding to the libraries.</param>
        /// <returns>
        /// Returns true if it was a new initialization and it succeeded. False if there was no need to initialize
        /// as there is already a valid initialization.
        /// </returns>
        /// <exception cref="FileNotFoundException">When ffmpeg libraries are not found.</exception>
        public static bool Initialize(int libIdentifiers)
        {
            lock (SyncLock) {
                if (m_IsInitialized)
                    return false;

                try {
                    var registrationIds = 0;

                    // Load FFmpeg binaries by Library ID
                    foreach (var lib in FFLibrary.All) {
                        if ((lib.FlagId & libIdentifiers) != 0 && lib.Load())
                            registrationIds |= lib.FlagId;
                    }

                    // Check if libraries were loaded correctly
                    if (FFLibrary.All.All(lib => lib.IsLoaded == false))
                        throw new FileNotFoundException($"Unable to load FFmpeg binaries from folder '{ffmpeg.RootPath}'.");

                    // Additional library initialization
                    if (FFLibrary.LibAVDevice.IsLoaded)
                        ffmpeg.avdevice_register_all();

                    if (FFLibrary.LibAVFormat.IsLoaded)
                        ffmpeg.avformat_network_init();

                    // Set logging levels and callbacks
                    ffmpeg.av_log_set_flags(ffmpeg.AV_LOG_SKIP_REPEATED);
                    ffmpeg.av_log_set_level(Library.FFmpegLogLevel);
                    ffmpeg.av_log_set_callback(FFmpegLogCallback);

                    // set the static environment properties
                    m_LibraryIdentifiers = registrationIds;
                    m_IsInitialized = true;
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                    m_LibraryIdentifiers = 0;
                    m_IsInitialized = false;

                    // rethrow the exception with the original stack trace.
                    throw;
                }

                return m_IsInitialized;
            }
        }

        #endregion

        #region Interop Helper Methods

        /// <summary>
        /// Copies the contents of a managed string to an unmanaged, UTF8 encoded string.
        /// </summary>
        /// <param name="source">The string to copy.</param>
        /// <returns>A pointer to a string in unmanaged memory.</returns>
        public static byte* StringToBytePointerUTF8(string source)
        {
            var sourceBytes = Encoding.UTF8.GetBytes(source);
            var result = (byte*)ffmpeg.av_mallocz((ulong)sourceBytes.Length + 1);
            Marshal.Copy(sourceBytes, 0, new IntPtr(result), sourceBytes.Length);
            return result;
        }

        /// <summary>
        /// Gets the FFmpeg error message based on the error code.
        /// </summary>
        /// <param name="errorCode">The code.</param>
        /// <returns>The decoded error message.</returns>
        public static unsafe string DecodeMessage(int errorCode)
        {
            var bufferSize = Constants.AudioSampleLength;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
            var message = Utilities.PtrToStringUTF8(buffer);
            return message ?? string.Empty;
        }


        /// <summary>
        /// Retrieves the codecs.
        /// </summary>
        /// <returns>The codecs.</returns>
        public static unsafe AVCodec*[] RetrieveCodecs()
        {
            var result = new List<IntPtr>(1024);
            void* iterator;
            AVCodec* item;
            while ((item = ffmpeg.av_codec_iterate(&iterator)) != null) {
                result.Add((IntPtr)item);
            }

            var collection = new AVCodec*[result.Count];
            for (var i = 0; i < result.Count; i++) {
                collection[i] = (AVCodec*)result[i];
            }

            return collection;
        }

        /// <summary>
        /// Retrieves the input format names.
        /// </summary>
        /// <returns>The collection of names.</returns>
        public static unsafe List<string> RetrieveInputFormatNames()
        {
            var result = new List<string>(128);
            void* iterator;
            AVInputFormat* item;
            while ((item = ffmpeg.av_demuxer_iterate(&iterator)) != null) {
                var value = Utilities.PtrToStringUTF8(item->name);
                if (string.IsNullOrEmpty(value)) continue;
                result.Add(value);
            }

            return result;
        }

        /// <summary>
        /// Retrieves the decoder names.
        /// </summary>
        /// <param name="allCodecs">All codecs.</param>
        /// <returns>The collection of names.</returns>
        public static unsafe List<string> RetrieveDecoderNames(AVCodec*[] allCodecs)
        {
            var codecNames = new List<string>(allCodecs.Length);
            foreach (var c in allCodecs) {
                if (ffmpeg.av_codec_is_decoder(c) != 0) {
                    var value = Utilities.PtrToStringUTF8(c->name);
                    if (string.IsNullOrEmpty(value)) continue;
                    codecNames.Add(value);
                }
            }

            return codecNames;
        }

        /// <summary>
        /// Retrieves the encoder names.
        /// </summary>
        /// <param name="allCodecs">All codecs.</param>
        /// <returns>The collection of names.</returns>
        public static unsafe List<string> RetrieveEncoderNames(AVCodec*[] allCodecs)
        {
            var codecNames = new List<string>(allCodecs.Length);
            foreach (var c in allCodecs) {
                if (ffmpeg.av_codec_is_encoder(c) != 0) {
                    var value = Utilities.PtrToStringUTF8(c->name);
                    if (string.IsNullOrEmpty(value)) continue;
                    codecNames.Add(value);
                }

            }

            return codecNames;
        }

        /// <summary>
        /// Log message callback from ffmpeg library.
        /// </summary>
        /// <param name="p0">The p0.</param>
        /// <param name="level">The level.</param>
        /// <param name="format">The format.</param>
        /// <param name="vl">The vl.</param>
        private static unsafe void OnFFmpegMessageLogged(void* p0, int level, string format, byte* vl)
        {
            const int lineSize = 1024;
            lock (FFmpegLogBufferSyncLock) {
                if (level > ffmpeg.av_log_get_level()) return;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Utilities.PtrToStringUTF8(lineBuffer);
                if (string.IsNullOrEmpty(line)) return;

                FFmpegLogBuffer.Add(line);

                var messageType = MediaLogMessageType.Debug;
                if (FFmpegLogLevels.ContainsKey(level))
                    messageType = FFmpegLogLevels[level];

                if (!line.EndsWith("\n", StringComparison.Ordinal)) return;
                line = string.Join(string.Empty, FFmpegLogBuffer);
                line = line.TrimEnd();
                FFmpegLogBuffer.Clear();
                Log.Info(messageType + ": " + line, Aspects.FFmpeg); ;
            }
        }

        #endregion
    }
}
