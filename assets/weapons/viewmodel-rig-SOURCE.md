# Animated viewmodel rig — local source record

The four `viewmodel-*.glb` rig weapons are generated locally from the
project-owner-supplied drops below. The original files are staged under
`.assets-scratch/` (gitignored) and are deliberately **not** copied into this
repository; only the built GLBs ship.

| Item | Record |
| --- | --- |
| Animation rig | `.assets-scratch/anim/source/AK74U.fbx` |
| Rig SHA-256 | `3F3F5E10AA6F256840C00B23CFDA6DAC68B1EA5D6B1F8768AB75BA312654D59F` |
| Rifle base | `.assets-scratch/ak/AKM.fbx` |
| Rifle SHA-256 | `152F608C545EBC691A711C72FDB3EA7637304C4B83D472C4217FD9F4571639B4` |
| Sniper base | `.assets-scratch/awm/source/AWM.fbx` |
| Sniper SHA-256 | `5E1A7D18384AFEB609032B0755C86F19C7F9C495661A02988D840229AA36DB69` |
| SMG base | `.assets-scratch/mp9/source/Low Poly MP9 Modified.fbx` |
| SMG SHA-256 | `76F6FCFD188ED2EFBBCD0CCAE9E85803ECDD3756A800DA311E688AB8FB40FEDB` |
| Arm texture | `.assets-scratch/hands/textures/arm2 - Copy.png` |
| Arm texture SHA-256 | `B8668BAFA461CC2FF25D5CA34C84C0CC686E17455D49AE61F8264BB5D0600443` |
| Rig textures | `.assets-scratch/anim/textures/*.png` (Ch08 diffuse/gloss/normal, Krinkov AO/color/metal/nrm/rough, Magazine color/metal/nrm/rough) |
| Runtime package | shared 49-bone rig; per-weapon gun mesh rigid-skinned to `Bone`/`carg`/`gatillo`/`recam`/`seg`; six actions at 30 fps |

`baconhand_smg.fbx` (the separate armed-hands drop) was reviewed and left
unused: the AK74U rig already provides the hoodie arms and hands the new
viewmodels ship with.

## Build boundary

The source drops did not include a licence, attribution notice, receipt, or
redistribution grant — one (AWM) carries markings consistent with a game rip.
The assets are therefore recorded as **project-owner supplied for this local
build**. Do not distribute them outside the project until the owner supplies
and records licence evidence for each drop.

The retargeting, placement math, state machine and fallback artwork around
these files are HGO-owned systems.
