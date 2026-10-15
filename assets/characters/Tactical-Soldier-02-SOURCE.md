# Tactical Soldier 02 — local source record

`tactical-soldier-02.glb` is HGO's runtime-ready character package. It was
generated locally from the project-owner-supplied source below; the original
files are deliberately **not** copied into this repository.

| Item | Record |
| --- | --- |
| Source | `G:\GAME MODELS\Tactical_Soldier_02.Fbx(1).fbx` |
| Source SHA-256 | `CCCACBAA091A2406174C45ABD55C33DB69481CA5C6D72C8472B4F804383EE27D` |
| Maps archive | `G:\GAME MODELS\Maps.zip` |
| Maps SHA-256 | `46A46710B13DB796E707AA2D59225B173AA471B2DD204991B72F1ACDED4A6ADB` |
| Input properties | Character Creator-style `CC_Base` rig; 101 bones; ten render meshes; no playable source locomotion clips |
| Runtime package | one skin, 20 materials, embedded PNG maps, no morph targets, and `Idle`, `Walk`, `Run`, `Crouch`, and `Reference` actions |

## Build boundary

The source folder did not include a licence, attribution notice, receipt, or
redistribution grant. The asset is therefore recorded as **project-owner
supplied for this local build**. Do not distribute it outside the project until
the owner supplies and records the relevant Character Creator/Reallusion and
asset licence evidence.

HGO-local locomotion loops are authored by
[`tools/convert_tactical_soldier.py`](../../tools/convert_tactical_soldier.py)
because the supplied FBX contains a bind pose only. They are not represented as
source-provided animations. Collision, hitboxes, weapon logic and the Gun Game
first-person weapon model remain HGO-owned systems.
