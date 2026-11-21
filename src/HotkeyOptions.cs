// RECONSTRUIDO por descompilacao (ilspycmd 11.1) do CreatureSpawner.dll instalado (build de 07/11/2026 17:36): o .cs
// original sumiu do scratchpad antigo (limpeza de pasta temporaria). Comentarios originais perdidos; codigo = o compilado.
using UnityEngine;

namespace CreatureSpawner;

internal static class HotkeyOptions
{
	public struct Option(string label, params KeyCode[] codes)
	{
		public string Label = label;

		public KeyCode[] Codes = codes;
	}

	public static readonly Option[] All = new Option[15]
	{
		new Option("Ctrl", KeyCode.LeftControl, KeyCode.RightControl),
		new Option("Alt", KeyCode.LeftAlt, KeyCode.RightAlt),
		new Option("Tab", KeyCode.Tab),
		new Option("P", KeyCode.P),
		new Option("O", KeyCode.O),
		new Option("Insert", KeyCode.Insert),
		new Option("Home", KeyCode.Home),
		new Option("End", KeyCode.End),
		new Option("Page Up", KeyCode.PageUp),
		new Option("Page Down", KeyCode.PageDown),
		new Option("F1", KeyCode.F1),
		new Option("F2", KeyCode.F2),
		new Option("F3", KeyCode.F3),
		new Option("F4", KeyCode.F4),
		new Option("`", KeyCode.BackQuote)
	};

	public static string[] Labels()
	{
		string[] array = new string[All.Length];
		for (int i = 0; i < All.Length; i++)
		{
			array[i] = All[i].Label;
		}
		return array;
	}

	public static int IndexOfLabel(string label)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Label == label)
			{
				return i;
			}
		}
		return 0;
	}

	public static bool WasPressedThisFrame(int index)
	{
		if (index < 0 || index >= All.Length)
		{
			index = 0;
		}
		KeyCode[] codes = All[index].Codes;
		foreach (KeyCode key in codes)
		{
			if (Input.GetKeyDown(key))
			{
				return true;
			}
		}
		return false;
	}
}
