using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Minimal PCM WAV reader. The game is IL2CPP, so UnityWebRequest/AssetBundle loading
    /// would need a coroutine; building the AudioClip directly is simpler and fully managed.
    /// Supports 8/16/24/32-bit integer PCM and 32-bit float, mono or stereo.
    /// </summary>
    internal static class WavLoader
    {
        /// <param name="trimmedFrames">
        /// Number of leading frames removed; divide by sampleRate for the time saved.
        /// </param>
        /// <param name="clipSampleRate">Sample rate of the returned clip.</param>
        internal static AudioClip Load(string path, out int trimmedFrames, out int clipSampleRate)
        {
            trimmedFrames = 0;
            clipSampleRate = 0;

            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || b[0] != (byte)'R' || b[1] != (byte)'I' ||
                b[2] != (byte)'F' || b[3] != (byte)'F' ||
                b[8] != (byte)'W' || b[9] != (byte)'A' ||
                b[10] != (byte)'V' || b[11] != (byte)'E')
                throw new InvalidDataException("not a RIFF/WAVE file: " + path);

            int audioFormat = 0;
            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataSize = 0;

            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int payload = pos + 8;
                if (size < 0 || payload + size > b.Length)
                    break;

                if (id == "fmt " && size >= 16)
                {
                    audioFormat = BitConverter.ToUInt16(b, payload);
                    channels = BitConverter.ToUInt16(b, payload + 2);
                    sampleRate = BitConverter.ToInt32(b, payload + 4);
                    bitsPerSample = BitConverter.ToUInt16(b, payload + 14);
                }
                else if (id == "data")
                {
                    dataOffset = payload;
                    dataSize = Math.Min(size, b.Length - payload);
                    break;
                }

                pos = payload + size + (size & 1);
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                throw new InvalidDataException("missing fmt/data chunk: " + path);

            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample < 1 || bytesPerSample > 4)
                throw new NotSupportedException("unsupported bit depth " + bitsPerSample + ": " + path);

            int totalSamples = dataSize / bytesPerSample;
            int frames = totalSamples / channels;
            if (frames <= 0)
                throw new InvalidDataException("empty audio data: " + path);

            bool floatFormat = audioFormat == 3;
            if (audioFormat != 1 && !floatFormat)
                throw new NotSupportedException("only PCM / IEEE float WAV is supported: " + path);

            float[] pcm = new float[totalSamples];
            int o = dataOffset;
            for (int i = 0; i < totalSamples; i++, o += bytesPerSample)
                pcm[i] = DecodeSample(b, o, bytesPerSample, floatFormat);

            clipSampleRate = sampleRate;

            // Drop the silent lead-in so the sound starts on the frame the game
            // reports the hit. The file on disk is left untouched.
            int skip = WavTrim.FirstAudibleFrame(pcm, channels, sampleRate);
            trimmedFrames = skip;
            int outFrames = frames - skip;
            float[] clipData = pcm;
            if (skip > 0)
            {
                clipData = new float[outFrames * channels];
                Array.Copy(pcm, skip * channels, clipData, 0, clipData.Length);
            }

            var clip = AudioClip.Create(
                Path.GetFileNameWithoutExtension(path), outFrames, channels, sampleRate, false);
            if (clip == null)
                throw new InvalidOperationException("AudioClip.Create returned null: " + path);

            // float[] implicitly converts to Il2CppStructArray<float>.
            if (!clip.SetData(clipData, 0))
                throw new InvalidOperationException("AudioClip.SetData failed: " + path);

            return clip;
        }

        private static float DecodeSample(byte[] b, int o, int width, bool floatFormat)
        {
            switch (width)
            {
                case 1:
                    return (b[o] - 128) / 128f;
                case 2:
                    return BitConverter.ToInt16(b, o) / 32768f;
                case 3:
                {
                    int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                    return v / 8388608f;
                }
                case 4:
                    return floatFormat
                        ? BitConverter.ToSingle(b, o)
                        : BitConverter.ToInt32(b, o) / 2147483648f;
                default:
                    return 0f;
            }
        }
    }
}
