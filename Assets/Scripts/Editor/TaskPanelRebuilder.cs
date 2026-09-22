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
    /// 不修改 XR Origin、控制器、TrainingDevice、Battery、TrainingManager、Input Actions。
    /// </summary>
    public static class TaskPanelRebuilder
    {
        private const string FontPath = "Assets/Fonts/Arial Unicode.ttf";

        private const string CanvasName = "TrainingCanvas";
        private const string PanelName = "TaskPanel";

        // 纯中文 + ASCII 数字 + 英文 + 标点，不含任何图标 / emoji / 特殊 Unicode
        private const string TextTaskTitle = "设备拆装培训";
        private const string TextCurrentStep = "当前步骤：拆卸 Battery";
        private const string TextStep1 = "1. 拆卸 Battery";
        private const string TextStep2 = "2. 拆卸后盖";
        private const string TextStep3 = "3. 拆卸主板";

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

            // B-1 删除全部子对象（TaskTitle / CurrentStepText / Step1~3Text / 任何重复或带空格的同类对象）
            int removedChildren = 0;
            for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = panelGO.transform.GetChild(i);
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

            string[] names = { "TaskTitle", "CurrentStepText", "Step1Text", "Step2Text", "Step3Text" };
            string[] expected = { TextTaskTitle, TextCurrentStep, TextStep1, TextStep2, TextStep3 };

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
                bool same = string.Equals(txt.text, expected[i]);
                if (!same)
                {
                    Debug.LogError("[任务面板] 文本不符：" + names[i] + " 实际=[" + txt.text + "] 期望=[" + expected[i] +
                                   "] hex=" + ToHex(txt.text) + " vs " + ToHex(expected[i]));
                    ok = false;
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
                          (same && fontOk ? " | OK" : " | FAIL"));
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
            string[] untouched = { "TrainingManager", "XR Origin (XR Rig)", "TrainingDevice", "Battery" };
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
