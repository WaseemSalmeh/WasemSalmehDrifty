using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class ForestRuntimeMapEnhancer {
	private const float RoadWidth = 10.2f;
	private const float ShoulderWidth = 18.2f;
	private const int TrackSamplesPerSegment = 8;

	private static readonly Vector3[] TrackCenterLine = {
		new Vector3(-42f, 0f, -38f),
		new Vector3(-12f, 0f, -49f),
		new Vector3(23f, 0f, -39f),
		new Vector3(43f, 0f, -18f),
		new Vector3(26f, 0f, 3f),
		new Vector3(49f, 0f, 26f),
		new Vector3(16f, 0f, 43f),
		new Vector3(-18f, 0f, 35f),
		new Vector3(-42f, 0f, 13f),
		new Vector3(-52f, 0f, -13f),
		new Vector3(-31f, 0f, -29f)
	};

	public static void Enhance (GameObject mapRoot) {
		if (mapRoot == null) return;

		Transform surfaces = GetOrCreateChild(mapRoot.transform, "Surfaces");
		SetSurface(surfaces, "Forest Ground", CreateGroundMesh(), CreateMaterial("Runtime Forest Grass", new Color(0.13f, 0.34f, 0.14f, 1f), 0.18f), true);
		SetSurface(surfaces, "Packed Dirt Shoulders", CreateTrackMesh(ShoulderWidth, 0.018f, "Runtime Forest Shoulder Mesh"), CreateMaterial("Runtime Packed Dirt Shoulder", new Color(0.30f, 0.21f, 0.13f, 1f), 0.28f), true);
		SetSurface(surfaces, "Asphalt Winding Circuit", CreateTrackMesh(RoadWidth, 0.04f, "Runtime Forest Asphalt Mesh"), CreateMaterial("Runtime Forest Asphalt", new Color(0.035f, 0.038f, 0.04f, 1f), 0.48f), true);
	}

	private static void SetSurface (Transform parent, string objectName, Mesh mesh, Material material, bool colliderEnabled) {
		GameObject surface = FindChildByName(parent, objectName);
		if (surface == null) {
			surface = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
			surface.transform.SetParent(parent, false);
		}

		MeshFilter filter = surface.GetComponent<MeshFilter>();
		if (filter == null) filter = surface.AddComponent<MeshFilter>();
		if (filter.sharedMesh == null) filter.sharedMesh = mesh;

		MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
		if (renderer == null) renderer = surface.AddComponent<MeshRenderer>();
		if (renderer.sharedMaterial == null) renderer.sharedMaterial = material;
		renderer.shadowCastingMode = ShadowCastingMode.On;
		renderer.receiveShadows = true;

		if (colliderEnabled) {
			MeshCollider meshCollider = surface.GetComponent<MeshCollider>();
			if (meshCollider == null) meshCollider = surface.AddComponent<MeshCollider>();
			meshCollider.sharedMesh = null;
			meshCollider.sharedMesh = filter.sharedMesh;
			meshCollider.convex = false;
			meshCollider.isTrigger = false;
		}
	}

	private static Mesh CreateGroundMesh () {
		const float halfWidth = 84f;
		const float halfDepth = 68f;
		Mesh mesh = new Mesh();
		mesh.name = "Runtime Forest Ground Mesh";
		mesh.SetVertices(new List<Vector3> {
			new Vector3(-halfWidth, 0f, -halfDepth),
			new Vector3(halfWidth, 0f, -halfDepth),
			new Vector3(-halfWidth, 0f, halfDepth),
			new Vector3(halfWidth, 0f, halfDepth)
		});
		mesh.SetUVs(0, new List<Vector2> {
			new Vector2(0f, 0f),
			new Vector2(12f, 0f),
			new Vector2(0f, 10f),
			new Vector2(12f, 10f)
		});
		mesh.SetTriangles(new [] { 0, 2, 1, 1, 2, 3 }, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static Mesh CreateTrackMesh (float width, float height, string meshName) {
		List<Vector3> trackLine = GetSmoothedTrackLine();
		int count = trackLine.Count;
		List<Vector3> vertices = new List<Vector3>(count * 2);
		List<Vector2> uvs = new List<Vector2>(count * 2);
		List<int> triangles = new List<int>(count * 6);
		float distance = 0f;

		for (int i = 0; i < count; i++) {
			Vector3 tangent;
			Vector3 right;
			GetFrame(trackLine, i, out tangent, out right);
			if (i > 0) distance += Vector3.Distance(trackLine[i - 1], trackLine[i]);
			vertices.Add(trackLine[i] - right * (width * 0.5f) + Vector3.up * height);
			vertices.Add(trackLine[i] + right * (width * 0.5f) + Vector3.up * height);
			uvs.Add(new Vector2(0f, distance * 0.08f));
			uvs.Add(new Vector2(1f, distance * 0.08f));
		}

		for (int i = 0; i < count; i++) {
			int next = (i + 1) % count;
			int leftA = i * 2;
			int rightA = leftA + 1;
			int leftB = next * 2;
			int rightB = leftB + 1;
			triangles.Add(leftA);
			triangles.Add(leftB);
			triangles.Add(rightA);
			triangles.Add(rightA);
			triangles.Add(leftB);
			triangles.Add(rightB);
		}

		Mesh mesh = new Mesh();
		mesh.name = meshName;
		mesh.SetVertices(vertices);
		mesh.SetUVs(0, uvs);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static List<Vector3> GetSmoothedTrackLine () {
		List<Vector3> points = new List<Vector3>(TrackCenterLine.Length * TrackSamplesPerSegment);
		for (int i = 0; i < TrackCenterLine.Length; i++) {
			Vector3 p0 = TrackCenterLine[(i - 1 + TrackCenterLine.Length) % TrackCenterLine.Length];
			Vector3 p1 = TrackCenterLine[i];
			Vector3 p2 = TrackCenterLine[(i + 1) % TrackCenterLine.Length];
			Vector3 p3 = TrackCenterLine[(i + 2) % TrackCenterLine.Length];
			for (int step = 0; step < TrackSamplesPerSegment; step++) {
				float t = step / (float)TrackSamplesPerSegment;
				points.Add(CatmullRom(p0, p1, p2, p3, t));
			}
		}
		return points;
	}

	private static Vector3 CatmullRom (Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
		float t2 = t * t;
		float t3 = t2 * t;
		return 0.5f * ((2f * p1) +
			(-p0 + p2) * t +
			(2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
			(-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	private static void GetFrame (List<Vector3> trackLine, int index, out Vector3 tangent, out Vector3 right) {
		Vector3 previous = trackLine[(index - 1 + trackLine.Count) % trackLine.Count];
		Vector3 next = trackLine[(index + 1) % trackLine.Count];
		tangent = (next - previous).normalized;
		if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.forward;
		right = Vector3.Cross(Vector3.up, tangent).normalized;
		if (right.sqrMagnitude < 0.001f) right = Vector3.right;
	}

	private static Material CreateMaterial (string name, Color color, float smoothness) {
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		Material material = new Material(shader);
		material.name = name;
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color")) material.SetColor("_Color", color);
		if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
		if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.02f);
		if (color.a < 0.99f) {
			material.SetOverrideTag("RenderType", "Transparent");
			material.renderQueue = (int)RenderQueue.Transparent;
			if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
			if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
			material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			material.EnableKeyword("_ALPHABLEND_ON");
		}
		return material;
	}

	private static Transform GetOrCreateChild (Transform parent, string childName) {
		Transform existing = parent.Find(childName);
		if (existing != null) return existing;
		GameObject child = new GameObject(childName);
		child.transform.SetParent(parent, false);
		return child.transform;
	}

	private static GameObject FindChildByName (Transform root, string childName) {
		if (root == null) return null;
		if (string.Equals(root.name, childName, System.StringComparison.OrdinalIgnoreCase)) return root.gameObject;
		for (int i = 0; i < root.childCount; i++) {
			GameObject found = FindChildByName(root.GetChild(i), childName);
			if (found != null) return found;
		}
		return null;
	}

}
