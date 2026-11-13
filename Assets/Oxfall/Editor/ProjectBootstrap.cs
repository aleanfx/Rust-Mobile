using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Oxfall.Editor
{
    /// <summary>
    /// Adds the Unity packages the game needs, by name and without a version: the resolver picks
    /// what suits this editor and Packages/packages-lock.json records it.
    /// </summary>
    /// <remarks>
    /// CI runs this in a throwaway project, before the repository's own code is ever compiled, so a
    /// package that is missing cannot stop the step that adds it. It must therefore use nothing but
    /// UnityEditor.PackageManager. Run with <c>-executeMethod Oxfall.Editor.ProjectBootstrap.AddPackages</c>.
    /// </remarks>
    public static class ProjectBootstrap
    {
        private static readonly (string Name, string Why)[] Required =
        {
            ("com.unity.render-pipelines.universal", "URP, the render pipeline"),
            ("com.unity.ugui", "the HUD and touch controls"),
        };

        public static void AddPackages()
        {
            var failures = new List<string>();
            foreach (var (name, why) in Required)
            {
                Debug.Log($"[Oxfall] adding {name} ({why})");
                var request = Client.Add(name);
                if (!Wait(request, name, failures)) continue;
                Debug.Log($"[Oxfall] {name} resolved to {request.Result.version}");
            }

            if (failures.Count > 0)
            {
                Debug.LogError("[Oxfall] these packages could not be added:\n  " + string.Join("\n  ", failures));
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        private static bool Wait(Request request, string what, List<string> failures)
        {
            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (!request.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    failures.Add($"{what}: timed out after five minutes");
                    return false;
                }
                System.Threading.Thread.Sleep(100);
            }
            if (request.Status == StatusCode.Success) return true;
            failures.Add($"{what}: {request.Error?.message ?? "unknown error"}");
            return false;
        }
    }
}
