using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

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

    public Dictionary<string, TextureDimension> canvasDimensions = new Dictionary<string, TextureDimension>();
    static TextureDimension canvasDimension { 
        get { 
            if (canvasName == "Vertex" || window == null || !window.canvasDimensions.ContainsKey(canvasName)) return TextureDimension.None;
            return window.canvasDimensions[canvasName];
        }
    }

    static bool painting = false;
    static bool hasPaintTarget {
        get { return window.paintTarget != null; }
    }

    static bool directional = false;
    static Color color = Color.white;
    static float radius = 0.5f;
    static float blend = 0.1f;

    static MeshCollider raycastTarget;
    static Mesh colliderMesh;
    static Vector2 uv;
    static Vector3 pos;
    static Vector3 norm;

    static Texture canvasTexture;
    static Mesh canvasMesh;
    static string canvasMeshName;
    static string canvasPath;
    static RenderTexture _renderCanvas;
    static RenderTexture renderCanvas {
        get {
            if (!hasPaintTarget) return null;
            if (_renderCanvas == null) {
                canvasTexture = window.paintTarget.sharedMaterial.GetTexture(canvasName);

                if (canvasTexture == null) {
                    int px = 2048;
                    if (canvasDimension == TextureDimension.Cube) px = 256;
                    _renderCanvas = new RenderTexture(px, px, 0, RenderTextureFormat.ARGB32);
                    _renderCanvas.dimension = canvasDimension;
                    _renderCanvas.Create();
                    canvasPath = null;
                    Graphics.Blit(Texture2D.whiteTexture, _renderCanvas);
                }
                else {
                    _renderCanvas = new RenderTexture(canvasTexture.width, canvasTexture.height, 0, RenderTextureFormat.ARGB32);
                    _renderCanvas.dimension = canvasDimension;
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
        if (onSceneFunc == null) onSceneFunc = new SceneView.OnSceneFunc(OnSceneGUI);

        GameObject g = new GameObject("RobBossTarget");
        g.hideFlags = HideFlags.HideAndDontSave;
        raycastTarget = g.AddComponent<MeshCollider>();
        colliderMesh = new Mesh();
        colliderMesh.name = "RobBossColliderMesh";
        colliderMesh.hideFlags = HideFlags.HideAndDontSave;
        raycastTarget.sharedMesh = colliderMesh;

        directional = EditorPrefs.GetInt("RobBoss.Directional", 0) == 1;
        string colorString = EditorPrefs.GetString("RobBoss.Color", "FFFFFFFF");
        ColorUtility.TryParseHtmlString("#" + colorString, out color);
        radius = EditorPrefs.GetFloat("RobBoss.Radius", 0.5f);
        blend = EditorPrefs.GetFloat("RobBoss.Blend", 0.1f);

        if (paintTarget != null) SetPaintTarget(paintTarget);
    }

    void OnDisable () {
        Undo.undoRedoPerformed -= UndoRedo;
        if (painting) SceneView.onSceneGUIDelegate -= onSceneFunc;

        DestroyImmediate(colliderMesh);
        DestroyImmediate(raycastTarget.gameObject);

        EditorPrefs.SetInt("RobBoss.Directional", directional ? 1 : 0);
        EditorPrefs.SetString("RobBoss.Color", ColorUtility.ToHtmlStringRGBA(color));
        EditorPrefs.SetFloat("RobBoss.Radius", radius);
        EditorPrefs.SetFloat("RobBoss.Blend", blend);
    }

    static bool didChange = false;
    void RegisterChange () {
        if (!didChange) return;
        didChange = false;
        Undo.RecordObject(window, "paints on canavs");
            
        if (canvasName == "Vertex") {
            MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
            Mesh undoMesh = Instantiate(f.sharedMesh);
            undoMesh.name = canvasMeshName;
            undoMeshes.Add(undoMesh);
        }
        else {
            Texture2D undoTex = new Texture2D(renderCanvas.width, renderCanvas.height);
            RenderTexture.active = renderCanvas;
            undoTex.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
            undoTex.Apply();
            RenderTexture.active = null;
            undoTextures.Add(undoTex);
        }
    }

    void UndoRedo () {
        if (paintTarget == null) return;

        if (canvasName == "Vertex") {
            int count = undoMeshes.Count;
            MeshFilter f = paintTarget.GetComponent<MeshFilter>();
            if (count > 0) f.sharedMesh = undoMeshes[count-1];
            else if (canvasMesh != null) f.sharedMesh = canvasMesh;
        }
        else {
            int count = undoTextures.Count;
            if (count > 0) Graphics.Blit(undoTextures[count-1], renderCanvas);
            else if (canvasTexture != null) Graphics.Blit(canvasTexture, renderCanvas);
            else Graphics.Blit(Texture2D.whiteTexture, renderCanvas);
        }
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
        
        directional = EditorGUILayout.Toggle("Directional", directional);
        if (canvasID > 0) brushTexture = EditorGUILayout.ObjectField("Brush", brushTexture, typeof(Texture2D), false) as Texture2D;
        color = EditorGUILayout.ColorField("Color", color);	

        string radLabel = (canvasID == 0) ? "Radius (meters)" : "Radius (UV)";	
        radius = Mathf.Max(0, EditorGUILayout.FloatField(radLabel, radius));
        blend = EditorGUILayout.Slider("Blend", blend, 0, 1);

        GUI.enabled = hasPaintTarget;
        if (!painting && GUILayout.Button("Start Painting")) {
            painting = true;
            SceneView.onSceneGUIDelegate += onSceneFunc;
            if (canvasID == 0) {
                MeshFilter f = paintTarget.GetComponent<MeshFilter>();
                f.sharedMesh = Instantiate(f.sharedMesh);
                f.sharedMesh.name = canvasMeshName;
            }
            else {
                Texture tex = paintTarget.sharedMaterial.GetTexture(canvasName);
                if (_renderCanvas == null || tex == null || _renderCanvas.GetInstanceID() != tex.GetInstanceID()) {
                    ResetRenderCanvas();
                }
            }
        }
        else if (painting && GUILayout.Button("Stop Painting")) {
            painting = false;
            SceneView.onSceneGUIDelegate -= onSceneFunc;
        }

        if (canvasName == "Vertex") return;

        GUI.enabled = (_renderCanvas != null);
        if (GUILayout.Button("Reset")) {
            ResetRenderCanvas();
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

    static void Save (string path) {
        if (string.IsNullOrEmpty(path)) return;
        
        painting = false;
        
        Texture2D canvasTexture2D = new Texture2D(renderCanvas.width, renderCanvas.height);
        RenderTexture.active = renderCanvas;
        canvasTexture2D.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
        canvasTexture2D.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, canvasTexture2D.EncodeToPNG());
        AssetDatabase.Refresh();

        canvasPath = path;
        path = Path.GetFullPath(path).Replace(Path.GetFullPath(Application.dataPath), "Assets");
        canvasTexture2D = AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) as Texture2D;
        canvasTexture = canvasTexture2D as Texture;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.isReadable = true;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        ResetRenderCanvas();
    }

    static void ResetRenderCanvas () {
        if (_renderCanvas != null) {
            _renderCanvas.Release();
            _renderCanvas = null;
        }

        Undo.RecordObject(window.paintTarget, "sets canvas");
        if (canvasTexture != null && canvasTexture.dimension == canvasDimension) {
            window.paintTarget.sharedMaterial.SetTexture(canvasName, canvasTexture);
            canvasTexture = null;
            canvasPath = null;
        }
    }

    public static void OnSceneGUI(SceneView sceneview) {
        EventType t = Event.current.type;
        if (painting && t != EventType.MouseUp && RaycastTarget(t == EventType.MouseDrag || t == EventType.MouseMove)) {
            PaintTarget();
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
        window.paintTarget = r;
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
        window.canvasDimensions.Clear();
        List<string> names = new List<string>();
        names.Add("Vertex");
        if (window.paintTarget == null) return;
        Shader shader = window.paintTarget.sharedMaterial.shader;
        for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++) {
            if (ShaderUtil.IsShaderPropertyHidden(shader, i)) continue;
            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) {
                string name = ShaderUtil.GetPropertyName(shader, i);
                names.Add(name);
                window.canvasDimensions[name] = ShaderUtil.GetTexDim(shader, i);
            }
        }
        window.canvasNames = names.ToArray();
    }

    static bool RaycastTarget(bool mouseMoved) {	
        if (raycastTarget == null || window.paintTarget == null) return false;

        raycastTarget.transform.position = window.paintTarget.transform.position;
        raycastTarget.transform.rotation = window.paintTarget.transform.rotation;
        raycastTarget.transform.localScale = window.paintTarget.transform.localScale;
        
        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        RaycastHit hit;
        if (raycastTarget.Raycast(ray, out hit, Mathf.Infinity)) {
            pos = window.paintTarget.transform.InverseTransformPoint(hit.point);
            norm = window.paintTarget.transform.InverseTransformDirection(hit.normal);
            if (directional && mouseMoved) {
                Vector2 dir = ((hit.textureCoord - uv).normalized + Vector2.one) * 0.5f;
                color = new Color(dir.x, dir.y, 0, 1);
                uv = hit.textureCoord;
            }
            else if (!directional) {
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
        
        if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) {
            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
            if (canvasName == "Vertex") {
                MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
                Mesh m = f.sharedMesh;
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
                    if (d > radius * radius) continue;
                    colors[i] = Color.Lerp(colors[i], color, blend);
                }

                m.colors = colors;
                f.sharedMesh = m;
            }
            else {
                Graphics.Blit(renderCanvas, renderCanvas, brushMaterial);
            }

            e.Use();
            didChange = true;
        }
        else {
            GUIUtility.hotControl = 0;
        }
    }
}
