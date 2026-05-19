using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SetCamera : MonoBehaviour {

    [SerializeField] private GameObject[] cameras;
	[SerializeField] private int currentCamera;
	[SerializeField] private GameObject[] cars;
	[SerializeField] private int currentCar;

	[SerializeField] private GameObject ui;

	private CarInputController carController;

	private bool GetKeyDown (KeyCode key) {
#if ENABLE_LEGACY_INPUT_MANAGER
		if (Input.GetKeyDown(key)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
		var keyboard = UnityEngine.InputSystem.Keyboard.current;
		if (keyboard == null) return false;
		switch (key) {
			case KeyCode.F1:
				return keyboard.f1Key.wasPressedThisFrame;
			case KeyCode.V:
				return keyboard.vKey.wasPressedThisFrame;
			case KeyCode.C:
				return keyboard.cKey.wasPressedThisFrame;
			default:
				return false;
		}
#else
		return false;
#endif
	}

	void Awake () {
        SetCurrentCamera();
		SetCurrentCar();
    }
	void Update () {
		if (GetKeyDown(KeyCode.F1)) {
			if (ui != null)
				ui.SetActive(!ui.activeInHierarchy);
		}
		if (GetKeyDown(KeyCode.V)) {
            if (currentCamera+1 < cameras.Length){
                currentCamera++;
            }
            else{
                currentCamera = 0;
            }
            SetCurrentCamera(currentCamera);
        }

		if (GetKeyDown(KeyCode.C)) {
			if (currentCar + 1 < cars.Length) {
				currentCar++;
			} else {
				currentCar = 0;
			}
			SetCurrentCar(currentCar);
		}
	}
    public void SetCurrentCamera(int _currentCamera = 0){
        for (var i = 0; i< cameras.Length; i++){
            if (i == _currentCamera)
                cameras[i].SetActive(true);
            else
                cameras[i].SetActive(false);
        }
    }

	public void SetCurrentCar (int _currentCar = 0) {
		if (carController != null) {
			carController.StopEngine();
			carController.carInFocus = false;
		}
		for (var i = 0; i < cars.Length; i++) {
			if (i == _currentCar) {
				foreach (var camera in cameras) {
					camera.GetComponent<CameraClass>().target = cars[i].transform;
					camera.GetComponent<CameraClass>().SetPosition();
				}
				carController = cars[i].GetComponent<CarInputController>();
				transform.position = cars[i].transform.position;
				carController.carInFocus = true;
			}
		}
	}
}
