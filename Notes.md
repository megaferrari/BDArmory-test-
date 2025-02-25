### Branches
Current un-merged branches (`git branch --no-merged`) are:
- AoA — respecting maxAoA and max G-load AI settings
- bias-testing — testing bias due to spawn position or camera focus
- motherships — which AI/WM is in control when parasite fighters get detached and automatically adding them to competitions

Outdated, probably to be deleted:
- spawn-strategy — partially implemented, then abandoned spawn strategy implementation by aubranium
- sph-inertia — Simple flight dynamics analysis draft impl by aubranium, better as a separate mod since it doesn't use anything BDA specific.


### Optimisation
- https://learn.unity.com/tutorial/fixing-performance-problems-2019-3-1#
- Various setters/accessors in Unity perform extra operations that may cause GC allocations or have other overheads:
    - Setting a transform's position/rotation causes OnTransformChanged events for all child transforms.
    - Prefer Transform.localPosition over Transform.position when possible or cache Transform.position as Transform.position calculates world position each time it's accessed.
    - Check if a field is actually a getter and cache the result instead of repeated get calls.
- Strings cause a lot of GC alloc.
    - Use interpolated strings or StringBuilder instead of concatenating strings.
    - UnityEngine.Object.name allocates a new string (Object.get_name).
    - Localizer.Format strings should be cached as they don't change during the game — StringUtils.cs
    - AddVesselSwitcherWindowEntry and WindowVesselSwitcher in LoadedVesselSwitcher.cs and WindowVesselSpawner in VesselSpawnerWindow.cs are doing a lot of string manipulation.
    - KerbalEngineer does a lot of string manipulation.
    - vessel.vesselName and vessel.GetName() are fine. vessel.GetDisplayName() is bad!
- Tuples are classes (allocated on the heap), ValueTuples are structs (allocated on the stack). Use ValueTuples to avoid GC allocations.
- Use non-allocating versions of RaycastAll, OverlapSphere and similar (Raycast uses the stack so it's fine).
- The break-even point for using RaycastCommand instead of multiple Raycasts seems to be around 8 raycasts. Also, until Unity 2022.2, RaycastCommand only returns the first hit per job.
- Cache "Wait..." yield instructions instead of using "new Wait...".
- Starting coroutines causes some GC — avoid starting them in Update or FixedUpdate.
- Avoid Linq expressions in critical areas. However, some Linq queries can be parallelised (PLINQ) with ".AsParallel()" and sequentialised with ".AsSequential()". Also, ".ForEach()" does a merge to sequential, while ".ForAll()" doesn't.
- Avoid excessive object references in structs and classes and prefer identifiers instead — affects GC checks.
- Trigger GC manually at appropriate times (System.GC.Collect()) when it won't affect gameplay, e.g., when resetting competition stuff.
- Intel and AMD have hardware support for sqrt, but M1 Macs don't, so we do need to avoid using sqrt in frequently used functions.

- Bad GC routines:
    - part.explode when triggering new vessels causes massive GC alloc, but it's in base KSP, so there's not much that can be done.
    - ExplosionFX.IsInLineOfSight — Sorting of the raycast hits by distance causes GC alloc, but using Array.Copy and Array.Sort is the best I've managed to find, certainly much better than Linq and Lists.
    - MissileFire.GuardTurretRoutine -> RadarUtils.RenderVesselRadarSnapshot -> GetPixels32 — Not much we can do about this. Also, GetPixels actually leaks memory!
    - PartResourceList: Part.Resources.GetEnumerator causes GC alloc. Using Part.Resources.dict.Values.GetEnumerator seems better?
    - VesselSpawnerWindow.WindowVesselSpawner -> string manipulation
    - LoadedVesselSwitcher.WindowVesselSwitcher -> string manipulation
    - LoadedVesselSwitcher.AddVesselSwitcherWindowEntry -> string manipulation
    - CamTools.SetDoppler -> get_name
    - CameraTools::CTPartAudioController.Awake

### Shader Compilation
- Shaders should be compiled using Unity 2018.4.36f1 to be compatible with KSP 1.9.1.
- To compile a shader bundle:
    1. Install AssetBundle Browser: https://docs.unity3d.com/Manual/AssetBundles-Browser.html
    2. Open a Unity project (an empty one is fine).
    3. Import the shaders (if not already done) via "Assets->Import New Asset...".
    4. Go to File->Build Settings. Pick Windows/Mac/Linux based on what bundle you plan to make.
    5. Go to "Window->AssetBundle Browser".
    6. Drag the 4 shader assets from the "Project" tab in the main Unity window into the AssetBundle Browser window.
    7. Rename the asset bundle to match the build target for loading in BDAShaderLoader.cs (e.g., "bdarmoryshaders_linux").
    8. In the build tab select Standalone Windows/Standalone OSX Universal/Standalone Linux 64 (match your build settings).
    9. Hit build.
    10. Repeat 4, 7, 8 and 9 for the remaining Windows/Mac/Linux bundles.
    11. Copy them from `~/Unity/<project name>/AssetBundles` (or equivalent on the OS you're using) to `Distribution/GameData/BDArmory/AssetBundles`.