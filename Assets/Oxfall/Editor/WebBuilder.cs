using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oxfall.Editor
{
    /// <summary>
    /// Builds the web (WebGL) version headlessly. Run by the web-build workflow with
    /// <c>-buildTarget WebGL -executeMethod Oxfall.Editor.WebBuilder.BuildAndExit -oxfallOutput &lt;folder&gt;</c>.
    /// </summary>
    public static class WebBuilder
    {
        public const string DefaultOutput = "Builds/WebGL";

        public static void BuildAndExit()
        {
            try
            {
                Build(AndroidBuilder.OutputPath(Environment.GetCommandLineArgs(), DefaultOutput));
            }
            catch (Exception error)
            {
                Debug.LogError($"[Oxfall] Web build failed: {error}");
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        public static void Build(string output)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new BuildFailedException(
                    $"The editor is targeting {EditorUserBuildSettings.activeBuildTarget}. Open it with -buildTarget WebGL.");

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("There is no scene in the build. Run ProjectSetup.ApplyAndExit first.");

            Directory.CreateDirectory(output);
            string previousVersion = PlayerSettings.bundleVersion;
            try
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out int run) && run > 0)
                    PlayerSettings.bundleVersion = $"0.1.{run}";

                PreferSmallDownload();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                });
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException(
                        $"The web build {summary.result} with {summary.totalErrors} error(s). The log above says which.");

                Debug.Log($"[Oxfall] Web build written to {output} in {summary.totalTime.TotalMinutes:0.0} min, " +
                          $"{summary.totalWarnings} warning(s).");
            }
            finally
            {
                PlayerSettings.bundleVersion = previousVersion;
            }
        }

        /// <summary>
        /// Phones on mobile data struggle with a big download, so the web build favours size: the
        /// "Disk Size with LTO" code optimisation. It lives in the WebGL editor module, which only the
        /// web image has, so it is set by reflection and skipped with a warning if it is not there.
        /// </summary>
        private static void PreferSmallDownload()
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("UnityEditor.WebGL.UserBuildSettings", false))
                    .FirstOrDefault(t => t != null);
                var property = type?.GetProperty("codeOptimization",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (property == null || !property.CanWrite)
                {
                    Debug.LogWarning("[Oxfall] WebGL code optimisation setting not found; building with the default.");
                    return;
                }
                var names = Enum.GetNames(property.PropertyType);
                string wanted = names.Contains("DiskSizeLTO") ? "DiskSizeLTO" : names.Contains("DiskSize") ? "DiskSize" : null;
                if (wanted == null)
                {
                    Debug.LogWarning($"[Oxfall] No disk-size code optimisation among: {string.Join(", ", names)}");
                    return;
                }
                property.SetValue(null, Enum.Parse(property.PropertyType, wanted));
                Debug.Log($"[Oxfall] WebGL code optimisation: {wanted}");
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Oxfall] Could not set the WebGL code optimisation: {error.Message}");
            }
        }
    }
}
