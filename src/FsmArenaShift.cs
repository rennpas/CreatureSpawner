using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Ajustes feitos nas FSMs de UMA copia spawnada (nunca no prefab em cache).
    //
    // IMPORTANTE (PlayMakerFSM.Awake/InitTemplate decompilado): FSMs com template sao recriadas no Awake e as
    // acoes so existem depois do Fsm.Init. Entao so mexemos numa FSM depois que o GameObject dela acordou:
    // logo apos o SetActive(true) pras ativas; pras filhas inativas, via FsmPatchOnEnable (adicionado por
    // ultimo no GameObject -> roda depois do Awake das PlayMakerFSM dele e antes do Start, que e quando a
    // FSM entra no estado inicial).
    internal sealed class SpawnContext
    {
        public ArenaProfile Profile;      // null = sem deslocamento de arena
        public Vector2 Delta;             // (heroi.x - Ref.x, GroundY - Ref.y)
        public Vector3 HeroPos;           // onde o heroi estava no spawn
        public float GroundY;             // chao real embaixo do heroi (raycast Terrain), ver SpawnFixups.FindGroundY
        public bool GroundFound;          // false = raycast nao achou chao (usou os pes do heroi)
        public Vector3 LeashAnchor;       // ancora da coleira pra chefes com perfil
        public string Name;
        public GameObject Prefab;         // clone em cache que foi instanciado (ex. WatcherKnightsEncounter reusa)
        private readonly HashSet<int> patched = new HashSet<int>();

        public Vector3 Delta3 => new Vector3(Delta.x, Delta.y, 0f);

        /// <summary>Corrige toda FSM ja acordada da hierarquia; agenda as das filhas ainda inativas.</summary>
        public void PatchHierarchy(GameObject root)
        {
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm == null) continue;
                if (fsm.gameObject.activeInHierarchy && fsm.Fsm != null && fsm.Fsm.Initialized)
                {
                    PatchFsm(fsm);
                }
                else if (fsm.gameObject.GetComponent<FsmPatchOnEnable>() == null)
                {
                    fsm.gameObject.AddComponent<FsmPatchOnEnable>().Ctx = this;
                }
            }
        }

        public void PatchFsm(PlayMakerFSM fsm)
        {
            if (fsm == null || !patched.Add(fsm.GetInstanceID())) return;
            try
            {
                FsmArenaShift.ForceBossSceneBranch(fsm);
                FsmArenaShift.DisableBattleSceneLookups(fsm);
                FsmArenaShift.DisableGhostDefeatSave(fsm);
                if (Profile != null)
                {
                    FsmArenaShift.Shift(fsm, Delta, Profile);
                    FsmArenaShift.DisableActions(fsm, Profile);
                    FsmArenaShift.Redirect(fsm, Profile);
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] falha ao ajustar FSM '{fsm.FsmName}' de '{fsm.name}': {ex}");
            }
        }
    }

    internal sealed class FsmPatchOnEnable : MonoBehaviour
    {
        public SpawnContext Ctx;
        private bool done;

        private void OnEnable()
        {
            if (done || Ctx == null) return;
            done = true;
            foreach (var fsm in GetComponents<PlayMakerFSM>()) Ctx.PatchFsm(fsm);
        }
    }

    /// <summary>Manda um evento pra uma FSM assim que ela chegar no estado em que espera por ele
    /// (o controlador da sala original, que mandaria isso, nao existe aqui).</summary>
    internal sealed class FsmEventWhenInState : MonoBehaviour
    {
        public string FsmName, StateName, EventName;
        /// <summary>Desiste depois disso (s). &lt;= 0: espera a luta inteira (eventos de meio de luta, ver
        /// ArenaProfile.PhaseEvents).</summary>
        public float Timeout = 6f;
        /// <summary>Tempo (s) que a FSM tem que ficar no estado antes do evento ser mandado.</summary>
        public float Delay;
        private float deadline, enteredAt = -1f;
        private PlayMakerFSM target;

        public static FsmEventWhenInState Add(GameObject go, string fsmName, string state, string evt,
                                              float delay = 0f, float timeout = 6f)
        {
            var c = go.AddComponent<FsmEventWhenInState>();
            c.FsmName = fsmName; c.StateName = state; c.EventName = evt;
            c.Delay = delay; c.Timeout = timeout;
            return c;
        }

        private void Start() => deadline = Time.time + Timeout;

        private void Update()
        {
            if (target == null)
                foreach (var f in GetComponents<PlayMakerFSM>()) if (f.FsmName == FsmName) { target = f; break; }
            if (target == null || (Timeout > 0f && Time.time > deadline)) { Destroy(this); return; }
            if (target.ActiveStateName != StateName) { enteredAt = -1f; return; }
            if (enteredAt < 0f) enteredAt = Time.time;
            if (Time.time - enteredAt < Delay) return;
            if (Timeout <= 0f)
                Modding.Logger.Log($"[CreatureSpawner] '{name}': FSM '{FsmName}' em '{StateName}' ha {Delay:0.##}s -> {EventName}");
            target.SendEvent(EventName);
            Destroy(this);
        }
    }

    internal static class FsmArenaShift
    {
        // Os bosses tem um ramo "Godhome" (GGCheckIfBossScene -> bossSceneEvent) feito justamente pra lutar
        // fora da historia: ele pula o orbe de essencia/"Dream Orb Get" (que faz fade branco + ChangeToScene
        // pro dreamReturnScene), o BeginSceneTransition('Waterways_15') do White Defender, os SetPlayerDataBool
        // de "chefe derrotado" e as trocas de musica. EndGGBossScene so age se BossSceneController.Instance
        // existir (decompilado), entao fora do Godhome ele nao faz nada. Forcamos esse ramo nas copias.
        public static int ForceBossSceneBranch(PlayMakerFSM fsm)
        {
            int n = 0;
            foreach (var st in fsm.FsmStates)
                foreach (var a in st.Actions)
                    if (a is GGCheckIfBossScene gg)
                    {
                        gg.regularSceneEvent = gg.bossSceneEvent;
                        n++;
                    }
            return n;
        }

        // Corpses de chefe de sonho fazem FindGameObject(withTag "Battle Scene") e mexem no 'Battle Control'
        // dele (Activated=true / END) - na sala atual isso acharia o Battle Scene DA SALA e bagunçaria a luta
        // dela. Desliga so essa busca (a variavel fica null e as acoes seguintes viram no-op).
        public static int DisableBattleSceneLookups(PlayMakerFSM fsm)
        {
            int n = 0;
            foreach (var st in fsm.FsmStates)
                foreach (var a in st.Actions)
                    if (a is FindGameObject fg && fg.withTag != null && fg.withTag.Value == "Battle Scene")
                    {
                        a.Enabled = false;
                        n++;
                    }
            return n;
        }

        // Dream Warriors (Elder Hu, Xero, Gorb, Marmu, No Eyes, Galien, Markoth - mesma FSM nos 7, ver
        // scratchpad\FsmDumpTool\out\warriors\*.txt): 'Set Ghost PD Int' faz, no ZERO HP, SetPlayerDataInt
        // <fantasma>Defeated=1. No save em que a essencia ja foi absorvida (=2) isso volta pra "derrotado, essencia
        // pendente" e a sala original oferece a essencia de novo. A recompensa da copia (GhostWarriorReward) nao le
        // esse valor - copia nao grava progresso.
        public static int DisableGhostDefeatSave(PlayMakerFSM fsm)
        {
            if (fsm.FsmName != "Set Ghost PD Int") return 0;
            int n = 0;
            foreach (var st in fsm.FsmStates)
                foreach (var a in st.Actions)
                    if (a is SetPlayerDataInt)
                    {
                        a.Enabled = false;
                        n++;
                    }
            return n;
        }

        /// <summary>Desliga as acoes listadas em ArenaProfile.DisableActions (so nesta copia).</summary>
        public static int DisableActions(PlayMakerFSM fsm, ArenaProfile p)
        {
            int n = 0;
            foreach (var (fsmName, stateName, actionName) in p.DisableActions)
            {
                if (fsm.FsmName != fsmName) continue;
                // "Tipo:EVENTO" = so as acoes desse tipo que mandam EVENTO (SendEventByName.sendEvent). Ex. Absolute
                // Radiance 'Final Impact' manda HUD OUT, ALL CHARMS END, FINAL HIT (pras FSMs dela - precisa ficar)
                // e Halo DOWN, todos SendEventByName.
                string typeName = actionName, evt = null;
                int colon = actionName.IndexOf(':');
                if (colon > 0) { typeName = actionName.Substring(0, colon); evt = actionName.Substring(colon + 1); }
                int before = n;
                bool hasState = false;
                foreach (var st in fsm.FsmStates)
                {
                    if (st.Name != stateName) continue;
                    hasState = true;
                    foreach (var a in st.Actions)
                        if (a != null && a.GetType().Name == typeName && (evt == null || SendsEvent(a, evt)))
                        {
                            a.Enabled = false; n++;
                        }
                }
                // (varias FSMs da hierarquia tem o mesmo nome - ex. 'Control' de cada Radiant Beam - so avisa na certa)
                if (hasState && n == before)
                    Modding.Logger.LogWarn($"[CreatureSpawner] DisableActions: nada casou com '{fsmName}'/'{stateName}'/'{actionName}' em '{fsm.name}'");
            }
            return n;
        }

        private static bool SendsEvent(FsmStateAction a, string evt) =>
            a is SendEventByName s && s.sendEvent != null && s.sendEvent.Value == evt;

        /// <summary>Redireciona transicoes listadas em ArenaProfile.Redirects (so nesta copia). A FSM ja inicializada
        /// guarda o estado destino resolvido em FsmTransition.ToFsmState (Fsm.DoTransition usa ele, nao o nome), entao
        /// os dois sao trocados.</summary>
        public static int Redirect(PlayMakerFSM fsm, ArenaProfile p)
        {
            int n = 0;
            foreach (var (fsmName, stateName, evt, toState) in p.Redirects)
            {
                if (fsm.FsmName != fsmName) continue;
                FsmState target = null, from = null;
                foreach (var st in fsm.FsmStates)
                {
                    if (st.Name == toState) target = st;
                    if (st.Name == stateName) from = st;
                }
                if (from == null) continue; // outra FSM com o mesmo nome (ex. 'Control' dos Radiant Beam)
                bool done = false;
                if (target != null)
                    foreach (var t in from.Transitions)
                        if (t != null && t.EventName == evt)
                        {
                            t.ToState = toState;
                            t.ToFsmState = target;
                            done = true; n++;
                        }
                if (done)
                    Modding.Logger.Log($"[CreatureSpawner] '{fsm.name}' FSM '{fsmName}': '{stateName}' --{evt}--> '{toState}' (redirecionado)");
                else
                    Modding.Logger.LogWarn($"[CreatureSpawner] Redirect falhou: '{fsmName}'/'{stateName}'/{evt} -> '{toState}' em '{fsm.name}'");
            }
            return n;
        }

        private static bool IsConst(FsmFloat f) => f != null && !f.IsNone && !f.UsesVariable;
        private static bool IsConst(FsmVector3 v) => v != null && !v.IsNone && !v.UsesVariable;
        // "None" (UsesVariable com nome vazio) nao e variavel: senao "" entrava em xs/ys e qualquer outro None
        // (ex. GetPosition x=None da Radiance 'Orb Pos') passava a ser tratado como coordenada.
        private static string VarName(FsmFloat f) => f != null && f.UsesVariable && !string.IsNullOrEmpty(f.Name) ? f.Name : null;

        /// <summary>Desloca as coordenadas absolutas de arena desta FSM por d.</summary>
        public static void Shift(PlayMakerFSM fsm, Vector2 d, ArenaProfile p)
        {
            var vars = fsm.FsmVariables;
            // 1) Variaveis que a FSM ja traz com coordenadas absolutas (lista do perfil).
            foreach (var n in p.XVars) { var v = vars.FindFsmFloat(n); if (v != null) v.Value += d.x; }
            foreach (var n in p.YVars) { var v = vars.FindFsmFloat(n); if (v != null) v.Value += d.y; }
            foreach (var n in p.V3Vars) { var v = vars.FindFsmVector3(n); if (v != null) v.Value += new Vector3(d.x, d.y, 0f); }

            // 2) Quais variaveis guardam X/Y absolutos: saida de GetPosition (mundo), entrada de SetPosition
            //    (mundo) e as listadas no perfil.
            var xs = new HashSet<string>(p.XVars);
            var ys = new HashSet<string>(p.YVars);
            var v3s = new HashSet<string>(p.V3Vars);
            var pxs = new HashSet<string>(p.XVars); // so as do perfil (variaveis de OUTRA FSM, ver SetFsmFloat)
            var pys = new HashSet<string>(p.YVars);
            foreach (var st in fsm.FsmStates)
                foreach (var a in st.Actions)
                {
                    if (a is GetPosition gp && gp.space == Space.World)
                    {
                        if (VarName(gp.x) is string gx) xs.Add(gx);
                        if (VarName(gp.y) is string gy) ys.Add(gy);
                    }
                    else if (a is SetPosition sp && sp.space == Space.World)
                    {
                        if (VarName(sp.x) is string sx) xs.Add(sx);
                        if (VarName(sp.y) is string sy) ys.Add(sy);
                    }
                }
            // (perfil) variaveis relativas que so parecem coordenadas por receber um GetPosition (ex. Grey Prince 'Jump X')
            foreach (var n in p.RelativeVars) { xs.Remove(n); ys.Remove(n); }
            var sfx = new HashSet<string>(p.SetFloatXVars);
            var sfy = new HashSet<string>(p.SetFloatYVars);

            // 3) Constantes usadas junto com essas variaveis. Estados de uma sub-arena (ArenaProfile.SubArenas)
            //    usam o chao DELA como referencia vertical em vez do chao da arena principal.
            foreach (var st in fsm.FsmStates)
            {
                var sd = StateDelta(fsm, st.Name, d, p);
                var d3 = new Vector3(sd.x, sd.y, 0f);
                foreach (var a in st.Actions)
                {
                    switch (a)
                    {
                        case SetPosition sp:
                            if (sp.space == Space.World || (sp.space == Space.Self && TargetsRootOwner(fsm, sp.gameObject)))
                            {
                                if (IsConst(sp.x)) sp.x.Value += sd.x;
                                if (IsConst(sp.y)) sp.y.Value += sd.y;
                                if (IsConst(sp.vector)) sp.vector.Value += d3;
                            }
                            break;
                        case FloatCompare fc:
                            ShiftPair(fc.float1, fc.float2, xs, ys, sd);
                            break;
                        case FloatTestToBool ft:
                            ShiftPair(ft.float1, ft.float2, xs, ys, sd);
                            break;
                        case SetFsmFloat sff:
                            // ex. Absolute Radiance 'Arena 2 Start' / 'Scream': SetFsmFloat(Owner, 'Attack Commands',
                            // 'Orb Min X' = 38.9 ...) - reescreve, no meio da luta, os limites ABSOLUTOS das orbes.
                            // So na mesma GameObject (FSM irma) e so pras variaveis listadas no perfil.
                            if (IsConst(sff.setValue) && sff.gameObject != null &&
                                sff.gameObject.OwnerOption == OwnerDefaultOption.UseOwner && sff.variableName != null)
                            {
                                if (pxs.Contains(sff.variableName.Value)) sff.setValue.Value += sd.x;
                                else if (pys.Contains(sff.variableName.Value)) sff.setValue.Value += sd.y;
                            }
                            break;
                        case FloatInRange fr:
                            {
                                float k = AxisDelta(VarName(fr.floatVariable), xs, ys, sd);
                                if (k != 0f)
                                {
                                    if (IsConst(fr.lowerValue)) fr.lowerValue.Value += k;
                                    if (IsConst(fr.upperValue)) fr.upperValue.Value += k;
                                }
                                break;
                            }
                        case FloatClamp fcl:
                            {
                                float k = AxisDelta(VarName(fcl.floatVariable), xs, ys, sd);
                                if (k != 0f)
                                {
                                    if (IsConst(fcl.minValue)) fcl.minValue.Value += k;
                                    if (IsConst(fcl.maxValue)) fcl.maxValue.Value += k;
                                }
                                break;
                            }
                        case SetFloatValue sfval:
                            // So pras variaveis de ArenaProfile.SetFloatXVars/SetFloatYVars (opt-in). Ex. Grey Prince 'Level N':
                            // Shockwave Y = 4.9 (altura ABSOLUTA da onda de choque), escrito no 1o frame da FSM.
                            if (sfval.floatVariable != null && sfval.floatVariable.UsesVariable && IsConst(sfval.floatValue))
                            {
                                if (sfx.Contains(sfval.floatVariable.Name)) sfval.floatValue.Value += sd.x;
                                else if (sfy.Contains(sfval.floatVariable.Name)) sfval.floatValue.Value += sd.y;
                            }
                            break;
                        case SetFsmVector3 sfv:
                            // ex. Absolute Radiance 'First Tele': SetFsmVector3('Teleport'.Destination, (60.63,27))
                            if (IsConst(sfv.setValue) && sfv.variableName != null && v3s.Contains(sfv.variableName.Value))
                                sfv.setValue.Value += d3;
                            break;
                        case SetVector3Value svv:
                            // ex. Absolute Radiance 'Tele 1..10': Destination = (60,37) etc.
                            if (IsConst(svv.vector3Value) && svv.vector3Variable != null && svv.vector3Variable.UsesVariable &&
                                v3s.Contains(svv.vector3Variable.Name))
                                svv.vector3Value.Value += d3;
                            break;
                        case SetVector3XYZ sxyz:
                            // ex. Elder Hu 'Choose L/R': Warp Pos = (39 | 60, 9.91) -> 'Warp' SetPosition(Warp Pos).
                            // So constantes (Radiance 'Set Dest' usa x/y = $Target X/Y: nada muda la).
                            if (sxyz.vector3Variable != null && sxyz.vector3Variable.UsesVariable &&
                                v3s.Contains(sxyz.vector3Variable.Name))
                            {
                                if (IsConst(sxyz.vector3Value)) sxyz.vector3Value.Value += d3;
                                if (IsConst(sxyz.x)) sxyz.x.Value += sd.x;
                                if (IsConst(sxyz.y)) sxyz.y.Value += sd.y;
                            }
                            break;
                        case RandomFloat rf:
                            {
                                // ex. NKG 'FB Tele R': RandomFloat(96..99) -> Tele X -> SetPosition x
                                float k = AxisDelta(VarName(rf.storeResult), xs, ys, sd);
                                if (k != 0f)
                                {
                                    if (IsConst(rf.min)) rf.min.Value += k;
                                    if (IsConst(rf.max)) rf.max.Value += k;
                                }
                                break;
                            }
                        case GetNextPreSpawnedGameObject gn:
                            // So com ArenaProfile.ShiftPreSpawnPositions (opt-in). Ex. Vengefly King GG 'GG Summon'.
                            if (p.ShiftPreSpawnPositions && IsConst(gn.spawnPosition)) gn.spawnPosition.Value += d3;
                            break;
                        case iTweenMoveTo tw:
                            // So nos estados listados em ArenaProfile.WorldTweens (ex. Radiance 'Knight Pull' / Absolute
                            // 'Knight Break Antic': o Knight falso voa pra (64.49,145.32) ABSOLUTO). Outros chefes: intocado.
                            ShiftWorldTween(fsm, st.Name, tw, sd, d, p);
                            break;
                    }
                }
            }
        }

        /// <summary>iTweenMoveTo de mundo com destino constante (sem transformPosition), num estado de ArenaProfile.WorldTweens:
        /// desloca pelo delta do estado e, se pedido, limita o Y a MinAboveFloor acima do chao real (Ref.y + d.y).</summary>
        private static void ShiftWorldTween(PlayMakerFSM fsm, string state, iTweenMoveTo tw, Vector2 sd, Vector2 d, ArenaProfile p)
        {
            foreach (var (wf, ws, minAbove) in p.WorldTweens)
            {
                if (wf != fsm.FsmName || ws != state) continue;
                if (tw.space != Space.World || !IsConst(tw.vectorPosition) || tw.transforms != null && tw.transforms.Length >= 2 ||
                    (tw.transformPosition != null && !tw.transformPosition.IsNone && tw.transformPosition.Value != null))
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] WorldTweens: iTweenMoveTo em '{fsm.FsmName}'/'{state}' de '{fsm.name}' nao e destino constante de mundo - nao deslocado");
                    return;
                }
                var from = tw.vectorPosition.Value;
                var v = from + new Vector3(sd.x, sd.y, 0f);
                if (!float.IsNaN(minAbove)) v.y = Mathf.Max(v.y, p.Ref.y + d.y + minAbove);
                tw.vectorPosition.Value = v;
                Modding.Logger.Log($"[CreatureSpawner] '{fsm.name}' FSM '{fsm.FsmName}' '{state}': iTweenMoveTo {from} -> {v}");
                return;
            }
        }

        /// <summary>Delta de um estado: o da arena principal, ou - se o estado pertence a uma sub-arena do perfil -
        /// o mesmo X e o Y relativo ao chao da sub-arena (chaoReal - FloorY = d.y + Ref.y - FloorY).</summary>
        private static Vector2 StateDelta(PlayMakerFSM fsm, string state, Vector2 d, ArenaProfile p)
        {
            foreach (var (fsmName, floorY, states) in p.SubArenas)
                if (fsmName == fsm.FsmName && Array.IndexOf(states, state) >= 0)
                    return new Vector2(d.x, d.y + p.Ref.y - floorY);
            return d;
        }

        private static float AxisDelta(string var, HashSet<string> xs, HashSet<string> ys, Vector2 d)
        {
            if (var == null) return 0f;
            if (xs.Contains(var)) return d.x;
            if (ys.Contains(var)) return d.y;
            return 0f;
        }

        private static void ShiftPair(FsmFloat a, FsmFloat b, HashSet<string> xs, HashSet<string> ys, Vector2 d)
        {
            float k = AxisDelta(VarName(a), xs, ys, d);
            if (k != 0f && IsConst(b)) { b.Value += k; return; }
            k = AxisDelta(VarName(b), xs, ys, d);
            if (k != 0f && IsConst(a)) a.Value += k;
        }

        // SetPosition em espaco local no proprio dono, quando o dono e raiz (copia spawnada) == mundo.
        // (NKG 'Constrain Y': SetPosition y=6.4 space=Self.)
        private static bool TargetsRootOwner(PlayMakerFSM fsm, FsmOwnerDefault go)
        {
            return go != null && go.OwnerOption == OwnerDefaultOption.UseOwner && fsm.transform.parent == null;
        }
    }
}
