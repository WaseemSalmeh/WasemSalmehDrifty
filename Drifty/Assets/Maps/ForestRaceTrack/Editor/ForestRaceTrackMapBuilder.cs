#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class ForestRaceTrackMapBuilder {
	private const string RootFolder = "Assets/Maps";
	private const string MapFolder = RootFolder + "/ForestRaceTrack";
	private const string MeshFolder = MapFolder + "/Meshes";
	private const string MaterialFolder = MapFolder + "/Materials";
	private const string PrefabFolder = MapFolder + "/Prefabs";
	private const string PrefabPath = PrefabFolder + "/ForestRaceTrackMap.prefab";
	private const string VersionPath = MapFolder + "/ForestRaceTrackMap.version";
	private const string MapVersion = "2026-05-20-smooth-forest-v2";
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

	static ForestRaceTrackMapBuilder () {
		EditorApplication.delayCall += AutoBuildIfMissing;
	}

	[MenuItem("Drifty/Maps/Rebuild Forest Race Track Map")]
	public static void RebuildFromMenu () {
		Build(true);
	}

	public static void BuildFromCommandLine () {
		Build(true);
	}

	private static void AutoBuildIfMissing () {
		if (EditorApplication.isPlayingOrWillChangePlaymode) return;
		if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
		Build(false);
	}

	private static void Build (bool forceRebuild) {
		EnsureFolders();

		Material grassMaterial = CreateMaterial("ForestGrass.mat", new Color(0.16f, 0.38f, 0.16f, 1f), 0.08f, 0.18f);
		Material mossMaterial = CreateMaterial("MossPatch.mat", new Color(0.27f, 0.52f, 0.18f, 1f), 0.04f, 0.22f);
		Material asphaltMaterial = CreateMaterial("ForestAsphalt.mat", new Color(0.035f, 0.038f, 0.04f, 1f), 0.08f, 0.48f);
		Material shoulderMaterial = CreateMaterial("PackedDirtShoulder.mat", new Color(0.31f, 0.22f, 0.14f, 1f), 0.02f, 0.28f);
		Material trunkMaterial = CreateMaterial("PineTrunk.mat", new Color(0.25f, 0.14f, 0.075f, 1f), 0.03f, 0.2f);
		Material pineMaterial = CreateMaterial("PineNeedles.mat", new Color(0.055f, 0.24f, 0.105f, 1f), 0.02f, 0.18f);
		Material leafMaterial = CreateMaterial("BroadleafCanopy.mat", new Color(0.10f, 0.34f, 0.11f, 1f), 0.04f, 0.18f);
		Material rockMaterial = CreateMaterial("ForestRock.mat", new Color(0.28f, 0.29f, 0.27f, 1f), 0.0f, 0.35f);
		Material waterMaterial = CreateMaterial("ForestPond.mat", new Color(0.08f, 0.25f, 0.28f, 0.84f), 0.0f, 0.75f);
		Material whiteMaterial = CreateMaterial("FinishWhite.mat", new Color(0.92f, 0.90f, 0.82f, 1f), 0.02f, 0.4f);
		Material blackMaterial = CreateMaterial("FinishBlack.mat", new Color(0.02f, 0.02f, 0.018f, 1f), 0.02f, 0.45f);

		Mesh groundMesh = SaveMesh(CreateGroundMesh(), MeshFolder + "/ForestGround.asset");
		Mesh shoulderMesh = SaveMesh(CreateTrackMesh(ShoulderWidth, 0.018f, "Forest Dirt Shoulder Mesh"), MeshFolder + "/ForestDirtShoulder.asset");
		Mesh asphaltMesh = SaveMesh(CreateTrackMesh(RoadWidth, 0.04f, "Forest Asphalt Track Mesh"), MeshFolder + "/ForestAsphaltTrack.asset");
		Mesh pondMesh = SaveMesh(CreateEllipseMesh(16f, 8f, "Forest Pond Mesh"), MeshFolder + "/ForestPond.asset");
		Mesh coneMesh = SaveMesh(CreateConeMesh(14, "Pine Crown Mesh"), MeshFolder + "/PineCrown.asset");

		GameObject root = new GameObject("Forest Race Track Map");
		root.isStatic = true;
		Transform surfaces = CreateGroup(root.transform, "Surfaces");
		Transform details = CreateGroup(root.transform, "Track Details");
		Transform markers = CreateGroup(root.transform, "Markers");
		Transform setDressing = CreateGroup(root.transform, "Set Dressing");

		CreateMeshObject(surfaces, "Forest Ground", groundMesh, grassMaterial, true);
		CreateMeshObject(surfaces, "Packed Dirt Shoulders", shoulderMesh, shoulderMaterial, true);
		CreateMeshObject(surfaces, "Asphalt Winding Circuit", asphaltMesh, asphaltMaterial, true);

		GameObject pond = CreateMeshObject(surfaces, "Forest Pond", pondMesh, waterMaterial, false);
		pond.transform.position = new Vector3(20f, 0.055f, 6f);
		pond.transform.rotation = Quaternion.Euler(0f, -18f, 0f);

		CreateMossPatches(setDressing, mossMaterial);
		CreateStartFinishLine(markers, whiteMaterial, blackMaterial);
		CreateSpawnPoint(markers);
		CreateForest(setDressing, trunkMaterial, pineMaterial, leafMaterial, rockMaterial, coneMesh);

		if (forceRebuild && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) {
			AssetDatabase.DeleteAsset(PrefabPath);
		}

		PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
		UnityEngine.Object.DestroyImmediate(root);
		File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), VersionPath), MapVersion + Environment.NewLine);
		AssetDatabase.ImportAsset(VersionPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("Created forest race track map prefab at " + PrefabPath);
	}

	private static void EnsureFolders () {
		EnsureFolder("Assets", "Maps");
		EnsureFolder(RootFolder, "ForestRaceTrack");
		EnsureFolder(MapFolder, "Meshes");
		EnsureFolder(MapFolder, "Materials");
		EnsureFolder(MapFolder, "Models");
		EnsureFolder(MapFolder, "Prefabs");
	}

	private static void EnsureFolder (string parent, string folderName) {
		string path = parent + "/" + folderName;
		if (!AssetDatabase.IsValidFolder(path)) {
			AssetDatabase.CreateFolder(parent, folderName);
		}
	}

	private static Material CreateMaterial (string fileName, Color color, float metallic, float smoothness) {
		string path = MaterialFolder + "/" + fileName;
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (material == null) {
			material = new Material(shader);
			AssetDatabase.CreateAsset(material, path);
		}

		material.shader = shader;
		material.name = fileName.Replace(".mat", "");
		SetMaterialColor(material, color);
		if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
		if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
		if (color.a < 0.99f) {
			material.SetOverrideTag("RenderType", "Transparent");
			material.renderQueue = (int)RenderQueue.Transparent;
			if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
			if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
			material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			material.EnableKeyword("_ALPHABLEND_ON");
		}
		EditorUtility.SetDirty(material);
		return material;
	}

	private static Transform CreateGroup (Transform parent, string groupName) {
		GameObject group = new GameObject(groupName);
		group.transform.SetParent(parent, false);
		group.isStatic = true;
		return group.transform;
	}

	private static void SetMaterialColor (Material material, Color color) {
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color")) material.SetColor("_Color", color);
	}

	private static Mesh SaveMesh (Mesh mesh, string path) {
		Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
		if (existing != null) AssetDatabase.DeleteAsset(path);
		AssetDatabase.CreateAsset(mesh, path);
		return AssetDatabase.LoadAssetAtPath<Mesh>(path);
	}

	private static Mesh CreateGroundMesh () {
		const float halfWidth = 82f;
		const float halfDepth = 66f;
		Mesh mesh = new Mesh();
		mesh.name = "Forest Ground Mesh";
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
			Vector3 previous = trackLine[(i - 1 + count) % count];
			Vector3 current = trackLine[i];
			Vector3 next = trackLine[(i + 1) % count];
			Vector3 tangent = (next - previous).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
			if (i > 0) distance += Vector3.Distance(trackLine[i - 1], current);
			vertices.Add(current - right * (width * 0.5f) + Vector3.up * height);
			vertices.Add(current + right * (width * 0.5f) + Vector3.up * height);
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

	private static Mesh CreateEllipseMesh (float radiusX, float radiusZ, string meshName) {
		const int segments = 40;
		List<Vector3> vertices = new List<Vector3> { Vector3.zero };
		List<int> triangles = new List<int>();
		for (int i = 0; i < segments; i++) {
			float angle = Mathf.PI * 2f * i / segments;
			vertices.Add(new Vector3(Mathf.Cos(angle) * radiusX, 0f, Mathf.Sin(angle) * radiusZ));
		}
		for (int i = 1; i <= segments; i++) {
			int next = i == segments ? 1 : i + 1;
			triangles.Add(0);
			triangles.Add(i);
			triangles.Add(next);
		}
		Mesh mesh = new Mesh();
		mesh.name = meshName;
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static Mesh CreateConeMesh (int segments, string meshName) {
		List<Vector3> vertices = new List<Vector3> {
			new Vector3(0f, 1.5f, 0f),
			Vector3.zero
		};
		List<int> triangles = new List<int>();
		for (int i = 0; i < segments; i++) {
			float angle = Mathf.PI * 2f * i / segments;
			vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
		}
		for (int i = 0; i < segments; i++) {
			int a = 2 + i;
			int b = 2 + ((i + 1) % segments);
			triangles.Add(0);
			triangles.Add(a);
			triangles.Add(b);
			triangles.Add(1);
			triangles.Add(b);
			triangles.Add(a);
		}
		Mesh mesh = new Mesh();
		mesh.name = meshName;
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static GameObject CreateMeshObject (Transform parent, string objectName, Mesh mesh, Material material, bool collider) {
		GameObject gameObject = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
		gameObject.transform.SetParent(parent, false);
		gameObject.isStatic = true;
		gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
		renderer.sharedMaterial = material;
		renderer.shadowCastingMode = ShadowCastingMode.On;
		renderer.receiveShadows = true;
		if (collider) {
			MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
			meshCollider.sharedMesh = mesh;
		}
		return gameObject;
	}

	private static void CreateMossPatches (Transform root, Material material) {
		GameObject parent = new GameObject("Moss And Grass Patches");
		parent.transform.SetParent(root, false);
		Vector3[] positions = {
			new Vector3(-8f, 0.065f, -9f),
			new Vector3(31f, 0.065f, 38f),
			new Vector3(-35f, 0.065f, 31f),
			new Vector3(8f, 0.065f, 22f),
			new Vector3(47f, 0.065f, -37f)
		};
		Vector3[] scales = {
			new Vector3(9f, 1f, 4f),
			new Vector3(8f, 1f, 5f),
			new Vector3(7f, 1f, 4f),
			new Vector3(11f, 1f, 5f),
			new Vector3(7f, 1f, 3f)
		};

		for (int i = 0; i < positions.Length; i++) {
			GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			patch.name = "Soft Moss Patch";
			patch.transform.SetParent(parent.transform, false);
			patch.transform.position = positions[i];
			patch.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
			patch.transform.localScale = scales[i];
			patch.GetComponent<MeshRenderer>().sharedMaterial = material;
			RemoveCollider(patch);
			patch.isStatic = true;
		}
	}

	private static void CreateRoadSurfaceDetails (Transform root, Material scuffMaterial, Material leafLitterMaterial) {
		GameObject parent = new GameObject("Road Scuffs And Leaf Litter");
		parent.transform.SetParent(root, false);
		parent.isStatic = true;
		List<Vector3> trackLine = GetSmoothedTrackLine();

		for (int i = 5; i < trackLine.Count; i += 9) {
			Vector3 previous = trackLine[(i - 1 + trackLine.Count) % trackLine.Count];
			Vector3 current = trackLine[i];
			Vector3 next = trackLine[(i + 1) % trackLine.Count];
			Vector3 tangent = (next - previous).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
			float sideOffset = ((i / 9) % 2 == 0 ? -1f : 1f) * Lerp(0.35f, 1.65f, Mathf.Abs(Mathf.Sin(i * 0.73f)));
			float length = Lerp(3.2f, 7.4f, Mathf.Abs(Mathf.Sin(i * 0.37f)));
			CreateFlatBox(parent.transform, "Dark Tire Scuff", current + right * sideOffset + Vector3.up * 0.092f, Quaternion.LookRotation(tangent, Vector3.up), new Vector3(0.52f, 0.012f, length), scuffMaterial);
		}

		for (int i = 3; i < trackLine.Count; i += 7) {
			Vector3 previous = trackLine[(i - 1 + trackLine.Count) % trackLine.Count];
			Vector3 current = trackLine[i];
			Vector3 next = trackLine[(i + 1) % trackLine.Count];
			Vector3 tangent = (next - previous).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
			float side = (i % 2 == 0) ? -1f : 1f;
			float shoulderOffset = RoadWidth * 0.5f + Lerp(1.4f, 3.4f, Mathf.Abs(Mathf.Cos(i * 0.41f)));
			Vector3 scale = new Vector3(Lerp(1.2f, 2.8f, Mathf.Abs(Mathf.Sin(i * 0.29f))), 0.014f, Lerp(1.8f, 4.5f, Mathf.Abs(Mathf.Cos(i * 0.23f))));
			CreateFlatBox(parent.transform, "Leaf Litter Patch", current + right * shoulderOffset * side + Vector3.up * 0.086f, Quaternion.LookRotation(tangent, Vector3.up), scale, leafLitterMaterial);
		}
	}

	private static void CreateTrackEdgeDetails (Transform root, Material postMaterial, Material markerMaterial) {
		GameObject parent = new GameObject("Track Edge Posts");
		parent.transform.SetParent(root, false);
		parent.isStatic = true;
		List<Vector3> trackLine = GetSmoothedTrackLine();

		for (int i = 0; i < trackLine.Count; i += 4) {
			Vector3 previous = trackLine[(i - 1 + trackLine.Count) % trackLine.Count];
			Vector3 current = trackLine[i];
			Vector3 next = trackLine[(i + 1) % trackLine.Count];
			Vector3 tangent = (next - previous).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
			CreateEdgePost(parent.transform, current - right * (RoadWidth * 0.5f + 1.35f), Quaternion.LookRotation(right, Vector3.up), postMaterial, markerMaterial);
			CreateEdgePost(parent.transform, current + right * (RoadWidth * 0.5f + 1.35f), Quaternion.LookRotation(-right, Vector3.up), postMaterial, markerMaterial);
		}
	}

	private static void CreateFlatBox (Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material) {
		GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
		box.name = name;
		box.transform.SetParent(parent, false);
		box.transform.position = position;
		box.transform.rotation = rotation;
		box.transform.localScale = scale;
		box.GetComponent<MeshRenderer>().sharedMaterial = material;
		RemoveCollider(box);
		box.isStatic = true;
	}

	private static void CreateEdgePost (Transform parent, Vector3 position, Quaternion rotation, Material postMaterial, Material markerMaterial) {
		GameObject postRoot = new GameObject("Road Edge Marker");
		postRoot.transform.SetParent(parent, false);
		postRoot.transform.position = position;
		postRoot.transform.rotation = rotation;
		postRoot.isStatic = true;

		GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		post.name = "Wood Marker Post";
		post.transform.SetParent(postRoot.transform, false);
		post.transform.localPosition = new Vector3(0f, 0.45f, 0f);
		post.transform.localScale = new Vector3(0.07f, 0.45f, 0.07f);
		post.GetComponent<MeshRenderer>().sharedMaterial = postMaterial;
		RemoveCollider(post);
		post.isStatic = true;

		GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
		marker.name = "Orange Reflector";
		marker.transform.SetParent(postRoot.transform, false);
		marker.transform.localPosition = new Vector3(0f, 0.86f, -0.055f);
		marker.transform.localScale = new Vector3(0.22f, 0.16f, 0.035f);
		marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
		RemoveCollider(marker);
		marker.isStatic = true;
	}

	private static void CreateStartFinishLine (Transform root, Material whiteMaterial, Material blackMaterial) {
		GameObject parent = new GameObject("Forest Finish Line");
		parent.transform.SetParent(root, false);
		parent.isStatic = true;

		Vector3 start = TrackCenterLine[0];
		Vector3 tangent = (TrackCenterLine[1] - TrackCenterLine[0]).normalized;
		Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
		parent.transform.position = start + Vector3.up * 0.1f;
		parent.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);

		const int columns = 8;
		const int rows = 2;
		float tileWidth = RoadWidth / columns;
		float tileDepth = 0.98f;
		for (int row = 0; row < rows; row++) {
			for (int column = 0; column < columns; column++) {
				bool white = (row + column) % 2 == 0;
				Vector3 offset = right * ((column - (columns - 1) * 0.5f) * tileWidth) + tangent * ((row - 0.5f) * tileDepth);
				CreateFlatBox(parent.transform, white ? "Finish White Tile" : "Finish Black Tile", start + offset + Vector3.up * 0.095f, Quaternion.LookRotation(tangent, Vector3.up), new Vector3(tileWidth * 0.92f, 0.045f, tileDepth * 0.92f), white ? whiteMaterial : blackMaterial);
			}
		}
	}

	private static void CreateSpawnPoint (Transform root) {
		GameObject spawnPoint = new GameObject("Forest Race Spawn Point");
		spawnPoint.transform.SetParent(root, false);
		Vector3 tangent = (TrackCenterLine[1] - TrackCenterLine[0]).normalized;
		spawnPoint.transform.position = TrackCenterLine[0] + Vector3.up * 0.35f;
		spawnPoint.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
	}

	private static void CreateTrackSigns (Transform root, Material woodMaterial, Material whiteMaterial, Material blackMaterial) {
		GameObject parent = new GameObject("Forest Track Signs");
		parent.transform.SetParent(root, false);
		for (int i = 0; i < TrackCenterLine.Length; i += 2) {
			Vector3 current = TrackCenterLine[i];
			Vector3 next = TrackCenterLine[(i + 1) % TrackCenterLine.Length];
			Vector3 previous = TrackCenterLine[(i - 1 + TrackCenterLine.Length) % TrackCenterLine.Length];
			Vector3 tangent = (next - previous).normalized;
			Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
			Vector3 position = current + right * (RoadWidth * 0.75f + 1.5f);
			CreateSign(parent.transform, position, Quaternion.LookRotation(-right, Vector3.up), woodMaterial, i % 4 == 0 ? whiteMaterial : blackMaterial);
		}
	}

	private static void CreateSign (Transform parent, Vector3 position, Quaternion rotation, Material postMaterial, Material faceMaterial) {
		GameObject sign = new GameObject("Corner Marker Sign");
		sign.transform.SetParent(parent, false);
		sign.transform.position = position;
		sign.transform.rotation = rotation;

		GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		post.name = "Wood Post";
		post.transform.SetParent(sign.transform, false);
		post.transform.localPosition = new Vector3(0f, 0.75f, 0f);
		post.transform.localScale = new Vector3(0.08f, 0.75f, 0.08f);
		post.GetComponent<MeshRenderer>().sharedMaterial = postMaterial;
		RemoveCollider(post);

		GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
		face.name = "Chevron Board";
		face.transform.SetParent(sign.transform, false);
		face.transform.localPosition = new Vector3(0f, 1.45f, 0f);
		face.transform.localScale = new Vector3(1.4f, 0.42f, 0.06f);
		face.GetComponent<MeshRenderer>().sharedMaterial = faceMaterial;
		RemoveCollider(face);
		sign.isStatic = true;
	}

	private static void CreateForest (Transform root, Material trunkMaterial, Material pineMaterial, Material leafMaterial, Material rockMaterial, Mesh coneMesh) {
		GameObject forest = new GameObject("Forest Set Dressing");
		forest.transform.SetParent(root, false);
		System.Random random = new System.Random(9047);

		for (int i = 0; i < 108; i++) {
			Vector3 position = SampleForestPosition(random);
			float scale = Lerp(0.82f, 1.32f, (float)random.NextDouble());
			bool pine = random.NextDouble() > 0.32;
			CreateTree(forest.transform, position, scale, pine, trunkMaterial, pineMaterial, leafMaterial, coneMesh);
		}

		for (int i = 0; i < 26; i++) {
			Vector3 position = SampleForestPosition(random);
			float scale = Lerp(0.7f, 1.8f, (float)random.NextDouble());
			CreateRock(forest.transform, position, scale, rockMaterial, random);
		}
	}

	private static Vector3 SampleForestPosition (System.Random random) {
		for (int attempt = 0; attempt < 80; attempt++) {
			float x = Lerp(-76f, 76f, (float)random.NextDouble());
			float z = Lerp(-60f, 60f, (float)random.NextDouble());
			Vector3 position = new Vector3(x, 0f, z);
			float edgeBias = Mathf.Max(Mathf.Abs(x) / 76f, Mathf.Abs(z) / 60f);
			if (edgeBias < 0.47f && random.NextDouble() > 0.32) continue;
			if (DistanceToTrack(position) < ShoulderWidth * 0.5f + 4.4f) continue;
			if (Vector3.Distance(position, new Vector3(20f, 0f, 6f)) < 18f) continue;
			return position;
		}
		return new Vector3(Lerp(-76f, 76f, (float)random.NextDouble()), 0f, Lerp(-60f, 60f, (float)random.NextDouble()));
	}

	private static void CreateTree (Transform parent, Vector3 position, float scale, bool pine, Material trunkMaterial, Material pineMaterial, Material leafMaterial, Mesh coneMesh) {
		GameObject tree = new GameObject(pine ? "Pine Tree" : "Broadleaf Tree");
		tree.transform.SetParent(parent, false);
		tree.transform.position = position;
		tree.transform.rotation = Quaternion.Euler(0f, Mathf.Abs(position.x * 11.3f + position.z * 5.7f) % 360f, 0f);
		tree.isStatic = true;

		float trunkHeight = (pine ? 3.7f : 2.8f) * scale;
		float trunkRadius = (pine ? 0.22f : 0.28f) * scale;
		GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		trunk.name = "Trunk";
		trunk.transform.SetParent(tree.transform, false);
		trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
		trunk.transform.localScale = new Vector3(trunkRadius, trunkHeight * 0.5f, trunkRadius);
		trunk.GetComponent<MeshRenderer>().sharedMaterial = trunkMaterial;
		RemoveCollider(trunk);
		trunk.isStatic = true;

		if (pine) {
			CreatePineCrown(tree.transform, coneMesh, pineMaterial, new Vector3(0f, trunkHeight * 0.68f, 0f), 2.45f * scale, 3.5f * scale);
			CreatePineCrown(tree.transform, coneMesh, pineMaterial, new Vector3(0f, trunkHeight * 1.02f, 0f), 1.75f * scale, 2.7f * scale);
		} else {
			GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			crown.name = "Leaf Canopy";
			crown.transform.SetParent(tree.transform, false);
			crown.transform.localPosition = new Vector3(0f, trunkHeight + 1.35f * scale, 0f);
			crown.transform.localScale = new Vector3(2.8f * scale, 2.25f * scale, 2.8f * scale);
			crown.GetComponent<MeshRenderer>().sharedMaterial = leafMaterial;
			RemoveCollider(crown);
			crown.isStatic = true;
		}
	}

	private static void CreatePineCrown (Transform parent, Mesh coneMesh, Material material, Vector3 localPosition, float radius, float height) {
		GameObject crown = new GameObject("Pine Crown", typeof(MeshFilter), typeof(MeshRenderer));
		crown.transform.SetParent(parent, false);
		crown.transform.localPosition = localPosition;
		crown.transform.localScale = new Vector3(radius, height, radius);
		crown.GetComponent<MeshFilter>().sharedMesh = coneMesh;
		crown.GetComponent<MeshRenderer>().sharedMaterial = material;
		crown.isStatic = true;
	}

	private static void CreateRock (Transform parent, Vector3 position, float scale, Material material, System.Random random) {
		GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		rock.name = "Forest Rock";
		rock.transform.SetParent(parent, false);
		rock.transform.position = position + Vector3.up * (0.18f * scale);
		rock.transform.rotation = Quaternion.Euler(0f, Lerp(0f, 360f, (float)random.NextDouble()), 0f);
		rock.transform.localScale = new Vector3(1.1f * scale, 0.45f * scale, 0.8f * scale);
		rock.GetComponent<MeshRenderer>().sharedMaterial = material;
		RemoveCollider(rock);
		rock.isStatic = true;
	}

	private static void RemoveCollider (GameObject gameObject) {
		Collider collider = gameObject.GetComponent<Collider>();
		if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
	}

	private static float DistanceToTrack (Vector3 position) {
		float best = float.MaxValue;
		for (int i = 0; i < TrackCenterLine.Length; i++) {
			Vector3 a = TrackCenterLine[i];
			Vector3 b = TrackCenterLine[(i + 1) % TrackCenterLine.Length];
			best = Mathf.Min(best, DistanceToSegment(position, a, b));
		}
		return best;
	}

	private static float DistanceToSegment (Vector3 point, Vector3 a, Vector3 b) {
		Vector3 ab = b - a;
		float t = Vector3.Dot(point - a, ab) / Mathf.Max(0.001f, Vector3.Dot(ab, ab));
		t = Mathf.Clamp01(t);
		return Vector3.Distance(point, a + ab * t);
	}

	private static float Lerp (float a, float b, float t) {
		return a + (b - a) * t;
	}
}
#endif
