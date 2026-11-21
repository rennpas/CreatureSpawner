// RECONSTRUIDO por descompilacao (ilspycmd 11.1) do CreatureSpawner.dll instalado (build de 07/11/2026 17:36): o .cs
// original sumiu do scratchpad antigo (limpeza de pasta temporaria). Comentarios originais perdidos; codigo = o compilado.
using UnityEngine;

namespace CreatureSpawner;

public class SpawnLeash : MonoBehaviour
{
	public float RadiusX = 27f;

	public float RadiusYUp = 9f;

	public float RadiusYDown = 9.900001f;

	private Vector3 anchor;

	private bool anchorSet;

	private Rigidbody2D rb;

	public Vector3 Anchor => anchor;

	public void SetAnchor(Vector3 worldAnchor)
	{
		anchor = worldAnchor;
		anchorSet = true;
	}

	private void Start()
	{
		rb = GetComponent<Rigidbody2D>();
		if (!anchorSet)
		{
			anchor = transform.position;
			anchorSet = true;
		}
	}

	private void LateUpdate()
	{
		if (!anchorSet)
		{
			return;
		}
		Vector3 vector = transform.position - anchor;
		float num = ((vector.y >= 0f) ? RadiusYUp : RadiusYDown);
		float num2 = vector.x / RadiusX;
		float num3 = vector.y / num;
		float num4 = Mathf.Sqrt(num2 * num2 + num3 * num3);
		if (!(num4 > 1f))
		{
			return;
		}
		float num5 = 1f / num4;
		vector.x *= num5;
		vector.y *= num5;
		transform.position = anchor + vector;
		if (!(rb != null) || rb.bodyType == RigidbodyType2D.Static)
		{
			return;
		}
		Vector2 vector2 = new Vector2(vector.x / (RadiusX * RadiusX), vector.y / (num * num));
		if (vector2.sqrMagnitude > 1E-12f)
		{
			vector2.Normalize();
			float num6 = Vector2.Dot(rb.velocity, vector2);
			if (num6 > 0f)
			{
				rb.velocity -= vector2 * num6;
			}
		}
	}
}
