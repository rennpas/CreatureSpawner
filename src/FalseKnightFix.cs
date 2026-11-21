using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // False Knight ('False Knight New', Crossroads_10_boss): pulos desproporcionais e sem os barris do teto (relatado). Fonte:
    // scratchpad\FsmDumpTool\out\false_knight_scene.txt, FSM 'FalseyControl'.
    //  - Pulos em X ABSOLUTO da arena original (15..42, centro 28.5): 'Towards' faz FloatClamp do X do heroi em Range Min..Range
    //    Max; 'Rage Jump Antic' pula pro Rage Point X (28.9) SEM clamp de velocidade; 'JA Antic 2' pro Final Point X (34); 'Esc
    //    Jump Antic' pro 43.8. Fora da arena todo pulo mira a sala original. Alem disso o pulo sobe ~12-14 (Y 90/105 amortecido
    //    por 0.85 a cada FixedUpdate) e a coleira padrao (RadiusYUp 9) cortava ele no meio.
    //  - Barris: 'Init' faz GetParent -> $Battle Scene e FindChild('FK Barrel Summon') -> $Barrel Summoner; sem o pai a var fica
    //    null e os SUMMON do 'JA Recoil'/'S Attack Recover'/'Turn' nao chegam em ninguem. O summoner (FSM 'summon') sorteia X em
    //    Summon Min..Max (13.18..44.27) e spawna o barril (pool global) no proprio Y (~40.98, logo abaixo do teto ~43).
    // Correcao (sem ArenaProfile): X deslocado pro heroi do spawn; coleira com folga pra cima; cada copia ganha o seu 'FK Barrel
    // Summon' pre-carregado (o FindChild dele no Init e desligado e a var aponta pra copia), com a faixa X deslocada e cada barril
    // numa coluna livre da sala atual, na mesma altura acima do chao que no original ou logo abaixo do teto real.
    internal static class FalseKnightFix
    {
        public const string Name = "False Knight";
        public const string Scene = "Crossroads_10_boss";
        public const string SummonerPath = "Battle Scene/FK Barrel Summon";
        public static GameObject SummonerPrefab;

        private const float OrigCenterX = 28.5f;
        internal const float BarrelHeight = 13.6f; // Y do summoner 40.98 - chao da arena ~27.4

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (Scene, SummonerPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            SummonerPrefab = EncounterGroups.CacheOne(pre, Scene, SummonerPath, "False Knight fica sem os barris do teto", log);
        }

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "FalseyControl", "Init", "Towards", "Rage Jump Antic", "JA Antic 2");
                if (fsm == null || ctx == null) return;
                float dx = ctx.HeroPos.x - OrigCenterX;
                foreach (var n in new[] { "Range Min", "Range Max", "Rage Point X", "Final Point X" }) AddFloat(fsm, n, dx);
                var esc = GruzZoteFixes.GetState(fsm, "Esc Jump Antic");
                if (esc != null)
                    foreach (var a in esc.Actions)
                        if (a is SetFloatValue sf && sf.floatValue != null && !sf.floatValue.IsNone && !sf.floatValue.UsesVariable) sf.floatValue.Value += dx;

                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null) leash.RadiusYUp = 26f;

                // Onda de choque do golpe no chao: ver FalseKnightShockwaveGuard.
                var recover = GruzZoteFixes.GetState(fsm, "S Attack Recover");
                var waveVar = fsm.FsmVariables.FindFsmGameObject("Shockwave");
                if (recover != null && waveVar != null)
                {
                    var ra = recover.Actions;
                    int ri = -1;
                    for (int i = 0; i < ra.Length; i++) if (ra[i] is SpawnObjectFromGlobalPool) ri = i + 1;
                    if (ri > 0)
                    {
                        var na = new FsmStateAction[ra.Length + 1];
                        Array.Copy(ra, 0, na, 0, ri);
                        na[ri] = new FalseKnightShockwaveAction { Wave = waveVar, Boss = clone };
                        Array.Copy(ra, ri, na, ri + 1, ra.Length - ri);
                        recover.Actions = na;
                    }
                }

                bool barrels = false;
                var summonerVar = fsm.FsmVariables.FindFsmGameObject("Barrel Summoner");
                if (SummonerPrefab != null && summonerVar != null)
                {
                    float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out _, out _);
                    var summoner = UnityEngine.Object.Instantiate(SummonerPrefab, new Vector3(ctx.HeroPos.x, groundY + BarrelHeight, 0f), Quaternion.identity);
                    summoner.name = "FK Barrel Summon (False Knight spawnado)";
                    summoner.AddComponent<DestroyWithOwner>().Owner = clone;
                    summoner.SetActive(true);
                    var sf = GruzZoteFixes.FindFsm(summoner, "summon", "Spawn", "Check Spawns");
                    if (sf != null)
                    {
                        AddFloat(sf, "Summon Min", dx);
                        AddFloat(sf, "Summon Max", dx);
                        var st = GruzZoteFixes.GetState(sf, "Spawn");
                        var old = st.Actions;
                        int at = -1;
                        for (int i = 0; i < old.Length; i++) if (old[i] is SetVector3XYZ) at = i + 1;
                        if (at > 0)
                        {
                            var actions = new FsmStateAction[old.Length + 1];
                            Array.Copy(old, 0, actions, 0, at);
                            actions[at] = new FalseKnightBarrelAction { Pos = sf.FsmVariables.FindFsmVector3("Spawn Pos") };
                            Array.Copy(old, at, actions, at + 1, old.Length - at);
                            st.Actions = actions;
                        }
                        foreach (var a in GruzZoteFixes.GetState(fsm, "Init").Actions)
                            if (a is FindChild fc && fc.childName != null && fc.childName.Value == "FK Barrel Summon") a.Enabled = false;
                        summonerVar.Value = summoner;
                        barrels = true;
                    }
                }
                Modding.Logger.Log($"[CreatureSpawner] False Knight: pulos deslocados em X por {dx:0.##}, barris do teto={(barrels ? "ligados" : "NAO (summoner nao pre-carregado)")}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] FalseKnightFix: {ex}");
            }
        }

        private static void AddFloat(PlayMakerFSM fsm, string name, float add)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value += add;
            else Modding.Logger.LogWarn($"[CreatureSpawner] False Knight: FSM '{fsm.FsmName}' sem a var '{name}'");
        }
    }

    /// <summary>'S Attack Recover', logo depois do SpawnObjectFromGlobalPool da onda: arma o FalseKnightShockwaveGuard nela.</summary>
    internal sealed class FalseKnightShockwaveAction : FsmStateAction
    {
        public FsmGameObject Wave;
        public GameObject Boss;

        public override void OnEnter()
        {
            try
            {
                var wave = Wave != null ? Wave.Value : null;
                if (wave != null)
                {
                    var guard = wave.GetComponent<FalseKnightShockwaveGuard>();
                    if (guard == null) guard = wave.AddComponent<FalseKnightShockwaveGuard>();
                    guard.Arm(Boss);
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] FalseKnightShockwaveAction: {ex}");
            }
            Finish();
        }
    }

    /// <summary>Onda de choque do False Knight ('Shockwave Wave', pool global; dump scratchpad\FsmDumpTool\out\sa48_shockwave.txt).
    /// A FSM 'shockwave' dela, no 'Move', se encerra sozinha por: RayCast2d pra baixo (de y+1, 1.6, camada 8) sem achar chao ->
    /// HIT (some na hora), ou Trigger2dEventLayer (camada 8) -> WALL ('End Pause': 0.15 s de rajada). Fora da sala original a
    /// onda quase sempre acabava na hora ou saia so um pedacinho (relatado). Nesta instancia as duas checagens da FSM sao
    /// desligadas e refeitas aqui com raycasts Terrain simples (chao na frente / parede na frente), a onda e posta na altura do
    /// chao real, e tudo volta ao original quando ela e reciclada (o pool e compartilhado com a luta de verdade).</summary>
    internal sealed class FalseKnightShockwaveGuard : MonoBehaviour
    {
        private static int logs;
        private PlayMakerFSM fsm;
        private FsmBool facingRight;
        private readonly List<FsmStateAction> disabled = new List<FsmStateAction>();
        private bool armed;

        public void Arm(GameObject boss)
        {
            Restore();
            fsm = GruzZoteFixes.FindFsm(gameObject, "shockwave", "Move", "End Particle", "End Pause");
            if (fsm == null) return;
            facingRight = fsm.FsmVariables.FindFsmBool("Facing Right");
            foreach (var a in GruzZoteFixes.GetState(fsm, "Move").Actions)
                if (a.Enabled && (a is RayCast2d || a is BoolTest || a.GetType().Name == "Trigger2dEventLayer"))
                {
                    a.Enabled = false;
                    disabled.Add(a);
                }
            Vector3 p = transform.position;
            var hit = Physics2D.Raycast(new Vector2(p.x, p.y + 3f), Vector2.down, 6f, SpawnFixups.TerrainMask);
            if (hit.collider != null) transform.position = new Vector3(p.x, hit.point.y, p.z);
            armed = true;
            if (logs++ < 6)
                Modding.Logger.Log($"[CreatureSpawner] False Knight: onda criada em y={p.y:0.##} (chefe y={(boss != null ? boss.transform.position.y : 0f):0.##}), " +
                                   $"chao {(hit.collider != null ? $"y={hit.point.y:0.##}" : "NAO achado")}, {disabled.Count} checagens da FSM assumidas");
        }

        private void Update()
        {
            if (!armed || fsm == null || fsm.ActiveStateName != "Move") return;
            float dir = facingRight != null && facingRight.Value ? 1f : -1f;
            Vector2 p = transform.position;
            string evt = null;
            if (Physics2D.Raycast(new Vector2(p.x, p.y + 0.6f), new Vector2(dir, 0f), 0.5f, SpawnFixups.TerrainMask).collider != null) evt = "WALL";
            else if (Physics2D.Raycast(new Vector2(p.x + dir, p.y + 1f), Vector2.down, 2f, SpawnFixups.TerrainMask).collider == null) evt = "HIT";
            if (evt == null) return;
            armed = false;
            fsm.SendEvent(evt);
        }

        private void OnDisable() => Restore();

        private void Restore()
        {
            foreach (var a in disabled) a.Enabled = true;
            disabled.Clear();
            armed = false;
        }
    }

    /// <summary>'Spawn' do summoner, depois do SetVector3XYZ: o barril nasce numa coluna livre da sala atual.</summary>
    internal sealed class FalseKnightBarrelAction : FsmStateAction
    {
        public FsmVector3 Pos;
        private int logs;

        public override void OnEnter()
        {
            try
            {
                if (Pos != null)
                {
                    var p = Pos.Value;
                    CollectorFix.Column(p.x, FalseKnightFix.BarrelHeight, 1f, out float x, out float y, out _);
                    Pos.Value = new Vector3(x, y, p.z);
                    if (logs++ < 5) Modding.Logger.Log($"[CreatureSpawner] False Knight: barril em ({x:0.##},{y:0.##})");
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] FalseKnightBarrelAction: {ex}");
            }
            Finish();
        }
    }
}
