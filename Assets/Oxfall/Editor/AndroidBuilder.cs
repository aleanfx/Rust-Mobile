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
    /// Builds the Android APK headlessly. Run by the android-build workflow with
    /// <c>-buildTarget Android -executeMethod Oxfall.Editor.AndroidBuilder.BuildAndExit -oxfallOutput &lt;path&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The version code comes from the CI run number so every build installs over the last one.
    /// When OXFALL_KEYSTORE points at a keystore, the APK is signed with it, so test builds keep the
    /// same signature from run to run. Both are put back afterwards, leaving the committed
    /// settings exactly as they were.
    /// </remarks>
    public static class AndroidBuilder
    {
        public const string DefaultOutput = "Builds/Android/Oxfall.apk";
        public const string OutputArgument = "-oxfallOutput";

        public static void BuildAndExit()
        {
            try
            {
                Build(OutputPath(Environment.GetCommandLineArgs()));
            }
            catch (Exception error)
            {
                Debug.LogError($"[Oxfall] Android build failed: {error}");
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        public static void Build(string output)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException(
                    $"The editor is targeting {EditorUserBuildSettings.activeBuildTarget}. Open it with -buildTarget Android.");

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("There is no scene in the build. Run ProjectSetup.ApplyAndExit first.");

            EditorUserBuildSettings.buildAppBundle = false;
            var folder = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            int previousCode = PlayerSettings.Android.bundleVersionCode;
            string previousVersion = PlayerSettings.bundleVersion;
            bool previousCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string previousKeystore = PlayerSettings.Android.keystoreName;
            string previousAlias = PlayerSettings.Android.keyaliasName;

            try
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out int run) && run > 0)
                {
                    PlayerSettings.Android.bundleVersionCode = run;
                    PlayerSettings.bundleVersion = $"0.1.{run}";
                }

                string keystore = Environment.GetEnvironmentVariable("OXFALL_KEYSTORE");
                string password = Environment.GetEnvironmentVariable("OXFALL_KEYSTORE_PASS");
                string alias = Environment.GetEnvironmentVariable("OXFALL_KEY_ALIAS");
                if (!string.IsNullOrEmpty(keystore) && File.Exists(keystore) && !string.IsNullOrEmpty(password))
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = keystore;
                    PlayerSettings.Android.keystorePass = password;
                    PlayerSettings.Android.keyaliasName = string.IsNullOrEmpty(alias) ? "oxfall-dev" : alias;
                    PlayerSettings.Android.keyaliasPass = password;
                    Debug.Log("[Oxfall] Signing with the development keystore.");
                }
                else
                {
                    Debug.Log("[Oxfall] No keystore given; Unity's debug key signs this APK.");
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException(
                        $"The Android build {summary.result} with {summary.totalErrors} error(s). The log above says which.");

                Debug.Log($"[Oxfall] APK built: {output}, {summary.totalSize / (1024.0 * 1024.0):0.0} MB, " +
                          $"in {summary.totalTime.TotalMinutes:0.0} min, {summary.totalWarnings} warning(s).");
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = previousCode;
                PlayerSettings.bundleVersion = previousVersion;
                PlayerSettings.Android.useCustomKeystore = previousCustomKeystore;
                PlayerSettings.Android.keystoreName = previousKeystore;
                PlayerSettings.Android.keyaliasName = previousAlias;
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
            }
        }

        public static string OutputPath(string[] arguments)
        {
            int index = Array.IndexOf(arguments, OutputArgument);
            if (index < 0) return DefaultOutput;
            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("-"))
                throw new ArgumentException($"{OutputArgument} needs a path after it.");
            return arguments[index + 1];
        }
    }
}
