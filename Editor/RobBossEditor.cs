using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class RobBossEditor : EditorWindow {

    static RobBossEditor _window;
    static RobBossEditor window {
        get {
            if (_window == null) {
                _window = (RobBossEditor)EditorWindow.GetWindow(
                    typeof(RobBossEditor), 
                    false, 
                    "R. Boss Painter", 
                    true
                );
            }
            return _window;
        }
    }

    public MeshRenderer paintTarget;
    public List<Texture2D> undoTextures = new List<Texture2D>();
    public List<Mesh> undoMeshes = new List<Mesh>();
    public int canvasID = 0;
    public string[] canvasNames = new string[0];
    static string canvasName { 
        get { 
            if (window.canvasNames.Length == 0) UpdateCanvasNames();
            if (window.canvasNames.Length == 0) return "Vertex";
            return window.canvasNames[window.canvasID];
        }
    }

    enum PressureType { Opacity = 1, Size = 2 }
    static PressureType pressureType = (PressureType)0;
    enum PaintType { Normal, Directional, Add, Subtract, Multiply }
    static PaintType paintType = PaintType.Normal;

    static bool painting = false;
    static bool hasPaintTarget {
        get { return window.paintTarget != null; }
    }

    static Color color = Color.white;
    static AnimationCurve falloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
    static float radius = 0.5f;
    static float blend = 0.1f;

    static MeshCollider raycastTarget;
    static Mesh colliderMesh;
    static Vector2 uv;
    static Vector3 pos;
    static Vector3 norm;

    static Texture2D canvasTexture;
    static Mesh canvasMesh;
    static string canvasMeshName;
    static string canvasPath;
    static RenderTexture _renderCanvas;
    static RenderTexture renderCanvas {
        get {
            if (!hasPaintTarget) return null;
            if (_renderCanvas == null) {
                canvasTexture = window.paintTarget.sharedMaterial.GetTexture(canvasName) as Texture2D;

                if (canvasTexture == null) {
                    _renderCanvas = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
                    _renderCanvas.wrapMode = TextureWrapMode.Repeat;
                    _renderCanvas.Create();
                    canvasPath = null;
                    Graphics.Blit(Texture2D.whiteTexture, _renderCanvas);
                }
                else {
                    _renderCanvas = new RenderTexture(canvasTexture.width, canvasTexture.height, 0, RenderTextureFormat.ARGB32);
                    _renderCanvas.wrapMode = canvasTexture.wrapMode;
                    _renderCanvas.Create();
                    canvasPath = Path.Combine(Directory.GetCurrentDirectory(), AssetDatabase.GetAssetPath(canvasTexture.GetInstanceID()));
                    Graphics.Blit(canvasTexture, _renderCanvas);
                }

                Undo.RecordObject(window.paintTarget, "sets renderCanvas");
                window.paintTarget.sharedMaterial.SetTexture(canvasName, _renderCanvas);
            }
            return _renderCanvas;
        }
    }

    static Material _brushMaterial;
    static Material brushMaterial {
        get {
            if (_brushMaterial == null) {
                _brushMaterial = new Material(Shader.Find("Rob Boss/Brush"));
                _brushMaterial.SetTexture("_Brush", brushTexture);
            }
            return _brushMaterial;
        }
    }

    static Texture2D _brushTexture;
    static Texture2D brushTexture {
        get {
            if (_brushTexture == null) {
                _brushTexture = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                Color[] colors = new Color[1024];
                for (int i = 0; i < 32; i++) {
                    for (int j = 0; j < 32; j++) {
                        float x = i*0.0625f-1;
                        float y = j*0.0625f-1;
                        float a = Mathf.Clamp01(1-Mathf.Sqrt(x*x+y*y));
                        colors[j*32+i] = new Color(1,1,1,a*a);
                    }
                }
                _brushTexture.SetPixels(colors);
                _brushTexture.Apply();
                _brushTexture.alphaIsTransparency = true;
                _brushTexture.wrapMode = TextureWrapMode.Clamp;
            }
            return _brushTexture;
        }
        set {
            if (value != _brushTexture) {
                _brushTexture = value;
                brushMaterial.SetTexture("_Brush", _brushTexture);
            }
        }
    }

    [MenuItem ("Window/Rob Boss Painter")]
    static void Open () {
        window.minSize = new Vector2(250, 250);
        window.Show();
    }

    static SceneView.OnSceneFunc onSceneFunc;
    void OnEnable () {
        Undo.undoRedoPerformed += UndoRedo;
        EditorSceneManager.sceneClosed += SceneClosed;

        if (onSceneFunc == null) onSceneFunc = new SceneView.OnSceneFunc(OnSceneGUI);

        GameObject g = new GameObject("RobBossTarget");
        g.hideFlags = HideFlags.HideAndDontSave;
        raycastTarget = g.AddComponent<MeshCollider>();
        colliderMesh = new Mesh();
        colliderMesh.name = "RobBossColliderMesh";
        colliderMesh.hideFlags = HideFlags.HideAndDontSave;
        raycastTarget.sharedMesh = colliderMesh;

        pressureType = (PressureType)EditorPrefs.GetInt("RobBoss.PressureType", 0);
        paintType = (PaintType)EditorPrefs.GetInt("RobBoss.PaintType", 0);
        string colorString = EditorPrefs.GetString("RobBoss.Color", "FFFFFFFF");
        ColorUtility.TryParseHtmlString("#" + colorString, out color);
        radius = EditorPrefs.GetFloat("RobBoss.Radius", 0.5f);
        blend = EditorPrefs.GetFloat("RobBoss.Blend", 0.1f);

        if (paintTarget != null) SetPaintTarget(paintTarget);
    }

    void OnDisable () {
        Undo.undoRedoPerformed -= UndoRedo;
        EditorSceneManager.sceneClosed -= SceneClosed;
        if (painting) SceneView.onSceneGUIDelegate -= onSceneFunc;

        DestroyImmediate(colliderMesh);
        DestroyImmediate(raycastTarget.gameObject);

        EditorPrefs.SetInt("RobBoss.PressureType", (int)pressureType);
        EditorPrefs.SetInt("RobBoss.PaintType", (int)paintType);
        EditorPrefs.SetString("RobBoss.Color", ColorUtility.ToHtmlStringRGBA(color));
        EditorPrefs.SetFloat("RobBoss.Radius", radius);
        EditorPrefs.SetFloat("RobBoss.Blend", blend);
    }

    static bool didChange = false;
    void RegisterChange () {
        if (!didChange || !hasPaintTarget) return;
        didChange = false;
        Undo.RecordObject(window, "paints on canavs");
            
        if (canvasName == "Vertex") {
            _prevMesh = CopyMesh();
            undoMeshes.Add(CopyMesh());
        }
        else {
            undoTextures.Add(CopyTexture());
        }
    }

    static Mesh CopyMesh () {
        MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
        Mesh m = Instantiate(f.sharedMesh);
        m.name = canvasMeshName;
        return m;
    }

    static Texture2D CopyTexture () {
        Texture2D t = new Texture2D(renderCanvas.width, renderCanvas.height);
        t.wrapMode = renderCanvas.wrapMode;
        RenderTexture.active = renderCanvas;
        t.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
        t.Apply();
        RenderTexture.active = null;
        return t;
    }

    void UndoRedo () {
        if (paintTarget == null) return;
        
        if (canvasName == "Vertex") {
            int count = undoMeshes.Count;
            MeshFilter f = paintTarget.GetComponent<MeshFilter>();
            if (count > 0) f.sharedMesh = undoMeshes[count-1];
            else if (canvasMesh != null) f.sharedMesh = canvasMesh;
            _prevMesh = null;
        }
        else {
            Graphics.Blit(prevTexture, renderCanvas);
        }
    }

    void SceneClosed(Scene scene) {
        painting = false;
        undoMeshes.Clear();
        undoTextures.Clear();
    }

    void OnGUI () {
        GUI.enabled = true;
        MeshRenderer r = EditorGUILayout.ObjectField("Paint Target", paintTarget, typeof(MeshRenderer), true) as MeshRenderer;
        if (r != paintTarget) SetPaintTarget(r);

        int newCanvasID = EditorGUILayout.Popup("Canvas", canvasID, canvasNames);
        if (newCanvasID != canvasID) {
            if (canvasTexture != null || _renderCanvas != null) ResetRenderCanvas();
            canvasID = newCanvasID;
        }

        pressureType = (PressureType)EditorGUILayout.EnumMaskField("Pressure Type", pressureType);

        paintType = (PaintType)EditorGUILayout.EnumPopup("Paint Type", paintType);
        switch (paintType) {
            case PaintType.Normal:
            case PaintType.Directional:
                brushMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                brushMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                break;
            case PaintType.Add:
                brushMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
                brushMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
                break;
            case PaintType.Subtract:
                brushMaterial.SetFloat("_SrcBlend", (float)BlendMode.OneMinusSrcColor);
                brushMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
                break;
            case PaintType.Multiply:
                brushMaterial.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
                brushMaterial.SetFloat("_DstBlend", (float)BlendMode.Zero);
                break;
        }
        
        if (canvasID == 0) falloff = EditorGUILayout.CurveField("Falloff", falloff, Color.white, Rect.MinMaxRect(0,0,1,1));
        else brushTexture = EditorGUILayout.ObjectField("Brush", brushTexture, typeof(Texture2D), false) as Texture2D;
        color = EditorGUILayout.ColorField("Color", color); 

        string radLabel = (canvasID == 0) ? "Radius (meters)" : "Radius (UV)";  
        radius = Mathf.Max(0, EditorGUILayout.FloatField(radLabel, radius));
        blend = EditorGUILayout.Slider("Blend", blend, 0, 1);

        GUI.enabled = hasPaintTarget;
        if (!painting && GUILayout.Button("Start Painting")) {
            painting = true;
            SceneView.onSceneGUIDelegate += onSceneFunc;
            SetupPainting();
        }
        else if (painting && GUILayout.Button("Stop Painting")) {
            painting = false;
            SceneView.onSceneGUIDelegate -= onSceneFunc;
        }

        if (canvasName == "Vertex") return;

        GUI.enabled = (_renderCanvas != null);
        if (GUILayout.Button("Reset")) {
            ResetCanvas();
        }

        EditorGUILayout.BeginHorizontal();
            GUI.enabled = !string.IsNullOrEmpty(canvasPath);
            if (GUILayout.Button("Save")) {
                Save(canvasPath);
            }
            
            GUI.enabled = (_renderCanvas != null);
            if (GUILayout.Button("Save As")) {
                string name = paintTarget.name + canvasName + ".png";
                string path = EditorUtility.SaveFilePanel("Save texture as PNG", Application.dataPath, name, "png");
                Save(path);
            }
        EditorGUILayout.EndHorizontal();
    }

    static Mesh _prevMesh;
    static Mesh prevMesh {
        get {
            if (_prevMesh == null) _prevMesh = CopyMesh();
            return _prevMesh;
        }
    }

    static Texture2D prevTexture {
        get {
            int count = window.undoTextures.Count;
            if (count > 0) return window.undoTextures[count-1];
            else if (canvasTexture != null) return canvasTexture;
            else return Texture2D.whiteTexture;
        }
    }

    static void SetupPainting () {
        if (window.canvasID == 0) {
            MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
            f.sharedMesh = CopyMesh();
            _prevMesh = CopyMesh();
        }
        else {
            ResetRenderCanvas();
            canvasTexture = window.paintTarget.sharedMaterial.GetTexture(canvasName) as Texture2D;
            window.undoTextures.Clear();
        }
    }

    static void Save (string path) {
        if (string.IsNullOrEmpty(path)) return;
        
        painting = false;
        
        canvasTexture = new Texture2D(renderCanvas.width, renderCanvas.height);
        RenderTexture.active = renderCanvas;
        canvasTexture.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
        canvasTexture.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, canvasTexture.EncodeToPNG());
        AssetDatabase.Refresh();

        canvasPath = path;
        path = Path.GetFullPath(path).Replace(Path.GetFullPath(Application.dataPath), "Assets");
        canvasTexture = AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) as Texture2D;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.isReadable = true;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        ResetRenderCanvas();
    }

    static void ResetCanvas () {
        ResetRenderCanvas();
        window.paintTarget.sharedMaterial.SetTexture(canvasName, canvasTexture);
    }

    static void ResetRenderCanvas () {
        if (_renderCanvas != null) {
            RenderTexture.active = null;
            _renderCanvas.Release();
            _renderCanvas = null;
        }
    }

    public static void OnSceneGUI(SceneView sceneview) {
        if (!painting || !hasPaintTarget) return;
        EventType t = Event.current.type;
        bool canPaint = (t != EventType.MouseUp && t != EventType.Repaint && t != EventType.Layout);
        bool moving = t == EventType.MouseDrag || t == EventType.MouseMove;
        if (canPaint) {
            if (RaycastTarget(moving)) PaintTarget();
            else if (t == EventType.MouseMove) ClearPaint();
        }
        else if (t == EventType.MouseUp) {
            window.RegisterChange();
        }
    }

    void OnSelectionChange () {
        if (UpdateTarget()) Repaint();
    }

    static bool UpdateTarget() {
        if (Selection.activeGameObject == null) return false;
        MeshRenderer r = Selection.activeGameObject.GetComponent<MeshRenderer>();
        if (r != window.paintTarget) {
            SetPaintTarget(r);
            return true;
        }
        return false;
    }

    static void SetPaintTarget (MeshRenderer r) {
        if (r == null) return;

        Undo.RecordObject(window, "sets paint target");
        ResetRenderCanvas();
    
        window.paintTarget = r;
        
        if (painting) SetupPainting();
        UpdateCanvasNames();
        colliderMesh.Clear();

        MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
        if (f != null && f.sharedMesh != null) {
            canvasMeshName = f.sharedMesh.name;
            canvasMesh = f.sharedMesh;
            colliderMesh.vertices = f.sharedMesh.vertices;
            colliderMesh.uv = f.sharedMesh.uv;
            colliderMesh.triangles = f.sharedMesh.triangles;
        }

        raycastTarget.sharedMesh = colliderMesh;
        raycastTarget.transform.position = window.paintTarget.transform.position;
        raycastTarget.transform.rotation = window.paintTarget.transform.rotation;
        raycastTarget.transform.localScale = window.paintTarget.transform.localScale;
    }

    static void UpdateCanvasNames() {
        List<string> names = new List<string>();
        names.Add("Vertex");
        if (!hasPaintTarget) return;
        Shader shader = window.paintTarget.sharedMaterial.shader;
        for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++) {
            if (ShaderUtil.IsShaderPropertyHidden(shader, i)) continue;
            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) {
                names.Add(ShaderUtil.GetPropertyName(shader, i));
            }
        }
        window.canvasNames = names.ToArray();
    }

    static bool RaycastTarget(bool mouseMoved) {
        if (!(EditorWindow.mouseOverWindow is SceneView)) return false; 
        if (raycastTarget == null || !hasPaintTarget) return false;

        raycastTarget.transform.position = window.paintTarget.transform.position;
        raycastTarget.transform.rotation = window.paintTarget.transform.rotation;
        raycastTarget.transform.localScale = window.paintTarget.transform.localScale;
        
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        RaycastHit hit;
        if (raycastTarget.Raycast(ray, out hit, Mathf.Infinity)) {
            pos = window.paintTarget.transform.InverseTransformPoint(hit.point);
            norm = window.paintTarget.transform.InverseTransformDirection(hit.normal);
            if (paintType == PaintType.Directional && mouseMoved) {
                Vector2 dir = ((hit.textureCoord - uv).normalized + Vector2.one) * 0.5f;
                color = new Color(dir.x, dir.y, 0, 1);
                uv = hit.textureCoord;
            }
            else if (paintType != PaintType.Directional) {
                uv = hit.textureCoord;
            }

            if (window.canvasID > 0) {
                Color c = color;
                c.a *= blend;
                brushMaterial.SetColor("_Color", c);
                brushMaterial.SetVector("_Transform", new Vector4(uv.x, uv.y, 0, radius));
            }

            Handles.color = color;
            Handles.DrawLine(hit.point, hit.point + hit.normal * 2);
            Handles.DrawWireDisc(hit.point, hit.normal, radius * window.paintTarget.bounds.extents.y);
            HandleUtility.Repaint();

            return true;
        }
        return false;
    }

    static void PaintTarget () {
        if (!hasPaintTarget) return;

        Event e = Event.current;

        if (e.modifiers != EventModifiers.None) return;
        float pressure = Mathf.Pow(e.pressure, 10);
        if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) {
            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
            e.Use();
            didChange = true;
            if ((int)pressureType == 0) pressure = 1;
        }
        else {
            GUIUtility.hotControl = 0;
            pressure = 1;
        }
    
        if (canvasName == "Vertex") {
            MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
            Mesh m = prevMesh;
            if (!didChange) {
                m = Instantiate(prevMesh);
                m.name = prevMesh.name;
            }

            Vector3[] verts = m.vertices;
            Vector3[] norms = m.normals;
            Color[] colors = m.colors;
            if (colors == null || colors.Length == 0) {
                colors = new Color[canvasMesh.vertexCount];
                for (int i = 0; i < canvasMesh.vertexCount; i++) colors[i] = Color.white;
            }
            
            for (int i = verts.Length-1; i >= 0; i--) {
                if (Vector3.Dot(norms[i], norm) < 0) continue;
                float d = (verts[i] - pos).sqrMagnitude;
                float r = radius * radius;
                if (((int)pressureType & (int)PressureType.Size) == (int)PressureType.Size) r *= pressure;
                if (d > r) continue;
                float b = blend * falloff.Evaluate(d / r);
                if (((int)pressureType & (int)PressureType.Opacity) == (int)PressureType.Opacity) b *= pressure;
                Color newColor = color;
                switch (paintType) {
                    case PaintType.Normal:
                    case PaintType.Directional:
                        newColor = color;
                        break;
                    case PaintType.Add:
                        newColor = colors[i] + color;
                        break;
                    case PaintType.Subtract:
                        newColor = colors[i] - color;
                        break;
                    case PaintType.Multiply:
                        newColor = colors[i] * color;
                        break;
                }
                colors[i] = Color.Lerp(colors[i], newColor, b);
            }

            m.colors = colors;
            f.sharedMesh = m;
        }
        else {
            if (!didChange) Graphics.Blit(prevTexture, renderCanvas, brushMaterial);
            else Graphics.Blit(renderCanvas, renderCanvas, brushMaterial);
        }
    }

    static void ClearPaint () {
        if (canvasName == "Vertex") {
            MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
            f.sharedMesh = Instantiate(prevMesh);
            f.sharedMesh.name = prevMesh.name;
        }
        else Graphics.Blit(prevTexture, renderCanvas);
    }
}
