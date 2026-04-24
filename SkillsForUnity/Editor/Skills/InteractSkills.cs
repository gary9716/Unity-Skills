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

        #region UGUI Interaction

        [UnitySkill("interact_click", "Simulate clicking a UI element (Button, etc.)")]
        public static object Click(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            // Try Button.onClick first (most common case)
            var button = go.GetComponent<Button>();
            if (button != null && button.onClick != null)
            {
                button.onClick.Invoke();
                return new { success = true, target = go.name, @event = "click", method = "Button.onClick" };
            }

            // Fallback: ExecuteEvents for generic IPointerClickHandler
            var pointerData = new PointerEventData(EventSystem.current)
            {
                pointerId = -1,
                position = GetScreenPosition(go)
            };
            ExecuteEvents.Execute<IPointerClickHandler>(go, pointerData, ExecuteEvents.pointerClickHandler);

            return new { success = true, target = go.name, @event = "click", method = "ExecuteEvents" };
        }

        [UnitySkill("interact_submit_text", "Simulate entering text into an InputField and submitting")]
        public static object SubmitText(string name = null, int instanceId = 0, string text = "")
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            // Try legacy InputField first
            var inputField = go.GetComponent<InputField>();
            if (inputField != null)
            {
                inputField.text = text;
                inputField.onEndEdit?.Invoke(text);
                return new { success = true, target = go.name, @event = "submit_text", text, fieldType = "InputField" };
            }

            // Try TMP_InputField
            var tmpInput = go.GetComponent("TMPro.TMP_InputField") as MonoBehaviour;
            if (tmpInput != null)
            {
                var textProp = tmpInput.GetType().GetProperty("text");
                var onEndEditProp = tmpInput.GetType().GetProperty("onEndEdit");
                if (textProp != null) textProp.SetValue(tmpInput, text);

                var onEndEdit = onEndEditProp?.GetValue(tmpInput);
                if (onEndEdit != null)
                {
                    var invokeMethod = onEndEdit.GetType().GetMethod("Invoke", new[] { typeof(string) });
                    invokeMethod?.Invoke(onEndEdit, new object[] { text });
                }
                return new { success = true, target = go.name, @event = "submit_text", text, fieldType = "TMP_InputField" };
            }

            return new { error = $"No InputField or TMP_InputField found on '{go.name}'" };
        }

        [UnitySkill("interact_toggle", "Set Toggle state and trigger onValueChanged")]
        public static object Toggle(string name = null, int instanceId = 0, bool isOn = true)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var toggle = go.GetComponent<Toggle>();
            if (toggle == null)
                return new { error = $"No Toggle found on '{go.name}'" };

            toggle.isOn = isOn;
            return new { success = true, target = go.name, @event = "toggle", isOn };
        }

        [UnitySkill("interact_slider_set", "Set Slider value and trigger onValueChanged")]
        public static object SliderSet(string name = null, int instanceId = 0, float value = 0f)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var slider = go.GetComponent<Slider>();
            if (slider == null)
                return new { error = $"No Slider found on '{go.name}'" };

            slider.value = value;
            return new { success = true, target = go.name, @event = "slider_set", value, minValue = slider.minValue, maxValue = slider.maxValue };
        }

        [UnitySkill("interact_dropdown_set", "Set Dropdown selected index and trigger onValueChanged")]
        public static object DropdownSet(string name = null, int instanceId = 0, int index = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var dropdown = go.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                if (index < 0 || index >= dropdown.options.Count)
                    return new { error = $"Index {index} out of range. Dropdown has {dropdown.options.Count} options." };
                dropdown.value = index;
                return new { success = true, target = go.name, @event = "dropdown_set", index, selectedText = dropdown.options[index].text };
            }

            // Try TMP_Dropdown
            var tmpDropdown = go.GetComponent("TMPro.TMP_Dropdown") as MonoBehaviour;
            if (tmpDropdown != null)
            {
                var optionsProp = tmpDropdown.GetType().GetProperty("options");
                var optionsList = optionsProp?.GetValue(tmpDropdown) as IList;
                if (optionsList != null && (index < 0 || index >= optionsList.Count))
                    return new { error = $"Index {index} out of range. TMP_Dropdown has {optionsList.Count} options." };

                var valueProp = tmpDropdown.GetType().GetProperty("value");
                if (valueProp != null) valueProp.SetValue(tmpDropdown, index);

                return new { success = true, target = go.name, @event = "dropdown_set", index, dropdownType = "TMP_Dropdown" };
            }

            return new { error = $"No Dropdown or TMP_Dropdown found on '{go.name}'" };
        }

        [UnitySkill("interact_pointer_event", "Send a pointer event to a UI element")]
        public static object PointerEvent(string name = null, int instanceId = 0, string eventType = "Click")
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var pointerData = new PointerEventData(EventSystem.current)
            {
                pointerId = -1,
                position = GetScreenPosition(go)
            };

            switch (eventType.ToLower())
            {
                case "enter":
                    ExecuteEvents.Execute<IPointerEnterHandler>(go, pointerData, ExecuteEvents.pointerEnterHandler);
                    break;
                case "exit":
                    ExecuteEvents.Execute<IPointerExitHandler>(go, pointerData, ExecuteEvents.pointerExitHandler);
                    break;
                case "down":
                    ExecuteEvents.Execute<IPointerDownHandler>(go, pointerData, ExecuteEvents.pointerDownHandler);
                    break;
                case "up":
                    ExecuteEvents.Execute<IPointerUpHandler>(go, pointerData, ExecuteEvents.pointerUpHandler);
                    break;
                case "click":
                    ExecuteEvents.Execute<IPointerClickHandler>(go, pointerData, ExecuteEvents.pointerClickHandler);
                    break;
                default:
                    return new { error = $"Unknown pointer event type: {eventType}. Use: Enter, Exit, Down, Up, Click" };
            }

            return new { success = true, target = go.name, @event = "pointer_" + eventType.ToLower() };
        }

        private static Vector2 GetScreenPosition(GameObject go)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null && rectTransform.parent != null)
            {
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                {
                    return rectTransform.position;
                }
            }
            return Vector2.zero;
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
