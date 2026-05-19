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
	private RectTransform hudRoot;
	private RectTransform pauseRoot;
	private Text countdownText;
	private Text checkpointText;
	private Text timerText;
	private Text finishText;
	private Text vehicleNameText;
	private Text vehicleStatsText;
	private Text menuBestText;
	private Text hudBestText;
	private Font uiFont;

	private readonly List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();
	private Mesh checkpointMesh;
	private Material checkpointMaterial;
	private GameObject checkpointRoot;
	private GameObject activeCar;
	private GameObject previewCar;
	private Transform previewCameraAnchor;
	private CarInputController activeController;
	private AudioSource uiAudioSource;
	private Vector3 spawnPosition;
	private Quaternion spawnRotation;
	private Vector3 spawnScale = Vector3.one;
	private int nextCheckpointIndex;
	private float raceTimer;
	private float bestLapTime = -1f;
	private bool restartQueued;
	private Coroutine raceRoutine;
	private int raceFlowVersion;

	private const string BestLapTimeKey = "Drifty.DesertRaceTrack.BestLapTime";

	void Awake () {
		ResolveSceneReferences();
		ResolveVehicles();
		ResolveAudioClips();
		EnsureUiAudioSource();
		LoadBestLapTime();
		CreateSharedCheckpointAssets();
		CreateCheckpointGates();
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
		pauseRoot = CreatePanel("Pause Menu", canvasObject.transform, new Color(0.012f, 0.014f, 0.018f, 0.92f));
		pauseRoot.gameObject.SetActive(false);

		CreateStripe(menuRoot, new Vector2(0.03f, 0f), new Vector2(0.08f, 1f), new Color(0.86f, 0.02f, 0.02f, 0.95f));
		CreateStripe(menuRoot, new Vector2(0.085f, 0f), new Vector2(0.098f, 1f), new Color(1f, 0.72f, 0.08f, 0.85f));
		CreateStripe(menuRoot, new Vector2(0.895f, 0f), new Vector2(0.91f, 1f), new Color(1f, 0.72f, 0.08f, 0.7f));
		CreateStripe(menuRoot, new Vector2(0.92f, 0f), new Vector2(0.975f, 1f), new Color(0.86f, 0.02f, 0.02f, 0.82f));
		CreateCheckeredBand(menuRoot, new Vector2(0.12f, 0.08f), new Vector2(0.54f, 0.16f), 14, 2);
		CreateStripe(menuRoot, new Vector2(0.12f, 0.57f), new Vector2(0.52f, 0.585f), new Color(0.9f, 0.04f, 0.02f, 0.92f));
		CreateStripe(menuRoot, new Vector2(0.12f, 0.545f), new Vector2(0.44f, 0.555f), new Color(1f, 0.68f, 0.06f, 0.82f));

		Text titleShadow = CreateText("Title Shadow", menuRoot, "DRIFTY DESERT GP", 76, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.55f));
		SetAnchors(titleShadow.rectTransform, new Vector2(0.143f, 0.637f), new Vector2(0.555f, 0.835f), Vector2.zero, Vector2.zero);
		Text title = CreateText("Title", menuRoot, "DRIFTY DESERT GP", 76, TextAnchor.MiddleLeft, new Color(1f, 0.94f, 0.74f, 1f));
		SetAnchors(title.rectTransform, new Vector2(0.14f, 0.64f), new Vector2(0.55f, 0.84f), Vector2.zero, Vector2.zero);

		menuBestText = CreateText("Menu Best Lap", menuRoot, "", 24, TextAnchor.MiddleLeft, new Color(1f, 0.78f, 0.18f, 1f));
		SetAnchors(menuBestText.rectTransform, new Vector2(0.145f, 0.50f), new Vector2(0.55f, 0.56f), Vector2.zero, Vector2.zero);

		Button playButton = CreateButton("Play Button", menuRoot, "PLAY", new Color(0.88f, 0.03f, 0.02f, 0.98f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(playButton.GetComponent<RectTransform>(), new Vector2(0.145f, 0.38f), new Vector2(0.37f, 0.48f), Vector2.zero, Vector2.zero);
		playButton.onClick.AddListener(() => {
			PlaySfx(menuSelectClip);
			BeginRaceFlow();
		});

		Button chooseButton = CreateButton("Choose Vehicle Button", menuRoot, "CHOOSE VEHICLE", new Color(0.08f, 0.09f, 0.105f, 0.95f), new Color(1f, 0.94f, 0.76f, 1f));
		SetAnchors(chooseButton.GetComponent<RectTransform>(), new Vector2(0.145f, 0.265f), new Vector2(0.37f, 0.365f), Vector2.zero, Vector2.zero);
		chooseButton.onClick.AddListener(() => {
			PlaySfx(menuSwitchClip);
			chooserRoot.gameObject.SetActive(!chooserRoot.gameObject.activeSelf);
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
		hudRoot.gameObject.SetActive(false);
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

	private void SpawnSelectedVehicle () {
		if (activeCar != null) Destroy(activeCar);
		GameObject prefab = vehiclePrefabs[Mathf.Clamp(selectedVehicleIndex, 0, vehiclePrefabs.Length - 1)];
		activeCar = Instantiate(prefab, spawnPosition, spawnRotation);
		activeCar.name = GetVehicleDisplayName(prefab.name) + " Player";
		activeCar.transform.localScale = spawnScale;
		RepairVehicleMaterials(activeCar, GetVehicleProfile(prefab.name).accentColor);

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
		previewCameraAnchor.position = spawnPosition + Vector3.up * 0.65f;
		previewCameraAnchor.rotation = spawnRotation;
		return previewCameraAnchor;
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
		GameObject asphalt = GameObject.Find("Asphalt Winding Circuit");
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
		finishText.text = "FINISH!\n" + FormatRaceTime(raceTimer) + (isNewBest ? "\nNEW BEST!" : "\nBEST " + FormatBestLapTime()) + "\nPRESS R TO RESTART";
		PlaySfx(winClip);
		UpdateHud();
	}

	private void UpdateHud () {
		timerText.text = FormatRaceTime(raceTimer);
		checkpointText.text = "CHECKPOINT " + Mathf.Min(nextCheckpointIndex, checkpoints.Count) + " / " + checkpoints.Count;
		if (hudBestText != null) hudBestText.text = "BEST " + FormatBestLapTime();
	}

	private string FormatRaceTime (float seconds) {
		TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
		return string.Format("{0:00}:{1:00}.{2:000}", span.Minutes, span.Seconds, span.Milliseconds);
	}

	private void LoadBestLapTime () {
		bestLapTime = PlayerPrefs.HasKey(BestLapTimeKey) ? PlayerPrefs.GetFloat(BestLapTimeKey, -1f) : -1f;
	}

	private bool SaveBestLapTimeIfNeeded (float lapTime) {
		if (lapTime <= 0f) return false;
		if (bestLapTime > 0f && lapTime >= bestLapTime) return false;
		bestLapTime = lapTime;
		PlayerPrefs.SetFloat(BestLapTimeKey, bestLapTime);
		PlayerPrefs.Save();
		UpdateBestLapLabels();
		return true;
	}

	private string FormatBestLapTime () {
		return bestLapTime > 0f ? FormatRaceTime(bestLapTime) : "--:--.---";
	}

	private void UpdateBestLapLabels () {
		string bestText = "BEST LAP  " + FormatBestLapTime();
		if (menuBestText != null) menuBestText.text = bestText;
		if (hudBestText != null) hudBestText.text = "BEST " + FormatBestLapTime();
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
			Material[] materials = renderer.materials;
			for (int m = 0; m < materials.Length; m++) {
				materials[m] = CreateSafeVehicleMaterial(materials[m], renderer.name, accentColor);
			}
			renderer.materials = materials;
		}
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

	private void SetMaterialColor (Material material, Color color) {
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color")) material.SetColor("_Color", color);
	}

	private void SetMaterialTexture (Material material, Texture texture) {
		if (texture == null) return;
		if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
		if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
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
