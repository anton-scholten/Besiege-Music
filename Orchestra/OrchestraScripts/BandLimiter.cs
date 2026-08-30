using UnityEngine;

namespace OrchestraMod
{
    /// <summary>
    /// The last thing between this mod and the speakers: a limiter on the mix
    /// itself, which is the only place the mix can actually be seen.
    ///
    /// <see cref="Master"/> shares one gain between the blocks, and it works from an
    /// *estimate* -- the power sum of what each block says it is about to play,
    /// because separate notes are not in phase. Notes of one instrument in a chord
    /// are more in phase than that. Measured on the overdriven guitar, a six-note
    /// chord of one sample reaches 2.75 where the power sum says 2.40, so holding
    /// the estimate to 0.85 lets the real signal reach 0.98 -- and a saturated
    /// waveform arriving inside the one buffer Master runs late puts it over. That
    /// is the clipping you hear standing next to the blocks and not from across the
    /// level, where the distance falloff has already taken it down.
    ///
    /// No estimate fixes that, because the error depends on what the notes *are*.
    /// This does not estimate. A `MonoBehaviour` on the object carrying the
    /// `AudioListener` is handed the finished mix, so it can read the peak of the
    /// very samples it is about to pass on and scale them before it does. The
    /// output cannot exceed the ceiling: the gain for a buffer is decided from that
    /// buffer's own peak, and rises again only as far as the buffer allows.
    ///
    /// **It sees the whole game, and touches it only when the whole game is about to
    /// clip.** Above the ceiling everything comes down together, which is what a
    /// master limiter is; below it the buffer is passed through untouched, not
    /// multiplied by one -- so Besiege's own audio is bit for bit what it was unless
    /// this mod's band is loud enough to have wrecked it anyway.
    /// </summary>
    public class BandLimiter : MonoBehaviour
    {
        /// <summary>What a sample is allowed to reach. Just under one: past one is
        /// what the sound card clips, and the last twentieth is the room this needs
        /// to work in.</summary>
        public const float Ceiling = 0.95f;

        /// <summary>How much of the way back to unity the gain travels in one
        /// buffer once the loud passage is over -- around a fifth of a second at
        /// the usual buffer size. Attack has no rate: it arrives at whatever this
        /// buffer needs, in time for this buffer, because arriving late is the one
        /// thing a limiter may not do.</summary>
        private const float Release = 0.1f;

        private float gain = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            gain = Apply(data, gain, Release, Ceiling);
        }

        /// <summary>
        /// One buffer, limited in place; returns the gain to carry into the next.
        ///
        /// Written as a plain static so it can be run and measured outside Unity,
        /// which is the only way to know a limiter does what it says: there is no
        /// listening to a unit test.
        /// </summary>
        public static float Apply(float[] data, float gain, float release,
                                  float ceiling)
        {
            if (data == null || data.Length == 0)
            {
                return gain;
            }

            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak)
                {
                    peak = a;
                }
            }

            // The most gain this buffer can take, from its own peak and nothing
            // else. Worked out whatever the current gain happens to be: asking only
            // when the *current* gain would clip leaves the release free to climb
            // past what the buffer allows, and a rising signal then walks out over
            // the ceiling one buffer at a time. It did, on noise, until this line
            // stopped depending on `gain`.
            float allowed = peak > 0f ? ceiling / peak : 1f;
            if (allowed > 1f)
            {
                allowed = 1f;
            }

            if (allowed < gain)
            {
                // Down, at once and for the whole buffer. A limiter that eases into
                // holding a peak has already let the peak through.
                gain = allowed;
            }
            else if (gain < 1f)
            {
                // Up, gently, and never past what this buffer allows -- so no
                // sample later in the buffer can climb over the ceiling behind us.
                float want = gain + (1f - gain) * release;
                gain = want < allowed ? want : allowed;
            }

            if (gain >= 1f)
            {
                // Nothing to do, and nothing done: the game's own audio goes through
                // this untouched rather than multiplied by one.
                gain = 1f;
                return gain;
            }

            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= gain;
            }
            return gain;
        }
    }

    /// <summary>
    /// Keeps <see cref="BandLimiter"/> on whichever object is listening.
    ///
    /// Besiege swaps cameras between building and running and the listener goes
    /// with them, so this is a thing to check for rather than to do once --
    /// `InstrumentBehaviour.Place` re-finds the same listener for the same reason.
    /// Cheap: a look every half second, and nothing at all once it is there.
    /// </summary>
    public class Ears : MonoBehaviour
    {
        private const float LookEvery = 0.5f;
        private float lookAt;

        private void Update()
        {
            if (Time.unscaledTime < lookAt)
            {
                return;
            }
            lookAt = Time.unscaledTime + LookEvery;

            AudioListener ear =
                (AudioListener)Object.FindObjectOfType(typeof(AudioListener));
            if (ear == null || ear.gameObject.GetComponent<BandLimiter>() != null)
            {
                return;
            }
            ear.gameObject.AddComponent<BandLimiter>();
            Log.Info("the band's limiter is on " + ear.gameObject.name
                     + ", which is what is listening.");
        }
    }
}
