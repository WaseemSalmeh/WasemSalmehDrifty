using UnityEngine;

public class RaceCheckpoint : MonoBehaviour {
	private RaceGameManager manager;
	private int checkpointIndex;
	private Renderer[] renderers;

	public void Initialize (RaceGameManager raceManager, int index) {
		manager = raceManager;
		checkpointIndex = index;
		renderers = GetComponentsInChildren<Renderer>();
		SetState(false, false);
	}

	public void SetState (bool active, bool passed) {
		if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
		for (int i = 0; i < renderers.Length; i++) {
			if (renderers[i] == null) continue;
			renderers[i].enabled = true;
			Material material = renderers[i].material;
			Color color = passed ? new Color(0.12f, 1f, 0.42f, 0.9f) : new Color(1f, 0.06f, 0.04f, active ? 1f : 0.32f);
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Color")) material.SetColor("_Color", color);
			if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * (active ? 2.2f : 0.75f));
		}
	}

	void OnTriggerEnter (Collider other) {
		if (manager != null) {
			manager.TryPassCheckpoint(checkpointIndex, other);
		}
	}
}
