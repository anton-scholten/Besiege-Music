#!/usr/bin/env python3
"""Makes the upright and honky-tonk pianos sound like themselves.

GeneralUser GS -- like most General MIDI fonts -- gives presets 0, 1 and 3
(Grand, Bright, Honky-tonk) **the same sample set**. What separates them is the
preset's *generators*: tuning, filter cutoff, envelope, and for honky-tonk a
second instrument zone detuned against the first. `extract-samples.py` pulls the
sample a preset points at and drops the generators, so all three came out of it
as the same recording -- byte for byte, once decoded. Three of the four piano
types sounded identical, and only the Rhodes did not.

This puts the difference back, from the samples already in the repository:

* **Honky-tonk** is a piano whose unison strings have drifted apart. A copy of
  the recording, sharp by a few cents, mixed against the original, is not an
  imitation of that -- it is that.
* **Upright** is a smaller instrument in a smaller box: less top end and less
  sustain than a concert grand. A gentle low-pass says the first half; its
  `decay` in Piano.xml already said the second.

**Both are made from the grand, every time.** Not from the file they are about to
replace: filtering an already-filtered sample would darken it again on every run,
and a tool whose output depends on how many times it has been run is a tool nobody
can trust. The grand is the one recording GeneralUser gave us, and the other two
are functions of it -- so this can be run after every extraction, or twice in a
row, and the answer is the same.

Lengths and rate are the grand's, so the loop points the piano block uses are the
grand's as well, and Piano.xml says so for all three.

    ./tools/derive-pianos.py            # rewrite the six files
    ./tools/derive-pianos.py --check    # say what they are now, change nothing
    ./tools/derive-pianos.py --demo out.ogg   # one note on each, to listen to

Needs ffmpeg on PATH, as `extract-samples.py` does, and reads and writes the
same format: mono Ogg Vorbis at 22050 Hz, `-q:a 2`.
"""

import os
import re
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.join(HERE, os.pardir, "Music", "Resources", "Samples")

RATE = 22050        # as extract-samples.py cuts them
QUALITY = "2"       # ffmpeg libvorbis -q, the same

# How sharp the second set of strings is. Real honky-tonk pianos are further out
# than this and in both directions; a single layer at fourteen cents is the
# effect without the parody, and it beats about three times a second at middle C.
HONKY_CENTS = 14.0

# How much of the note is the detuned layer. Half would be two pianos; this is
# one piano with something wrong with it.
HONKY_MIX = 0.42

# The layer is faded out before the sustain loop and is not in it. `Voices` jumps
# from loopEnd back to loopStart with no crossfade, and a second layer whose
# phase does not match across that jump clicks once a bar. The character of a
# honky-tonk is in the attack and the first second of decay in any case -- by the
# loop, a struck piano is already ringing out.
HONKY_FADE = 0.55   # the layer starts fading at this fraction of the loop start

# The corner of the upright's low-pass, in Hz, and how many poles. One pole at
# 2600 took the band above 3 kHz down to a third of the grand's and was still
# hard to hear, a piano at middle C keeping nine tenths of its energy below 1 kHz;
# two poles at 1800 is the smaller, boxier instrument the name promises. Its
# shorter sustain is the `decay` in Piano.xml, which is the other half of it.
UPRIGHT_HZ = 1800.0
UPRIGHT_POLES = 2

GENERATED = os.path.join(HERE, os.pardir, "docs", "generated-samples.md")


def grand():
    """The three grand recordings, and where each one's sustain loop begins.

    Read out of what `extract-samples.py` last wrote rather than hard-coded: the
    root key a font gives a piano zone is the font's business, and it changed
    under this tool once already when the source font did. The loop start is only
    needed to know where the honky-tonk's second layer has to be gone by.
    """
    line = ""
    for row in open(GENERATED):
        if row.startswith("piano_grand"):
            line = row
            break
    if not line:
        raise SystemExit("no piano_grand line in docs/generated-samples.md -- run "
                         "tools/extract-samples.py first")
    names = re.search(r'samples="([^"]*)"', line).group(1).split()
    loops = re.search(r'loops="([^"]*)"', line).group(1).split()
    out = []
    for name, loop in zip(names, loops):
        out.append((name, int(loop.split("-")[0])))
    return out


# What the extractor cut for the two derived types, which this replaces. A font
# gives presets 0, 1 and 3 the same recordings, so those cuts are the grand under
# another name and at another root; the derived pair carries the grand's names, so
# all three piano types share one set of loop points.
def stale():
    out = []
    for row in open(GENERATED):
        for stem in ("piano_upright", "piano_honky"):
            if row.startswith(stem):
                out += re.search(r'samples="([^"]*)"', row).group(1).split()
    return out


def read(name):
    """One sample as a list of floats, at its own rate."""
    path = os.path.join(SAMPLES, name + ".ogg")
    raw = subprocess.check_output(
        ["ffmpeg", "-v", "error", "-i", path, "-f", "s16le", "-ac", "1",
         "-ar", str(RATE), "-"])
    count = len(raw) // 2
    return list(struct.unpack("<%dh" % count, raw[:count * 2]))


def write(name, frames):
    """Back over the file it came from, in the format the mod loads."""
    path = os.path.join(SAMPLES, name + ".ogg")
    wav = path + ".tmp.wav"
    body = struct.pack("<%dh" % len(frames), *frames)
    with open(wav, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVEfmt ")
        f.write(struct.pack("<IHHIIHH", 16, 1, 1, RATE, RATE * 2, 2, 16))
        f.write(b"data" + struct.pack("<I", len(body)) + body)
    subprocess.check_call(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-ar", str(RATE),
         "-ac", "1", "-c:a", "libvorbis", "-q:a", QUALITY, path])
    os.remove(wav)


def clipped(value):
    return max(-32768, min(32767, int(round(value))))


def levelled(out, source):
    """The loudest sample where it was before. Two copies of one recording add
    up while they are in phase, which is the first thing anybody hears."""
    peak = max(abs(v) for v in out) or 1.0
    was = max(abs(v) for v in source) or 1.0
    gain = was / peak
    return [clipped(v * gain) for v in out]


def honky(frames, loop_start):
    """The original against a sharp copy of itself."""
    ratio = 2.0 ** (HONKY_CENTS / 1200.0)
    fade_from = max(1, int(loop_start * HONKY_FADE))
    out = []
    for i, dry in enumerate(frames):
        # The copy is read faster, which is what makes it sharp. Linear between
        # samples: at fourteen cents the two are never far apart, and the error
        # is far below what a 22 kHz cut already throws away.
        at = i * ratio
        left = int(at)
        wet = 0.0
        if left + 1 < len(frames):
            part = at - left
            wet = frames[left] * (1.0 - part) + frames[left + 1] * part
        if i >= loop_start:
            wet = 0.0
        elif i > fade_from:
            wet *= 1.0 - float(i - fade_from) / (loop_start - fade_from)
        out.append(dry * (1.0 - HONKY_MIX) + wet * HONKY_MIX)
    return levelled(out, frames)


def upright(frames):
    """A smaller piano in a smaller room."""
    import math
    a = 1.0 - math.exp(-2.0 * math.pi * UPRIGHT_HZ / RATE)
    out = list(frames)
    for _ in range(UPRIGHT_POLES):
        held = 0.0
        rolled = []
        for dry in out:
            held += a * (dry - held)
            rolled.append(held)
        out = rolled
    return levelled(out, frames)


def demo(path):
    """Middle C on each of the four, one after another, to listen to."""
    gap = [0] * int(RATE * 0.25)
    together = []
    note = [n for n, _ in grand()][1].rsplit("_", 1)[1]
    for name in ("piano_grand_" + note, "piano_upright_" + note,
                 "piano_honky_" + note, "piano_rhodes_60"):
        together += read(name) + gap
    write_demo(path, together)


def write_demo(path, frames):
    wav = path + ".tmp.wav"
    body = struct.pack("<%dh" % len(frames), *frames)
    with open(wav, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVEfmt ")
        f.write(struct.pack("<IHHIIHH", 16, 1, 1, RATE, RATE * 2, 2, 16))
        f.write(b"data" + struct.pack("<I", len(body)) + body)
    subprocess.check_call(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-ar", str(RATE),
         "-ac", "1", "-c:a", "libvorbis", "-q:a", "4", path])
    os.remove(wav)


def check():
    """How alike the four pianos are now, as the audit that found this ran."""
    import itertools
    middle = [n for n, _ in grand()][1]
    note = middle.rsplit("_", 1)[1]
    names = ["piano_grand_" + note, "piano_upright_" + note,
             "piano_honky_" + note, "piano_rhodes_60"]
    loaded = dict((n, read(n)) for n in names)
    print("waveform correlation at middle C (1.000 = the same recording):")
    for a, b in itertools.combinations(names, 2):
        x, y = loaded[a], loaded[b]
        n = min(len(x), len(y))
        if n < 1000:
            continue
        mx = sum(x[:n]) / n
        my = sum(y[:n]) / n
        num = sum((x[i] - mx) * (y[i] - my) for i in range(n))
        dx = sum((x[i] - mx) ** 2 for i in range(n)) ** 0.5
        dy = sum((y[i] - my) ** 2 for i in range(n)) ** 0.5
        r = num / (dx * dy) if dx and dy else 0.0
        print("  %-18s %-18s r=%+.3f%s"
              % (a[6:], b[6:], r, "   <-- the same file" if r > 0.999 else ""))
    return 0


def main():
    args = sys.argv[1:]
    if "--check" in args:
        return check()
    if "--demo" in args:
        demo(args[args.index("--demo") + 1])
        return 0

    # The extractor's own upright and honky cuts go: they are the grand's
    # recordings under another root, and leaving them would ship a second copy of
    # the same audio that nothing points at.
    for name in stale():
        if name.startswith("piano_grand"):
            continue
        path = os.path.join(SAMPLES, name + ".ogg")
        if os.path.exists(path):
            os.remove(path)
            print("removed %s: the same recording as the grand" % name)

    was = {}
    for source, loop_start in grand():
        frames = read(source)
        if loop_start >= len(frames):
            raise SystemExit("%s: loop starts at %d but the sample is %d long"
                             % (source, loop_start, len(frames)))
        note = source.rsplit("_", 1)[1]

        name = "piano_honky_" + note
        was[name] = len(frames)
        write(name, honky(frames, loop_start))
        print("%-20s %d frames, a copy %.0f cents sharp under the first %d"
              % (name, len(frames), HONKY_CENTS, loop_start))

        name = "piano_upright_" + note
        was[name] = len(frames)
        write(name, upright(frames))
        print("%-20s %d frames, %d poles rolling off above %d Hz"
              % (name, len(frames), UPRIGHT_POLES, int(UPRIGHT_HZ)))

    # The loop points in Piano.xml are frame numbers into these files, so a
    # round trip that gained or lost a frame would move every loop in the piano
    # block by that much. Vorbis carries the exact length in its granule
    # positions and ffmpeg keeps it -- but that is a thing to check, not a thing
    # to believe, and it is cheap to check.
    for name in sorted(was):
        now = len(read(name))
        if now != was[name]:
            raise SystemExit(
                "%s came back %d frames rather than %d: the loops in Piano.xml "
                "no longer point where they did." % (name, now, was[name]))
    print("all six re-read at their original lengths, so every loop in Piano.xml "
          "still points where it did")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
