using System;

namespace OrchestraMod
{
    /// <summary>
    /// One limiter shared by every instrument block, so a machine of sixty of them
    /// does not clip when they all play at once.
    ///
    /// **The clipping is not in any one block.** Each block already ends its buffer
    /// with a soft knee, and each one on its own stays inside it. What clips is
    /// Unity's own mix: sixty `AudioSource`s each handing over a signal that peaks
    /// near one, added together, is a signal that peaks near sixty. Nothing inside a
    /// block can see that, because a block only ever sees itself.
    ///
    /// So the blocks tell each other. Every block reports the loudest thing it is
    /// about to play, and reads back the gain they should all be using. One number,
    /// shared, applied by everybody: the band gets quieter together rather than the
    /// loudest instrument being singled out, which is what a limiter on each block
    /// would have done and would have sounded like a different arrangement.
    ///
    /// **Power, not peaks.** The total is `sqrt(sum of squares)` rather than the sum:
    /// separate notes are not in phase, so they add as power, and adding their peaks
    /// instead would say eight notes at 0.7 reach 5.6 and pull the whole song down to
    /// a sixth of its volume. They reach about 2, and that is what this corrects for.
    ///
    /// **A buffer late, on purpose.** A block reports its peak and is given the gain
    /// worked out from what everyone reported last time. Waiting for the others
    /// would mean blocking the audio thread on sixty other audio callbacks, which is
    /// how an audio thread misses its deadline and the whole game crackles. One
    /// buffer is around twenty milliseconds and the per-block knee covers the
    /// transient that arrives inside it.
    ///
    /// Nothing here calls into Unity: `SampleBank`'s rule -- the audio thread cannot
    /// -- holds for this too, which is why the blocks bring their own numbers and
    /// why the buffer is counted rather than timed.
    /// </summary>
    public static class Master
    {
        /// <summary>What the sum of the band is allowed to reach. Under one, so
        /// there is somewhere for a transient to go before Unity's mix hard-clips
        /// it.</summary>
        private const float Ceiling = 0.85f;

        /// <summary>How much of the way back up the gain comes each buffer once the
        /// loud passage is over. Coming down is immediate -- a limiter that eases
        /// into holding a peak has already let the peak through -- and coming up is
        /// slow enough not to be heard as breathing.
        ///
        /// Per *buffer*, not per block: every block asks once a buffer, so the step
        /// each one takes is this divided between them. Without that the release
        /// would be sixty times faster on a sixty-block machine than on a one-block
        /// one, which is the same limiter pumping on the song and not on the
        /// note.</summary>
        private const float Release = 0.05f;

        /// <summary>Most blocks this expects to hear from. A song is a block per
        /// distinct voice, and the note limit is what really caps it; a machine
        /// past this still plays, it is just not counted into the total.</summary>
        private const int Room = 512;

        private static readonly object gate = new object();
        private static readonly float[] squares = new float[Room];
        private static readonly bool[] taken = new bool[Room];
        private static float totalSquares;
        private static float gain = 1f;
        private static int live;

        /// <summary>Takes a slot for one block, or -1 when there is no room -- in
        /// which case that block plays unlimited, which is better than not playing.
        /// Called from the game thread.</summary>
        public static int Join()
        {
            lock (gate)
            {
                for (int i = 0; i < Room; i++)
                {
                    if (!taken[i])
                    {
                        taken[i] = true;
                        squares[i] = 0f;
                        live++;
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>Gives the slot back, and takes what was in it out of the total,
        /// so a block that is destroyed mid-note does not hold the band down.</summary>
        public static void Leave(int slot)
        {
            if (slot < 0 || slot >= Room)
            {
                return;
            }
            lock (gate)
            {
                totalSquares -= squares[slot];
                if (totalSquares < 0f)
                {
                    totalSquares = 0f;
                }
                squares[slot] = 0f;
                if (taken[slot])
                {
                    taken[slot] = false;
                    live--;
                }
            }
        }

        /// <summary>
        /// One block says how loud it is about to be, and is told what everybody is
        /// multiplying by. Called on the audio thread, once per buffer per block.
        /// </summary>
        /// <param name="peak">The loudest sample this block is about to play, after
        /// its own volume and before this gain.</param>
        public static float Ask(int slot, float peak)
        {
            if (slot < 0 || slot >= Room)
            {
                return 1f;
            }
            lock (gate)
            {
                // Kept as a running sum rather than added up each time: every block
                // asks every buffer, and walking the whole table each time would be
                // the one part of this that grew with the size of the machine.
                float square = peak * peak;
                totalSquares += square - squares[slot];
                squares[slot] = square;
                if (totalSquares < 0f)
                {
                    totalSquares = 0f;
                }

                float loudest = (float)Math.Sqrt(totalSquares);
                float want = loudest > Ceiling ? Ceiling / loudest : 1f;
                float step = Release / Math.Max(1, live);
                gain = want < gain ? want : gain + (want - gain) * step;
                return gain;
            }
        }

        /// <summary>
        /// A block has stopped playing and will not be asking again until it starts.
        ///
        /// Besiege stops an `AudioSource` that has nothing to play, and a stopped
        /// source's filter is not called -- so whatever that block last reported
        /// would sit in the total for as long as it stayed quiet, holding the rest
        /// of the band down for a note that finished minutes ago.
        /// </summary>
        public static void Quiet(int slot)
        {
            if (slot < 0 || slot >= Room)
            {
                return;
            }
            lock (gate)
            {
                totalSquares -= squares[slot];
                if (totalSquares < 0f)
                {
                    totalSquares = 0f;
                }
                squares[slot] = 0f;
            }
        }
    }
}
