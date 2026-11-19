using UnityEditor;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>
    /// 브러시 모드의 창 패널: 프리셋(불러오기·덮어쓰기·새로 저장), 브러시 구성(섞을 장식과 비율), 브러시 설정.
    /// 장식은 아래 팔레트에서 타일을 클릭해 담고 뺀다. 씬뷰 조작(뿌리기·지우기)은 MapToolWindow.BrushScene.
    /// </summary>
    public sealed partial class MapToolWindow
    {
        private const float BrushThumbSize = 20f;
        private const float BrushNameWidth = 110f;

        private static readonly GUIContent[] EraseTargetLabels =
        {
            new("구성에 담은 종류만", "브러시 구성에 담은 장식만 지운다"),
            new("모든 장식", "종류를 가리지 않고 장식을 지운다"),
        };

        private static readonly GUIContent[] BrushModeLabels =
        {
            new("그리기", "드래그로 장식을 뿌린다. Shift를 누르는 동안은 지우개 (E로 전환)"),
            new("지우개", "드래그로 장식을 지운다. Shift를 누르는 동안은 그리기 (E로 전환)"),
        };

        [SerializeField] private ScatterBrushState brush = new();
        [SerializeField] private ScatterBrushPreset brushPreset;
        [SerializeField] private Vector2 brushListScroll;
        [SerializeField] private bool brushVariationFoldout = true;
        [SerializeField] private bool brushFilterFoldout;
        [SerializeField] private bool scattersHidden;

        private GUIStyle brushMissingStyle;

        private void DrawBrushPanel()
        {
            DrawBrushPresetRow();
            EditorGUILayout.Space(6);
            DrawBrushComposition();
            EditorGUILayout.Space(6);
            DrawBrushSettings();
            EditorGUILayout.LabelField(brush.EraseMode
                    ? "드래그 = 지우기 · Shift+드래그 = 뿌리기 · E = 전환 · [ ] = 반경"
                    : "드래그 = 뿌리기 · Shift+드래그 = 지우기 · E = 전환 · [ ] = 반경 · Ctrl+휠 = 밀도",
                EditorStyles.miniLabel);
            DrawScatterSceneRow();
            EditorGUILayout.Space(6);
        }

        /// <summary>맵의 장식 개수와 씬뷰 숨기기 토글(에디터 전용, 씬 파일·빌드에 영향 없음).</summary>
        private void DrawScatterSceneRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int count = service?.Map.ScatterCount ?? 0;
                EditorGUILayout.LabelField($"맵의 장식 {count:N0}개", GUILayout.Width(140f));
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool hide = GUILayout.Toggle(scattersHidden,
                    new GUIContent("씬에서 장식 숨기기", "씬뷰에서만 숨깁니다. 씬 파일과 빌드에는 영향이 없습니다."),
                    EditorStyles.miniButton, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck()) SetScattersHidden(hide);
            }
        }

        // ───────────────────────── 프리셋 ─────────────────────────

        private void DrawBrushPresetRow()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (ScatterBrushPreset)EditorGUILayout.ObjectField(
                new GUIContent("프리셋", "고르면 그 프리셋의 설정과 구성을 바로 불러온다"), brushPreset, typeof(ScatterBrushPreset), false);
            if (EditorGUI.EndChangeCheck())
            {
                brushPreset = picked;
                if (picked != null) brush.Load(picked);
                SceneView.RepaintAll();
            }

            bool modified = brushPreset != null && !brush.Matches(brushPreset);
            using (new EditorGUILayout.HorizontalScope())
            {
                string state = brushPreset == null ? "프리셋 없음" : modified ? "수정됨" : "프리셋과 같음";
                GUILayout.Label(state, modified ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!modified))
                {
                    if (GUILayout.Button(new GUIContent("다시 불러오기", "고친 값을 버리고 프리셋 값으로 돌아간다"), EditorStyles.miniButtonLeft))
                        brush.Load(brushPreset);
                    if (GUILayout.Button(new GUIContent("덮어쓰기", "지금 값을 이 프리셋에 저장한다"), EditorStyles.miniButtonMid))
                        OverwriteBrushPreset();
                }
                if (GUILayout.Button(new GUIContent("새 프리셋으로 저장...", "지금 값을 새 프리셋 에셋으로 저장한다"),
                        brushPreset == null ? EditorStyles.miniButton : EditorStyles.miniButtonRight))
                    SaveNewBrushPreset();
            }
        }

        private void OverwriteBrushPreset()
        {
            Undo.RecordObject(brushPreset, "브러시 프리셋 덮어쓰기");
            brush.SaveTo(brushPreset);
            EditorUtility.SetDirty(brushPreset);
            AssetDatabase.SaveAssetIfDirty(brushPreset);
            status = $"브러시 프리셋 '{brushPreset.name}'에 저장했습니다.";
        }

        private void SaveNewBrushPreset()
        {
            var near = brushPreset != null ? (Object)brushPreset : map;
            string folder = near != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(near)) : "Assets";
            string path = EditorUtility.SaveFilePanelInProject(
                "새 브러시 프리셋", "ScatterBrushPreset", "asset", "브러시 프리셋을 저장할 위치를 고르세요.", folder);
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<ScatterBrushPreset>();
            brush.SaveTo(asset);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            brushPreset = asset;
            EditorGUIUtility.PingObject(asset);
            status = $"브러시 프리셋 '{asset.name}'을 만들었습니다.";
        }

        // ───────────────────────── 구성 ─────────────────────────

        private void DrawBrushComposition()
        {
            var items = brush.Items;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"브러시 구성 ({items.Count})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(items.Count == 0))
                {
                    if (GUILayout.Button("모두 빼기", EditorStyles.miniButton)) brush.Clear();
                }
            }

            if (items.Count == 0)
            {
                EditorGUILayout.HelpBox("아래 팔레트에서 장식을 클릭해 담으세요. 여러 개를 담으면 비율대로 섞어 뿌립니다.", MessageType.Info);
                return;
            }

            int remove = -1;
            brushListScroll = EditorGUILayout.BeginScrollView(brushListScroll, GUILayout.MaxHeight(150f));
            for (int i = 0; i < items.Count; i++)
                if (DrawBrushItemRow(i)) remove = i;
            EditorGUILayout.EndScrollView();
            if (remove >= 0) brush.RemoveAt(remove);

            if (!brush.HasUsableEntries)
                EditorGUILayout.HelpBox("뿌릴 수 있는 장식이 없습니다. 비율을 0보다 크게 하거나 장식을 담으세요.", MessageType.Warning);
        }

        /// <returns>✕를 눌렀으면 true.</returns>
        private bool DrawBrushItemRow(int index)
        {
            var item = brush.Items[index];
            var definition = item.Definition;
            bool broken = definition == null || !definition.IsScatter;

            using (new EditorGUILayout.HorizontalScope())
            {
                Texture thumb = null;
                if (definition != null)
                {
                    if (definition.Prefab != null) thumb = AssetPreview.GetAssetPreview(definition.Prefab);
                    if (thumb == null) thumb = AssetPreview.GetMiniThumbnail(definition);
                }
                GUILayout.Label(thumb, GUILayout.Width(BrushThumbSize), GUILayout.Height(BrushThumbSize));

                string name = definition == null ? "(지워진 정의)" : definition.name;
                string tooltip = definition == null ? "정의 에셋이 지워졌습니다. ✕로 빼세요."
                    : !definition.IsScatter ? "장식이 아니라서 뿌리지 않습니다. 정의의 종류를 '장식'으로 바꾸거나 빼세요."
                    : $"{definition.Id} · 클릭하면 프로젝트 창에서 보여 줌";
                brushMissingStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.45f, 0.4f) } };
                if (GUILayout.Button(new GUIContent(name, tooltip), broken ? brushMissingStyle : EditorStyles.label,
                        GUILayout.Width(BrushNameWidth), GUILayout.Height(BrushThumbSize)) && definition != null)
                    EditorGUIUtility.PingObject(definition);

                using (new EditorGUI.DisabledScope(broken))
                {
                    var rect = EditorGUILayout.GetControlRect(GUILayout.Height(BrushThumbSize), GUILayout.MinWidth(80f));
                    rect.height = EditorGUIUtility.singleLineHeight;
                    rect.y += (BrushThumbSize - rect.height) * 0.5f;
                    item.Weight = EditorGUI.Slider(rect, item.Weight, 0f, ScatterBrushItem.MaxWeight);
                }

                GUILayout.Label($"{brush.Share(index) * 100f:0}%", EditorStyles.miniLabel, GUILayout.Width(34f), GUILayout.Height(BrushThumbSize));
                return GUILayout.Button(new GUIContent("✕", "구성에서 빼기"), GUILayout.Width(22f), GUILayout.Height(BrushThumbSize));
            }
        }

        // ───────────────────────── 설정 ─────────────────────────

        private void DrawBrushSettings()
        {
            var s = brush.Settings;
            EditorGUILayout.LabelField("브러시 설정", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(brushStroke.Active))
            {
                int modeIndex = GUILayout.Toolbar(brush.EraseMode ? 1 : 0, BrushModeLabels, GUILayout.Height(22f));
                SetBrushEraseMode(modeIndex == 1);
            }
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();

            s.Radius = EditorGUILayout.Slider(new GUIContent("반경 (m)", "씬뷰에서 [ ] 키로도 조절"), s.Radius, 0.1f, 20f);
            s.Density = EditorGUILayout.Slider(new GUIContent("밀도 (개/㎡)", "씬뷰에서 Ctrl+휠로도 조절. 같은 자리를 계속 문질러도 이 밀도까지만 채운다"), s.Density, 0f, 10f);
            s.MinSpacing = EditorGUILayout.Slider(new GUIContent("최소 간격 (m)", "장식끼리 이보다 가까이 놓지 않는다. 0이면 검사 안 함"), s.MinSpacing, 0f, 5f);
            float perStamp = s.Density * Mathf.PI * s.Radius * s.Radius;
            EditorGUILayout.LabelField(" ", $"빈 땅이면 원 하나에 약 {perStamp:0}개", EditorStyles.miniLabel);

            brushVariationFoldout = EditorGUILayout.Foldout(brushVariationFoldout, "변형", true);
            if (brushVariationFoldout)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    s.RandomYaw = EditorGUILayout.Toggle(new GUIContent("랜덤 회전 (Y)", "Y축으로 아무 방향이나 돌린다"), s.RandomYaw);
                    MinMaxRow(new GUIContent("스케일", "이 범위에서 고르게 뽑는다"), ref s.ScaleMin, ref s.ScaleMax, 0.1f, 3f);
                    s.AlignToNormal = EditorGUILayout.Slider(new GUIContent("지면 기울기 따르기", "0 = 똑바로 서기, 1 = 지면에 딱 붙기"), s.AlignToNormal, 0f, 1f);
                }
            }

            brushFilterFoldout = EditorGUILayout.Foldout(brushFilterFoldout, "필터", true);
            if (brushFilterFoldout)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    s.MaxSlope = EditorGUILayout.Slider(new GUIContent("최대 경사 (°)", "이보다 가파른 비탈에는 놓지 않는다"), s.MaxSlope, 0f, 90f);
                    s.UseHeightFilter = EditorGUILayout.Toggle(new GUIContent("높이 제한", "켜면 아래 높이(월드 Y) 범위 안에만 놓는다"), s.UseHeightFilter);
                    using (new EditorGUI.DisabledScope(!s.UseHeightFilter))
                    {
                        GetMapHeightRange(out float low, out float high);
                        MinMaxRow(new GUIContent("높이 (월드 Y)"), ref s.MinHeight, ref s.MaxHeight,
                            Mathf.Min(low, s.MinHeight), Mathf.Max(high, s.MaxHeight));
                    }
                }
            }

            int eraseTarget = EditorGUILayout.Popup(new GUIContent("지우개 대상", "지우개로 지울 장식. 칸 오브젝트는 지우지 않는다"),
                brush.EraseAllKinds ? 1 : 0, EraseTargetLabels);
            brush.EraseAllKinds = eraseTarget == 1;

            if (EditorGUI.EndChangeCheck())
            {
                s.Sanitize();
                SceneView.RepaintAll();
            }
        }

        /// <summary>슬라이더 높이 범위 기본값: 맵 바닥부터 꼭대기까지(월드 Y).</summary>
        private void GetMapHeightRange(out float low, out float high)
        {
            var grid = service.Map.Grid;
            low = grid.Origin.y;
            high = grid.Origin.y + service.Map.Size.y * grid.CellSize.y;
        }

        /// <summary>[최소 숫자] ━━슬라이더━━ [최대 숫자] 한 줄.</summary>
        private static void MinMaxRow(GUIContent label, ref float min, ref float max, float low, float high)
        {
            const float fieldWidth = 42f;
            const float gap = 4f;

            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            min = EditorGUI.FloatField(new Rect(rect.x, rect.y, fieldWidth, rect.height), min);
            var slider = new Rect(rect.x + fieldWidth + gap, rect.y, Mathf.Max(0f, rect.width - 2f * (fieldWidth + gap)), rect.height);
            EditorGUI.MinMaxSlider(slider, ref min, ref max, low, high);
            max = EditorGUI.FloatField(new Rect(rect.xMax - fieldWidth, rect.y, fieldWidth, rect.height), max);

            EditorGUI.indentLevel = indent;
        }
    }
}
