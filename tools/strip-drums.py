#!/usr/bin/env python3
"""Takes the percussion out of a MIDI file.

    ./tools/strip-drums.py "Music/Songs/Some Song.mid"
    ./tools/strip-drums.py song.mid --check        # say what is there, change nothing
    ./tools/strip-drums.py song.mid -o out.mid     # write elsewhere

General MIDI puts the kit on **channel 10** -- index 9 -- where the note number
picks a drum rather than a pitch. Everything on that channel goes; every other
channel is left exactly as it was.

Why a mod for musical instruments would want this: a timer per note. A drum track
is the densest thing in a pop arrangement -- hi-hats on every eighth for four
minutes -- and it can be more notes than the whole of the rest of the score, so it
eats the block budget and leaves the tune truncated. `--no-drums` on
`make-song.py` is a different thing: it keeps those notes and plays them *pitched*,
which is a kit banged out on a piano.

**Deltas are recomputed, not patched.** A MIDI event's time is relative to the one
before it, so deleting an event silently moves everything after it earlier. This
reads each track to absolute ticks, drops what it is told to, and writes fresh
deltas -- which is also why every event comes back with an explicit status byte
rather than running status: correct, and a few per cent larger.

**Tracks are kept even when emptied.** `make-song.py --track N=Piano` addresses
tracks by number, so removing one would silently repoint every mapping past it. A
track with nothing left in it contributes no notes and costs a few bytes.
"""

import os
import struct
import sys

DRUM_CHANNEL = 9        # "channel 10", counting from one, as General MIDI says


def varlen(data, at):
    value = 0
    while True:
        byte = data[at]
        at += 1
        value = (value << 7) | (byte & 0x7F)
        if not byte & 0x80:
            return value, at


def write_varlen(value):
    out = bytearray([value & 0x7F])
    value >>= 7
    while value:
        out.insert(0, 0x80 | (value & 0x7F))
        value >>= 7
    return bytes(out)


def events(data):
    """One track as (tick, status, payload), running status resolved."""
    out = []
    at = 0
    tick = 0
    status = 0
    while at < len(data):
        delta, at = varlen(data, at)
        tick += delta
        if at >= len(data):
            break
        byte = data[at]
        if byte & 0x80:
            status = byte
            at += 1

        if status == 0xFF:
            kind = data[at]
            at += 1
            size, at = varlen(data, at)
            out.append((tick, status, bytes([kind]) + write_varlen(size)
                        + data[at:at + size]))
            at += size
        elif status in (0xF0, 0xF7):
            size, at = varlen(data, at)
            out.append((tick, status, write_varlen(size) + data[at:at + size]))
            at += size
        else:
            high = status & 0xF0
            width = 1 if high in (0xC0, 0xD0) else 2
            out.append((tick, status, data[at:at + width]))
            at += width
    return out


def rebuilt(kept):
    """Events back into a track chunk, with deltas worked out afresh."""
    body = bytearray()
    last = 0
    for tick, status, payload in kept:
        body += write_varlen(tick - last)
        body.append(status)
        body += payload
        last = tick
    return b"MTrk" + struct.pack(">I", len(body)) + bytes(body)


def counted(track):
    """Notes per channel in one track's events."""
    tally = {}
    for _, status, payload in track:
        if status & 0xF0 == 0x90 and len(payload) > 1 and payload[1] > 0:
            channel = status & 0x0F
            tally[channel] = tally.get(channel, 0) + 1
    return tally


def name_of(track):
    for _, status, payload in track:
        if status == 0xFF and payload and payload[0] == 0x03:
            size, at = varlen(payload, 1)
            try:
                return payload[at:at + size].decode("latin-1").strip()
            except Exception:
                return ""
    return ""


def main():
    args = [a for a in sys.argv[1:]]
    check = "--check" in args
    if check:
        args.remove("--check")
    out_path = None
    if "-o" in args:
        at = args.index("-o")
        out_path = args[at + 1]
        del args[at:at + 2]
    if len(args) != 1:
        raise SystemExit(__doc__.strip().splitlines()[0]
                         + "\n\n  ./tools/strip-drums.py <file.mid> [-o out.mid] [--check]")
    path = args[0]

    data = open(path, "rb").read()
    if data[:4] != b"MThd":
        raise SystemExit("%s is not a MIDI file" % path)
    header_len = struct.unpack_from(">I", data, 4)[0]
    fmt, count, division = struct.unpack_from(">3h", data, 8)

    at = 8 + header_len
    tracks = []
    while at < len(data) and len(tracks) < count:
        if data[at:at + 4] != b"MTrk":
            break
        size = struct.unpack_from(">I", data, at + 4)[0]
        tracks.append(events(data[at + 8:at + 8 + size]))
        at += 8 + size

    print("%s: format %d, %d track(s), %d ticks per beat"
          % (os.path.basename(path), fmt, len(tracks), division))

    chunks = [data[:8 + header_len]]
    removed = 0
    kept_notes = 0
    for index, track in enumerate(tracks):
        before = counted(track)
        drums = before.get(DRUM_CHANNEL, 0)
        kept = [e for e in track
                if e[1] >= 0xF0 or (e[1] & 0x0F) != DRUM_CHANNEL]
        after = counted(kept)
        removed += drums
        kept_notes += sum(after.values())
        if drums:
            print("  track %2d  %-24s %d percussion note(s) removed, %d left"
                  % (index, name_of(track)[:24], drums, sum(after.values())))
        chunks.append(rebuilt(kept))

    print("  %d percussion note(s) in all; %d note(s) left" % (removed, kept_notes))
    if check:
        print("  --check: nothing written")
        return 0
    if removed == 0:
        print("  nothing on channel %d, so nothing to do"
              % (DRUM_CHANNEL + 1))
        return 0

    where = out_path or path
    open(where, "wb").write(b"".join(chunks))
    print("  written to %s" % where)

    # Read it back, because a MIDI file that no longer parses is a thing to find
    # out about here rather than in the game.
    again = open(where, "rb").read()
    if again[:4] != b"MThd":
        raise SystemExit("what was written is not a MIDI file")
    fmt2, count2, division2 = struct.unpack_from(">3h", again, 8)
    if (fmt2, count2, division2) != (fmt, count, division):
        raise SystemExit("the header changed: %s -> %s"
                         % ((fmt, count, division), (fmt2, count2, division2)))
    check_at = 8 + struct.unpack_from(">I", again, 4)[0]
    seen = 0
    total = 0
    while check_at < len(again) and seen < count2:
        if again[check_at:check_at + 4] != b"MTrk":
            raise SystemExit("track %d is not where it should be" % seen)
        size = struct.unpack_from(">I", again, check_at + 4)[0]
        track = events(again[check_at + 8:check_at + 8 + size])
        tally = counted(track)
        if DRUM_CHANNEL in tally:
            raise SystemExit("track %d still has percussion in it" % seen)
        total += sum(tally.values())
        check_at += 8 + size
        seen += 1
    if seen != count2 or total != kept_notes:
        raise SystemExit("read back %d track(s) and %d note(s), expected %d and %d"
                         % (seen, total, count2, kept_notes))
    print("  read back: %d track(s), %d note(s), no percussion" % (seen, total))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
