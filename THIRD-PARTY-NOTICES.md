# Third-party notices

Cozy Marbles VR is an unofficial fan modification. It is not affiliated with or endorsed by the developers or
publishers of Cozy Marbles, Valve or Unity. It contains no game files; the game is changed in memory at start-up.

The mod's own code (`src/`, `build/`) is licensed under the [MIT license](LICENSE). The release packages and this
repository also contain the following third-party components, which keep their own licenses:

| Component | Where | License |
|---|---|---|
| [BepInEx 5](https://github.com/BepInEx/BepInEx) (incl. Unity Doorstop) | `CozyMarblesVR-with-BepInEx.zip`: `winhttp.dll`, `doorstop_config.ini`, `BepInEx/core` | LGPL-2.1 |
| [HarmonyX](https://github.com/BepInEx/HarmonyX), [MonoMod](https://github.com/MonoMod/MonoMod), [Mono.Cecil](https://github.com/jbevain/cecil) | `BepInEx/core` (shipped with BepInEx) | MIT |
| Unity XR Management, XR Core Utils, OpenXR Plugin (compiled from the Unity packages `com.unity.xr.management`, `com.unity.xr.core-utils`, `com.unity.xr.openxr`) | `lib/`, `BepInEx/plugins/CozyMarblesVR/Unity.XR.*.dll`, `UnityOpenXR.dll`, `UnitySubsystemsManifest.json` | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) |
| Khronos OpenXR loader | `openxr_loader.dll` | Apache-2.0 |
| Unity OpenXR mock runtime (development only, not in releases) | `lib/mock` | [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) |

The Steam Frame controller models are **not** distributed: the mod loads them at runtime from the player's own
SteamVR installation.
