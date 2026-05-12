using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
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

        [UnitySkill(
            "component_set_property_ex",
            "Enhanced set property with assetPath support for prefab/asset references. "
                + "Use assetPath to reference project assets (prefabs, sprites, materials).",
            TracksWorkflow = true
        )]
        public static object ComponentSetPropertyEx(
            string name = null,
            int instanceId = 0,
            string path = null,
            string componentType = null,
            string propertyName = null,
            string value = null,
            string referencePath = null,
            string referenceName = null,
            string assetPath = null
        )
        {
            if (string.IsNullOrEmpty(componentType) || string.IsNullOrEmpty(propertyName))
                return new { error = "componentType and propertyName are required" };

            var (go, findErr) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (findErr != null)
                return findErr;

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
                        return new
                        {
                            error = $"Could not load asset at '{assetPath}' as {targetType.Name}",
                        };
                }
                else if (
                    !string.IsNullOrEmpty(referencePath) || !string.IsNullOrEmpty(referenceName)
                )
                {
                    var targetGo = GameObjectFinder.Find(name: referenceName, path: referencePath);
                    if (targetGo == null)
                        return new
                        {
                            error = $"Could not resolve reference: path='{referencePath}', name='{referenceName}'",
                        };

                    if (targetType == typeof(Transform))
                        converted = targetGo.transform;
                    else if (targetType == typeof(GameObject))
                        converted = targetGo;
                    else if (typeof(Component).IsAssignableFrom(targetType))
                        converted = targetGo.GetComponent(targetType);
                    else
                        converted = null;

                    if (converted == null)
                        return new { error = $"Could not resolve reference for {propertyName}" };
                }
                else
                {
                    // ConvertValue is internal to UnitySkills assembly, use reflection
                    converted = InvokeConvertValue(value, targetType);
                }

                if (prop != null && prop.CanWrite)
                    prop.SetValue(comp, converted);
                else if (field != null)
                    field.SetValue(comp, converted);
                else
                    return new { error = $"Property {propertyName} is read-only" };

                EditorUtility.SetDirty(comp);
                return new
                {
                    success = true,
                    gameObject = go.name,
                    component = componentType,
                    property = propertyName,
                    valueSet = converted?.ToString() ?? "null",
                };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // ─── Fix 2: component_set_property_batch_ex ──────────────────

        [UnitySkill(
            "component_set_property_batch_ex",
            "Enhanced batch set properties. Accepts items as native JSON array (not string). "
                + "Supports assetPath for prefab/asset references.",
            TracksWorkflow = true
        )]
        public static object ComponentSetPropertyBatchEx(string items)
        {
            return BatchExecutor.Execute<BatchSetPropertyExItem>(
                items,
                item =>
                {
                    var result = ComponentSetPropertyEx(
                        name: item.name,
                        instanceId: item.instanceId,
                        path: item.path,
                        componentType: item.componentType,
                        propertyName: item.propertyName,
                        value: item.value,
                        referencePath: item.referencePath,
                        referenceName: item.referenceName,
                        assetPath: item.assetPath
                    );

                    // Check if result is an error
                    var resultType = result.GetType();
                    var errorProp =
                        resultType.GetProperty("error")
                        ?? resultType.GetField("error") as MemberInfo;
                    if (errorProp != null)
                    {
                        var errorVal = errorProp is PropertyInfo pi
                            ? pi.GetValue(result)
                            : ((FieldInfo)errorProp).GetValue(result);
                        throw new Exception(errorVal?.ToString() ?? "Unknown error");
                    }
                    return new
                    {
                        target = item.name ?? item.path,
                        success = true,
                        property = item.propertyName,
                    };
                },
                item => item.name ?? item.path
            );
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

        [UnitySkill(
            "component_get_serialized_fields",
            "Get all SerializeField values on a component, including private fields. "
                + "Shows reference targets with paths for UnityEngine.Object references."
        )]
        public static object ComponentGetSerializedFields(
            string name = null,
            int instanceId = 0,
            string path = null,
            string componentType = null
        )
        {
            if (string.IsNullOrEmpty(componentType))
                return new { error = "componentType is required" };

            var (go, findErr) = GameObjectFinder.FindOrError(name, instanceId, path);
            if (findErr != null)
                return findErr;

            var type = ComponentSkills.FindComponentType(componentType);
            if (type == null)
                return new { error = $"Component type not found: {componentType}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new { error = $"Component not found: {componentType}" };

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
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
                            isPublic = f.IsPublic,
                        };
                    }
                    catch
                    {
                        return new
                        {
                            name = f.Name,
                            type = f.FieldType.Name,
                            fullType = f.FieldType.FullName,
                            value = "(error)",
                            isNull = true,
                            hasSerializeField = false,
                            isPublic = f.IsPublic,
                        };
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
                fields,
            };
        }

        // ─── Fix 4: prefab_edit_asset / prefab_save_and_close ────────

        [UnitySkill(
            "prefab_edit_asset",
            "Open a prefab in Prefab Stage for direct editing without scene instantiation. "
                + "Use component_add/component_set_property_ex on the returned root. "
                + "Call prefab_save_and_close when done.",
            TracksWorkflow = true
        )]
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
                children = Enumerable
                    .Range(0, root.transform.childCount)
                    .Select(i => root.transform.GetChild(i).name)
                    .ToArray(),
                hint = "Use component_add / component_set_property_ex with the rootName. Call prefab_save_and_close when done.",
            };
        }

        [UnitySkill(
            "prefab_save_and_close",
            "Save changes to the currently open prefab stage and return to the scene.",
            TracksWorkflow = true
        )]
        public static object PrefabSaveAndClose()
        {
            var prefabStage =
                UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
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
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop = type.GetProperty(memberName, flags);
            var field = type.GetField(memberName, flags);
            if (prop == null && field == null)
            {
                prop = type.GetProperties(flags)
                    .FirstOrDefault(p =>
                        p.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase)
                    );
                field = type.GetFields(flags)
                    .FirstOrDefault(f =>
                        f.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase)
                    );
            }
            return (prop, field);
        }

        static string FormatDetailed(object val)
        {
            if (val == null)
                return "null";
            if (val is UnityEngine.Object obj)
            {
                if (obj == null)
                    return "null (missing)";
                var ap = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(ap))
                    return $"{obj.name} [{ap}]";
                if (obj is GameObject go)
                    return $"{go.name} [scene:{GameObjectFinder.GetPath(go)}]";
                if (obj is Component c)
                    return $"{c.gameObject.name}/{c.GetType().Name} [scene:{GameObjectFinder.GetPath(c.gameObject)}]";
                return obj.name;
            }
            if (val is Vector2 v2)
                return $"({v2.x}, {v2.y})";
            if (val is Vector3 v3)
                return $"({v3.x}, {v3.y}, {v3.z})";
            if (val is Color c2)
                return $"({c2.r}, {c2.g}, {c2.b}, {c2.a})";
            return val.ToString();
        }

        static readonly MethodInfo _convertValueMethod = typeof(ComponentSkills).GetMethod(
            "ConvertValue",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
        );

        static object InvokeConvertValue(string value, Type targetType)
        {
            if (_convertValueMethod != null)
                return _convertValueMethod.Invoke(null, new object[] { value, targetType });
            // Fallback for simple types
            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(int))
                return int.Parse(value ?? "0");
            if (targetType == typeof(float))
                return float.Parse(value ?? "0");
            if (targetType == typeof(bool))
                return bool.Parse(value ?? "false");
            return Convert.ChangeType(value, targetType);
        }

        // ─── scene_load_and_play ─────────────────────────────────────

        [UnitySkill(
            "scene_load_and_play",
            "Open a scene by path and immediately enter play mode in one call. "
                + "Returns once play mode is entered. "
                + "Call scene_screenshot after sleeping for the expected transition time."
        )]
        public static object SceneLoadAndPlay(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return new { error = "scenePath is required" };

            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            if (!scene.IsValid())
                return new { error = $"Failed to open scene: {scenePath}" };

            EditorApplication.isPlaying = true;

            return new
            {
                success = true,
                scenePath,
                sceneName = scene.name,
                hint = "Play mode entered. Sleep for expected transition time, then call scene_screenshot.",
            };
        }

        // ─── Addressables setup + build ───────────────────────────────

        [UnitySkill(
            "setup_addressables",
            "Initialize AddressableAssetSettings (creates if missing), set Play Mode Script to "
                + "'Use Asset Database' so Editor play mode works without a content build, "
                + "and create a default Built-In group. Run this once before build_addressables."
        )]
        public static object SetupAddressables()
        {
#if UNITY_EDITOR
            try
            {
                // Use AddressableAssetSettingsDefaultObject to get/create settings
                var defaultObjType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor"
                );
                if (defaultObjType == null)
                    return new { error = "Addressables package not found in project" };

                var settingsProp = defaultObjType.GetProperty(
                    "Settings",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (settingsProp == null)
                    return new
                    {
                        error = "AddressableAssetSettingsDefaultObject.Settings not found",
                    };

                // Get existing or null — we'll create via GetSettings(true) on the settings type
                var settings = settingsProp.GetValue(null);

                if (settings == null)
                {
                    // Create new settings via AddressableAssetSettings.Create
                    var settingsType = Type.GetType(
                        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor"
                    );
                    if (settingsType == null)
                        return new { error = "AddressableAssetSettings type not found" };

                    var createMethod = settingsType.GetMethod(
                        "Create",
                        BindingFlags.Public | BindingFlags.Static
                    );
                    if (createMethod == null)
                    {
                        // Try GetSettings(true) as fallback
                        var getSettingsMethod = settingsType.GetMethod(
                            "GetSettings",
                            BindingFlags.Public | BindingFlags.Static
                        );
                        if (getSettingsMethod != null)
                            settings = getSettingsMethod.Invoke(null, new object[] { true });
                    }
                    else
                    {
                        settings = createMethod.Invoke(
                            null,
                            new object[]
                            {
                                "Assets/AddressableAssetsData",
                                "AddressableAssetSettings",
                                true,
                                true,
                            }
                        );
                    }
                }

                if (settings == null)
                    return new { error = "Failed to get or create AddressableAssetSettings" };

                var settingsObjType = settings.GetType();

                // Set Play Mode Script to index 0 = "Use Asset Database (fastest)"
                var playModeIndexProp = settingsObjType.GetProperty(
                    "ActivePlayModeDataBuilderIndex"
                );
                if (playModeIndexProp != null)
                    playModeIndexProp.SetValue(settings, 0);

                // Disable remote catalog
                var buildRemoteCatalogProp = settingsObjType.GetProperty("BuildRemoteCatalog");
                if (buildRemoteCatalogProp != null)
                    buildRemoteCatalogProp.SetValue(settings, false);

                UnityEditor.EditorUtility.SetDirty(settings as UnityEngine.Object);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();

                Debug.Log(
                    "[UnitySkillsExtensions] AddressableAssetSettings ready. "
                        + "Play Mode = 'Use Asset Database'. BuildRemoteCatalog = false."
                );
                return new
                {
                    success = true,
                    message = "Addressables settings ready. Play Mode Script = 'Use Asset Database' "
                        + "(Editor play mode no longer requires a content build). "
                        + "BuildRemoteCatalog = false.",
                };
            }
            catch (Exception e)
            {
                return new { error = e.InnerException?.Message ?? e.Message };
            }
#else
            return new { error = "Editor-only skill" };
#endif
        }

        [UnitySkill(
            "build_addressables",
            "Build Addressables player content (equivalent to Build → New Build → Default Build Script). "
                + "Run setup_addressables first if settings do not exist yet."
        )]
        public static object BuildAddressables()
        {
#if UNITY_EDITOR
            try
            {
                var settingsType = Type.GetType(
                    "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor"
                );
                if (settingsType == null)
                    return new { error = "Addressables package not found in project" };

                // Ensure settings exist — try DefaultObject first, fallback to direct asset load
                var defaultObjType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor"
                );
                var settingsProp = defaultObjType?.GetProperty(
                    "Settings",
                    BindingFlags.Public | BindingFlags.Static
                );
                var settings = settingsProp?.GetValue(null);

                // Fallback: load directly from known path
                if (settings == null)
                {
                    settings = UnityEditor.AssetDatabase.LoadAssetAtPath(
                        "Assets/AddressableAssetsData/AddressableAssetSettings.asset",
                        settingsType
                    );
                }

                if (settings == null)
                    return new
                    {
                        error = "AddressableAssetSettings not found. Run setup_addressables first.",
                    };

                // Register as DefaultObject so BuildPlayerContent() can find it internally.
                // Without this, BuildPlayerContent() queries DefaultObject.Settings and gets null.
                var settingsSetProp = defaultObjType?.GetProperty(
                    "Settings",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (settingsSetProp != null && settingsSetProp.CanWrite)
                {
                    settingsSetProp.SetValue(null, settings);
                    UnityEditor.AssetDatabase.SaveAssets();
                }

                // Find zero-parameter BuildPlayerContent overload
                MethodInfo buildMethod = null;
                foreach (
                    var m in settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                )
                {
                    if (m.Name == "BuildPlayerContent" && m.GetParameters().Length == 0)
                    {
                        buildMethod = m;
                        break;
                    }
                }
                if (buildMethod == null)
                    foreach (
                        var m in settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    )
                    {
                        if (m.Name == "BuildPlayerContent")
                        {
                            buildMethod = m;
                            break;
                        }
                    }

                if (buildMethod == null)
                    return new { error = "BuildPlayerContent method not found" };

                var parameters = buildMethod.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    buildMethod.Invoke(null, new object[] { true });
                else
                    buildMethod.Invoke(null, null);

                Debug.Log("[UnitySkillsExtensions] Addressables build complete");
                return new
                {
                    success = true,
                    message = "Addressables player content built successfully",
                };
            }
            catch (Exception e)
            {
                return new { error = e.InnerException?.Message ?? e.Message };
            }
#else
            return new { error = "Editor-only skill" };
#endif
        }

        // ─── build_android ────────────────────────────────────────────

        [UnitySkill(
            "build_android",
            "Build a development Android APK. Outputs to Builds/Android/pokeno-mania.apk. "
                + "Takes several minutes — do not call other skills while this runs."
        )]
        public static object BuildAndroid(string outputPath = null)
        {
#if UNITY_EDITOR
            try
            {
                string outDir = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "Builds", "Android")
                );
                Directory.CreateDirectory(outDir);
                string apkPath = outputPath ?? Path.Combine(outDir, "pokeno-mania.apk");

                PlayerSettings.SetApplicationIdentifier(
                    UnityEditor.Build.NamedBuildTarget.Android,
                    "com.pokenomania.pokeno"
                );
                EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

                var options = new BuildPlayerOptions
                {
                    scenes = new[]
                    {
                        "Assets/Scenes/Boot.unity",
                        "Assets/Scenes/Menu.unity",
                        "Assets/Scenes/Lobby.unity",
                        "Assets/Scenes/Game.unity",
                    },
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };

                Debug.Log($"[BuildAndroid] Starting build → {apkPath}");
                var report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result == BuildResult.Succeeded)
                {
                    return new
                    {
                        success = true,
                        apkPath,
                        totalSeconds = (int)report.summary.totalTime.TotalSeconds,
                        totalSizeBytes = report.summary.totalSize,
                    };
                }

                var errors = report
                    .steps.SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content)
                    .Take(10)
                    .ToArray();

                return new { error = $"Build failed: {report.summary.result}", errors };
            }
            catch (Exception e)
            {
                return new { error = e.InnerException?.Message ?? e.Message };
            }
#else
            return new { error = "Editor-only skill" };
#endif
        }

        // ─── set_screen_orientation ──────────────────────────────────

        [UnitySkill(
            "set_screen_orientation",
            "Lock screen orientation. orientation: Portrait | PortraitUpsideDown | LandscapeLeft | LandscapeRight | AutoRotation"
        )]
        public static object SetScreenOrientation(string orientation = "Portrait")
        {
            if (!System.Enum.TryParse<UIOrientation>(orientation, out var parsed))
                return new
                {
                    error = $"Unknown orientation: {orientation}. Valid: Portrait, PortraitUpsideDown, LandscapeLeft, LandscapeRight, AutoRotation",
                };

            PlayerSettings.defaultInterfaceOrientation = parsed;
            PlayerSettings.allowedAutorotateToPortrait = parsed == UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            UnityEditor.AssetDatabase.SaveAssets();

            return new { success = true, orientation = parsed.ToString() };
        }

        // ─── Force SkillRouter refresh + auto-start server on every load ─

        [InitializeOnLoadMethod]
        static void ForceSkillRefreshOnDomainReload()
        {
            EditorApplication.delayCall += () =>
            {
                SkillRouter.Refresh();
                if (!SkillsHttpServer.IsRunning)
                {
                    SkillsHttpServer.Start(8090, fallbackToAuto: true);
                    Debug.Log(
                        "[UnitySkillsExtensions] Unity Skills server auto-started on port 8090"
                    );
                }
            };
        }

        // ─── script_create_ex ────────────────────────────────────────

        [UnitySkill(
            "script_create_ex",
            "Create a C# script with explicit raw content. "
                + "Use instead of script_create when you need to write full file content. "
                + "folder defaults to Assets/Scripts. scriptName must not include path separators.",
            TracksWorkflow = true
        )]
        public static object ScriptCreateEx(
            string scriptName = null,
            string content = null,
            string folder = "Assets/Scripts"
        )
        {
            if (string.IsNullOrEmpty(scriptName))
                return new { error = "scriptName is required" };
            if (string.IsNullOrEmpty(content))
                return new { error = "content is required" };
            if (scriptName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return new
                {
                    error = "scriptName must not contain path separators; use folder param for directory",
                };

            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, scriptName + ".cs");
                File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(path);
                AssetDatabase.Refresh();
                return new { success = true, path };
            }
            catch (Exception ex)
            {
                return new { error = ex.Message };
            }
        }

        // ─── gameobject_reparent_ex ───────────────────────────────────

        [UnitySkill(
            "gameobject_reparent_ex",
            "Reparent a GameObject. Works on prefab-instance children where gameobject_set_parent silently fails. "
                + "Skips WorkflowManager snapshot (root cause of the prefab-instance reparent bug). "
                + "Accepts child/parent by name, instanceId, or path.",
            TracksWorkflow = true
        )]
        public static object GameObjectReparentEx(
            string childName = null,
            int childInstanceId = 0,
            string childPath = null,
            string parentName = null,
            int parentInstanceId = 0,
            string parentPath = null
        )
        {
            var (child, childError) = GameObjectFinder.FindOrError(
                childName,
                childInstanceId,
                childPath
            );
            if (childError != null)
                return childError;

            Transform parent = null;
            if (
                !string.IsNullOrEmpty(parentName)
                || parentInstanceId != 0
                || !string.IsNullOrEmpty(parentPath)
            )
            {
                var (parentGo, parentError) = GameObjectFinder.FindOrError(
                    parentName,
                    parentInstanceId,
                    parentPath
                );
                if (parentError != null)
                    return parentError;
                parent = parentGo.transform;
            }

            Undo.SetTransformParent(child.transform, parent, "Reparent Ex");

            if (PrefabUtility.IsPartOfPrefabInstance(child))
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.transform);

            EditorUtility.SetDirty(child);

            return new
            {
                success = true,
                child = child.name,
                parent = parent?.name ?? "(root)",
                newPath = GameObjectFinder.GetPath(child),
            };
        }
    }
}
