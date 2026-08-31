# Machines that play a song

There are two ways to turn a MIDI file into a Besiege machine that plays it on
Music blocks, and they write the same machine.

**In the game**, the **Loader** block — the download arrow — reads a file, says
what it comes to, and either adds those blocks to the machine you are building or
saves them as a machine of their own.

**Outside it**, `tools/make-song.py` does the same job with more control over
which part goes where:

```sh
./tools/make-song.py travelers.mid --instrument "Piano:Grand piano" --install
```

Neither has any dependencies: standard Python for the tool, and the MIDI parser is
written out in both.

## The loader block

Place it, click it, and Besiege's own menu keeps **the key and nothing else** --
every timer the block writes waits for that key, so binding it there binds the
whole song, and rebinding is the mapper's own business. Everything else is in the
panel docked underneath: the folder, the file, the two selectors (which block, and
which instrument within it), volume, range, transpose and delay, the summary, and
the two buttons. With no key bound the timers start with the simulation instead.

The file list is a dropdown rather than the game's `< choice >` stepper -- a
folder of thirty files is thirty presses of an arrow. Its open list is shortened
to fit what is underneath it: a uGUI `Dropdown` parents that list to itself, so it
is drawn inside this window's own scroll mask and anything past the window's edge
is simply not there.

The panel leads with the folder to put files in, because that is the first thing
anybody needs:

```
Besiege_Data/Mods/Data/Music_<mod id>/Songs
```

The path is a button — click it and the folder opens in your file manager. The
reload arrow beside it lists that folder again, so a file dropped in while the
game is running does not need a restart or a fresh block. Everything in it is
listed underneath, one clickable row each, and the one you chose is written in
white.

`ModIO` will not say where that folder is — everything it takes and returns is
relative — so the path is rebuilt from the pieces the game builds it from:
`StaticSettings.DataPath + "/Mods/Data/" + name-without-spaces + "_" + guid`.
That path is for *showing*, though. A file chosen from the list is remembered by
**name alone** and read back relative to the mod's data folder, which is the form
`ModIO` is meant for and the one that cannot be wrong about where that folder is;
an absolute path is only used for something typed by hand or picked from the
system dialog, and even then the name is tried in the Songs folder first.

### Tempo

The panel's TEMPO slider is a readout most of the time and a setting when it is
wanted. Reading a file sets it to that file's own starting tempo, and picking
another file sets it again; moving it or typing into it stops that, until the next
file.

The two states are not the same conversion. Following the file passes
`SongOptions.Tempo = 0`, which keeps the file's **whole tempo map** -- a score that
slows down for eight bars still slows down. A number in the slider replaces the map
with one tempo for the length of the piece. That is why "follow the file" is a
control of its own rather than a comparison against the number in the box: the two
would look identical at the moment a file is read and behave differently after it.

Both are saved with the machine, being ordinary mapper controls.

MIDI files in the wild carry some remarkable tempos. The Chopin nocturne bundled
here arrived from Online Sequencer claiming **999 bpm**, which put a four-minute
piece into thirteen seconds; the file was wrong, not the converter, and it has been
rewritten to the 50 bpm the notes were written for. The slider is what makes the
next one survivable -- and the summary line says which tempo the length above it
was worked out at, so an absurd one shows up before a thousand blocks are placed
for it.

### Playing a score on the instruments it names

A MIDI file says what each of its parts is -- a program change per channel,
`Violin`, `Jazz Guitar`, `Tenor Sax`. Until `Gm` existed the converter threw all of
that away and played every melodic part on whichever single block the panel was
set to, so a file naming eight instruments came out as eight tracks of piano.
(Percussion was never in that: channel 10 is a kit by convention rather than by
program change, and its note *numbers* have always mapped onto Drums and Cymbals.)

Set INSTRUMENT to **As the file says** — or `--instrument file` — and each part
goes where it says. `Gm` is a 128-entry table from General MIDI's instruments to
`Family:Type`, and `SongCheck` holds every one of its entries to a block and an
instrument this mod actually has.

Eleven blocks against 128 instruments, and the approximations are down to the
sound effects: General MIDI's synth leads, pads and effects have the **FM Synth**
block, which is what most of those presets were on the machines they came from.
The **Braids** block takes them by default -- twenty-three models against two
operators is the fuller synthesiser -- and the FM block is what they fall back to.
Braids was a mod of its own and had to be asked for with `--braids`; it is one of
these blocks now, and `--no-braids` is what asks for the other one. Every entry in
`Gm.cs` carries the reasoning, and the guesses say they are guesses. The families
that map exactly — guitars, basses, solo strings, brass, reeds, pipes, tuned and
plucked percussion, organs, choirs — are most of what a real score uses.

**It costs blocks.** A part per instrument is a block per instrument *per pitch*,
so the same note limit covers less music:

| song | one instrument | as the file says |
| --- | --- | --- |
| Sax | 21 blocks, 59 s | 43 blocks, 44 s |
| Outer Wilds - Travelers | 24 blocks, 42 s | 42 blocks, 42 s |
| Never Gonna Give You Up | 37 blocks, 88 s | 82 blocks, 75 s |
| Shelter | 35 blocks, 171 s | 134 blocks, 39 s |

Shelter is the warning: ten declared parts, and asking for all of them turns a
machine that played most of the song into one that plays the first forty seconds.
The note limit is the lever, and one instrument for everything is still there for
when a song is worth more than its orchestration.

### Percussion is expensive

A timer per note, and a drum track is the densest thing in a pop arrangement --
hi-hats on every eighth for four minutes. `Rick Astley - Never Gonna Give You Up`
arrived with 3097 notes on channel 10 against 4246 for the whole of the rest of the
song: the kit alone was more than twice the note limit, so the machine was mostly
hi-hat and stopped 49 seconds in.

`tools/strip-drums.py` removes everything on channel 10 -- where General MIDI puts
the kit -- and leaves every other channel exactly as it was. That file now reaches
88 seconds on the same note budget.

```sh
./tools/strip-drums.py "Music/Songs/Some Song.mid" --check   # say what is there
./tools/strip-drums.py "Music/Songs/Some Song.mid"           # take it out
```

It is not `make-song.py --no-drums`, which keeps those notes and plays them
*pitched* -- a kit banged out on a piano.

Two details it has to get right, and does:

* **Deltas are recomputed, not patched.** A MIDI event's time is relative to the
  one before it, so deleting an event moves everything after it earlier unless the
  gap is carried. Tracks are read to absolute ticks, filtered, and re-emitted with
  fresh deltas — verified by comparing every surviving note's `(tick, channel,
  pitch, velocity, track)` before and after, which must be identical.
* **Emptied tracks stay.** `make-song.py --track N=Piano` addresses tracks by
  number, so dropping one would silently repoint every mapping past it.

### The note limit

A timer per note, so the note limit is most of the block count: 700 notes is a
machine of about 760 blocks. It is the panel's NOTE LIMIT slider, 700 by default
and 10000 at the most, and `--limit` in `tools/make-song.py` with the same default.
The default came down from 1200 when each part started getting its own instrument:
the same score now wants a block per instrument *per pitch*, and 700 is a machine
that still builds and runs in a level.

Unlike TEMPO it does not follow the file. What the number is for is how large a
machine you are willing to run, which has nothing to do with how many notes the
score happens to have; the summary says how many the current number leaves behind,
redrawn when the drag settles rather than on every frame of it -- reading a long
score is not something to do sixty times a second.

Notes past the limit are dropped from the end, after everything else has been
worked out, so a truncated song is the beginning of the piece rather than a thinned
version of the whole of it.

### Songs that ship with the mod

`ModIO` has two roots, chosen by the `data` flag every one of its methods takes:
the mod's **data** folder above, which the player can write to, and the mod's
**own** folder, which is the directory a Workshop subscription downloads into.
Anything in `Music/Songs/` is read from the second and listed with
`(built-in) ` in front of its name.

Nothing has to be declared for it. A MIDI file is read as bytes rather than loaded
as a resource, and a mod's folder is uploaded to the Workshop **whole** —
`ModListUI.CreateUploadData` sets `UploadData.Path` to `ModInfo.Directory` and
`IsFolder` true, and `Mod.xml` never filters it — so a file dropped into that
folder ships with the mod as it stands.

The mark is not decoration. All the block keeps of a chosen song is its *name*, so
the name is what has to say which of the two folders to read it back out of, and
it is also what keeps a bundled song and a player's own of the same name from
being one entry. Built-ins are listed after the player's own files: they are the
same for everybody, and what somebody put in their own folder is what they opened
the list for.

An update replaces the mod's folder whole, so nothing a player made belongs in it.
That is what the data folder is for, and it is what every button on the panel
points at.

There is no "browse" button, and cannot usefully be one: Besiege ships a file
dialog (`SFB.StandaloneFileBrowser`, which the game itself never calls) that can
show you the whole disk, and `ModIO` will open nothing outside the mod's own
folders. The file has to be *in* that folder.

The summary is written from the converted machine rather than from the file, so
the numbers are the ones the machine will really have: its length, how many notes
survived the separation below, and the blocks it will cost.

**ADD TO MACHINE** is Besiege's own additive load, the one the load screen's "add
to machine" button runs, so the blocks arrive selected with the move tool up and
one undo takes them all away again. They are laid out around the loader block, so
they appear where you are looking.

**SAVE AS MACHINE** does the same and then opens Besiege's own save screen over
it, where **SELECTION ONLY** saves exactly the blocks that were just added. It has
to: a mod cannot write a machine file. `XmlSaver.Save` is one of the four methods
the mod loader forbids by name and every caller of it is private, and `ModIO` --
the only file API a mod has -- refuses any path outside the mod's own folders. The
game naming the file is no loss: it also asks before overwriting and renders the
thumbnail. Where that screen cannot be found, the `.bsg` is written into
`Mods/Data/Music_<id>/Machines/` instead and the panel says so.

What the loader cannot do that the tool can: several instruments at once (it puts
every pitched part on one block family), a slice of a score, or a tempo of its own.

## Why a score and not a YouTube link

A recording has to be turned back into notes before anything can play it, and
that is **polyphonic transcription** — an open research problem. The best of the
free models (Spotify's `basic-pitch`, Magenta's `onsets-and-frames`) are a
~100 MB neural network each, want `tensorflow` or `torch`, and on solo piano get
most of the melody, some of the accompaniment, and a scattering of notes that are
not there. Piano is their best case; anything with drums or several instruments is
worse. On top of that a YouTube video has to be downloaded and demuxed first,
which is another dependency and someone else's terms of service.

A score is already the notes, exactly, with their lengths and their tempo — the
thing transcription is trying and failing to recover. So the tool takes MIDI.

**Getting a MIDI.** MuseScore exports one from any score in its library
(*Download → MIDI*, or File → Export in the desktop app). Most game and film
soundtracks have a transcription there already. For the Outer Wilds example:
<https://musescore.com/user/36615991/scores/20587660>.

If you would rather transcribe audio anyway, the tool still takes it from there:
run `basic-pitch` over a downloaded track and hand the MIDI it writes to
`make-song.py`. Expect to edit it.

## How the machine works

The same for both, and `tools/tests/SongCheck.cs` holds the in-game converter to
it at build time: it converts a made-up scale, writes the machine, reads it back,
and checks the things that make a machine play rather than sit there.

Two kinds of block, laid out in a grid on the ground, all at one height and all
turned to stand up — the quarter turn about X that Besiege's own saves give a
block placed on a flat surface, so the instruments face the sky rather than lying
on their sides:

- an **instrument block** per distinct pitch, set to that note and nothing else —
  a Music block plays one note, so a tune is a row of blocks;
- a **timer block** per note in the score, `wait` set to the note's moment and
  `emulation-time` to its length. Each timer waits its own time from the key --
  `M` unless `--key` says otherwise -- so one press starts the band. With
  `--key none` the timers are `automatic` instead and the song starts with the
  simulation, which is what a loader block with nothing bound to its mapper does.

They are joined by **variables**, not keys. A Besiege key can carry a *message* —
a variable name — and `KeyInputController` keeps a table of which keys listen to
which name, so a timer emulating `orch_042` presses every key that names it.
Keyboard keys would do the same job and there are about a hundred of them;
variable names are unlimited, which is what a song needs.

The `wait` and `emulation-time` sliders are declared with `AddSliderUnclamped`, so
a note four minutes in is one timer with `wait=240`, not a chain of them.

Nothing is connected to anything. The blocks are laid out, not built: Besiege
loads them all the same and they drop to the ground when the simulation starts,
which does not stop the music. Raise `--height` if you would rather they fell
further, or lower it to keep them still.

## Why every key still names a keycode

A key set to a variable in the save keeps a keycode entry it never answers to:

```xml
<StringArray key="bmt-Activate">
    <String>N</String>
    <String>Message=orch_007</String>
    <String>Use=True</String>
</StringArray>
```

`Machine.InitSimBlock` registers a key with `KeyInputController` from inside
`for (i = 0; i < key.KeysCount; i++)`, and `AddMKey` is what files it under its
variable name. No keycodes, no iterations, no registration — the block never
joins the table the timers look names up in, and the machine plays nothing at
all. That was the first version of this tool, and it looked exactly like the
blocks not supporting emulation.

The keyboard cannot trigger the block either way: `AddMKey` files a key under its
name *or* its keys, never both, and `Use=True` chooses the name. The keycode is
there to be counted. In game the question never arises, because
`KeySelector.SetVariable` sets the name and leaves the block's own key alone.

## Repeated notes, and why there is a gap

An emulated key is **reference counted**: `MKey.UpdateEmulation` adds one on
press and takes one away on release, and a key is "down" while the count is above
nought. A second timer firing while the first still holds the same name takes the
count from one to two — which is not a press, so the repeated note would never
sound, and the note would not end until the last timer let go.

So the tool separates the score per block: every note is cut short of the next one
on its own block by `--gap` (60 ms by default), and a note that would start inside
that gap is dropped and counted rather than lost silently. Sixty milliseconds is
below what a listener notices as a shortened note and above a fixed step, which is
what the counter is sampled on.

## Options

`./tools/make-song.py --help` lists every block and the instruments it holds,
read from the block XMLs, so it is never out of date.

| Option | What it does |
| --- | --- |
| `--instrument FAMILY[:TYPE]` | the block every note goes to unless a `--track` says otherwise (default `Piano`) |
| `--track N=FAMILY[:TYPE]` | the block for one track, repeatable |
| `--tempo BPM` | ignore the file's tempo map |
| `--transpose N` | in semitones |
| `--offset S` | quiet before the first note (default 0 s) |
| `--key KEYCODE` | the key every timer waits for (default `M`) |
| `--from S`, `--seconds S` | play part of the score |
| `--gap S` | silence between two notes on one block (default 0.06 s) |
| `--limit N` | most notes to place (default 700) |
| `--columns N`, `--spacing N` | the grid |
| `--height N` | where the machine spawns |
| `--volume N`, `--range N` | scales every block's volume; how far they carry |
| `--no-drums` | treat channel 10 as pitched rather than as a kit |
| `--install` | write into Besiege's `SavedMachines` as well |
| `--self-test` | build from a made-up score and check the output |

### Starting on a variable

A Besiege key can carry a *message* — one or more variable names — and listen to
that instead of the keyboard; `KeyInputController` keeps a table from name to the
keys registered under it. Set the loader block's own Start mapping to a variable
and the timers it writes are given the same name, so whatever raises that variable
starts the song. `--variable NAME` does the same from the command line.

**The timers still carry a keycode, and never answer to it.**
`Machine.InitSimBlock` files a block's keys with `KeyInputController` inside
`for (i = 0; i < key.KeysCount; i++)`, and it is that loop that puts a key into the
by-name table. A key with no keycodes runs the loop no times, joins no table and
hears nothing — silently, in a way that looks exactly like a block that does not
support emulation. So a timer on a variable is written as

```
["M", "Message=start-me", "Use=True"]
```

where `M` is there to be counted. With `Use=True` the key answers to the name and
not to the keyboard, so which keycode it is does not matter; the block's own is
used when it has one, and `C` when it does not.

The tool's defaults are the loader block's: instrument Piano, volume 1, range 120,
transpose 0, offset 0, note limit 700, and `M` as the key. `--key none` is how you
ask for a song that starts with the simulation, which is what the block says by
having nothing bound to its mapper.

`--key` takes a Unity key name — `Space`, `Return`, `Alpha1`, `LeftShift`, or a
single letter. Common spellings are corrected (`space`, `enter`, `1`, `k`), and
anything else is passed through as written: Besiege parses the name when the save
loads and quietly drops one it does not recognise.

Each family, instrument and pitch gets its own block, so a piano C4 and a cello
C4 are two blocks with two variables and several parts can play at once:

```sh
./tools/make-song.py song.mid --instrument "Strings:Ensemble" \
    --track 0="Piano:Grand piano" --track 2=Bass --track 3="Brass:Trumpet"
```

The score's own program changes — its "this part is a flute" — are parsed and
then ignored, so which part plays what is `--track`'s to say. Two things follow
from that: a **format 0** MIDI keeps every part on one track, where `--track`
cannot separate them (MuseScore exports format 1, a track per staff, so this
mostly bites on files from elsewhere); and there is no listing of what the tracks
are, so a MIDI from an unfamiliar source is worth opening in a score editor first.

Channel 10 is General MIDI percussion whatever the tracks say, and is mapped onto
the struck blocks: kick, snare and toms to **Drums**; hi-hat, crash and ride to
**Cymbals**. Anything unrecognised becomes a snare.

Velocity sets each block's volume, averaged over that pitch's notes — a block
cannot be struck harder, so the dynamics that survive are between parts rather
than within them.

## What to expect in game

A few hundred blocks is a machine Besiege loads happily; a few thousand is a
machine that loads slowly and simulates slowly, and the timers are what multiply.
`--limit` caps it, `--seconds` cuts the score, and a busy orchestral MIDI is worth
thinning in a score editor first.

The instruments are heard from where they stand, so a wide grid spreads the band
across the stereo field. Fly the camera into the middle of it.
