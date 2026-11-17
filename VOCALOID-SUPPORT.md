# VOCALOID support, V1 to V6

This layer identifies, installs and lists VOCALOID voicebanks and imports their projects.
It does not synthesise: there is no DSP bridge here, same as before.

## What loads

| Gen | Voicebank files | Project files | Notes |
|-----|-----------------|---------------|-------|
| V1  | .ddb .ddi (label only, see limits) | plain MIDI | goes through the MIDI importer |
| V2  | .ddb .ddi .vvd | .vsq | lyrics and phonemes via `VsqImporter` |
| V3  | .ddb .ddi .vvd | .vsqx (v3 schema) | existing importer |
| V4  | .ddb .ddi .vvd | .vpr, .vsqx (v4 schema) | existing importers |
| V5  | .ddb .ddi (.vvd when present) | .vpr | existing importer |
| V6  | V3/V4/V5 banks on its VOCALOID tracks | .vpr | VOCALOID:AI banks are not supported |

A voicebank is a folder holding a `.ddb` with a `.ddi` of the same name (the `.vvd` is a small info file,
not audio), or a `.vcld` package.

## How the generation is decided

1. `package-config.json`: `"vocaloidVersion": 3`, `"3"`, `"v3"` or `"VOCALOID3"`.
2. Names: manifest id and name, the `.ddb` name, the bank folder, its parent folder ("RanaV4", "MIKU_V3_Original", "V4X"...).
3. Otherwise `Unknown`: the bank loads the same and shows as "VOCALOID".

`VocaloidSinger.EngineVersion` holds it. `USingerType` has no per-generation values, so every bank still
reports `USingerType.Vocaloid4`.

## package-config.json changes

- new optional `vocaloidVersion`
- `ddb`, `ddi` and `vvd` are optional: when empty they are found by extension, paired by base name
- no yml is read or written by any of this

## Limits

- No synthesis. VOCALOID:AI (V6 native) banks have no documented format here.
- V1 bank layout was not verified against a real bank: V1 is a label, the loader treats it like V2 to V5.
- `VsqImporter` converts notes, lyrics, phonemes, tempo and time signature. Vibrato, pitch and dynamics curves are dropped.
  Any failure falls back to the plain MIDI import.
- The default phonemizer name for loose banks is `VocaloidPackageLoader.DefaultPhonemizerName`; that class is not in this bundle.

## Sound and phonemes (Flame engine)

Why there was no sound: the old renderer read the `.vvd` (a tiny info file, not audio) as PCM and parsed the `.ddb` with an invented layout, so every render was silence. The phonemizers also returned `note.position` (absolute) where OpenUtau expects a position relative to the note, which pushed phonemes out of the note.

Now:

- `VocaloidPhonemizers.cs`: JA (kana, yōon, small kana, romaji), EN (dictionary + rules), ES, ZH (pinyin), KO (hangul), VI, FR, DE, IT, RU, PT, TH and RAW. Consonants start before the beat so the vowel lands on the note.
- `VocaloidPhonemeSet.cs`: symbol catalogue. `VocaloidSinger.TryGetOto` accepts any known symbol, so phonemes are no longer red. An unknown symbol is still red.
- `FlameVoice.cs` + `VocaloidRenderer.cs`: built-in source-filter synthesizer that follows pitch (vibrato, PITD), DYN, GENC, BREC and TENC. It sings with any bank, but the timbre is generic, not the sampled voice.
- `VocaloidBankProbe.cs`: on load writes `probe-<bank>.txt` into the cache (`vocaloid` folder) with the first 128 bytes of `.ddi`, `.ddb` and `.vvd`. Send that file to build a real sample decoder.

Not done: decoding the real `.ddb`/`.ddi` samples (the format is proprietary and no sample bank was available here to verify it).

## Themes

Settings, Appearance, preset swatches: VOCALOID1 to VOCALOID6 (seed colors in `ThemeSeedPresets`).

## Real samples (VocaloidSampleBank)

Format verified on MIKU_V4X_EVEC (V4X, 2.3 GB). Hybrid mode: stationary vowels and nasals come from the bank, consonants and transitions still use Flame.

- `.vvd`: text obfuscated with XOR 0x1A (spaces and CRLF untouched): ID, FORMAT, VOICEIDSTR, VOICENAME and the voice parameters.
- `.ddb`: chain of chunks `[tag(4) size(u32) ...]`, 229232 chunks that end exactly at the end of the file.
  - `FRM2`: frame of 256 samples at 44100 Hz. Header: type (u32 @8), time in seconds (double @0x0c), `0x20` = start of the tables (@0x18), `350` = capacity (@0x1c). Tables of 350 floats: harmonic frequencies @0x20, amplitude in dB @0x598 (padding 10000), phase in radians @0xb10. f0 (float) @0x1088, then `ENV ` sub-chunks. A frame of 32 bytes = no voice.
  - `SND `: `[tag, size, 44100(u32), channels(u16), samples(u32)]` (18 bytes) followed by int16 PCM.
- `.ddi`: `DBSe` header, `PHDC` = 133 phoneme names in 31-byte slots from 0x2c, `PHG2` groups, `TDB `/`TMM `/`ARR ` tree (pitch, dynamics, opening), `ART`/`SND`/`EpR`/`STA` records.
  - Stationary record: `EpR 0xFFFFFFFF N`, N u64 pointers to frames, `[44100 u32][channels u16][samples u32][u64 pointer]`. The pointer addresses sample 1024 of the SND, so the SND starts at `pointer - 2066`. The PCM has 4 frames of margin before and 4 after the N frames. Then 16 bytes, `u32 = 1`, `c` (1 byte), `nameLen` (u32), `name`.
  - 144 records = 36 phonemes (12 sonorants x variants plain, #2, #6) x 4 pitches (about C4, E4, G4 and C5). Only the 4th record of each group carries the name.
- Not decoded yet: the ~5700 articulation samples (`ART` records) that hold consonants and transitions, the `ENV` and `TMM` tables.
