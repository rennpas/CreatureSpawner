using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Broken Vessel (Abyss_19 'Infected Knight'): nao aparecia (relatado). FSM 'IK Control' (dump abyss19_full.txt): Init -> GGCheckIfBossScene
    // forcado = ACTIVE -> 'Repause' -> 'Waiting' (renderer OFF, invencivel) pra sempre. A cena de verdade e o ramo 'Sleep': BATTLE START ->
    // 'Start Pause' -> 'Rumble Start' -> Spawning 1 (broadcast SCUTTLER SPAWN START) -> Wake 1..3 -> 'Stop Spawning' (SCUTTLER SPAWN END) -> 'Check'
    // (espera sumirem todos os 'Orange Scuttler IK(Clone)') -> Roar Start -> Roar -> Roar End -> luta. Ele comeca como corpo e os bichinhos
    // entram nele. Quem spawna os bichinhos e o objeto 'Scuttler Spawner' DA SALA (FSM 'Spawn': RandomFloat X 15.3..36.4, 'Pos Y' 27.7 ABSOLUTOS,
    // SpawnObjectFromGlobalPool Orange Scuttler IK); cada bichinho procura o objeto de nome EXATO 'Infected Knight' (FindGameObject).
    // Correcao: Redirect Init ACTIVE -> Sleep; WakeEvents manda o BATTLE START; o clone se chama 'Infected Knight'; cada vessel ganha o SEU
    // Scuttler Spawner pre-carregado, deslocado pro chao/centro real. 'Start Pause' (grava infectedKnightEncountered) e o letreiro/area do 'Roar'
    // (objetos da sala) ficam desligados. Mesmas constantes de arena do Lost Kin (mesma sala/FSM).
    internal static class BrokenVesselFix
    {
        public const string Name = "Broken Vessel";
        public const string Scene = "Abyss_19";
        public const string SpawnerPath = "Scuttler Spawner";
        public static GameObject SpawnerPrefab;

        // true  = os bichinhos saem do chao e sao ARREMESSADOS bem alto antes de cair no Vessel (efeito escolhido pelo usuario: o deslocamento de X
        //         tambem entra no sorteio 'Jump' 2..8 do spawner, virando ~56..62 de forca).
        // false = igual ao original da sala: so a posicao X e deslocada, o pulo continua 2..8 (pulinho baixo).
        // Pra voltar ao original: troque pra false e recompile.
        public const bool KeepFlyingScuttlers = false;

        public static void AddProfiles(Dictionary<string, ArenaProfile> d)
        {
            d[Name] = new ArenaProfile
            {
                Ref = new Vector2(26.3f, 27.0f),
                XVars = new[] { "Left X", "Right X", "Jump X", "X Min", "X Max" },
                YVars = new[] { "Air Dash Height", "Min Dstab Height", "Y Min", "Y Max" },
                SpawnAtArenaPosition = true, // o corpo nasce onde estava na sala (pivo 23.33,28.18) + delta
                LeashUp = 20f,
                Redirects = new[] { ("IK Control", "Init", "ACTIVE", "Sleep") },
                WakeEvents = new[] { ("IK Control", "Sleep", "BATTLE START") },
                DisableActions = new[]
                {
                    ("IK Control", "Start Pause", "SetPlayerDataBool"), // infectedKnightEncountered (save)
                    ("IK Control", "Roar", "ActivateGameObject"),       // $AreaTitle (objeto da sala)
                    ("IK Control", "Roar", "SetFsmBool"),
                    ("IK Control", "Roar", "SetFsmString"),
                },
            };
        }

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (Scene, SpawnerPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            SpawnerPrefab = EncounterGroups.CacheOne(pre, Scene, SpawnerPath, "Broken Vessel fica sem os bichinhos da intro", log);
        }

        public static void Attach(GameObject vessel, SpawnContext ctx)
        {
            try
            {
                vessel.name = "Infected Knight"; // os 'Orange Scuttler IK' o procuram por este nome exato
                if (SpawnerPrefab == null || ctx == null) return;
                var t = SpawnerPrefab.transform;
                var sp = UnityEngine.Object.Instantiate(SpawnerPrefab, t.position + ctx.Delta3, t.rotation);
                sp.name = "Scuttler Spawner (Broken Vessel)";
                sp.AddComponent<DestroyWithOwner>().Owner = vessel;
                sp.SetActive(true);

                var fsm = GruzZoteFixes.FindFsm(sp, "Spawn", "Idle", "Spawn", "Pause");
                int shifted = 0;
                bool pool = false;
                if (fsm != null)
                {
                    foreach (var a in GruzZoteFixes.GetState(fsm, "Spawn").Actions)
                    {
                        if (a is RandomFloat rf && rf.storeResult != null && (KeepFlyingScuttlers || rf.storeResult.Name == "Pos X"))
                        {
                            if (rf.min != null && !rf.min.IsNone && !rf.min.UsesVariable) { rf.min.Value += ctx.Delta.x; shifted++; }
                            if (rf.max != null && !rf.max.IsNone && !rf.max.UsesVariable) { rf.max.Value += ctx.Delta.x; shifted++; }
                        }
                        else if (a is SpawnObjectFromGlobalPool so) pool = so.gameObject != null && so.gameObject.Value != null;
                    }
                    var py = fsm.FsmVariables.FindFsmFloat("Pos Y");
                    if (py != null) { py.Value += ctx.Delta.y; shifted++; }
                }
                Modding.Logger.Log($"[CreatureSpawner] Broken Vessel: spawner proprio em {sp.transform.position}, FSM={(fsm != null)}, " +
                                   $"{shifted} valores deslocados (dx {ctx.Delta.x:0.##}, dy {ctx.Delta.y:0.##}), prefab 'Orange Scuttler IK'={(pool ? "ok" : "NULO")}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] BrokenVesselFix: {ex}");
            }
        }
    }
}
