# Soldier character asset

`soldier.glb` is a runtime-safe conversion of the `Soldier.glb` sample from the
[Three.js examples](https://github.com/mrdoob/three.js/tree/master/examples/models/gltf).
The upstream example credits the character model to Mixamo.

The source model is not claimed to be MIT merely because Three.js is MIT. Keep
the applicable Mixamo terms and this attribution with any redistribution of the
asset. The conversion in [`tools/build_character_asset.py`](../../tools/build_character_asset.py)
retains the supplied body, visor, and animation clips while adapting its embedded
textures, skin layout, and UV attributes for this project's Raylib runtime.

The verified source recipe is pinned to SHA-256
`dfb230fc1f942f259dd00281a1186953ad602fc5d69067ce63e24b2aa439736b`.

To regenerate the committed game asset from the supplied source model:

```powershell
python tools/build_character_asset.py `
  --input F:\WORK\cli-gui\threejs-animation-multiple-preview\assets\Soldier.glb `
  --output assets\characters\soldier.glb
```
