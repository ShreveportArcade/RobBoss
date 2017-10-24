using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class RobBossEditor : EditorWindow {

	static bool directional = false;
	static Color color = Color.white;
	static float radius = 0.5f;
	static float blend = 0.1f;

	static MeshCollider raycastTarget;
	static Mesh colliderMesh;
	static Renderer paintTarget;

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

	public static void OnSceneGUI(SceneView sceneview) {			
		UpdateTarget();
		DrawBrush();
	}

	static void UpdateTarget() {
		if (Selection.activeGameObject == null) return;
		
		Renderer r = Selection.activeGameObject.GetComponentInChildren<Renderer>();
		if (r != null && r != paintTarget) {
			paintTarget = r;
	
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
		}
	}

	static void DrawBrush() {	
		if (raycastTarget == null) return;
		Handles.color = color;	 
		Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
		RaycastHit hit;
		if (raycastTarget.Raycast(ray, out hit, Mathf.Infinity)) {
			Handles.DrawLine(hit.point, hit.point + hit.normal * 2);
			Handles.DrawWireDisc(hit.point, hit.normal, radius * paintTarget.bounds.extents.y);		
		} 
	}

	void OnGUI () {
		directional = EditorGUILayout.Toggle("Directional", directional);
		GUI.enabled = !directional;
		color = EditorGUILayout.ColorField("Color", color);
		GUI.enabled = true;
		radius = EditorGUILayout.FloatField("Radius", radius);
		blend = EditorGUILayout.FloatField("Blend", blend);
	}
}
