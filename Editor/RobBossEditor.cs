using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class RobBossEditor : EditorWindow {

	static int canvasID = 0;
	static bool directional = false;
	static Color color = Color.white;
	static float radius = 0.5f;
	static float blend = 0.1f;

	static string[] canvasNames = new string[0];
	static MeshCollider raycastTarget;
	static Mesh colliderMesh;
	static Renderer paintTarget;
	static Vector2? uv;

	[MenuItem ("Window/Rob Boss Painter")]
	static void Open () {
		RobBossEditor window = EditorWindow.GetWindow(typeof(RobBossEditor)) as RobBossEditor;
		window.minSize = new Vector2(250, 360);
		window.Show();
	}

	static SceneView.OnSceneFunc onSceneFunc;
    void OnEnable () {
   		if (onSceneFunc == null) onSceneFunc = new SceneView.OnSceneFunc(OnSceneGUI);
		SceneView.onSceneGUIDelegate += onSceneFunc;

		GameObject g = new GameObject("RobBossTarget");
		g.hideFlags = HideFlags.HideAndDontSave;
		raycastTarget = g.AddComponent<MeshCollider>();
		colliderMesh = new Mesh();
		colliderMesh.name = "RobBossColliderMesh";
		colliderMesh.hideFlags = HideFlags.HideAndDontSave;
		raycastTarget.sharedMesh = colliderMesh;

		directional = EditorPrefs.GetInt("RobBoss.Directional", 0) == 1;
		string colorString = EditorPrefs.GetString("RobBoss.Color", "#FFFFFFFF");
		ColorUtility.TryParseHtmlString(colorString, out color);
		radius = EditorPrefs.GetFloat("RobBoss.Radius", 0.5f);
		blend = EditorPrefs.GetFloat("RobBoss.Blend", 0.1f);
    }

    void OnDisable () {
    	SceneView.onSceneGUIDelegate -= onSceneFunc;

		DestroyImmediate(colliderMesh);
		DestroyImmediate(raycastTarget.gameObject);

		EditorPrefs.SetInt("RobBoss.Directional", directional ? 1 : 0);
		EditorPrefs.SetString("RobBoss.Color", ColorUtility.ToHtmlStringRGBA(color));
		EditorPrefs.SetFloat("RobBoss.Radius", radius);
		EditorPrefs.SetFloat("RobBoss.Blend", blend);
    }

	void OnGUI () {
		canvasID = EditorGUILayout.Popup("Canvas", canvasID, canvasNames);
		directional = EditorGUILayout.Toggle("Directional", directional);
		GUI.enabled = !directional;
		color = EditorGUILayout.ColorField("Color", color);
		GUI.enabled = true;
		radius = EditorGUILayout.FloatField("Radius", radius);
		blend = EditorGUILayout.FloatField("Blend", blend);
	}

	public static void OnSceneGUI(SceneView sceneview) {			
		UpdateTarget();
		RaycastTarget();
	}

	static void UpdateTarget() {
		if (Selection.activeGameObject == null) return;

		Renderer r = Selection.activeGameObject.GetComponentInChildren<Renderer>();
		if (r != null && r != paintTarget) {
			paintTarget = r;
			UpdateCanvasNames();
			colliderMesh.Clear();

			if (paintTarget is MeshRenderer) {
				MeshFilter f = paintTarget.GetComponent<MeshFilter>();
				if (f != null && f.sharedMesh != null) {
					colliderMesh.vertices = f.sharedMesh.vertices;
					colliderMesh.uv = f.sharedMesh.uv;
					colliderMesh.triangles = f.sharedMesh.triangles;
				}
			}
			else if (paintTarget is SpriteRenderer) {
				Sprite s = (paintTarget as SpriteRenderer).sprite;
				if (s != null) {
					colliderMesh.vertices = Array.ConvertAll(s.vertices, (v) => (Vector3)v);
					colliderMesh.uv = s.uv;
					colliderMesh.triangles = Array.ConvertAll(s.triangles, (t) => (int)t);
				}
			}

			raycastTarget.sharedMesh = colliderMesh;
			raycastTarget.transform.position = paintTarget.transform.position;
			raycastTarget.transform.rotation = paintTarget.transform.rotation;
			raycastTarget.transform.localScale = paintTarget.transform.localScale;
		}
	}

	static void RaycastTarget() {	
		if (raycastTarget == null) return;

		Handles.color = color;	 
		Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
		RaycastHit hit;
		if (raycastTarget.Raycast(ray, out hit, Mathf.Infinity)) {
			Handles.DrawLine(hit.point, hit.point + hit.normal * 2);
			Handles.DrawWireDisc(hit.point, hit.normal, radius * paintTarget.bounds.extents.y);
			uv = hit.textureCoord;
		} 
		else {
			uv = null;
		}
	}

	static void UpdateCanvasNames() {
		List<string> names = new List<string>();
		Shader shader = paintTarget.sharedMaterial.shader;
    	for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++) {
			if (ShaderUtil.IsShaderPropertyHidden(shader, i)) continue;
    		if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) {
    			names.Add(ShaderUtil.GetPropertyName(shader, i));
			}
    	}
		canvasNames = names.ToArray();
	}
}
