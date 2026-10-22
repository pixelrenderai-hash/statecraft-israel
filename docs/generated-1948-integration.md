# Generated 1948 asset integration

This change adds independently generated presentation assets while preserving existing source assets and historical deployment records. Syrian, ALA and Iraqi figures map only to their existing faction formations. The Egyptian character remains review-only because its existing force has simultaneous overlapping Isdud/Majdal base anchors; no presentation decision is invented to resolve that. Jordanian Legion remains unchanged. Two separate houses join Netanya's selection alongside its original B13a; the lighter stone-house revision joins Tiberias alongside its original B17. Safed and other shared-B17 locations are unchanged.

Twelve static assets have individual models, 512px colour maps, materials, prefabs and portable provenance in `unity/Assets/Art/Generated1948Review/`. Nine remain review-only: tents, props and faction vehicles/aircraft have no newly invented placement. The stone house uses the lighter, textured-roof revision. Courtyard house 05 is excluded because front-sided overhead inspection found an incomplete roof. The original tiled-roof pilot 01 is also excluded: its archived diagnosis records genuine roof gaps, and its separate backing-shell candidate was explicitly not production-ready. Whole-town concepts, provisional M22 and the alternate tent are not included.

## Character workflow

Unity import settings do not create skin weights. The existing collection uses Generic rigs, CreateFromThisModel, one looping idle take and one-state controllers. Actual Tripo skinning was obtained for seven characters for 140 existing credits (300 cap; no purchases). Three early outputs used the default 65-bone Mixamo preset; the remaining four use the established 67-bone Unity Humanoid Compatible preset after comparison with the original C08/C09 sources. Original provider outputs are retained in staging. Inclusion requires actual Unity skin/Avatar/playback validation, regardless of preset label.

The existing C09 idle and walk actions were retargeted locally using rest-relative bone rotations while retaining target bone lengths and skin weights. Near meshes preserve their source triangles (6,948–7,616); separate far derivatives follow the existing 1,500-triangle maximum. Runtime controllers retain idle only. Separate walk FBXs support deformation review without introducing new movement rules. Native FBX units are normalized; world bounds, weights and topology were checked on reimport. Original source GLBs and 4K texture masters remain outside git; the delivered 512px maps follow the collection's runtime convention.

The Egyptian infantry, Lebanese regular, Bedouin civilian and recreated local irregular are review-only. Lebanon's existing row has no anchors; no other faction's positions are reused. The civilian does not replace village residents globally. The original local irregular remains intact.

## Reproduction and evidence

`Statecraft.Game.EditorTools.Generated1948Review.Import` rebuilds the twelve static candidates only. `Generated1948Review.Capture` produces front/rear/elevated Unity asset previews under `captures/generated-1948-review/`. These previews are not gameplay or performance evidence. Materials cull backfaces so missing roof surfaces remain visible. Character import, animation evidence and validation are recorded separately.

Each new asset's `provenance.json` records source/output hashes and processing lineage. `ASSETS.yaml` and the generated AI disclosure cover the delivered files. This changes presentation mappings, not simulation rules, sourced force records, licences of existing assets, or release gates.

## Validation and remaining gates

Seven actual Unity Avatars, skin weights, idle/walk deformation and far budgets passed all 14 rig tests. The final targeted integration run passed 165 EditMode tests with no failures. Matched before/after map renders are in `docs/images/generated-1948-*-comparison.jpg`; the camera/state record is `generated-1948-capture-evidence.json`. Character sheets are isolated actual Unity asset previews, not gameplay. Existing terrain/plinth and original-model artifacts visible in the map remain outside this change.

The collection bundle build remains blocked by compiler errors in the installed Unity Collections package: NativeList.cs lines 850/864 calls a NativeArray.ReadOnly constructor without the required safety argument (CS7036). No package file was patched and a successful player/WebGL build is not claimed. That one bundle-building test is excluded from the passing targeted run and its separate failure is retained in the review evidence.

The tools run passed 1500 tests with 5 skips and 2 subtests; two rasterio/GDAL modules could not run because native GDAL is unavailable. The policy suite passed 320 tests. The .NET 10 simulation run passed 2423 tests with 0 warnings. No simulation goldens or content were changed.

## Bundle failure baseline comparison

The same collection-bundle test was run on unchanged base commit `4d92aff9eb162ad24ebc25db7baf05041a9e16c1` in a separate detached checkout, using the same Unity 6000.3.25f1 editor after the standard simulation-DLL preparation. It failed with the identical CS7036 errors at NativeList.cs lines 850 and 864. Package manifests/lock, editor version, CollectionImport build code and actual NativeList package source are byte-identical. [Recorded comparison and hashes](generated-1948-build-baseline.json) establish that this asset branch did not introduce the failure.

The failing non-checks branch calls a two-argument NativeArray.ReadOnly constructor, while the compiler reports a three-argument constructor requiring a safety handle. This establishes the compile-time API mismatch, not whether it comes from an editor regression or this installation's player-reference assemblies. Unity's [6000.3.21f1 release notes](https://unity.com/releases/editor/whats-new/6000.3.21f1) include Collections 2.6.8; its presence alone does not justify a blind package upgrade. No verified small asset-branch fix was found.

Smallest next step: run only `CollectionImportTests.BuildBundlesWritesTheChecksBesideTheShippedBundles` on that unchanged base in a clean installation of the pinned editor, after `tools/build_sim_for_unity.py`, to distinguish a machine/install issue from a reproducible editor/package defect. Keep the branch draft until that toolchain gate is fixed and the same test passes. Do not regenerate the art, disable the test, patch cached package files or add global safety defines as a guessed workaround.
