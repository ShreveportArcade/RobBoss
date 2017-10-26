using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

	public Renderer paintTarget;
	public List<Texture2D> undoTextures = new List<Texture2D>();
	public int canvasID = 0;
	public string[] canvasNames = new string[0];
	static string canvasName { 
		get { 
			if (window.canvasNames.Length == 0) UpdateCanvasNames();
			if (window.canvasNames.Length == 0) return "_MainTex";
			return window.canvasNames[window.canvasID];
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

	static Texture2D canvas;
	static string canvasPath;
	static RenderTexture _renderCanvas;
	static RenderTexture renderCanvas {
		get {
			if (!hasPaintTarget) return null;
			if (_renderCanvas == null) {
				canvas = window.paintTarget.sharedMaterial.GetTexture(canvasName) as Texture2D;
		    	if (canvas == null) {
					_renderCanvas = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
					_renderCanvas.Create();
					canvasPath = null;
					Graphics.Blit(Texture2D.whiteTexture, _renderCanvas);
			    }
			    else {
					_renderCanvas = new RenderTexture(canvas.width, canvas.height, 0, RenderTextureFormat.ARGB32);
					_renderCanvas.Create();
					canvasPath = Path.Combine(Directory.GetCurrentDirectory(), AssetDatabase.GetAssetPath(canvas.GetInstanceID()));
					Graphics.Blit(canvas, _renderCanvas);
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
						colors[(int)(j*32+i)] = new Color(1,1,1,a*a);
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
		window.minSize = new Vector2(250, 360);
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

		int selectionID = EditorPrefs.GetInt("RobBoss.SelectionID", -1);
		SetPaintTarget(EditorUtility.InstanceIDToObject(selectionID) as Renderer);
		
		directional = EditorPrefs.GetInt("RobBoss.Directional", 0) == 1;
		string colorString = EditorPrefs.GetString("RobBoss.Color", "#FFFFFFFF");
		ColorUtility.TryParseHtmlString(colorString, out color);
		radius = EditorPrefs.GetFloat("RobBoss.Radius", 0.5f);
		blend = EditorPrefs.GetFloat("RobBoss.Blend", 0.1f);		
	}

    void OnDisable () {
		Undo.undoRedoPerformed -= UndoRedo;
    	if (painting) SceneView.onSceneGUIDelegate -= onSceneFunc;

		DestroyImmediate(colliderMesh);
		DestroyImmediate(raycastTarget.gameObject);

		if (hasPaintTarget) EditorPrefs.SetInt("RobBoss.SelectionID", paintTarget.GetInstanceID());
		EditorPrefs.SetInt("RobBoss.Directional", directional ? 1 : 0);
		EditorPrefs.SetString("RobBoss.Color", ColorUtility.ToHtmlStringRGBA(color));
		EditorPrefs.SetFloat("RobBoss.Radius", radius);
		EditorPrefs.SetFloat("RobBoss.Blend", blend);
    }

	static bool didChange = false;
	void RegisterChange () {
		if (!didChange) return;
		didChange = false;

		Texture2D undoTex = new Texture2D(renderCanvas.width, renderCanvas.height);
		RenderTexture.active = renderCanvas;
		undoTex.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
		undoTex.Apply();
		RenderTexture.active = null;

		Undo.RecordObject(window, "paints on canavs");
		undoTextures.Add(undoTex);
	}

	void UndoRedo () {
		int count = undoTextures.Count;
		if (count > 0) Graphics.Blit(undoTextures[count-1], renderCanvas);
		else if (canvas != null) Graphics.Blit(canvas, renderCanvas);
		else Graphics.Blit(Texture2D.whiteTexture, renderCanvas);
	}

	void OnGUI () {
		GUI.enabled = true;
		Renderer r = EditorGUILayout.ObjectField("Paint Target", paintTarget, typeof(Renderer), true) as Renderer;
		if (r != paintTarget) SetPaintTarget(r);

		int newCanvasID = EditorGUILayout.Popup("Canvas", canvasID, canvasNames);
		if (newCanvasID != canvasID) {
			if (canvas != null || _renderCanvas != null) ResetCanvas();
			canvasID = newCanvasID;
		}
		
		directional = EditorGUILayout.Toggle("Directional", directional);
		brushTexture = EditorGUILayout.ObjectField("Brush", brushTexture, typeof(Texture2D), false) as Texture2D;
		color = EditorGUILayout.ColorField("Color", color);		
		radius = EditorGUILayout.FloatField("Radius", radius);
		blend = EditorGUILayout.Slider("Blend", blend, 0, 1);

		GUI.enabled = hasPaintTarget;
		if (!painting && GUILayout.Button("Start Painting")) {
			painting = true;
			SceneView.onSceneGUIDelegate += onSceneFunc;
			Texture tex = paintTarget.sharedMaterial.GetTexture(canvasName);
			if (_renderCanvas != null && _renderCanvas.GetInstanceID() != tex.GetInstanceID()) {
				ResetCanvas();
			}
		}
		else if (painting && GUILayout.Button("Stop Painting")) {
			painting = false;
			SceneView.onSceneGUIDelegate -= onSceneFunc;
		}

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

	static void Save (string path) {
		if (string.IsNullOrEmpty(path)) return;
		
		painting = false;
		
		canvas = new Texture2D(renderCanvas.width, renderCanvas.height);
		RenderTexture.active = renderCanvas;
		canvas.ReadPixels(new Rect(0, 0, renderCanvas.width, renderCanvas.height), 0, 0);
		canvas.Apply();
		RenderTexture.active = null;
		File.WriteAllBytes(path, canvas.EncodeToPNG());
		AssetDatabase.Refresh();

		canvasPath = path;
		path = path.Replace(Application.dataPath, "Assets");
		canvas = AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D)) as Texture2D;
		TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
		importer.isReadable = true;
		AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

		ResetCanvas();
	}

	static void ResetCanvas () {
		if (_renderCanvas != null) {
			_renderCanvas.Release();
			_renderCanvas = null;
		}

		Undo.RecordObject(window.paintTarget, "sets canvas");
		window.paintTarget.sharedMaterial.SetTexture(canvasName, canvas);
		canvas = null;
		canvasPath = null;
	}

	public static void OnSceneGUI(SceneView sceneview) {
		EventType t = Event.current.type;
		if (painting && t != EventType.MouseUp && RaycastTarget(t == EventType.MouseDrag || t == EventType.MouseMove)) {
			PaintTarget();
		}
		else if (t == EventType.MouseUp) {
            GUIUtility.hotControl = 0;
			window.RegisterChange();
    	}
	}

	void OnSelectionChange () {
		if (UpdateTarget()) Repaint();
    }

	static bool UpdateTarget() {
		if (Selection.activeGameObject == null) return false;
		Renderer r = Selection.activeGameObject.GetComponent<Renderer>();
		if (r != window.paintTarget) {
			SetPaintTarget(r);
			return true;
		}
		return false;
	}

	static void SetPaintTarget (Renderer r) {
		if (r == null) return;

		window.paintTarget = r;
		UpdateCanvasNames();
		colliderMesh.Clear();

		if (window.paintTarget is MeshRenderer) {
			MeshFilter f = window.paintTarget.GetComponent<MeshFilter>();
			if (f != null && f.sharedMesh != null) {
				colliderMesh.vertices = f.sharedMesh.vertices;
				colliderMesh.uv = f.sharedMesh.uv;
				colliderMesh.triangles = f.sharedMesh.triangles;
			}
		}
		else if (window.paintTarget is SpriteRenderer) {
			Sprite s = (window.paintTarget as SpriteRenderer).sprite;
			if (s != null) {
				colliderMesh.vertices = Array.ConvertAll(s.vertices, (v) => (Vector3)v);
				colliderMesh.uv = s.uv;
				colliderMesh.triangles = Array.ConvertAll(s.triangles, (t) => (int)t);
			}
		}

		raycastTarget.sharedMesh = colliderMesh;
		raycastTarget.transform.position = window.paintTarget.transform.position;
		raycastTarget.transform.rotation = window.paintTarget.transform.rotation;
		raycastTarget.transform.localScale = window.paintTarget.transform.localScale;
	}

	static void UpdateCanvasNames() {
		List<string> names = new List<string>();
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
		if (raycastTarget == null) return false;

		Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
		RaycastHit hit;
		if (raycastTarget.Raycast(ray, out hit, Mathf.Infinity)) {
			if (directional && mouseMoved) {
				Vector2 dir = ((hit.textureCoord - uv).normalized + Vector2.one) * 0.5f;
				color = new Color(dir.x, dir.y, 0, 1);
				uv = hit.textureCoord;
			}
			else if (!directional) {
				uv = hit.textureCoord;
			}

			brushMaterial.SetVector("_Transform", new Vector4(uv.x, uv.y, 0, radius));

			Color c = color;
			c.a *= blend;
			brushMaterial.SetColor("_Color", c);

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

		if (e.type == EventType.MouseDown) GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
		
		if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) {
			Graphics.Blit(renderCanvas, renderCanvas, brushMaterial);
			e.Use();
			didChange = true;
        }
	}
}
