using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MapTool.Editor
{
    /// <summary>
    /// 맵툴 창. 맵 에셋을 고르고 팔레트에서 오브젝트를 골라 씬뷰에서 배치·선택·이동·회전·삭제한다.
    /// 모든 변경은 PlacementService 커맨드로만 하고, 씬은 MapSceneView가 이벤트를 받아 맞춘다.
    ///
    /// 조작 (씬뷰)
    ///   배치 모드: 클릭 = 배치, R = 회전
    ///   선택 모드: 클릭 = 선택, 드래그 = 이동, R = 회전, Delete = 삭제
    ///   브러시 모드: 팔레트 클릭 = 장식 담기/빼기, 창에서 비율·설정·프리셋.
    ///              씬뷰 드래그 = 뿌리기, Shift+드래그 = 지우기, [ ] = 반경, Ctrl+휠 = 밀도 (MapToolWindow.BrushScene)
    ///   존 모드:   드래그 = 새 존, Shift+드래그 = 선택한 존에 상자 추가, 클릭 = 선택(겹친 곳은 다시 클릭하면 다음 존),
    ///              면 핸들 드래그 = 격자 단위 크기 조절, Delete = 삭제. 창의 존 패널에서 이름·프로필·우선순위 편집
    ///   스폰 모드: 빈 칸 클릭 = 새 스폰, 스폰 클릭 = 선택, 드래그 = 이동, R = 방향, Delete = 삭제.
    ///              창의 스폰 목록에서 이름·기본(★) 지정 (MapToolWindow.Spawns)
    ///   공통: Ctrl+Z / Ctrl+Y(Ctrl+Shift+Z) = 코어 Undo/Redo, Esc = 취소
    /// </summary>
    public sealed partial class MapToolWindow : EditorWindow
    {
        // 값은 창에 직렬화되므로 순서를 바꾸지 말고 뒤에 붙인다. 툴바 순서는 ModeOrder.
        private enum Mode { Off, Select, Place, Zone, Brush, Spawn }

        private static readonly Mode[] ModeOrder = { Mode.Off, Mode.Select, Mode.Place, Mode.Brush, Mode.Zone, Mode.Spawn };
        private static readonly string[] ModeLabels = { "끄기", "선택", "배치", "브러시", "존", "스폰" };

        private const string AllCategories = "전체";
        /// <summary>타일 최소 크기. 창 너비를 이 크기로 나눈 만큼 열을 만들고, 남는 폭은 타일을 키워 채운다.</summary>
        private const float MinTileSize = 72f;
        private const float TileSpacing = 6f;

        private static readonly Color ValidColor = new(0.3f, 1f, 0.45f, 1f);
        private static readonly Color InvalidColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color SelectColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.7f);
        private static readonly Color GridColor = new(1f, 1f, 1f, 0.12f);
        private static readonly Color ZoneColor = new(0.35f, 0.75f, 1f, 0.8f);

        [SerializeField] private MapAsset map;
        [SerializeField] private Mode mode = Mode.Select;
        [SerializeField] private int layer;
        [SerializeField] private string paletteSelection;
        [SerializeField] private GridRotation placeRotation;
        [SerializeField] private string search = "";
        [SerializeField] private string category = AllCategories;
        [SerializeField] private Vector2 paletteScroll;
        [System.NonSerialized] private float paletteWidth;
        [SerializeField] private Vector3Int newMapSize = new(20, 4, 20);
        [SerializeField] private bool zoneFullHeight = true;
        [SerializeField] private int zoneHeight = 1;

        private EditorObjectCatalog catalog;
        private PlacementService service;
        private MapSceneView view;
        private ScatterSceneView scatterView;
        private IZoneSceneSync zoneSync;
        private SpawnSceneSync spawnSync;
        private GhostPreview ghost;
        private readonly MapToolShortcuts.Context shortcutContext = new();

        // 선택한 오브젝트들(Ctrl+클릭으로 늘린다). 마지막 항목이 주 선택이고 정보 패널에 보인다.
        private readonly List<int> selection = new();

        /// <summary>주 선택. 대입하면 그 하나만 선택한다(0이면 선택 해제).</summary>
        private int SelectedId
        {
            get => selection.Count > 0 ? selection[^1] : 0;
            set
            {
                selection.Clear();
                if (value != 0) selection.Add(value);
            }
        }

        private bool dragging;
        // 드래그로 잡은 오브젝트. 묶음은 이 오브젝트가 움직인 만큼 함께 움직인다.
        private int dragAnchorId;
        private Vector3Int dragOffset;
        private Vector3Int dragTarget;
        // 드래그 중 마지막으로 규칙을 통과한 칸(잡은 오브젝트 기준). 빨간 칸에서 놓으면 취소 대신 여기로 옮긴다.
        private Vector3Int dragLastValid;
        // 묶음 안의 오브젝트를 Ctrl 없이 눌렀을 때: 끌지 않고 떼면 그 하나만 선택한다.
        private int clickToSingle;

        private string status;

        private int selectedZoneId;
        private bool zoneDragging;
        private Vector3Int zoneDragStart;
        private Vector3Int zoneDragEnd;

        internal static MapToolWindow Active { get; private set; }

        internal bool IsEditing => service != null && view != null && view.Root != null;
        internal bool IsCapturingInput => IsEditing && mode != Mode.Off;

        [MenuItem("Tools/Map Tool/Map Tool 창", priority = 0)]
        public static void Open() => GetWindow<MapToolWindow>("Map Tool");

        public static void Open(MapAsset asset)
        {
            var window = GetWindow<MapToolWindow>("Map Tool");
            window.map = asset;
            window.LoadSession();
        }

        // ───────────────────────── 수명 ─────────────────────────

        private void OnEnable()
        {
            Active = this;
            ghost = new GhostPreview();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            ShortcutManager.RegisterContext(shortcutContext);
            if (map == null) map = FindMapInScene(SceneManager.GetActiveScene()) ?? LoadLastMap();
            LoadSession();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            ShortcutManager.UnregisterContext(shortcutContext);
            EndSession();
            ghost?.Dispose();
            if (Active == this) Active = null;
        }

        private void LoadSession()
        {
            EndSession();
            catalog = EditorObjectCatalog.LoadAll();
            if (map == null) return;
            RememberLastMap(map);

            service = new PlacementService(map.Data, catalog);
            layer = Mathf.Clamp(layer, 0, map.Data.Size.y - 1);
            var root = MapSceneView.FindRoot(SceneManager.GetActiveScene(), map.name);
            if (root != null) AttachView(root);
            SceneView.RepaintAll();
        }

        private void AttachView(Transform root)
        {
            view = new MapSceneView(service, catalog, root);
            bool changed = view.Sync();
            scatterView = new ScatterSceneView(service, catalog, root);
            changed |= scatterView.Sync();
            scatterView.Hidden = scattersHidden;
            zoneSync = ZoneBridge.Current?.CreateSceneSync(service, root);
            if (zoneSync != null) changed |= zoneSync.Sync();
            spawnSync = new SpawnSceneSync(service, root);
            changed |= spawnSync.Sync();
            if (changed) EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        private void SetScattersHidden(bool hide)
        {
            scattersHidden = hide;
            if (scatterView != null) scatterView.Hidden = hide;
            SceneView.RepaintAll();
        }

        private void EndSession()
        {
            EndBrushStroke();
            view?.Dispose();
            view = null;
            scatterView?.Dispose();
            scatterView = null;
            zoneSync?.Dispose();
            zoneSync = null;
            spawnSync?.Dispose();
            spawnSync = null;
            service = null;
            SelectedId = 0;
            dragging = false;
            selectedZoneId = 0;
            zoneDragging = false;
            zoneResizing = false;
            detailZoneId = 0;
            selectedSpawnId = 0;
            spawnDragging = false;
            detailSpawnId = 0;
            focusedIssue = null;
            InvalidateValidation();
            ghost?.Hide();
        }

        private void OnProjectChanged()
        {
            // 정의(크기·프리팹)가 바뀌었거나 맵 에셋이 다시 로드됐으면 세션을 새로 만든다. 이때 Undo 기록은 버려진다.
            var latest = EditorObjectCatalog.LoadAll();
            bool stale = map == null || service == null || service.Map != map.Data ||
                         catalog == null || latest.Signature != catalog.Signature;
            if (stale) LoadSession();
            InvalidateValidation(); // 프로필 에셋이 생기거나 지워졌을 수 있다
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            // 새 씬에 맵이 구워져 있으면 그 맵으로 바꾼다. 없으면 보던 맵을 유지한다.
            var sceneMap = FindMapInScene(to);
            if (sceneMap != null) map = sceneMap;
            LoadSession();
            Repaint();
        }

        // ───────────────────────── 맵 자동 선택 ─────────────────────────

        private const string LastMapKey = "MapTool.LastMapGuid";

        /// <summary>씬 루트의 "[MapTool] 이름" 오브젝트에서 맵 이름을 읽어 같은 이름의 MapAsset을 찾는다.</summary>
        internal static MapAsset FindMapInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (!go.name.StartsWith(MapSceneView.RootPrefix)) continue;
                string mapName = go.name.Substring(MapSceneView.RootPrefix.Length);
                foreach (string guid in AssetDatabase.FindAssets("t:MapAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != mapName) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
                    if (asset != null) return asset;
                }
            }
            return null;
        }

        // EditorUserSettings는 프로젝트별·사용자별(UserSettings 폴더, 버전 관리 제외)로 저장된다.
        private static void RememberLastMap(MapAsset asset)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long _))
                EditorUserSettings.SetConfigValue(LastMapKey, guid);
        }

        private static MapAsset LoadLastMap()
        {
            string guid = EditorUserSettings.GetConfigValue(LastMapKey);
            if (string.IsNullOrEmpty(guid)) return null;
            return AssetDatabase.LoadAssetAtPath<MapAsset>(AssetDatabase.GUIDToAssetPath(guid));
        }

        // ───────────────────────── 창 UI ─────────────────────────

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (MapAsset)EditorGUILayout.ObjectField("맵", map, typeof(MapAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                map = picked;
                LoadSession();
            }

            if (map == null)
            {
                DrawNewMapGUI();
                return;
            }
            if (service == null) LoadSession();

            if (view == null || view.Root == null)
            {
                EditorGUILayout.HelpBox("현재 씬에 이 맵의 오브젝트가 없습니다.", MessageType.Info);
                if (GUILayout.Button("현재 씬에 맵 표시"))
                {
                    var root = MapSceneView.CreateRoot(SceneManager.GetActiveScene(), map.name);
                    AttachView(root);
                    EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                }
                return;
            }

            DrawToolbar();
            DrawValidationPanel();
            DrawModeAndLayer();
            EditorGUILayout.Space(4);
            DrawSelectionInfo();
            EditorGUILayout.Space(4);
            if (mode == Mode.Zone)
            {
                DrawZonePanel();
            }
            else if (mode == Mode.Spawn)
            {
                DrawSpawnPanel();
            }
            else
            {
                if (mode == Mode.Brush) DrawBrushPanel();
                DrawPalette();
            }

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawNewMapGUI()
        {
            EditorGUILayout.HelpBox("편집할 맵 에셋을 고르거나 새로 만드세요.", MessageType.Info);
            DrawNewMapFromJsonButton();
            EditorGUILayout.Space(6);
            newMapSize = EditorGUILayout.Vector3IntField("새 맵 크기 (X, 층, Z)", newMapSize);
            if (!GUILayout.Button("새 맵 만들기...")) return;

            string path = EditorUtility.SaveFilePanelInProject("새 맵", "Map", "asset", "맵 에셋을 저장할 위치를 고르세요.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<MapAsset>();
            asset.Initialize(new Vector3Int(Mathf.Max(1, newMapSize.x), Mathf.Max(1, newMapSize.y), Mathf.Max(1, newMapSize.z)), GridSpace.Unit);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            map = asset;
            LoadSession();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!service.CanUndo))
                {
                    if (GUILayout.Button("되돌리기", EditorStyles.toolbarButton)) UndoAction();
                }
                using (new EditorGUI.DisabledScope(!service.CanRedo))
                {
                    if (GUILayout.Button("다시 실행", EditorStyles.toolbarButton)) RedoAction();
                }

                GUILayout.Space(6f);
                DrawValidationButton();

                GUILayout.FlexibleSpace();
                bool dirty = EditorUtility.IsDirty(map);
                GUILayout.Label(dirty ? "저장 안 됨" : "저장됨", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("저장", EditorStyles.toolbarButton)) Save();
                }
                DrawJsonMenu();
            }
        }

        private void DrawModeAndLayer()
        {
            EditorGUI.BeginChangeCheck();
            int modeIndex = Mathf.Max(0, System.Array.IndexOf(ModeOrder, mode));
            mode = ModeOrder[GUILayout.Toolbar(modeIndex, ModeLabels)];
            // 브러시는 지면 레이캐스트로 높이를 정하므로 층을 쓰지 않는다.
            if (mode != Mode.Brush) layer = EditorGUILayout.IntSlider("층 (Y)", layer, 0, map.Data.Size.y - 1);
            if (mode == Mode.Zone) DrawZoneHeight();
            if (EditorGUI.EndChangeCheck())
            {
                dragging = false;
                zoneDragging = false;
                zoneResizing = false;
                spawnDragging = false;
                if (mode != Mode.Place) ghost.Hide();
                SceneView.RepaintAll();
            }

            var size = map.Data.Size;
            EditorGUILayout.LabelField(
                $"크기 {size.x}×{size.y}×{size.z} · 오브젝트 {map.Data.Count}개 · 장식 {map.Data.ScatterCount}개 · 존 {map.Data.Zones.Count}개 · 스폰 {map.Data.Spawns.Count}개",
                EditorStyles.miniLabel);
        }

        private void DrawZoneHeight()
        {
            zoneFullHeight = EditorGUILayout.Toggle(new GUIContent("새 상자: 맵 전체 높이", "끄면 현재 층부터 아래 높이만큼 만든다"), zoneFullHeight);
            using (new EditorGUI.DisabledScope(zoneFullHeight))
            {
                int maxHeight = Mathf.Max(1, map.Data.Size.y - layer);
                zoneHeight = EditorGUILayout.IntSlider("새 상자 높이 (층)", Mathf.Clamp(zoneHeight, 1, maxHeight), 1, maxHeight);
            }
        }

        private void DrawSelectionInfo()
        {
            if (mode == Mode.Place)
            {
                string name = catalog.TryGetAsset(paletteSelection, out var def) ? def.name : "(팔레트에서 고르세요)";
                EditorGUILayout.LabelField("배치", $"{name} · 회전 {placeRotation.ToDegrees():0}° (R)");
                return;
            }

            if (mode == Mode.Zone || mode == Mode.Brush || mode == Mode.Spawn) return; // 존·브러시·스폰 패널이 따로 보여 준다

            if (SelectedId == 0 || !service.Map.TryGet(SelectedId, out var placed))
            {
                EditorGUILayout.LabelField("선택", mode == Mode.Select ? "씬뷰에서 오브젝트를 클릭하세요 (Ctrl+클릭: 여러 개)" : "-");
                return;
            }

            bool multi = selection.Count > 1;
            if (multi)
                EditorGUILayout.LabelField("선택", $"{selection.Count}개 · 주 선택 #{placed.InstanceId} {placed.DefinitionId}");
            else
                EditorGUILayout.LabelField("선택", $"#{placed.InstanceId} {placed.DefinitionId} · 셀 {placed.Cell} · {placed.Rotation.ToDegrees():0}°");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(multi))
                {
                    var rotateLabel = new GUIContent("회전 (R)", multi ? "여러 개를 선택했을 때는 회전할 수 없습니다" : null);
                    if (GUILayout.Button(rotateLabel)) RotateAction();
                }
                if (GUILayout.Button("복제 (Ctrl+D)")) DuplicateAction();
                if (GUILayout.Button("삭제 (Del)")) DeleteAction();
            }
        }

        private void DrawPalette()
        {
            if (catalog.Definitions.Count == 0)
            {
                EditorGUILayout.HelpBox("오브젝트 정의가 없습니다.\nTools > Map Tool > 샘플 오브젝트 생성, 또는 Create > Map Tool > Object Definition으로 만드세요.", MessageType.Info);
                return;
            }

            // 브러시 모드에서는 장식만 보인다. 카테고리도 보이는 정의 기준으로 만든다.
            bool brushing = mode == Mode.Brush;
            var pool = catalog.Definitions.Where(d => !brushing || d.IsScatter).ToList();
            if (pool.Count == 0)
            {
                EditorGUILayout.HelpBox("장식 정의가 없습니다.\n오브젝트 정의 에셋의 종류를 '장식'으로 바꾸면 여기에 나옵니다.", MessageType.Info);
                return;
            }

            var categories = new[] { AllCategories }.Concat(pool.Select(d => d.Category).Distinct()).ToArray();
            int categoryIndex = Mathf.Max(0, System.Array.IndexOf(categories, category));
            categoryIndex = GUILayout.Toolbar(categoryIndex, categories);
            category = categories[categoryIndex];
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

            var items = pool
                .Where(d => category == AllCategories || d.Category == category)
                .Where(d => string.IsNullOrEmpty(search) ||
                            d.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            d.Id.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            bool loadingPreview = false;

            paletteScroll = EditorGUILayout.BeginScrollView(paletteScroll);

            // 스크롤 뷰 안에서 실제로 쓸 수 있는 폭을 잰다(세로 스크롤바 유무 반영). Layout 이벤트의 값은 가짜라 Repaint 때만 갱신한다.
            var widthProbe = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && !Mathf.Approximately(widthProbe.width, paletteWidth))
            {
                paletteWidth = widthProbe.width;
                Repaint();
            }
            float available = paletteWidth > 0f ? paletteWidth : position.width - 24f;

            // 최소 크기로 열 수를 정하고, 남는 폭은 타일을 키워 채운다. 마지막 열의 오른쪽 끝이 폭 끝에 딱 맞는다.
            int columns = Mathf.Max(1, (int)((available + TileSpacing) / (MinTileSize + TileSpacing)));
            float tileSize = Mathf.Max(1f, (available - TileSpacing * (columns - 1)) / columns);
            float labelHeight = EditorGUIUtility.singleLineHeight;

            for (int i = 0; i < items.Count; i += columns)
            {
                var row = GUILayoutUtility.GetRect(0f, tileSize + labelHeight + TileSpacing, GUILayout.ExpandWidth(true));
                for (int j = i; j < Mathf.Min(i + columns, items.Count); j++)
                {
                    var cell = new Rect(row.x + (j - i) * (tileSize + TileSpacing), row.y, tileSize, tileSize + labelHeight);
                    loadingPreview |= DrawTile(cell, items[j]);
                }
            }
            EditorGUILayout.EndScrollView();

            if (loadingPreview) Repaint();
        }

        /// <returns>썸네일이 아직 로딩 중이면 true.</returns>
        private bool DrawTile(Rect cell, MapObjectDefinition definition)
        {
            var preview = definition.Prefab != null ? AssetPreview.GetAssetPreview(definition.Prefab) : null;
            bool loading = definition.Prefab != null && preview == null && AssetPreview.IsLoadingAssetPreviews();
            var image = preview != null ? preview : AssetPreview.GetMiniThumbnail(definition);

            bool highlighted = mode == Mode.Brush ? brush.Contains(definition)
                : mode == Mode.Place && definition.Id == paletteSelection;
            var previous = GUI.backgroundColor;
            if (highlighted) GUI.backgroundColor = SelectColor;

            var tooltip = definition.IsScatter
                ? $"{definition.Id} · 장식 · {(mode == Mode.Brush ? "클릭 = 브러시에 담기/빼기" : "클릭 = 브러시로 뿌리기")}"
                : $"{definition.Id} · {definition.Size.x}×{definition.Size.y}×{definition.Size.z}";
            var tileRect = new Rect(cell.x, cell.y, cell.width, cell.width);
            if (GUI.Button(tileRect, new GUIContent(image, tooltip)))
                OnTileClicked(definition);
            GUI.backgroundColor = previous;

            if (mode == Mode.Brush && highlighted) DrawTileBadge(tileRect, "✓");
            else if (mode != Mode.Brush && definition.IsScatter) DrawTileBadge(tileRect, "장식");

            var labelRect = new Rect(cell.x, tileRect.yMax, cell.width, cell.height - cell.width);
            GUI.Label(labelRect, definition.name, EditorStyles.centeredGreyMiniLabel);
            return loading;
        }

        /// <summary>
        /// 브러시 모드: 장식을 구성에 담거나 뺀다.
        /// 다른 모드: 칸 오브젝트는 배치 모드로, 장식은 브러시 모드로 바꾸고 구성에 담는다(이미 담긴 구성은 지우지 않는다).
        /// </summary>
        private void OnTileClicked(MapObjectDefinition definition)
        {
            SelectedId = 0;
            dragging = false;
            if (definition.IsScatter)
            {
                if (mode == Mode.Brush) brush.Toggle(definition);
                else
                {
                    brush.Add(definition);
                    mode = Mode.Brush;
                    ghost.Hide();
                }
            }
            else
            {
                paletteSelection = definition.Id;
                mode = Mode.Place;
            }
            SceneView.RepaintAll();
        }

        private static GUIStyle tileBadgeStyle;

        private static void DrawTileBadge(Rect tile, string text)
        {
            tileBadgeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                padding = new RectOffset(3, 3, 0, 0),
            };
            var content = new GUIContent(text);
            var size = tileBadgeStyle.CalcSize(content);
            var rect = new Rect(tile.xMax - size.x - 3f, tile.y + 3f, size.x, size.y);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(rect, content, tileBadgeStyle);
        }

        // ───────────────────────── 씬뷰 ─────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!IsEditing)
            {
                ghost?.Hide();
                return;
            }

            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                DrawGrid();
                DrawValidationMarkers(); // 끄기·브러시 모드에서도 보이게 모드별 처리보다 먼저
                DrawSpawns();            // 스폰도 모든 모드에서 보인다
            }
            if (mode != Mode.Brush) EndBrushStroke(); // 드래그 도중 모드를 바꿨으면 그때까지 찍은 것을 닫는다
            if (mode == Mode.Off)
            {
                ghost.Hide();
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);

            if (mode == Mode.Brush)
            {
                ghost.Hide();
                HandleBrush(sceneView, e, controlId);
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && brushStroke.Active)
                {
                    EndBrushStroke();
                    e.Use();
                }
                if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) sceneView.Repaint();
                return;
            }

            bool hasCell = TryGetHoveredCell(e.mousePosition, out var cell);
            if (e.type == EventType.MouseLeaveWindow) hasCell = false;

            if (mode == Mode.Zone)
            {
                ghost.Hide();
                DrawZones();
                HandleZoneFaces();
                HandleZone(e, controlId, hasCell, cell);
            }
            else if (mode == Mode.Spawn)
            {
                ghost.Hide();
                HandleSpawn(e, controlId, hasCell, cell);
            }
            else
            {
                if (mode == Mode.Place) HandlePlace(e, controlId, hasCell, cell);
                else HandleSelect(e, controlId, hasCell, cell);
                DrawSelected();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (dragging) dragging = false;
                else if (zoneDragging) { zoneDragging = false; GUIUtility.hotControl = 0; }
                else if (spawnDragging) { spawnDragging = false; GUIUtility.hotControl = 0; }
                else if (mode == Mode.Place) mode = Mode.Select;
                else if (mode == Mode.Zone) selectedZoneId = 0;
                else if (mode == Mode.Spawn) selectedSpawnId = 0;
                else SelectedId = 0;
                ghost.Hide();
                e.Use();
                Repaint();
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) sceneView.Repaint();
        }

        private void HandlePlace(Event e, int controlId, bool hasCell, Vector3Int cell)
        {
            // 정의 종류가 나중에 장식으로 바뀐 경우: 코어가 칸 배치를 거부하므로 고스트도 띄우지 않는다.
            if (!hasCell || !catalog.TryGetAsset(paletteSelection, out var definition) || definition.IsScatter)
            {
                ghost.Hide();
                return;
            }

            var candidate = new PlacedObject(0, definition.Id, cell, placeRotation);
            var check = service.CanPlace(candidate);
            bool valid = check.IsValid;
            if (service.TryGetBounds(candidate, out var bounds))
            {
                DrawFootprint(bounds, valid ? ValidColor : InvalidColor);
                if (!valid) DrawFootprintLabel(bounds, check.Message, InvalidColor);
            }
            ghost.Show(definition, MapSceneView.ComputePose(service, candidate), valid);

            if (e.type == EventType.MouseDown && IsPrimary(e))
            {
                GUIUtility.hotControl = controlId;
                Execute(new PlaceCommand(definition.Id, cell, placeRotation));
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        private void HandleSelect(Event e, int controlId, bool hasCell, Vector3Int cell)
        {
            ghost.Hide();
            int hovered = hasCell && service.Occupancy.TryGetOccupant(cell, out var occupant) ? occupant : 0;

            if (!dragging && hovered != 0 && !selection.Contains(hovered) &&
                service.Map.TryGet(hovered, out var hoveredObject) && service.TryGetBounds(hoveredObject, out var hoverBounds))
                DrawFootprint(hoverBounds, HoverColor);

            switch (e.type)
            {
                case EventType.MouseDown when IsPrimary(e) && (e.control || e.command):
                    // Ctrl(맥은 Cmd)+클릭: 선택에 넣거나 뺀다. 끌지는 않는다.
                    GUIUtility.hotControl = controlId;
                    if (hovered != 0 && !selection.Remove(hovered)) selection.Add(hovered);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDown when IsPrimary(e):
                    GUIUtility.hotControl = controlId;
                    clickToSingle = 0;
                    if (hovered != 0 && selection.Count > 1 && selection.Contains(hovered))
                    {
                        // 묶음을 잡았다: 선택은 그대로 두고, 끌지 않고 떼면 그 하나만 남긴다.
                        clickToSingle = hovered;
                        selection.Remove(hovered);
                        selection.Add(hovered); // 주 선택으로
                    }
                    else
                    {
                        SelectedId = hovered;
                    }

                    if (hovered != 0 && service.Map.TryGet(hovered, out var grabbed))
                    {
                        dragging = true;
                        dragAnchorId = hovered;
                        dragOffset = grabbed.Cell - cell;
                        dragTarget = grabbed.Cell;
                        dragLastValid = grabbed.Cell;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when dragging && GUIUtility.hotControl == controlId:
                    if (hasCell && service.Map.TryGet(dragAnchorId, out var anchor))
                    {
                        var target = cell + dragOffset;
                        target.y = anchor.Cell.y; // 층은 유지하고 X/Z로만 옮긴다
                        if (target != dragTarget) clickToSingle = 0;
                        dragTarget = target;
                        if (service.CanPlaceGroup(MovedSelection(target - anchor.Cell)).IsValid) dragLastValid = target;
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    if (dragging && service.Map.TryGet(dragAnchorId, out var moving) && dragLastValid != moving.Cell)
                        Execute(new MoveManyCommand(selection, dragLastValid - moving.Cell));
                    else if (clickToSingle != 0)
                        SelectedId = clickToSingle;
                    clickToSingle = 0;
                    dragging = false;
                    e.Use();
                    Repaint();
                    break;
            }

            if (dragging && service.Map.TryGet(dragAnchorId, out var dragged))
            {
                var moved = MovedSelection(dragTarget - dragged.Cell);
                var check = service.CanPlaceGroup(moved);
                DrawFootprints(moved, check.IsValid ? ValidColor : InvalidColor, check.IsValid ? null : check.Message);

                // 빨간 칸 위에 있을 때는 놓으면 실제로 갈 자리를 함께 보여 준다.
                if (!check.IsValid && dragLastValid != dragged.Cell)
                    DrawFootprints(MovedSelection(dragLastValid - dragged.Cell), ValidColor, null);
            }
        }

        /// <summary>선택한 오브젝트 전부를 delta만큼 옮긴 후보. 회전은 그대로다.</summary>
        private List<PlacedObject> MovedSelection(Vector3Int delta)
        {
            var moved = new List<PlacedObject>(selection.Count);
            foreach (var id in selection)
                if (service.Map.TryGet(id, out var placed))
                    moved.Add(placed.WithPose(placed.Cell + delta, placed.Rotation));
            return moved;
        }

        /// <summary>후보들의 영역을 그린다. message가 있으면 첫 후보 위에 띄운다.</summary>
        private void DrawFootprints(List<PlacedObject> candidates, Color color, string message)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!service.TryGetBounds(candidates[i], out var bounds)) continue;
                DrawFootprint(bounds, color);
                if (i == 0 && message != null) DrawFootprintLabel(bounds, message, color);
            }
        }

        // ───────────────────────── 복제 ─────────────────────────

        /// <summary>
        /// Ctrl+D: 선택한 오브젝트들의 사본을 원본 바로 옆 빈자리에 즉시 놓고 사본을 선택한다.
        /// 상대 위치·회전·층은 그대로이고, 원하는 곳으로는 이어서 끌어 옮기면 된다.
        /// </summary>
        internal void DuplicateAction()
        {
            if (!IsEditing || mode != Mode.Select || dragging || selection.Count == 0) return;
            if (!service.Map.TryGet(SelectedId, out var primary)) return;

            // 첫 항목이 주 선택. 사본도 같은 순서로 만들어 주 선택의 사본이 다시 주 선택이 되게 한다.
            var sources = new List<PlacedObject> { primary };
            foreach (var id in selection)
                if (id != primary.InstanceId && service.Map.TryGet(id, out var placed))
                    sources.Add(placed);

            var candidates = FindNearestCopySpot(sources);
            if (candidates == null)
            {
                ShowFailure(PlacementResult.Fail(PlacementCodes.Overlap, "근처에 복제할 빈자리가 없습니다."));
                return;
            }

            var command = new PlaceManyCommand(candidates);
            if (!Execute(command).IsValid) return;
            selection.Clear();
            for (int i = command.Placed.Count - 1; i >= 0; i--) selection.Add(command.Placed[i].InstanceId);
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 같은 층에서 원본과 가장 가까운 평행 이동 자리를 찾는다. 거리가 같으면 +X, +Z, -Z, -X 순으로 고른다.
        /// 놓을 곳이 없으면 null.
        /// </summary>
        private List<PlacedObject> FindNearestCopySpot(List<PlacedObject> sources)
        {
            var size = service.Map.Size;
            int maxRing = Mathf.Max(size.x, size.z);
            var candidates = new List<PlacedObject>(sources.Count);
            var ring = new List<Vector3Int>();
            for (int r = 1; r <= maxRing; r++)
            {
                ring.Clear();
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == r)
                        ring.Add(new Vector3Int(dx, 0, dz));
                ring.Sort((a, b) =>
                {
                    int c = (a.x * a.x + a.z * a.z).CompareTo(b.x * b.x + b.z * b.z);
                    if (c != 0) return c;
                    c = b.x.CompareTo(a.x);
                    return c != 0 ? c : b.z.CompareTo(a.z);
                });

                foreach (var offset in ring)
                {
                    candidates.Clear();
                    foreach (var source in sources)
                        candidates.Add(new PlacedObject(0, source.DefinitionId, source.Cell + offset, source.Rotation));
                    if (service.CanPlaceGroup(candidates).IsValid) return candidates;
                }
            }
            return null;
        }

        private void HandleZone(Event e, int controlId, bool hasCell, Vector3Int cell)
        {
            var size = service.Map.Size;
            if (hasCell) cell = new Vector3Int(Mathf.Clamp(cell.x, 0, size.x - 1), layer, Mathf.Clamp(cell.z, 0, size.z - 1));

            if (!zoneDragging && hasCell)
            {
                int hovered = PickZone(cell, false);
                if (hovered != 0 && hovered != selectedZoneId && service.Map.TryGetZone(hovered, out var hoveredZone))
                    DrawZone(hoveredZone, HoverColor, false);
            }

            switch (e.type)
            {
                case EventType.MouseDown when IsPrimary(e) && hasCell:
                    GUIUtility.hotControl = controlId;
                    zoneDragging = true;
                    zoneDragStart = cell;
                    zoneDragEnd = cell;
                    e.Use();
                    break;

                case EventType.MouseDrag when zoneDragging && GUIUtility.hotControl == controlId:
                    if (hasCell) zoneDragEnd = cell;
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    if (zoneDragging)
                    {
                        zoneDragging = false;
                        if (zoneDragEnd == zoneDragStart) selectedZoneId = PickZone(zoneDragStart, true);
                        else CommitZoneBox(DragBox(), e.shift);
                    }
                    e.Use();
                    Repaint();
                    break;
            }

            if (zoneDragging && zoneDragEnd != zoneDragStart)
            {
                var box = DragBox();
                bool adding = e.shift && service.Map.TryGetZone(selectedZoneId, out _);
                var preview = new PlacedZone(0, "", "", 0, new[] { box });
                var check = service.CanPlaceZone(preview);
                DrawFootprint(box.Bounds, !check.IsValid ? InvalidColor : adding ? SelectColor : ValidColor);
                if (e.type == EventType.Repaint)
                {
                    var grid = service.Map.Grid;
                    var top = (grid.CellToWorld(box.Bounds.min) + grid.CellToWorld(box.Bounds.max)) * 0.5f;
                    string label = $"{box.Size.x}×{box.Size.z}{(adding ? " (상자 추가)" : " (새 존)")}";
                    if (check.IsValid) Handles.Label(top, label, EditorStyles.whiteBoldLabel);
                    else DrawFootprintLabel(box.Bounds, $"{label} — {check.Message}", InvalidColor);
                }
            }
        }

        /// <summary>드래그한 두 셀로 만든 상자. 높이는 창 설정(맵 전체 또는 현재 층부터 N층).</summary>
        private ZoneBox DragBox()
        {
            var min = Vector3Int.Min(zoneDragStart, zoneDragEnd);
            var max = Vector3Int.Max(zoneDragStart, zoneDragEnd);
            int mapHeight = service.Map.Size.y;
            int minY = zoneFullHeight ? 0 : layer;
            int height = zoneFullHeight ? mapHeight : Mathf.Clamp(zoneHeight, 1, mapHeight - layer);
            return new ZoneBox(new Vector3Int(min.x, minY, min.z), new Vector3Int(max.x - min.x + 1, height, max.z - min.z + 1));
        }

        private void CommitZoneBox(ZoneBox box, bool addToSelected)
        {
            if (addToSelected && service.Map.TryGetZone(selectedZoneId, out var zone))
            {
                Execute(new ModifyZoneCommand(zone.WithBoxes(zone.Boxes.Append(box))));
                return;
            }

            var command = new AddZoneCommand(new PlacedZone(0, NextZoneName(), "", 0, new[] { box }));
            if (Execute(command).IsValid) selectedZoneId = command.Added.Id;
        }

        private string NextZoneName()
        {
            var names = new System.Collections.Generic.HashSet<string>(service.Map.Zones.Select(z => z.Name));
            int n = service.Map.Zones.Count + 1;
            while (names.Contains($"존 {n}")) n++;
            return $"존 {n}";
        }

        /// <summary>
        /// 셀을 포함한 존 중 하나. 우선순위가 높고 작은 존이 먼저.
        /// cycle이면 지금 선택한 존이 후보에 있을 때 다음 후보를 고른다(겹친 존을 클릭으로 돌아가며 선택).
        /// </summary>
        private int PickZone(Vector3Int cell, bool cycle)
        {
            var hits = service.Map.Zones
                .Where(z => z.Contains(cell))
                .OrderByDescending(z => z.Priority)
                .ThenBy(z => z.CellCount)
                .Select(z => z.Id)
                .ToList();
            if (hits.Count == 0) return 0;
            if (!cycle) return hits[0];

            int current = hits.IndexOf(selectedZoneId);
            return hits[(current + 1) % hits.Count];
        }

        private void DrawZones()
        {
            foreach (var zone in service.Map.Zones)
            {
                if (zone.Id == selectedZoneId) continue;
                DrawZone(zone, ZoneColor, true);
            }

            if (selectedZoneId == 0) return;
            if (TryGetDisplayedSelectedZone(out var selected)) DrawZone(selected, SelectColor, true);
            else selectedZoneId = 0;
        }

        private void DrawZone(PlacedZone zone, Color color, bool label)
        {
            if (Event.current.type != EventType.Repaint) return;

            foreach (var box in zone.Boxes) DrawFootprint(box.Bounds, color);
            if (!label || zone.Boxes.Count == 0) return;

            var grid = service.Map.Grid;
            var first = zone.Boxes[0].Bounds;
            var min = grid.CellToWorld(first.min);
            var max = grid.CellToWorld(first.max);
            var style = new GUIStyle(EditorStyles.whiteBoldLabel) { normal = { textColor = color } };
            Handles.Label(new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f), zone.Name, style);
        }

        private static bool IsPrimary(Event e) => e.button == 0 && !e.alt;

        private bool TryGetHoveredCell(Vector2 mousePosition, out Vector3Int cell)
        {
            var grid = service.Map.Grid;
            float height = grid.Origin.y + layer * grid.CellSize.y;
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance))
            {
                cell = default;
                return false;
            }

            cell = grid.WorldToCell(ray.GetPoint(distance));
            cell.y = layer;
            return true;
        }

        private void DrawGrid()
        {
            var grid = service.Map.Grid;
            var size = service.Map.Size;
            float y = grid.Origin.y + layer * grid.CellSize.y;
            var origin = grid.Origin;
            float width = size.x * grid.CellSize.x;
            float depth = size.z * grid.CellSize.z;

            using (new Handles.DrawingScope(GridColor))
            {
                for (int x = 0; x <= size.x; x++)
                {
                    float wx = origin.x + x * grid.CellSize.x;
                    Handles.DrawLine(new Vector3(wx, y, origin.z), new Vector3(wx, y, origin.z + depth));
                }
                for (int z = 0; z <= size.z; z++)
                {
                    float wz = origin.z + z * grid.CellSize.z;
                    Handles.DrawLine(new Vector3(origin.x, y, wz), new Vector3(origin.x + width, y, wz));
                }
            }

            var min = grid.CellToWorld(Vector3Int.zero);
            var max = grid.CellToWorld(size);
            using (new Handles.DrawingScope(new Color(1f, 1f, 1f, 0.25f)))
                Handles.DrawWireCube((min + max) * 0.5f, max - min);
        }

        private void DrawSelected()
        {
            selection.RemoveAll(id => !service.Map.TryGet(id, out _));
            foreach (var id in selection)
                if (service.Map.TryGet(id, out var placed) && service.TryGetBounds(placed, out var bounds))
                    DrawFootprint(bounds, SelectColor);
        }

        private void DrawFootprint(BoundsInt bounds, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;

            var grid = service.Map.Grid;
            var min = grid.CellToWorld(bounds.min);
            var max = grid.CellToWorld(bounds.max);
            using (new Handles.DrawingScope(color))
                Handles.DrawWireCube((min + max) * 0.5f, max - min);

            float y = min.y + 0.01f;
            var face = new[]
            {
                new Vector3(min.x, y, min.z), new Vector3(max.x, y, min.z),
                new Vector3(max.x, y, max.z), new Vector3(min.x, y, max.z),
            };
            Handles.DrawSolidRectangleWithOutline(face, new Color(color.r, color.g, color.b, 0.15f), color);
        }

        private static GUIStyle footprintLabelStyle;

        /// <summary>영역 윗면 가운데에 글씨를 띄운다. 놓을 수 없는 이유를 클릭 전에 보여 주는 데 쓴다.</summary>
        private void DrawFootprintLabel(BoundsInt bounds, string text, Color color)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;

            var grid = service.Map.Grid;
            var min = grid.CellToWorld(bounds.min);
            var max = grid.CellToWorld(bounds.max);
            var top = new Vector3((min.x + max.x) * 0.5f, max.y + 0.15f, (min.z + max.z) * 0.5f);

            footprintLabelStyle ??= new GUIStyle(EditorStyles.whiteBoldLabel) { richText = false };
            footprintLabelStyle.normal.textColor = color;
            Handles.Label(top, text, footprintLabelStyle);
        }

        // ───────────────────────── 동작 ─────────────────────────

        private PlacementResult Execute(MapCommand command)
        {
            var result = service.Execute(command);
            if (result.IsValid) OnMapChanged();
            else ShowFailure(result);
            return result;
        }

        internal void UndoAction()
        {
            // 드래그 도중 Ctrl+Z: 코어가 열린 묶음을 닫고 되돌린다. 이어지는 드래그가 묶음 없이 찍히지 않게 획도 끝낸다.
            EndBrushStroke();
            if (service != null && service.Undo()) OnMapChanged();
        }

        internal void RedoAction()
        {
            EndBrushStroke();
            if (service != null && service.Redo()) OnMapChanged();
        }

        internal void RotateAction()
        {
            if (!IsEditing || mode == Mode.Zone || mode == Mode.Brush) return;

            if (mode == Mode.Spawn)
            {
                // 선택한 스폰을 돌리고, 다음에 놓을 방향도 같이 맞춘다(방금 놓은 지점을 돌리면 다음 지점도 그 방향).
                if (selectedSpawnId != 0 && service.Map.TryGetSpawn(selectedSpawnId, out var spawn))
                {
                    var rotated = spawn.Rotation.Clockwise();
                    if (Execute(new ModifySpawnCommand(spawn.WithRotation(rotated))).IsValid) spawnRotation = rotated;
                }
                else
                {
                    spawnRotation = spawnRotation.Clockwise();
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
            else if (mode == Mode.Place)
            {
                placeRotation = placeRotation.Clockwise();
                Repaint();
                SceneView.RepaintAll();
            }
            else if (selection.Count > 1)
            {
                // 묶음 회전은 회전 축과 겹침 처리를 정해야 해서 아직 막아 둔다.
                ShowFailure(PlacementResult.Fail(PlacementCodes.NothingToDo, "여러 개를 선택했을 때는 회전할 수 없습니다."));
            }
            else if (SelectedId != 0 && service.Map.TryGet(SelectedId, out var placed))
            {
                Execute(new MoveCommand(SelectedId, placed.Cell, placed.Rotation.Clockwise()));
            }
        }

        internal void DeleteAction()
        {
            if (!IsEditing) return;

            if (mode == Mode.Zone)
            {
                if (selectedZoneId != 0 && Execute(new RemoveZoneCommand(selectedZoneId)).IsValid) selectedZoneId = 0;
                return;
            }

            if (mode == Mode.Spawn)
            {
                if (selectedSpawnId != 0 && Execute(new RemoveSpawnCommand(selectedSpawnId)).IsValid) selectedSpawnId = 0;
                return;
            }

            if (mode != Mode.Select || selection.Count == 0) return;
            if (Execute(new RemoveManyCommand(selection)).IsValid) selection.Clear();
        }

        private void OnMapChanged()
        {
            status = null;
            EditorUtility.SetDirty(map);
            if (view?.Root != null) EditorSceneManager.MarkSceneDirty(view.Root.gameObject.scene);
            selection.RemoveAll(id => !service.Map.TryGet(id, out _));
            if (selectedZoneId != 0 && !service.Map.TryGetZone(selectedZoneId, out _)) selectedZoneId = 0;
            if (selectedSpawnId != 0 && !service.Map.TryGetSpawn(selectedSpawnId, out _)) selectedSpawnId = 0;
            InvalidateValidation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ShowFailure(PlacementResult result)
        {
            status = result.Message;
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(result.Message), 1.5f);
            Repaint();
        }

        private void Save()
        {
            AssetDatabase.SaveAssetIfDirty(map);
            status = "맵을 저장했습니다. 씬 오브젝트는 씬을 저장해야 남습니다.";
        }
    }
}
