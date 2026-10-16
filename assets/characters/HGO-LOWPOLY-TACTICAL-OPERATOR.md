# HGO Low-Poly Capsule Combatant

`hgo-lowpoly-tactical-operator.glb` is an original, procedural HGO character
asset. It contains no downloaded mesh, texture, image, logo, audio, or copied
third-party character/weapon content.

## Runtime profile

| Property | Value |
| --- | --- |
| Standing source bounds | `0.918 × 0.986 × 1.942 m` (width × depth × height) |
| Geometry | 632 triangles, 12 skinned render meshes, 12 flat PBR materials |
| Skeleton | One lightweight 17-joint capsule-compatible rig |
| Clips | `Idle`, `Walk`, `Run`, `Crouch`, `Prone`, `Reference` |
| Carbine | Original compact generic 0.84 m world-model, rigidly skinned to the trigger hand in a two-hand, three-quarter ready pose |
| Textures / images | None — deliberate flat low-poly PBR colours |

The actor faces Blender `-Y`, which the existing HGO GLB path presents as
glTF `+Z`; it therefore follows the same `180°` world-yaw convention documented
for the current character path. Its rounded white capsule mass, deep blue-grey
face panel, cyan eyes, compact steel/cyan carbine and small warm/cool accessory
accents are original HGO art. `CharacterModels` adds a presentation-only, stable
per-actor cone, horn, halo, or top-hat silhouette at runtime. The weapon is a
world character prop only: it is not a replacement for, or dependency of, the
Gun Game first-person view-models.

## Repeatable build

Run from the repository root with the local Blender installation:

```powershell
& 'F:\WORK\cli-gui\.tooling\blender-5.2.2\blender-5.2.2-windows-x64\blender.exe' `
  --factory-startup --disable-autoexec --background `
  --python tools\build_lowpoly_tactical_operator.py -- `
  --output assets\characters\hgo-lowpoly-tactical-operator.glb
```

For a local review-only image, append
`--preview C:\Temp\hgo-lowpoly-tactical-operator-preview.png`. The preview
camera, lights, and plane are created only after the GLB export and are never
included in the game asset.

## Integration boundary

`CharacterModels` selects this as an integrated-carbine world character and
attaches the visual-only accessory variants. Neither path changes simulation,
weapons, collision, bot decisions, damage, or Gun Game; the old oversized
fallback proxy stays disabled for this asset.
