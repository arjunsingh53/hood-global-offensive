# Optional equirectangular sky panorama

`hgo-sunset-panorama.png` is an optional, user-supplied art input. This
repository intentionally does not include or download a third-party skybox.

To use an authorised source, export or place a licensed **2:1 equirectangular
PNG** at:

```text
assets/sky/hgo-sunset-panorama.png
```

Use a panorama at least `512 x 256` pixels; `2048 x 1024` is a practical
starting point. The renderer validates the 2:1 ratio before loading it, samples
it from the active camera's direction, and repeats horizontally at the seam.
Missing, corrupt, too-small, or non-2:1 files are ignored and HGO continues to
render its original procedural dusk sky.

Rebuild after adding the file so the project copies it alongside the executable;
a packaged build reads the same relative path next to its own executable.

Keep licence/attribution evidence for any source you add. For the linked Unity
Asset Store package, download it through the account that holds the applicable
licence, then provide an authorised export rather than copying files from an
unverified source.
