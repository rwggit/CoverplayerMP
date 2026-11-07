using AcoustID.Audio;
using AcoustID.Chromaprint;
using AcoustID.Native;
using FFmpeg.AutoGen.Abstractions;

namespace CoverPlayerMP.Shared.Player;

public sealed unsafe class FfmpegAudioFingerprintProvider : IAudioFingerprintProvider
{
    private const int TargetSampleRate = 11_025;
    private const int TargetChannels = 1;
    private const int TargetBytesPerSample = 2;
    private const int OutputChunkSamples = 16_384;

    public Task<AudioFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Die Audiodatei wurde nicht gefunden.", path);

        cancellationToken.ThrowIfCancellationRequested();
        FfmpegNativeLibraryLoader.EnsureInitialized();

        return Task.Run(() => DecodeAndFingerprint(path, cancellationToken), cancellationToken);
    }

    private static AudioFingerprint DecodeAndFingerprint(string path, CancellationToken cancellationToken)
    {
        using var fingerprinter = new NativeChromaContext(ChromaprintAlgorithm.TEST2);

        AVFormatContext* formatContext = null;
        AVCodecContext* codecContext = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        SwrContext* resampler = null;

        try
        {
            Check(ffmpeg.avformat_open_input(&formatContext, path, null, null), "Die Audiodatei konnte nicht geöffnet werden.");
            Check(ffmpeg.avformat_find_stream_info(formatContext, null), "Die Audio-Streaminformationen konnten nicht gelesen werden.");

            var streamIndex = FindAudioStream(formatContext);
            var stream = formatContext->streams[streamIndex];
            var codecParameters = stream->codecpar;
            var codec = ffmpeg.avcodec_find_decoder(codecParameters->codec_id);
            if (codec is null)
                throw new InvalidOperationException("Für den Audio-Stream wurde kein FFmpeg-Decoder gefunden.");

            codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (codecContext is null)
                throw new InvalidOperationException("Der FFmpeg-Codec-Kontext konnte nicht angelegt werden.");

            Check(ffmpeg.avcodec_parameters_to_context(codecContext, codecParameters), "Die Audio-Codecparameter konnten nicht übernommen werden.");
            Check(ffmpeg.avcodec_open2(codecContext, codec, null), "Der FFmpeg-Audio-Decoder konnte nicht geöffnet werden.");

            var inputLayout = codecContext->ch_layout;
            if (inputLayout.nb_channels <= 0)
                inputLayout = new AVChannelLayout
                {
                    order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
                    nb_channels = 1,
                    u = new AVChannelLayout_u { mask = 1 }
                };

            var outputLayout = new AVChannelLayout
            {
                order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
                nb_channels = TargetChannels,
                u = new AVChannelLayout_u { mask = 1 }
            };

            Check(ffmpeg.swr_alloc_set_opts2(
                &resampler,
                &outputLayout,
                AVSampleFormat.AV_SAMPLE_FMT_S16,
                TargetSampleRate,
                &inputLayout,
                codecContext->sample_fmt,
                codecContext->sample_rate,
                0,
                null), "Der FFmpeg-Resampler konnte nicht konfiguriert werden.");
            Check(ffmpeg.swr_init(resampler), "Der FFmpeg-Resampler konnte nicht initialisiert werden.");

            if (!fingerprinter.Start(TargetSampleRate, TargetChannels))
                throw new InvalidOperationException("AcoustID.NET konnte den Fingerprinter nicht starten.");

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            if (packet is null || frame is null)
                throw new InvalidOperationException("FFmpeg konnte die Audio-Puffer nicht anlegen.");

            while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (packet->stream_index != streamIndex)
                        continue;

                    Check(ffmpeg.avcodec_send_packet(codecContext, packet), "Das Audio-Paket konnte nicht an den FFmpeg-Decoder gesendet werden.");
                    ReceiveFrames(codecContext, frame, resampler, fingerprinter, cancellationToken);
                }
                finally
                {
                    ffmpeg.av_packet_unref(packet);
                }
            }

            Check(ffmpeg.avcodec_send_packet(codecContext, null), "Der FFmpeg-Decoder konnte nicht beendet werden.");
            ReceiveFrames(codecContext, frame, resampler, fingerprinter, cancellationToken);
            FlushResampler(resampler, fingerprinter, cancellationToken);

            var encoded = EncodeFingerprint(fingerprinter);
            if (string.IsNullOrWhiteSpace(encoded))
                throw new InvalidOperationException("AcoustID.NET konnte keinen Fingerprint erzeugen.");

            var duration = Math.Max(1, (int)Math.Round(EstimateDuration(stream)));
            return new AudioFingerprint(encoded, duration);
        }
        finally
        {
            if (resampler is not null)
                ffmpeg.swr_free(&resampler);
            if (frame is not null)
                ffmpeg.av_frame_free(&frame);
            if (packet is not null)
                ffmpeg.av_packet_free(&packet);
            if (codecContext is not null)
                ffmpeg.avcodec_free_context(&codecContext);
            if (formatContext is not null)
                ffmpeg.avformat_close_input(&formatContext);
        }
    }

    private static void ReceiveFrames(
        AVCodecContext* codecContext,
        AVFrame* frame,
        SwrContext* resampler,
        NativeChromaContext fingerprinter,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = ffmpeg.avcodec_receive_frame(codecContext, frame);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result, "Der FFmpeg-Decoder konnte keinen Audio-Frame liefern.");
            cancellationToken.ThrowIfCancellationRequested();

            var outputSamples = Math.Max(OutputChunkSamples, ffmpeg.swr_get_out_samples(resampler, frame->nb_samples));
            var output = new short[outputSamples];
            fixed (short* outputSamplesPointer = output)
            {
                byte* outputPointer = (byte*)outputSamplesPointer;
                byte** outputData = &outputPointer;
                var converted = ffmpeg.swr_convert(resampler, outputData, outputSamples, frame->extended_data, frame->nb_samples);
                Check(converted, "FFmpeg konnte Audio nicht in Mono-PCM konvertieren.");
                if (converted > 0)
                    fingerprinter.Consume(output, converted);
            }
            ffmpeg.av_frame_unref(frame);
        }
    }

    private static void FlushResampler(
        SwrContext* resampler,
        NativeChromaContext fingerprinter,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = new short[OutputChunkSamples];
            fixed (short* outputSamplesPointer = output)
            {
                byte* outputPointer = (byte*)outputSamplesPointer;
                byte** outputData = &outputPointer;
                var converted = ffmpeg.swr_convert(resampler, outputData, output.Length, null, 0);
                Check(converted, "FFmpeg konnte den Resampler nicht leeren.");
                if (converted <= 0)
                    return;
                fingerprinter.Consume(output, converted);
            }
        }
    }

    private static int FindAudioStream(AVFormatContext* formatContext)
    {
        for (var index = 0; index < formatContext->nb_streams; index++)
        {
            if (formatContext->streams[index]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                return index;
        }

        throw new InvalidOperationException("Die Datei enthält keinen Audio-Stream.");
    }

    private static string EncodeFingerprint(NativeChromaContext fingerprint)
    {
        fingerprint.Finish();
        return fingerprint.GetFingerprint();
    }

    private static double EstimateDuration(AVStream* stream) =>
        stream->duration > 0 && stream->time_base.den != 0
            ? stream->duration * stream->time_base.num / (double)stream->time_base.den
            : 1;

    private static void Check(int result, string message)
    {
        if (result < 0)
            throw new InvalidOperationException($"{message} FFmpeg-Fehlercode: {result}.");
    }
}