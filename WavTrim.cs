using System;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Removes leading silence from a decoded PCM buffer.
    ///
    /// Many extracted / recorded feedback sounds start with 100-200 ms of pure
    /// digital silence before the audible transient. Playing them as-is makes
    /// the feedback feel late even though the plugin reacts within one frame.
    ///
    /// Pure C# with no Unity dependency so the rule can be unit tested.
    /// The original files on disk are never modified: trimming happens only on
    /// the in-memory buffer that is handed to AudioClip.SetData.
    /// </summary>
    internal static class WavTrim
    {
        /// <summary>Onset threshold, relative to this clip's own peak.</summary>
        internal const float ThresholdRatio = 0.02f;

        /// <summary>Keep this much audio before the detected onset so the
        /// transient attack is not clipped and the buffer still starts near zero.</summary>
        internal const double KeepLeadSeconds = 0.004;

        /// <summary>Never trim more than this, even if a clip has a long intro.</summary>
        internal const double MaxTrimSeconds = 0.35;

        /// <summary>
        /// Returns the first frame that should be kept (0 = keep everything).
        /// A frame is the group of samples for all channels at one time step.
        /// </summary>
        internal static int FirstAudibleFrame(float[] pcm, int channels, int sampleRate)
        {
            if (pcm == null || pcm.Length == 0 || channels <= 0 || sampleRate <= 0)
                return 0;

            float peak = 0f;
            for (int i = 0; i < pcm.Length; i++)
            {
                float a = Math.Abs(pcm[i]);
                if (a > peak)
                    peak = a;
            }
            if (peak <= 1e-6f)
                return 0; // fully silent: do not touch

            float threshold = peak * ThresholdRatio;
            int frames = pcm.Length / channels;
            int first = -1;
            for (int f = 0; f < frames; f++)
            {
                int o = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    if (Math.Abs(pcm[o + c]) > threshold)
                    {
                        first = f;
                        break;
                    }
                }
                if (first >= 0)
                    break;
            }

            if (first <= 0)
                return 0;

            int keep = (int)(sampleRate * KeepLeadSeconds);
            int maxTrim = (int)(sampleRate * MaxTrimSeconds);
            int start = first - keep;
            if (start < 0) start = 0;
            if (start > maxTrim) start = maxTrim;
            if (start >= frames) start = 0;
            return start;
        }
    }
}
