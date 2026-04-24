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
