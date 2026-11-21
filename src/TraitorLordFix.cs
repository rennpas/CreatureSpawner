using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Traitor Lord ('Mantis Traitor Lord', Fungus3_23_boss): pulo (DSlash) interrompido "como se tivesse um teto/parede invisivel" e dano
    // ANTES de ele cair no comeco (relatado). Fonte: scratchpad\FsmDumpTool\out\traitor_scene.txt, FSM 'Mantis' - tudo em Y/X ABSOLUTOS
    // da arena original (pivo dele no chao y=31.49; centro X ~43.5):
    //  - 'DSlash' (gravidade 0, desce na diagonal) so termina com colisao embaixo OU Self Y <= 31.49; 'Land' faz SetPosition y=31.49.
    //    Numa sala com o chao abaixo de 31.49 o ataque acaba no 1o frame ('Jump' sobe so ~9) e ele e TELEPORTADO pra y=31.49 -> o corte
    //    "no ar" relatado. Idem 'Fall'/'Intro Land' da entrada.
    //  - Entrada: nasce em (46.06, 42.71) (11.22 acima do chao), 'Emerge Dust' espera 2 s INVISIVEL com BoxCollider2D+DamageHero ligados e so
    //    depois 'Fall' liga o renderer. Sem perfil ele nascia em heroi+(2,2): encostado no heroi e machucando 2 s antes de aparecer.
    //  - 'Death Shift?' (Self X 58/29) e 'Check L/R' (Self X 42.3/39.2) comparam X absoluto.
    // Correcao: constantes pro chao real (pivo = chao + pes do collider) e X deslocado; ele nasce no alto como na sala original (11.22 acima do
    // chao, ou logo abaixo do teto real), longe do heroi.
    internal static class TraitorLordFix
    {
        public const string Name = "Traitor Lord";
        private const float OrigFloorPivotY = 31.49f, OrigCenterX = 43.5f, OrigSpawnRise = 11.22f, OrigSpawnDx = 2.56f, DefaultFeet = 2.0f;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Mantis", "Fall", "Intro Land", "DSlash", "Land", "Death Shift?", "Check L", "Check R");
                if (fsm == null || ctx == null) return;
                float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out bool found, out _);
                float feet = DefaultFeet, top = 2.5f;
                var col = clone.GetComponent<BoxCollider2D>();
                if (col != null)
                {
                    float sy = Mathf.Abs(clone.transform.lossyScale.y);
                    feet = Mathf.Clamp(-(col.offset.y - col.size.y * 0.5f) * sy, 0.5f, 6f);
                    top = Mathf.Clamp((col.offset.y + col.size.y * 0.5f) * sy, 0.5f, 6f);
                }
                float floorPivot = groundY + feet;
                float dx = ctx.HeroPos.x - OrigCenterX;

                SetCompare(GruzZoteFixes.GetState(fsm, "Fall"), floorPivot);
                SetCompare(GruzZoteFixes.GetState(fsm, "DSlash"), floorPivot);
                SetPosY(GruzZoteFixes.GetState(fsm, "Intro Land"), floorPivot);
                SetPosY(GruzZoteFixes.GetState(fsm, "Land"), floorPivot);
                foreach (var n in new[] { "Death Shift?", "Check L", "Check R" })
                    foreach (var a in GruzZoteFixes.GetState(fsm, n).Actions)
                        if (a is FloatCompare fc && fc.float2 != null && !fc.float2.IsNone && !fc.float2.UsesVariable) fc.float2.Value += dx;

                // Nasce no alto, como na sala original (sem passar do teto real).
                float rise = OrigSpawnRise;
                var hit = Physics2D.Raycast(new Vector2(ctx.HeroPos.x + OrigSpawnDx, floorPivot), Vector2.up, rise + top + 0.3f, SpawnFixups.TerrainMask);
                if (hit.collider != null) rise = Mathf.Max(3f, hit.point.y - top - 0.3f - floorPivot);
                var t = clone.transform;
                t.position = new Vector3(ctx.HeroPos.x + OrigSpawnDx, floorPivot + rise, t.position.z);

                // Sem perfil a coleira ancora no ponto de nascimento (SpawnLeash.Start) = la no alto: RadiusYDown 9.9 abaixo dele ficava ACIMA do
                // chao (nasce 11.22 acima) e ele travava no ar no 'Fall'. Ancora no chao, como o ConfigureLeash faz pros chefes com perfil.
                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null)
                {
                    leash.SetAnchor(new Vector3(ctx.HeroPos.x, floorPivot + 2f, t.position.z));
                    leash.RadiusYUp = Mathf.Max(leash.RadiusYUp, rise + 6f);
                }
                Modding.Logger.Log($"[CreatureSpawner] Traitor Lord: chao(pivo) y={floorPivot:0.##} (chao {(found ? "raycast" : "pes do heroi")} y={groundY:0.##}, pes {feet:0.##}), " +
                                   $"X deslocado {dx:0.##}, nasce {rise:0.##} acima do chao em ({t.position.x:0.##},{t.position.y:0.##})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] TraitorLordFix: {ex}");
            }
        }

        private static void SetCompare(FsmState state, float value)
        {
            foreach (var a in state.Actions)
                if (a is FloatCompare fc && fc.float2 != null && !fc.float2.IsNone && !fc.float2.UsesVariable) fc.float2.Value = value;
        }

        private static void SetPosY(FsmState state, float value)
        {
            foreach (var a in state.Actions)
                if (a is SetPosition sp && sp.y != null && !sp.y.IsNone && !sp.y.UsesVariable) sp.y.Value = value;
        }
    }
}
