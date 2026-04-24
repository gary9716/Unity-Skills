# Interact Skills Design Spec — Playwright-style Unity Testing

> Date: 2026-04-23
> Status: Draft

## Goal

Add a new `interact_*` skill module to Unity-Skills that enables AI to simulate user interactions and query runtime state in Unity's Play Mode, similar to how Playwright tests web applications. No test code needs to be written — AI calls the API directly and judges results.

## Scope

- **In scope**: UGUI interaction simulation, GameObject/MonoBehaviour runtime state query, Play Mode control
- **Out of scope**: UI Toolkit support, visual regression (screenshot diff), record-and-replay

## Architecture

New file `Editor/Skills/InteractSkills.cs` using `[UnitySkill]` attribute for auto-registration in `SkillRouter`. Skill documentation at `unity-skills~/skills/interact/SKILL.md`.

### Execution Flow

```
AI calls interact_click("MyButton")
    → SkillRouter routes to InteractSkills.InteractClick()
    → Validate Play Mode is active
    → GameObjectFinder.Find("MyButton") → GameObject
    → GetComponent<Button>() → invoke onClick
    → Return { success: true, target, event }
```

## Skill Inventory

### UGUI Interaction Skills

| Skill | Parameters | Behavior |
|-------|-----------|----------|
| `interact_click` | name or instanceId | Invoke Button.onClick; also support generic IPointerClickHandler via ExecuteEvents |
| `interact_submit_text` | name, text | Set InputField.text and invoke onEndEdit |
| `interact_toggle` | name, isOn | Set Toggle.isOn (triggers onValueChanged) |
| `interact_slider_set` | name, value | Set Slider.value (triggers onValueChanged) |
| `interact_dropdown_set` | name, index | Set Dropdown.value (triggers onValueChanged) |
| `interact_pointer_event` | name, eventType | Send PointerEventData via ExecuteEvents (Enter/Exit/Down/Up/Click) |

### GameObject Behavior Skills

| Skill | Parameters | Behavior |
|-------|-----------|----------|
| `interact_invoke_method` | name, methodName, args (optional JSON) | Call a public method on a MonoBehaviour via reflection |
| `interact_set_field` | name, fieldName, value, componentType (optional) | Set a field value (including SerializeField) via reflection |
| `interact_send_message` | name, methodName, value (optional) | SendMessage to the GameObject |

### UI State Query Skills

| Skill | Parameters | Returns |
|-------|-----------|---------|
| `interact_get_text` | name or instanceId | Text content (Text or TMP_Text) |
| `interact_get_active` | name | activeSelf, activeInHierarchy |
| `interact_get_rect` | name | anchoredPosition, sizeDelta, anchorMin, anchorMax |
| `interact_get_component_prop` | name, component, property | Property value |
| `interact_get_color` | name | r, g, b, a from Graphic |
| `interact_get_interactable` | name | interactable state of Selectable |
| `interact_get_toggle_state` | name | isOn value |
| `interact_get_slider_value` | name | slider value, minValue, maxValue |

### GameObject Query Skills

| Skill | Parameters | Returns |
|-------|-----------|---------|
| `interact_get_field` | name, fieldName, componentType (optional) | Field value via reflection (including SerializeField) |
| `interact_get_position` | name | position, rotation (euler), scale |
| `interact_get_children` | name | Array of child names with instanceIds |
| `interact_find_by_tag` | tag | Array of matching GameObjects |
| `interact_find_by_component` | componentType | Array of GameObjects with that component |

### Play Mode Control Skills

| Skill | Parameters | Behavior |
|-------|-----------|----------|
| `interact_enter_playmode` | — | Enter Play Mode via EditorApplication |
| `interact_exit_playmode` | — | Exit Play Mode |
| `interact_wait_frames` | frames | Wait N frames (async job pattern) |
| `interact_snapshot_scene` | — | Return snapshot of all root GameObjects with key properties |

## Technical Details

### Play Mode Guard

All interaction and query skills check `EditorApplication.isPlaying`. If not in Play Mode, return:

```json
{ "error": "Not in Play Mode. Call interact_enter_playmode first." }
```

### Object Resolution

Use existing `GameObjectFinder.Find()` for name/instanceId resolution, consistent with other skill modules.

### UGUI Event Simulation Strategy

- **Direct invoke** for high-level events: `button.onClick.Invoke()`, `toggle.onValueChanged.Invoke()`, `slider.onValueChanged.Invoke()`, `dropdown.onValueChanged.Invoke()`
- **ExecuteEvents** for pointer-level events: `ExecuteEvents.Execute<IPointerClickHandler>(go, eventData, callback)`
- InputField: set `.text` property directly, then invoke `onEndEdit.Invoke(text)`

### Reflection for MonoBehaviour Access

- `interact_get_field` and `interact_set_field` use reflection with `BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance`
- Support basic types: int, float, string, bool, Vector2, Vector3, Color, Enum
- For complex types, return JSON-serialized representation

### Wait Frames Implementation

`interact_wait_frames` uses the existing async job pattern (returns jobId, poll with a companion skill). Internally uses `EditorApplication.delayCall` or a coroutine-like frame counter via `[UnityTest]` context or `EditorApplication.update`.

### Dependencies

- Existing: `GameObjectFinder`, `Validate`, `SkillRouter`, `UnitySkillAttribute`, `BatchExecutor` (if batch variants are added later)
- New: `InteractSkills.cs` only, no new package dependencies

## File Changes

1. **New**: `SkillsForUnity/Editor/Skills/InteractSkills.cs` — C# implementation
2. **New**: `SkillsForUnity/unity-skills~/skills/interact/SKILL.md` — Skill documentation
3. **Updated**: `SkillsForUnity/unity-skills~/skills/SKILL.md` — Add interact module to index table

## Example AI Workflow

```
# Test: clicking "AddScore" button increments the score display

1. interact_enter_playmode()
2. interact_click("AddScore")
3. interact_get_text("ScoreLabel")  →  { text: "1" }
4. interact_click("AddScore")
5. interact_get_text("ScoreLabel")  →  { text: "2" }
6. interact_exit_playmode()

# AI compares returned values and judges test passed/failed
```

## Future Extensions (not in this spec)

- `interact_screenshot` — capture Game view screenshot for visual validation
- `interact_input_key` — simulate keyboard input
- `interact_drag` — simulate drag and drop between two UI elements
- UI Toolkit (UIDocument) support
- Batch variants (`interact_click_batch`, etc.)
