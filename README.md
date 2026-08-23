# Besiege Orchestra

Nine instrument blocks, in [Besiege](https://store.steampowered.com/app/346010/Besiege/).

![The nine blocks: piano, electric guitar, acoustic bass, violin, trumpet, saxophone, xylophone, drum and cymbal](Promo_1.jpg)

Piano, guitar, bass, strings, brass, woodwind, mallets, drums and cymbals. Each
block plays one note on one key, heard from where the block stands — so a machine
can carry a band, and you hear it move.

**[UI Factory](https://steamcommunity.com/sharedfiles/filedetails/?id=2913469777)**
(another Besiege mod which enables the nice UI, see workshop item `2913469777`) is
optional here. With it the blocks get a panel docked under the block mapper;
without it they use Besiege's ordinary mapper and everything still works.

## Install

Either subscribe to the mod on Steam, or if you don't use Steam you can clone the repo then:

```sh
./tools/install.sh              # symlink into Besiege_Data/Mods
./tools/install.sh --copy       # copy instead
./tools/install.sh --uninstall
```

Set `BESIEGE_DIR` if your install isn't found automatically. Start Besiege, enable
**Orchestra** in the mods menu, and the nine blocks appear in the toolbar. No C#
toolchain is needed; the build uses Besiege's own compiler.

## Options

Every block has these:

| Setting | What it does |
| --- | --- |
| Play | Key that sounds the note. Default `N` |
| Instrument | Which instrument of that family |
| Note | Pitch, as a MIDI note number. 60 is middle C |
| Volume | 0 to 1 |
| Range | How far it carries before falling silent |
| Toggle | On, the key starts and stops. Off, it plays while held |

**Toggle** is only on strings, brass and woodwind. Latching the key means nothing
on something struck or plucked: those notes die on their own whatever the key
does.

Each block adds its own on top:

| Block | Adds |
| --- | --- |
| Piano | Sustain, Release |
| Guitar | Pluck, Palm mute |
| Bass | Slap |
| Strings | Pizzicato, Vibrato |
| Brass | Mute, Attack, Vibrato |
| Woodwind | Breath, Vibrato |
| Mallets | Hardness, Motor |
| Drums | Tune, Decay, Damping |
| Cymbals | Size, Open |

They mean what they say rather than switching to another recording. **Pizzicato**
drops the loop out of a bowed note and cuts its tail; **Mute** and **Palm mute**
are the same low-pass, because a hand on the strings and a mute in a bell come to
the same thing; **Size** on a cymbal lengthens the decay *and* crowds the
partials, because that is what a bigger plate does.

Sliders are clipped but out-of-range values can be typed.

| Setting | Slider | Typed |
| --- | --- | --- |
| Note | 21–108 (a piano's keys) | 0–127 |
| Range | 5–500 m | 0.5 m – 2 km |
| Attack | 0.005–1.5 s | up to 60 s |
| Decay | 0.05–3 s | up to 60 s |
| Release | 0.05–6 s | up to 60 s |

## The panel

With UI Factory installed, opening a block brings up a panel docked to the bottom
edge of the block mapper, the same width, following it as you drag it. The
instrument sits in the game's own `< Grand piano >` selector, the note is shown as
a note name and snaps to semitones, and there is a row for every control the block
declared — so a new instrument gets its panel for nothing.

The speaker at the bottom left plays the block where it stands, with the settings
as they are, so an instrument can be chosen by ear without starting the machine.
The block's toggles share the rest of that row.

The mapper above keeps the key and nothing else: two panels for one block is one
too many, and rebinding needs Besiege's own key capture. Without UI Factory the
mapper keeps everything, which is what makes it a fallback rather than a second
copy.

## One block, one note

A block plays a single note, and a tune is a row of blocks each triggered by its
own variable. That is not a shortcut — Besiege binds automation variables to keys
and to nothing else, so a note slider could never be driven by a variable.

Blocks are still polyphonic, eight voices each. That is not for chords, which come
from separate blocks, but so striking a block again does not cut off what is still
ringing.

A block swells when it plays, and goes on breathing while a held note lasts. It is
the visual that moves and not the block, so a machine does not turn springy
because it is playing.

## Sound

**Synthesised** — mallets, drums, cymbals. A bank of decaying partials for
anything metal: rigid metal rings at frequencies that are not harmonics, which is
why a crash starts bright and ends as a hum, and why **Size** and **Decay** here
change the physics rather than pick a different recording. Drums are a pitched
body that falls as it decays, plus noise for the skin.

**Sampled** — piano, guitar, bass, strings, brass, woodwind. Three recorded notes
per instrument, so pitch-shifting never stretches more than about three semitones.
The 84 clips are cut from [GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS)
at build time and come to 776 KB; see [docs/SAMPLES.md](docs/SAMPLES.md) for how,
and how to cut them from a different font.

Sound is generated in `OnAudioFilterRead`, which the mixer calls on the buffer it
is about to play, so a note starts when the key does. The obvious alternative — a
streaming `AudioClip` fed through a PCM reader callback — is read well ahead of
being heard and answers the key late. So each block places itself instead: a gain
per ear, worked out each frame from where it stands relative to the listener.

## Adding an instrument

No code. Each block's XML declares its own types and controls:

```xml
<Type name="Vibraphone" engine="modal" decay="4.0" brightness="0.45"
      inharmonicity="1.18" noise="0.08" />
<Extra kind="slider" key="MotorKey" name="Motor" min="0" max="1" default="0" />
```

`engine` is `modal`, `drum` or `sampler`. A sampled type names its clips instead,
`samples="piano_grand_36 piano_grand_60 piano_grand_84"`, where the trailing
number is the MIDI note each was recorded at.

Each block wears its own instrument: low-poly models from
[Poly Pizza](https://poly.pizza), converted and stood upright by
`tools/make-block-meshes.py`. They carry no textures of their own, only a flat
colour per material, so the tool gathers those into a small palette and points
each triangle at its own patch — which is why a block's texture is a few dozen
bytes.

## Notes

Details land in `Player.log` and in the in-game console with `show_logs true`.

AI agent? see [AGENTS.md](AGENTS.md) for layout, build, and any relevant info.
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md) has what this mod had to work out
about Besiege's modding API — including how to dock a UI Factory window to the
block mapper, which is worth reading before trying it.

## Credits

The block models are Creative Commons Attribution (CC-BY 3.0), from Poly Pizza:

| Block | Model | By |
| --- | --- | --- |
| Piano | [Piano](https://poly.pizza/m/7U-93vxPOER) | jeremy |
| Guitar | [Electric guitar](https://poly.pizza/m/0hg94uOO-sS) | jeremy |
| Bass | [Acoustic guitar](https://poly.pizza/m/afr6GCpce_I) | jeremy |
| Strings | [Violin](https://poly.pizza/m/fhj0GK-0kJu) | jeremy |
| Brass | [Trumpet](https://poly.pizza/m/0Mj5XgeGtKJ) | jeremy |
| Woodwind | [Saxophone](https://poly.pizza/m/6A2UAKdCNy7) | jeremy |
| Drums | [Drum](https://poly.pizza/m/5Wp2emwd7xw) | jeremy |
| Mallets | [Xylophone](https://poly.pizza/m/a-OYg3WVXfV) | Daniel Melchior |
| Cymbals | [Cymbal](https://poly.pizza/m/f8SdBE98BXE) | Poly by Google |

Sampled instruments are cut from [GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS),
which is permissively licensed; only the cut samples are redistributed.

## Licence

MIT. Besiege is Spiderling Studios'; nothing of theirs is redistributed here.
Sampled instruments are cut from an open SoundFont — see
[docs/SAMPLES.md](docs/SAMPLES.md) for which, and under what terms.
