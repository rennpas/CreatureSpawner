using System;
using UnityEngine;

namespace CreatureSpawner
{
    // White Defender: o monte de terra/tremor ('Burrow Effect') que mostra onde ele vai sair ficava um pouco acima do chao (relatado; so visual).
    // Fonte: scratchpad\FsmDumpTool\out\white_defender_scene.txt. O monte tem a FSM 'Keep Y' (Init: SetParent null no 2o frame, depois so
    // acompanha o X do pai todo frame) -> o Y de MUNDO que ele tinha ao nascer (pai + 4.55 local) fica fixo pra sempre, em qualquer chao.
    // (Mesma historia do Dung Defender: ver DungDefenderFix.cs, onde o monte fica ~2.08 acima do chao.)
    // Correcao: o monte acompanha o chao real na coluna X dele (raycast; sem chao, o chao do spawn), com a folga do Dung Defender.
    internal sealed class WhiteDefenderMound : MonoBehaviour
    {
        public const string Name = "White Defender";
        private const float BelowGround = 0.15f;
        private const float AboveGround = 2.08f; // Dung Defender: monte 8.45 sobre o chao 6.37 (pivo em pe 8.97 - pes 2.6)

        private Transform mound;
        private float groundY;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                if (ctx == null) return;
                var mound = clone.transform.Find("Burrow Effect");
                if (mound == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] White Defender: filho 'Burrow Effect' nao encontrado - monte nao ajustado");
                    return;
                }
                var c = clone.AddComponent<WhiteDefenderMound>();
                c.mound = mound;
                // Sem ArenaProfile o ctx.GroundY nao e calculado (fica 0): acha o chao embaixo do heroi aqui.
                c.groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out bool found, out _);
                Modding.Logger.Log($"[CreatureSpawner] White Defender: monte ('Burrow Effect') vai acompanhar o chao real (spawn y={c.groundY:0.##}, {(found ? "raycast" : "pes do heroi")})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] WhiteDefenderMound: {ex}");
            }
        }

        private float nextLog;

        private void LateUpdate()
        {
            if (mound == null) return;
            float x = mound.position.x;
            var hit = Physics2D.Raycast(new Vector2(x, groundY + 3f), Vector2.down, 14f, SpawnFixups.TerrainMask);
            float g = hit.collider != null ? hit.point.y : groundY;
            var p = mound.position;
            var mr = mound.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled)
            {
                // A base do sprite do monte assenta no chao (um pouco abaixo, como terra solta saindo dele).
                mound.position = new Vector3(p.x, p.y + ((g - BelowGround) - mr.bounds.min.y), p.z);
                if (Time.time >= nextLog)
                {
                    nextLog = Time.time + 4f;
                    Modding.Logger.Log($"[CreatureSpawner] White Defender monte: chao={g:0.##} base={mr.bounds.min.y:0.##} topo={mr.bounds.max.y:0.##} pos.y={mound.position.y:0.##}");
                }
            }
            else mound.position = new Vector3(p.x, g + AboveGround, p.z);
        }
    }
}
