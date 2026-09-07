// Đặt file này trong folder Editor (ví dụ: Assets/Editor/SpriteToTextureWindow.cs)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public class SpriteToTextureWindow : EditorWindow
{
    private Vector2 _scroll;
    private string _outputFolder = "Assets/ExtractedTextures";
    private bool _useSpriteName = true;
    private bool _powerOfTwo = false;
    private bool _overwrite = true;
    private bool _onlyConvertChecked = true;
    private bool _lockList = false;
    private string _search = "";

    private readonly List<Sprite> _sprites = new List<Sprite>();
    private readonly HashSet<Sprite> _checked = new HashSet<Sprite>();
    private readonly List<Object> _sources = new List<Object>();

    // Kết quả của chức năng "Tìm atlas"
    private readonly List<Object> _atlasResults = new List<Object>();
    private string _atlasResultLabel = "";

    // Kết quả của chức năng "Texture → Sprite asset"
    private readonly List<Sprite> _spriteResults = new List<Sprite>();
    private string _spriteResultLabel = "";
    private Vector2 _spriteResultScroll;
    private Texture2D _textureProbe;       // texture đang soi (tự set khi click texture trong Project)
    private bool _deepScanSprites = true;  // quét toàn project nếu texture không có sprite con

    // Index atlas của project (dựng 1 lần, bấm "Quét lại atlas" để làm mới)
    private Dictionary<string, List<SpriteAtlas>> _atlasBySheetPath;
    private List<KeyValuePair<string, SpriteAtlas>> _atlasFolders;
    private List<SpriteAtlas> _allAtlases;

    // Khi ta chủ động đổi Selection (để trỏ tới sprite trong Project),
    // bỏ qua 1 lần OnSelectionChange để không mất danh sách sprite của atlas.
    private bool _suppressSelectionChange;

    [MenuItem("Tools/Sprite → Texture2D")]
    public static void Open()
    {
        var w = GetWindow<SpriteToTextureWindow>("Sprite → Texture2D");
        w.minSize = new Vector2(420, 520);
        w.RefreshSelection();
    }

    private void OnSelectionChange()
    {
        if (_suppressSelectionChange)
        {
            _suppressSelectionChange = false;
            Repaint();
            return;
        }

        RefreshSelection();
        Repaint();
    }

    // ---------------------------------------------------------------- Collect

    private void RefreshSelection()
    {
        if (_lockList) return;

        _sprites.Clear();
        _checked.Clear();
        _sources.Clear();

        var seen = new HashSet<Sprite>();
        foreach (var obj in Selection.objects)
            Collect(obj, seen, 0);

        _checked.UnionWith(_sprites);

        // Click vào 1 texture (hoặc 1 sprite) trong Project → nạp sẵn texture để bấm "Tìm sprite"
        var picked = Selection.objects.OfType<Texture2D>().FirstOrDefault()
                     ?? Selection.objects.OfType<Sprite>().Select(s => s.texture).FirstOrDefault();
        if (picked != null && picked != _textureProbe)
        {
            _textureProbe = picked;
            _spriteResults.Clear();
            _spriteResultLabel = "";
        }
    }

    private void Collect(Object obj, HashSet<Sprite> seen, int depth)
    {
        Collect(obj, seen, depth, _sprites, _sources);
    }

    // sources = null khi chỉ muốn gom sprite mà không đụng tới panel "Nguồn"
    private static void Collect(Object obj, HashSet<Sprite> seen, int depth,
        List<Sprite> output, List<Object> sources)
    {
        if (obj == null || depth > 4) return;

        if (obj is Sprite sp)
        {
            if (seen.Add(sp)) output.Add(sp);
            return;
        }

        if (obj is Texture2D)
        {
            // Texture atlas: lấy tất cả sprite con được cắt trong nó
            if (sources != null && !sources.Contains(obj)) sources.Add(obj);
            AddSpritesAtPath(AssetDatabase.GetAssetPath(obj), seen, output);
            return;
        }

        if (obj is SpriteAtlas atlas)
        {
            // Sprite Atlas asset: duyệt các packable (texture / sprite / folder)
            if (sources != null && !sources.Contains(obj)) sources.Add(obj);
            foreach (var packable in atlas.GetPackables())
                Collect(packable, seen, depth + 1, output, sources);
            return;
        }

        // Folder trong Project window
        string path = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
        {
            if (sources != null && !sources.Contains(obj)) sources.Add(obj);
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { path }))
                AddSpritesAtPath(AssetDatabase.GUIDToAssetPath(guid), seen, output);
        }
    }

    private static void AddSpritesAtPath(string path, HashSet<Sprite> seen, List<Sprite> output)
    {
        if (string.IsNullOrEmpty(path)) return;
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            if (sub is Sprite s && seen.Add(s)) output.Add(s);
    }

    private IEnumerable<Sprite> Filtered()
    {
        if (string.IsNullOrEmpty(_search)) return _sprites;
        return _sprites.Where(s => s != null &&
            s.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private List<Sprite> ToConvert()
    {
        var list = Filtered().Where(s => s != null);
        if (_onlyConvertChecked) list = list.Where(s => _checked.Contains(s));
        return list.ToList();
    }

    // ------------------------------------------------------------- Find Atlas

    // Quét toàn bộ Sprite Atlas trong project và ghi nhớ packable của từng atlas
    private void BuildAtlasIndex(bool force = false)
    {
        if (!force && _atlasBySheetPath != null) return;

        _atlasBySheetPath = new Dictionary<string, List<SpriteAtlas>>();
        _atlasFolders = new List<KeyValuePair<string, SpriteAtlas>>();
        _allAtlases = new List<SpriteAtlas>();

        var guids = AssetDatabase.FindAssets("t:SpriteAtlas");
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string atlasPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                EditorUtility.DisplayProgressBar("Quét Sprite Atlas", atlasPath, (float)i / Mathf.Max(1, guids.Length));

                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (atlas == null) continue;
                _allAtlases.Add(atlas);

                foreach (var packable in atlas.GetPackables())
                {
                    if (packable == null) continue;
                    string p = AssetDatabase.GetAssetPath(packable);
                    if (string.IsNullOrEmpty(p)) continue;

                    if (AssetDatabase.IsValidFolder(p))
                    {
                        _atlasFolders.Add(new KeyValuePair<string, SpriteAtlas>(p, atlas));
                        continue;
                    }

                    List<SpriteAtlas> list;
                    if (!_atlasBySheetPath.TryGetValue(p, out list))
                        _atlasBySheetPath[p] = list = new List<SpriteAtlas>();
                    if (!list.Contains(atlas)) list.Add(atlas);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // Trả về các Sprite Atlas chứa sprite này, kèm texture sheet gốc ở cuối
    private List<Object> FindAtlasesFor(Sprite sp)
    {
        var result = new List<Object>();
        if (sp == null) return result;

        BuildAtlasIndex();

        string spritePath = AssetDatabase.GetAssetPath(sp);

        if (!string.IsNullOrEmpty(spritePath))
        {
            // Atlas khai báo trực tiếp texture/sprite này
            List<SpriteAtlas> direct;
            if (_atlasBySheetPath.TryGetValue(spritePath, out direct))
                foreach (var a in direct)
                    if (!result.Contains(a)) result.Add(a);

            // Atlas khai báo folder chứa sprite này
            foreach (var kv in _atlasFolders)
                if (spritePath.StartsWith(kv.Key + "/", System.StringComparison.OrdinalIgnoreCase)
                    && !result.Contains(kv.Value))
                    result.Add(kv.Value);
        }

        // Atlas đã pack: hỏi trực tiếp binding (bắt cả trường hợp packable lồng nhau)
        foreach (var atlas in _allAtlases)
        {
            if (atlas == null || result.Contains(atlas)) continue;
            try { if (atlas.CanBindTo(sp)) result.Add(atlas); }
            catch { /* atlas chưa được pack */ }
        }

        // Texture sheet gốc (atlas dạng texture cắt sẵn) - để cuối làm fallback
        var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
        if (sheet != null && !result.Contains(sheet)) result.Add(sheet);

        return result;
    }

    // Tìm atlas cho các sprite chỉ định rồi trỏ (reference) tới atlas đầu tiên
    private void FindAtlasForSprites(IList<Sprite> sprites)
    {
        _atlasResults.Clear();
        _atlasResultLabel = "";
        if (sprites == null || sprites.Count == 0) return;

        var valid = sprites.Where(s => s != null).ToList();
        if (valid.Count == 0) return;

        foreach (var sp in valid)
            foreach (var o in FindAtlasesFor(sp))
                if (o != null && !_atlasResults.Contains(o)) _atlasResults.Add(o);

        _atlasResultLabel = valid.Count == 1 ? valid[0].name : $"{valid.Count} sprite";

        if (_atlasResults.Count > 0)
            SelectInProject(_atlasResults[0]);
        else
            ShowNotification(new GUIContent($"Không tìm thấy atlas cho {_atlasResultLabel}"));
    }

    private void FindAtlasForSprite(Sprite sp)
    {
        FindAtlasForSprites(new[] { sp });
    }

    // ------------------------------------------------- Texture → Sprite asset

    // Lấy các sprite mà 1 Sprite Atlas đã pack (bản clone, phải huỷ sau khi dùng)
    private static Sprite[] GetPackedSprites(SpriteAtlas atlas)
    {
        try
        {
            int count = atlas.spriteCount;
            if (count <= 0) return null;
            var buffer = new Sprite[count];
            atlas.GetSprites(buffer);
            return buffer;
        }
        catch { return null; } // atlas chưa được pack trong Editor
    }

    // Tìm mọi Sprite asset "trỏ tới" texture này:
    //  1) sprite con được cắt sẵn ngay trong file texture (Multiple sprite mode)
    //  2) nếu texture là trang atlas đã pack → lấy sprite nguồn của các Sprite Atlas đó
    //  3) tuỳ chọn: quét toàn project, so sánh sprite.texture == texture
    private List<Sprite> FindSpritesForTexture(Texture2D tex, bool allowDeepScan)
    {
        var result = new List<Sprite>();
        var seen = new HashSet<Sprite>();
        if (tex == null) return result;

        // 1) Sprite con trong chính file texture
        string texPath = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(texPath))
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(texPath))
                if (sub is Sprite s && seen.Add(s)) result.Add(s);

        // 2) Texture là trang của Sprite Atlas → lấy sprite nguồn của atlas đó
        BuildAtlasIndex();
        foreach (var atlas in _allAtlases)
        {
            if (atlas == null) continue;

            var packed = GetPackedSprites(atlas);
            if (packed == null) continue;

            bool match = packed.Any(p => p != null && p.texture == tex);
            foreach (var p in packed)
                if (p != null) Object.DestroyImmediate(p);

            if (!match) continue;

            // Sprite nguồn (asset thật) mà atlas này pack vào trang texture đó
            Collect(atlas, seen, 0, result, null);
        }

        // 3) Quét toàn project (bắt các sprite asset rời cùng dùng texture này)
        if (result.Count == 0 && allowDeepScan)
        {
            var guids = AssetDatabase.FindAssets("t:Sprite");
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Quét Sprite trong project", p, (float)i / Mathf.Max(1, guids.Length)))
                        break;

                    foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(p))
                        if (sub is Sprite s && s.texture == tex && seen.Add(s)) result.Add(s);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        return result;
    }

    private void FindSpritesForTextureAndShow(Texture2D tex)
    {
        _spriteResults.Clear();
        _spriteResultLabel = "";
        if (tex == null) return;

        _spriteResults.AddRange(FindSpritesForTexture(tex, _deepScanSprites));
        _spriteResultLabel = tex.name;

        if (_spriteResults.Count == 0)
            ShowNotification(new GUIContent($"Không tìm thấy sprite asset nào dùng \"{tex.name}\""));
    }

    // Hoãn hành động làm đổi layout (nạp danh sách, đổi Selection...) ra ngoài OnGUI
    private void Defer(System.Action action)
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            action();
            Repaint();
        };
    }

    // Nạp lại danh sách sprite từ 1 atlas/texture/folder vừa tìm được
    private void LoadFrom(Object obj)
    {
        if (obj == null) return;

        _sprites.Clear();
        _checked.Clear();
        _sources.Clear();

        var seen = new HashSet<Sprite>();
        Collect(obj, seen, 0);
        _checked.UnionWith(_sprites);
        Repaint();
    }

    // Nạp thẳng 1 danh sách sprite (kết quả "Tìm sprite" từ texture) vào danh sách convert
    private void LoadSprites(IList<Sprite> sprites)
    {
        if (sprites == null || sprites.Count == 0) return;

        _sprites.Clear();
        _checked.Clear();
        _sources.Clear();

        var seen = new HashSet<Sprite>();
        foreach (var s in sprites)
            if (s != null && seen.Add(s)) _sprites.Add(s);

        _checked.UnionWith(_sprites);
        Repaint();
    }

    // ------------------------------------------------------- Select in Project

    private void SelectInProject(IList<Object> objs)
    {
        if (objs == null || objs.Count == 0) return;

        _suppressSelectionChange = true;
        Selection.objects = objs.ToArray();
        EditorGUIUtility.PingObject(objs[0]);

        // Mở Project window để thấy sprite con được highlight bên trong atlas
        EditorApplication.ExecuteMenuItem("Window/General/Project");
    }

    private void SelectInProject(Object obj)
    {
        SelectInProject(new[] { obj });
    }

    // -------------------------------------------------------------------- GUI

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cấu hình", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string picked = EditorUtility.OpenFolderPanel("Chọn folder output", Application.dataPath, "");
            if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                _outputFolder = "Assets" + picked.Substring(Application.dataPath.Length);
        }
        EditorGUILayout.EndHorizontal();

        _useSpriteName = EditorGUILayout.Toggle(
            new GUIContent("Đặt tên theo Sprite", "Bỏ chọn để thêm hậu tố _tex"), _useSpriteName);
        _powerOfTwo = EditorGUILayout.Toggle(
            new GUIContent("Pad về Power-of-Two", "Đệm trong suốt cho kích thước POT (cần cho 1 số nén)"), _powerOfTwo);
        _overwrite = EditorGUILayout.Toggle("Ghi đè nếu trùng", _overwrite);
        _onlyConvertChecked = EditorGUILayout.Toggle(
            new GUIContent("Chỉ convert dòng đã tick", "Bỏ chọn để convert toàn bộ danh sách đang hiển thị"),
            _onlyConvertChecked);

        DrawSources();
        DrawTextureProbe();
        DrawSpriteResults();
        DrawAtlasResults();
        DrawToolbar();
        DrawList();

        EditorGUILayout.Space();
        var toConvert = ToConvert();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = toConvert.Count > 0;
        if (GUILayout.Button($"Convert sang Texture2D Asset ({toConvert.Count})", GUILayout.Height(34)))
            Export(toConvert, OutputMode.TextureAsset);
        if (GUILayout.Button(new GUIContent($"Export PNG ({toConvert.Count})",
                "Xuất các sprite đang chọn ra file .png"), GUILayout.Height(34)))
            Export(toConvert, OutputMode.Png);
        EditorGUILayout.EndHorizontal();

        var all = _sprites.Where(s => s != null).ToList();
        GUI.enabled = all.Count > 0;
        if (GUILayout.Button(new GUIContent($"Export TẤT CẢ sprite ra PNG ({all.Count})",
                "Xuất toàn bộ sprite tìm thấy, bỏ qua bộ lọc và tick chọn"), GUILayout.Height(24)))
            Export(all, OutputMode.Png);
        GUI.enabled = true;
    }

    private void DrawSources()
    {
        if (_sources.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Nguồn (atlas / folder)", EditorStyles.boldLabel);
        foreach (var src in _sources)
        {
            if (src == null) continue;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(src, typeof(Object), false);
            if (GUILayout.Button(new GUIContent("Ping", "Trỏ tới file này trong Project"), GUILayout.Width(46)))
                SelectInProject(src);
            if (src is Texture2D srcTex &&
                GUILayout.Button(new GUIContent("Tìm sprite", "Tìm các Sprite asset dùng texture này"),
                    GUILayout.Width(78)))
            {
                _textureProbe = srcTex;
                Defer(() => FindSpritesForTextureAndShow(srcTex));
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawTextureProbe()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Texture → Sprite asset", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _textureProbe = (Texture2D)EditorGUILayout.ObjectField(
            new GUIContent("Texture", "Click 1 texture trong Project để tự điền, hoặc kéo thả vào đây"),
            _textureProbe, typeof(Texture2D), false);

        GUI.enabled = _textureProbe != null;
        if (GUILayout.Button(new GUIContent("Tìm sprite",
                "Tìm mọi Sprite asset trỏ tới texture này (sprite con đã cắt, sprite nguồn của Sprite Atlas)"),
                GUILayout.Width(90)))
        {
            var t = _textureProbe;
            Defer(() => FindSpritesForTextureAndShow(t));
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        _deepScanSprites = EditorGUILayout.Toggle(
            new GUIContent("Quét sâu toàn project",
                "Nếu không tìm thấy sprite con/atlas, quét mọi Sprite trong project và so sánh texture"),
            _deepScanSprites);
    }

    private void DrawSpriteResults()
    {
        if (_spriteResults.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            $"Sprite asset dùng \"{_spriteResultLabel}\" ({_spriteResults.Count})", EditorStyles.boldLabel);

        _spriteResultScroll = EditorGUILayout.BeginScrollView(_spriteResultScroll, GUILayout.MaxHeight(160));
        foreach (var sp in _spriteResults)
        {
            if (sp == null) continue;
            EditorGUILayout.BeginHorizontal();

            var previewRect = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
            DrawSpritePreview(previewRect, sp);

            EditorGUILayout.ObjectField(sp, typeof(Sprite), false);

            if (GUILayout.Button(new GUIContent("Ping", "Trỏ tới sprite này trong Project"), GUILayout.Width(46)))
                SelectInProject(sp);
            if (GUILayout.Button(new GUIContent("Tìm atlas", "Tìm atlas chứa sprite này"), GUILayout.Width(70)))
            {
                var target = sp;
                Defer(() => FindAtlasForSprite(target));
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent($"Select {_spriteResults.Count} sprite trong Project",
                "Chọn toàn bộ sprite vừa tìm được"), EditorStyles.miniButton))
        {
            var picked = _spriteResults.Where(s => s != null).Cast<Object>().ToList();
            SelectInProject(picked);
        }
        if (GUILayout.Button(new GUIContent("Nạp vào danh sách", "Đưa các sprite này xuống danh sách convert"),
                EditorStyles.miniButton, GUILayout.Width(130)))
        {
            var picked = _spriteResults.Where(s => s != null).ToList();
            Defer(() => LoadSprites(picked));
        }
        if (GUILayout.Button(new GUIContent("Xoá kết quả", "Ẩn danh sách sprite vừa tìm"),
                EditorStyles.miniButton, GUILayout.Width(90)))
            Defer(() => { _spriteResults.Clear(); _spriteResultLabel = ""; });
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAtlasResults()
    {
        if (_atlasResults.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Atlas chứa \"{_atlasResultLabel}\" ({_atlasResults.Count})", EditorStyles.boldLabel);

        foreach (var obj in _atlasResults)
        {
            if (obj == null) continue;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(obj is SpriteAtlas ? "Atlas" : "Texture", GUILayout.Width(52));
            EditorGUILayout.ObjectField(obj, typeof(Object), false);
            if (GUILayout.Button(new GUIContent("Ping", "Trỏ tới file này trong Project"), GUILayout.Width(46)))
                SelectInProject(obj);
            if (GUILayout.Button(new GUIContent("Nạp", "Nạp toàn bộ sprite của atlas này vào danh sách"), GUILayout.Width(46)))
            {
                var target = obj;
                Defer(() => LoadFrom(target));
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Xoá kết quả", "Ẩn danh sách atlas vừa tìm"),
                EditorStyles.miniButton, GUILayout.Width(90)))
            Defer(() => _atlasResults.Clear());
        if (GUILayout.Button(new GUIContent("Quét lại atlas", "Dựng lại index Sprite Atlas của project"),
                EditorStyles.miniButton, GUILayout.Width(110)))
            BuildAtlasIndex(true);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Sprite tìm thấy: {_sprites.Count}", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.FlexibleSpace();
        _lockList = GUILayout.Toggle(_lockList,
            new GUIContent("Khoá", "Giữ nguyên danh sách khi đổi selection trong Project"),
            EditorStyles.miniButton, GUILayout.Width(50));
        if (GUILayout.Button(new GUIContent("Làm mới", "Nạp lại từ selection hiện tại"),
                EditorStyles.miniButton, GUILayout.Width(64)))
        {
            bool wasLocked = _lockList;
            _lockList = false;
            RefreshSelection();
            _lockList = wasLocked;
        }
        EditorGUILayout.EndHorizontal();

        _search = EditorGUILayout.TextField("Lọc theo tên", _search);

        var visible = Filtered().Where(s => s != null).ToList();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = visible.Count > 0;
        if (GUILayout.Button(new GUIContent($"Select {visible.Count} sprite trong Project",
                "Chọn các sprite con này trong Project window"), GUILayout.Height(22)))
            SelectInProject(visible.Cast<Object>().ToList());

        if (GUILayout.Button(new GUIContent("Tick tất cả", "Đánh dấu tất cả dòng đang hiển thị"),
                GUILayout.Width(90), GUILayout.Height(22)))
            _checked.UnionWith(visible);

        if (GUILayout.Button(new GUIContent("Bỏ tick", "Bỏ đánh dấu các dòng đang hiển thị"),
                GUILayout.Width(70), GUILayout.Height(22)))
            _checked.ExceptWith(visible);

        if (GUILayout.Button(new GUIContent("Tìm atlas",
                "Tìm atlas chứa các sprite đã tick (hoặc toàn bộ dòng đang hiển thị)"),
                GUILayout.Width(80), GUILayout.Height(22)))
        {
            var picked = visible.Where(s => _checked.Contains(s)).ToList();
            if (picked.Count == 0) picked = visible;
            Defer(() => FindAtlasForSprites(picked));
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawList()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180));

        if (_sprites.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Chọn Sprite, Texture atlas, Sprite Atlas hoặc folder trong Project window.\n" +
                "Khi chọn 1 texture atlas, toàn bộ sprite con sẽ hiện ở đây và có thể select ngược lại trong Project.\n" +
                "Chọn 1 sprite rồi bấm \"Tìm atlas\" để tìm Sprite Atlas / texture sheet chứa nó.\n" +
                "Click 1 texture rồi bấm \"Tìm sprite\" ở mục \"Texture → Sprite asset\" để lấy các Sprite asset trỏ tới texture đó.",
                MessageType.Info);
        }
        else
        {
            foreach (var sp in Filtered())
            {
                if (sp == null) continue;
                DrawSpriteRow(sp);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSpriteRow(Sprite sp)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(38));

        bool isChecked = _checked.Contains(sp);
        bool nowChecked = EditorGUILayout.Toggle(isChecked, GUILayout.Width(18));
        if (nowChecked != isChecked)
        {
            if (nowChecked) _checked.Add(sp);
            else _checked.Remove(sp);
        }

        var previewRect = GUILayoutUtility.GetRect(34, 34, GUILayout.Width(34), GUILayout.Height(34));
        DrawSpritePreview(previewRect, sp);

        EditorGUILayout.ObjectField(sp, typeof(Sprite), false);

        var r = sp.textureRect;
        EditorGUILayout.LabelField($"{(int)r.width}x{(int)r.height}", GUILayout.Width(74));

        if (GUILayout.Button(new GUIContent("Select", "Chọn sprite này trong Project window"), GUILayout.Width(56)))
            SelectInProject(sp);

        if (GUILayout.Button(new GUIContent("Tìm atlas", "Tìm atlas chứa sprite này và trỏ tới atlas đó"),
                GUILayout.Width(70)))
        {
            var target = sp;
            Defer(() => FindAtlasForSprite(target));
        }

        EditorGUILayout.EndHorizontal();

        // Double-click vào cả dòng cũng trỏ tới sprite trong Project
        var rowRect = GUILayoutUtility.GetLastRect();
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && rowRect.Contains(e.mousePosition))
        {
            SelectInProject(sp);
            e.Use();
        }
    }

    // Vẽ preview trực tiếp từ vùng texture của sprite (không cần Read/Write)
    private static void DrawSpritePreview(Rect rect, Sprite sp)
    {
        var tex = sp.texture;
        if (tex == null || Event.current.type != EventType.Repaint) return;

        var tr = sp.textureRect;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

        float aspect = tr.width / Mathf.Max(1f, tr.height);
        float w = rect.width, h = rect.height;
        if (aspect > 1f) h = w / aspect; else w = h * aspect;
        var draw = new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);

        GUI.DrawTextureWithTexCoords(draw, tex, uv, true);
    }

    // ----------------------------------------------------------- Convert/Export

    private enum OutputMode { TextureAsset, Png }

    private void Export(List<Sprite> sprites, OutputMode mode)
    {
        if (!Directory.Exists(_outputFolder))
        {
            Directory.CreateDirectory(_outputFolder);
            AssetDatabase.Refresh();
        }

        bool png = mode == OutputMode.Png;
        string title = png ? "Exporting PNG" : "Converting";

        int ok = 0, fail = 0;
        var madeReadable = new HashSet<string>();

        try
        {
            for (int i = 0; i < sprites.Count; i++)
            {
                var sp = sprites[i];
                EditorUtility.DisplayProgressBar(title, sp.name, (float)i / sprites.Count);

                if (sp.texture == null) { fail++; continue; }

                // Bảo đảm texture nguồn đọc được (tạm bật Read/Write)
                string srcPath = AssetDatabase.GetAssetPath(sp.texture);
                var importer = AssetImporter.GetAtPath(srcPath) as TextureImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    madeReadable.Add(srcPath);
                }

                var tex = ExtractTexture(sp);
                if (tex == null) { fail++; continue; }

                string baseName = _useSpriteName ? sp.name : sp.name + "_tex";
                baseName = SanitizeFileName(baseName);

                if (png)
                {
                    string filePath = $"{_outputFolder}/{baseName}.png";
                    if (!_overwrite)
                        filePath = UniqueFilePath(_outputFolder, baseName, ".png");

                    var bytes = tex.EncodeToPNG();
                    Object.DestroyImmediate(tex);
                    if (bytes == null) { fail++; continue; }

                    File.WriteAllBytes(filePath, bytes);
                }
                else
                {
                    string assetPath = $"{_outputFolder}/{baseName}.asset";
                    if (!_overwrite)
                        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

                    var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (existing != null && _overwrite)
                    {
                        EditorUtility.CopySerialized(tex, existing);
                        Object.DestroyImmediate(tex);
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(tex, assetPath);
                    }
                }
                ok++;
            }
        }
        finally
        {
            // Trả lại trạng thái Read/Write ban đầu
            foreach (var p in madeReadable)
            {
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp != null) { imp.isReadable = false; imp.SaveAndReimport(); }
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string kind = png ? "PNG" : "Texture2D Asset";
        Debug.Log($"[Sprite→{kind}] Hoàn tất. Thành công: {ok}, lỗi: {fail}. Output: {_outputFolder}");
        EditorUtility.DisplayDialog($"Sprite → {kind}",
            $"Thành công: {ok}\nLỗi: {fail}\nFolder: {_outputFolder}", "OK");

        PingOutputFolder();
    }

    private void PingOutputFolder()
    {
        if (!_outputFolder.Replace('\\', '/').StartsWith("Assets")) return;
        var folder = AssetDatabase.LoadAssetAtPath<Object>(_outputFolder);
        if (folder != null) EditorGUIUtility.PingObject(folder);
    }

    private static string UniqueFilePath(string folder, string baseName, string ext)
    {
        string path = $"{folder}/{baseName}{ext}";
        int i = 1;
        while (File.Exists(path))
            path = $"{folder}/{baseName}_{i++}{ext}";
        return path;
    }

    private Texture2D ExtractTexture(Sprite sp)
    {
        var rect = sp.textureRect;
        int w = Mathf.Max(1, (int)rect.width);
        int h = Mathf.Max(1, (int)rect.height);

        Color[] pixels;
        try
        {
            pixels = sp.texture.GetPixels((int)rect.x, (int)rect.y, w, h);
        }
        catch (UnityException e)
        {
            Debug.LogError($"Không đọc được pixel của '{sp.name}': {e.Message}. " +
                           "Bật Read/Write Enabled cho texture nguồn.");
            return null;
        }

        int outW = _powerOfTwo ? Mathf.NextPowerOfTwo(w) : w;
        int outH = _powerOfTwo ? Mathf.NextPowerOfTwo(h) : h;

        var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        tex.name = sp.name;

        if (_powerOfTwo && (outW != w || outH != h))
        {
            var clear = new Color[outW * outH];
            tex.SetPixels(clear);                 // nền trong suốt
            tex.SetPixels(0, 0, w, h, pixels);    // dán sprite vào góc dưới-trái
        }
        else
        {
            tex.SetPixels(pixels);
        }
        tex.Apply();
        return tex;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
