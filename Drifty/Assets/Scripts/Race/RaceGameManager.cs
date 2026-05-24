using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RaceGameManager : MonoBehaviour {
	private enum RaceState {
		Menu,
		Countdown,
		Racing,
		Finished,
		Paused
	}

	private enum MapChoice {
		Desert,
		Forest
	}

	private struct VehicleProfile {
		public string displayName;
		public string trait;
		public float maxSpeed;
		public float power;
		public float brake;
		public float mass;
		public float rearDriftGrip;
		public float yawAssist;
		public float sideAssist;
		public float offTrackPower;
		public float steeringReduction;
		public float stability;
		public float downforce;
		public Color accentColor;
	}

	private struct VehicleLightProfile {
		public float xScale;
		public float heightRatio;
		public float frontReach;
		public float rearReach;
		public float frontInset;
		public float rearInset;
		public float frontVerticalOffset;
		public float rearVerticalOffset;
		public float headlightTilt;
		public float headlightIntensity;
		public float headlightRange;
		public float headlightSpotAngle;
	}

	private struct VehicleLightLayout {
		public float frontZ;
		public float rearZ;
		public float xOffset;
		public float wheelY;
	}

	[Header("Vehicles")]
	[SerializeField] private GameObject[] vehiclePrefabs;
	[SerializeField] private GameObject startingSceneCar;
	[SerializeField] private int selectedVehicleIndex;

	[Header("Race Setup")]
	[SerializeField] private Transform carSpawnPoint;
	[SerializeField] private CameraTarget raceCamera;
	[SerializeField] private SetCamera cameraManager;
	[SerializeField] private float countdownStepSeconds = 0.8f;
	[SerializeField] private float checkpointSpacing = 24f;
	[SerializeField] private int minimumCheckpointCount = 8;
	[SerializeField] private int maximumCheckpointCount = 14;

	[Header("Maps")]
	[SerializeField] private GameObject desertMapRoot;
	[SerializeField] private GameObject forestMapPrefab;
	[SerializeField] private Material desertSkyboxMaterial;
	[SerializeField] private Material forestSkyboxMaterial;
	[SerializeField] private int selectedMapIndex;

	[Header("Audio")]
	[SerializeField] private AudioClip carStartClip;
	[SerializeField] private AudioClip carEngineClip;
	[SerializeField] private AudioClip truckStartClip;
	[SerializeField] private AudioClip truckEngineClip;
	[SerializeField] private AudioClip driftAsphaltClip;
	[SerializeField] private AudioClip menuSwitchClip;
	[SerializeField] private AudioClip menuSelectClip;
	[SerializeField] private AudioClip checkpointClip;
	[SerializeField] private AudioClip winClip;
	[SerializeField] private AudioClip countdownTickClip;
	[SerializeField] private AudioClip countdownGoClip;
	[SerializeField] private AudioClip pauseClip;
	[SerializeField] private float uiSfxVolume = 0.78f;

	private RaceState state;
	private RaceState stateBeforePause;
	private Canvas uiCanvas;
	private RectTransform menuRoot;
	private RectTransform chooserRoot;
	private RectTransform mapChooserRoot;
	private RectTransform hudRoot;
	private RectTransform pauseRoot;
	private Text countdownText;
	private Text checkpointText;
	private Text timerText;
	private Text finishText;
	private Text vehicleNameText;
	private Text vehicleStatsText;
	private Text selectedMapText;
	private Text menuBestText;
	private Text hudBestText;
	private Font uiFont;
	private Image desertMapCardImage;
	private Image forestMapCardImage;
	private Sprite desertMapThumbnail;
	private Sprite forestMapThumbnail;
	private static Texture2D softHeadlightBeamTexture;
	private static Texture2D softHeadlightRoadGlowTexture;

	private readonly List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();
	private Mesh checkpointMesh;
	private Material checkpointMaterial;
	private GameObject checkpointRoot;
	private GameObject forestMapInstance;
	private GameObject activeMapObject;
	private Transform runtimeMapsRoot;
	private bool forestMapPrepared;
	private Light mapDirectionalLight;
	private GameObject activeCar;
	private GameObject previewCar;
	private Transform previewCameraAnchor;
	private CarInputController activeController;
	private AudioSource uiAudioSource;
	private Vector3 desertSpawnPosition;
	private Quaternion desertSpawnRotation;
	private Vector3 desertSpawnScale = Vector3.one;
	private Vector3 spawnPosition;
	private Quaternion spawnRotation;
	private Vector3 spawnScale = Vector3.one;
	private int nextCheckpointIndex;
	private float raceTimer;
	private float bestLapTime = -1f;
	private bool restartQueued;
	private Coroutine raceRoutine;
	private int raceFlowVersion;

	private const string BestLapTimeKeyPrefix = "Drifty.RaceTrack.BestLapTime.";
	private const string LegacyBestLapTimeKey = "Drifty.DesertRaceTrack.BestLapTime";

	void Awake () {
		ResolveSceneReferences();
		ResolveVehicles();
		ResolveMapAssets();
		ResolveAudioClips();
		EnsureUiAudioSource();
		LoadBestLapTime();
		CreateSharedCheckpointAssets();
		ApplySelectedMap(false);
		CreateUserInterface();
		ShowMainMenu();
	}

	void Start () {
		if (state == RaceState.Menu) {
			UpdateCameraTarget(GetPreviewCameraAnchor());
			KeepPreviewCameraTarget();
		}
	}

	void Update () {
		if (state == RaceState.Menu && previewCar != null) {
			previewCar.transform.Rotate(Vector3.up, 24f * Time.unscaledDeltaTime, Space.World);
		}

		if (state == RaceState.Racing) {
			raceTimer += Time.deltaTime;
			UpdateHud();
		}

		if (PausePressed()) {
			if (state == RaceState.Paused) {
				ResumeRace();
			} else if (state == RaceState.Countdown || state == RaceState.Racing || state == RaceState.Finished) {
				PauseRace();
			}
		}

		if (state != RaceState.Menu && state != RaceState.Paused && ResetPressed() && !restartQueued) {
			BeginRaceFlow();
		}
	}

	public void TryPassCheckpoint (int checkpointIndex, Collider other) {
		if (state != RaceState.Racing || checkpointIndex != nextCheckpointIndex || !ColliderBelongsToActiveCar(other)) {
			return;
		}

		checkpoints[checkpointIndex].SetState(false, true);
		nextCheckpointIndex++;
		PlaySfx(checkpointClip);

		if (nextCheckpointIndex >= checkpoints.Count) {
			FinishRace();
			return;
		}

		checkpoints[nextCheckpointIndex].SetState(true, false);
		UpdateHud();
	}

	private void ResolveSceneReferences () {
		if (startingSceneCar == null) {
			CarInputController[] controllers = FindObjectsByType<CarInputController>(FindObjectsInactive.Include);
			for (int i = 0; i < controllers.Length; i++) {
				if (controllers[i] != null && controllers[i].gameObject.scene.IsValid()) {
					startingSceneCar = controllers[i].gameObject;
					break;
				}
			}
		}

		if (startingSceneCar != null) {
			spawnPosition = startingSceneCar.transform.position;
			spawnRotation = startingSceneCar.transform.rotation;
			spawnScale = startingSceneCar.transform.localScale;
			startingSceneCar.SetActive(false);
		} else if (carSpawnPoint != null) {
			spawnPosition = carSpawnPoint.position;
			spawnRotation = carSpawnPoint.rotation;
			spawnScale = carSpawnPoint.localScale;
		} else {
			spawnPosition = transform.position;
			spawnRotation = transform.rotation;
		}
		desertSpawnPosition = spawnPosition;
		desertSpawnRotation = spawnRotation;
		desertSpawnScale = spawnScale;

		if (raceCamera == null) {
			raceCamera = FindAnyObjectByType<CameraTarget>();
		}

		if (cameraManager == null) {
			cameraManager = FindAnyObjectByType<SetCamera>();
		}
	}

	private void ResolveVehicles () {
		bool loadedFromProject = false;
#if UNITY_EDITOR
		if (vehiclePrefabs == null || vehiclePrefabs.Length == 0) {
			List<GameObject> loadedVehicles = new List<GameObject>();
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new [] { "Assets/PainterCars/Prefabs/Cars" });
			for (int i = 0; i < guids.Length; i++) {
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null || prefab.GetComponentInChildren<CarInputController>(true) == null) continue;
				loadedVehicles.Add(prefab);
			}
			loadedVehicles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
			vehiclePrefabs = loadedVehicles.ToArray();
			loadedFromProject = true;
		}
#endif
		if ((loadedFromProject || selectedVehicleIndex <= 0) && vehiclePrefabs != null) {
			for (int i = 0; i < vehiclePrefabs.Length; i++) {
				if (vehiclePrefabs[i] != null && vehiclePrefabs[i].name == "MuscleCar1") {
					selectedVehicleIndex = i;
					break;
				}
			}
		}

		FilterSelectableVehicles();
	}

	private void ResolveMapAssets () {
		if (desertMapRoot == null) {
			desertMapRoot = GameObject.Find("Desert Race Track");
		}

#if UNITY_EDITOR
		if (forestMapPrefab == null) {
			forestMapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maps/ForestRaceTrack/Prefabs/ForestRaceTrackMap.prefab");
			if (forestMapPrefab == null) {
				forestMapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maps/ForestRaceTrack/Models/ForestRaceTrackMap.obj");
			}
		}
		if (desertSkyboxMaterial == null) {
			desertSkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/SkyboxDemo/textures/1920x1080/Materials/11.mat");
		}
		if (forestSkyboxMaterial == null) {
			forestSkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Maps/ForestRaceTrack/Materials/ForestNightMoonStars.mat");
			if (forestSkyboxMaterial == null) {
				forestSkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/SkyboxDemo/textures/1920x1080/Materials/14.mat");
			}
		}
#endif
		selectedMapIndex = Mathf.Clamp(selectedMapIndex, 0, 1);
	}

	private void FilterSelectableVehicles () {
		if (vehiclePrefabs == null) return;
		List<GameObject> filteredVehicles = new List<GameObject>();
		for (int i = 0; i < vehiclePrefabs.Length; i++) {
			GameObject prefab = vehiclePrefabs[i];
			if (prefab == null) continue;
			if (string.Equals(prefab.name, "TruckAndTrailer", StringComparison.OrdinalIgnoreCase)) continue;
			filteredVehicles.Add(prefab);
		}
		vehiclePrefabs = filteredVehicles.ToArray();
		selectedVehicleIndex = Mathf.Clamp(selectedVehicleIndex, 0, Mathf.Max(0, vehiclePrefabs.Length - 1));
	}

	private void CreateUserInterface () {
		EnsureEventSystem();

		GameObject canvasObject = new GameObject("Race UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		uiCanvas = canvasObject.GetComponent<Canvas>();
		uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
		uiCanvas.sortingOrder = 100;
		CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;

		menuRoot = CreatePanel("Main Menu", canvasObject.transform, new Color(0.012f, 0.014f, 0.018f, 0.94f));
		hudRoot = CreatePanel("Race HUD", canvasObject.transform, new Color(0f, 0f, 0f, 0f));
		chooserRoot = CreatePanel("Vehicle Chooser", menuRoot, new Color(0.028f, 0.032f, 0.04f, 0.90f));
		SetAnchors(chooserRoot, new Vector2(0.58f, 0.14f), new Vector2(0.94f, 0.72f), Vector2.zero, Vector2.zero);
		mapChooserRoot = CreatePanel("Map Chooser", menuRoot, new Color(0.028f, 0.032f, 0.04f, 0.92f));
		SetAnchors(mapChooserRoot, new Vector2(0.58f, 0.14f), new Vector2(0.94f, 0.72f), Vector2.zero, Vector2.zero);
		pauseRoot = CreatePanel("Pause Menu", canvasObject.transform, new Color(0.012f, 0.014f, 0.018f, 0.92f));
		pauseRoot.gameObject.SetActive(false);

		CreateStripe(menuRoot, new Vector2(0.03f, 0f), new Vector2(0.08f, 1f), new Color(0.86f, 0.02f, 0.02f, 0.95f));
		CreateStripe(menuRoot, new Vector2(0.085f, 0f), new Vector2(0.098f, 1f), new Color(1f, 0.72f, 0.08f, 0.85f));
		CreateStripe(menuRoot, new Vector2(0.895f, 0f), new Vector2(0.91f, 1f), new Color(1f, 0.72f, 0.08f, 0.7f));
		CreateStripe(menuRoot, new Vector2(0.92f, 0f), new Vector2(0.975f, 1f), new Color(0.86f, 0.02f, 0.02f, 0.82f));
		CreateCheckeredBand(menuRoot, new Vector2(0.12f, 0.08f), new Vector2(0.54f, 0.16f), 14, 2);
		CreateStripe(menuRoot, new Vector2(0.12f, 0.625f), new Vector2(0.52f, 0.64f), new Color(0.9f, 0.04f, 0.02f, 0.92f));
		CreateStripe(menuRoot, new Vector2(0.12f, 0.60f), new Vector2(0.44f, 0.61f), new Color(1f, 0.68f, 0.06f, 0.82f));

		Text titleShadow = CreateText("Title Shadow", menuRoot, "DRIFTY", 76, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.55f));
		SetAnchors(titleShadow.rectTransform, new Vector2(0.143f, 0.637f), new Vector2(0.555f, 0.835f), Vector2.zero, Vector2.zero);
		Text title = CreateText("Title", menuRoot, "DRIFTY", 76, TextAnchor.MiddleLeft, new Color(1f, 0.94f, 0.74f, 1f));
		SetAnchors(title.rectTransform, new Vector2(0.14f, 0.64f), new Vector2(0.55f, 0.84f), Vector2.zero, Vector2.zero);

		menuBestText = CreateText("Menu Best Lap", menuRoot, "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.78f, 0.18f, 1f));
		SetAnchors(menuBestText.rectTransform, new Vector2(0.145f, 0.555f), new Vector2(0.55f, 0.615f), Vector2.zero, Vector2.zero);

		Button playButton = CreateButton("Play Button", menuRoot, "PLAY", new Color(0.88f, 0.03f, 0.02f, 0.98f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(playButton.GetComponent<RectTransform>(), new Vector2(0.145f, 0.435f), new Vector2(0.37f, 0.535f), Vector2.zero, Vector2.zero);
		playButton.onClick.AddListener(() => {
			PlaySfx(menuSelectClip);
			BeginRaceFlow();
		});

		Button chooseButton = CreateButton("Choose Vehicle Button", menuRoot, "CHOOSE VEHICLE", new Color(0.08f, 0.09f, 0.105f, 0.95f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(chooseButton.GetComponent<RectTransform>(), new Vector2(0.145f, 0.32f), new Vector2(0.37f, 0.42f), Vector2.zero, Vector2.zero);
		chooseButton.onClick.AddListener(() => {
			PlaySfx(menuSwitchClip);
			ShowMenuPanel(chooserRoot);
		});

		Button chooseMapButton = CreateButton("Choose Map Button", menuRoot, "CHOOSE MAP", new Color(0.08f, 0.09f, 0.105f, 0.95f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(chooseMapButton.GetComponent<RectTransform>(), new Vector2(0.145f, 0.205f), new Vector2(0.37f, 0.305f), Vector2.zero, Vector2.zero);
		chooseMapButton.onClick.AddListener(() => {
			PlaySfx(menuSwitchClip);
			ShowMenuPanel(mapChooserRoot);
		});

		vehicleNameText = CreateText("Vehicle Name", chooserRoot, "", 34, TextAnchor.MiddleCenter, new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(vehicleNameText.rectTransform, new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.90f), Vector2.zero, Vector2.zero);

		vehicleStatsText = CreateText("Vehicle Stats", chooserRoot, "", 22, TextAnchor.UpperLeft, new Color(0.88f, 0.92f, 0.95f, 0.95f));
		SetAnchors(vehicleStatsText.rectTransform, new Vector2(0.1f, 0.25f), new Vector2(0.9f, 0.66f), Vector2.zero, Vector2.zero);

		Button previousButton = CreateButton("Previous Vehicle", chooserRoot, "<", new Color(0.9f, 0.05f, 0.03f, 0.95f), Color.white);
		SetAnchors(previousButton.GetComponent<RectTransform>(), new Vector2(0.11f, 0.08f), new Vector2(0.28f, 0.20f), Vector2.zero, Vector2.zero);
		previousButton.onClick.AddListener(() => {
			PlaySfx(menuSwitchClip);
			SelectVehicle(selectedVehicleIndex - 1);
		});

		Button nextButton = CreateButton("Next Vehicle", chooserRoot, ">", new Color(0.9f, 0.05f, 0.03f, 0.95f), Color.white);
		SetAnchors(nextButton.GetComponent<RectTransform>(), new Vector2(0.72f, 0.08f), new Vector2(0.89f, 0.20f), Vector2.zero, Vector2.zero);
		nextButton.onClick.AddListener(() => {
			PlaySfx(menuSwitchClip);
			SelectVehicle(selectedVehicleIndex + 1);
		});

		CreateMapChooser();

		countdownText = CreateText("Countdown", hudRoot, "", 120, TextAnchor.MiddleCenter, new Color(1f, 0.08f, 0.02f, 1f));
		SetAnchors(countdownText.rectTransform, new Vector2(0.35f, 0.36f), new Vector2(0.65f, 0.64f), Vector2.zero, Vector2.zero);

		timerText = CreateText("Timer", hudRoot, "00:00.000", 34, TextAnchor.MiddleLeft, new Color(1f, 0.95f, 0.78f, 1f));
		SetAnchors(timerText.rectTransform, new Vector2(0.035f, 0.90f), new Vector2(0.28f, 0.975f), Vector2.zero, Vector2.zero);

		checkpointText = CreateText("Checkpoint Counter", hudRoot, "", 30, TextAnchor.MiddleRight, new Color(1f, 0.95f, 0.78f, 1f));
		SetAnchors(checkpointText.rectTransform, new Vector2(0.70f, 0.90f), new Vector2(0.965f, 0.975f), Vector2.zero, Vector2.zero);

		hudBestText = CreateText("Best Lap", hudRoot, "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.78f, 0.18f, 1f));
		SetAnchors(hudBestText.rectTransform, new Vector2(0.035f, 0.84f), new Vector2(0.30f, 0.90f), Vector2.zero, Vector2.zero);

		finishText = CreateText("Finish Text", hudRoot, "", 64, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.78f, 1f));
		SetAnchors(finishText.rectTransform, new Vector2(0.25f, 0.40f), new Vector2(0.75f, 0.58f), Vector2.zero, Vector2.zero);

		CreatePauseMenu();

		chooserRoot.gameObject.SetActive(true);
		mapChooserRoot.gameObject.SetActive(false);
		hudRoot.gameObject.SetActive(false);
		UpdateMapSelectionUI();
		UpdateBestLapLabels();
	}

	private void EnsureEventSystem () {
		EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
		if (eventSystem == null) {
			GameObject eventSystemObject = new GameObject("Race Event System", typeof(EventSystem));
			eventSystem = eventSystemObject.GetComponent<EventSystem>();
		}

#if ENABLE_INPUT_SYSTEM
		InputSystemUIInputModule inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
		if (inputSystemModule == null) inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
		inputSystemModule.AssignDefaultActions();
		inputSystemModule.enabled = true;
		StandaloneInputModule standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
		if (standaloneModule != null) standaloneModule.enabled = false;
#else
		StandaloneInputModule standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
		if (standaloneModule == null) standaloneModule = eventSystem.gameObject.AddComponent<StandaloneInputModule>();
		standaloneModule.enabled = true;
#endif
	}

	private void ShowMainMenu () {
		Time.timeScale = 1f;
		state = RaceState.Menu;
		stateBeforePause = RaceState.Menu;
		restartQueued = false;
		raceFlowVersion++;
		raceRoutine = null;
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		if (activeCar != null) Destroy(activeCar);
		activeCar = null;
		activeController = null;
		ApplySelectedMap(false);
		CreateCheckpointGates();
		SetCheckpointStatesForMenu();
		menuRoot.gameObject.SetActive(true);
		hudRoot.gameObject.SetActive(false);
		if (pauseRoot != null) pauseRoot.gameObject.SetActive(false);
		UpdateCameraTarget(GetPreviewCameraAnchor());
		SelectVehicle(Mathf.Clamp(selectedVehicleIndex, 0, Mathf.Max(0, vehiclePrefabs.Length - 1)));
	}

	private void BeginRaceFlow () {
		raceFlowVersion++;
		if (raceRoutine != null) StopCoroutine(raceRoutine);
		raceRoutine = StartCoroutine(StartRaceFlow(raceFlowVersion));
	}

	private IEnumerator StartRaceFlow (int flowId) {
		if (vehiclePrefabs == null || vehiclePrefabs.Length == 0 || state == RaceState.Countdown) yield break;
		Time.timeScale = 1f;
		ApplySelectedMap(false);
		CreateCheckpointGates();
		restartQueued = true;
		state = RaceState.Countdown;
		stateBeforePause = RaceState.Countdown;
		menuRoot.gameObject.SetActive(false);
		hudRoot.gameObject.SetActive(true);
		if (pauseRoot != null) pauseRoot.gameObject.SetActive(false);
		Cursor.visible = false;
		finishText.text = "";
		countdownText.text = "";
		DestroyPreviewCar();
		SpawnSelectedVehicle();
		ResetCheckpointsForRace();
		raceTimer = 0f;
		UpdateHud();

		string[] steps = { "3", "2", "1", "GO!" };
		for (int i = 0; i < steps.Length; i++) {
			if (flowId != raceFlowVersion) yield break;
			countdownText.text = steps[i];
			PlaySfx(i < steps.Length - 1 ? countdownTickClip : countdownGoClip);
			yield return new WaitForSeconds(countdownStepSeconds);
		}

		if (flowId != raceFlowVersion) yield break;
		countdownText.text = "";
		if (activeController != null) activeController.SetInputLocked(false);
		state = RaceState.Racing;
		stateBeforePause = RaceState.Racing;
		restartQueued = false;
		raceRoutine = null;
	}

	private void PauseRace () {
		if (state == RaceState.Menu || state == RaceState.Paused) return;
		stateBeforePause = state;
		state = RaceState.Paused;
		Time.timeScale = 0f;
		if (activeController != null) activeController.SetInputLocked(true);
		if (pauseRoot != null) pauseRoot.gameObject.SetActive(true);
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		PlaySfx(pauseClip != null ? pauseClip : menuSwitchClip);
	}

	private void ResumeRace () {
		if (state != RaceState.Paused) return;
		state = stateBeforePause;
		Time.timeScale = 1f;
		if (pauseRoot != null) pauseRoot.gameObject.SetActive(false);
		Cursor.visible = false;
		if (activeController != null && state == RaceState.Racing) activeController.SetInputLocked(false);
		PlaySfx(menuSelectClip);
	}

	private void ReturnToMainMenu () {
		if (raceRoutine != null) StopCoroutine(raceRoutine);
		raceFlowVersion++;
		raceRoutine = null;
		restartQueued = false;
		Time.timeScale = 1f;
		if (activeController != null) {
			activeController.StopEngine();
			activeController.SetInputLocked(true);
		}
		PlaySfx(menuSelectClip);
		ShowMainMenu();
	}

	private void ShowMenuPanel (RectTransform panel) {
		if (chooserRoot != null) chooserRoot.gameObject.SetActive(panel == chooserRoot);
		if (mapChooserRoot != null) mapChooserRoot.gameObject.SetActive(panel == mapChooserRoot);
	}

	private void SelectMap (MapChoice mapChoice) {
		int mapIndex = (int)mapChoice;
		if (selectedMapIndex == mapIndex) {
			LoadBestLapTime();
			UpdateBestLapLabels();
			UpdateMapSelectionUI();
			return;
		}

		selectedMapIndex = mapIndex;
		LoadBestLapTime();
		ApplySelectedMap(true);
		CreateCheckpointGates();
		UpdateBestLapLabels();
		UpdateMapSelectionUI();
		if (state == RaceState.Menu) {
			DestroyPreviewCar();
			SelectVehicle(Mathf.Clamp(selectedVehicleIndex, 0, Mathf.Max(0, vehiclePrefabs.Length - 1)));
			UpdateCameraTarget(GetPreviewCameraAnchor());
			KeepPreviewCameraTarget();
		}
	}

	private void ApplySelectedMap (bool rebuildPreviewAnchor) {
		MapChoice mapChoice = GetSelectedMapChoice();
		MapChoice appliedMapChoice = MapChoice.Desert;

		if (mapChoice == MapChoice.Forest) {
			SetForestMapInstancesActive(false);
			GameObject forestObject = EnsureForestMapInstance();
			if (forestObject != null) {
				SetDesertMapContentActive(false);
				forestObject.SetActive(true);
				activeMapObject = forestObject;
				appliedMapChoice = MapChoice.Forest;
				SetForestSpawnPose(forestObject);
			} else {
				SetForestMapInstancesActive(false);
				activeMapObject = desertMapRoot;
				spawnPosition = desertSpawnPosition;
				spawnRotation = desertSpawnRotation;
				spawnScale = desertSpawnScale;
				SetDesertMapContentActive(true);
			}
		} else {
			SetForestMapInstancesActive(false);
			SetDesertMapContentActive(true);
			activeMapObject = desertMapRoot;
			spawnPosition = desertSpawnPosition;
			spawnRotation = desertSpawnRotation;
			spawnScale = desertSpawnScale;
		}

		if (rebuildPreviewAnchor && previewCameraAnchor != null) {
			UpdatePreviewCameraAnchorPose();
		}

		ApplyMapSkybox(appliedMapChoice);
		EnsureGameplayCameraActive();
	}

	private void ApplyMapSkybox (MapChoice mapChoice) {
		Material targetSkybox = mapChoice == MapChoice.Forest ? forestSkyboxMaterial : desertSkyboxMaterial;
		if (targetSkybox != null) {
			RenderSettings.skybox = targetSkybox;
			RenderSettings.ambientMode = AmbientMode.Skybox;
		}

		ApplyMapAtmosphere(mapChoice);
		DynamicGI.UpdateEnvironment();
		EnsureMapCamerasUseSkybox();
		ApplyMapDirectionalLight(mapChoice);
	}

	private void ApplyMapAtmosphere (MapChoice mapChoice) {
		if (mapChoice == MapChoice.Forest) {
			RenderSettings.ambientIntensity = 0.64f;
			RenderSettings.reflectionIntensity = 0.42f;
			RenderSettings.fog = true;
			RenderSettings.fogMode = FogMode.ExponentialSquared;
			RenderSettings.fogColor = new Color(0.055f, 0.07f, 0.12f, 1f);
			RenderSettings.fogDensity = 0.0028f;
		} else {
			RenderSettings.ambientIntensity = 1f;
			RenderSettings.reflectionIntensity = 1f;
			RenderSettings.fog = false;
		}
	}

	private void EnsureMapCamerasUseSkybox () {
		Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
		for (int i = 0; i < cameras.Length; i++) {
			if (cameras[i] != null) cameras[i].clearFlags = CameraClearFlags.Skybox;
		}
	}

	private void ApplyMapDirectionalLight (MapChoice mapChoice) {
		if (mapDirectionalLight == null) {
			Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include);
			for (int i = 0; i < lights.Length; i++) {
				if (lights[i] != null && lights[i].type == LightType.Directional) {
					mapDirectionalLight = lights[i];
					break;
				}
			}
		}
		if (mapDirectionalLight == null) return;

		mapDirectionalLight.enabled = true;
		mapDirectionalLight.shadows = LightShadows.Soft;
		mapDirectionalLight.renderMode = LightRenderMode.ForcePixel;
		if (mapChoice == MapChoice.Forest) {
			mapDirectionalLight.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
			mapDirectionalLight.color = new Color(0.72f, 0.82f, 1f, 1f);
			mapDirectionalLight.intensity = 0.72f;
			mapDirectionalLight.shadowStrength = 0.48f;
		} else {
			mapDirectionalLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
			mapDirectionalLight.color = new Color(1f, 0.88f, 0.62f, 1f);
			mapDirectionalLight.intensity = 1.18f;
			mapDirectionalLight.shadowStrength = 0.75f;
		}
	}

	private void SetDesertMapContentActive (bool active) {
		List<GameObject> desertRoots = GetDesertMapRoots();
		for (int rootIndex = 0; rootIndex < desertRoots.Count; rootIndex++) {
			GameObject root = desertRoots[rootIndex];
			if (root == null) continue;
			ActivateTransformPath(root.transform);

			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < renderers.Length; i++) {
				if (renderers[i] != null && CanToggleDesertMapObject(renderers[i].gameObject)) {
					renderers[i].enabled = active;
				}
			}

			Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
			for (int i = 0; i < colliders.Length; i++) {
				if (colliders[i] != null && CanToggleDesertMapObject(colliders[i].gameObject)) {
					colliders[i].enabled = active;
				}
			}
		}
	}

	private List<GameObject> GetDesertMapRoots () {
		List<GameObject> roots = new List<GameObject>();
		AddUniqueRoot(roots, desertMapRoot);
		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null && transforms[i].name == "Desert Race Track") {
				AddUniqueRoot(roots, transforms[i].gameObject);
			}
		}
		return roots;
	}

	private void AddUniqueRoot (List<GameObject> roots, GameObject candidate) {
		if (candidate == null || roots.Contains(candidate)) return;
		roots.Add(candidate);
	}

	private bool CanToggleDesertMapObject (GameObject obj) {
		if (obj == null) return false;
		if (obj.GetComponentInParent<Camera>() != null) return false;
		if (obj.GetComponentInParent<AudioListener>() != null) return false;
		if (obj.GetComponentInParent<Canvas>() != null) return false;
		if (obj.GetComponentInParent<EventSystem>() != null) return false;
		if (obj.GetComponentInParent<RaceGameManager>() != null) return false;
		if (obj.GetComponentInParent<SetCamera>() != null) return false;
		if (obj.GetComponentInParent<CameraTarget>() != null) return false;
		if (obj.GetComponentInParent<CarInputController>() != null) return false;
		if (obj.GetComponentInParent<Light>() != null) return false;
		return IsLikelyDesertMapObject(obj);
	}

	private bool IsLikelyDesertMapObject (GameObject obj) {
		string lower = obj.name.ToLowerInvariant();
		return lower.Contains("asphalt") ||
			lower.Contains("track") ||
			lower.Contains("road") ||
			lower.Contains("sand") ||
			lower.Contains("desert") ||
			lower.Contains("ground") ||
			lower.Contains("terrain") ||
			lower.Contains("grass") ||
			lower.Contains("rock") ||
			lower.Contains("island") ||
			lower.Contains("patch") ||
			lower.Contains("runoff") ||
			lower.Contains("curb") ||
			lower.Contains("barrier") ||
			lower.Contains("guardrail") ||
			lower.Contains("finish") ||
			lower.Contains("scuff") ||
			lower.Contains("circuit");
	}

	private void EnsureGameplayCameraActive () {
		if (raceCamera == null) {
			CameraTarget[] targets = FindObjectsByType<CameraTarget>(FindObjectsInactive.Include);
			if (targets.Length > 0) raceCamera = targets[0];
		}

		if (cameraManager == null) {
			SetCamera[] managers = FindObjectsByType<SetCamera>(FindObjectsInactive.Include);
			if (managers.Length > 0) cameraManager = managers[0];
		}

		if (cameraManager != null) {
			ActivateTransformPath(cameraManager.transform);
			cameraManager.enabled = true;
			ActivateConfiguredCamera();
		}

		if (raceCamera != null) {
			ActivateTransformPath(raceCamera.transform);
			raceCamera.enabled = true;
			Camera camera = raceCamera.GetComponent<Camera>();
			if (camera != null) camera.enabled = true;
			AudioListener listener = raceCamera.GetComponent<AudioListener>();
			if (listener != null) listener.enabled = true;
		}

		if (!HasRenderingCamera()) {
			ActivateFirstAvailableCamera();
		}
	}

	private void ActivateTransformPath (Transform target) {
		if (target == null) return;
		Stack<Transform> path = new Stack<Transform>();
		Transform current = target;
		while (current != null) {
			path.Push(current);
			current = current.parent;
		}

		while (path.Count > 0) {
			Transform transformInPath = path.Pop();
			if (transformInPath != null && !transformInPath.gameObject.activeSelf) {
				transformInPath.gameObject.SetActive(true);
			}
		}
	}

	private bool ActivateConfiguredCamera () {
		FieldInfo camerasField = typeof(SetCamera).GetField("cameras", BindingFlags.Instance | BindingFlags.NonPublic);
		if (camerasField == null || cameraManager == null) return false;

		GameObject[] cameraObjects = camerasField.GetValue(cameraManager) as GameObject[];
		if (cameraObjects == null || cameraObjects.Length == 0) return false;

		FieldInfo currentCameraField = typeof(SetCamera).GetField("currentCamera", BindingFlags.Instance | BindingFlags.NonPublic);
		int currentCamera = 0;
		if (currentCameraField != null) {
			currentCamera = Mathf.Clamp((int)currentCameraField.GetValue(cameraManager), 0, cameraObjects.Length - 1);
		}

		bool activated = false;
		for (int i = 0; i < cameraObjects.Length; i++) {
			GameObject cameraObject = cameraObjects[i];
			if (cameraObject == null) continue;
			bool shouldBeActive = i == currentCamera;
			ActivateTransformPath(cameraObject.transform);
			cameraObject.SetActive(shouldBeActive);

			if (shouldBeActive) {
				Camera camera = cameraObject.GetComponentInChildren<Camera>(true);
				if (camera != null) {
					camera.enabled = true;
					activated = true;
				}
			}
		}

		return activated;
	}

	private bool HasRenderingCamera () {
		Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
		for (int i = 0; i < cameras.Length; i++) {
			if (cameras[i] != null && cameras[i].enabled && cameras[i].gameObject.activeInHierarchy) {
				return true;
			}
		}
		return false;
	}

	private void ActivateFirstAvailableCamera () {
		Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
		for (int i = 0; i < cameras.Length; i++) {
			if (cameras[i] == null) continue;
			ActivateTransformPath(cameras[i].transform);
			cameras[i].enabled = true;
			return;
		}
	}

	private MapChoice GetSelectedMapChoice () {
		return selectedMapIndex == (int)MapChoice.Forest ? MapChoice.Forest : MapChoice.Desert;
	}

	private GameObject EnsureForestMapInstance () {
		if (forestMapInstance == null) {
			forestMapInstance = FindExistingForestMapInstance();
		}

		if (forestMapInstance != null) {
			PrepareForestMapInstance();
			return forestMapInstance;
		}

		if (forestMapPrefab == null) {
			ResolveMapAssets();
		}
		if (forestMapPrefab == null) return null;

		forestMapInstance = Instantiate(forestMapPrefab);
		forestMapInstance.name = "Runtime Forest Race Track Map";
		forestMapInstance.transform.SetParent(GetRuntimeMapsRoot(), false);
		forestMapInstance.transform.position = Vector3.zero;
		forestMapInstance.transform.rotation = Quaternion.identity;
		forestMapInstance.transform.localScale = Vector3.one;
		forestMapPrepared = false;
		PrepareForestMapInstance();
		return forestMapInstance;
	}

	private void PrepareForestMapInstance () {
		if (forestMapInstance == null || forestMapPrepared) return;
		OptimizeForestRuntimeHierarchy(forestMapInstance);
		ForestRuntimeMapEnhancer.Enhance(forestMapInstance);
		RemoveForestWhiteMarkerSticks(forestMapInstance);
		RepairMapMaterials(forestMapInstance);
		EnsureForestFinishLineVisible(forestMapInstance);
		RemoveDecorativeMapColliders(forestMapInstance);
		EnsureMapColliders(forestMapInstance);
		forestMapPrepared = true;
	}

	private GameObject FindExistingForestMapInstance () {
		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null && transforms[i].name == "Runtime Forest Race Track Map") {
				return transforms[i].gameObject;
			}
		}
		return null;
	}

	private void SetForestMapInstancesActive (bool active) {
		List<GameObject> forestInstances = GetForestMapInstances();
		for (int i = 0; i < forestInstances.Count; i++) {
			if (forestInstances[i] != null) forestInstances[i].SetActive(active);
		}
	}

	private List<GameObject> GetForestMapInstances () {
		List<GameObject> instances = new List<GameObject>();
		AddUniqueRoot(instances, forestMapInstance);
		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null && transforms[i].name == "Runtime Forest Race Track Map") {
				AddUniqueRoot(instances, transforms[i].gameObject);
			}
		}
		return instances;
	}

	private void EnsureForestFinishLineVisible (GameObject forestObject) {
		Transform finishLine = FindChildByName(forestObject.transform, "Forest Finish Line");
		if (finishLine == null) return;
		finishLine.gameObject.SetActive(true);

		Transform[] transforms = finishLine.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null) transforms[i].gameObject.SetActive(true);
		}

		Renderer[] renderers = finishLine.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++) {
			if (renderers[i] != null) renderers[i].enabled = true;
		}

		Collider[] colliders = finishLine.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++) {
			if (colliders[i] != null) colliders[i].enabled = false;
		}
	}

	private void RemoveForestWhiteMarkerSticks (GameObject forestObject) {
		if (forestObject == null) return;
		Transform[] children = forestObject.GetComponentsInChildren<Transform>(true);
		for (int i = children.Length - 1; i >= 0; i--) {
			Transform child = children[i];
			if (child == null || child == forestObject.transform) continue;
			string lower = child.name.ToLowerInvariant();
			if (lower == "forest track signs" ||
				lower == "corner marker sign" ||
				lower == "track edge posts" ||
				lower == "road edge marker" ||
				lower == "road scuffs and leaf litter" ||
				lower == "dark tire scuff" ||
				lower == "leaf litter patch") {
				DestroyGeneratedObject(child.gameObject);
			}
		}
	}

	private Transform GetRuntimeMapsRoot () {
		if (runtimeMapsRoot != null) return runtimeMapsRoot;
		GameObject root = GameObject.Find("Runtime Race Maps");
		if (root == null) root = new GameObject("Runtime Race Maps");
		GameObject sceneRoot = GameObject.Find("00_Scene");
		if (sceneRoot != null) {
			Transform runtimeGroup = sceneRoot.transform.Find("06_Runtime");
			if (runtimeGroup == null) {
				GameObject runtimeGroupObject = new GameObject("06_Runtime");
				runtimeGroup = runtimeGroupObject.transform;
				runtimeGroup.SetParent(sceneRoot.transform, false);
			}
			root.transform.SetParent(runtimeGroup, true);
		}
		runtimeMapsRoot = root.transform;
		return runtimeMapsRoot;
	}

	private void OptimizeForestRuntimeHierarchy (GameObject forestObject) {
		if (forestObject == null) return;
		Transform surfaces = GetOrCreateChild(forestObject.transform, "Surfaces");
		Transform dressing = GetOrCreateChild(forestObject.transform, "Set Dressing");
		Transform markers = GetOrCreateChild(forestObject.transform, "Markers");
		Transform[] children = forestObject.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++) {
			Transform child = children[i];
			if (child == null || child == forestObject.transform || child.parent != forestObject.transform) continue;
			string lower = child.name.ToLowerInvariant();
			if (lower == "surfaces" || lower == "set dressing" || lower == "markers") continue;
			if (lower.Contains("asphalt") || lower.Contains("ground") || lower.Contains("shoulder") || lower.Contains("pond")) {
				child.SetParent(surfaces, true);
			} else if (lower.Contains("finish") || lower.Contains("spawn") || lower.Contains("sign")) {
				child.SetParent(markers, true);
			} else {
				child.SetParent(dressing, true);
			}
		}
	}

	private Transform GetOrCreateChild (Transform parent, string childName) {
		Transform existing = parent.Find(childName);
		if (existing != null) return existing;
		GameObject child = new GameObject(childName);
		child.transform.SetParent(parent, false);
		return child.transform;
	}

	private void SetForestSpawnPose (GameObject forestObject) {
		Transform spawn = FindChildByName(forestObject.transform, "Forest Race Spawn Point");
		Transform finishLine = FindChildByName(forestObject.transform, "Forest Finish Line");
		if (finishLine != null) {
			spawnPosition = GetFinishLineSpawnPosition(finishLine);
			spawnRotation = GetFinishLineSpawnRotation(finishLine);
			spawnScale = spawn != null && spawn.localScale != Vector3.zero ? spawn.localScale : Vector3.one;
			if (spawn != null) {
				spawn.position = spawnPosition;
				spawn.rotation = spawnRotation;
			}
			return;
		}

		if (spawn != null) {
			spawnPosition = spawn.position;
			spawnRotation = spawn.rotation;
			spawnScale = spawn.localScale == Vector3.zero ? Vector3.one : spawn.localScale;
			return;
		}

		Vector3 trackStart = new Vector3(-42f, 0f, -38f);
		spawnPosition = trackStart + Vector3.up * 0.35f;
		spawnRotation = GetForestStartRotation();
		spawnScale = Vector3.one;
	}

	private Vector3 GetFinishLineSpawnPosition (Transform finishLine) {
		Transform[] transforms = finishLine.GetComponentsInChildren<Transform>(true);
		Vector3 total = Vector3.zero;
		int count = 0;
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] == null || transforms[i] == finishLine || !IsFinishLineTile(transforms[i].name)) continue;
			total += transforms[i].position;
			count++;
		}
		if (count > 0) {
			Vector3 center = total / count;
			return new Vector3(center.x, 0.35f, center.z);
		}

		Renderer[] renderers = finishLine.GetComponentsInChildren<Renderer>(true);
		if (renderers.Length == 0) return finishLine.position + Vector3.up * 0.35f;

		Bounds bounds = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++) {
			if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
		}

		return new Vector3(bounds.center.x, 0.35f, bounds.center.z);
	}

	private Quaternion GetFinishLineSpawnRotation (Transform finishLine) {
		Transform[] transforms = finishLine.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null && transforms[i] != finishLine && IsFinishLineTile(transforms[i].name)) {
				return transforms[i].rotation;
			}
		}
		return finishLine.rotation;
	}

	private bool IsFinishLineTile (string objectName) {
		return !string.IsNullOrEmpty(objectName) &&
			objectName.IndexOf("Finish", StringComparison.OrdinalIgnoreCase) >= 0 &&
			objectName.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private Quaternion GetForestStartRotation () {
		Vector3 trackStart = new Vector3(-42f, 0f, -38f);
		Vector3 trackNext = new Vector3(-12f, 0f, -49f);
		Vector3 tangent = (trackNext - trackStart).normalized;
		return Quaternion.LookRotation(tangent, Vector3.up);
	}

	private Transform FindChildByName (Transform root, string childName) {
		if (root == null) return null;
		if (string.Equals(root.name, childName, StringComparison.OrdinalIgnoreCase)) return root;
		for (int i = 0; i < root.childCount; i++) {
			Transform found = FindChildByName(root.GetChild(i), childName);
			if (found != null) return found;
		}
		return null;
	}

	private void SpawnSelectedVehicle () {
		if (activeCar != null) Destroy(activeCar);
		GameObject prefab = vehiclePrefabs[Mathf.Clamp(selectedVehicleIndex, 0, vehiclePrefabs.Length - 1)];
		activeCar = Instantiate(prefab, spawnPosition, spawnRotation);
		activeCar.name = GetVehicleDisplayName(prefab.name) + " Player";
		activeCar.transform.localScale = spawnScale;
		RepairVehicleMaterials(activeCar, GetVehicleProfile(prefab.name).accentColor);
		ConfigureForestVehicleLights(activeCar, prefab.name);

		activeController = activeCar.GetComponentInChildren<CarInputController>(true);
		VehicleProfile profile = GetVehicleProfile(prefab.name);
		Rigidbody body = activeCar.GetComponent<Rigidbody>();
		if (body != null) {
			body.mass = profile.mass;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.interpolation = RigidbodyInterpolation.Interpolate;
		}

		if (activeController != null) {
			activeController.carInFocus = true;
			activeController.SetRaceStartPose(spawnPosition, spawnRotation);
			activeController.ConfigureRaceHandling(profile.maxSpeed, profile.power, profile.brake, profile.rearDriftGrip, profile.yawAssist, profile.sideAssist, profile.offTrackPower, profile.steeringReduction, profile.stability, profile.downforce);
			ConfigureVehicleAudio(activeController, prefab.name);
			activeController.SetInputLocked(true);
			activeController.ResetToRaceStart();
			activeController.StartEngine();
		}

		UpdateCameraTarget(activeController != null ? activeController.transform : activeCar.transform);
	}

	private void ConfigureForestVehicleLights (GameObject vehicle, string vehicleName) {
		if (vehicle == null || GetSelectedMapChoice() != MapChoice.Forest) return;

		Transform existingLights = vehicle.transform.Find("Forest Vehicle Lights");
		if (existingLights != null) DestroyGeneratedObject(existingLights.gameObject);

		Bounds bounds = GetVehicleLocalBounds(vehicle);
		VehicleLightProfile profile = GetVehicleLightProfile(vehicleName);
		VehicleLightLayout layout = GetVehicleLightLayout(vehicle, bounds);
		float maxLightX = Mathf.Max(0.36f, bounds.extents.x * 0.9f);
		float xOffset = Mathf.Clamp(layout.xOffset * profile.xScale, 0.34f, maxLightX);
		float frontZ = Mathf.Lerp(layout.frontZ, bounds.max.z, profile.frontReach) - profile.frontInset;
		float rearZ = Mathf.Lerp(layout.rearZ, bounds.min.z, profile.rearReach) + profile.rearInset;
		float frontY = Mathf.Lerp(bounds.min.y, bounds.max.y, profile.heightRatio) + profile.frontVerticalOffset;
		float rearY = Mathf.Lerp(bounds.min.y, bounds.max.y, Mathf.Max(0.3f, profile.heightRatio - 0.08f)) + profile.rearVerticalOffset;
		frontY = Mathf.Clamp(frontY, layout.wheelY + 0.08f, Mathf.Max(layout.wheelY + 0.18f, bounds.max.y - 0.08f));
		rearY = Mathf.Clamp(rearY, layout.wheelY + 0.05f, Mathf.Max(layout.wheelY + 0.16f, bounds.max.y - 0.1f));
		Quaternion headlightRotation = Quaternion.LookRotation(new Vector3(0f, -profile.headlightTilt, 1f).normalized, Vector3.up);

		GameObject root = new GameObject("Forest Vehicle Lights");
		root.transform.SetParent(vehicle.transform, false);

		Color headlightColor = new Color(1f, 0.92f, 0.72f, 1f);
		Color backlightColor = new Color(1f, 0.04f, 0.015f, 1f);
		Vector3 leftHeadlight = new Vector3(-xOffset, frontY, frontZ);
		Vector3 rightHeadlight = new Vector3(xOffset, frontY, frontZ);
		Vector3 leftLightOrigin = leftHeadlight + Vector3.forward * 0.18f;
		Vector3 rightLightOrigin = rightHeadlight + Vector3.forward * 0.18f;
		CreateVehicleSpotLight(root.transform, "Left Headlight", leftLightOrigin, headlightRotation, headlightColor, profile.headlightIntensity, profile.headlightRange, profile.headlightSpotAngle);
		CreateVehicleSpotLight(root.transform, "Right Headlight", rightLightOrigin, headlightRotation, headlightColor, profile.headlightIntensity, profile.headlightRange, profile.headlightSpotAngle);
		CreateVehiclePointLight(root.transform, "Left Headlight Glow", leftLightOrigin + Vector3.forward * 0.08f, headlightColor, 2.8f, 8.5f, "Headlight Glow", new Vector3(0.3f, 0.13f, 0.065f));
		CreateVehiclePointLight(root.transform, "Right Headlight Glow", rightLightOrigin + Vector3.forward * 0.08f, headlightColor, 2.8f, 8.5f, "Headlight Glow", new Vector3(0.3f, 0.13f, 0.065f));
		CreateVehicleHeadlightBeam(root.transform, "Left Headlight Beam", leftLightOrigin, headlightColor, profile.headlightRange);
		CreateVehicleHeadlightBeam(root.transform, "Right Headlight Beam", rightLightOrigin, headlightColor, profile.headlightRange);
		CreateVehicleHeadlightRoadGlow(root.transform, "Left Headlight Road Glow", leftLightOrigin, headlightColor, layout.wheelY, profile.headlightRange);
		CreateVehicleHeadlightRoadGlow(root.transform, "Right Headlight Road Glow", rightLightOrigin, headlightColor, layout.wheelY, profile.headlightRange);
		CreateVehiclePointLight(root.transform, "Left Backlight", new Vector3(-xOffset, rearY, rearZ), backlightColor, 1.55f, 8.5f, "Backlight Lens", new Vector3(0.14f, 0.075f, 0.045f));
		CreateVehiclePointLight(root.transform, "Right Backlight", new Vector3(xOffset, rearY, rearZ), backlightColor, 1.55f, 8.5f, "Backlight Lens", new Vector3(0.14f, 0.075f, 0.045f));
	}

	private VehicleLightProfile GetVehicleLightProfile (string vehicleName) {
		string lower = string.IsNullOrEmpty(vehicleName) ? string.Empty : vehicleName.ToLowerInvariant();
		VehicleLightProfile profile = new VehicleLightProfile {
			xScale = 0.78f,
			heightRatio = 0.39f,
			frontReach = 0.82f,
			rearReach = 0.86f,
			frontInset = 0.03f,
			rearInset = 0.04f,
			frontVerticalOffset = 0f,
			rearVerticalOffset = -0.03f,
			headlightTilt = 0.13f,
			headlightIntensity = 15f,
			headlightRange = 68f,
			headlightSpotAngle = 76f
		};

		if (lower.Contains("car3")) {
			profile.xScale = 0.7f;
			profile.heightRatio = 0.34f;
			profile.frontReach = 0.9f;
			profile.rearReach = 0.9f;
			profile.headlightIntensity = 13f;
			profile.headlightRange = 60f;
		} else if (lower.Contains("muscle")) {
			profile.xScale = 0.76f;
			profile.heightRatio = 0.33f;
			profile.frontReach = 0.88f;
			profile.rearReach = 0.9f;
			profile.frontVerticalOffset = 0.03f;
			profile.headlightIntensity = 14f;
			profile.headlightRange = 64f;
		} else if (lower.Contains("pickup")) {
			profile.xScale = 0.8f;
			profile.heightRatio = 0.42f;
			profile.frontReach = 0.85f;
			profile.rearReach = 0.84f;
			profile.headlightRange = 72f;
		} else if (lower.Contains("suv")) {
			profile.xScale = 0.82f;
			profile.heightRatio = 0.43f;
			profile.frontReach = 0.84f;
			profile.rearReach = 0.86f;
			profile.headlightRange = 74f;
		} else if (lower.Contains("ambulance") || lower.Contains("van")) {
			profile.xScale = 0.82f;
			profile.heightRatio = 0.44f;
			profile.frontReach = 0.86f;
			profile.rearReach = 0.88f;
			profile.frontVerticalOffset = -0.04f;
			profile.headlightTilt = 0.15f;
			profile.headlightRange = 76f;
		} else if (lower.Contains("fire")) {
			profile.xScale = 0.82f;
			profile.heightRatio = 0.47f;
			profile.frontReach = 0.9f;
			profile.rearReach = 0.86f;
			profile.frontVerticalOffset = 0.03f;
			profile.headlightTilt = 0.16f;
			profile.headlightIntensity = 21f;
			profile.headlightRange = 82f;
			profile.headlightSpotAngle = 80f;
		} else if (lower.Contains("truck")) {
			profile.xScale = 0.8f;
			profile.heightRatio = 0.48f;
			profile.frontReach = 0.9f;
			profile.rearReach = 0.88f;
			profile.frontVerticalOffset = 0.03f;
			profile.headlightTilt = 0.16f;
			profile.headlightIntensity = 20f;
			profile.headlightRange = 86f;
			profile.headlightSpotAngle = 80f;
		} else if (lower.Contains("bus")) {
			profile.xScale = 0.82f;
			profile.heightRatio = 0.46f;
			profile.frontReach = 0.9f;
			profile.rearReach = 0.84f;
			profile.frontVerticalOffset = 0.02f;
			profile.headlightTilt = 0.15f;
			profile.headlightIntensity = 20f;
			profile.headlightRange = 84f;
			profile.headlightSpotAngle = 80f;
		}

		return profile;
	}

	private VehicleLightLayout GetVehicleLightLayout (GameObject vehicle, Bounds bounds) {
		VehicleLightLayout layout = new VehicleLightLayout {
			frontZ = bounds.max.z,
			rearZ = bounds.min.z,
			xOffset = Mathf.Max(0.42f, bounds.extents.x * 0.55f),
			wheelY = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.18f)
		};

		WheelCollider[] wheelColliders = vehicle.GetComponentsInChildren<WheelCollider>(true);
		if (wheelColliders == null || wheelColliders.Length == 0) return layout;

		float frontZ = float.NegativeInfinity;
		float rearZ = float.PositiveInfinity;
		Vector3[] localPositions = new Vector3[wheelColliders.Length];
		for (int i = 0; i < wheelColliders.Length; i++) {
			if (wheelColliders[i] == null) continue;
			Vector3 localPosition = vehicle.transform.InverseTransformPoint(wheelColliders[i].transform.position);
			localPositions[i] = localPosition;
			frontZ = Mathf.Max(frontZ, localPosition.z);
			rearZ = Mathf.Min(rearZ, localPosition.z);
		}

		if (float.IsInfinity(frontZ) || float.IsInfinity(rearZ)) return layout;

		float wheelbase = Mathf.Max(0.1f, frontZ - rearZ);
		float frontBand = Mathf.Max(0.18f, wheelbase * 0.12f);
		float xSum = 0f;
		float ySum = 0f;
		int frontWheelCount = 0;
		for (int i = 0; i < localPositions.Length; i++) {
			if (localPositions[i].z < frontZ - frontBand) continue;
			xSum += Mathf.Abs(localPositions[i].x);
			ySum += localPositions[i].y;
			frontWheelCount++;
		}

		layout.frontZ = frontZ;
		layout.rearZ = rearZ;
		if (frontWheelCount > 0) {
			layout.xOffset = Mathf.Max(0.34f, xSum / frontWheelCount);
			layout.wheelY = ySum / frontWheelCount;
		}
		return layout;
	}

	private Bounds GetVehicleLocalBounds (GameObject vehicle) {
		Renderer[] renderers = vehicle.GetComponentsInChildren<Renderer>(true);
		if (renderers.Length == 0) return new Bounds(Vector3.zero, new Vector3(1.8f, 1.1f, 3.8f));

		Bounds bounds = new Bounds(vehicle.transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
		for (int i = 0; i < renderers.Length; i++) {
			if (renderers[i] == null) continue;
			Vector3 localCenter = vehicle.transform.InverseTransformPoint(renderers[i].bounds.center);
			Vector3 localExtents = vehicle.transform.InverseTransformVector(renderers[i].bounds.extents);
			localExtents = new Vector3(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y), Mathf.Abs(localExtents.z));
			bounds.Encapsulate(localCenter - localExtents);
			bounds.Encapsulate(localCenter + localExtents);
		}
		return bounds;
	}

	private void CreateVehicleSpotLight (Transform parent, string name, Vector3 localPosition, Quaternion localRotation, Color color, float intensity, float range, float spotAngle) {
		GameObject lightObject = new GameObject(name);
		lightObject.transform.SetParent(parent, false);
		lightObject.transform.localPosition = localPosition;
		lightObject.transform.localRotation = localRotation;
		Light light = lightObject.AddComponent<Light>();
		light.type = LightType.Spot;
		light.color = color;
		light.intensity = intensity;
		light.range = range;
		light.spotAngle = spotAngle;
		light.innerSpotAngle = Mathf.Max(10f, spotAngle * 0.42f);
		light.bounceIntensity = 0.35f;
		light.renderMode = LightRenderMode.ForcePixel;
		light.shadows = LightShadows.None;
		CreateVehicleLightLens(lightObject.transform, "Headlight Lens", color, new Vector3(0.32f, 0.16f, 0.11f), Vector3.forward * 0.1f);
	}

	private void CreateVehicleHeadlightBeam (Transform parent, string name, Vector3 localPosition, Color color, float range) {
		float length = Mathf.Clamp(range * 0.16f, 7.5f, 12f);
		float nearHalfWidth = 0.2f;
		float farHalfWidth = Mathf.Clamp(length * 0.34f, 2.5f, 4.3f);
		Mesh mesh = new Mesh();
		mesh.name = name + " Mesh";
		mesh.vertices = new Vector3[] {
			new Vector3(-nearHalfWidth, 0.04f, 0.1f),
			new Vector3(-farHalfWidth, -0.52f, length),
			new Vector3(farHalfWidth, -0.52f, length),
			new Vector3(nearHalfWidth, 0.04f, 0.1f)
		};
		mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
		mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
		mesh.RecalculateBounds();
		mesh.RecalculateNormals();

		GameObject beam = new GameObject(name);
		beam.transform.SetParent(parent, false);
		beam.transform.localPosition = localPosition + Vector3.forward * 0.18f;
		MeshFilter filter = beam.AddComponent<MeshFilter>();
		filter.sharedMesh = mesh;
		MeshRenderer renderer = beam.AddComponent<MeshRenderer>();
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		renderer.sharedMaterial = CreateHeadlightEffectMaterial(name, color, 0.11f, 1.5f);
	}

	private void CreateVehicleHeadlightRoadGlow (Transform parent, string name, Vector3 localPosition, Color color, float wheelY, float range) {
		float length = Mathf.Clamp(range * 0.11f, 6.5f, 9.5f);
		float width = Mathf.Clamp(length * 0.52f, 3.2f, 5.2f);
		GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
		glow.name = name;
		glow.transform.SetParent(parent, false);
		float glowY = Mathf.Max(wheelY + 0.12f, localPosition.y - 0.35f);
		glow.transform.localPosition = new Vector3(localPosition.x, glowY, localPosition.z + length * 0.55f);
		glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		glow.transform.localScale = new Vector3(width, length, 1f);
		Collider collider = glow.GetComponent<Collider>();
		if (collider != null) {
			collider.enabled = false;
			DestroyGeneratedObject(collider);
		}
		MeshRenderer renderer = glow.GetComponent<MeshRenderer>();
		if (renderer != null) {
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.sharedMaterial = CreateHeadlightEffectMaterial(name, color, 0.16f, 1.7f);
		}
	}

	private void CreateVehiclePointLight (Transform parent, string name, Vector3 localPosition, Color color, float intensity, float range) {
		CreateVehiclePointLight(parent, name, localPosition, color, intensity, range, "Backlight Lens", new Vector3(0.14f, 0.075f, 0.045f));
	}

	private void CreateVehiclePointLight (Transform parent, string name, Vector3 localPosition, Color color, float intensity, float range, string lensName, Vector3 lensScale) {
		GameObject lightObject = new GameObject(name);
		lightObject.transform.SetParent(parent, false);
		lightObject.transform.localPosition = localPosition;
		Light light = lightObject.AddComponent<Light>();
		light.type = LightType.Point;
		light.color = color;
		light.intensity = intensity;
		light.range = range;
		light.bounceIntensity = 0.28f;
		light.renderMode = LightRenderMode.ForcePixel;
		light.shadows = LightShadows.None;
		CreateVehicleLightLens(lightObject.transform, lensName, color, lensScale);
	}

	private void CreateVehicleLightLens (Transform parent, string name, Color color, Vector3 scale) {
		CreateVehicleLightLens(parent, name, color, scale, Vector3.zero);
	}

	private void CreateVehicleLightLens (Transform parent, string name, Color color, Vector3 scale, Vector3 localPosition) {
		GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		lens.name = name;
		lens.transform.SetParent(parent, false);
		lens.transform.localPosition = localPosition;
		lens.transform.localScale = scale;
		Collider collider = lens.GetComponent<Collider>();
		if (collider != null) {
			collider.enabled = false;
			DestroyGeneratedObject(collider);
		}
		MeshRenderer renderer = lens.GetComponent<MeshRenderer>();
		if (renderer != null) renderer.sharedMaterial = CreateVehicleLightMaterial(name, color);
	}

	private Material CreateVehicleLightMaterial (string name, Color color) {
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		Material material = new Material(shader);
		material.name = "Runtime " + name;
		SetMaterialColor(material, color);
		if (material.HasProperty("_EmissionColor")) {
			float emissionMultiplier = name.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0 ? 2.8f : 5.2f;
			material.EnableKeyword("_EMISSION");
			material.SetColor("_EmissionColor", color * emissionMultiplier);
		}
		return material;
	}

	private Material CreateHeadlightEffectMaterial (string name, Color color, float alpha, float emissionMultiplier) {
		Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
		if (shader == null) shader = Shader.Find("Sprites/Default");
		if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
		Material material = new Material(shader);
		material.name = "Runtime " + name;
		Color effectColor = new Color(color.r, color.g, color.b, alpha);
		SetMaterialColor(material, effectColor);
		SetMaterialTexture(material, GetSoftHeadlightTexture(name.ToLowerInvariant().Contains("beam")));
		if (material.HasProperty("_EmissionColor")) {
			material.EnableKeyword("_EMISSION");
			material.SetColor("_EmissionColor", color * emissionMultiplier);
		}
		MakeMaterialTransparent(material);
		if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
		if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
		if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.One);
		material.renderQueue = (int)RenderQueue.Transparent + 20;
		return material;
	}

	private Texture2D GetSoftHeadlightTexture (bool beam) {
		if (beam) {
			if (softHeadlightBeamTexture == null) {
				softHeadlightBeamTexture = CreateSoftHeadlightTexture("Runtime Soft Headlight Beam Texture", true);
			}
			return softHeadlightBeamTexture;
		}

		if (softHeadlightRoadGlowTexture == null) {
			softHeadlightRoadGlowTexture = CreateSoftHeadlightTexture("Runtime Soft Headlight Road Glow Texture", false);
		}
		return softHeadlightRoadGlowTexture;
	}

	private Texture2D CreateSoftHeadlightTexture (string name, bool beam) {
		const int width = 64;
		const int height = 128;
		Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
		texture.name = name;
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;

		for (int y = 0; y < height; y++) {
			float v = y / (float)(height - 1);
			for (int x = 0; x < width; x++) {
				float u = (x / (float)(width - 1)) * 2f - 1f;
				float alpha;
				if (beam) {
					float halfWidth = Mathf.Lerp(0.24f, 1f, v);
					float lateral = Mathf.Clamp01(Mathf.Abs(u) / halfWidth);
					float sideFade = 1f - Mathf.SmoothStep(0.42f, 1f, lateral);
					float nearFade = Mathf.SmoothStep(0.02f, 0.18f, v);
					float farFade = 1f - Mathf.SmoothStep(0.68f, 1f, v);
					alpha = sideFade * nearFade * farFade;
				} else {
					float vertical = v * 2f - 1f;
					float ellipse = u * u * 0.82f + vertical * vertical * 0.52f;
					alpha = 1f - Mathf.SmoothStep(0.16f, 1f, ellipse);
				}

				texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
			}
		}

		texture.Apply(false, true);
		return texture;
	}

	private void DestroyGeneratedObject (UnityEngine.Object target) {
		if (target == null) return;
#if UNITY_EDITOR
		if (!Application.isPlaying) {
			DestroyImmediate(target);
			return;
		}
#endif
		Destroy(target);
	}

	private void SelectVehicle (int index) {
		if (vehiclePrefabs == null || vehiclePrefabs.Length == 0) return;
		if (index < 0) index = vehiclePrefabs.Length - 1;
		if (index >= vehiclePrefabs.Length) index = 0;
		selectedVehicleIndex = index;
		if (state == RaceState.Menu) KeepPreviewCameraTarget();
		GameObject prefab = vehiclePrefabs[selectedVehicleIndex];
		VehicleProfile profile = GetVehicleProfile(prefab.name);
		vehicleNameText.text = profile.displayName.ToUpperInvariant();
		vehicleStatsText.text =
			"TOP SPEED  " + Mathf.RoundToInt(profile.maxSpeed * 7.2f) + " KM/H\n" +
			"POWER      " + Mathf.RoundToInt(profile.power) + "\n" +
			"BRAKING    " + Mathf.RoundToInt(profile.brake) + "\n" +
			"TRAIT      " + profile.trait.ToUpperInvariant();
		CreatePreviewCar(prefab, profile);
	}

	private void CreatePreviewCar (GameObject prefab, VehicleProfile profile) {
		DestroyPreviewCar();
		previewCar = Instantiate(prefab, spawnPosition, spawnRotation);
		previewCar.name = "Preview " + profile.displayName;
		previewCar.transform.localScale = spawnScale * 1.08f;
		RepairVehicleMaterials(previewCar, profile.accentColor);

		CarInputController controller = previewCar.GetComponentInChildren<CarInputController>(true);
		if (controller != null) controller.enabled = false;
		Rigidbody body = previewCar.GetComponent<Rigidbody>();
		if (body != null) {
			body.isKinematic = true;
			body.useGravity = false;
		}
		Collider[] colliders = previewCar.GetComponentsInChildren<Collider>();
		for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
		AudioSource[] sources = previewCar.GetComponentsInChildren<AudioSource>();
		for (int i = 0; i < sources.Length; i++) sources[i].enabled = false;
	}

	private void DestroyPreviewCar () {
		if (previewCar != null) {
			DestroyPreviewInstance(previewCar);
			previewCar = null;
		}

		Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
		for (int i = 0; i < transforms.Length; i++) {
			if (transforms[i] != null && transforms[i].parent == null && transforms[i].name.StartsWith("Preview ", StringComparison.OrdinalIgnoreCase)) {
				DestroyPreviewInstance(transforms[i].gameObject);
			}
		}
	}

	private void DestroyPreviewInstance (GameObject previewObject) {
		if (previewObject == null) return;
#if UNITY_EDITOR
		DestroyImmediate(previewObject);
#else
		Destroy(previewObject);
#endif
	}

	private void UpdateCameraTarget (Transform target) {
		if (target == null) return;
		CameraTarget[] targets = FindObjectsByType<CameraTarget>(FindObjectsInactive.Include);
		for (int i = 0; i < targets.Length; i++) {
			targets[i].target = target;
			targets[i].SetPosition();
		}

		if (cameraManager != null && activeCar != null) {
			FieldInfo carsField = typeof(SetCamera).GetField("cars", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo currentCarField = typeof(SetCamera).GetField("currentCar", BindingFlags.Instance | BindingFlags.NonPublic);
			GameObject focusedCar = activeController != null ? activeController.gameObject : activeCar;
			if (carsField != null) carsField.SetValue(cameraManager, new [] { focusedCar });
			if (currentCarField != null) currentCarField.SetValue(cameraManager, 0);
			cameraManager.transform.position = activeCar.transform.position;
		}
	}

	private Transform GetPreviewCameraAnchor () {
		if (previewCameraAnchor != null) return previewCameraAnchor;
		GameObject anchorObject = new GameObject("Menu Vehicle Camera Anchor");
		previewCameraAnchor = anchorObject.transform;
		UpdatePreviewCameraAnchorPose();
		return previewCameraAnchor;
	}

	private void UpdatePreviewCameraAnchorPose () {
		if (previewCameraAnchor == null) return;
		previewCameraAnchor.position = spawnPosition + Vector3.up * 0.65f;
		previewCameraAnchor.rotation = spawnRotation;
	}

	private void KeepPreviewCameraTarget () {
		Transform anchor = GetPreviewCameraAnchor();
		CameraTarget[] targets = FindObjectsByType<CameraTarget>(FindObjectsInactive.Include);
		for (int i = 0; i < targets.Length; i++) {
			targets[i].target = anchor;
		}
	}

	private void CreateSharedCheckpointAssets () {
		checkpointMesh = CreateHalfRingMesh(4.7f, 0.24f, 28, 8);
		Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
		if (shader == null) shader = Shader.Find("Unlit/Color");
		checkpointMaterial = new Material(shader);
		checkpointMaterial.name = "Runtime Checkpoint Red";
		SetMaterialColor(checkpointMaterial, new Color(1f, 0.05f, 0.03f, 0.95f));
		if (checkpointMaterial.HasProperty("_EmissionColor")) {
			checkpointMaterial.EnableKeyword("_EMISSION");
			checkpointMaterial.SetColor("_EmissionColor", new Color(1f, 0.05f, 0.03f, 1f) * 2f);
		}
	}

	private void CreateCheckpointGates () {
		if (checkpointRoot != null) Destroy(checkpointRoot);
		checkpointRoot = new GameObject("Generated Race Checkpoints");
		checkpoints.Clear();

		List<Vector3> centerLine = GetAsphaltCenterLine();
		if (centerLine.Count < 4) {
			centerLine = GetFallbackCenterLine();
		}

		List<Vector3> orderedLine = OrderCenterLineFromSpawn(centerLine);
		float totalLength = GetPolylineLength(orderedLine);
		int targetCount = Mathf.Clamp(Mathf.RoundToInt(totalLength / checkpointSpacing), minimumCheckpointCount, maximumCheckpointCount);
		float spacing = totalLength / (targetCount + 1);
		float nextDistance = spacing;
		float walked = 0f;

		for (int i = 1; i < orderedLine.Count && checkpoints.Count < targetCount; i++) {
			Vector3 previous = orderedLine[i - 1];
			Vector3 current = orderedLine[i];
			float segmentLength = Vector3.Distance(previous, current);
			while (walked + segmentLength >= nextDistance && checkpoints.Count < targetCount) {
				float segmentT = Mathf.InverseLerp(walked, walked + segmentLength, nextDistance);
				Vector3 position = Vector3.Lerp(previous, current, segmentT);
				Vector3 direction = (current - previous).normalized;
				CreateCheckpointGate(checkpoints.Count, position, direction);
				nextDistance += spacing;
			}
			walked += segmentLength;
		}

		SetCheckpointStatesForMenu();
	}

	private void CreateCheckpointGate (int index, Vector3 position, Vector3 direction) {
		GameObject gate = new GameObject("Checkpoint " + (index + 1).ToString("00"));
		gate.transform.SetParent(checkpointRoot.transform, false);
		gate.transform.position = new Vector3(position.x, 0.16f, position.z);
		if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
		gate.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

		GameObject visual = new GameObject("Red Half Ring");
		visual.transform.SetParent(gate.transform, false);
		MeshFilter filter = visual.AddComponent<MeshFilter>();
		filter.sharedMesh = checkpointMesh;
		MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
		renderer.sharedMaterial = checkpointMaterial;
		renderer.shadowCastingMode = ShadowCastingMode.On;
		renderer.receiveShadows = true;

		BoxCollider trigger = gate.AddComponent<BoxCollider>();
		trigger.isTrigger = true;
		trigger.center = new Vector3(0f, 2.45f, 0f);
		trigger.size = new Vector3(10.3f, 5.1f, 4.2f);

		RaceCheckpoint checkpoint = gate.AddComponent<RaceCheckpoint>();
		checkpoint.Initialize(this, index);
		checkpoints.Add(checkpoint);
	}

	private List<Vector3> GetAsphaltCenterLine () {
		List<Vector3> centerLine = new List<Vector3>();
		GameObject asphalt = FindAsphaltObject();
		if (asphalt == null) return centerLine;
		MeshFilter filter = asphalt.GetComponent<MeshFilter>();
		if (filter == null || filter.sharedMesh == null) return centerLine;
		Vector3[] vertices = filter.sharedMesh.vertices;
		for (int i = 0; i + 1 < vertices.Length; i += 2) {
			Vector3 left = asphalt.transform.TransformPoint(vertices[i]);
			Vector3 right = asphalt.transform.TransformPoint(vertices[i + 1]);
			Vector3 center = (left + right) * 0.5f;
			if (centerLine.Count == 0 || Vector3.Distance(centerLine[centerLine.Count - 1], center) > 0.4f) {
				centerLine.Add(center);
			}
		}
		return centerLine;
	}

	private GameObject FindAsphaltObject () {
		GameObject asphalt = FindNamedMeshInMap(activeMapObject, "asphalt", "circuit");
		if (asphalt != null) return asphalt;
		asphalt = GameObject.Find("Asphalt Winding Circuit");
		if (asphalt != null) return asphalt;
		asphalt = GameObject.Find("Asphalt_Winding_Circuit");
		if (asphalt != null) return asphalt;

		MeshFilter[] filters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
		for (int i = 0; i < filters.Length; i++) {
			if (filters[i] == null) continue;
			string normalized = filters[i].name.Replace("_", " ").ToLowerInvariant();
			if (normalized.Contains("asphalt") && normalized.Contains("circuit")) return filters[i].gameObject;
		}
		return null;
	}

	private GameObject FindNamedMeshInMap (GameObject root, string firstTerm, string secondTerm) {
		if (root == null) return null;
		MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(false);
		for (int i = 0; i < filters.Length; i++) {
			if (filters[i] == null) continue;
			string normalized = filters[i].name.Replace("_", " ").ToLowerInvariant();
			if (normalized.Contains(firstTerm) && normalized.Contains(secondTerm)) return filters[i].gameObject;
		}
		return null;
	}

	private List<Vector3> OrderCenterLineFromSpawn (List<Vector3> points) {
		List<Vector3> ordered = new List<Vector3>();
		if (points.Count == 0) return ordered;
		int nearest = 0;
		float bestDistance = float.MaxValue;
		for (int i = 0; i < points.Count; i++) {
			float distance = (points[i] - spawnPosition).sqrMagnitude;
			if (distance < bestDistance) {
				bestDistance = distance;
				nearest = i;
			}
		}

		Vector3 spawnForward = spawnRotation * Vector3.forward;
		int next = (nearest + 1) % points.Count;
		int previous = (nearest - 1 + points.Count) % points.Count;
		bool reverse = Vector3.Dot((points[next] - points[nearest]).normalized, spawnForward) <
		               Vector3.Dot((points[previous] - points[nearest]).normalized, spawnForward);

		for (int step = 0; step < points.Count; step++) {
			int index = reverse ? (nearest - step + points.Count) % points.Count : (nearest + step) % points.Count;
			ordered.Add(points[index]);
		}
		ordered.Add(ordered[0]);
		return ordered;
	}

	private float GetPolylineLength (List<Vector3> points) {
		float length = 0f;
		for (int i = 1; i < points.Count; i++) {
			length += Vector3.Distance(points[i - 1], points[i]);
		}
		return length;
	}

	private List<Vector3> GetFallbackCenterLine () {
		if (GetSelectedMapChoice() == MapChoice.Forest) {
			return new List<Vector3> {
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
				new Vector3(-31f, 0f, -29f),
				new Vector3(-42f, 0f, -38f)
			};
		}

		return new List<Vector3> {
			new Vector3(-4.5f, 0f, -43.4f),
			new Vector3(22f, 0f, -39f),
			new Vector3(40f, 0f, -22f),
			new Vector3(35f, 0f, 8f),
			new Vector3(12f, 0f, 18f),
			new Vector3(38f, 0f, 34f),
			new Vector3(6f, 0f, 43f),
			new Vector3(-20f, 0f, 31f),
			new Vector3(-39f, 0f, 12f),
			new Vector3(-43f, 0f, -18f),
			new Vector3(-4.5f, 0f, -43.4f)
		};
	}

	private void ResetCheckpointsForRace () {
		nextCheckpointIndex = 0;
		for (int i = 0; i < checkpoints.Count; i++) {
			checkpoints[i].SetState(i == 0, false);
		}
	}

	private void SetCheckpointStatesForMenu () {
		for (int i = 0; i < checkpoints.Count; i++) {
			checkpoints[i].SetState(false, false);
		}
	}

	private bool ColliderBelongsToActiveCar (Collider other) {
		if (activeCar == null || other == null) return false;
		if (other.transform.root == activeCar.transform) return true;
		CarInputController controller = other.GetComponentInParent<CarInputController>();
		return controller != null && controller == activeController;
	}

	private void FinishRace () {
		state = RaceState.Finished;
		stateBeforePause = RaceState.Finished;
		if (activeController != null) activeController.SetInputLocked(true);
		bool isNewBest = SaveBestLapTimeIfNeeded(raceTimer);
		string mapName = GetSelectedMapChoice().ToString().ToUpperInvariant();
		finishText.text = "FINISH!\n" + FormatRaceTime(raceTimer) + (isNewBest ? "\nNEW " + mapName + " BEST!" : "\n" + mapName + " BEST " + FormatBestLapTime()) + "\nPRESS R TO RESTART";
		PlaySfx(winClip);
		UpdateHud();
	}

	private void UpdateHud () {
		timerText.text = FormatRaceTime(raceTimer);
		checkpointText.text = "CHECKPOINT " + Mathf.Min(nextCheckpointIndex, checkpoints.Count) + " / " + checkpoints.Count;
		if (hudBestText != null) hudBestText.text = GetSelectedMapChoice().ToString().ToUpperInvariant() + " BEST " + FormatBestLapTime();
	}

	private string FormatRaceTime (float seconds) {
		TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
		return string.Format("{0:00}:{1:00}.{2:000}", span.Minutes, span.Seconds, span.Milliseconds);
	}

	private void LoadBestLapTime () {
		string key = GetBestLapTimeKey();
		if (PlayerPrefs.HasKey(key)) {
			bestLapTime = PlayerPrefs.GetFloat(key, -1f);
			return;
		}

		if (GetSelectedMapChoice() == MapChoice.Desert && PlayerPrefs.HasKey(LegacyBestLapTimeKey)) {
			bestLapTime = PlayerPrefs.GetFloat(LegacyBestLapTimeKey, -1f);
			if (bestLapTime > 0f) {
				PlayerPrefs.SetFloat(key, bestLapTime);
				PlayerPrefs.Save();
			}
			return;
		}

		bestLapTime = -1f;
	}

	private bool SaveBestLapTimeIfNeeded (float lapTime) {
		if (lapTime <= 0f) return false;
		if (bestLapTime > 0f && lapTime >= bestLapTime) return false;
		bestLapTime = lapTime;
		PlayerPrefs.SetFloat(GetBestLapTimeKey(), bestLapTime);
		PlayerPrefs.Save();
		UpdateBestLapLabels();
		return true;
	}

	private string GetBestLapTimeKey () {
		return BestLapTimeKeyPrefix + GetSelectedMapChoice().ToString();
	}

	private string FormatBestLapTime () {
		return bestLapTime > 0f ? FormatRaceTime(bestLapTime) : "--:--.---";
	}

	private void UpdateBestLapLabels () {
		string mapName = GetSelectedMapChoice().ToString().ToUpperInvariant();
		string bestText = "BEST " + mapName + "  " + FormatBestLapTime();
		if (menuBestText != null) menuBestText.text = bestText;
		if (hudBestText != null) hudBestText.text = mapName + " BEST " + FormatBestLapTime();
	}

	private bool ResetPressed () {
#if ENABLE_LEGACY_INPUT_MANAGER
		if (Input.GetKeyDown(KeyCode.R)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
		var keyboard = UnityEngine.InputSystem.Keyboard.current;
		return keyboard != null && keyboard.rKey.wasPressedThisFrame;
#else
		return false;
#endif
	}

	private bool PausePressed () {
#if ENABLE_LEGACY_INPUT_MANAGER
		if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
		var keyboard = UnityEngine.InputSystem.Keyboard.current;
		return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
		return false;
#endif
	}

	private void EnsureUiAudioSource () {
		if (uiAudioSource != null) return;
		uiAudioSource = gameObject.AddComponent<AudioSource>();
		uiAudioSource.playOnAwake = false;
		uiAudioSource.loop = false;
		uiAudioSource.spatialBlend = 0f;
		uiAudioSource.volume = uiSfxVolume;
		uiAudioSource.priority = 32;
	}

	private void PlaySfx (AudioClip clip) {
		if (clip == null) return;
		EnsureUiAudioSource();
		uiAudioSource.PlayOneShot(clip, uiSfxVolume);
	}

	private void ConfigureVehicleAudio (CarInputController controller, string vehicleName) {
		if (controller == null) return;
		bool heavyVehicle = IsHeavyVehicle(vehicleName);
		AudioClip startClip = heavyVehicle && truckStartClip != null ? truckStartClip : carStartClip;
		AudioClip engineClip = heavyVehicle && truckEngineClip != null ? truckEngineClip : carEngineClip;
		controller.ConfigureAudioClips(startClip, engineClip, driftAsphaltClip);
	}

	private bool IsHeavyVehicle (string vehicleName) {
		if (string.IsNullOrEmpty(vehicleName)) return false;
		string lower = vehicleName.ToLowerInvariant();
		return lower.Contains("truck") || lower.Contains("bus") || lower.Contains("fire");
	}

	private void ResolveAudioClips () {
#if UNITY_EDITOR
		if (carStartClip == null) carStartClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/PainterCars/Sounds/CarEngineStart.ogg");
		if (carEngineClip == null) carEngineClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/PainterCars/Sounds/CarEngineWorking.ogg");
		if (truckStartClip == null) truckStartClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/PainterCars/Sounds/TruckStartEngine.ogg");
		if (truckEngineClip == null) truckEngineClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/PainterCars/Sounds/TruckEngineWorking.ogg");
		if (driftAsphaltClip == null) driftAsphaltClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/Vehicles/DriftAsphaltSkidLoop.wav");
		if (menuSwitchClip == null) menuSwitchClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/MenuSwitch.wav");
		if (menuSelectClip == null) menuSelectClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/MenuSelect.wav");
		if (checkpointClip == null) checkpointClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/CheckpointPass.wav");
		if (winClip == null) winClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/RaceWin.wav");
		if (countdownTickClip == null) countdownTickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/CountdownTick.wav");
		if (countdownGoClip == null) countdownGoClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/CountdownGo.wav");
		if (pauseClip == null) pauseClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/UI/PauseOpen.wav");
#endif
	}

	private VehicleProfile GetVehicleProfile (string vehicleName) {
		string lower = vehicleName.ToLowerInvariant();
		VehicleProfile profile = new VehicleProfile {
			displayName = GetVehicleDisplayName(vehicleName),
			trait = "balanced drift",
			maxSpeed = 25f,
			power = 270f,
			brake = 720f,
			mass = 900f,
			rearDriftGrip = 0.30f,
			yawAssist = 1.55f,
			sideAssist = 2.4f,
			offTrackPower = 0.45f,
			steeringReduction = 0.48f,
			stability = 2.4f,
			downforce = 0.22f,
			accentColor = new Color(0.9f, 0.04f, 0.03f, 1f)
		};

		if (lower.Contains("police")) {
			profile.trait = "grip pursuit";
			profile.maxSpeed = 27f;
			profile.power = 285f;
			profile.brake = 820f;
			profile.rearDriftGrip = 0.38f;
			profile.stability = 3.2f;
			profile.downforce = 0.28f;
			profile.accentColor = new Color(0.08f, 0.28f, 1f, 1f);
		}
		if (lower.Contains("muscle")) {
			profile.trait = "power slide";
			profile.maxSpeed = 29f;
			profile.power = 330f;
			profile.brake = 760f;
			profile.rearDriftGrip = 0.24f;
			profile.yawAssist = 1.75f;
			profile.sideAssist = 2.8f;
			profile.accentColor = new Color(1f, 0.08f, 0.02f, 1f);
		}
		if (lower.Contains("pickup")) {
			profile.trait = "sand bite";
			profile.maxSpeed = 24f;
			profile.power = 295f;
			profile.brake = 760f;
			profile.mass = 1020f;
			profile.offTrackPower = 0.68f;
			profile.stability = 2.8f;
			profile.accentColor = new Color(1f, 0.56f, 0.08f, 1f);
		}
		if (lower.Contains("suv")) {
			profile.trait = "stable grip";
			profile.maxSpeed = 25f;
			profile.power = 300f;
			profile.brake = 800f;
			profile.mass = 1120f;
			profile.rearDriftGrip = 0.36f;
			profile.stability = 3.4f;
			profile.downforce = 0.26f;
			profile.accentColor = new Color(0.12f, 0.72f, 0.92f, 1f);
		}
		if (lower.Contains("ambulance") || lower.Contains("van")) {
			profile.trait = "quick recovery";
			profile.maxSpeed = 24f;
			profile.power = 280f;
			profile.brake = 900f;
			profile.mass = 1180f;
			profile.steeringReduction = 0.42f;
			profile.stability = 3.0f;
			profile.accentColor = new Color(0.95f, 0.95f, 0.92f, 1f);
		}
		if (lower.Contains("truck") || lower.Contains("bus") || lower.Contains("fire")) {
			profile.trait = "heavy momentum";
			profile.maxSpeed = 20f;
			profile.power = 360f;
			profile.brake = 1050f;
			profile.mass = 1900f;
			profile.rearDriftGrip = 0.46f;
			profile.yawAssist = 0.9f;
			profile.sideAssist = 1.2f;
			profile.steeringReduction = 0.34f;
			profile.stability = 4.2f;
			profile.downforce = 0.34f;
			profile.accentColor = new Color(1f, 0.22f, 0.06f, 1f);
		}
		if (lower.Contains("car3")) {
			profile.trait = "lightweight snap";
			profile.maxSpeed = 28f;
			profile.power = 260f;
			profile.brake = 720f;
			profile.mass = 760f;
			profile.rearDriftGrip = 0.28f;
			profile.yawAssist = 1.9f;
			profile.steeringReduction = 0.55f;
			profile.accentColor = new Color(0.32f, 1f, 0.42f, 1f);
		}

		return profile;
	}

	private string GetVehicleDisplayName (string vehicleName) {
		return vehicleName.Replace("_", " ").Replace("1", " 1").Replace("2", " 2").Replace("3", " 3");
	}

	private void RepairVehicleMaterials (GameObject vehicle, Color accentColor) {
		Renderer[] renderers = vehicle.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++) {
			Renderer renderer = renderers[i];
			renderer.shadowCastingMode = ShadowCastingMode.On;
			renderer.receiveShadows = true;
			Material[] materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
			for (int m = 0; m < materials.Length; m++) {
				materials[m] = CreateSafeVehicleMaterial(materials[m], renderer.name, accentColor);
			}
			if (Application.isPlaying) {
				renderer.materials = materials;
			} else {
				renderer.sharedMaterials = materials;
			}
		}
	}

	private void RepairMapMaterials (GameObject mapObject) {
		if (mapObject == null) return;
		Renderer[] renderers = mapObject.GetComponentsInChildren<Renderer>(true);
		for (int i = 0; i < renderers.Length; i++) {
			Renderer renderer = renderers[i];
			renderer.shadowCastingMode = ShadowCastingMode.On;
			renderer.receiveShadows = true;
			Material[] materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
			for (int m = 0; m < materials.Length; m++) {
				materials[m] = CreateSafeMapMaterial(materials[m], renderer.name);
			}
			if (Application.isPlaying) {
				renderer.materials = materials;
			} else {
				renderer.sharedMaterials = materials;
			}
		}
	}

	private void EnsureMapColliders (GameObject mapObject) {
		if (mapObject == null) return;
		MeshFilter[] filters = mapObject.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < filters.Length; i++) {
			MeshFilter filter = filters[i];
			if (filter == null || filter.sharedMesh == null || !ShouldHaveMapCollider(filter.gameObject)) continue;
			MeshCollider meshCollider = filter.GetComponent<MeshCollider>();
			if (meshCollider == null) meshCollider = filter.gameObject.AddComponent<MeshCollider>();
			meshCollider.sharedMesh = filter.sharedMesh;
			meshCollider.convex = false;
			meshCollider.isTrigger = false;
		}
	}

	private void RemoveDecorativeMapColliders (GameObject mapObject) {
		if (mapObject == null) return;
		Collider[] colliders = mapObject.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++) {
			Collider collider = colliders[i];
			if (collider == null || collider.isTrigger || ShouldHaveMapCollider(collider.gameObject)) continue;
			DestroyMapComponent(collider);
		}
	}

	private void DestroyMapComponent (Component component) {
		if (component == null) return;
#if UNITY_EDITOR
		if (!Application.isPlaying) {
			DestroyImmediate(component);
			return;
		}
#endif
		Destroy(component);
	}

	private bool ShouldHaveMapCollider (GameObject gameObject) {
		if (gameObject == null) return false;
		string normalized = gameObject.name.Replace("_", " ").ToLowerInvariant();
		if (IsDecorativeMapSurface(normalized)) return false;
		if (normalized.Contains("asphalt") || normalized.Contains("shoulder") || normalized.Contains("ground")) return true;
		Renderer renderer = gameObject.GetComponent<Renderer>();
		if (renderer == null) return false;
		Material[] materials = renderer.sharedMaterials;
		for (int i = 0; i < materials.Length; i++) {
			if (materials[i] == null) continue;
			string materialName = materials[i].name.Replace("_", " ").ToLowerInvariant();
			if (IsDecorativeMapSurface(materialName)) return false;
			if (materialName.Contains("asphalt") || materialName.Contains("shoulder") || materialName.Contains("ground") || materialName.Contains("grass")) return true;
		}
		return false;
	}

	private bool IsDecorativeMapSurface (string normalizedName) {
		return normalizedName.Contains("scuff") ||
			normalizedName.Contains("litter") ||
			normalizedName.Contains("marker") ||
			normalizedName.Contains("reflector") ||
			normalizedName.Contains("sign") ||
			normalizedName.Contains("post") ||
			normalizedName.Contains("tree") ||
			normalizedName.Contains("rock") ||
			normalizedName.Contains("moss") ||
			normalizedName.Contains("pond") ||
			normalizedName.Contains("water") ||
			normalizedName.Contains("finish");
	}

	private bool IsMagenta (Color color) {
		return color.r > 0.85f && color.b > 0.85f && color.g < 0.25f;
	}

	private Material CreateSafeVehicleMaterial (Material source, string rendererName, Color accentColor) {
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		Material material = new Material(shader);
		material.name = source != null ? "Runtime URP " + source.name : "Runtime URP Vehicle Material";

		string combinedName = ((source != null ? source.name : "") + " " + rendererName).ToLowerInvariant();
		Color sourceColor = GetMaterialColor(source);
		Texture sourceTexture = GetMaterialTexture(source);
		bool unusableSource = source == null || source.shader == null || source.shader.name.Contains("Error") || IsMagenta(sourceColor) || IsUnsupportedVehicleShader(source.shader);
		Color color = GetVehiclePartColor(combinedName, accentColor, unusableSource ? Color.white : sourceColor);

		SetMaterialColor(material, color);
		SetMaterialTexture(material, sourceTexture);
		if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.5f);
		if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.05f);
		return material;
	}

	private Material CreateSafeMapMaterial (Material source, string rendererName) {
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null) shader = Shader.Find("Standard");
		Material material = new Material(shader);
		material.name = source != null ? "Runtime URP Map " + source.name : "Runtime URP Map Material";

		string combinedName = ((source != null ? source.name : "") + " " + rendererName).Replace("_", " ").ToLowerInvariant();
		Color sourceColor = GetMaterialColor(source);
		Color color = GetMapPartColor(combinedName, sourceColor);
		SetMaterialColor(material, color);
		Texture sourceTexture = GetMaterialTexture(source);
		SetMaterialTexture(material, sourceTexture);
		if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", combinedName.Contains("pond") || combinedName.Contains("water") ? 0.75f : 0.28f);
		if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.02f);
		if (color.a < 0.99f) MakeMaterialTransparent(material);
		return material;
	}

	private bool IsUnsupportedVehicleShader (Shader shader) {
		if (shader == null) return true;
		string shaderName = shader.name;
		return !shaderName.Contains("Universal Render Pipeline") && !shaderName.Contains("Sprites");
	}

	private Color GetMaterialColor (Material material) {
		if (material == null) return Color.white;
		if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
		if (material.HasProperty("_Color")) return material.GetColor("_Color");
		return Color.white;
	}

	private Texture GetMaterialTexture (Material material) {
		if (material == null) return null;
		if (material.HasProperty("_BaseMap")) return material.GetTexture("_BaseMap");
		if (material.HasProperty("_MainTex")) return material.GetTexture("_MainTex");
		return null;
	}

	private Color GetVehiclePartColor (string partName, Color accentColor, Color sourceColor) {
		if (partName.Contains("wheel") || partName.Contains("tire") || partName.Contains("tyre")) return new Color(0.025f, 0.025f, 0.025f, 1f);
		if (partName.Contains("window") || partName.Contains("glass")) return new Color(0.08f, 0.18f, 0.23f, 1f);
		if (partName.Contains("light") || partName.Contains("lamp")) return new Color(1f, 0.93f, 0.72f, 1f);
		if (partName.Contains("chrome") || partName.Contains("metal")) return new Color(0.55f, 0.56f, 0.58f, 1f);
		if (partName.Contains("body") || partName.Contains("car") || IsMagenta(sourceColor)) return accentColor;
		return sourceColor;
	}

	private Color GetMapPartColor (string partName, Color sourceColor) {
		if (partName.Contains("scuff") || partName.Contains("tire")) return new Color(0.01f, 0.011f, 0.01f, 0.48f);
		if (partName.Contains("litter")) return new Color(0.34f, 0.23f, 0.10f, 0.82f);
		if (partName.Contains("reflector") || partName.Contains("marker")) return new Color(1f, 0.24f, 0.08f, 1f);
		if (partName.Contains("asphalt")) return new Color(0.035f, 0.038f, 0.04f, 1f);
		if (partName.Contains("dirt") || partName.Contains("shoulder")) return new Color(0.31f, 0.22f, 0.14f, 1f);
		if (partName.Contains("grass") || partName.Contains("ground")) return new Color(0.16f, 0.38f, 0.16f, 1f);
		if (partName.Contains("moss")) return new Color(0.27f, 0.52f, 0.18f, 1f);
		if (partName.Contains("trunk") || partName.Contains("post")) return new Color(0.25f, 0.14f, 0.075f, 1f);
		if (partName.Contains("pine") || partName.Contains("leaf") || partName.Contains("canopy") || partName.Contains("crown")) return new Color(0.055f, 0.24f, 0.105f, 1f);
		if (partName.Contains("rock")) return new Color(0.28f, 0.29f, 0.27f, 1f);
		if (partName.Contains("pond") || partName.Contains("water")) return new Color(0.08f, 0.25f, 0.28f, 0.84f);
		if (partName.Contains("white")) return new Color(0.92f, 0.90f, 0.82f, 1f);
		if (partName.Contains("black")) return new Color(0.02f, 0.02f, 0.018f, 1f);
		return IsMagenta(sourceColor) ? new Color(0.16f, 0.38f, 0.16f, 1f) : sourceColor;
	}

	private void SetMaterialColor (Material material, Color color) {
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color")) material.SetColor("_Color", color);
	}

	private void SetMaterialTexture (Material material, Texture texture) {
		if (texture == null) return;
		if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
		if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
	}

	private void MakeMaterialTransparent (Material material) {
		material.SetOverrideTag("RenderType", "Transparent");
		material.renderQueue = (int)RenderQueue.Transparent;
		if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
		if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
		material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		material.EnableKeyword("_ALPHABLEND_ON");
	}

	private Mesh CreateHalfRingMesh (float radius, float tubeRadius, int arcSegments, int tubeSegments) {
		List<Vector3> vertices = new List<Vector3>();
		List<int> triangles = new List<int>();
		for (int i = 0; i <= arcSegments; i++) {
			float arc = Mathf.PI - (Mathf.PI * i / arcSegments);
			Vector3 center = new Vector3(Mathf.Cos(arc) * radius, Mathf.Sin(arc) * radius, 0f);
			for (int j = 0; j < tubeSegments; j++) {
				float tube = Mathf.PI * 2f * j / tubeSegments;
				vertices.Add(center + new Vector3(Mathf.Cos(arc) * Mathf.Cos(tube) * tubeRadius, Mathf.Sin(arc) * Mathf.Cos(tube) * tubeRadius, Mathf.Sin(tube) * tubeRadius));
			}
		}

		for (int i = 0; i < arcSegments; i++) {
			for (int j = 0; j < tubeSegments; j++) {
				int a = i * tubeSegments + j;
				int b = i * tubeSegments + (j + 1) % tubeSegments;
				int c = (i + 1) * tubeSegments + j;
				int d = (i + 1) * tubeSegments + (j + 1) % tubeSegments;
				triangles.Add(a);
				triangles.Add(c);
				triangles.Add(b);
				triangles.Add(b);
				triangles.Add(c);
				triangles.Add(d);
			}
		}

		Mesh mesh = new Mesh();
		mesh.name = "Runtime Red Checkpoint Half Ring";
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private void CreateMapChooser () {
		Text title = CreateText("Map Chooser Title", mapChooserRoot, "CHOOSE MAP", 34, TextAnchor.MiddleCenter, new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(title.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.94f), Vector2.zero, Vector2.zero);

		selectedMapText = CreateText("Selected Map", mapChooserRoot, "", 21, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.18f, 1f));
		SetAnchors(selectedMapText.rectTransform, new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.80f), Vector2.zero, Vector2.zero);

		desertMapCardImage = CreateMapOptionButton("Desert Map Option", "DESERT", "OPEN SAND CIRCUIT", MapChoice.Desert, GetMapThumbnail(MapChoice.Desert));
		SetAnchors(desertMapCardImage.rectTransform, new Vector2(0.08f, 0.17f), new Vector2(0.48f, 0.68f), Vector2.zero, Vector2.zero);

		forestMapCardImage = CreateMapOptionButton("Forest Map Option", "FOREST", "WOODLAND ASPHALT", MapChoice.Forest, GetMapThumbnail(MapChoice.Forest));
		SetAnchors(forestMapCardImage.rectTransform, new Vector2(0.52f, 0.17f), new Vector2(0.92f, 0.68f), Vector2.zero, Vector2.zero);
	}

	private Image CreateMapOptionButton (string objectName, string title, string subtitle, MapChoice mapChoice, Sprite thumbnail) {
		GameObject cardObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		cardObject.transform.SetParent(mapChooserRoot, false);
		Image cardImage = cardObject.GetComponent<Image>();
		cardImage.color = new Color(0.07f, 0.075f, 0.085f, 0.96f);
		cardImage.raycastTarget = true;
		Outline outline = cardObject.AddComponent<Outline>();
		outline.effectColor = new Color(1f, 0.78f, 0.18f, 0.35f);
		outline.effectDistance = new Vector2(2f, -2f);

		Button button = cardObject.GetComponent<Button>();
		ColorBlock colors = button.colors;
		colors.normalColor = cardImage.color;
		colors.highlightedColor = Color.Lerp(cardImage.color, Color.white, 0.12f);
		colors.pressedColor = Color.Lerp(cardImage.color, Color.black, 0.22f);
		colors.selectedColor = colors.highlightedColor;
		button.colors = colors;
		button.onClick.AddListener(() => {
			PlaySfx(menuSelectClip);
			SelectMap(mapChoice);
		});

		GameObject thumbnailObject = new GameObject("Map Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		thumbnailObject.transform.SetParent(cardObject.transform, false);
		Image thumbnailImage = thumbnailObject.GetComponent<Image>();
		thumbnailImage.sprite = thumbnail;
		thumbnailImage.type = Image.Type.Simple;
		thumbnailImage.preserveAspect = false;
		thumbnailImage.raycastTarget = false;
		SetAnchors(thumbnailImage.rectTransform, new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero);

		Text titleText = CreateText("Map Name", cardObject.transform, title, 24, TextAnchor.MiddleCenter, new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(titleText.rectTransform, new Vector2(0.08f, 0.17f), new Vector2(0.92f, 0.31f), Vector2.zero, Vector2.zero);

		Text subtitleText = CreateText("Map Subtitle", cardObject.transform, subtitle, 16, TextAnchor.MiddleCenter, new Color(0.85f, 0.90f, 0.92f, 0.88f));
		SetAnchors(subtitleText.rectTransform, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.17f), Vector2.zero, Vector2.zero);
		return cardImage;
	}

	private void UpdateMapSelectionUI () {
		MapChoice selected = GetSelectedMapChoice();
		if (selectedMapText != null) selectedMapText.text = "SELECTED MAP  " + selected.ToString().ToUpperInvariant();
		if (desertMapCardImage != null) {
			desertMapCardImage.color = selected == MapChoice.Desert ? new Color(0.34f, 0.065f, 0.045f, 0.98f) : new Color(0.07f, 0.075f, 0.085f, 0.96f);
		}
		if (forestMapCardImage != null) {
			forestMapCardImage.color = selected == MapChoice.Forest ? new Color(0.34f, 0.065f, 0.045f, 0.98f) : new Color(0.07f, 0.075f, 0.085f, 0.96f);
		}
	}

	private Sprite GetMapThumbnail (MapChoice mapChoice) {
		if (mapChoice == MapChoice.Desert) {
			if (desertMapThumbnail == null) desertMapThumbnail = CreateMapThumbnail(mapChoice);
			return desertMapThumbnail;
		}
		if (forestMapThumbnail == null) forestMapThumbnail = CreateMapThumbnail(mapChoice);
		return forestMapThumbnail;
	}

	private Sprite CreateMapThumbnail (MapChoice mapChoice) {
		const int width = 192;
		const int height = 112;
		Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
		texture.name = mapChoice + " Map Thumbnail";
		Color background = mapChoice == MapChoice.Desert ? new Color(0.74f, 0.56f, 0.30f, 1f) : new Color(0.09f, 0.26f, 0.11f, 1f);
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				float shade = Mathf.PerlinNoise(x * 0.045f, y * 0.045f) * 0.08f;
				texture.SetPixel(x, y, Color.Lerp(background, Color.white, shade));
			}
		}

		if (mapChoice == MapChoice.Desert) {
			DrawDisc(texture, 40, 72, 16, new Color(0.35f, 0.58f, 0.18f, 1f));
			DrawDisc(texture, 73, 36, 11, new Color(0.45f, 0.65f, 0.22f, 1f));
			DrawDisc(texture, 136, 76, 13, new Color(0.40f, 0.62f, 0.20f, 1f));
			DrawDisc(texture, 160, 34, 5, new Color(0.47f, 0.37f, 0.24f, 1f));
			DrawDisc(texture, 24, 25, 4, new Color(0.47f, 0.37f, 0.24f, 1f));
		} else {
			DrawDisc(texture, 139, 36, 17, new Color(0.08f, 0.25f, 0.28f, 1f));
			for (int i = 0; i < 38; i++) {
				int x = 10 + (i * 37) % (width - 20);
				int y = 8 + (i * 53) % (height - 16);
				DrawDisc(texture, x, y, 4 + i % 3, i % 2 == 0 ? new Color(0.04f, 0.20f, 0.08f, 1f) : new Color(0.12f, 0.34f, 0.10f, 1f));
			}
		}

		Vector2[] track = {
			new Vector2(30f, 30f),
			new Vector2(70f, 18f),
			new Vector2(117f, 24f),
			new Vector2(157f, 45f),
			new Vector2(121f, 58f),
			new Vector2(164f, 82f),
			new Vector2(94f, 92f),
			new Vector2(52f, 76f),
			new Vector2(30f, 30f)
		};
		DrawPolyline(texture, track, mapChoice == MapChoice.Desert ? 12 : 14, mapChoice == MapChoice.Desert ? new Color(0.50f, 0.31f, 0.16f, 1f) : new Color(0.24f, 0.17f, 0.10f, 1f));
		DrawPolyline(texture, track, 8, new Color(0.035f, 0.038f, 0.04f, 1f));
		DrawLine(texture, new Vector2(24f, 32f), new Vector2(37f, 27f), 2, new Color(0.92f, 0.90f, 0.82f, 1f));
		DrawLine(texture, new Vector2(28f, 34f), new Vector2(41f, 29f), 2, new Color(0.02f, 0.02f, 0.018f, 1f));

		texture.Apply();
		return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
	}

	private void DrawPolyline (Texture2D texture, Vector2[] points, int radius, Color color) {
		for (int i = 1; i < points.Length; i++) {
			DrawLine(texture, points[i - 1], points[i], radius, color);
		}
	}

	private void DrawLine (Texture2D texture, Vector2 start, Vector2 end, int radius, Color color) {
		int steps = Mathf.CeilToInt(Vector2.Distance(start, end) * 1.6f);
		for (int i = 0; i <= steps; i++) {
			float t = steps <= 0 ? 0f : i / (float)steps;
			Vector2 point = Vector2.Lerp(start, end, t);
			DrawDisc(texture, Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), radius, color);
		}
	}

	private void DrawDisc (Texture2D texture, int centerX, int centerY, int radius, Color color) {
		int radiusSquared = radius * radius;
		for (int y = centerY - radius; y <= centerY + radius; y++) {
			if (y < 0 || y >= texture.height) continue;
			for (int x = centerX - radius; x <= centerX + radius; x++) {
				if (x < 0 || x >= texture.width) continue;
				int dx = x - centerX;
				int dy = y - centerY;
				if (dx * dx + dy * dy <= radiusSquared) {
					texture.SetPixel(x, y, color);
				}
			}
		}
	}

	private void CreatePauseMenu () {
		if (pauseRoot == null) return;
		CreateStripe(pauseRoot, new Vector2(0.32f, 0.25f), new Vector2(0.68f, 0.75f), new Color(0.04f, 0.045f, 0.055f, 0.92f));
		CreateStripe(pauseRoot, new Vector2(0.32f, 0.735f), new Vector2(0.68f, 0.75f), new Color(0.9f, 0.05f, 0.025f, 1f));
		CreateCheckeredBand(pauseRoot, new Vector2(0.38f, 0.30f), new Vector2(0.62f, 0.35f), 8, 1);

		Text pausedTitle = CreateText("Pause Title", pauseRoot, "PAUSED", 58, TextAnchor.MiddleCenter, new Color(1f, 0.94f, 0.74f, 1f));
		SetAnchors(pausedTitle.rectTransform, new Vector2(0.35f, 0.61f), new Vector2(0.65f, 0.72f), Vector2.zero, Vector2.zero);

		Button resumeButton = CreateButton("Resume Button", pauseRoot, "RESUME", new Color(0.9f, 0.05f, 0.025f, 0.98f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(resumeButton.GetComponent<RectTransform>(), new Vector2(0.40f, 0.50f), new Vector2(0.60f, 0.58f), Vector2.zero, Vector2.zero);
		resumeButton.onClick.AddListener(ResumeRace);

		Button mainMenuButton = CreateButton("Return To Main Menu Button", pauseRoot, "MAIN MENU", new Color(0.08f, 0.09f, 0.105f, 0.98f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(mainMenuButton.GetComponent<RectTransform>(), new Vector2(0.40f, 0.40f), new Vector2(0.60f, 0.48f), Vector2.zero, Vector2.zero);
		mainMenuButton.onClick.AddListener(ReturnToMainMenu);
	}

	private void CreateCheckeredBand (RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, int columns, int rows) {
		for (int y = 0; y < rows; y++) {
			for (int x = 0; x < columns; x++) {
				Vector2 cellMin = new Vector2(
					Mathf.Lerp(anchorMin.x, anchorMax.x, x / (float)columns),
					Mathf.Lerp(anchorMin.y, anchorMax.y, y / (float)rows));
				Vector2 cellMax = new Vector2(
					Mathf.Lerp(anchorMin.x, anchorMax.x, (x + 1) / (float)columns),
					Mathf.Lerp(anchorMin.y, anchorMax.y, (y + 1) / (float)rows));
				Color color = (x + y) % 2 == 0 ? new Color(1f, 0.94f, 0.76f, 0.92f) : new Color(0.025f, 0.028f, 0.035f, 0.92f);
				CreateStripe(parent, cellMin, cellMax, color);
			}
		}
	}

	private RectTransform CreatePanel (string objectName, Transform parent, Color color) {
		GameObject panel = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		panel.transform.SetParent(parent, false);
		RectTransform rect = panel.GetComponent<RectTransform>();
		SetAnchors(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		Image image = panel.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
		return rect;
	}

	private void CreateStripe (RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color) {
		RectTransform stripe = CreatePanel("Racing Stripe", parent, color);
		SetAnchors(stripe, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
	}

	private Text CreateText (string objectName, Transform parent, string value, int size, TextAnchor anchor, Color color) {
		GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text text = textObject.GetComponent<Text>();
		text.text = value;
		text.font = GetUIFont();
		text.fontSize = size;
		text.resizeTextForBestFit = true;
		text.resizeTextMinSize = Mathf.Max(12, size / 2);
		text.resizeTextMaxSize = size;
		text.alignment = anchor;
		text.color = color;
		text.raycastTarget = false;
		return text;
	}

	private Button CreateButton (string objectName, Transform parent, string label, Color background, Color textColor) {
		GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		buttonObject.transform.SetParent(parent, false);
		Image image = buttonObject.GetComponent<Image>();
		image.color = background;
		image.raycastTarget = true;
		Outline outline = buttonObject.AddComponent<Outline>();
		outline.effectColor = new Color(1f, 0.78f, 0.18f, 0.55f);
		outline.effectDistance = new Vector2(2f, -2f);
		Button button = buttonObject.GetComponent<Button>();
		ColorBlock colors = button.colors;
		colors.normalColor = background;
		colors.highlightedColor = Color.Lerp(background, Color.white, 0.14f);
		colors.pressedColor = Color.Lerp(background, Color.black, 0.18f);
		colors.selectedColor = colors.highlightedColor;
		button.colors = colors;

		Text text = CreateText("Label", buttonObject.transform, label, 30, TextAnchor.MiddleCenter, textColor);
		SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		Outline textOutline = text.gameObject.AddComponent<Outline>();
		textOutline.effectColor = new Color(0f, 0f, 0f, 0.55f);
		textOutline.effectDistance = new Vector2(1.4f, -1.4f);
		return button;
	}

	private void SetAnchors (RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.offsetMin = offsetMin;
		rect.offsetMax = offsetMax;
	}

	private Font GetUIFont () {
		if (uiFont != null) return uiFont;
		uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
		return uiFont;
	}
}
