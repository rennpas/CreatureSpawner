// RECONSTRUIDO por descompilacao (ilspycmd 11.1) do CreatureSpawner.dll instalado (build de 07/11/2026 17:36): o .cs
// original sumiu do scratchpad antigo (limpeza de pasta temporaria). Comentarios originais perdidos; codigo = o compilado.
using System;
using System.Linq;
using UnityEngine;

namespace CreatureSpawner;

public class CreatureSpawnerPanel : MonoBehaviour
{
	public CreatureSpawnerMod Mod;

	private bool panelOpen;

	private Vector2 scrollPos;

	private string statusMessage = "";

	private string search = "";

	private void Update()
	{
		if (HotkeyOptions.WasPressedThisFrame(Mod.HotkeyIndex))
		{
			panelOpen = !panelOpen;
		}
	}

	private void OnGUI()
	{
		if (!panelOpen)
		{
			return;
		}
		float num = 360f;
		float height = 480f;
		Rect position = new Rect(20f, 20f, num, height);
		string label = HotkeyOptions.All[(Mod.HotkeyIndex >= 0 && Mod.HotkeyIndex < HotkeyOptions.All.Length) ? Mod.HotkeyIndex : 0].Label;
		GUI.Box(position, $"Spawnar Criatura ({Mod.CachedPrefabs.Count}) - {label} pra fechar");
		GUILayout.BeginArea(new Rect(position.x + 10f, position.y + 50f, position.width - 20f, position.height - 85f));
		GUI.SetNextControlName("CreatureSearch");
		search = GUILayout.TextField(search, GUILayout.Width(num - 40f));
		scrollPos = GUILayout.BeginScrollView(scrollPos);
		IOrderedEnumerable<string> orderedEnumerable = Mod.CachedPrefabs.Keys.OrderBy((string n) => n);
		if (!string.IsNullOrEmpty(search))
		{
			orderedEnumerable = from n in orderedEnumerable
				where n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || (Mod.LocalizedNames.TryGetValue(n, out var value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
				orderby n
				select n;
		}
		foreach (string item in orderedEnumerable)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(item, GUILayout.Width(200f));
			if (GUILayout.Button("Spawnar"))
			{
				SpawnNearPlayer(item);
			}
			GUILayout.EndHorizontal();
		}
		if (Mod.CachedPrefabs.Count == 0)
		{
			GUILayout.Label("(nenhuma criatura carregada ainda)");
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
		GUI.Label(new Rect(position.x + 10f, position.y + position.height - 30f, position.width - 20f, 25f), statusMessage);
	}

	private void SpawnNearPlayer(string name)
	{
		try
		{
			if (!Mod.CachedPrefabs.TryGetValue(name, out var value) || value == null)
			{
				statusMessage = "Nao foi possivel spawnar '" + name + "'";
				return;
			}
			HeroController instance = HeroController.instance;
			if (instance == null)
			{
				statusMessage = "Personagem ainda nao carregado";
				return;
			}
			DreamDeathGuard.Ensure(instance);
			SpawnContext ctx = SpawnFixups.Prepare(name, value, instance.transform.position, out var spawnPos);
			GameObject gameObject = UnityEngine.Object.Instantiate(value, spawnPos, Quaternion.identity);
			gameObject.AddComponent<SpawnLeash>();
			DormantBossFix.Apply(gameObject);
			SpawnFixups.BeforeActivate(gameObject, ctx);
			gameObject.SetActive(value: true);
			SpawnFixups.AfterActivate(gameObject, ctx);
			statusMessage = $"'{name}' spawnado em {spawnPos}";
		}
		catch (Exception ex)
		{
			statusMessage = "Erro: " + ex.Message;
		}
	}
}
