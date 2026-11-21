using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Hive Knight sem as bolas de espinhos e sem as abelhas caindo (relatado). Fonte: scratchpad\FsmDumpTool\out\hive_knight_scene.txt.
    // A FSM 'Control' dele so MANDA eventos pra objetos irmaos na sala (Hive_05 'Battle Scene'), que nao vem no prefab dele - o
    // SceneLinkSanitizer zera as 3 vars (ModLog: 'Droppers', 'Globs Container', 'Swarm Audio'):
    //  - 'Glob Strike': SendEventByName($Globs Container, FIRE). 'Globs' (FSM 'Control') pega 3 filhos 'Hive Knight Glob', poe em
    //    x local -7.57/-0.57/+6.43, desparenta e manda FIRE; cada bola sobe 9, estoura e liga os 'Stingers' (HiveKnightStinger so usa
    //    posicao local), depois volta pro pai ($Parent, interno ao grupo) em y local 0.
    //  - 'Roar Recover': SendEventByName($Droppers, SWARM, filhos). Cada 'Bee Dropper' (FSM 'Control'): Pause -> 'Swarm Start'
    //    (SetPosition x aleatorio em X Left..X Right = 58.4..80, y = Start Y = 42) -> 'Swarm' (persegue o heroi em X, cai a -13 ate
    //    Y < 22) -> Reset.
    //  - 'Roar': ActivateGameObject($Swarm Audio) = so o som.
    // Correcao (mesmo padrao do ElderHuRings/HornetSentinelBarbs): cada copia ganha os seus 'Globs'/'Droppers'/'Swarm Audio',
    // deslocados da arena original (centro X 68.95 = meio de Left X..Right X, chao Ground Y 28.694) pro heroi/chao do spawn, e as
    // vars apontam pras copias. O Hive Knight nao tem ArenaProfile, entao o delta e calculado aqui. 'Left X'/'Right X' dele (alvo
    // do pulo no 'Aim Jump', lado do teleporte) vao junto no mesmo delta.
    internal sealed class HiveKnightAttacks : MonoBehaviour
    {
        public const string Scene = "Hive_05";
        public const string GlobsPath = "Battle Scene/Globs";
        public const string DroppersPath = "Battle Scene/Droppers";
        public const string SwarmAudioPath = "Battle Scene/Swarm Audio";
        public static GameObject GlobsPrefab, DroppersPrefab, SwarmAudioPrefab;

        private const float OrigCenterX = 68.95f, OrigGroundY = 28.694f;
        private static readonly Vector3 OrigGlobsPos = new Vector3(69.99f, 24.99f, 0f);

        private GameObject owner;
        private readonly List<GameObject> parts = new List<GameObject>();

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (Scene, GlobsPath);
            yield return (Scene, DroppersPath);
            yield return (Scene, SwarmAudioPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            GlobsPrefab = EncounterGroups.CacheOne(pre, Scene, GlobsPath, "Hive Knight fica sem as bolas de espinhos", log);
            DroppersPrefab = EncounterGroups.CacheOne(pre, Scene, DroppersPath, "Hive Knight fica sem as abelhas caindo", log);
            SwarmAudioPrefab = EncounterGroups.CacheOne(pre, Scene, SwarmAudioPath, "Hive Knight fica sem o som do enxame", log);
        }

        public static void Attach(GameObject knight, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(knight, "Control", "Glob Strike", "Roar Recover");
                if (fsm == null || ctx == null) return;
                float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out bool found, out _);
                var delta = new Vector3(ctx.HeroPos.x - OrigCenterX, groundY - OrigGroundY, 0f);

                AddFloat(fsm, "Left X", delta.x);
                AddFloat(fsm, "Right X", delta.x);

                var keeper = knight.AddComponent<HiveKnightAttacks>();
                keeper.owner = knight;

                if (GlobsPrefab != null)
                {
                    var globs = Instantiate(GlobsPrefab, OrigGlobsPos + delta, Quaternion.identity);
                    globs.name = "Globs (Hive Knight spawnado)";
                    ActivateChildren(globs.transform);
                    globs.SetActive(true);
                    keeper.parts.Add(globs);
                    // As bolas saem do pai (SetParent null) durante o ataque -> guardadas a parte pra sumirem com o chefe.
                    foreach (Transform g in globs.transform) keeper.parts.Add(g.gameObject);
                    Link(fsm, "Globs Container", globs);
                }
                if (DroppersPrefab != null)
                {
                    var droppers = Instantiate(DroppersPrefab, Vector3.zero, Quaternion.identity);
                    droppers.name = "Droppers (Hive Knight spawnado)";
                    ActivateChildren(droppers.transform);
                    droppers.SetActive(true);
                    keeper.parts.Add(droppers);
                    int bees = 0;
                    foreach (Transform b in droppers.transform)
                    {
                        b.position += delta;
                        var bf = GruzZoteFixes.FindFsm(b.gameObject, "Control", "Swarm Start", "Swarm");
                        if (bf == null) continue;
                        AddFloat(bf, "X Left", delta.x);
                        AddFloat(bf, "X Right", delta.x);
                        AddFloat(bf, "Start Y", delta.y);
                        foreach (var a in GruzZoteFixes.GetState(bf, "Swarm").Actions)
                            if (a is FloatCompare fc && fc.float2 != null && !fc.float2.UsesVariable) fc.float2.Value += delta.y;
                        bees++;
                    }
                    Link(fsm, "Droppers", droppers);
                    Modding.Logger.Log($"[CreatureSpawner] Hive Knight: {bees} abelha(s) ajustada(s)");
                }
                if (SwarmAudioPrefab != null)
                {
                    var audio = Instantiate(SwarmAudioPrefab);
                    audio.name = "Swarm Audio (Hive Knight spawnado)";
                    keeper.parts.Add(audio); // fica inativo: o 'Roar' liga
                    Link(fsm, "Swarm Audio", audio);
                }
                Modding.Logger.Log($"[CreatureSpawner] Hive Knight: ataques da sala ligados (globs={GlobsPrefab != null}, abelhas={DroppersPrefab != null}, " +
                                   $"som={SwarmAudioPrefab != null}) delta=({delta.x:0.##},{delta.y:0.##}) chao={(found ? "raycast" : "pes do heroi")} y={groundY:0.##}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] HiveKnightAttacks: {ex}");
            }
        }

        // Filhos de um grupo pre-carregado podem vir com activeSelf=false (visto no 'Brothers' dos Nailmasters). Liga os filhos e os
        // netos que sao ativos na cena; 'Splat' e os 'Stinger' (bisnetos) sao desligados na cena e continuam assim.
        private static void ActivateChildren(Transform root)
        {
            foreach (Transform c in root)
            {
                c.gameObject.SetActive(true);
                foreach (Transform g in c)
                    if (g.name != "Splat") g.gameObject.SetActive(true);
            }
        }

        private static void AddFloat(PlayMakerFSM fsm, string name, float add)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value += add;
            else Modding.Logger.LogWarn($"[CreatureSpawner] Hive Knight: FSM '{fsm.FsmName}' de '{fsm.name}' sem a var '{name}'");
        }

        private static void Link(PlayMakerFSM fsm, string name, GameObject go)
        {
            var v = fsm.FsmVariables.FindFsmGameObject(name);
            if (v != null) v.Value = go;
            else Modding.Logger.LogWarn($"[CreatureSpawner] Hive Knight: FSM '{fsm.FsmName}' sem a var '{name}'");
        }

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            foreach (var p in parts) if (p != null) Destroy(p);
            parts.Clear();
        }
    }
}
