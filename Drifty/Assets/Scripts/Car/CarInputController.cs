using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

public class CarInputController : MonoBehaviour {

    [Header("Car Parameters")]
    [SerializeField] private float maxSpeed;
    [SerializeField] private float powerEngine;
    [SerializeField] private float brakePower;
    [SerializeField] private Transform COM;
    [SerializeField] private Wheels[] wheels;
	[SerializeField] private bool autoStartEngine = true;

	[Header("Drift Parameters")]
	[SerializeField] private float driftFrontSideGrip = 0.75f;
	[SerializeField] private float driftRearSideGrip = 0.3f;
	[SerializeField] private float driftGripChangeSpeed = 14f;
	[SerializeField] private float driftMotorMultiplier = 1.12f;
	[SerializeField] private float driftBrakePower = 180f;
	[SerializeField] private float driftMinSpeed = 3f;
	[SerializeField] private float driftYawAssist = 1.55f;
	[SerializeField] private float driftSideAssist = 2.4f;
	[SerializeField] private float driftStability = 1.2f;
	[SerializeField] private float maxDriftYawRate = 2.1f;
	[SerializeField] private float driftSlipForEffects = 0.25f;

	[Header("Surface Parameters")]
	[SerializeField] private float offTrackPowerMultiplier = 0.45f;
	[SerializeField] private float offTrackMaxSpeedMultiplier = 0.45f;
	[SerializeField] private float offTrackLinearDamping = 2.4f;
	[SerializeField] private float offTrackAngularDamping = 2.0f;
	[SerializeField] private float offTrackRumble = 28f;

	[Header("High Speed Handling")]
	[SerializeField] private float highSpeedSteeringReduction = 0.48f;
	[SerializeField] private float highSpeedGripRecovery = 0.28f;
	[SerializeField] private float highSpeedDownforce = 0.22f;
	[SerializeField] private float highSpeedLateralStability = 2.4f;
	[SerializeField] private float highSpeedDriftAssistReduction = 0.55f;
	[SerializeField] private float highSpeedMaxYawRate = 1.35f;

	[Header("Drift Effects")]
	[SerializeField] private AudioClip DriftClip;
	[SerializeField] private Color driftSmokeColor = new Color(0.86f, 0.84f, 0.78f, 0.65f);
	[SerializeField] private Color driftTrailColor = new Color(0.04f, 0.04f, 0.035f, 0.65f);

	[Header("Sounds")]
	[SerializeField] private AudioClip StartEngineClip;
	[SerializeField] private AudioClip WorkingEngineClip;
	[HideInInspector] public bool carInFocus;

	private Rigidbody rb;
	private AudioSource audioSource;
	private AudioSource driftAudioSource;
	private bool engineWorking;
	private bool engineManuallyStopped;
	private float[] defaultSideGrip;
	private ParticleSystem[] driftSmoke;
	private TrailRenderer[] driftTrails;
	private Material driftSmokeMaterial;
	private Material driftTrailMaterial;
	private bool isOnAsphalt;
	private bool isDrifting;
	private float defaultLinearDamping;
	private float defaultAngularDamping;
	private Vector3 startPosition;
	private Quaternion startRotation;
	public bool ReadyMove { get {
			return engineWorking && carInFocus;
		}
	}

	private bool EngineTogglePressed {
		get {
#if ENABLE_LEGACY_INPUT_MANAGER
			if (Input.GetKeyDown(KeyCode.E)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			return keyboard != null && keyboard.eKey.wasPressedThisFrame;
#else
			return false;
#endif
		}
	}

	private float VerticalInput {
		get {
			float value = 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
			value = Input.GetAxis("Vertical");
			if (Mathf.Abs(value) > 0.001f) return value;
#endif
#if ENABLE_INPUT_SYSTEM
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (keyboard != null) {
				if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) value += 1f;
				if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) value -= 1f;
			}
#endif
			return Mathf.Clamp(value, -1f, 1f);
		}
	}

	private float HorizontalInput {
		get {
			float value = 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
			value = Input.GetAxis("Horizontal");
			if (Mathf.Abs(value) > 0.001f) return value;
#endif
#if ENABLE_INPUT_SYSTEM
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (keyboard != null) {
				if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) value += 1f;
				if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) value -= 1f;
			}
#endif
			return Mathf.Clamp(value, -1f, 1f);
		}
	}

	private bool DriftPressed {
		get {
#if ENABLE_LEGACY_INPUT_MANAGER
			if (Input.GetKey(KeyCode.Space)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			return keyboard != null && keyboard.spaceKey.isPressed;
#else
			return false;
#endif
		}
	}

	private bool ResetPressed {
		get {
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
	}

	[System.Serializable]
    public class Wheels{
        [Space(20), Header("Parametrs Wheel")]
        public WheelCollider wheelCollider;
		public GameObject wheelObject;
		public float angleTurningWheel;
        [Range(0, 100)]   public float percentMotorPower;
        [HideInInspector] public float wheelPower;

    }

    void Start () {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
		startPosition = transform.position;
		startRotation = transform.rotation;
		if (rb != null && COM != null) {
			rb.centerOfMass = COM.localPosition;
			defaultLinearDamping = rb.linearDamping;
			defaultAngularDamping = rb.angularDamping;
		}
		defaultSideGrip = new float[wheels.Length];
		for (int i = 0; i < wheels.Length; i++) {
			if (wheels[i].wheelCollider != null) {
				defaultSideGrip[i] = wheels[i].wheelCollider.sidewaysFriction.stiffness;
			} else {
				defaultSideGrip[i] = 1f;
			}
		}
		if (autoStartEngine && carInFocus) {
			StartEngine();
		}
		SetupDriftEffects();
    }

	public void StartEngine () {
		engineManuallyStopped = false;
		if (engineWorking) return;
		engineWorking = true;
		if (audioSource == null) return;
		StartCoroutine("StartEngineCor");
	}

	public void StopEngine () {
		engineManuallyStopped = true;
		if (audioSource != null) audioSource.Stop();
		engineWorking = false;
	}

	IEnumerator StartEngineCor () {
		if (StartEngineClip != null) {
			audioSource.clip = StartEngineClip;
			audioSource.Play();
			yield return new WaitForSeconds(StartEngineClip.length);
		}
		if (WorkingEngineClip != null) {
			audioSource.clip = WorkingEngineClip;
			audioSource.loop = true;
			audioSource.Play();
		}
	}
	void Update () {
		if (ResetPressed) {
			ResetCarToStart();
		}
		if (EngineTogglePressed && carInFocus) {
			if (engineWorking) {
				StopEngine();
			} else {
				StartEngine();
			}
		}
	}
	void FixedUpdate() {
		Vector3 position;
		Quaternion rotation;
		float currentSpeed = rb.linearVelocity.magnitude;
		if (carInFocus && autoStartEngine && !engineWorking && !engineManuallyStopped) {
			StartEngine();
		}
		if (!carInFocus && currentSpeed < 0.01f) return;
		float inputVertical = VerticalInput;
		float inputHorizontal = HorizontalInput;
		float speedRatio = GetSpeedRatio(currentSpeed);
		float steeringScale = GetSteeringScale(speedRatio);
		float effectiveHorizontal = inputHorizontal * steeringScale;
		bool driftPressed = DriftPressed;
		isOnAsphalt = IsOnAsphalt();
		bool driftRequested = carInFocus && isOnAsphalt && driftPressed && currentSpeed > driftMinSpeed;
		float lateralSlip = Mathf.Abs(transform.InverseTransformDirection(rb.linearVelocity).x);
		isDrifting = driftRequested && (Mathf.Abs(effectiveHorizontal) > 0.05f || Mathf.Abs(inputVertical) > 0.05f || lateralSlip > driftSlipForEffects);
		float surfacePower = isOnAsphalt ? 1f : offTrackPowerMultiplier;
		float surfaceMaxSpeed = maxSpeed * (isOnAsphalt ? 1f : offTrackMaxSpeedMultiplier);
		ApplySurfaceFeel(isOnAsphalt, currentSpeed);
		ApplyHighSpeedStability(currentSpeed, driftRequested, speedRatio);
		ApplyDriftPhysics(driftRequested, effectiveHorizontal, currentSpeed, speedRatio);
		UpdateDriftEffects(isDrifting, currentSpeed, lateralSlip);
		for (int i = 0; i < wheels.Length; i++) {
            if (wheels[i].wheelCollider == null) continue;
			ApplyDriftGrip(i, driftRequested, speedRatio);
			if (currentSpeed > surfaceMaxSpeed) {
				wheels[i].wheelPower = 0;
			} else {
				wheels[i].wheelPower = powerEngine * (wheels[i].percentMotorPower * 0.1f) * surfacePower;
			}

			if (carInFocus) {
				if (engineWorking) {
					if (wheels[i].wheelCollider.rpm < 0.01f && inputVertical < 0f || wheels[i].wheelCollider.rpm >= -0.01f && inputVertical >= 0f) {
						wheels[i].wheelCollider.brakeTorque = 0;
						float driftPower = driftRequested ? driftMotorMultiplier : 1f;
						wheels[i].wheelCollider.motorTorque = inputVertical * wheels[i].wheelPower * driftPower;
					} else {
						wheels[i].wheelCollider.motorTorque = 0;
						WheelBrake(wheels[i].wheelCollider);
					}
				} else {
					wheels[i].wheelCollider.motorTorque = 0;
				}

				wheels[i].wheelCollider.steerAngle = effectiveHorizontal * wheels[i].angleTurningWheel;
				if (driftRequested && wheels[i].percentMotorPower > 0f) {
					wheels[i].wheelCollider.brakeTorque = Mathf.Max(wheels[i].wheelCollider.brakeTorque, driftBrakePower);
				}
			} else {
				wheels[i].wheelCollider.motorTorque = 0;
			}

			wheels[i].wheelCollider.GetWorldPose(out position, out rotation);
			wheels[i].wheelObject.transform.position = position;
			wheels[i].wheelObject.transform.localPosition -= wheels[i].wheelCollider.center;
			wheels[i].wheelObject.transform.rotation = rotation;

			if (audioSource != null) {
                var speed = rb.linearVelocity.magnitude;
                audioSource.pitch = 1 + (speed * 0.03f);
            }
        }
    }

	void WheelBrake (WheelCollider wheelCollider) {
		wheelCollider.brakeTorque = brakePower;
	}

	float GetSpeedRatio (float currentSpeed) {
		return Mathf.Clamp01(currentSpeed / Mathf.Max(maxSpeed, 0.1f));
	}

	float GetSteeringScale (float speedRatio) {
		return Mathf.Lerp(1f, highSpeedSteeringReduction, speedRatio * speedRatio);
	}

	void ApplyDriftGrip (int wheelIndex, bool driftPressed, float speedRatio) {
		var wheelCollider = wheels[wheelIndex].wheelCollider;
		var sidewaysFriction = wheelCollider.sidewaysFriction;
		float normalGrip = defaultSideGrip != null && wheelIndex < defaultSideGrip.Length ? defaultSideGrip[wheelIndex] : 1f;
		float targetGrip = normalGrip;
		if (driftPressed) {
			float driftGrip = wheels[wheelIndex].percentMotorPower > 0f ? driftRearSideGrip : driftFrontSideGrip;
			targetGrip = Mathf.Lerp(driftGrip, normalGrip, speedRatio * highSpeedGripRecovery);
		}
		sidewaysFriction.stiffness = Mathf.Lerp(sidewaysFriction.stiffness, targetGrip, Time.fixedDeltaTime * driftGripChangeSpeed);
		wheelCollider.sidewaysFriction = sidewaysFriction;
	}

	void ApplyHighSpeedStability (float currentSpeed, bool driftRequested, float speedRatio) {
		if (rb == null) return;
		float speedFactor = speedRatio * speedRatio;
		if (currentSpeed > 1f) {
			rb.AddForce(-transform.up * currentSpeed * currentSpeed * highSpeedDownforce, ForceMode.Acceleration);
		}

		Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
		float stability = driftRequested ? highSpeedLateralStability * 0.45f : highSpeedLateralStability;
		rb.AddForce(-transform.right * localVelocity.x * stability * speedFactor, ForceMode.Acceleration);

		Vector3 angularVelocity = rb.angularVelocity;
		float yawLimit = Mathf.Lerp(maxDriftYawRate, highSpeedMaxYawRate, speedFactor);
		angularVelocity.y = Mathf.Clamp(angularVelocity.y, -yawLimit, yawLimit);
		rb.angularVelocity = angularVelocity;
	}

	void ApplyDriftPhysics (bool driftRequested, float steeringInput, float currentSpeed, float speedRatio) {
		if (rb == null || !driftRequested) return;
		float speedFactor = Mathf.Clamp01((currentSpeed - driftMinSpeed) / 12f);
		float highSpeedAssist = Mathf.Lerp(1f, highSpeedDriftAssistReduction, speedRatio * speedRatio);
		if (Mathf.Abs(steeringInput) > 0.03f) {
			rb.AddTorque(Vector3.up * steeringInput * driftYawAssist * speedFactor * highSpeedAssist, ForceMode.Acceleration);
			rb.AddForce(transform.right * steeringInput * driftSideAssist * speedFactor * highSpeedAssist, ForceMode.Acceleration);
		}

		Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
		float lateralVelocity = Mathf.Clamp(localVelocity.x, -8f, 8f);
		rb.AddForce(-transform.right * lateralVelocity * driftStability * speedFactor * (1f + speedRatio), ForceMode.Acceleration);

		Vector3 angularVelocity = rb.angularVelocity;
		float driftYawLimit = Mathf.Lerp(maxDriftYawRate, highSpeedMaxYawRate, speedRatio * speedRatio);
		angularVelocity.y = Mathf.Clamp(angularVelocity.y, -driftYawLimit, driftYawLimit);
		rb.angularVelocity = angularVelocity;
	}

	void ResetCarToStart () {
		StopAllCoroutines();
		engineManuallyStopped = false;
		engineWorking = false;
		if (audioSource != null) audioSource.Stop();

		if (rb != null) {
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
			rb.position = startPosition;
			rb.rotation = startRotation;
			rb.linearDamping = defaultLinearDamping;
			rb.angularDamping = defaultAngularDamping;
			rb.WakeUp();
		} else {
			transform.position = startPosition;
			transform.rotation = startRotation;
		}

		transform.position = startPosition;
		transform.rotation = startRotation;
		isDrifting = false;
		isOnAsphalt = true;
		ResetWheelState();
		UpdateDriftEffects(false, 0f, 0f);
		ClearDriftEffects();

		if (autoStartEngine && carInFocus) {
			StartEngine();
		}
	}

	void ResetWheelState () {
		Vector3 position;
		Quaternion rotation;
		for (int i = 0; i < wheels.Length; i++) {
			if (wheels[i].wheelCollider == null) continue;
			wheels[i].wheelCollider.motorTorque = 0f;
			wheels[i].wheelCollider.brakeTorque = 0f;
			wheels[i].wheelCollider.steerAngle = 0f;
			wheels[i].wheelPower = 0f;

			if (defaultSideGrip != null && i < defaultSideGrip.Length) {
				var sidewaysFriction = wheels[i].wheelCollider.sidewaysFriction;
				sidewaysFriction.stiffness = defaultSideGrip[i];
				wheels[i].wheelCollider.sidewaysFriction = sidewaysFriction;
			}

			if (wheels[i].wheelObject == null) continue;
			wheels[i].wheelCollider.GetWorldPose(out position, out rotation);
			wheels[i].wheelObject.transform.position = position;
			wheels[i].wheelObject.transform.localPosition -= wheels[i].wheelCollider.center;
			wheels[i].wheelObject.transform.rotation = rotation;
		}
	}

	void ClearDriftEffects () {
		if (driftAudioSource != null) {
			driftAudioSource.Stop();
			driftAudioSource.volume = 0f;
		}

		for (int i = 0; i < wheels.Length; i++) {
			if (driftSmoke != null && i < driftSmoke.Length && driftSmoke[i] != null) {
				driftSmoke[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			}
			if (driftTrails != null && i < driftTrails.Length && driftTrails[i] != null) {
				driftTrails[i].emitting = false;
				driftTrails[i].Clear();
			}
		}
	}

	bool IsOnAsphalt () {
		int groundedWheels = 0;
		int asphaltWheels = 0;
		for (int i = 0; i < wheels.Length; i++) {
			if (wheels[i].wheelCollider == null) continue;
			WheelHit hit;
			if (!wheels[i].wheelCollider.GetGroundHit(out hit)) continue;
			groundedWheels++;
			if (IsAsphaltCollider(hit.collider)) {
				asphaltWheels++;
			}
		}
		return groundedWheels == 0 || asphaltWheels >= Mathf.Max(1, groundedWheels / 2);
	}

	bool IsAsphaltCollider (Collider hitCollider) {
		if (hitCollider == null) return false;
		if (ContainsAsphalt(hitCollider.gameObject.name)) return true;
		if (hitCollider.transform.parent != null && ContainsAsphalt(hitCollider.transform.parent.name)) return true;

		Renderer renderer = hitCollider.GetComponent<Renderer>();
		if (renderer == null) renderer = hitCollider.GetComponentInParent<Renderer>();
		if (renderer == null) return false;
		for (int i = 0; i < renderer.sharedMaterials.Length; i++) {
			Material material = renderer.sharedMaterials[i];
			if (material != null && ContainsAsphalt(material.name)) return true;
		}
		return false;
	}

	bool ContainsAsphalt (string value) {
		return !string.IsNullOrEmpty(value) && value.IndexOf("Asphalt", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	void ApplySurfaceFeel (bool onAsphalt, float currentSpeed) {
		if (rb == null) return;
		float targetLinearDamping = onAsphalt ? defaultLinearDamping : offTrackLinearDamping;
		float targetAngularDamping = onAsphalt ? defaultAngularDamping : offTrackAngularDamping;
		rb.linearDamping = Mathf.Lerp(rb.linearDamping, targetLinearDamping, Time.fixedDeltaTime * 5f);
		rb.angularDamping = Mathf.Lerp(rb.angularDamping, targetAngularDamping, Time.fixedDeltaTime * 5f);
		if (!onAsphalt && currentSpeed > 0.75f) {
			float rumble = Mathf.PerlinNoise(Time.time * 14f, transform.position.x * 0.07f) - 0.5f;
			rb.AddForce(transform.right * rumble * offTrackRumble, ForceMode.Acceleration);
		}
	}

	void SetupDriftEffects () {
		driftSmoke = new ParticleSystem[wheels.Length];
		driftTrails = new TrailRenderer[wheels.Length];
		driftSmokeMaterial = MakeSmokeMaterial();
		driftTrailMaterial = MakeRuntimeMaterial("DriftTrailMaterial", driftTrailColor);
		for (int i = 0; i < wheels.Length; i++) {
			if (wheels[i].wheelObject == null) continue;
			var smokeObject = new GameObject("Drift Smoke");
			smokeObject.transform.SetParent(wheels[i].wheelObject.transform, false);
			smokeObject.transform.localPosition = Vector3.down * 0.25f;
			var smoke = smokeObject.AddComponent<ParticleSystem>();
			var main = smoke.main;
			main.loop = true;
			main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.2f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
			main.startColor = driftSmokeColor;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.maxParticles = 140;
			var emission = smoke.emission;
			emission.rateOverTime = 0f;
			var shape = smoke.shape;
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.radius = 0.16f;
			var noise = smoke.noise;
			noise.enabled = true;
			noise.strength = 0.18f;
			noise.frequency = 0.75f;
			var colorOverLifetime = smoke.colorOverLifetime;
			colorOverLifetime.enabled = true;
			var gradient = new Gradient();
			gradient.SetKeys(
				new [] {
					new GradientColorKey(driftSmokeColor, 0f),
					new GradientColorKey(Color.white, 1f)
				},
				new [] {
					new GradientAlphaKey(driftSmokeColor.a, 0f),
					new GradientAlphaKey(0f, 1f)
				});
			colorOverLifetime.color = gradient;
			var renderer = smoke.GetComponent<ParticleSystemRenderer>();
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.material = driftSmokeMaterial;
			renderer.minParticleSize = 0.02f;
			renderer.maxParticleSize = 1.25f;
			renderer.sortingFudge = 1f;
			smoke.Stop();
			driftSmoke[i] = smoke;

			var trailObject = new GameObject("Drift Tire Trail");
			trailObject.transform.SetParent(wheels[i].wheelObject.transform, false);
			trailObject.transform.localPosition = Vector3.down * 0.32f;
			var trail = trailObject.AddComponent<TrailRenderer>();
			trail.time = 1.25f;
			trail.minVertexDistance = 0.08f;
			trail.widthMultiplier = 0.12f;
			trail.material = driftTrailMaterial;
			trail.startColor = driftTrailColor;
			trail.endColor = new Color(driftTrailColor.r, driftTrailColor.g, driftTrailColor.b, 0f);
			trail.emitting = false;
			driftTrails[i] = trail;
		}

		driftAudioSource = gameObject.AddComponent<AudioSource>();
		driftAudioSource.loop = true;
		driftAudioSource.playOnAwake = false;
		driftAudioSource.spatialBlend = 0.45f;
		driftAudioSource.volume = 0f;
		driftAudioSource.pitch = 1f;
		driftAudioSource.priority = 48;
		driftAudioSource.dopplerLevel = 0f;
		driftAudioSource.minDistance = 8f;
		driftAudioSource.maxDistance = 70f;
		driftAudioSource.clip = DriftClip != null ? DriftClip : CreateDriftClip();
	}

	Material MakeRuntimeMaterial (string materialName, Color color) {
		var shader = Shader.Find("Universal Render Pipeline/Unlit");
		if (shader == null) shader = Shader.Find("Sprites/Default");
		if (shader == null) shader = Shader.Find("Unlit/Color");
		var material = new Material(shader);
		material.name = materialName;
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
		if (material.HasProperty("_Color")) material.SetColor("_Color", color);
		MakeMaterialTransparent(material);
		return material;
	}

	Material MakeSmokeMaterial () {
		var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
		if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
		if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
		if (shader == null) shader = Shader.Find("Sprites/Default");
		var material = new Material(shader);
		material.name = "Runtime Drift Smoke Material";
		Texture2D smokeTexture = CreateSmokeTexture(64);
		if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", smokeTexture);
		if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", smokeTexture);
		if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", driftSmokeColor);
		if (material.HasProperty("_Color")) material.SetColor("_Color", driftSmokeColor);
		MakeMaterialTransparent(material);
		return material;
	}

	Texture2D CreateSmokeTexture (int size) {
		var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
		texture.name = "Runtime Drift Smoke Soft Texture";
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Bilinear;

		float center = (size - 1) * 0.5f;
		for (int y = 0; y < size; y++) {
			for (int x = 0; x < size; x++) {
				float dx = (x - center) / center;
				float dy = (y - center) / center;
				float distance = Mathf.Sqrt(dx * dx + dy * dy);
				float softCircle = Mathf.Clamp01(1f - distance);
				softCircle = Mathf.SmoothStep(0f, 1f, softCircle);
				float noise = Mathf.PerlinNoise(x * 0.12f, y * 0.12f) * 0.25f + 0.75f;
				float alpha = Mathf.Clamp01(softCircle * noise);
				texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
			}
		}
		texture.Apply();
		return texture;
	}

	void MakeMaterialTransparent (Material material) {
		material.SetOverrideTag("RenderType", "Transparent");
		material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
		if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
		if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
		if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
		material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		material.EnableKeyword("_ALPHABLEND_ON");
	}

	AudioClip CreateDriftClip () {
		const int sampleRate = 44100;
		const int sampleCount = sampleRate * 2;
		var samples = new float[sampleCount];
		for (int i = 0; i < sampleCount; i++) {
			float t = i / (float)sampleRate;
			float hiss = Mathf.PerlinNoise(t * 115f, 0.37f) * 2f - 1f;
			float coarse = Mathf.PerlinNoise(t * 31f, 0.83f) * 2f - 1f;
			float scrape = Mathf.Sin(t * 2f * Mathf.PI * 72f) * 0.16f;
			float rumble = Mathf.Sin(t * 2f * Mathf.PI * 38f) * 0.08f;
			samples[i] = Mathf.Clamp((hiss * 0.42f) + (coarse * 0.22f) + scrape + rumble, -0.75f, 0.75f);
		}

		const int crossfadeSamples = 2048;
		for (int i = 0; i < crossfadeSamples; i++) {
			float blend = i / (float)(crossfadeSamples - 1);
			int tailIndex = sampleCount - crossfadeSamples + i;
			samples[tailIndex] = Mathf.Lerp(samples[tailIndex], samples[i], blend);
		}

		var clip = AudioClip.Create("Generated Drift Tire Skid Loop", sampleCount, 1, sampleRate, false);
		clip.SetData(samples, 0);
		return clip;
	}

	void UpdateDriftEffects (bool active, float currentSpeed, float lateralSlip) {
		float intensity = Mathf.Clamp01(Mathf.Max((currentSpeed - driftMinSpeed) / 12f, lateralSlip / 4f));
		for (int i = 0; i < wheels.Length; i++) {
			bool rearWheel = wheels[i].percentMotorPower > 0f;
			bool emit = active && rearWheel;
			if (driftSmoke != null && i < driftSmoke.Length && driftSmoke[i] != null) {
				var emission = driftSmoke[i].emission;
				emission.rateOverTime = emit ? Mathf.Lerp(25f, 90f, intensity) : 0f;
				if (emit && !driftSmoke[i].isPlaying) driftSmoke[i].Play();
				if (!emit && driftSmoke[i].isPlaying) driftSmoke[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
			}
			if (driftTrails != null && i < driftTrails.Length && driftTrails[i] != null) {
				driftTrails[i].emitting = emit;
			}
		}
		if (driftAudioSource == null) return;
		if (active) {
			if (!driftAudioSource.isPlaying) driftAudioSource.Play();
			driftAudioSource.volume = Mathf.Lerp(driftAudioSource.volume, Mathf.Lerp(0.28f, 0.9f, intensity), Time.fixedDeltaTime * 10f);
			driftAudioSource.pitch = Mathf.Lerp(0.82f, 1.28f, intensity);
		} else {
			driftAudioSource.volume = Mathf.Lerp(driftAudioSource.volume, 0f, Time.fixedDeltaTime * 8f);
			if (driftAudioSource.volume < 0.02f && driftAudioSource.isPlaying) driftAudioSource.Stop();
		}
	}
}
