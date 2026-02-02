# 1.0.0 - Audio fix and first stable version !
## New
- Customizable hard limit for audio scaling, to avoid big players to get crazy voice ranges.
## Change
- Updated audio default values to improve hearing of very small player to very big ones.
## Fix
- Audio scaling is now properly streamed to all clients and applied to all players, but only every 0.5sec to avoid network events overload. 


# 0.1.1 - VPM fix
## Change
- Editor PropertyDrawer to convert enum in int, as using assembly prevents U# to serialize enum types.
## Fix
- Created assembly for package installation to work properly.


# 0.1 - Scaling basics and debug.
## New
- Ingame rescale using VR controllers or keyboard, with customizable keys for keyboard.
- UI toggle prefab to activate or deactivate rescaling.
- Changing size adapts avatar speed, gravity, jump height, voice range and audio range.
- Chose between linear, non linear and custom curve how scale affects avatar speed and range.
- Option in prefab settings to persist user size and gesture toggle.
- Debug UI prefab to display how local player size is affected by the scaler.
- Camera near plane is automatically adjusted when players shrinks below 0.4m. 
## Note
Audio scaling is not fully working yet.