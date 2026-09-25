using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace VRDeviceTraining.EditorTools
{
    /// <summary>
    /// 重建任务面板 UI（Legacy UnityEngine.UI.Text）。
    /// 全程只使用 Unity Editor API 操作当前打开的场景，不解析/不书写 .unity YAML。
    /// 只触碰 TrainingCanvas / TaskPanel 及其文字子对象，
    /// 重建后自动重新绑定 TrainingTaskPanelUI 的 5 个受管文本引用，
    /// 不修改 XR Origin、控制器、TrainingDevice、零件对象、TrainingManager、Input Actions。
    /// </summary>
    public static class TaskPanelRebuilder
    {
        private const string FontPath = "Assets/Fonts/NotoSansSC-Regular.otf";

        private const string CanvasName = "TrainingCanvas";
        private const string PanelName = "TaskPanel";

        // Rebuilder 自己创建、并负责自动重绑的文字对象（按名字精确管理）。
        // TaskPanel 下的其他子对象一律不删，否则会连带破坏
        // TrainingTaskPanelUI 上已有的 SerializedField 引用。
        private static readonly string[] ManagedTextNames =
            { "TaskTitle", "CurrentStepText", "Step1Text", "Step2Text", "Step3Text" };

        // 不属于 Rebuilder 管理、重建后必须仍然存在、且绑定必须保持原样的对象。
        // 不检查它们的文字与配置，只要求在重建中存活下来。
        private static readonly string[] PreservedNames = { "HintText", "ResetButton" };

        // TrainingTaskPanelUI 上与 ManagedTextNames 一一对应的 SerializedField 名。
        // 只写这 5 个字段，hintText / resetButton 保持原样。
        private static readonly string[] PanelUiTextProperties =
            { "taskTitleText", "currentStepText", "step1Text", "step2Text", "step3Text" };

        // 纯中文 + ASCII 数字 + 英文 + 标点，不含任何图标 / emoji / 特殊 Unicode
        //
        // 注意：下面这些文本只是 Editor 创建 UI 时的占位内容，方便在编辑态看到面板骨架。
        // 运行时的真正文案由 TrainingTaskPanelUI.Refresh() 从 TrainingManager 生成并覆盖，
        // 因此这里不写任何具体训练步骤名称（避免把业务步骤名固化到工具里）。
        private const string TextTaskTitle = "设备拆装培训";
        private const string TextCurrentStep = "当前步骤：待开始";
        private const string TextStep1 = "1. 待配置";
        private const string TextStep2 = "2. 待配置";
        private const string TextStep3 = "3. 待配置";

        [MenuItem("VRDeviceTraining/重建任务面板 UI", false, 100)]
        public static void Rebuild()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[任务面板] 当前没有已保存并加载的场景，无法重建。请先打开 TrainingScene。");
                return;
            }

            Debug.Log("[任务面板] ==== 开始重建任务面板 UI ====");

            GameObject canvasGO = FindSingleRoot(CanvasName);
            if (canvasGO == null)
            {
                Debug.LogError("[任务面板] 场景中找不到名为 \"" + CanvasName + "\" 的根对象，已中止，未做任何改动。");
                return;
            }

            // ---------- A. Canvas ----------
            var canvas = canvasGO.GetComponent<Canvas>();
            if (canvas == null) canvas = Undo.AddComponent<Canvas>(canvasGO);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = Undo.AddComponent<CanvasScaler>(canvasGO);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (canvasGO.GetComponent<GraphicRaycaster>() == null)
                Undo.AddComponent<GraphicRaycaster>(canvasGO);

            // ---------- B. 清空 TaskPanel 下的任务文字对象 ----------
            GameObject panelGO = FindChildExact(canvasGO.transform, PanelName);
            if (panelGO == null)
            {
                panelGO = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Undo.RegisterCreatedObjectUndo(panelGO, "Create TaskPanel");
                panelGO.transform.SetParent(canvasGO.transform, false);
            }

            // B-1 只删除 Rebuilder 自己管理的 5 个文字对象（按名字精确匹配，同名重复对象也一并清理）。
            // 不再删除 TaskPanel 的全部子对象：HintText / ResetButton 等由其它流程配置，
            // 删掉它们会让 TrainingTaskPanelUI 的 hintText / resetButton 引用失效，
            // 而这类引用无法用本工具自动恢复（它不负责创建这两个对象）。
            int removedChildren = 0;
            for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = panelGO.transform.GetChild(i);
                if (child == null) continue;
                if (!IsManagedName(child.name)) continue;

                removedChildren++;
                Undo.DestroyObjectImmediate(child.gameObject);
            }

            // B-2 删除名字前后带空格 / 名字不规范的对象（整棵 Canvas 子树）
            int removedBadNames = 0;
            var allDescendants = canvasGO.GetComponentsInChildren<Transform>(true);
            for (int i = allDescendants.Length - 1; i >= 0; i--)
            {
                var t = allDescendants[i];
                if (t == null) continue;
                string n = t.name;
                if (n != n.Trim() || n.Contains("  ") || n.StartsWith("'") || n.EndsWith("'"))
                {
                    removedBadNames++;
                    Undo.DestroyObjectImmediate(t.gameObject);
                }
            }

            // B-3 清除 Canvas 子树里所有 TextMeshPro 组件（按类型名判断，不硬依赖 TMPro 程序集）
            int removedTmp = RemoveTextMeshProComponents(canvasGO);

            Debug.Log("[任务面板] 预清理：删除子对象 " + removedChildren +
                      " 个，删除不规范命名对象 " + removedBadNames +
                      " 个，移除 TextMeshPro 组件 " + removedTmp + " 个。");

            // ---------- E. TaskPanel 布局 ----------
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(40f, -40f);
            panelRect.sizeDelta = new Vector2(460f, 250f);
            panelRect.localScale = Vector3.one;
            panelRect.localRotation = Quaternion.identity;
            panelGO.layer = LayerMask.NameToLayer("UI");

            // ---------- F. 背景 ----------
            var panelImage = panelGO.GetComponent<Image>();
            if (panelImage == null) panelImage = Undo.AddComponent<Image>(panelGO);
            panelImage.color = new Color(0.06f, 0.09f, 0.14f, 0.88f);
            panelImage.raycastTarget = false;

            // ---------- D. 字体 ----------
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError("[任务面板] 字体不存在：" + FontPath + " 。已中止重建（不会自动从其它路径复制字体）。");
                return;
            }
            Debug.Log("[任务面板] 使用字体：" + FontPath + " (fontName=" + font.name + ")");

            // ---------- C. 重新创建 5 个 Legacy Text ----------
            CreateText(panelGO.transform, "TaskTitle", TextTaskTitle, -18f, 42f, 34, FontStyle.Bold, Color.white);
            CreateText(panelGO.transform, "CurrentStepText", TextCurrentStep, -66f, 36f, 26, FontStyle.Bold, new Color(1f, 0.82f, 0.4f));
            CreateText(panelGO.transform, "Step1Text", TextStep1, -118f, 34f, 24, FontStyle.Normal, Color.white);
            CreateText(panelGO.transform, "Step2Text", TextStep2, -156f, 34f, 24, FontStyle.Normal, Color.white);
            CreateText(panelGO.transform, "Step3Text", TextStep3, -194f, 34f, 24, FontStyle.Normal, Color.white);

            // ---------- C-2. 自动重新绑定 TrainingTaskPanelUI 的文本引用 ----------
            // 上面销毁并重建了 5 个 Text，fileID 已变化，旧引用会失效。
            // 这里用 SerializedObject 把新 Text 写回组件，避免每次重建都要手工拖拽。
            // 只写 5 个受管字段，hintText / resetButton 保持原样不动。
            RebindTaskPanelUIReferences(canvasGO, panelGO);

            // ---------- G. 保存 ----------
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---------- H. 只读验证 ----------
            Verify(canvasGO, font);
        }

        private static GameObject CreateText(Transform parent, string name, string content,
                                             float posY, float height, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);   // 顶部 + 横向拉伸
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);       // 左上
            rect.anchoredPosition = new Vector2(20f, posY); // 左右 padding = 20
            rect.sizeDelta = new Vector2(-40f, height);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var text = go.GetComponent<Text>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.lineSpacing = 1f;

            // 明确不添加 LayoutGroup / ContentSizeFitter / LayoutElement
            return go;
        }

        private static int RemoveTextMeshProComponents(GameObject root)
        {
            int count = 0;
            var comps = root.GetComponentsInChildren<Component>(true);
            for (int i = comps.Length - 1; i >= 0; i--)
            {
                var c = comps[i];
                if (c == null) continue; // Missing script
                var t = c.GetType();
                string tn = t.Name;
                string ns = t.Namespace ?? "";
                if (tn.StartsWith("TextMeshPro") || ns == "TMPro")
                {
                    Undo.DestroyObjectImmediate(c);
                    count++;
                }
            }
            return count;
        }

        private static bool IsManagedName(string name)
        {
            for (int i = 0; i < ManagedTextNames.Length; i++)
                if (ManagedTextNames[i] == name) return true;
            return false;
        }

        /// <summary>
        /// 查找场景中的 TrainingTaskPanelUI。
        /// 该组件挂在 TrainingCanvas 上（不是 TaskPanel），
        /// 因此先在 Canvas 子树里找，找不到再退回全场景查找。
        /// </summary>
        private static TrainingTaskPanelUI FindTaskPanelUI(GameObject canvasGO)
        {
            if (canvasGO != null)
            {
                var inCanvas = canvasGO.GetComponentsInChildren<TrainingTaskPanelUI>(true);
                for (int i = 0; i < inCanvas.Length; i++)
                    if (inCanvas[i] != null) return inCanvas[i];
            }

            foreach (var ui in Resources.FindObjectsOfTypeAll<TrainingTaskPanelUI>())
                if (ui != null && ui.gameObject.scene.isLoaded) return ui;

            return null;
        }

        /// <summary>
        /// 把刚创建的 5 个 Text 重新绑定到 TrainingTaskPanelUI 上。
        /// 只写 PanelUiTextProperties 列出的 5 个字段，不碰 hintText / resetButton。
        /// </summary>
        private static void RebindTaskPanelUIReferences(GameObject canvasGO, GameObject panelGO)
        {
            var panelUI = FindTaskPanelUI(canvasGO);
            if (panelUI == null)
            {
                Debug.LogWarning("[任务面板] 场景中没有找到 TrainingTaskPanelUI，已跳过引用自动重绑。");
                return;
            }

            var so = new SerializedObject(panelUI);

            for (int i = 0; i < PanelUiTextProperties.Length; i++)
            {
                string propertyName = PanelUiTextProperties[i];
                string objectName = ManagedTextNames[i];

                GameObject target = FindChildExact(panelGO.transform, objectName);
                Text targetText = target != null ? target.GetComponent<Text>() : null;

                BindTextProperty(so, propertyName, targetText, objectName);
            }

            so.ApplyModifiedProperties();

            Debug.Log("[任务面板] 已自动重新绑定 TrainingTaskPanelUI 的 " + PanelUiTextProperties.Length +
                      " 个文本引用（hintText / resetButton 未改动）。");
        }

        private static bool BindTextProperty(SerializedObject so, string propertyName,
                                             Text value, string expectedObjectName)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogError("[任务面板] TrainingTaskPanelUI 上找不到字段：" + propertyName);
                return false;
            }

            if (value == null)
            {
                Debug.LogError("[任务面板] 找不到引用目标对象：" + expectedObjectName +
                               "（字段 " + propertyName + " 未修改）");
                return false;
            }

            prop.objectReferenceValue = value;
            return true;
        }

        private static GameObject FindSingleRoot(string name)
        {
            GameObject found = null;
            int hits = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (go.transform.parent != null) continue; // 只认根对象
                if (go.name == name) { found = go; hits++; }
            }
            if (hits > 1) Debug.LogWarning("[任务面板] 场景中有 " + hits + " 个根对象叫 " + name + "，已取第一个。");
            return found;
        }

        private static GameObject FindChildExact(Transform parent, string name)
        {
            foreach (Transform t in parent)
                if (t != null && t.name == name) return t.gameObject;
            return null;
        }

        private static void Verify(GameObject canvasGO, Font expectedFont)
        {
            Debug.Log("[任务面板] ==== 开始验证 ====");
            GameObject panelGO = FindChildExact(canvasGO.transform, PanelName);

            // 只验证结构与字体，不再校验文本具体内容：
            // 这些文本都是编辑期占位内容，运行时由 TrainingTaskPanelUI.Refresh()
            // 依据 TrainingManager 的步骤生成并覆盖。这里若校验文本，
            // 就会把具体训练步骤名重新写死回工具里（M25.4-3-1 要消除的正是这点）。
            string[] names = ManagedTextNames;

            bool ok = true;

            // 1 / 2 / 4 / 5
            for (int i = 0; i < names.Length; i++)
            {
                var go = FindChildExact(panelGO.transform, names[i]);
                if (go == null)
                {
                    Debug.LogError("[任务面板] 缺失对象：" + names[i]);
                    ok = false;
                    continue;
                }
                var txt = go.GetComponent<Text>();
                if (txt == null)
                {
                    Debug.LogError("[任务面板] " + names[i] + " 上没有 UnityEngine.UI.Text");
                    ok = false;
                    continue;
                }
                bool fontOk = txt.font == expectedFont;
                if (!fontOk)
                {
                    Debug.LogError("[任务面板] 字体不符：" + names[i] + " font=" + (txt.font != null ? txt.font.name : "null"));
                    ok = false;
                }
                Debug.Log("[任务面板] " + names[i] +
                          " | type=" + txt.GetType().FullName +
                          " | size=" + txt.fontSize +
                          " | style=" + txt.fontStyle +
                          " | text=[" + txt.text + "]" +
                          " | hex=" + ToHex(txt.text) +
                          " | font=" + (txt.font != null ? txt.font.name : "null") +
                          (fontOk ? " | OK" : " | FAIL"));
            }

            // 1b. 非 Rebuilder 管理的对象必须仍然保留。
            // 只校验存在性：它们不是本工具创建的，也不要求改动其文字或配置。
            for (int i = 0; i < PreservedNames.Length; i++)
            {
                var preserved = FindChildExact(panelGO.transform, PreservedNames[i]);
                if (preserved == null)
                {
                    Debug.LogError("[任务面板] 缺失对象：" + PreservedNames[i] + "（重建不应删除它）");
                    ok = false;
                    continue;
                }
                Debug.Log("[任务面板] " + PreservedNames[i] + " 已保留 | OK");
            }

            // 3. TMP 数量 = 0
            int tmpCount = 0;
            foreach (var c in canvasGO.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t.Name.StartsWith("TextMeshPro") || t.Namespace == "TMPro") tmpCount++;
            }
            Debug.Log("[任务面板] Canvas 子树内 TextMeshPro 组件数量 = " + tmpCount + (tmpCount == 0 ? " | OK" : " | FAIL"));
            if (tmpCount != 0) ok = false;

            // 6. 无重复对象 / 无带空格名字
            var descs = canvasGO.GetComponentsInChildren<Transform>(true);
            int duplicates = 0, badNames = 0;
            for (int i = 0; i < descs.Length; i++)
            {
                for (int j = i + 1; j < descs.Length; j++)
                    if (descs[i] != null && descs[j] != null && descs[i].parent == descs[j].parent && descs[i].name == descs[j].name)
                        duplicates++;
                if (descs[i] != null && descs[i].name != descs[i].name.Trim()) badNames++;
            }
            Debug.Log("[任务面板] 同级重复对象 = " + duplicates + "，带空格命名对象 = " + badNames +
                      ((duplicates == 0 && badNames == 0) ? " | OK" : " | FAIL"));
            if (duplicates != 0 || badNames != 0) ok = false;

            // 7 / 8 / 9. 未被脚本触碰的对象仍然存在
            // 不再按具体零件名逐个写死，只校验系统级对象。
            string[] untouched = { "TrainingManager", "XR Origin (XR Rig)", "TrainingDevice" };
            foreach (var n in untouched)
            {
                GameObject go = null;
                foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (g != null && g.name == n && (g.hideFlags & HideFlags.HideInHierarchy) == 0)
                    {
                        if (g.scene.isLoaded) { go = g; break; }
                    }
                Debug.Log("[任务面板] 未触碰对象 " + n + " : " + (go != null ? "存在，未被修改" : "未找到（请确认场景）"));
            }

            // 10. 通用零件存在性检查：按组件类型统计，不依赖任何具体零件名。
            int partCount = 0;
            foreach (var part in Resources.FindObjectsOfTypeAll<PartInteractable>())
            {
                if (part == null) continue;
                if (!part.gameObject.scene.isLoaded) continue;
                partCount++;
            }
            Debug.Log("[任务面板] 场景中 PartInteractable 零件数量 = " + partCount +
                      (partCount > 0 ? " | OK" : " | 未发现零件（请确认场景是否包含零件对象）"));

            // 11. TrainingTaskPanelUI 的 5 个文本引用必须指向上面那 5 个对象。
            // 只做结构性检查，不校验任何业务文本内容。
            var panelUI = FindTaskPanelUI(canvasGO);
            if (panelUI == null)
            {
                Debug.LogError("[任务面板] 未找到 TrainingTaskPanelUI，无法校验 UI 引用绑定。");
                ok = false;
            }
            else
            {
                var so = new SerializedObject(panelUI);

                for (int i = 0; i < PanelUiTextProperties.Length; i++)
                {
                    string propertyName = PanelUiTextProperties[i];
                    string objectName = ManagedTextNames[i];

                    var prop = so.FindProperty(propertyName);
                    if (prop == null)
                    {
                        Debug.LogError("[任务面板] TrainingTaskPanelUI 上找不到字段：" + propertyName);
                        ok = false;
                        continue;
                    }

                    GameObject expectedGO = FindChildExact(panelGO.transform, objectName);
                    Text expectedText = expectedGO != null ? expectedGO.GetComponent<Text>() : null;
                    if (expectedText == null)
                    {
                        Debug.LogError("[任务面板] 引用校验失败：找不到目标对象 " + objectName);
                        ok = false;
                        continue;
                    }

                    var actual = prop.objectReferenceValue as Text;
                    bool boundOk = actual == expectedText;
                    if (!boundOk)
                    {
                        Debug.LogError("[任务面板] 引用未绑定或指向错误：" + propertyName +
                                       " 实际=" + (actual != null ? actual.gameObject.name : "null") +
                                       " 期望=" + objectName);
                        ok = false;
                    }
                    Debug.Log("[任务面板] 引用 " + propertyName + " -> " +
                              (actual != null ? actual.gameObject.name : "null") +
                              (boundOk ? " | OK" : " | FAIL"));
                }
            }

            Debug.Log("[任务面板] ==== 验证结果：" + (ok ? "全部通过" : "存在问题，请查看上方 Error") + " ====");
        }

        private static string ToHex(string s)
        {
            if (s == null) return "null";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(((int)c).ToString("X4")).Append(' ');
            return sb.ToString().Trim();
        }
    }
}
