# Optional licensed audio overrides

The game always starts with original procedural fallback sounds. It does not
download, extract, or redistribute audio from reference videos.

To replace those fallbacks with files you have the right to use, put WAV, OGG,
or MP3 files in this folder using these names:

| Event | Preferred filename |
| --- | --- |
| Rifle / shared firearm | `gun-rifle.wav` or `gun.wav` |
| Pistol | `gun-pistol.wav` |
| Heavy pistol | `gun-deagle.wav` |
| SMG | `gun-smg.wav` |
| Shotgun | `gun-shotgun.wav` |
| Sniper rifle | `gun-sniper.wav` |
| Reload | `reload.wav` |
| Footstep | `footstep.wav` |
| Player hitmarker | `hitmarker.wav` |
| Player headshot confirmation | `headshot.wav` |

The first matching file is loaded at launch. Keep clips short, dry, and free of
unwanted music or ambience; the runtime already handles event timing, voices,
and distance attenuation. Preserve the licence/provenance alongside any file
that is added here.

The supplied YouTube links are useful sound references, but they are not a
licence grant. In particular, the headshot reference is a CS:GO compilation,
so it must not be copied into the build without an appropriate rightsholder
licence or an authorised source file.
