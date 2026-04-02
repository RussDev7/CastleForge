Restore360Water
================

What this version does
- Restores the dormant 360-style surface water plane.
- Uses !Mods\Restore360Water\Textures\Terrain\water_normalmap.png instead of the game's vanilla Content folder.
- Resolves water by biome-aware MinY/MaxY bands using the classic CMZ ring-biome distance math.
- Keeps reflection disabled by default for stability.
- Removes the surrogate Murky Water item/block path from this build.

Important behavior notes
- This is still plane-based classic water, not flowing voxel water.
- Biome-local water here is driven by the player/camera biome band, so it is most faithful in the classic radial biome layout.
- Water below MinY is treated as inactive so caves / hell no longer stay submerged forever.
- Runtime biome water updates are edge-triggered, so the mod no longer reattaches/detaches scene state every HUD tick while you stand in the same biome band.

Useful config example
[Biome.Lagoon]
Enabled = true
MinY    = -3.5
MaxY    = 4.5

Commands
- /r360water reload
- /r360water status
- /r360water on
- /r360water off
