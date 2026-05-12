---
name: ugui-builder
description: "UGUI composite builders. Use when building UI widgets: scrollable lists, forms, modals, tab views, HUDs, grids. Triggers: scrolllist, form, modal, dialog, tabview, HUD, grid, UGUI, 滾動列表, 表單, 對話框."
---

# UGUI Builder Skills

> **BUILDER-FIRST**: For common UI patterns, use `ugui_build_*` composites — they create the correct hierarchy automatically. For custom layouts not covered by composites, use the Layout Recipes below.

## TMP Preference Rule

If `TextMeshPro` is available in the project, **always use TMP components** instead of Legacy UI components:

| Legacy | TMP Equivalent |
|--------|----------------|
| `UnityEngine.UI.Text` | `TMPro.TextMeshProUGUI` |
| `UnityEngine.UI.InputField` | `TMPro.TMP_InputField` |
| `UnityEngine.UI.Dropdown` | `TMPro.TMP_Dropdown` |

The builders auto-detect TMP via reflection and use TMP variants when available. No extra config needed.

## Decision Tree: Which Builder to Use

```
Need scrollable list of items?           → ugui_build_scrolllist
Need user input fields / form?           → ugui_build_form
Need popup / confirmation dialog?        → ugui_build_modal
Need tabbed content panels?              → ugui_build_tabview
Need fixed screen-corner status display? → ugui_build_hud
Need grid of equal-size cells?           → ugui_build_grid
None of the above?                       → See Layout Recipes section
```

## Web → UGUI Concept Map

| Web / CSS | UGUI Component | Key Setting |
|-----------|----------------|-------------|
| `display:flex; flex-direction:row` | HorizontalLayoutGroup | spacing, padding |
| `display:flex; flex-direction:column` | VerticalLayoutGroup | spacing, padding |
| `display:grid` | GridLayoutGroup | cellSize, spacing, constraint |
| `flex-grow:1` | LayoutElement | flexibleWidth/Height = 1 |
| `width:fit-content` | ContentSizeFitter | horizontalFit = PreferredSize |
| `overflow:scroll` | ScrollRect | Viewport(RectMask2D) → Content(CSF) |
| `z-index` | Sibling Index | SetSiblingIndex() — higher = on top |
| `gap` | LayoutGroup.spacing | single value only |
| `position:absolute` | RectTransform | anchorMin == anchorMax |
| `position:fixed` | RectTransform | anchor to screen corner |

## RectTransform: Two Modes

**Fixed Size** (`anchorMin == anchorMax`): Inspector shows Pos X/Y + Width/Height.
Equivalent to `position:absolute` with explicit dimensions.

**Stretch** (`anchorMin != anchorMax`): Inspector shows Left/Right/Top/Bottom margins.
**NEVER manually set Width when in stretch mode** — sizeDelta means margin offset, not size.

**Canvas Scaler:** Always set `Scale With Screen Size`. Use `Match = 0.5` as default.

## Common LLM Mistakes — Avoid These

1. **Setting Width in stretch mode** — `anchorMin.x != anchorMax.x` means sizeDelta.x = -(left+right). Setting Width directly is ignored or broken.
2. **Missing ContentSizeFitter on ScrollView Content** — Content height won't grow. Must add CSF + `verticalFit = PreferredSize`.
3. **Manual position inside LayoutGroup** — LayoutGroup overrides all child RectTransforms. anchoredPosition is silently ignored.
4. **ScrollRect MovementType = Unrestricted** — content escapes bounds permanently. Use Elastic or Clamped.
5. **Missing Canvas Scaler** — UI breaks at non-reference resolutions.

## Composite Builders

### ugui_build_scrolllist

```python
ugui_build_scrolllist(
    name="MyList",      # root object name
    parent="Canvas",    # parent canvas/panel
    items=[             # initial items (optional)
        {"id": "1", "text": "Item One"},
        {"id": "2", "text": "Item Two"}
    ],
    item_height=60,     # height of each item row
    width=400,
    height=600,
    x=0, y=0,
    show_scrollbar=True
)
# Returns: {"root": "MyList", "content": "MyList/Viewport/Content", "named_objects": {...}}
```

Hierarchy created:
```
MyList (ScrollRect, Elastic)
  ├─ Viewport (RectMask2D, stretch)
  │   └─ Content (VerticalLayoutGroup + ContentSizeFitter verticalFit=Preferred)
  │       └─ Item[n] (LayoutElement preferredHeight=item_height)
  └─ Scrollbar (optional, BottomToTop)
```

### ugui_build_form

```python
ugui_build_form(
    name="LoginForm",
    parent="Canvas",
    fields=[
        {"type": "text",     "label": "Username", "placeholder": "Enter username"},
        {"type": "password", "label": "Password", "placeholder": "Enter password"},
        {"type": "number",   "label": "Port",     "placeholder": "8080"},
        {"type": "dropdown", "label": "Mode",     "options": ["Easy", "Normal", "Hard"]},
        {"type": "toggle",   "label": "Remember"}
    ],
    submit_label="Login",
    width=360,
    x=0, y=0
)
# Height is auto-sized by ContentSizeFitter — do NOT pass height parameter
```

Field types:

| type | Component | Notes |
|------|-----------|-------|
| `text` | InputField / TMP_InputField | default |
| `password` | InputField (Password mode) | characters masked |
| `number` | InputField (DecimalNumber mode) | numeric keyboard |
| `dropdown` | Dropdown / TMP_Dropdown | requires `options: [...]` list |
| `toggle` | Toggle | checkbox-style |

Hierarchy created:
```
LoginForm (VerticalLayoutGroup + ContentSizeFitter verticalFit=Preferred, center anchor)
  ├─ Field_Username (HorizontalLayoutGroup)
  │   ├─ Label (Text/TMP, LayoutElement preferredWidth=120)
  │   └─ Input (InputField/TMP_InputField, LayoutElement flexibleWidth=1)
  ├─ Field_Mode (HorizontalLayoutGroup)
  │   ├─ Label
  │   └─ Input (Dropdown/TMP_Dropdown with options + Arrow)
  └─ SubmitButton (Button, blue)
```

### ugui_build_modal

```python
ugui_build_modal(
    name="ConfirmDialog",
    parent="Canvas",
    title="Confirm Exit",
    message="Are you sure you want to quit?",
    buttons=["Cancel", "Confirm"],
    width=400,
    height=220,
    show_overlay=True
)
# Modal starts INACTIVE — call SetActive(true) via gameobject_set_active to show
```

### ugui_build_tabview

```python
ugui_build_tabview(
    name="Settings",
    parent="Canvas",
    tabs=["Audio", "Video", "Controls"],
    tab_height=40,
    width=600,
    height=400,
    x=0, y=0
)
# Access panels: named_objects["panels"]["Audio"] = "Settings/ContentArea/Panel_Audio"
```

### ugui_build_hud

```python
ugui_build_hud(
    name="GameHUD",
    parent="Canvas",
    corner="top-left",   # top-left | top-right | bottom-left | bottom-right
    elements=[
        {"type": "text",   "name": "Score",  "text": "Score: 0"},
        {"type": "slider", "name": "HP"}
    ],
    padding=20
)
```

### ugui_build_grid

```python
ugui_build_grid(
    name="ItemGrid",
    parent="Canvas",
    columns=4,
    cell_width=120,
    cell_height=120,
    spacing=8,
    scrollable=True,
    width=520,
    height=480,
    x=0, y=0
)
```

## Layout Recipes (Custom Layouts)

Use these when composite builders don't fit. Compose with `ui_create_*` and `ui_set_*` primitives.

### Recipe: Form Row (Label + Input)

```
HorizontalLayoutGroup (row container)
  ├─ Label (Text/TMP, LayoutElement: preferredWidth=120, minWidth=80)
  └─ InputField/TMP_InputField (LayoutElement: flexibleWidth=1)

Container anchor: horizontal stretch (anchorMin.x=0, anchorMax.x=1)
NEVER manually set width on row container when inside a LayoutGroup.
InputField MUST have LayoutElement flexibleWidth=1 to fill remaining space.
```

### Recipe: Vertical Form

```
VerticalLayoutGroup + ContentSizeFitter (container)
  ├─ FormRow_1 (HLG — see Form Row recipe)
  ├─ FormRow_2 (HLG)
  └─ SubmitButton

ContentSizeFitter: verticalFit = PreferredSize
Container anchor: horizontal stretch, top-anchored (anchorMin.x=0, anchorMax.x=1, anchorMin.y=anchorMax.y=1)
```

### Recipe: Manual ScrollList

```
ScrollView root (ScrollRect, MovementType.Elastic)
  ├─ Viewport (RectMask2D, stretch fill parent: anchorMin=0,0 anchorMax=1,1 offsetMin/Max=0)
  │   └─ Content (VerticalLayoutGroup + ContentSizeFitter verticalFit=PreferredSize)
  │       anchorMin=(0,1) anchorMax=(1,1) pivot=(0.5,1) sizeDelta=0
  │       └─ Item[n]
  └─ Scrollbar Vertical (optional)

Auto Layout calc order: Width before Height. Height can depend on width, not vice versa.
```
