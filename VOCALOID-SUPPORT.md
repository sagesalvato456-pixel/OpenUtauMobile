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
