# Low Poly Industrial Zone integration

`assets/maps/low-poly-industrial-zone.glb` is the sole rendered level geometry.
It is a byte-identical runtime copy of the user-supplied GLB; no replacement
crates, generated buildings, floor grid, or random prop system is drawn over it.

HGO's existing movement, hit-scan and bot code use axis-aligned collision brushes
rather than triangle-mesh collision. `IndustrialZoneLayout` therefore provides a
deterministic sidecar: one island floor, four containment bounds, and 50 merged
footprints measured from the supplied GLB's upward-facing structures. The sidecar
is invisible and has no rendering code. It preserves the source layout while
keeping the existing player controller, bullet traces, spawn selection and
navigation graph reliable.

This is not per-triangle collision. Small decorative mesh details, fence gaps,
thin foliage and roof bevels use the nearest measured hard-cover footprint. A
future physics-mesh pipeline could refine those edge cases, but it must keep the
GLB as the visual source and retain the CC-BY attribution in
`assets/maps/LOW-POLY-INDUSTRIAL-ZONE-ATTRIBUTION.md`.
