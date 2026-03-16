using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnitySkills
{
    /// <summary>
    /// Project-level extensions for UnitySkills.
    /// Adds: assetPath support, prefab stage editing, SerializeField visibility, batch fix.
    /// </summary>
    public static class UnitySkillsExtensions
    {
        // ─── Fix 1: component_set_property with assetPath ─────────────

        [UnitySkill("component_set_property_ex",
            "Enhanced set property with assetPath support for prefab/asset references. " +
            "Use assetPath to reference project assets (prefabs, sprites, materials).",
            TracksWorkflow = true)]
        public static object ComponentSetPropertyEx(
            string name = null, int instanceId = 0, string path = null,
            string componentType = null, string propertyName = null,
            string value = null, string referencePath = null, string referenceName = null,
            string assetPath = null)
        {
            if (string.IsNullOrEmpty(componentType) || string.IsNullOrEmpty(propertyName))
                return new { error = "componentType and propertyName are required" };

            var (go, findErr) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (findErr != null) return findErr;

            var type = ComponentSkills.FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found: {componentType}" };

            var (prop, field) = FindMember(type, propertyName);
            if (prop == null && field == null)
                return new { error = $"Property/field not found: {propertyName}" };

            Undo.RecordObject(comp, "Set Property");

            try
            {
                var targetType = prop?.PropertyType ?? field.FieldType;
                object converted;

                if (!string.IsNullOrEmpty(assetPath))
                {
                    converted = ResolveAssetReference(targetType, assetPath);
                    if (converted == null)
                        return new { error = $"Could not load asset at '{assetPath}' as {targetType.Name}" };
                }
                else if (!string.IsNullOrEmpty(referencePath) || !string.IsNullOrEmpty(referenceName))
                {
                    var targetGo = GameObjectFinder.Find(name: referenceName, path: referencePath);
                    if (targetGo == null)
                        return new { error = $"Could not resolve reference: path='{referencePath}', name='{referenceName}'" };

                    if (targetType == typeof(Transform)) converted = targetGo.transform;
                    else if (targetType == typeof(GameObject)) converted = targetGo;
                    else if (typeof(Component).IsAssignableFrom(targetType)) converted = targetGo.GetComponent(targetType);
                    else converted = null;

                    if (converted == null)
                        return new { error = $"Could not resolve reference for {propertyName}" };
                }
                else
                {
                    // ConvertValue is internal to UnitySkills assembly, use reflection
                    converted = InvokeConvertValue(value, targetType);
                }

                if (prop != null && prop.CanWrite) prop.SetValue(comp, converted);
                else if (field != null) field.SetValue(comp, converted);
                else return new { error = $"Property {propertyName} is read-only" };

                EditorUtility.SetDirty(comp);
                return new { success = true, gameObject = go.name, component = componentType, property = propertyName, valueSet = converted?.ToString() ?? "null" };
            }
            catch (Exception ex) { return new { error = ex.Message }; }
        }

        // ─── Fix 2: component_set_property_batch_ex ──────────────────

        [UnitySkill("component_set_property_batch_ex",
            "Enhanced batch set properties. Accepts items as native JSON array (not string). " +
            "Supports assetPath for prefab/asset references.",
            TracksWorkflow = true)]
        public static object ComponentSetPropertyBatchEx(string items)
        {
            return BatchExecutor.Execute<BatchSetPropertyExItem>(items, item =>
            {
                var result = ComponentSetPropertyEx(
                    name: item.name, instanceId: item.instanceId, path: item.path,
                    componentType: item.componentType, propertyName: item.propertyName,
                    value: item.value, referencePath: item.referencePath,
                    referenceName: item.referenceName, assetPath: item.assetPath);

                // Check if result is an error
                var resultType = result.GetType();
                var errorProp = resultType.GetProperty("error") ?? resultType.GetField("error") as MemberInfo;
                if (errorProp != null)
                {
                    var errorVal = errorProp is PropertyInfo pi ? pi.GetValue(result) : ((FieldInfo)errorProp).GetValue(result);
                    throw new Exception(errorVal?.ToString() ?? "Unknown error");
                }
                return new { target = item.name ?? item.path, success = true, property = item.propertyName };
            }, item => item.name ?? item.path);
        }

        private class BatchSetPropertyExItem
        {
            public string name { get; set; }
            public int instanceId { get; set; }
            public string path { get; set; }
            public string componentType { get; set; }
            public string propertyName { get; set; }
            public string value { get; set; }
            public string referencePath { get; set; }
            public string referenceName { get; set; }
            public string assetPath { get; set; }
        }

        // ─── Fix 3: component_get_serialized_fields ──────────────────

        [UnitySkill("component_get_serialized_fields",
            "Get all SerializeField values on a component, including private fields. " +
            "Shows reference targets with paths for UnityEngine.Object references.")]
        public static object ComponentGetSerializedFields(
            string name = null, int instanceId = 0, string path = null,
            string componentType = null)
        {
            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var (go, findErr) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (findErr != null) return findErr;

            var type = ComponentSkills.FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found: {componentType}" };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var fields = type.GetFields(flags)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                .Select(f =>
                {
                    try
                    {
                        var val = f.GetValue(comp);
                        return new
                        {
                            name = f.Name,
                            type = f.FieldType.Name,
                            fullType = f.FieldType.FullName,
                            value = FormatDetailed(val),
                            isNull = val == null || (val is UnityEngine.Object obj && obj == null),
                            hasSerializeField = f.GetCustomAttribute<SerializeField>() != null,
                            isPublic = f.IsPublic
                        };
                    }
                    catch
                    {
                        return new { name = f.Name, type = f.FieldType.Name, fullType = f.FieldType.FullName, value = "(error)", isNull = true, hasSerializeField = false, isPublic = f.IsPublic };
                    }
                })
                .ToArray();

            return new
            {
                success = true,
                gameObject = go.name,
                component = componentType,
                fullTypeName = type.FullName,
                fieldCount = fields.Length,
                fields
            };
        }

        // ─── Fix 4: prefab_edit_asset / prefab_save_and_close ────────

        [UnitySkill("prefab_edit_asset",
            "Open a prefab in Prefab Stage for direct editing without scene instantiation. " +
            "Use component_add/component_set_property_ex on the returned root. " +
            "Call prefab_save_and_close when done.",
            TracksWorkflow = true)]
        public static object PrefabEditAsset(string prefabPath, bool autoSave = true)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return new { error = "prefabPath is required" };

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return new { error = $"Prefab not found: {prefabPath}" };

            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(prefabPath);
            if (prefabStage == null)
                return new { error = $"Failed to open prefab stage for: {prefabPath}" };

            var root = prefabStage.prefabContentsRoot;

            return new
            {
                success = true,
                prefabPath,
                rootName = root.name,
                rootInstanceId = root.GetInstanceID(),
                autoSave,
                childCount = root.transform.childCount,
                children = Enumerable.Range(0, root.transform.childCount)
                    .Select(i => root.transform.GetChild(i).name)
                    .ToArray(),
                hint = "Use component_add / component_set_property_ex with the rootName. Call prefab_save_and_close when done."
            };
        }

        [UnitySkill("prefab_save_and_close",
            "Save changes to the currently open prefab stage and return to the scene.",
            TracksWorkflow = true)]
        public static object PrefabSaveAndClose()
        {
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
                return new { error = "No prefab stage is currently open" };

            var prefabPath = prefabStage.assetPath;
            PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabPath);
            UnityEditor.SceneManagement.StageUtility.GoToMainStage();

            return new { success = true, savedTo = prefabPath };
        }

        // ─── Helpers ─────────────────────────────────────────────────

        static object ResolveAssetReference(Type targetType, string assetPath)
        {
            if (targetType == typeof(GameObject))
                return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (targetType == typeof(Sprite))
                return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                return AssetDatabase.LoadAssetAtPath(assetPath, targetType);
            return null;
        }

        static (PropertyInfo prop, FieldInfo field) FindMember(Type type, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop = type.GetProperty(memberName, flags);
            var field = type.GetField(memberName, flags);
            if (prop == null && field == null)
            {
                prop = type.GetProperties(flags).FirstOrDefault(p => p.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase));
                field = type.GetFields(flags).FirstOrDefault(f => f.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase));
            }
            return (prop, field);
        }

        static string FormatDetailed(object val)
        {
            if (val == null) return "null";
            if (val is UnityEngine.Object obj)
            {
                if (obj == null) return "null (missing)";
                var ap = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(ap)) return $"{obj.name} [{ap}]";
                if (obj is GameObject go) return $"{go.name} [scene:{GameObjectFinder.GetPath(go)}]";
                if (obj is Component c) return $"{c.gameObject.name}/{c.GetType().Name} [scene:{GameObjectFinder.GetPath(c.gameObject)}]";
                return obj.name;
            }
            if (val is Vector2 v2) return $"({v2.x}, {v2.y})";
            if (val is Vector3 v3) return $"({v3.x}, {v3.y}, {v3.z})";
            if (val is Color c2) return $"({c2.r}, {c2.g}, {c2.b}, {c2.a})";
            return val.ToString();
        }

        static readonly MethodInfo _convertValueMethod = typeof(ComponentSkills)
            .GetMethod("ConvertValue", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        static object InvokeConvertValue(string value, Type targetType)
        {
            if (_convertValueMethod != null)
                return _convertValueMethod.Invoke(null, new object[] { value, targetType });
            // Fallback for simple types
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value ?? "0");
            if (targetType == typeof(float)) return float.Parse(value ?? "0");
            if (targetType == typeof(bool)) return bool.Parse(value ?? "false");
            return Convert.ChangeType(value, targetType);
        }

        // ─── Force SkillRouter refresh on domain reload ──────────────

        [InitializeOnLoadMethod]
        static void ForceSkillRefreshOnDomainReload()
        {
            // Delay to ensure all assemblies are loaded before re-scanning
            EditorApplication.delayCall += () =>
            {
                SkillRouter.Refresh();
                Debug.Log($"[UnitySkillsExtensions] SkillRouter refreshed after domain reload");
            };
        }
    }
}
