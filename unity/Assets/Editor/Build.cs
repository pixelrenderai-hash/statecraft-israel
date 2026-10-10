#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Statecraft.Game.EditorTools
{
    /// <summary>Headless player builds. Map.unity is the boot scene; ContentSync copies content/ into StreamingAssets
    /// first (IPreprocessBuildWithReport). Exits non-zero on failure.
    ///   Web: Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -buildTarget WebGL -executeMethod Statecraft.Game.EditorTools.Build.Web
    ///        [-compression disabled|gzip|brotli] [-out Builds/Web-disabled]
    ///   Mac: Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -buildTarget OSXUniversal -executeMethod Statecraft.Game.EditorTools.Build.Mac
    ///        [-out Builds/Mac/Statecraft.app] [-ffp-contract-off]</summary>
    public static class Build
    {
        static string? Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

        public static WebGLCompressionFormat Compression(string? name) => (name ?? "disabled") switch
        {
            "disabled" => WebGLCompressionFormat.Disabled,
            "gzip" => WebGLCompressionFormat.Gzip,
            "brotli" => WebGLCompressionFormat.Brotli,
            _ => throw new ArgumentException($"-compression must be disabled, gzip or brotli (got '{name}')"),
        };

        /// <summary>Why a shareable build may not be made, or null: every event with a sensitive tag needs the owner's
        /// sensitivity review first (status sensitivity-reviewed; docs/m3-release.md, M3 final review I4).</summary>
        public static string? ShareableRefusal(Statecraft.Sim.Content c)
        {
            var open = Statecraft.Sim.ReleaseGate.SensitiveUnreviewed(c);
            return open.Count == 0 ? null
                : "a shareable build needs the owner's sensitivity review of: " + string.Join(", ", open) + " (set status: sensitivity-reviewed; see docs/m3-release.md)";
        }

        /// <summary>The gate for a build made with these command-line args: a -shareable build (Web or Mac) is refused
        /// until the review is done; a dev build is never gated (web_check and the golden checks need them).</summary>
        public static string? GateRefusal(string[] args, Statecraft.Sim.Content c) =>
            Array.IndexOf(args, "-shareable") >= 0 ? ShareableRefusal(c) : null;

        /// <summary>Why a -shareable build may not be made while local prototype art exists, or null. Everything under its
        /// Resources folder ships in every player, and those Tripo models are pending-rights local prototypes
        /// (docs/art-prototypes.md). Kept out of GateRefusal, whose tests expect the content gate alone.</summary>
        public static string? PrototypeRefusal(string[] args, bool prototypesPresent) =>
            Array.IndexOf(args, "-shareable") >= 0 && prototypesPresent
                ? $"{PrototypeImport.Folder} holds pending-rights prototype models that would ship in this build (see docs/art-prototypes.md); move it out of the project first"
                : null;

        /// <summary>Logs "BUILD REFUSED ..." and exits 1 when a -shareable build is refused (local prototype art, then the
        /// sensitivity gate); true if it did.</summary>
        static bool RefuseUnlessShareable()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-shareable") < 0) return false;
            if (!((PrototypeRefusal(args, Directory.Exists(PrototypeImport.Folder))
                ?? GateRefusal(args, Statecraft.Sim.ContentLoader.Load(Paths.Content))) is string refusal)) return false;
            Debug.LogError("BUILD REFUSED " + refusal);
            EditorApplication.Exit(1);
            return true;
        }

        public static void Web()
        {
            if (RefuseUnlessShareable()) return;
            var name = Arg("-compression") ?? "disabled";
            ProjectSetup.Apply();
            PlayerSettings.WebGL.compressionFormat = Compression(name);
            PlayerSettings.WebGL.decompressionFallback = false;   // itch.io serves .br/.gz with Content-Encoding
            bool ok;
            try { ok = Run(BuildTarget.WebGL, Arg("-out") ?? "Builds/Web-" + name); }
            finally { ProjectSetup.Apply(); }                      // back to the committed settings (compression Disabled)
            ExitIfFailed(ok);
        }

        public static void Mac()
        {
            if (RefuseUnlessShareable()) return;
            ProjectSetup.Apply();
            // Apple clang fuses a*b+c into fmadd by default (tech notes 4.2); turn it off if the Mac golden check fails.
            PlayerSettings.SetAdditionalIl2CppArgs(Flag("-ffp-contract-off") ? "--compiler-flags=-ffp-contract=off" : "");
            bool ok;
            try { ok = Run(BuildTarget.StandaloneOSX, Arg("-out") ?? "Builds/Mac/Statecraft.app"); }
            finally { PlayerSettings.SetAdditionalIl2CppArgs(""); AssetDatabase.SaveAssets(); }
            ExitIfFailed(ok);
        }

        /// <summary>Exits with code 1 after the caller's settings have been restored.</summary>
        static void ExitIfFailed(bool ok) { if (!ok) EditorApplication.Exit(1); }

        /// <summary>The real size of the build output (summary.totalSize also counts build intermediates).</summary>
        static double OutputMegabytes(string path)
        {
            if (File.Exists(path)) return new FileInfo(path).Length / 1048576.0;
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1048576.0 : 0;
        }

        static bool Run(BuildTarget target, string location)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.BootScene }, locationPathName = location, target = target, options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"BUILD {s.result} {target} -> {location}: {OutputMegabytes(location):0.0} MB on disk, {s.totalErrors} errors, {s.totalTime.TotalSeconds:0}s");
            return s.result == BuildResult.Succeeded;
        }
    }

    /// <summary>Every player build, from Build.Web/Mac or the Build Profiles window, while local prototype art exists: warns
    /// before, and after puts <see cref="Marker"/> in a WebGL build folder, which tools/package_release.py refuses to zip
    /// even once the art is gone. A local test build stays allowed (docs/art-prototypes.md), so nothing here refuses one. A Mac bundle
    /// is not marked: a file inside it would change the bundle.</summary>
    public sealed class PrototypeBuildMark : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string Marker = "PENDING-RIGHTS-PROTOTYPES.txt";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Directory.Exists(PrototypeImport.Folder))
                Debug.LogWarning($"BUILD includes {PrototypeImport.Folder} (pending-rights prototype models in Resources): keep this build local, never share it");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL) Mark(report.summary.outputPath, Directory.Exists(PrototypeImport.Folder));
        }

        /// <summary>Marks <paramref name="buildDir"/> when the prototypes were present, else drops a stale marker (a rebuild
        /// into the same folder may keep files it did not write). Public for the tests.</summary>
        public static void Mark(string buildDir, bool prototypesPresent)
        {
            if (!Directory.Exists(buildDir)) return;
            var marker = Path.Combine(buildDir, Marker);
            if (prototypesPresent)
                File.WriteAllText(marker, $"This build includes pending-rights local prototype models ({PrototypeImport.Folder}; see docs/art-prototypes.md). Keep it local: never upload, deploy or share it.\n");
            else if (File.Exists(marker)) File.Delete(marker);
        }
    }
}
