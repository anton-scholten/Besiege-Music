#!/usr/bin/env python3
"""Turns a MIDI file into a Besiege machine that plays it on Orchestra blocks.

    ./tools/make-song.py song.mid --instrument Piano --install

The machine is a flat grid of blocks, all at one height. Two kinds:

  * an **instrument block** per distinct pitch, holding that note and nothing
    else -- an Orchestra block plays one note, so a tune is a row of blocks;
  * a **timer block** per note in the score, set to fire at that note's moment
    and to hold the key for as long as the note lasts. They start with the
    simulation, or on a keypress with `--key`.

They are joined by Besiege's own variable system rather than by keys. A key can
carry a *message* -- a variable name -- and `KeyInputController` keeps a table of
which keys listen to which name, so a timer emulating `orch_042` presses every
key that names it. Keyboard keys would work the same way and there are about a
hundred of them; variable names are unlimited, which is what a song needs.

Nothing is connected to anything: the blocks are laid out, not built. Besiege
loads them all the same, and they drop to the ground when the simulation starts
without stopping the music.

**Why MIDI and not a YouTube link.** Turning recorded audio back into notes is
polyphonic transcription -- a research problem, wrong often enough to be
disappointing, and a heavy dependency (a neural net, ~100 MB) for the privilege.
A score already *is* the notes. MuseScore exports MIDI from any score in its
library, which is two clicks and exact. So: bring a MIDI file.
"""

import argparse
import glob
import math
import os
import struct
import sys
import uuid
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BLOCKS = os.path.join(REPO, "Orchestra")

# Besiege's own block ids, from the game's BlockType enum.
STARTING_BLOCK = 0
TIMER = 66

# What a missing Orchestra shows instead, as the game itself writes: a ballast.
FALLBACK = 35

# A quarter turn about X, which is what a block placed on a flat surface carries
# in Besiege's own saves. It sends the block's up axis -- the one an instrument
# stands on, and the one a timer's dial faces along -- to world up, so a field of
# these is a field of instruments standing up rather than lying on their sides.
FACE_UP = (-0.7071068, 0.0, 0.0, 0.7071068)

# The mapper keys the timer block declares (TimerBlock.Awake).
TIMER_WAIT = "bmt-wait"
TIMER_HOLD = "bmt-emulation-time"
TIMER_AUTO = "bmt-automatic"
TIMER_EMULATE = "bmt-emulate"
TIMER_START = "bmt-activate"

# General MIDI percussion, mapped onto the three struck families. Only the
# common half of the kit; anything else falls back to the snare.
DRUM_MAP = {
    35: ("Drums", "Kick"), 36: ("Drums", "Kick"),
    38: ("Drums", "Snare"), 40: ("Drums", "Snare"), 37: ("Drums", "Snare"),
    41: ("Drums", "Tom"), 43: ("Drums", "Tom"), 45: ("Drums", "Tom"),
    47: ("Drums", "Tom"), 48: ("Drums", "Tom"), 50: ("Drums", "Tom"),
    42: ("Cymbals", "Hi-hat"), 44: ("Cymbals", "Hi-hat"), 46: ("Cymbals", "Hi-hat"),
    49: ("Cymbals", "Crash"), 57: ("Cymbals", "Crash"),
    51: ("Cymbals", "Ride"), 59: ("Cymbals", "Ride"),
}

# The note a drum block is asked for, per kit piece: these engines are pitched,
# and a kick wants to be lower than a tom.
DRUM_NOTE = {"Kick": 36, "Snare": 50, "Tom": 45,
             "Hi-hat": 78, "Crash": 72, "Ride": 76}


# ---- MIDI ------------------------------------------------------------------

class Midi(object):
    """A standard MIDI file, read down to the notes.

    Written out here rather than taken from a library so the tool has no
    dependencies: a repository that builds with the game's own compiler should
    not need pip to make a song.
    """

    def __init__(self, path):
        data = open(path, "rb").read()
        if data[:4] != b"MThd":
            raise SystemExit("%s is not a MIDI file" % path)
        length = struct.unpack_from(">I", data, 4)[0]
        fmt, tracks, division = struct.unpack_from(">3h", data, 8)
        if division <= 0:
            raise SystemExit("SMPTE timecode MIDI is not supported; "
                             "export with ticks per beat instead")
        self.division = division
        self.format = fmt

        at = 8 + length
        self.tracks = []
        while at < len(data) and len(self.tracks) < tracks:
            if data[at:at + 4] != b"MTrk":
                break
            size = struct.unpack_from(">I", data, at + 4)[0]
            self.tracks.append(self._events(data[at + 8:at + 8 + size]))
            at += 8 + size

    @staticmethod
    def _varlen(data, at):
        value = 0
        while True:
            b = data[at]
            at += 1
            value = (value << 7) | (b & 0x7F)
            if not b & 0x80:
                return value, at

    def _events(self, data):
        """One track, as (tick, kind, a, b) with running status resolved."""
        out = []
        at = 0
        tick = 0
        status = 0
        while at < len(data):
            delta, at = self._varlen(data, at)
            tick += delta
            if at >= len(data):
                break
            byte = data[at]
            if byte & 0x80:
                status = byte
                at += 1
            # else: running status -- the previous one still stands

            if status == 0xFF:
                kind = data[at]
                at += 1
                size, at = self._varlen(data, at)
                body = data[at:at + size]
                at += size
                if kind == 0x51 and size == 3:      # tempo
                    out.append((tick, "tempo",
                                (body[0] << 16) | (body[1] << 8) | body[2], 0))
                elif kind == 0x2F:                  # end of track
                    break
            elif status in (0xF0, 0xF7):
                size, at = self._varlen(data, at)
                at += size
            else:
                high = status & 0xF0
                channel = status & 0x0F
                if high in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
                    a, b = data[at], data[at + 1]
                    at += 2
                    if high == 0x90 and b > 0:
                        out.append((tick, "on", channel, (a, b)))
                    elif high == 0x80 or (high == 0x90 and b == 0):
                        out.append((tick, "off", channel, (a, b)))
                elif high in (0xC0, 0xD0):
                    a = data[at]
                    at += 1
                    if high == 0xC0:
                        out.append((tick, "program", channel, (a, 0)))
                else:
                    at += 1
        return out

    def seconds(self, override_bpm=None):
        """A function from tick to seconds, following the file's tempo map."""
        changes = [(0, 500000)]                     # MIDI default: 120 bpm
        if override_bpm:
            changes = [(0, int(round(60000000.0 / override_bpm)))]
        else:
            for track in self.tracks:
                for tick, kind, a, _ in track:
                    if kind == "tempo":
                        changes.append((tick, a))
            # By tick alone, and not by the whole tuple. Sorting the tuples
            # compares the microseconds when two tempos share a tick, so a file
            # whose own tick-0 tempo is *faster* than 120 bpm -- a smaller number
            # -- sorted ahead of the default above and the walk below then took
            # the default as the later one. Every such file was played at 120.
            # Python's sort is stable, so keying on the tick keeps the default
            # first and whatever the file said last, which is what wins.
            changes.sort(key=lambda change: change[0])

        # Walk the map once, remembering where each segment starts in seconds.
        marks = []
        last_tick, last_us, elapsed = 0, changes[0][1], 0.0
        for tick, us in changes:
            if tick > last_tick:
                elapsed += (tick - last_tick) * last_us / 1e6 / self.division
                last_tick = tick
            last_us = us
            marks.append((tick, elapsed, us))

        def at(tick):
            lo, hi = 0, len(marks) - 1
            while lo < hi:
                mid = (lo + hi + 1) // 2
                if marks[mid][0] <= tick:
                    lo = mid
                else:
                    hi = mid - 1
            start_tick, start_time, us = marks[lo]
            return start_time + (tick - start_tick) * us / 1e6 / self.division

        return at

    def notes(self, override_bpm=None):
        """Every note as (start, duration, pitch, velocity, channel, track)."""
        when = self.seconds(override_bpm)
        out = []
        for index, track in enumerate(self.tracks):
            sounding = {}
            for tick, kind, channel, payload in track:
                if kind == "on":
                    sounding.setdefault((channel, payload[0]), []).append(
                        (tick, payload[1]))
                elif kind == "off":
                    held = sounding.get((channel, payload[0]))
                    if held:
                        start, velocity = held.pop(0)
                        out.append((when(start), when(tick) - when(start),
                                    payload[0], velocity, channel, index))
            # A note left on at the end of the track still gets to sound.
            for (channel, pitch), held in sounding.items():
                for start, velocity in held:
                    out.append((when(start), 0.5, pitch, velocity, channel, index))
        out.sort()
        return out


# ---- the blocks this mod ships ---------------------------------------------

def catalogue():
    """Each Orchestra block, read from its own XML: id, name, and its types."""
    found = {}
    for path in sorted(glob.glob(os.path.join(BLOCKS, "*.xml"))):
        if os.path.basename(path) == "Mod.xml":
            continue
        root = ET.parse(path).getroot()
        if root.tag != "Block":
            continue
        name = root.findtext("Name", "").strip()
        local = int(root.findtext("ID", "0").strip())
        types = [t.get("name") for t in root.iter("Type")]
        # Which of them a block starts on. By name on the module element, not by
        # the order of the list: the type is saved as an *index*, so moving a
        # different one to the front would change what every machine already built
        # plays. `iter("Extra")` carries a `default` of its own, which is why this
        # reads the module element rather than any attribute called default.
        chosen = 0
        for module in root.iter("OrchestraMod"):
            wanted = (module.get("default") or "").strip()
            for index, one in enumerate(types):
                if one and one.lower() == wanted.lower():
                    chosen = index
        found[name.lower()] = (name, local, types, chosen)
    if not found:
        raise SystemExit("no block XMLs found in %s" % BLOCKS)
    return found


def mod_details():
    """The mod's own id, version and name, for the save's requiredMods line."""
    root = ET.parse(os.path.join(BLOCKS, "Mod.xml")).getroot()
    mod_id = (root.findtext("ID") or "").strip()
    if not mod_id:
        raise SystemExit("Orchestra/Mod.xml has no <ID> yet -- run the game once "
                         "with the mod installed so it writes one")
    return mod_id, (root.findtext("Version") or "0.1.0").strip(), \
        (root.findtext("Name") or "Orchestra").strip()


def pick_type(types, wanted, fallback=0):
    """The index of a named type, matched loosely, or the block's own default."""
    if not wanted:
        return fallback
    for index, name in enumerate(types):
        if name and name.lower() == wanted.lower():
            return index
    for index, name in enumerate(types):
        if name and wanted.lower() in name.lower():
            return index
    raise SystemExit("no type called '%s'; this block has: %s"
                     % (wanted, ", ".join(t for t in types if t)))


# ---- writing the machine ---------------------------------------------------

def element(parent, tag, **attrs):
    return ET.SubElement(parent, tag, dict((k, str(v)) for k, v in attrs.items()))


def block(blocks, block_id, position, mod=None, local=None, facing=FACE_UP):
    """One block at a grid position, with the transform Besiege expects."""
    attrs = {"id": str(block_id), "guid": str(uuid.uuid4())}
    if mod is not None:
        # The loader resolves a modded block by modId and localId -- the id
        # above is recomputed on load (XmlLoader.HandleMod), and fallback is
        # what stands in when the mod is absent.
        attrs["modId"] = mod
        attrs["localId"] = str(local)
        attrs["fallback"] = str(FALLBACK)
    node = ET.SubElement(blocks, "Block", attrs)
    transform = ET.SubElement(node, "Transform")
    element(transform, "Position", x=position[0], y=position[1], z=position[2])
    element(transform, "Rotation", x=facing[0], y=facing[1],
            z=facing[2], w=facing[3])
    element(transform, "Scale", x=1, y=1, z=1)
    data = ET.SubElement(node, "Data")
    # Written on every block by the game itself; harmless and one less
    # difference between a generated save and a saved one.
    value(data, "Integer", "bmt-version", "1")
    return data


def value(data, kind, key, text):
    node = ET.SubElement(data, kind, {"key": key})
    node.text = text
    return node


def variable_key(data, key, name, keycode):
    """A mapper key driven by a variable rather than the keyboard.

    `MKey.Serialize` writes one entry per keycode and then the extras, so a key
    that listens to a variable *looks* like it needs only the two: the name, and
    the flag that says to use it.

    It needs the keycode as well, and this is the whole of why the first machines
    this tool wrote were silent. `Machine.InitSimBlock` registers a key with
    `KeyInputController` inside `for (i = 0; i < key.KeysCount; i++)`, and
    `AddMKey` is what files a key under its variable name. No keycodes, no
    iterations, no registration -- the block never joins the table the timers
    look names up in, and nothing reaches it. The keyboard cannot trigger it
    either way: `AddMKey` files a key under its name *or* its keys, never both,
    and `Use=True` chooses the name. So the keycode is there to be counted.

    In game this never comes up, because `KeySelector.SetVariable` sets the name
    and leaves the block's own key alone.
    """
    node = ET.SubElement(data, "StringArray", {"key": key})
    for entry in (keycode, "Message=" + name, "Use=True"):
        ET.SubElement(node, "String").text = entry


def grid(index, columns, spacing):
    """Blocks are laid out, not built: a field on the ground, a row at a time.

    Flat rather than upright, so the band is spread across the level instead of
    stacked into a wall -- and so nothing has far to fall, none of it being
    attached to anything.
    """
    return (round((index % columns) * spacing, 4),
            0,
            round((index // columns) * spacing, 4))


def build(notes, options, families):
    """The machine, as an XML tree."""
    mod_id, version, mod_name = mod_details()

    machine = ET.Element("Machine", {"version": "1", "bsgVersion": "1.4",
                                     "name": options.name})
    globals_ = ET.SubElement(machine, "Global")
    element(globals_, "Position", x=0, y=options.height, z=0)
    element(globals_, "Rotation", x=0, y=0, z=0, w=1)

    data = ET.SubElement(machine, "Data")
    required = ET.SubElement(data, "StringArray", {"key": "requiredMods"})
    required.text = "%s~L~%s~%s" % (mod_id, version, mod_name)

    blocks = ET.SubElement(machine, "Blocks")
    placed = [0]                        # a counter the closures can advance

    def place(block_id, mod=None, local=None, facing=FACE_UP):
        spot = grid(placed[0], options.columns, options.spacing)
        placed[0] += 1
        return block(blocks, block_id, spot, mod, local, facing)

    # Every machine has one of these, and the game is happier when it is first.
    # Left in the orientation Besiege gives it: it is the machine's root, not one
    # of the instruments.
    place(STARTING_BLOCK, facing=(0.0, 0.0, 0.0, 1.0))

    # One instrument block per distinct voice, named so the timers can find it.
    voices = {}
    loudness = {}
    for start, length, pitch, velocity, channel, track in notes:
        voice = assign(pitch, channel, track, families, options)
        voices.setdefault(voice, len(voices))
        loudness.setdefault(voice, []).append(velocity)

    for voice, index in sorted(voices.items(), key=lambda kv: kv[1]):
        family, type_index, pitch = voice
        name, local, _, _ = families[family.lower()]
        # 1004 + localId is what this Besiege assigns Orchestra's blocks; the
        # loader recomputes it from modId and localId anyway.
        data = place(1004 + local, mod_id, local)
        # N is the block's own default key, kept so the registration loop runs.
        variable_key(data, "bmt-Activate",
                     "%s%03d" % (named(options.prefix), index), "N")
        value(data, "Integer", "bmt-TypeKey", str(type_index))
        value(data, "Single", "bmt-NoteKey", str(pitch))
        # One block, one note, one loudness: the score's velocities for this
        # pitch are averaged, since a block cannot be struck harder.
        mean = sum(loudness[voice]) / float(len(loudness[voice]))
        # Velocity 0..127 onto a third of the way up and no further than full:
        # a block set to the raw velocity of a quiet passage is a block nobody
        # hears, and the dynamics that matter are between the parts, not within.
        level = options.volume * (0.35 + 0.65 * mean / 127.0)
        value(data, "Single", "bmt-VolumeKey",
              "%.3f" % max(0.05, min(1.0, level)))
        value(data, "Single", "bmt-RangeKey", str(options.range))

    # One timer per note in the score.
    for start, length, pitch, velocity, channel, track in notes:
        voice = assign(pitch, channel, track, families, options)
        data = place(TIMER)
        if getattr(options, "variable", None):
            # A variable instead of the keyboard, which is what the loader block
            # writes when its own key is set to one. The keycode alongside is never
            # answered to -- with Use=True the key listens to the name -- but it has
            # to be there: Machine.InitSimBlock registers a key once per keycode it
            # holds, so a key with none is filed under no name and hears nothing.
            variable_key(data, TIMER_START, options.variable, options.key or "C")
        elif options.key:
            # Every timer waits its own time from the moment the key is pressed,
            # so one press starts the song. `automatic` would start it with the
            # simulation instead, which is what --key none asks for.
            keyed = ET.SubElement(data, "StringArray", {"key": TIMER_START})
            ET.SubElement(keyed, "String").text = options.key
        else:
            value(data, "Boolean", TIMER_AUTO, "True")
        value(data, "Single", TIMER_WAIT, "%.4f" % (start + options.offset))
        value(data, "Single", TIMER_HOLD, "%.4f" % max(0.05, length))
        # C is the timer's own default for this key, kept for the same reason.
        variable_key(data, TIMER_EMULATE,
                     "%s%03d" % (named(options.prefix), voices[voice]), "C")

    return machine, len(voices), placed[0]


def separate(notes, options, families):
    """Keeps two notes on the same block from running into each other.

    A key driven by variables counts its emulators: `MKey.UpdateEmulation` adds
    one on press and takes one away on release, and `Emulating` is "the count is
    above nought". So a second timer firing while the first still holds the same
    name takes the count from one to two, which is not a *press* -- the repeated
    note is silently dropped, and the note does not end until the last timer lets
    go. Repeated notes are half of most tunes, so the score is separated instead:
    each note is cut short of the next on its own block, and a note that would
    start inside that gap is dropped rather than lost silently.

    Returns the notes, and how many were dropped.
    """
    voices = {}
    for index, note in enumerate(notes):
        voices.setdefault(assign(note[2], note[4], note[5], families, options),
                          []).append(index)

    out = list(notes)
    drop = set()
    for indices in voices.values():
        indices.sort(key=lambda i: notes[i][0])
        for first, second in zip(indices, indices[1:]):
            start, length = out[first][0], out[first][1]
            next_start = out[second][0]
            if next_start - start < options.gap:
                drop.add(second)
                continue
            if start + length > next_start - options.gap:
                out[first] = (start, next_start - options.gap - start) + out[first][2:]
    return [n for i, n in enumerate(out) if i not in drop], len(drop)


def assign(pitch, channel, track, families, options):
    """Which block plays a note: (family, type index, pitch)."""
    if channel == 9 and not options.no_drums:
        family, piece = DRUM_MAP.get(pitch, ("Drums", "Snare"))
        name, _, types, fallback = families[family.lower()]
        return (name, pick_type(types, piece, fallback), DRUM_NOTE[piece])

    wanted = options.tracks.get(track, options.instrument)
    family, _, wanted_type = wanted.partition(":")
    if family.lower() not in families:
        raise SystemExit("no block called '%s'; there are: %s"
                         % (family, ", ".join(sorted(n for n, _, _ in families.values()))))
    name, _, types, fallback = families[family.lower()]
    return (name, pick_type(types, wanted_type, fallback), pitch + options.transpose)


def indent(node, depth=0):
    """Besiege writes its saves indented, and a diff of two of them should read."""
    pad = "\n" + "    " * depth
    if len(node):
        if not (node.text or "").strip():
            node.text = pad + "    "
        for child in node:
            indent(child, depth + 1)
        if not (node.tail or "").strip():
            node.tail = pad
        if not (node[-1].tail or "").strip():
            node[-1].tail = pad
    elif depth and not (node.tail or "").strip():
        node.tail = pad


# What people type, and what Unity calls it. Anything else is passed through as
# written -- Besiege parses the name with KeyCodeConverter, and a name it cannot
# parse is dropped when the save loads, so the spelling has to be Unity's.
KEY_ALIASES = {
    "enter": "Return", "return": "Return", "space": "Space", "spacebar": "Space",
    "shift": "LeftShift", "ctrl": "LeftControl", "control": "LeftControl",
    "alt": "LeftAlt", "tab": "Tab", "esc": "Escape", "escape": "Escape",
    "up": "UpArrow", "down": "DownArrow", "left": "LeftArrow", "right": "RightArrow",
}


DEFAULT_PREFIX = "orch_"


def named(prefix):
    """A prefix that can safely be a variable name, or the default.

    `MKey` joins several names with `;` and spells the whole thing
    `Message=a;b`, so a name carrying either character would be read back as two
    names or as none. Letters, digits, `_` and `-` are the whole of it. Kept in
    step with `Song.Named` in the mod, which does the same check for the block.
    """
    wanted = (prefix or "").strip()
    if not wanted or len(wanted) > 24:
        return DEFAULT_PREFIX
    for c in wanted:
        if not (c.isalnum() and c.isascii()) and c not in "_-":
            return DEFAULT_PREFIX
    return wanted


def keycode(name):
    """A key name as Unity spells it, or None for "start with the simulation"."""
    if not name:
        return None
    plain = name.strip()
    # The way to ask for no key at all, now that there is a key by default. The
    # block in game says the same thing by having nothing bound to its mapper.
    if plain.lower() in ("none", "off", "-"):
        return None
    if plain.lower() in KEY_ALIASES:
        return KEY_ALIASES[plain.lower()]
    if len(plain) == 1 and plain.isalpha():
        return plain.upper()                    # Unity: letters are A..Z
    if len(plain) == 1 and plain.isdigit():
        return "Alpha" + plain                  # and digits are Alpha0..Alpha9
    return plain


def instruments():
    """The families and their instruments, for --help.

    Read from the block XMLs rather than listed here, so a tenth block appears in
    the help the day it is added.
    """
    import textwrap
    try:
        found = catalogue()
    except SystemExit:
        return ""                       # run from outside the repo: no list to give

    lines = ["blocks, and the instruments each one holds:"]
    for name, _, types, _ in sorted(found.values()):
        listed = ", ".join(t for t in types if t)
        lines.append(textwrap.fill(listed, width=74,
                                   initial_indent="  %-9s " % name,
                                   subsequent_indent=" " * 12))
    lines += ["",
              "several at once, by track:",
              "  make-song.py song.mid --instrument \"Strings:Ensemble\" \\",
              "      --track 0=\"Piano:Grand piano\" --track 2=Bass",
              "",
              "channel 10 is General MIDI percussion whatever the tracks say, and",
              "goes to Drums and Cymbals by kit piece; --no-drums treats it as",
              "pitched instead. The score's own program changes are not read, so",
              "which part plays what is --track's to say -- and a format 0 MIDI",
              "keeps every part on one track, where --track cannot separate them."]
    return "\n".join(lines)


def saved_machines():
    """Besiege's own SavedMachines folder, or None."""
    for root in (os.environ.get("BESIEGE_DIR"),
                 os.path.expanduser("~/.steam/steam/steamapps/common/Besiege"),
                 os.path.expanduser("~/.local/share/Steam/steamapps/common/Besiege")):
        if root and os.path.isdir(os.path.join(root, "Besiege_Data", "SavedMachines")):
            return os.path.join(root, "Besiege_Data", "SavedMachines")
    for vdf in (os.path.expanduser("~/.steam/steam/steamapps/libraryfolders.vdf"),
                os.path.expanduser("~/.local/share/Steam/steamapps/libraryfolders.vdf")):
        if not os.path.isfile(vdf):
            continue
        for line in open(vdf):
            if '"path"' not in line:
                continue
            path = line.split('"')[3]
            spot = os.path.join(path, "steamapps", "common", "Besiege",
                                "Besiege_Data", "SavedMachines")
            if os.path.isdir(spot):
                return spot
    return None


def main():
    parser = argparse.ArgumentParser(
        description="Build a Besiege machine that plays a MIDI file on Orchestra "
                    "blocks: an instrument block per pitch, a timer block per note.",
        epilog=instruments(),
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("midi", nargs="?", help="the score, as a .mid file")
    parser.add_argument("-o", "--out", help="where to write the .bsg")
    parser.add_argument("--name", help="the machine's name in game")
    parser.add_argument("--instrument", default="Piano", metavar="FAMILY[:TYPE]",
                        help="the block every note goes to unless a --track says "
                             "otherwise (default: Piano). FAMILY is one of the nine "
                             "blocks and TYPE is one of that block's instruments, "
                             "as in --instrument \"Strings:Cello\"")
    parser.add_argument("--track", action="append", default=[],
                        metavar="N=FAMILY[:TYPE]",
                        help="the block for one track of the score, repeatable: "
                             "--track 0=\"Piano:Grand piano\" --track 2=Bass. "
                             "Each family, instrument and pitch gets its own block")
    parser.add_argument("--tempo", type=float,
                        help="override the file's tempo, in bpm")
    parser.add_argument("--transpose", type=int, default=0, help="in semitones")
    parser.add_argument("--offset", type=float, default=0.0,
                        help="seconds of quiet before the first note (default 0, "
                             "as the loader block's DELAY)")
    parser.add_argument("--gap", type=float, default=0.06,
                        help="silence between two notes on one block (default 0.06 s)")
    parser.add_argument("--from", dest="skip", type=float, default=0.0,
                        help="drop everything before this many seconds")
    parser.add_argument("--seconds", type=float,
                        help="stop after this many seconds of the score")
    parser.add_argument("--limit", type=int, default=1200,
                        help="most notes to place (default 1200)")
    parser.add_argument("--columns", type=int, default=0,
                        help="blocks per row (default: roughly square)")
    parser.add_argument("--spacing", type=float, default=1.0,
                        help="blocks apart, in block widths")
    parser.add_argument("--height", type=float, default=5.05,
                        help="where the machine spawns")
    parser.add_argument("--volume", type=float, default=0.7,
                        help="scales every block's volume")
    parser.add_argument("--range", type=float, default=300.0,
                        help="how far each block carries")
    parser.add_argument("--key", metavar="KEYCODE", default="M",
                        help="the key every timer waits for (default M, as the "
                             "loader block's own key mapper). --key none starts "
                             "the song with the simulation instead")
    parser.add_argument("--prefix", default=DEFAULT_PREFIX, metavar="NAME",
                        help="what the song's variables are named after "
                             "(default %s000, %s001, ...); worth changing when two "
                             "songs share a machine"
                             % (DEFAULT_PREFIX, DEFAULT_PREFIX))
    parser.add_argument("--variable", metavar="NAME",
                        help="the variable every timer waits for, instead of the "
                             "keyboard -- what the loader block does when its own "
                             "key is set to a variable rather than a key")
    parser.add_argument("--no-drums", action="store_true",
                        help="treat channel 10 as pitched, not as a kit")
    parser.add_argument("--install", action="store_true",
                        help="write into Besiege's SavedMachines as well")
    parser.add_argument("--self-test", action="store_true",
                        help="build from a made-up score and check the output")
    options = parser.parse_args()

    if options.self_test:
        return self_test(options)
    options.key = keycode(options.key)
    if not options.midi:
        parser.error("a MIDI file is needed (or --self-test)")

    options.tracks = {}
    for pair in options.track:
        number, _, family = pair.partition("=")
        if not family:
            raise SystemExit("--track wants N=Family, as in --track 1=Bass")
        options.tracks[int(number)] = family

    notes = Midi(options.midi).notes(options.tempo)
    if not notes:
        raise SystemExit("no notes in %s" % options.midi)

    start = notes[0][0] + options.skip
    notes = [n for n in notes if n[0] >= start]
    if options.seconds:
        notes = [n for n in notes if n[0] - start < options.seconds]
    # Zero the clock on the first note that survived.
    base = notes[0][0] if notes else 0.0
    notes = [(n[0] - base,) + n[1:] for n in notes]

    families = catalogue()
    notes, crowded = separate(notes, options, families)

    dropped = 0
    if len(notes) > options.limit:
        dropped = len(notes) - options.limit
        notes = notes[:options.limit]

    if not options.name:
        options.name = os.path.splitext(os.path.basename(options.midi))[0]
    if not options.columns:
        options.columns = max(1, int(math.ceil(math.sqrt(len(notes) + 40))))

    machine, voices, blocks = build(notes, options, families)
    indent(machine)
    text = ('<?xml version="1.0" encoding="utf-8"?>\n'
            '<!--Besiege machine save file.-->\n'
            + ET.tostring(machine).decode("utf-8") + "\n")

    out = options.out or os.path.join(REPO, options.name + ".bsg")
    open(out, "w").write(text)
    print("%s: %d notes, %d instrument block(s), %d blocks, %.1f seconds"
          % (os.path.basename(out), len(notes), voices, blocks,
             max(n[0] + n[1] for n in notes) + options.offset))
    if crowded:
        print("  %d note(s) fell inside another note on the same block, and went"
              % crowded)
    if dropped:
        print("  %d note(s) past --limit were dropped" % dropped)

    if options.install:
        folder = saved_machines()
        if folder is None:
            raise SystemExit("could not find Besiege's SavedMachines; set BESIEGE_DIR")
        copy = os.path.join(folder, os.path.basename(out))
        open(copy, "w").write(text)
        print("  installed to %s" % copy)
    return 0


def self_test(options):
    """A scale, built end to end, checked without the game."""
    import tempfile

    def varlen(n):
        out = bytearray([n & 0x7F])
        n >>= 7
        while n:
            out.insert(0, (n & 0x7F) | 0x80)
            n >>= 7
        return bytes(out)

    events = bytearray()
    events += b"\x00\xFF\x51\x03" + bytes([0x07, 0xA1, 0x20])       # 120 bpm
    for pitch in [60, 62, 64, 65, 67, 69, 71, 72]:
        events += varlen(0) + bytes([0x90, pitch, 100])
        events += varlen(480) + bytes([0x80, pitch, 0])   # a beat each, back to back
    # Middle C again, twice, the second starting while the first is still down:
    # the case a counted emulator would swallow.
    events += varlen(0) + bytes([0x90, 60, 100])
    events += varlen(240) + bytes([0x90, 60, 100])
    events += varlen(240) + bytes([0x80, 60, 0])
    events += varlen(0) + bytes([0x80, 60, 0])
    events += b"\x00\xFF\x2F\x00"
    track = b"MTrk" + struct.pack(">I", len(events)) + bytes(events)
    midi = b"MThd" + struct.pack(">I", 6) + struct.pack(">3h", 0, 1, 480) + track

    folder = tempfile.mkdtemp()
    path = os.path.join(folder, "scale.mid")
    open(path, "wb").write(midi)

    notes = Midi(path).notes()
    assert len(notes) == 10, "expected 10 notes, got %d" % len(notes)
    assert abs(notes[1][0] - 0.5) < 1e-6, "second note at %.3f s" % notes[1][0]
    assert abs(notes[0][1] - 0.5) < 1e-6, "note lasts %.3f s" % notes[0][1]

    options.tracks = {}
    options.gap = 0.06
    options.key = None
    options.name = "Self test"
    options.columns = 4

    # The two middle Cs overlap, so the first is cut short of the second and both
    # still sound; nothing is dropped, because they start far enough apart.
    families = catalogue()
    notes, crowded = separate(notes, options, families)
    assert crowded == 0, "%d note(s) dropped as crowded" % crowded
    middle = sorted(n for n in notes if n[2] == 60)
    assert len(middle) == 3, "expected 3 middle Cs, got %d" % len(middle)
    assert middle[1][0] + middle[1][1] <= middle[2][0] - options.gap + 1e-6, \
        "a repeat runs into the next: ends %.3f, next starts %.3f" \
        % (middle[1][0] + middle[1][1], middle[2][0])

    machine, voices, blocks = build(notes, options, families)
    assert voices == 8, "expected 8 instrument blocks, got %d" % voices
    assert blocks == 1 + 8 + 10, "expected 19 blocks, got %d" % blocks

    text = ET.tostring(machine).decode("utf-8")
    parsed = ET.fromstring(text)                    # it has to be XML
    timers = [b for b in parsed.iter("Block") if b.get("id") == str(TIMER)]
    assert len(timers) == 10, "expected 10 timers, got %d" % len(timers)
    waits = sorted(float(v.text) for t in timers
                   for v in t.iter("Single") if v.get("key") == TIMER_WAIT)
    assert abs(waits[1] - waits[0] - 0.5) < 1e-4, "timers %.3f s apart" % (waits[1] - waits[0])
    names = set(s.text for t in timers for s in t.iter("String")
                if s.text.startswith("Message="))
    assert len(names) == 8, "expected 8 variables, got %d" % len(names)

    # Every variable key keeps a keycode: without one, Machine.InitSimBlock never
    # registers it and the machine plays nothing.
    for keyed in parsed.iter("StringArray"):
        entries = [e.text for e in keyed]
        if not entries:
            continue                    # requiredMods, which is one inline value
        assert not entries[0].startswith(("Message=", "Use=")), \
            "a variable key with no keycode: %s" % entries

    # One flat field, not a wall: every *block* at the same height, the machine's
    # own spawn position aside.
    heights = set(p.get("y") for b in parsed.iter("Block")
                  for p in b.iter("Position"))
    assert heights == set(["0"]), "blocks are not all at one height: %s" % heights

    # And standing up, the starting block aside.
    facing = set(r.get("x") for b in parsed.iter("Block") for r in b.iter("Rotation"))
    assert facing == set(["0.0", str(FACE_UP[0])]), "not facing up: %s" % facing

    # Timers start with the simulation unless a key is asked for.
    assert all(t.find("Data/Boolean[@key='%s']" % TIMER_AUTO) is not None
               for t in timers), "a timer does not start with the simulation"
    # A variable in place of the keyboard: the timers listen to the name, and the
    # keycode has to be there to be counted -- see variable_key.
    options.key = keycode("M")
    options.variable = "start-me"
    varied, _, _ = build(notes, options, families)
    started = [t for t in varied.iter("StringArray") if t.get("key") == TIMER_START]
    assert len(started) == 10, "expected 10 started timers, got %d" % len(started)
    said = [e.text for e in started[0]]
    assert said == ["M", "Message=start-me", "Use=True"], \
        "a timer on a variable reads %s" % said
    assert not [t for t in varied.iter("Boolean") if t.get("key") == TIMER_AUTO], \
        "a timer on a variable is still automatic"
    options.variable = None

    assert named("") == DEFAULT_PREFIX, "an empty prefix falls back"
    assert named("a;b") == DEFAULT_PREFIX, "a prefix with a semicolon falls back"
    assert named("song2_") == "song2_", "a plain prefix is kept"

    assert keycode("none") is None, "--key none should mean no key at all"
    assert keycode("M") == "M", "a plain letter should stay itself"
    options.key = keycode("space")
    assert options.key == "Space", "key alias: %s" % options.key
    keyed, _, _ = build(notes, options, families)
    started = [t for t in keyed.iter("StringArray") if t.get("key") == TIMER_START]
    assert len(started) == 10, "expected 10 keyed timers, got %d" % len(started)
    assert started[0][0].text == "Space"
    assert not [t for t in keyed.iter("Boolean") if t.get("key") == TIMER_AUTO], \
        "a keyed timer is still automatic"
    assert len([b for b in parsed.iter("Block") if b.get("modId")]) == 8
    print("self test: %d notes, %d blocks, timers %.2f s apart, %d variables"
          % (len(notes), blocks, waits[1] - waits[0], len(names)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
