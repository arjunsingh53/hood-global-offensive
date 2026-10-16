# Animated first-person viewmodel rig pack

`viewmodel-rifle.glb`, `viewmodel-sniper.glb`, `viewmodel-smg.glb` and
`viewmodel-pistol.glb` are HGO's primary first-person weapons. They were built
locally by [`tools/build_animated_viewmodels.py`](../../tools/build_animated_viewmodels.py)
from the project-owner-supplied FBX drops recorded in
[`viewmodel-rig-SOURCE.md`](viewmodel-rig-SOURCE.md); the original files are
deliberately **not** copied into this repository.

All four GLBs carry the same skeleton: the AK74U animation rig (hoodie
first-person arms with hands, plus per-weapon gun bones) and six packed
actions — `DRAW`, `IDLE`, `INSPEC`, `RELOAD1`, `RELOAD2`, `SHOOT` — exported
at 30 fps; raylib samples them at 60. Each weapon's gun mesh is retargeted onto
that rig by rigid skinning, so the runtime only picks an action and a frame and
lets the engine pose the skin.

## Runtime contract

`src/Render/AnimatedViewmodels.cs` owns the state machine (reload clips chosen
by empty/full start, draw from deploy time, shoot from the muzzle-flash edge,
IDLE otherwise) and the placement math:

* Rig space is the GLB's own glTF space: muzzle `+Z`, up `+Y`. The view-model
  space is `-Z` forward, so the rig is drawn with a 180° yaw.
* Placement is anchored on the gun's **trigger point**. The measured trigger
  (glTF space) is mapped onto a view-local target — `TriggerTarget` for hip,
  `AdsTarget` for full ADS — blended by `ZoomBlend`. These spec values are the
  only tuning knobs for sight pictures.
* The scale applies to the **whole rig including the arms**, so every weapon
  must keep `ViewLength ≈ GunLengthRigUnits × 56.7` or the hands inflate. The
  pistol is the one deliberate readability cheat (11.5 ≈ 1.21×).

If a rig GLB is missing or fails to load, `Renderer` falls back to the static
`ak47-viewmodel.glb` / `glock17.glb` artwork and then to box shapes, so the
game still runs without the pack.

## Rebuilding

From the checkout, stage the sources under `.assets-scratch/` (gitignored) as
listed in the source record, then run the bundled Blender:

```powershell
& 'F:\WORK\cli-gui\.tooling\blender-5.2.2\blender-5.2.2-windows-x64\blender.exe' `
  --factory-startup --disable-autoexec --background --python `
  tools\build_animated_viewmodels.py -- --scratch .assets-scratch --out assets\weapons
```

`--only rifle,smg,sniper,pistol` builds a subset; `--preview <dir>` writes
mount-review images. Two import quirks are handled in the script and will
reappear if it is edited: FBX import overrides the scene fps (it is re-asserted
to 30), and the rig bind pose parks the gun bones at the floor (meshes are
placed from the IDLE keyframe 1 pose).

## Verification

Captures go through the real viewmodel camera, so they are the acceptance
check for placement and pose:

```powershell
dotnet run -- --screenshot hip.png  --weapon Rifle --seconds 4 --bots 9 --width 1920 --height 1080
dotnet run -- --screenshot ads.png  --weapon Rifle --seconds 4 --bots 9 --width 1920 --height 1080 --zoom --reticle
dotnet run -- --screenshot clip.png --weapon Rifle --anim RELOAD1:1.0
```

`--reticle` marks the exact centre-ray pixel for sight calibration; `--anim
<action[:seconds]>` pins one action clip at a fixed time so `DRAW` / `RELOAD1`
/ `RELOAD2` / `SHOOT` / `INSPEC` poses can be inspected deterministically.
