using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

namespace UnitySkills
{
    /// <summary>
    /// Interaction simulation skills — Playwright-style testing for Unity.
    /// Simulate UGUI events and query runtime state in Play Mode.
    /// </summary>
    public static class InteractSkills
    {
        #region Play Mode Control

        [UnitySkill("interact_enter_playmode", "Enter Play Mode for interaction testing")]
        public static object EnterPlaymode()
        {
            if (EditorApplication.isPlaying)
                return new { warning = "Already in Play Mode" };

            EditorApplication.isPlaying = true;
            return new { success = true, message = "Entering Play Mode" };
        }

        [UnitySkill("interact_exit_playmode", "Exit Play Mode")]
        public static object ExitPlaymode()
        {
            if (!EditorApplication.isPlaying)
                return new { warning = "Not in Play Mode" };

            EditorApplication.isPlaying = false;
            return new { success = true, message = "Exiting Play Mode" };
        }

        [UnitySkill("interact_wait_frames", "Wait N frames then return. Uses async job pattern.")]
        public static object WaitFrames(int frames = 1)
        {
            if (frames <= 0) frames = 1;
            var jobId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.Now;
            int counted = 0;

            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                counted++;
                if (counted >= frames)
                {
                    EditorApplication.update -= callback;
                    _waitResults[jobId] = new WaitResult
                    {
                        JobId = jobId,
                        Status = "completed",
                        FramesWaited = counted,
                        ElapsedMs = (DateTime.Now - startTime).TotalMilliseconds
                    };
                }
            };
            EditorApplication.update += callback;

            return new { success = true, jobId, frames, message = "Use interact_get_wait_result to poll" };
        }

        [UnitySkill("interact_get_wait_result", "Get result of a wait_frames job")]
        public static object GetWaitResult(string jobId)
        {
            if (!_waitResults.TryGetValue(jobId, out var result))
                return new { jobId, status = "waiting" };
            return new { jobId, result.Status, result.FramesWaited, elapsedMs = result.ElapsedMs };
        }

        [UnitySkill("interact_snapshot_scene", "Get snapshot of current scene state (all root GameObjects with key properties)")]
        public static object SnapshotScene()
        {
            var roots = new List<object>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                {
                    roots.Add(SnapshotGameObject(go, 0, 2));
                }
            }
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount,
                rootObjects = roots
            };
        }

        #endregion

        #region Helpers

        private static object SnapshotGameObject(GameObject go, int depth, int maxDepth)
        {
            var info = new Dictionary<string, object>
            {
                { "name", go.name },
                { "active", go.activeSelf },
                { "instanceId", go.GetInstanceID() },
                { "position", FormatVector3(go.transform.position) },
                { "childCount", go.transform.childCount }
            };

            if (depth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<object>();
                foreach (Transform child in go.transform)
                    children.Add(SnapshotGameObject(child.gameObject, depth + 1, maxDepth));
                info["children"] = children;
            }

            return info;
        }

        private static string FormatVector3(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

        private static string FormatVector2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is Vector2 v2) return FormatVector2(v2);
            if (val is Vector3 v3) return FormatVector3(v3);
            if (val is Color c) return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            if (val is UnityEngine.Object obj) return obj.name;
            return val.ToString();
        }

        /// <summary>
        /// Find a GameObject and return it, or return an error object.
        /// All interact_* skills use this for consistent object resolution.
        /// </summary>
        private static (GameObject go, object error) FindTarget(string name = null, int instanceId = 0)
        {
            if (!EditorApplication.isPlaying)
                return (null, new { error = "Not in Play Mode. Call interact_enter_playmode first." });

            return GameObjectFinder.FindOrError(name, instanceId);
        }

        private static object GetMemberValue(Component comp, Type type, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                try
                {
                    var val = prop.GetValue(comp);
                    return new { success = true, component = type.Name, property = memberName, value = FormatValue(val), valueType = prop.PropertyType.Name };
                }
                catch (Exception ex)
                {
                    return new { error = $"Error reading {memberName}: {ex.Message}" };
                }
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                try
                {
                    var val = field.GetValue(comp);
                    return new { success = true, component = type.Name, property = memberName, value = FormatValue(val), valueType = field.FieldType.Name };
                }
                catch (Exception ex)
                {
                    return new { error = $"Error reading {memberName}: {ex.Message}" };
                }
            }

            return new { error = $"Property/field '{memberName}' not found on {type.Name}" };
        }

        #endregion

        private class WaitResult
        {
            public string JobId;
            public string Status;
            public int FramesWaited;
            public double ElapsedMs;
        }

        private static readonly Dictionary<string, WaitResult> _waitResults = new Dictionary<string, WaitResult>();
    }
}
