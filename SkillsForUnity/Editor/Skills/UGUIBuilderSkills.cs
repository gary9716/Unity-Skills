using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace UnitySkills
{
    public static class UGUIBuilderSkills
    {
        // ─── TMP Detection (mirrors UISkills pattern) ───────────────────────

        private static Type _tmpTextType;
        private static Type _tmpInputFieldType;
        private static Type _tmpDropdownType;
        private static bool _tmpChecked;
        private static bool _tmpAvailable;

        private static bool IsTMPAvailable()
        {
            if (!_tmpChecked)
            {
                _tmpChecked = true;
                _tmpTextType       = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                _tmpInputFieldType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
                _tmpDropdownType   = Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro");
                _tmpAvailable = _tmpTextType != null;
            }
            return _tmpAvailable;
        }

        // ─── Shared Helpers ─────────────────────────────────────────────────

        private static void SetStretch(RectTransform rt,
            float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void SetCenterAnchor(RectTransform rt,
            float width, float height, float x = 0, float y = 0)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static void SetTopStretch(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void SetCornerAnchor(RectTransform rt, string corner, float padding)
        {
            switch (corner.ToLowerInvariant())
            {
                case "top-right":
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(1, 1);
                    rt.anchoredPosition = new Vector2(-padding, -padding);
                    break;
                case "bottom-left":
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
                    rt.pivot = new Vector2(0, 0);
                    rt.anchoredPosition = new Vector2(padding, padding);
                    break;
                case "bottom-right":
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
                    rt.pivot = new Vector2(1, 0);
                    rt.anchoredPosition = new Vector2(-padding, padding);
                    break;
                default: // top-left
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                    rt.pivot = new Vector2(0, 1);
                    rt.anchoredPosition = new Vector2(padding, -padding);
                    break;
            }
        }

        private static GameObject FindOrCreateCanvas(string parentName)
        {
            if (!string.IsNullOrEmpty(parentName))
            {
                var found = GameObject.Find(parentName);
                if (found != null) return found;
            }
            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas != null) return canvas.gameObject;

            var go = new GameObject("Canvas", typeof(RectTransform));
            go.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            return go;
        }

        // TMP-aware text. Returns TextMeshProUGUI when TMP present, else UnityEngine.UI.Text.
        private static Component AddText(GameObject go, string text, int fontSize = 14,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            if (IsTMPAvailable())
            {
                var tmp = go.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("text")?.SetValue(tmp, text);
                _tmpTextType.GetProperty("fontSize")?.SetValue(tmp, (float)fontSize);
                _tmpTextType.GetProperty("alignment")?.SetValue(tmp, ToTMPAlignment(alignment));
                _tmpTextType.GetProperty("color")?.SetValue(tmp, Color.black);
                return (Component)tmp;
            }
            return AddLegacyText(go, text, fontSize, alignment);
        }

        private static int ToTMPAlignment(TextAnchor anchor)
        {
            // TextAlignmentOptions: Horizontal bits (Left=1,Center=2,Right=4) | Vertical bits (Top=256,Middle=512,Bottom=1024)
            switch (anchor)
            {
                case TextAnchor.MiddleCenter: return 514;
                case TextAnchor.UpperCenter:  return 258;
                case TextAnchor.LowerCenter:  return 1026;
                default:                      return 513; // MiddleLeft
            }
        }

        private static Text AddLegacyText(GameObject go, string text, int fontSize = 14,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.color = Color.black;
            t.alignment = alignment;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            return t;
        }

        // Adds TMP_InputField or legacy InputField (with children) to inputGo.
        private static void BuildInputField(GameObject inputGo, string fieldType, string placeholder)
        {
            if (IsTMPAvailable())
            {
                var inputField = inputGo.AddComponent(_tmpInputFieldType);

                var textAreaGo = new GameObject("Text Area", typeof(RectTransform));
                textAreaGo.transform.SetParent(inputGo.transform, false);
                Undo.RegisterCreatedObjectUndo(textAreaGo, "Create Text Area");
                var textAreaRt = textAreaGo.GetComponent<RectTransform>();
                textAreaRt.anchorMin = Vector2.zero;
                textAreaRt.anchorMax = Vector2.one;
                textAreaRt.offsetMin = new Vector2(5, 2);
                textAreaRt.offsetMax = new Vector2(-5, -2);
                textAreaGo.AddComponent<RectMask2D>();

                var phGo = new GameObject("Placeholder", typeof(RectTransform));
                phGo.transform.SetParent(textAreaGo.transform, false);
                Undo.RegisterCreatedObjectUndo(phGo, "Create Placeholder");
                SetStretch(phGo.GetComponent<RectTransform>());
                var phComp = phGo.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("text")?.SetValue(phComp, placeholder);
                _tmpTextType.GetProperty("fontSize")?.SetValue(phComp, 13f);
                _tmpTextType.GetProperty("color")?.SetValue(phComp, new Color(0.5f, 0.5f, 0.5f, 1f));

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(textAreaGo.transform, false);
                Undo.RegisterCreatedObjectUndo(textGo, "Create Input Text");
                SetStretch(textGo.GetComponent<RectTransform>());
                var textComp = textGo.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("fontSize")?.SetValue(textComp, 13f);

                _tmpInputFieldType.GetProperty("textViewport")?.SetValue(inputField, textAreaRt);
                _tmpInputFieldType.GetProperty("textComponent")?.SetValue(inputField, textComp);
                _tmpInputFieldType.GetProperty("placeholder")?.SetValue(inputField, phComp);

                // TMP_InputField.ContentType: Password=7, DecimalNumber=3
                var ctProp = _tmpInputFieldType.GetProperty("contentType");
                if (ctProp != null)
                {
                    if (fieldType == "password")
                        ctProp.SetValue(inputField, Enum.ToObject(ctProp.PropertyType, 7));
                    else if (fieldType == "number")
                        ctProp.SetValue(inputField, Enum.ToObject(ctProp.PropertyType, 3));
                }
            }
            else
            {
                var inputField = inputGo.AddComponent<InputField>();
                if (fieldType == "password")
                    inputField.contentType = InputField.ContentType.Password;
                else if (fieldType == "number")
                    inputField.contentType = InputField.ContentType.DecimalNumber;

                var phGo = new GameObject("Placeholder", typeof(RectTransform));
                phGo.transform.SetParent(inputGo.transform, false);
                Undo.RegisterCreatedObjectUndo(phGo, "Create Placeholder");
                SetStretch(phGo.GetComponent<RectTransform>(), 5, 5, 2, 2);
                var phText = AddLegacyText(phGo, placeholder, 13);
                phText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                inputField.placeholder = phText;

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(inputGo.transform, false);
                Undo.RegisterCreatedObjectUndo(textGo, "Create Input Text");
                SetStretch(textGo.GetComponent<RectTransform>(), 5, 5, 2, 2);
                inputField.textComponent = AddLegacyText(textGo, "", 13);
            }
        }

        // Adds TMP_Dropdown or legacy Dropdown (with caption label + arrow) to dropGo.
        private static void BuildDropdown(GameObject dropGo, string[] options)
        {
            if (IsTMPAvailable() && _tmpDropdownType != null)
            {
                var dropdown = dropGo.AddComponent(_tmpDropdownType);

                var captionGo = new GameObject("Label", typeof(RectTransform));
                captionGo.transform.SetParent(dropGo.transform, false);
                Undo.RegisterCreatedObjectUndo(captionGo, "Create Caption");
                SetStretch(captionGo.GetComponent<RectTransform>(), 10, 30, 4, 4);
                var captionComp = captionGo.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("text")?.SetValue(captionComp, options.Length > 0 ? options[0] : "");
                _tmpTextType.GetProperty("fontSize")?.SetValue(captionComp, 13f);
                _tmpDropdownType.GetProperty("captionText")?.SetValue(dropdown, captionComp);

                BuildDropdownArrow(dropGo);

                var optionDataType = Type.GetType("TMPro.TMP_Dropdown+OptionData, Unity.TextMeshPro");
                if (optionDataType != null)
                {
                    var listType = typeof(List<>).MakeGenericType(optionDataType);
                    var optList  = Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add");
                    foreach (var opt in options)
                    {
                        var optData = Activator.CreateInstance(optionDataType, new object[] { opt });
                        addMethod.Invoke(optList, new[] { optData });
                    }
                    _tmpDropdownType.GetProperty("options")?.SetValue(dropdown, optList);
                }
            }
            else
            {
                var dropdown = dropGo.AddComponent<Dropdown>();

                var captionGo = new GameObject("Label", typeof(RectTransform));
                captionGo.transform.SetParent(dropGo.transform, false);
                Undo.RegisterCreatedObjectUndo(captionGo, "Create Caption");
                SetStretch(captionGo.GetComponent<RectTransform>(), 10, 30, 4, 4);
                dropdown.captionText = AddLegacyText(captionGo, options.Length > 0 ? options[0] : "", 13);

                BuildDropdownArrow(dropGo);

                var optList = new List<Dropdown.OptionData>();
                foreach (var opt in options)
                    optList.Add(new Dropdown.OptionData(opt));
                dropdown.options = optList;
            }
        }

        private static void BuildDropdownArrow(GameObject parent)
        {
            var arrow = new GameObject("Arrow", typeof(RectTransform));
            arrow.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(arrow, "Create Arrow");
            var arrowRt = arrow.GetComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1, 0.5f);
            arrowRt.anchorMax = new Vector2(1, 0.5f);
            arrowRt.pivot = new Vector2(1, 0.5f);
            arrowRt.sizeDelta = new Vector2(20, 20);
            arrowRt.anchoredPosition = new Vector2(-5, 0);
            arrow.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
        }

        private static object BuildResult(string rootName, string contentPath,
            Dictionary<string, object> named)
        {
            return new { root = rootName, content = contentPath, named_objects = named };
        }

        // ─── ugui_build_scrolllist ───────────────────────────────────────────

        [UnitySkill("ugui_build_scrolllist",
            "Build a complete scrollable list: ScrollRect > Viewport(RectMask2D) > Content(VLG+ContentSizeFitter)",
            TracksWorkflow = true)]
        public static object UGUIBuildScrollList(
            string name = "ScrollList",
            string parent = null,
            string items = null,
            float itemHeight = 60f,
            float width = 400f,
            float height = 600f,
            float x = 0f,
            float y = 0f,
            bool showScrollbar = true)
        {
            var parentGo = FindOrCreateCanvas(parent);

            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parentGo.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create ScrollList");
            var scrollRect = root.AddComponent<ScrollRect>();
            root.AddComponent<Image>().color = Color.clear;
            SetCenterAnchor(root.GetComponent<RectTransform>(), width, height, x, y);

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(viewport, "Create Viewport");
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = Color.clear;
            SetStretch(viewport.GetComponent<RectTransform>());

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            Undo.RegisterCreatedObjectUndo(content, "Create Content");
            SetTopStretch(content.GetComponent<RectTransform>());
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = content.GetComponent<RectTransform>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;

            var named = new Dictionary<string, object>
            {
                { "viewport", $"{name}/Viewport" },
                { "content",  $"{name}/Viewport/Content" }
            };

            if (showScrollbar)
            {
                var sbGo = new GameObject("Scrollbar", typeof(RectTransform));
                sbGo.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(sbGo, "Create Scrollbar");
                sbGo.AddComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 1f);
                var sb = sbGo.AddComponent<Scrollbar>();
                sb.direction = Scrollbar.Direction.BottomToTop;
                var sbRt = sbGo.GetComponent<RectTransform>();
                sbRt.anchorMin = new Vector2(1, 0);
                sbRt.anchorMax = new Vector2(1, 1);
                sbRt.pivot     = new Vector2(1, 0.5f);
                sbRt.sizeDelta = new Vector2(20, 0);
                sbRt.anchoredPosition = Vector2.zero;

                var sliding = new GameObject("Sliding Area", typeof(RectTransform));
                sliding.transform.SetParent(sbGo.transform, false);
                Undo.RegisterCreatedObjectUndo(sliding, "Create Sliding Area");
                SetStretch(sliding.GetComponent<RectTransform>(), 10, 10, 10, 10);

                var handle = new GameObject("Handle", typeof(RectTransform));
                handle.transform.SetParent(sliding.transform, false);
                Undo.RegisterCreatedObjectUndo(handle, "Create Handle");
                handle.AddComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 1f);
                SetStretch(handle.GetComponent<RectTransform>(), -10, -10, -10, -10);
                sb.handleRect = handle.GetComponent<RectTransform>();

                scrollRect.verticalScrollbar = sb;
                scrollRect.verticalScrollbarVisibility =
                    ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

                viewport.GetComponent<RectTransform>().offsetMax = new Vector2(-20, 0);
                named["scrollbar"] = $"{name}/Scrollbar";
            }

            if (!string.IsNullOrEmpty(items) && items != "[]")
            {
                var itemArray = JArray.Parse(items);
                foreach (var itemToken in itemArray)
                {
                    var itemId   = itemToken["id"]?.ToString() ?? "Item";
                    var itemText = itemToken["text"]?.ToString() ?? "";
                    var itemGo = new GameObject(itemId, typeof(RectTransform));
                    itemGo.transform.SetParent(content.transform, false);
                    Undo.RegisterCreatedObjectUndo(itemGo, "Create List Item");
                    itemGo.AddComponent<Image>().color = new Color(1, 1, 1, 0.05f);
                    var le = itemGo.AddComponent<LayoutElement>();
                    le.preferredHeight = itemHeight;
                    le.minHeight = itemHeight;
                    var textGo = new GameObject("Label", typeof(RectTransform));
                    textGo.transform.SetParent(itemGo.transform, false);
                    Undo.RegisterCreatedObjectUndo(textGo, "Create Item Label");
                    SetStretch(textGo.GetComponent<RectTransform>(), 8, 8, 4, 4);
                    AddText(textGo, itemText);
                }
            }

            EditorUtility.SetDirty(root);
            return BuildResult(name, $"{name}/Viewport/Content", named);
        }

        // ─── ugui_build_form ─────────────────────────────────────────────────

        [UnitySkill("ugui_build_form",
            "Build a form: VLG+ContentSizeFitter root, HLG rows. Field types: text, password, number, dropdown (with options:[]), toggle. Height auto-sized.",
            TracksWorkflow = true)]
        public static object UGUIBuildForm(
            string name = "Form",
            string parent = null,
            string fields = null,
            string submitLabel = "Submit",
            float width = 360f,
            float x = 0f,
            float y = 0f)
        {
            var parentGo = FindOrCreateCanvas(parent);

            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parentGo.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create Form");
            root.AddComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f, 1f);
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(width, 0); // height = auto
            rootRt.anchoredPosition = new Vector2(x, y);
            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 8;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var named = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(fields) && fields != "[]")
            {
                var fieldArray = JArray.Parse(fields);
                foreach (var f in fieldArray)
                {
                    var fieldType   = f["type"]?.ToString() ?? "text";
                    var fieldLabel  = f["label"]?.ToString() ?? "Field";
                    var placeholder = f["placeholder"]?.ToString() ?? "";

                    var rowGo = new GameObject($"Field_{fieldLabel}", typeof(RectTransform));
                    rowGo.transform.SetParent(root.transform, false);
                    Undo.RegisterCreatedObjectUndo(rowGo, "Create Form Row");
                    var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = 8;
                    hlg.childControlHeight = true;
                    hlg.childControlWidth = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = false;
                    var rowLe = rowGo.AddComponent<LayoutElement>();
                    rowLe.preferredHeight = 32;
                    rowLe.minHeight = 32;

                    // Label
                    var labelGo = new GameObject("Label", typeof(RectTransform));
                    labelGo.transform.SetParent(rowGo.transform, false);
                    Undo.RegisterCreatedObjectUndo(labelGo, "Create Label");
                    AddText(labelGo, fieldLabel, 13);
                    var labelLe = labelGo.AddComponent<LayoutElement>();
                    labelLe.preferredWidth = 120;
                    labelLe.minWidth = 80;

                    // Input widget — branch by field type
                    if (fieldType == "dropdown")
                    {
                        var dropGo = new GameObject("Input", typeof(RectTransform));
                        dropGo.transform.SetParent(rowGo.transform, false);
                        Undo.RegisterCreatedObjectUndo(dropGo, "Create Dropdown");
                        dropGo.AddComponent<Image>().color = Color.white;
                        dropGo.AddComponent<LayoutElement>().flexibleWidth = 1;
                        var optTokens = f["options"] as JArray;
                        var opts = optTokens != null ? optTokens.ToObject<string[]>() : new string[0];
                        BuildDropdown(dropGo, opts);
                    }
                    else if (fieldType == "toggle")
                    {
                        var toggleGo = new GameObject("Input", typeof(RectTransform));
                        toggleGo.transform.SetParent(rowGo.transform, false);
                        Undo.RegisterCreatedObjectUndo(toggleGo, "Create Toggle");
                        toggleGo.AddComponent<LayoutElement>().flexibleWidth = 1;
                        var toggle = toggleGo.AddComponent<Toggle>();
                        var bgGo = new GameObject("Background", typeof(RectTransform));
                        bgGo.transform.SetParent(toggleGo.transform, false);
                        Undo.RegisterCreatedObjectUndo(bgGo, "Create Toggle BG");
                        var bgRt = bgGo.GetComponent<RectTransform>();
                        bgRt.anchorMin = new Vector2(0, 0.5f);
                        bgRt.anchorMax = new Vector2(0, 0.5f);
                        bgRt.pivot = new Vector2(0, 0.5f);
                        bgRt.sizeDelta = new Vector2(20, 20);
                        bgRt.anchoredPosition = Vector2.zero;
                        var bgImg = bgGo.AddComponent<Image>();
                        bgImg.color = Color.white;
                        toggle.targetGraphic = bgImg;
                        var checkGo = new GameObject("Checkmark", typeof(RectTransform));
                        checkGo.transform.SetParent(bgGo.transform, false);
                        Undo.RegisterCreatedObjectUndo(checkGo, "Create Checkmark");
                        SetStretch(checkGo.GetComponent<RectTransform>(), 2, 2, 2, 2);
                        var checkImg = checkGo.AddComponent<Image>();
                        checkImg.color = new Color(0.2f, 0.5f, 1f, 1f);
                        toggle.graphic = checkImg;
                    }
                    else
                    {
                        // text / password / number → InputField (TMP or legacy)
                        var inputGo = new GameObject("Input", typeof(RectTransform));
                        inputGo.transform.SetParent(rowGo.transform, false);
                        Undo.RegisterCreatedObjectUndo(inputGo, "Create Input");
                        inputGo.AddComponent<Image>().color = Color.white;
                        inputGo.AddComponent<LayoutElement>().flexibleWidth = 1;
                        BuildInputField(inputGo, fieldType, placeholder);
                    }

                    named[$"field_{fieldLabel}"] = $"{name}/Field_{fieldLabel}/Input";
                }
            }

            // Submit button
            var btnGo = new GameObject("SubmitButton", typeof(RectTransform));
            btnGo.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(btnGo, "Create Submit Button");
            btnGo.AddComponent<Image>().color = new Color(0.2f, 0.5f, 1f, 1f);
            btnGo.AddComponent<Button>();
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 36;
            btnLe.minHeight = 36;
            var btnTextGo = new GameObject("Text", typeof(RectTransform));
            btnTextGo.transform.SetParent(btnGo.transform, false);
            Undo.RegisterCreatedObjectUndo(btnTextGo, "Create Button Text");
            SetStretch(btnTextGo.GetComponent<RectTransform>());
            AddText(btnTextGo, submitLabel, 14, TextAnchor.MiddleCenter);
            named["submit_button"] = $"{name}/SubmitButton";

            EditorUtility.SetDirty(root);
            return BuildResult(name, null, named);
        }

        // ─── ugui_build_modal ────────────────────────────────────────────────

        [UnitySkill("ugui_build_modal",
            "Build an inactive modal dialog with optional overlay. Call gameobject_set_active to show.",
            TracksWorkflow = true)]
        public static object UGUIBuildModal(
            string name = "Modal",
            string parent = null,
            string title = "Dialog",
            string message = "",
            string buttons = null,
            float width = 400f,
            float height = 220f,
            bool showOverlay = true)
        {
            var parentGo = FindOrCreateCanvas(parent);
            var named = new Dictionary<string, object>();

            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parentGo.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create Modal");
            SetStretch(root.GetComponent<RectTransform>());

            if (showOverlay)
            {
                var overlay = new GameObject("Overlay", typeof(RectTransform));
                overlay.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(overlay, "Create Overlay");
                SetStretch(overlay.GetComponent<RectTransform>());
                overlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
                overlay.AddComponent<Button>(); // block input behind modal
                named["overlay"] = $"{name}/Overlay";
            }

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(panel, "Create Panel");
            panel.AddComponent<Image>().color = new Color(0.95f, 0.95f, 0.95f, 1f);
            SetCenterAnchor(panel.GetComponent<RectTransform>(), width, height);
            var panelVlg = panel.AddComponent<VerticalLayoutGroup>();
            panelVlg.padding = new RectOffset(20, 20, 16, 16);
            panelVlg.spacing = 10;
            panelVlg.childControlHeight = true;
            panelVlg.childControlWidth = true;
            panelVlg.childForceExpandWidth = true;
            panelVlg.childForceExpandHeight = false;
            named["panel"] = $"{name}/Panel";

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panel.transform, false);
            Undo.RegisterCreatedObjectUndo(titleGo, "Create Title");
            AddText(titleGo, title, 18, TextAnchor.MiddleCenter);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 28;
            named["title"] = $"{name}/Panel/Title";

            if (!string.IsNullOrEmpty(message))
            {
                var msgGo = new GameObject("Message", typeof(RectTransform));
                msgGo.transform.SetParent(panel.transform, false);
                Undo.RegisterCreatedObjectUndo(msgGo, "Create Message");
                AddText(msgGo, message, 13, TextAnchor.MiddleCenter);
                msgGo.AddComponent<LayoutElement>().preferredHeight = 40;
                named["message"] = $"{name}/Panel/Message";
            }

            var btnRow = new GameObject("ButtonRow", typeof(RectTransform));
            btnRow.transform.SetParent(panel.transform, false);
            Undo.RegisterCreatedObjectUndo(btnRow, "Create ButtonRow");
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childControlHeight = true;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36;
            named["button_row"] = $"{name}/Panel/ButtonRow";

            var buttonLabels = new[] { "OK" };
            if (!string.IsNullOrEmpty(buttons) && buttons != "[]")
            {
                var parsed = JArray.Parse(buttons);
                buttonLabels = new string[parsed.Count];
                for (int i = 0; i < parsed.Count; i++)
                    buttonLabels[i] = parsed[i].ToString();
            }

            var buttonPaths = new Dictionary<string, string>();
            foreach (var label in buttonLabels)
            {
                var btn = new GameObject(label, typeof(RectTransform));
                btn.transform.SetParent(btnRow.transform, false);
                Undo.RegisterCreatedObjectUndo(btn, "Create Modal Button");
                btn.AddComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.AddComponent<Button>();
                btn.AddComponent<LayoutElement>().flexibleWidth = 1;
                var btnText = new GameObject("Text", typeof(RectTransform));
                btnText.transform.SetParent(btn.transform, false);
                Undo.RegisterCreatedObjectUndo(btnText, "Create Button Text");
                SetStretch(btnText.GetComponent<RectTransform>());
                AddText(btnText, label, 13, TextAnchor.MiddleCenter);
                buttonPaths[label] = $"{name}/Panel/ButtonRow/{label}";
            }
            named["buttons"] = buttonPaths;

            root.SetActive(false);

            EditorUtility.SetDirty(root);
            return BuildResult(name, null, named);
        }

        // ─── ugui_build_tabview ──────────────────────────────────────────────

        [UnitySkill("ugui_build_tabview",
            "Build tabbed UI: TabBar(HLG+ToggleGroup) + ContentArea with one panel per tab. First tab active.",
            TracksWorkflow = true)]
        public static object UGUIBuildTabView(
            string name = "TabView",
            string parent = null,
            string tabs = null,
            float tabHeight = 40f,
            float width = 600f,
            float height = 400f,
            float x = 0f,
            float y = 0f)
        {
            var parentGo = FindOrCreateCanvas(parent);
            var named = new Dictionary<string, object>();

            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parentGo.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create TabView");
            SetCenterAnchor(root.GetComponent<RectTransform>(), width, height, x, y);
            var rootVlg = root.AddComponent<VerticalLayoutGroup>();
            rootVlg.childControlHeight = true;
            rootVlg.childControlWidth = true;
            rootVlg.childForceExpandWidth = true;
            rootVlg.childForceExpandHeight = false;
            rootVlg.spacing = 0;

            var tabBar = new GameObject("TabBar", typeof(RectTransform));
            tabBar.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(tabBar, "Create TabBar");
            tabBar.AddComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 1f);
            var tabBarHlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            tabBarHlg.childControlHeight = true;
            tabBarHlg.childControlWidth = true;
            tabBarHlg.childForceExpandWidth = true;
            tabBarHlg.childForceExpandHeight = false;
            var toggleGroup = tabBar.AddComponent<ToggleGroup>();
            toggleGroup.allowSwitchOff = false;
            var tabBarLe = tabBar.AddComponent<LayoutElement>();
            tabBarLe.preferredHeight = tabHeight;
            tabBarLe.minHeight = tabHeight;
            named["tab_bar"] = $"{name}/TabBar";

            var contentArea = new GameObject("ContentArea", typeof(RectTransform));
            contentArea.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(contentArea, "Create ContentArea");
            contentArea.AddComponent<Image>().color = new Color(0.92f, 0.92f, 0.92f, 1f);
            contentArea.AddComponent<LayoutElement>().flexibleHeight = 1;
            named["content_area"] = $"{name}/ContentArea";

            var tabNames = new string[0];
            if (!string.IsNullOrEmpty(tabs) && tabs != "[]")
            {
                var parsed = JArray.Parse(tabs);
                tabNames = new string[parsed.Count];
                for (int i = 0; i < parsed.Count; i++)
                    tabNames[i] = parsed[i].ToString();
            }

            var tabPaths   = new Dictionary<string, string>();
            var panelPaths = new Dictionary<string, string>();

            for (int i = 0; i < tabNames.Length; i++)
            {
                var tabName = tabNames[i];

                var tabGo = new GameObject($"Tab_{tabName}", typeof(RectTransform));
                tabGo.transform.SetParent(tabBar.transform, false);
                Undo.RegisterCreatedObjectUndo(tabGo, "Create Tab");
                tabGo.AddComponent<Image>().color = i == 0
                    ? new Color(1, 1, 1, 1f)
                    : new Color(0.7f, 0.7f, 0.7f, 1f);
                var toggle = tabGo.AddComponent<Toggle>();
                toggle.group = toggleGroup;
                toggle.isOn = i == 0;
                var tabTextGo = new GameObject("Label", typeof(RectTransform));
                tabTextGo.transform.SetParent(tabGo.transform, false);
                Undo.RegisterCreatedObjectUndo(tabTextGo, "Create Tab Label");
                SetStretch(tabTextGo.GetComponent<RectTransform>());
                AddText(tabTextGo, tabName, 13, TextAnchor.MiddleCenter);
                toggle.targetGraphic = tabGo.GetComponent<Image>();
                tabPaths[tabName] = $"{name}/TabBar/Tab_{tabName}";

                var panel = new GameObject($"Panel_{tabName}", typeof(RectTransform));
                panel.transform.SetParent(contentArea.transform, false);
                Undo.RegisterCreatedObjectUndo(panel, "Create Panel");
                SetStretch(panel.GetComponent<RectTransform>());
                panel.AddComponent<Image>().color = Color.white;
                panel.SetActive(i == 0);
                panelPaths[tabName] = $"{name}/ContentArea/Panel_{tabName}";
            }

            named["tabs"]   = tabPaths;
            named["panels"] = panelPaths;

            EditorUtility.SetDirty(root);
            return BuildResult(name, null, named);
        }

        // ─── ugui_build_hud ──────────────────────────────────────────────────

        [UnitySkill("ugui_build_hud",
            "Build a corner-anchored HUD container with VLG. Corner: top-left|top-right|bottom-left|bottom-right",
            TracksWorkflow = true)]
        public static object UGUIBuildHUD(
            string name = "HUD",
            string parent = null,
            string corner = "top-left",
            string elements = null,
            float padding = 20f)
        {
            var parentGo = FindOrCreateCanvas(parent);
            var named = new Dictionary<string, object>();

            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parentGo.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create HUD");
            var rt = root.GetComponent<RectTransform>();
            SetCornerAnchor(rt, corner, padding);
            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4;
            var csf = root.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var elementPaths = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(elements) && elements != "[]")
            {
                var elementArray = JArray.Parse(elements);
                foreach (var el in elementArray)
                {
                    var elType = el["type"]?.ToString() ?? "text";
                    var elName = el["name"]?.ToString() ?? "Element";
                    var elText = el["text"]?.ToString() ?? "";

                    var elGo = new GameObject(elName, typeof(RectTransform));
                    elGo.transform.SetParent(root.transform, false);
                    Undo.RegisterCreatedObjectUndo(elGo, "Create HUD Element");

                    if (elType == "text")
                    {
                        elGo.AddComponent<LayoutElement>().preferredHeight = 24;
                        AddText(elGo, elText, 13);
                    }
                    else if (elType == "slider")
                    {
                        var le = elGo.AddComponent<LayoutElement>();
                        le.preferredHeight = 20;
                        le.preferredWidth  = 160;
                        elGo.AddComponent<Slider>();
                        elGo.AddComponent<Image>().color = new Color(0.3f, 0.7f, 0.3f, 1f);
                    }
                    else if (elType == "image")
                    {
                        var le = elGo.AddComponent<LayoutElement>();
                        le.preferredHeight = 40;
                        le.preferredWidth  = 40;
                        elGo.AddComponent<Image>();
                    }
                    else // progressbar
                    {
                        var le = elGo.AddComponent<LayoutElement>();
                        le.preferredHeight = 16;
                        le.preferredWidth  = 160;
                        elGo.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1f, 1f);
                    }

                    elementPaths[elName] = $"{name}/{elName}";
                }
            }

            named["elements"] = elementPaths;

            EditorUtility.SetDirty(root);
            return BuildResult(name, null, named);
        }

        // ─── ugui_build_grid ─────────────────────────────────────────────────

        [UnitySkill("ugui_build_grid",
            "Build a GridLayoutGroup with FixedColumnCount, optionally wrapped in ScrollRect.",
            TracksWorkflow = true)]
        public static object UGUIBuildGrid(
            string name = "Grid",
            string parent = null,
            int columns = 3,
            float cellWidth = 120f,
            float cellHeight = 120f,
            float spacing = 8f,
            bool scrollable = true,
            float width = 400f,
            float height = 480f,
            float x = 0f,
            float y = 0f)
        {
            var parentGo = FindOrCreateCanvas(parent);
            var named = new Dictionary<string, object>();

            if (scrollable)
            {
                var root = new GameObject(name, typeof(RectTransform));
                root.transform.SetParent(parentGo.transform, false);
                Undo.RegisterCreatedObjectUndo(root, "Create Grid");
                root.AddComponent<Image>().color = Color.clear;
                SetCenterAnchor(root.GetComponent<RectTransform>(), width, height, x, y);
                var scrollRect = root.AddComponent<ScrollRect>();
                scrollRect.horizontal = false;
                scrollRect.vertical   = true;
                scrollRect.movementType = ScrollRect.MovementType.Elastic;

                var viewport = new GameObject("Viewport", typeof(RectTransform));
                viewport.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(viewport, "Create Viewport");
                viewport.AddComponent<RectMask2D>();
                viewport.AddComponent<Image>().color = Color.clear;
                SetStretch(viewport.GetComponent<RectTransform>());

                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                Undo.RegisterCreatedObjectUndo(content, "Create Content");
                SetTopStretch(content.GetComponent<RectTransform>());
                var glg = content.AddComponent<GridLayoutGroup>();
                glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = columns;
                glg.cellSize        = new Vector2(cellWidth, cellHeight);
                glg.spacing         = new Vector2(spacing, spacing);
                var csf = content.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scrollRect.viewport = viewport.GetComponent<RectTransform>();
                scrollRect.content  = content.GetComponent<RectTransform>();

                named["viewport"] = $"{name}/Viewport";
                named["content"]  = $"{name}/Viewport/Content";

                EditorUtility.SetDirty(root);
                return BuildResult(name, $"{name}/Viewport/Content", named);
            }
            else
            {
                var root = new GameObject(name, typeof(RectTransform));
                root.transform.SetParent(parentGo.transform, false);
                Undo.RegisterCreatedObjectUndo(root, "Create Grid");
                SetCenterAnchor(root.GetComponent<RectTransform>(), width, height, x, y);

                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(content, "Create Content");
                SetStretch(content.GetComponent<RectTransform>());
                var glg = content.AddComponent<GridLayoutGroup>();
                glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = columns;
                glg.cellSize        = new Vector2(cellWidth, cellHeight);
                glg.spacing         = new Vector2(spacing, spacing);

                named["content"] = $"{name}/Content";

                EditorUtility.SetDirty(root);
                return BuildResult(name, $"{name}/Content", named);
            }
        }
    }
}
