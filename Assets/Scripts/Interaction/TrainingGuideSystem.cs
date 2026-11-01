using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 培训引导系统：高亮当前目标零件 + 3D 箭头指引（对标开源项目 GuideStep）。
///
/// 设计要点：
/// - 零外部资源：高亮材质与箭头全部在运行时用代码生成，不依赖任何预制体/贴图；
/// - 不改现有框架：通过 PartInteractable.Status == Available 判断"当前目标零件"，
///   不需要修改 TrainingManager / PartInteractable 的任何逻辑；
/// - 安全恢复：高亮时克隆零件材质并开启 Emission，结束时恢复原材质，不污染资源；
/// - 自动跟随：零件被用户抓走 / 放下时，箭头每帧跟随目标位置并指向目标中心。
///
/// 使用方式：
/// 1. 场景新建空物体（或挂到任意管理物体上）并挂本脚本；
/// 2. Inspector 拖入 trainingManager；
/// 3. 进入 Play 后自动生效：当前步骤的目标零件发光 + 箭头指引。
/// </summary>
public class TrainingGuideSystem : MonoBehaviour
{
    [Header("数据来源")]
    [SerializeField] private TrainingManager trainingManager;

    [Header("功能开关")]
    [SerializeField] private bool enableHighlight = true;
    [SerializeField] private bool enableArrow = true;

    [Header("高亮样式")]
    [Tooltip("目标零件的高亮发光颜色。")]
    [SerializeField] private Color highlightColor = new Color(0f, 0.9f, 1f, 1f);

    [Header("箭头样式")]
    [Tooltip("箭头悬停在目标上方的距离。")]
    [SerializeField] private float arrowDistance = 0.35f;
    [Tooltip("箭头浮动动画速度。")]
    [SerializeField] private float arrowFloatSpeed = 2f;
    [Tooltip("箭头浮动高度幅度。")]
    [SerializeField] private float arrowFloatHeight = 0.08f;
    [Tooltip("箭头指向目标中心时的抬升高度（指向点）。")]
    [SerializeField] private float arrowAimHeight = 0.3f;

    // ===== 运行时状态 =====
    private PartInteractable[] allParts;
    private PartInteractable currentTarget;
    private readonly Dictionary<Renderer, Material> originalMaterials = new Dictionary<Renderer, Material>();
    private readonly List<Renderer> highlightedRenderers = new List<Renderer>();
    private Material highlightMaterial;
    private GameObject arrowRoot;
    private int lastStepIndex = -1;
    private bool lastCompleted;
    private float arrowBaseY;

    private void Start()
    {
        if (trainingManager == null)
        {
            Debug.LogWarning("[引导系统] 未绑定 trainingManager，引导功能已禁用。", this);
            enabled = false;
            return;
        }

        allParts = FindObjectsOfType<PartInteractable>(true);

        if (enableHighlight)
            highlightMaterial = CreateHighlightMaterial();

        if (enableArrow)
            CreateArrow();

        // 订阅重置事件：重置后立即刷新引导目标
        trainingManager.OnTrainingReset -= HandleTrainingReset;
        trainingManager.OnTrainingReset += HandleTrainingReset;

        RefreshTarget(true);
    }

    private void OnDestroy()
    {
        if (trainingManager != null)
            trainingManager.OnTrainingReset -= HandleTrainingReset;
    }

    private void HandleTrainingReset()
    {
        lastStepIndex = -1;
        lastCompleted = false;
        RefreshTarget(true);
    }

    private void Update()
    {
        if (trainingManager == null)
            return;

        bool completed = trainingManager.IsCompleted;
        int stepIndex = trainingManager.CurrentStepIndex;

        // 步骤或完成状态变化时才刷新目标
        if (stepIndex != lastStepIndex || completed != lastCompleted)
        {
            lastStepIndex = stepIndex;
            lastCompleted = completed;
            RefreshTarget(false);
        }

        // 箭头每帧跟随目标位置（零件被抓走/放下时持续跟随）
        UpdateArrow();
    }

    /// <summary>
    /// 刷新当前引导目标：找到 Status == Available 的零件并高亮，其他零件取消高亮。
    /// </summary>
    private void RefreshTarget(bool force)
    {
        PartInteractable newTarget = FindCurrentTarget();

        if (newTarget != currentTarget || force)
        {
            ClearHighlight();

            currentTarget = newTarget;

            if (currentTarget != null)
            {
                if (enableHighlight)
                    ApplyHighlight(currentTarget);

                Debug.Log($"【引导】当前目标：{currentTarget.gameObject.name}（步骤 {trainingManager.CurrentStepIndex}）");
            }
            else
            {
                HideArrow();
            }
        }

        if (currentTarget == null)
            HideArrow();
    }

    /// <summary>在场景中寻找当前可操作的零件（Status == Available）。</summary>
    private PartInteractable FindCurrentTarget()
    {
        if (allParts == null || trainingManager.IsCompleted)
            return null;

        for (int i = 0; i < allParts.Length; i++)
        {
            PartInteractable part = allParts[i];
            if (part == null)
                continue;

            if (part.Status == PartStatus.Available)
                return part;
        }

        return null;
    }

    // ==================== 高亮 ====================

    /// <summary>创建共享高亮材质（Standard + Emission）。</summary>
    private static Material CreateHighlightMaterial()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
            return null;

        Material mat = new Material(standard);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 1f, 1f) * 1.6f);
        mat.SetColor("_Color", new Color(0.7f, 0.97f, 1f, 1f));
        return mat;
    }

    /// <summary>把目标零件所有 Renderer 替换为高亮材质（保存原始材质以便恢复）。</summary>
    private void ApplyHighlight(PartInteractable part)
    {
        if (part == null || highlightMaterial == null)
            return;

        Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            // 首次遇到该 Renderer 时保存原材质
            if (!originalMaterials.ContainsKey(r))
                originalMaterials[r] = r.sharedMaterial;

            r.material = highlightMaterial;
            highlightedRenderers.Add(r);
        }
    }

    /// <summary>恢复所有高亮零件的原始材质并清空记录。</summary>
    private void ClearHighlight()
    {
        for (int i = 0; i < highlightedRenderers.Count; i++)
        {
            Renderer r = highlightedRenderers[i];
            if (r == null)
                continue;

            Material original;
            if (originalMaterials.TryGetValue(r, out original))
                r.sharedMaterial = original;
        }

        highlightedRenderers.Clear();
        originalMaterials.Clear();
    }

    // ==================== 箭头 ====================

    /// <summary>运行时生成 3D 箭头（圆柱杆 + 圆锥头），初始隐藏。</summary>
    private void CreateArrow()
    {
        arrowRoot = new GameObject("TrainingGuideArrow");
        arrowRoot.transform.SetParent(transform, false);

        // 杆：圆柱
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "ArrowShaft";
        shaft.transform.SetParent(arrowRoot.transform, false);
        shaft.transform.localScale = new Vector3(0.025f, 0.09f, 0.025f);
        shaft.transform.localPosition = Vector3.zero;

        // 头：圆锥（Unity 无 Cone 原语，用代码生成圆锥 Mesh）
        GameObject head = new GameObject("ArrowHead");
        head.transform.SetParent(arrowRoot.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        head.transform.localScale = new Vector3(0.09f, 0.13f, 0.09f);

        MeshFilter headFilter = head.AddComponent<MeshFilter>();
        headFilter.sharedMesh = CreateConeMesh(1f, 2f, 24);
        head.AddComponent<MeshRenderer>();

        // 统一颜色（发光）
        Shader standard = Shader.Find("Standard");
        if (standard != null)
        {
            Material mat = new Material(standard);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.1f, 1f, 0.3f, 1f) * 1.5f);
            mat.SetColor("_Color", new Color(0.6f, 1f, 0.7f, 1f));

            Renderer[] renderers = arrowRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = mat;
        }

        arrowRoot.SetActive(false);
    }

    /// <summary>每帧更新箭头：位置跟随目标、浮动动画、朝向目标中心。</summary>
    private void UpdateArrow()
    {
        if (arrowRoot == null)
            return;

        if (currentTarget == null || trainingManager.IsCompleted)
        {
            HideArrow();
            return;
        }

        if (!arrowRoot.activeSelf)
            arrowRoot.SetActive(true);

        Vector3 targetPos = currentTarget.transform.position;
        Vector3 aimPoint = targetPos + Vector3.up * arrowAimHeight;

        // 浮动动画：在悬停高度上做正弦上下
        float floatOffset = Mathf.Sin(Time.time * arrowFloatSpeed) * arrowFloatHeight;
        arrowRoot.transform.position = targetPos + Vector3.up * (arrowDistance + floatOffset);

        // 指向目标中心上方
        arrowRoot.transform.LookAt(aimPoint, Vector3.up);
    }

    private void HideArrow()
    {
        if (arrowRoot != null && arrowRoot.activeSelf)
            arrowRoot.SetActive(false);
    }

    /// <summary>
    /// 运行时生成圆锥 Mesh（Unity 的 PrimitiveType 没有 Cone，需手动构建）。
    /// </summary>
    /// <param name="radius">底面半径</param>
    /// <param name="height">圆锥高度</param>
    /// <param name="segments">底面分段数（越多越圆）</param>
    private static Mesh CreateConeMesh(float radius, float height, int segments)
    {
        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[segments + 2];
        Vector3[] normals = new Vector3[segments + 2];
        int[] triangles = new int[segments * 6];

        // 顶点 0：锥尖；顶点 1：底面圆心；其余：底面圆周
        vertices[0] = new Vector3(0f, height * 0.5f, 0f);
        vertices[1] = new Vector3(0f, -height * 0.5f, 0f);

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[i + 2] = new Vector3(x, -height * 0.5f, z);

            // 侧面法线：指向外侧（简化处理，指向径向）
            Vector3 radial = new Vector3(x, 0f, z).normalized;
            normals[i + 2] = radial;
        }
        normals[0] = Vector3.up;
        normals[1] = Vector3.down;

        // 侧面三角（锥尖 → 圆周两两成面）
        int tri = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            triangles[tri++] = 0;
            triangles[tri++] = i + 2;
            triangles[tri++] = next + 2;
        }

        // 底面三角（底面圆心 → 圆周，法线朝下）
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            triangles[tri++] = 1;
            triangles[tri++] = next + 2;
            triangles[tri++] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
