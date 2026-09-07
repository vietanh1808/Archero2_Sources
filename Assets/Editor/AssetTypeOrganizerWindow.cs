// Đặt file này trong folder Editor (Assets/Editor/AssetTypeOrganizerWindow.cs)
// Gom các file nằm rải rác ở gốc Assets/ vào folder theo đúng loại asset
// (Mesh, GameObject, Material, Texture2D, Sprite, MonoBehaviour, ...).
// Dùng AssetDatabase.MoveAsset nên GUID được giữ nguyên → mọi reference không đứt.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.U2D;

public class AssetTypeOrganizerWindow : EditorWindow
{
    private const string RootFolder = "Assets";

    private struct MoveItem
    {
        public string Source;
        public string Dest;      // đường dẫn đích cuối cùng (đã xử lý trùng tên)
        public string Folder;    // tên folder đích
        public string TypeName;  // loại asset thật do AssetDatabase trả về
        public bool IsFolder;    // folder đi kèm scene
    }

    private readonly List<MoveItem> _plan = new List<MoveItem>();
    private readonly List<string> _skipped = new List<string>();
    private readonly Dictionary<string, int> _byFolder = new Dictionary<string, int>();

    private Vector2 _scroll;
    private bool _moveSceneFolders = true;
    private bool _writeReport = true;
    private bool _batchEditing = true;
    private string _skipNames = "";
    private string _status = "Bấm \"Quét\" để dựng kế hoạch sắp xếp.";

    [MenuItem("Tools/Sắp xếp Assets theo loại")]
    public static void Open()
    {
        var w = GetWindow<AssetTypeOrganizerWindow>("Sắp xếp Assets");
        w.minSize = new Vector2(460, 480);
    }

    // ------------------------------------------------------------ Phân loại

    // Trả về tên folder đích cho 1 asset, null nếu không xác định được
    private static string DestFolderFor(string path, out string typeName)
    {
        typeName = "";
        var t = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (t == null) return null;
        typeName = t.Name;

        // DefaultAsset = folder hoặc loại Unity không hiểu → không đụng tới
        if (t == typeof(DefaultAsset)) return null;

        if (t == typeof(SceneAsset)) return "Scene";
        if (t == typeof(MonoScript)) return "Scripts";

        // Các loại đặc thù phải kiểm tra trước quy tắc ScriptableObject bên dưới
        if (typeof(PlayableAsset).IsAssignableFrom(t)) return "PlayableAsset";
        if (typeof(AudioMixer).IsAssignableFrom(t)) return "AudioMixerController";
        if (typeof(RuntimeAnimatorController).IsAssignableFrom(t)) return "AnimatorController";
        if (typeof(SpriteAtlas).IsAssignableFrom(t)) return "SpriteAtlas";
        if (typeof(RenderTexture).IsAssignableFrom(t)) return "RenderTexture";
        if (typeof(Texture2D).IsAssignableFrom(t)) return "Texture2D";

        // ScriptableObject tự viết (SkeletonDataAsset, config, ...) → gom vào MonoBehaviour
        // cho khớp quy ước sẵn có của project
        if (typeof(ScriptableObject).IsAssignableFrom(t)) return "MonoBehaviour";

        // Còn lại: Mesh, GameObject, Material, Sprite, AnimationClip, AudioClip,
        // TextAsset, Shader, Font, Avatar, VideoClip, ShaderVariantCollection...
        // tên class trùng luôn tên folder đang dùng trong project
        return t.Name;
    }

    // Trùng tên trong folder đích → thêm hậu tố _1, _2... (giống quy ước _0 sẵn có)
    private static string UniqueDestPath(string folder, string fileName, HashSet<string> taken)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        string path = $"{folder}/{baseName}{ext}";

        int i = 1;
        while (taken.Contains(path.ToLowerInvariant()) || File.Exists(path) || Directory.Exists(path))
            path = $"{folder}/{baseName}_{i++}{ext}";

        taken.Add(path.ToLowerInvariant());
        return path;
    }

    // --------------------------------------------------------------- Quét

    private void Scan()
    {
        _plan.Clear();
        _skipped.Clear();
        _byFolder.Clear();

        var skip = new HashSet<string>(
            _skipNames.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
            System.StringComparer.OrdinalIgnoreCase);

        // Tên đã bị chiếm ở từng folder đích (để đánh hậu tố khi trùng)
        var taken = new Dictionary<string, HashSet<string>>();
        // Folder cùng tên với scene ở gốc → đi theo scene
        var sceneFolders = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        var files = Directory.GetFiles(RootFolder, "*", SearchOption.TopDirectoryOnly)
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !p.EndsWith(".meta"))
            .OrderBy(p => p)
            .ToList();

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);

                if (i % 200 == 0)
                    EditorUtility.DisplayProgressBar("Phân loại asset", name, (float)i / files.Count);

                if (skip.Contains(name)) { _skipped.Add($"{name}  (trong danh sách bỏ qua)"); continue; }

                string typeName;
                string folderName = DestFolderFor(path, out typeName);
                if (string.IsNullOrEmpty(folderName))
                {
                    _skipped.Add($"{name}  (không xác định được loại: {typeName})");
                    continue;
                }

                string destFolder = $"{RootFolder}/{folderName}";

                HashSet<string> set;
                if (!taken.TryGetValue(destFolder, out set))
                {
                    set = new HashSet<string>();
                    if (Directory.Exists(destFolder))
                        foreach (var existing in Directory.GetFiles(destFolder, "*", SearchOption.TopDirectoryOnly))
                            set.Add(existing.Replace('\\', '/').ToLowerInvariant());
                    taken[destFolder] = set;
                }

                var item = new MoveItem
                {
                    Source = path,
                    Dest = UniqueDestPath(destFolder, name, set),
                    Folder = folderName,
                    TypeName = typeName,
                };
                _plan.Add(item);

                // Scene kéo theo folder cùng tên (lightmap / NavMesh / asset riêng của scene)
                if (folderName == "Scene" && _moveSceneFolders)
                {
                    string sib = $"{RootFolder}/{Path.GetFileNameWithoutExtension(name)}";
                    if (Directory.Exists(sib))
                        sceneFolders[sib] = $"{destFolder}/{Path.GetFileNameWithoutExtension(item.Dest)}";
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        foreach (var kv in sceneFolders)
        {
            _plan.Add(new MoveItem
            {
                Source = kv.Key,
                Dest = kv.Value,
                Folder = "Scene",
                TypeName = "Folder của scene",
                IsFolder = true,
            });
        }

        foreach (var item in _plan)
        {
            int c;
            _byFolder.TryGetValue(item.Folder, out c);
            _byFolder[item.Folder] = c + 1;
        }

        _status = $"Tìm thấy {_plan.Count} mục cần chuyển, {_skipped.Count} mục bỏ qua.";
        Repaint();
    }

    // ------------------------------------------------------------ Di chuyển

    private void Execute()
    {
        if (_plan.Count == 0) return;

        int ok = 0, fail = 0;
        var errors = new List<string>();
        var report = new List<string> { "type,source,dest,result" };

        // Tạo trước các folder đích
        foreach (var folder in _byFolder.Keys.ToList())
            EnsureFolder($"{RootFolder}/{folder}");
        AssetDatabase.Refresh();

        if (_batchEditing) AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < _plan.Count; i++)
            {
                var item = _plan[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                        $"Đang chuyển ({i + 1}/{_plan.Count})",
                        $"{item.Source} → {item.Dest}", (float)i / _plan.Count))
                    break;

                string err = AssetDatabase.MoveAsset(item.Source, item.Dest);
                bool moved = string.IsNullOrEmpty(err);
                if (moved) ok++;
                else
                {
                    fail++;
                    if (errors.Count < 20) errors.Add($"{item.Source}: {err}");
                }

                if (_writeReport)
                    report.Add($"{item.TypeName},{item.Source},{item.Dest},{(moved ? "ok" : err.Replace(',', ';'))}");
            }
        }
        finally
        {
            if (_batchEditing) AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (_writeReport)
        {
            string reportPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".", "AssetOrganizeReport.csv");
            File.WriteAllLines(reportPath, report);
            Debug.Log($"[Sắp xếp Assets] Báo cáo: {reportPath}");
        }

        foreach (var e in errors) Debug.LogError($"[Sắp xếp Assets] {e}");
        Debug.Log($"[Sắp xếp Assets] Xong. Thành công: {ok}, lỗi: {fail}.");
        EditorUtility.DisplayDialog("Sắp xếp Assets theo loại",
            $"Đã chuyển: {ok}\nLỗi: {fail}\n\nXem Console để biết chi tiết.", "OK");

        Scan(); // quét lại để thấy phần còn sót
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // ----------------------------------------------------------------- GUI

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Gom file rời ở gốc Assets/ vào folder theo loại asset thật (AssetDatabase), " +
            "ví dụ .prefab → GameObject/, mesh .asset → Mesh/, sprite .asset → Sprite/, .png → Texture2D/.\n" +
            "Dùng AssetDatabase.MoveAsset nên GUID giữ nguyên, reference trong prefab/scene không đứt.\n" +
            "Trùng tên ở folder đích sẽ được thêm hậu tố _1, _2...",
            MessageType.Info);

        _moveSceneFolders = EditorGUILayout.Toggle(
            new GUIContent("Chuyển folder kèm scene", "Folder cùng tên với file .unity sẽ đi theo scene"),
            _moveSceneFolders);
        _writeReport = EditorGUILayout.Toggle(
            new GUIContent("Ghi báo cáo CSV", "AssetOrganizeReport.csv cạnh folder Assets"), _writeReport);
        _batchEditing = EditorGUILayout.Toggle(
            new GUIContent("Gộp batch (nhanh hơn)",
                "Bọc trong Start/StopAssetEditing. Nếu Unity báo lỗi move hàng loạt thì tắt đi chạy lại"),
            _batchEditing);
        _skipNames = EditorGUILayout.TextField(
            new GUIContent("Bỏ qua (tên file)", "Ngăn cách bằng dấu phẩy, ví dụ: catalog.json, clientversion.json"),
            _skipNames);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Quét (xem trước)", GUILayout.Height(30)))
            Scan();

        GUI.enabled = _plan.Count > 0;
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.75f, 0.6f);
        if (GUILayout.Button($"Thực hiện di chuyển ({_plan.Count})", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Xác nhận",
                    $"Sẽ chuyển {_plan.Count} mục trong Assets/ vào các folder theo loại.\n" +
                    "Nên commit git trước khi chạy. Tiếp tục?", "Chuyển", "Huỷ"))
                Execute();
        }
        GUI.backgroundColor = old;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(_status, EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (_byFolder.Count > 0)
        {
            EditorGUILayout.LabelField("Kế hoạch theo folder đích", EditorStyles.boldLabel);
            foreach (var kv in _byFolder.OrderByDescending(k => k.Value))
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                bool exists = AssetDatabase.IsValidFolder($"{RootFolder}/{kv.Key}");
                EditorGUILayout.LabelField($"Assets/{kv.Key}", GUILayout.Width(220));
                EditorGUILayout.LabelField(kv.Value.ToString(), GUILayout.Width(60));
                EditorGUILayout.LabelField(exists ? "" : "(tạo mới)");
                EditorGUILayout.EndHorizontal();
            }
        }

        if (_plan.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Ví dụ ({Mathf.Min(30, _plan.Count)}/{_plan.Count} mục)",
                EditorStyles.boldLabel);
            foreach (var item in _plan.Take(30))
                EditorGUILayout.LabelField($"[{item.TypeName}] {item.Source}  →  {item.Dest}",
                    EditorStyles.miniLabel);
        }

        if (_skipped.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Bỏ qua ({_skipped.Count})", EditorStyles.boldLabel);
            foreach (var s in _skipped.Take(30))
                EditorGUILayout.LabelField(s, EditorStyles.miniLabel);
        }

        EditorGUILayout.EndScrollView();
    }
}
