using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Grimm (Grimm_Main_Tent_boss): ataque de ESPINHOS no chao sem espinhos (relatado). Fonte: scratchpad\FsmDumpTool\out\grimm_tent_full.txt.
    // O 'Control' do Grimm so faz 'Spike Attack': SendEventToRegister('SPIKE ATTACK') (global). Quem responde e o objeto 'Grimm Spike
    // Holder' DA SALA (raiz da cena, nao vem no prefab): EventRegister + FSM 'Spike Control' (Idle -> SPIKE ATTACK -> Ready: X Pos =
    // RandomFloatEither(66, 67.125) -> SetPosition x -> SPIKES READY pros 17 filhos 'Grimm Spike' -> 0.55 s -> SPIKES UP ...). Os filhos
    // ficam em x local 0,3,...,45 (y local 1.11) sob o holder em (66, 3.34): uma fileira que cobre a arena original (66..111).
    // Correcao (mesmo padrao do Elder Hu / Hornet Sentinel): cada Grimm spawnado ganha o SEU 'Grimm Spike Holder' pre-carregado,
    // deslocado pelo delta da arena (perfil "Grimm"), com o X sorteado deslocado junto; cada espinho acompanha o chao real na sua
    // coluna (e some se nao ha chao ali ou ele e muito diferente do chao do heroi). Some junto com o Grimm.
    internal static class GrimmSpikes
    {
        public const string Name = "Grimm";
        public const string Scene = "Grimm_Main_Tent_boss";
        public const string HolderPath = "Grimm Spike Holder";
        public static GameObject HolderPrefab;

        private const float MaxGroundDiff = 3f;

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (Scene, HolderPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            HolderPrefab = EncounterGroups.CacheOne(pre, Scene, HolderPath, "Grimm fica sem os espinhos do chao", log);
        }

        public static void Attach(GameObject grimm, SpawnContext ctx)
        {
            try
            {
                if (HolderPrefab == null || ctx == null || ctx.Profile == null) return;
                var t = HolderPrefab.transform;
                var holder = UnityEngine.Object.Instantiate(HolderPrefab, t.position + ctx.Delta3, t.rotation);
                holder.name = "Grimm Spike Holder (Grimm spawnado)";
                holder.AddComponent<DestroyWithOwner>().Owner = grimm;
                holder.SetActive(true);

                var fsm = GruzZoteFixes.FindFsm(holder, "Spike Control", "Ready", "Up");
                int shifted = 0;
                if (fsm != null)
                    foreach (var a in GruzZoteFixes.GetState(fsm, "Ready").Actions)
                        if (a is RandomFloatEither re)
                        {
                            if (re.value1 != null && !re.value1.IsNone && !re.value1.UsesVariable) { re.value1.Value += ctx.Delta.x; shifted++; }
                            if (re.value2 != null && !re.value2.IsNone && !re.value2.UsesVariable) { re.value2.Value += ctx.Delta.x; shifted++; }
                        }

                int kept = 0, off = 0;
                foreach (Transform spike in holder.transform)
                {
                    if (!spike.name.StartsWith("Grimm Spike")) continue;
                    var hit = Physics2D.Raycast(new Vector2(spike.position.x, ctx.GroundY + 8f), Vector2.down, 24f, SpawnFixups.TerrainMask);
                    if (hit.collider == null || Mathf.Abs(hit.point.y - ctx.GroundY) > MaxGroundDiff)
                    {
                        spike.gameObject.SetActive(false);
                        off++;
                        continue;
                    }
                    spike.position += new Vector3(0f, hit.point.y - ctx.GroundY, 0f);
                    kept++;
                }
                Modding.Logger.Log($"[CreatureSpawner] Grimm: fileira de espinhos propria em {holder.transform.position} (original {t.position}), " +
                                   $"FSM={(fsm != null)}, X sorteado deslocado ({shifted} valores), {kept} espinhos no chao, {off} desligados (sem chao/parede)");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GrimmSpikes: {ex}");
            }
        }
    }
}
