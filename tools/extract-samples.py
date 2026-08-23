#!/usr/bin/env python3
"""Cuts the sampled instruments out of a General MIDI SoundFont.

Besiege cannot read an .sf2 -- the mod loader blacklists System.IO outright --
so the font is build-time source material only. This resolves each General MIDI
preset to its zones, pulls the sample covering each wanted note, encodes it as
Ogg, and prints the Mod.xml resource lines and the samples="..." attributes to
paste into the block XMLs.

    ./tools/extract-samples.py path/to/GeneralUser-GS.sf2
    ./tools/extract-samples.py font.sf2 --only piano_grand
    ./tools/extract-samples.py font.sf2 --list

Needs ffmpeg on PATH. The font itself is never redistributed; only the cut
samples are, under whatever licence it carries -- FluidR3 and MuseScore General
are MIT, GeneralUser GS is permissive.
"""

import os
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "Orchestra", "Resources", "Samples")

# stem -> (GM preset number, [notes to cover])
# Three notes per instrument keeps pitch-shifting inside about +/-3 semitones,
# which is inaudible; one sample stretched across a range is what makes cheap
# samplers sound like chipmunks.
INSTRUMENTS = {
    "piano_grand":    (0,  [36, 60, 84]),
    "piano_upright":  (1,  [36, 60, 84]),
    "piano_rhodes":   (4,  [36, 60, 84]),
    "piano_honky":    (3,  [36, 60, 84]),
    "guitar_nylon":   (24, [40, 52, 64]),
    "guitar_steel":   (25, [40, 52, 64]),
    "guitar_jazz":    (26, [40, 52, 64]),
    "guitar_clean":   (27, [40, 52, 64]),
    "guitar_drive":   (29, [40, 52, 64]),
    "bass_acoustic":  (32, [28, 40, 52]),
    "bass_finger":    (33, [28, 40, 52]),
    "bass_pick":      (34, [28, 40, 52]),
    "bass_fretless":  (35, [28, 40, 52]),
    "bass_synth":     (38, [28, 40, 52]),
    "strings_violin": (40, [55, 67, 79]),
    "strings_viola":  (41, [48, 60, 72]),
    "strings_cello":  (42, [36, 48, 60]),
    "strings_dbass":  (43, [28, 40, 52]),
    "strings_ens":    (48, [48, 60, 72]),
    "brass_trumpet":  (56, [54, 66, 78]),
    "brass_trombone": (57, [40, 52, 64]),
    "brass_horn":     (60, [41, 53, 65]),
    "brass_tuba":     (58, [28, 40, 52]),
    "brass_section":  (61, [48, 60, 72]),
    "wind_flute":     (73, [60, 72, 84]),
    "wind_clarinet":  (71, [50, 62, 74]),
    "wind_oboe":      (68, [58, 70, 82]),
    "wind_bassoon":   (70, [34, 46, 58]),
    "wind_sax":       (65, [49, 61, 73]),
}

RATE = 22050        # plenty for these ranges, half the size of 44.1k
SECONDS = 2.0

# Families whose note holds for as long as you hold the key, rather than dying
# on its own. Both kinds are cut through their loop point and carry it into the
# game; what differs is what the game does with it -- a sustaining instrument
# goes round it for as long as the key is down, a struck one goes round it while
# it fades, which is how the font itself builds a guitar or a piano. Every one of
# these presets loops, most of them over the last few milliseconds.
SUSTAINED = ("strings_", "brass_", "wind_")
QUALITY = "2"       # ffmpeg libvorbis -q

GEN_INSTRUMENT = 41
GEN_KEYRANGE = 43
GEN_SAMPLEID = 53
GEN_ROOTKEY = 58


def chunks(data, start, end):
    i = start
    while i + 8 <= end:
        cid = data[i:i + 4].decode("ascii", "replace")
        size = struct.unpack_from("<I", data, i + 4)[0]
        yield cid, i + 8, size
        i += 8 + size + (size & 1)


class SoundFont(object):
    """Just enough of the format to answer "which sample plays this note?"."""

    def __init__(self, path):
        self.data = open(path, "rb").read()
        if self.data[0:4] != b"RIFF" or self.data[8:12] != b"sfbk":
            raise SystemExit("not a SoundFont: " + path)

        found = {}
        for cid, off, size in chunks(self.data, 12, len(self.data)):
            if cid != "LIST":
                continue
            kind = self.data[off:off + 4].decode("ascii", "replace")
            for sub, soff, ssize in chunks(self.data, off + 4, off + size):
                found[sub] = (soff, ssize)
        for needed in ("smpl", "phdr", "pbag", "pgen", "inst", "ibag", "igen", "shdr"):
            if needed not in found:
                raise SystemExit("SoundFont is missing its %s chunk" % needed)

        self.smpl = found["smpl"]
        self.phdr = self._records(found["phdr"], 38, self._preset)
        self.pbag = self._records(found["pbag"], 4, self._bag)
        self.pgen = self._records(found["pgen"], 4, self._gen)
        self.inst = self._records(found["inst"], 22, self._instrument)
        self.ibag = self._records(found["ibag"], 4, self._bag)
        self.igen = self._records(found["igen"], 4, self._gen)
        self.shdr = self._records(found["shdr"], 46, self._sample)

    def _records(self, where, width, parse):
        off, size = where
        return [parse(off + i * width) for i in range(size // width)]

    def _preset(self, at):
        name = self.data[at:at + 20].split(b"\0")[0].decode("ascii", "replace")
        preset, bank, bag = struct.unpack_from("<HHH", self.data, at + 20)
        return {"name": name, "preset": preset, "bank": bank, "bag": bag}

    def _instrument(self, at):
        name = self.data[at:at + 20].split(b"\0")[0].decode("ascii", "replace")
        bag = struct.unpack_from("<H", self.data, at + 20)[0]
        return {"name": name, "bag": bag}

    def _bag(self, at):
        gen, mod = struct.unpack_from("<HH", self.data, at)
        return {"gen": gen}

    def _gen(self, at):
        oper, amount = struct.unpack_from("<HH", self.data, at)
        return {"oper": oper, "amount": amount}

    def _sample(self, at):
        name = self.data[at:at + 20].split(b"\0")[0].decode("ascii", "replace")
        start, end, ls, le, rate = struct.unpack_from("<IIIII", self.data, at + 20)
        pitch = self.data[at + 40]
        return {"name": name, "start": start, "end": end,
                "loop_start": ls, "loop_end": le, "rate": rate, "pitch": pitch}

    # ---- zone walking ---------------------------------------------------

    @staticmethod
    def _covers(gens, note):
        """A zone with no key range covers everything; otherwise lo <= note <= hi."""
        for g in gens:
            if g["oper"] == GEN_KEYRANGE:
                lo = g["amount"] & 0xff
                hi = (g["amount"] >> 8) & 0xff
                return lo <= note <= hi
        return True

    @staticmethod
    def _value(gens, oper):
        for g in gens:
            if g["oper"] == oper:
                return g["amount"]
        return None

    def _zone_gens(self, bags, gens, index, end):
        out = []
        for b in range(index, end):
            first = bags[b]["gen"]
            last = bags[b + 1]["gen"] if b + 1 < len(bags) else len(gens)
            out.append(gens[first:last])
        return out

    def find_sample(self, preset_number, note, bank=0):
        """The sample header and root key that a GM preset uses for one note."""
        for i, p in enumerate(self.phdr):
            if p["preset"] != preset_number or p["bank"] != bank:
                continue
            end = self.phdr[i + 1]["bag"] if i + 1 < len(self.phdr) else len(self.pbag)
            for gens in self._zone_gens(self.pbag, self.pgen, p["bag"], end):
                inst_id = self._value(gens, GEN_INSTRUMENT)
                if inst_id is None or not self._covers(gens, note):
                    continue                      # global zone, or wrong range
                hit = self._instrument_sample(inst_id, note)
                if hit is not None:
                    return hit
        return None

    def _instrument_sample(self, inst_id, note):
        if inst_id >= len(self.inst):
            return None
        start = self.inst[inst_id]["bag"]
        end = self.inst[inst_id + 1]["bag"] if inst_id + 1 < len(self.inst) else len(self.ibag)
        for gens in self._zone_gens(self.ibag, self.igen, start, end):
            sample_id = self._value(gens, GEN_SAMPLEID)
            if sample_id is None or not self._covers(gens, note):
                continue
            if sample_id >= len(self.shdr):
                continue
            header = self.shdr[sample_id]
            root = self._value(gens, GEN_ROOTKEY)
            if root is None or root > 127:
                root = header["pitch"]
            return header, root
        return None

    def pcm(self, header):
        """The sample's frames, as signed 16-bit ints."""
        base = self.smpl[0]
        first = base + header["start"] * 2
        count = header["end"] - header["start"]
        return struct.unpack_from("<%dh" % count, self.data, first), header["rate"]


def decoded_loop(ogg, loop):
    """The loop points against what actually comes back out of the Ogg.

    Vorbis does not hand back the number of samples it was given -- a quarter of a
    second of a second is the usual difference -- and a loop that ends past the end
    of the decoded clip is one the game throws away, which is a note that does not
    sustain. So the file is read back and the pair moved down to fit, keeping its
    length: the loop is a few milliseconds earlier in a region that is repeating
    anyway.
    """
    pcm = subprocess.check_output(
        ["ffmpeg", "-v", "error", "-i", ogg, "-f", "s16le", "-ac", "1", "-"])
    length = len(pcm) // 2
    start, end = loop
    if end <= length:
        return loop
    shift = end - length
    start -= shift
    end -= shift
    if start < 0 or end - start < 16:
        print("      loop does not survive the encode; left unlooped")
        return None
    return (start, end)


def write_wav(path, frames, rate):
    body = struct.pack("<%dh" % len(frames), *frames)
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVEfmt ")
        f.write(struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16))
        f.write(b"data" + struct.pack("<I", len(body)) + body)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        raise SystemExit(__doc__)
    font = SoundFont(args[0])

    if "--list" in sys.argv:
        for p in font.phdr[:-1]:
            print("  bank %-3d preset %-3d  %s" % (p["bank"], p["preset"], p["name"]))
        return

    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1]

    os.makedirs(OUT, exist_ok=True)
    resources = []
    attributes = {}
    total = 0

    for stem in sorted(INSTRUMENTS):
        if only and stem != only:
            continue
        preset, notes = INSTRUMENTS[stem]
        names = []
        loops = []
        for note in notes:
            hit = font.find_sample(preset, note)
            if hit is None:
                print("  %-16s note %-3d  no zone covers it" % (stem, note))
                continue
            header, root = hit
            frames, rate = font.pcm(header)
            if not frames:
                continue

            # Loop points are absolute into the font's sample block; ours are
            # relative to the cut, and at the output rate rather than the font's.
            scale = RATE / float(rate)
            loop = None
            ls = header["loop_start"] - header["start"]
            le = header["loop_end"] - header["start"]
            if 0 <= ls < le <= len(frames):
                keep = le
                loop = (int(ls * scale), int(le * scale))
            else:
                keep = int(min(len(frames), SECONDS * rate))

            frames = list(frames[:keep])
            if loop is None:
                # A hard cut is a click; ten milliseconds of fade is not audible
                # as anything but the end of the note. A looped sample must not
                # be faded -- the loop is what the note goes on sounding with,
                # whether it is being held or dying away.
                fade = min(int(rate * 0.01), keep)
                for i in range(fade):
                    frames[keep - fade + i] = int(frames[keep - fade + i] * (1.0 - i / float(fade)))

            name = "%s_%d" % (stem, root)
            if name in names:
                # Two wanted notes landed in the same zone, so the font has only
                # one recording between them. Nothing gained by cutting it twice.
                print("  %-16s note %-3d  already covered by %s" % (stem, note, name))
                continue
            wav = os.path.join(OUT, name + ".wav")
            ogg = os.path.join(OUT, name + ".ogg")
            write_wav(wav, frames, rate)
            subprocess.check_call(
                ["ffmpeg", "-y", "-loglevel", "error", "-i", wav,
                 "-ar", str(RATE), "-ac", "1", "-c:a", "libvorbis", "-q:a", QUALITY, ogg])
            os.remove(wav)

            if loop is not None:
                loop = decoded_loop(ogg, loop)

            size = os.path.getsize(ogg)
            total += size
            names.append(name)
            loops.append("%d-%d" % loop if loop is not None else "-")
            resources.append('\t\t<AudioClip name="%s" path="Samples\\%s.ogg" />' % (name, name))
            print("  %-16s note %-3d  root %-3d  %-28s %5.1f KB%s"
                  % (stem, note, root, header["name"], size / 1024.0,
                     "  looped" if loop is not None else ""))
        if names:
            attributes[stem] = (" ".join(names),
                                " ".join(loops) if any(l != "-" for l in loops) else "")

    print()
    print("%d clips, %.1f KB total" % (len(resources), total / 1024.0))
    generated = os.path.join(OUT, "..", "..", "..", "docs", "generated-samples.md")
    with open(generated, "w") as f:
        f.write("# Generated by tools/extract-samples.py\n\n")
        f.write("## Mod.xml resources\n\n```xml\n")
        f.write("\n".join(resources))
        f.write("\n```\n\n## Block XML attributes\n\n```\n")
        for stem in sorted(attributes):
            sample_names, loop_spec = attributes[stem]
            line = '%-16s samples="%s"' % (stem, sample_names)
            if loop_spec:
                line += ' loops="%s"' % loop_spec
            f.write(line + "\n")
        f.write("```\n")
    print("wrote", os.path.relpath(generated, HERE))


if __name__ == "__main__":
    main()
