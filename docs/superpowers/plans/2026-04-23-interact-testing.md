# Interact Skills Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `interact_*` skill module that lets AI simulate UGUI interactions and query GameObject state in Play Mode, Playwright-style.

**Architecture:** New `InteractSkills.cs` with `[UnitySkill]` methods. Uses existing `GameObjectFinder` for object resolution, `ComponentSkills.FindComponentType` for type lookup, and `ComponentSkills.ConvertValue` for value conversion. Play Mode guard on all interaction/query skills.

**Tech Stack:** Unity Editor C#, reflection, UGUI event system, existing Unity-Skills infrastructure.

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `SkillsForUnity/Editor/Skills/InteractSkills.cs` | All `interact_*` skill implementations |
| Create | `SkillsForUnity/unity-skills~/skills/interact/SKILL.md` | Skill documentation for AI |
| Modify | `SkillsForUnity/unity-skills~/skills/SKILL.md` | Add interact to module index table |

---

### Task 1: Play Mode Control Skills

**Files:**
- Create: `SkillsForUnity/Editor/Skills/InteractSkills.cs`

- [ ] **Step 1: Create InteractSkills.cs with Play Mode control methods**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Linq;
using System.Reflection;
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
```

- [ ] **Step 2: Commit**

```bash
git add SkillsForUnity/Editor/Skills/InteractSkills.cs
git commit -m "feat: add InteractSkills with Play Mode control and snapshot"
```

---

### Task 2: UGUI Interaction Skills

**Files:**
- Modify: `SkillsForUnity/Editor/Skills/InteractSkills.cs` — add UGUI interaction region

- [ ] **Step 1: Add UGUI interaction methods before the Helpers region**

Insert the following region after `#region Play Mode Control` / `#endregion` and before `#region Helpers`:

```csharp
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
                return new { success = true, target = go.name, event = "click", method = "Button.onClick" };
            }

            // Fallback: ExecuteEvents for generic IPointerClickHandler
            var pointerData = new PointerEventData(EventSystem.current)
            {
                pointerId = -1,
                position = GetScreenPosition(go)
            };
            ExecuteEvents.Execute<IPointerClickHandler>(go, pointerData, ExecuteEvents.pointerClickHandler);

            return new { success = true, target = go.name, event = "click", method = "ExecuteEvents" };
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
                return new { success = true, target = go.name, event = "submit_text", text, fieldType = "InputField" };
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
                return new { success = true, target = go.name, event = "submit_text", text, fieldType = "TMP_InputField" };
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
            // onValueChanged is automatically invoked by setting isOn
            return new { success = true, target = go.name, event = "toggle", isOn };
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
            return new { success = true, target = go.name, event = "slider_set", value, minValue = slider.minValue, maxValue = slider.maxValue };
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
                return new { success = true, target = go.name, event = "dropdown_set", index, selectedText = dropdown.options[index].text };
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

                return new { success = true, target = go.name, event = "dropdown_set", index, dropdownType = "TMP_Dropdown" };
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

            return new { success = true, target = go.name, event = "pointer_" + eventType.ToLower() };
        }

        private static Vector2 GetScreenPosition(GameObject go)
        {
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform != null && rectTransform.parent != null)
            {
                var canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rectTransform.parent as RectTransform,
                        RectTransformUtility.WorldToScreenPoint(canvas.worldCamera ?? Camera.main, rectTransform.position),
                        canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null,
                        out var localPoint);
                    return rectTransform.position;
                }
            }
            return Vector2.zero;
        }

        #endregion
```

Also add the `using UnityEngine.EventSystems;` import at the top if not already present, and the `using System.Collections;` import for `IList`.

- [ ] **Step 2: Commit**

```bash
git add SkillsForUnity/Editor/Skills/InteractSkills.cs
git commit -m "feat: add UGUI interaction skills (click, submit_text, toggle, slider, dropdown, pointer)"
```

---

### Task 3: UGUI State Query Skills

**Files:**
- Modify: `SkillsForUnity/Editor/Skills/InteractSkills.cs` — add query region

- [ ] **Step 1: Add UI state query methods**

Insert the following region between the UGUI Interaction region and Helpers region:

```csharp
        #region UI State Queries

        [UnitySkill("interact_get_text", "Get text content from a Text or TMP_Text element")]
        public static object GetText(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            // Try legacy Text
            var text = go.GetComponent<Text>();
            if (text != null)
                return new { success = true, target = go.name, text = text.text, textType = "Text" };

            // Try TMP_Text
            var tmpText = go.GetComponent("TMPro.TMP_Text") as MonoBehaviour;
            if (tmpText != null)
            {
                var textProp = tmpText.GetType().GetProperty("text");
                return new { success = true, target = go.name, text = textProp?.GetValue(tmpText)?.ToString(), textType = "TMP_Text" };
            }

            return new { error = $"No Text or TMP_Text found on '{go.name}'" };
        }

        [UnitySkill("interact_get_active", "Get active state of a GameObject")]
        public static object GetActive(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            return new { success = true, target = go.name, activeSelf = go.activeSelf, activeInHierarchy = go.activeInHierarchy };
        }

        [UnitySkill("interact_get_rect", "Get RectTransform position and size")]
        public static object GetRect(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null)
                return new { error = $"No RectTransform on '{go.name}'" };

            return new
            {
                success = true,
                target = go.name,
                anchoredPosition = FormatVector2(rectTransform.anchoredPosition),
                sizeDelta = FormatVector2(rectTransform.sizeDelta),
                anchorMin = FormatVector2(rectTransform.anchorMin),
                anchorMax = FormatVector2(rectTransform.anchorMax),
                pivot = FormatVector2(rectTransform.pivot),
                offsetMin = FormatVector2(rectTransform.offsetMin),
                offsetMax = FormatVector2(rectTransform.offsetMax)
            };
        }

        [UnitySkill("interact_get_component_prop", "Get a property value from a component")]
        public static object GetComponentProp(string name = null, int instanceId = 0, string component = null, string property = null)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            if (string.IsNullOrEmpty(component) || string.IsNullOrEmpty(property))
                return new { error = "component and property are required" };

            var type = ComponentSkills.FindComponentType(component);
            if (type == null)
                return new { error = $"Component type not found: {component}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"No {component} on '{go.name}'" };

            return GetMemberValue(comp, type, property);
        }

        [UnitySkill("interact_get_color", "Get the color of a Graphic (Image, Text, etc.)")]
        public static object GetColor(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var graphic = go.GetComponent<Graphic>();
            if (graphic == null)
                return new { error = $"No Graphic component on '{go.name}'" };

            var c = graphic.color;
            return new { success = true, target = go.name, r = c.r, g = c.g, b = c.b, a = c.a, hex = ColorUtility.ToHtmlStringRGBA(c) };
        }

        [UnitySkill("interact_get_interactable", "Get interactable state of a Selectable")]
        public static object GetInteractable(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var selectable = go.GetComponent<Selectable>();
            if (selectable == null)
                return new { error = $"No Selectable component on '{go.name}'" };

            return new { success = true, target = go.name, interactable = selectable.interactable, enabled = selectable.enabled };
        }

        [UnitySkill("interact_get_toggle_state", "Get Toggle isOn value")]
        public static object GetToggleState(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var toggle = go.GetComponent<Toggle>();
            if (toggle == null)
                return new { error = $"No Toggle on '{go.name}'" };

            return new { success = true, target = go.name, isOn = toggle.isOn };
        }

        [UnitySkill("interact_get_slider_value", "Get Slider value")]
        public static object GetSliderValue(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var slider = go.GetComponent<Slider>();
            if (slider == null)
                return new { error = $"No Slider on '{go.name}'" };

            return new { success = true, target = go.name, value = slider.value, minValue = slider.minValue, maxValue = slider.maxValue };
        }

        #endregion
```

Add this helper method inside the `#region Helpers` section:

```csharp
        private static string FormatVector2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";

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

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is Vector2 v2) return FormatVector2(v2);
            if (val is Vector3 v3) return FormatVector3(v3);
            if (val is Color c) return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
            if (val is UnityEngine.Object obj) return obj.name;
            return val.ToString();
        }
```

Note: remove the duplicate `FormatValue` method if the helpers region already defines `FormatVector3` and you add `FormatValue`. The helpers should have: `FormatVector3`, `FormatVector2`, `FormatValue`, `FindTarget`, `GetMemberValue`.

- [ ] **Step 2: Commit**

```bash
git add SkillsForUnity/Editor/Skills/InteractSkills.cs
git commit -m "feat: add UI state query skills (get_text, get_rect, get_color, etc.)"
```

---

### Task 4: GameObject Query Skills

**Files:**
- Modify: `SkillsForUnity/Editor/Skills/InteractSkills.cs` — add GameObject query region

- [ ] **Step 1: Add GameObject query methods**

Insert this region after the UI State Queries region:

```csharp
        #region GameObject Queries

        [UnitySkill("interact_get_field", "Read a field value from a MonoBehaviour (supports SerializeField via reflection)")]
        public static object GetField(string name = null, int instanceId = 0, string fieldName = null, string componentType = null)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            if (string.IsNullOrEmpty(fieldName))
                return new { error = "fieldName is required" };

            // If componentType specified, use it; otherwise search all MonoBehaviours
            if (!string.IsNullOrEmpty(componentType))
            {
                var type = ComponentSkills.FindComponentType(componentType);
                if (type == null)
                    return new { error = $"Component type not found: {componentType}" };
                var comp = go.GetComponent(type);
                if (comp == null)
                    return new { error = $"No {componentType} on '{go.name}'" };
                return GetMemberValue(comp, type, fieldName);
            }

            // Search all MonoBehaviours on the GameObject
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var result = GetMemberValue(comp, comp.GetType(), fieldName);
                var successProp = result.GetType().GetProperty("success");
                if (successProp != null && (bool)successProp.GetValue(result))
                {
                    // Enrich with component name
                    return new { success = true, target = go.name, component = comp.GetType().Name, property = fieldName,
                        value = result.GetType().GetProperty("value")?.GetValue(result),
                        valueType = result.GetType().GetProperty("valueType")?.GetValue(result) };
                }
            }

            return new { error = $"Field '{fieldName}' not found on any component of '{go.name}'" };
        }

        [UnitySkill("interact_get_position", "Get Transform position, rotation, and scale")]
        public static object GetPosition(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var t = go.transform;
            return new
            {
                success = true,
                target = go.name,
                position = new { x = t.position.x, y = t.position.y, z = t.position.z },
                localPosition = new { x = t.localPosition.x, y = t.localPosition.y, z = t.localPosition.z },
                rotation = new { x = t.eulerAngles.x, y = t.eulerAngles.y, z = t.eulerAngles.z },
                scale = new { x = t.localScale.x, y = t.localScale.y, z = t.localScale.z }
            };
        }

        [UnitySkill("interact_get_children", "List children of a GameObject")]
        public static object GetChildren(string name = null, int instanceId = 0)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            var children = new List<object>();
            foreach (Transform child in go.transform)
            {
                children.Add(new
                {
                    name = child.name,
                    instanceId = child.gameObject.GetInstanceID(),
                    active = child.gameObject.activeSelf
                });
            }

            return new { success = true, target = go.name, childCount = children.Count, children };
        }

        [UnitySkill("interact_find_by_tag", "Find GameObjects by tag")]
        public static object FindByTag(string tag = null)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Not in Play Mode. Call interact_enter_playmode first." };

            if (string.IsNullOrEmpty(tag))
                return new { error = "tag is required" };

            try
            {
                var objects = GameObject.FindGameObjectsWithTag(tag);
                return new
                {
                    success = true,
                    tag,
                    count = objects.Length,
                    objects = objects.Select(go => new { name = go.name, instanceId = go.GetInstanceID() }).ToArray()
                };
            }
            catch (UnityException)
            {
                return new { error = $"Tag '{tag}' is not defined in TagManager" };
            }
        }

        [UnitySkill("interact_find_by_component", "Find all GameObjects that have a specific component")]
        public static object FindByComponent(string componentType = null)
        {
            if (!EditorApplication.isPlaying)
                return new { error = "Not in Play Mode. Call interact_enter_playmode first." };

            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var type = ComponentSkills.FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var allObjects = FindHelper.FindAll<GameObject>(includeInactive: true);
            var matches = allObjects
                .Where(go => go.GetComponent(type) != null)
                .Take(50)
                .Select(go => new { name = go.name, instanceId = go.GetInstanceID(), active = go.activeInHierarchy })
                .ToArray();

            return new { success = true, componentType, count = matches.Length, objects = matches };
        }

        #endregion
```

- [ ] **Step 2: Commit**

```bash
git add SkillsForUnity/Editor/Skills/InteractSkills.cs
git commit -m "feat: add GameObject query skills (get_field, get_position, get_children, find_by_tag, find_by_component)"
```

---

### Task 5: MonoBehaviour Interaction Skills

**Files:**
- Modify: `SkillsForUnity/Editor/Skills/InteractSkills.cs` — add MonoBehaviour interaction region

- [ ] **Step 1: Add MonoBehaviour interaction methods**

Insert this region between the UGUI Interaction region and UI State Queries region:

```csharp
        #region MonoBehaviour Interaction

        [UnitySkill("interact_invoke_method", "Invoke a public method on a MonoBehaviour")]
        public static object InvokeMethod(string name = null, int instanceId = 0, string methodName = null, string args = null)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            if (string.IsNullOrEmpty(methodName))
                return new { error = "methodName is required" };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod;

            // Search all MonoBehaviours for the method
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;

                var method = comp.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method == null) continue;

                try
                {
                    var parameters = method.GetParameters();
                    object[] invokeArgs;

                    if (parameters.Length == 0)
                    {
                        invokeArgs = Array.Empty<object>();
                    }
                    else if (!string.IsNullOrEmpty(args))
                    {
                        var argArray = Newtonsoft.Json.JsonConvert.DeserializeObject<object[]>(args);
                        invokeArgs = new object[parameters.Length];
                        for (int i = 0; i < parameters.Length && i < argArray.Length; i++)
                            invokeArgs[i] = ComponentSkills.ConvertValue(argArray[i]?.ToString(), parameters[i].ParameterType);
                    }
                    else
                    {
                        // Try to invoke with default values
                        invokeArgs = parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                    }

                    var result = method.Invoke(comp, invokeArgs);
                    return new
                    {
                        success = true,
                        target = go.name,
                        component = comp.GetType().Name,
                        method = methodName,
                        returnValue = result?.ToString() ?? "void"
                    };
                }
                catch (TargetInvocationException tie)
                {
                    return new { error = $"Method {methodName} threw: {tie.InnerException?.Message ?? tie.Message}" };
                }
                catch (Exception ex)
                {
                    return new { error = $"Error invoking {methodName}: {ex.Message}" };
                }
            }

            return new { error = $"Method '{methodName}' not found on any component of '{go.name}'" };
        }

        [UnitySkill("interact_set_field", "Set a field value on a MonoBehaviour (supports SerializeField)")]
        public static object SetField(string name = null, int instanceId = 0, string fieldName = null, string value = null, string componentType = null)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            if (string.IsNullOrEmpty(fieldName))
                return new { error = "fieldName is required" };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (!string.IsNullOrEmpty(componentType))
            {
                var type = ComponentSkills.FindComponentType(componentType);
                if (type == null) return new { error = $"Component type not found: {componentType}" };
                var comp = go.GetComponent(type);
                if (comp == null) return new { error = $"No {componentType} on '{go.name}'" };
                return SetMemberValue(comp, type, fieldName, value);
            }

            // Search all MonoBehaviours
            foreach (var comp in go.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                var field = comp.GetType().GetField(fieldName, flags);
                if (field != null)
                    return SetMemberValue(comp, comp.GetType(), fieldName, value);
            }

            return new { error = $"Field '{fieldName}' not found on any component of '{go.name}'" };
        }

        [UnitySkill("interact_send_message", "Send a message to a GameObject (calls named method on all components)")]
        public static object SendMessage(string name = null, int instanceId = 0, string methodName = null, string value = null)
        {
            var (go, err) = FindTarget(name, instanceId);
            if (err != null) return err;

            if (string.IsNullOrEmpty(methodName))
                return new { error = "methodName is required" };

            var options = string.IsNullOrEmpty(value) ? SendMessageOptions.DontRequireReceiver : SendMessageOptions.RequireReceiver;

            if (!string.IsNullOrEmpty(value))
                go.SendMessage(methodName, value, options);
            else
                go.SendMessage(methodName, options);

            return new { success = true, target = go.name, method = methodName, value };
        }

        private static object SetMemberValue(Component comp, Type type, string fieldName, string value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var prop = type.GetProperty(fieldName, flags);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    var converted = ComponentSkills.ConvertValue(value, prop.PropertyType);
                    prop.SetValue(comp, converted);
                    return new { success = true, target = comp.gameObject.name, component = type.Name, field = fieldName, valueSet = converted?.ToString() ?? "null" };
                }
                catch (Exception ex) { return new { error = $"Error setting {fieldName}: {ex.Message}" }; }
            }

            var field = type.GetField(fieldName, flags);
            if (field != null)
            {
                try
                {
                    var converted = ComponentSkills.ConvertValue(value, field.FieldType);
                    field.SetValue(comp, converted);
                    return new { success = true, target = comp.gameObject.name, component = type.Name, field = fieldName, valueSet = converted?.ToString() ?? "null" };
                }
                catch (Exception ex) { return new { error = $"Error setting {fieldName}: {ex.Message}" }; }
            }

            return new { error = $"Property/field '{fieldName}' not found on {type.Name}" };
        }

        #endregion
```

- [ ] **Step 2: Commit**

```bash
git add SkillsForUnity/Editor/Skills/InteractSkills.cs
git commit -m "feat: add MonoBehaviour interaction skills (invoke_method, set_field, send_message)"
```

---

### Task 6: Skill Documentation (SKILL.md)

**Files:**
- Create: `SkillsForUnity/unity-skills~/skills/interact/SKILL.md`
- Modify: `SkillsForUnity/unity-skills~/skills/SKILL.md`

- [ ] **Step 1: Create interact/SKILL.md**

```markdown
---
name: unity-interact
description: "Playwright-style interaction testing for Unity. Simulate UGUI clicks, input, toggles and query runtime state. Must be in Play Mode. Triggers: interact, click, simulate, test ui, ui test, 交互测试, UI测试, 模拟点击."
---

# Interact Skills — Playwright-style Testing for Unity

Simulate user interactions and query runtime state in Play Mode.

> **Requires Play Mode**: All interact skills (except `interact_enter_playmode` and `interact_exit_playmode`) require the editor to be in Play Mode.

## Quick Start

```python
# Enter Play Mode
unity_skills.call_skill("interact_enter_playmode")
unity_skills.call_skill("interact_wait_frames", frames=5)  # Wait for initialization

# Simulate interaction
unity_skills.call_skill("interact_click", name="AddScore")

# Query state
result = unity_skills.call_skill("interact_get_text", name="ScoreLabel")
# result.text == "1" → AI judges test passed

# Exit
unity_skills.call_skill("interact_exit_playmode")
```

## Play Mode Control

| Skill | Description |
|-------|-------------|
| `interact_enter_playmode` | Enter Play Mode |
| `interact_exit_playmode` | Exit Play Mode |
| `interact_wait_frames` | Wait N frames (async, returns jobId) |
| `interact_get_wait_result` | Poll wait_frames job status |
| `interact_snapshot_scene` | Get scene state snapshot |

## UGUI Interaction

| Skill | Parameters | Description |
|-------|-----------|-------------|
| `interact_click` | name or instanceId | Click Button / IPointerClickHandler |
| `interact_submit_text` | name, text | Enter text into InputField |
| `interact_toggle` | name, isOn | Set Toggle state |
| `interact_slider_set` | name, value | Set Slider value |
| `interact_dropdown_set` | name, index | Set Dropdown selection |
| `interact_pointer_event` | name, eventType | Send pointer event (Enter/Exit/Down/Up/Click) |

## UI State Queries

| Skill | Returns |
|-------|---------|
| `interact_get_text` | Text content (Text or TMP_Text) |
| `interact_get_active` | activeSelf, activeInHierarchy |
| `interact_get_rect` | RectTransform size/position/anchors |
| `interact_get_component_prop` | Any component property value |
| `interact_get_color` | Graphic color (r, g, b, a, hex) |
| `interact_get_interactable` | Selectable interactable state |
| `interact_get_toggle_state` | Toggle isOn |
| `interact_get_slider_value` | Slider value, min, max |

## GameObject Queries

| Skill | Returns |
|-------|---------|
| `interact_get_field` | MonoBehaviour field value (incl. SerializeField) |
| `interact_get_position` | Transform position/rotation/scale |
| `interact_get_children` | Child GameObject list |
| `interact_find_by_tag` | GameObjects by tag |
| `interact_find_by_component` | GameObjects by component type |

## MonoBehaviour Interaction

| Skill | Parameters | Description |
|-------|-----------|-------------|
| `interact_invoke_method` | name, methodName, args (JSON) | Call a public method |
| `interact_set_field` | name, fieldName, value, componentType | Set field value |
| `interact_send_message` | name, methodName, value | SendMessage to GameObject |

## Testing Pattern

AI workflow for testing:

```
1. interact_enter_playmode()
2. interact_wait_frames(5) → wait for scene init
3. interact_click("ButtonName") → trigger action
4. interact_get_text("ResultLabel") → read result
5. AI compares result with expected value
6. interact_exit_playmode()
```

## Notes

- TextMeshPro is auto-detected for InputField and Dropdown
- `interact_click` tries Button.onClick first, then falls back to ExecuteEvents
- `interact_get_field` and `interact_set_field` use reflection to access private `[SerializeField]` fields
- `interact_invoke_method` args should be a JSON array: `'["arg1", 42, true]'`
```

- [ ] **Step 2: Update module index in skills/SKILL.md**

Add a row to the Modules table in `SkillsForUnity/unity-skills~/skills/SKILL.md`:

```markdown
| [interact](./interact/SKILL.md) | Playwright-style interaction testing | No |
```

Insert this row after the `test` row in the Core runtime modules table.

- [ ] **Step 3: Commit**

```bash
git add SkillsForUnity/unity-skills~/skills/interact/SKILL.md SkillsForUnity/unity-skills~/skills/SKILL.md
git commit -m "docs: add interact skill documentation and module index entry"
```

---

## Self-Review

**1. Spec coverage:**
- Play Mode control: Task 1 ✓
- UGUI interactions (click, submit_text, toggle, slider, dropdown, pointer): Task 2 ✓
- UI state queries (get_text, get_active, get_rect, get_component_prop, get_color, get_interactable, get_toggle_state, get_slider_value): Task 3 ✓
- MonoBehaviour interaction (invoke_method, set_field, send_message): Task 5 ✓
- GameObject queries (get_field, get_position, get_children, find_by_tag, find_by_component): Task 4 ✓
- Documentation: Task 6 ✓

**2. Placeholder scan:** No TBD/TODO/placeholder patterns found.

**3. Type consistency:** All methods use consistent `(string name, int instanceId)` parameters. `FindTarget` is used uniformly. `ComponentSkills.FindComponentType` and `ComponentSkills.ConvertValue` referenced correctly (they are `internal static` or `public static` in the codebase).
