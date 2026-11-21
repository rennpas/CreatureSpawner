using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Gruz Mother ('Crossroads_04/_Enemies/Giant Fly') e Zote Crew Fat/Normal/Tall (GG_Mighty_Zote, adds do Eternal Ordeal). Mesmo
    // esquema do VariantEncounters (arquivo proprio pra nao mexer no que ja funciona). Evidencia (dumps novos):
    //   scratchpad\FsmDumpTool\out\gruzzote\gruz_c04.txt (FSMs da Gruz Mother na cena), sa40.txt (corpse 'Corpse Big Fly 1' +
    //   'Corpse Big Fly Burster'), c04_flyspawn.txt ('_Enemies/Fly Spawn' do Crossroads_04), zote_fat/normal/tall.txt,
    //   sa481.txt ('Zote Crew Death', 'Shockwave Wave Short'); BundleProbe (repacked_full_v9): fsm/mb/edges/clips.
    internal static class GruzZoteFixes
    {
        // ---------------------------------------------------------------- preload extra (Gruz Mother)
        // As moscas que saem da Gruz Mother morta NAO sao filhas dela nem do corpse: sao o objeto da SALA '_Enemies/Fly Spawn'
        // (7 Gruzzers 'Fly'..'Fly 6', guardados em (91.6,-29.4), bem abaixo da arena). O 'Corpse Big Fly Burster' (criado pelo
        // corpse no 'Blow') faz no 'Initiate' FindGameObject(objectName 'Fly Spawn') e, no 'Spawn Flies 2' (depois do Burst), so
        // SetPosition($Fly Spawn, posicao dele) - as moscas acordam sozinhas quando chegam perto da camera (bouncer_control
        // 'Initialise': distancia ate a camera < 44; FSM ligada pelo FSMActivator ao entrar na ActiveRegion). Fora do Crossroads_04
        // o Find devolve null -> so a animacao do Burst, nenhuma mosca. O Crossroads_04 ja e pre-carregado (a propria Gruz Mother).
        public const string GruzScene = "Crossroads_04";
        public const string GruzFlySpawnPath = "_Enemies/Fly Spawn";
        public static GameObject GruzFlySpawnPrefab;

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (GruzScene, GruzFlySpawnPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            GruzFlySpawnPrefab = EncounterGroups.CacheOne(pre, GruzScene, GruzFlySpawnPath, "Gruz Mother morre sem soltar as moscas", log);
        }

        public static bool IsZoteCrew(string name) =>
            name == "Zote Crew Fat" || name == "Zote Crew Normal" || name == "Zote Crew Tall";

        /// <summary>Chamado no fim do SpawnFixups.AfterActivate (FSMs ja acordadas e deslocadas, antes do Start delas).</summary>
        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            if (ctx == null || clone == null) return;
            if (ctx.Name == "Gruz Mother")
            {
                GruzMotherFix.Attach(clone);
                GruzMotherFlies.Attach(clone);
            }
            if (IsZoteCrew(ctx.Name)) ZoteCrewFix.Attach(clone, ctx);
        }

        // ---------------------------------------------------------------- perfis (Zote Crew)
        // GG_Mighty_Zote (Eternal Ordeal). Os tres 'Zote Crew' tem a MESMA FSM 'Control' (so muda o tipo de ataque):
        //  - Init (GetHP) -> 'Dormant' (SEM acoes), unica saida SPAWN, mandado pelo 'Battle Control' da sala (fora do preload) ->
        //    ficavam parados pra sempre (o relato). SPAWN -> Multiply -> 'Spawn Antic': RandomFloat(91.54..114.56) -> X Pos e
        //    SetPosition(X Pos, 21.5) MUNDO (cai do teto da arena), invencivel 1s -> Activate (SetIsDead false, SetHP $HP) ->
        //    Tumble Out -> In Air -> LAND -> luta. Dr: X Pos vs 103.96 (centro). Fat 'Land Waves': onda em y=$Shockwave Y (5 =
        //    chao; a FSM 'shockwave' da 'Shockwave Wave Short' so vive com chao a 1 de distancia - raycast relativo). Tall 'Grav':
        //    SetPosition y=7.17 (pouso) + ConstrainPosition X 87.6..118.56. Constantes de SetPosition/RandomFloat/FloatCompare
        //    sao deslocadas sozinhas pelo FsmArenaShift; Shockwave Y (variavel serializada) vai em YVars.
        //  - Chao: EdgeCollider2D do GG_Mighty_Zote em y=5 de x=75 a 130 (BundleProbe 'edges level481'), teto y=23. Centro X =
        //    meio do RandomFloat do Spawn Antic (103.05; ConstrainPosition do Tall da 103.08). A queda sai 16.5 acima do chao =
        //    13.1 acima da ancora da coleira -> LeashUp 14 (o ZoteCrewFix ainda limita a altura pelo teto da sala atual).
        //  - MORTE (o bug do HP negativo / mascara repetida): HealthManager hasSpecialDeath=1 (BundleProbe 'mb' nos 3). Die()
        //    (decompilado) manda ZERO HP e, com hasSpecialDeath, faz so NonFatalHit e RETORNA - nunca marca isDead, nunca chama
        //    EnemyDeathEffects/OnDeath. ZERO HP (global) -> 'Death': SpawnObjectFromGlobalPool 'Zote Crew Death' (a mascara
        //    voando, sharedassets481), FreezeMoment, SendEventToRegister 'ZOTE WARRIOR KILLED' -> 'Death Reset': kinematic +
        //    SetPosition y=99 MUNDO (o "esconderijo" fora da tela da arena original) -> 'Dormant' de novo, esperando o proximo SPAWN
        //    da sala (o Eternal Ordeal RECICLA os Zotes). Aqui: y=99 vira um teleporte pra cima (a coleira segura na borda de cima
        //    = "voou pra cima"), ele fica parado no Dormant, vulneravel, com hp <= 0 (TakeDamage: hp = Max(hp - dano, -50) = o -50
        //    visto) e CADA golpe seguinte -> TakeDamage -> hp <= 0 -> Die -> ZERO HP -> 'Death' de novo -> outra mascara. Player.log
        //    do teste: 70 "Object Pool ... Zote Crew Death" = um por golpe. Uma copia so, nao varias empilhadas.
        //    Correcao: a copia nao e reciclada - no fim do 'Death' (mascara ja emitida, igual ao original) ela e marcada isDead e
        //    removida (ZoteCrewFix). 'Death Reset' SetPosition y=99 desligado por garantia; 'ZOTE WARRIOR KILLED' (contador do
        //    Eternal Ordeal via EventRegister) desligado - copia nao conta pra luta real.
        public static void AddProfiles(Dictionary<string, ArenaProfile> d)
        {
            foreach (var n in new[] { "Zote Crew Fat", "Zote Crew Normal", "Zote Crew Tall" })
            {
                d[n] = new ArenaProfile
                {
                    Ref = new Vector2(103.05f, 5.0f),
                    YVars = new[] { "Shockwave Y" }, // so o Fat tem (FindFsmFloat null nos outros = ignorado)
                    LeashUp = 14f,
                    WakeEvents = new[] { ("Control", "Dormant", "SPAWN") },
                    DisableActions = new[]
                    {
                        ("Control", "Death", "SendEventToRegister"), // 'ZOTE WARRIOR KILLED' (contador do Eternal Ordeal)
                        ("Control", "Death Reset", "SetPosition"),   // y=99 (esconderijo da arena original)
                    },
                };
            }
        }

        internal static PlayMakerFSM FindFsm(GameObject go, string fsmName, params string[] states)
        {
            if (go == null) return null;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != fsmName) continue;
                bool ok = true;
                foreach (var s in states) if (GetState(f, s) == null) { ok = false; break; }
                if (ok) return f;
            }
            return null;
        }

        internal static FsmState GetState(PlayMakerFSM f, string name)
        {
            if (f == null || f.Fsm == null) return null;
            foreach (var st in f.FsmStates) if (st.Name == name) return st;
            return null;
        }

        internal static bool IsConst(FsmFloat f) => f != null && !f.IsNone && !f.UsesVariable;
    }

    // ==================================================================== Zote Crew (Fat/Normal/Tall)
    internal static class ZoteCrewFix
    {
        private const float WallMargin = 2f;      // folga da parede pro X da queda
        private const float CeilingMargin = 2.5f; // pivo -> topo do corpo (Fat/Tall), folga do teto
        private const float MinDropAboveFloor = 3f;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Control", "Death", "Spawn Antic", "Dormant");
                if (fsm == null)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': FSM 'Control' com Death/Spawn Antic/Dormant nao encontrada - morte/queda nao ajustadas");
                    return;
                }
                // 1) Morte: no FIM do 'Death' (depois da mascara 'Zote Crew Death' sair), marca isDead e remove a copia.
                var death = GruzZoteFixes.GetState(fsm, "Death");
                var old = death.Actions;
                var actions = new FsmStateAction[old.Length + 1];
                Array.Copy(old, actions, old.Length);
                actions[old.Length] = new RemoveSpawnedCopyAction
                {
                    Copy = clone,
                    Hm = clone.GetComponent<HealthManager>(),
                    Label = ctx.Name,
                };
                death.Actions = actions;

                // 2) Queda do 'Spawn Antic' dentro da sala atual (as constantes ja foram deslocadas pelo FsmArenaShift).
                ClampDrop(fsm, ctx);
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] ZoteCrewFix: {ex}");
            }
        }

        // Spawn Antic: X = RandomFloat(91.54..114.56)+dx (+-11.5 do heroi), Y = 21.5+dy (16.5 acima do chao). Na arena original o
        // teto fica 18 acima do chao e as paredes fora dessa faixa; numa sala qualquer ele poderia nascer dentro/acima do teto (e
        // cair em cima dele, fora da sala) ou dentro de uma parede. Limita pelo Terrain em volta do heroi.
        private static void ClampDrop(PlayMakerFSM fsm, SpawnContext ctx)
        {
            var st = GruzZoteFixes.GetState(fsm, "Spawn Antic");
            RandomFloat rf = null; SetPosition sp = null;
            foreach (var a in st.Actions)
            {
                if (rf == null && a is RandomFloat r && r.storeResult != null && r.storeResult.Name == "X Pos") rf = r;
                if (sp == null && a is SetPosition p && p.space == Space.World) sp = p;
            }
            float ground = ctx.GroundY;
            float hx = ctx.HeroPos.x;
            int mask = SpawnFixups.TerrainMask;
            if (rf != null && GruzZoteFixes.IsConst(rf.min) && GruzZoteFixes.IsConst(rf.max))
            {
                float min = rf.min.Value, max = rf.max.Value;
                var origin = new Vector2(hx, ground + 1.5f);
                var hl = Physics2D.Raycast(origin, Vector2.left, hx - min + WallMargin, mask);
                var hr = Physics2D.Raycast(origin, Vector2.right, max - hx + WallMargin, mask);
                if (hl.collider != null) min = Mathf.Max(min, hl.point.x + WallMargin);
                if (hr.collider != null) max = Mathf.Min(max, hr.point.x - WallMargin);
                if (min > max) min = max = hx;
                rf.min.Value = min; rf.max.Value = max;
            }
            if (sp != null && GruzZoteFixes.IsConst(sp.y))
            {
                float y = sp.y.Value;
                var hu = Physics2D.Raycast(new Vector2(hx, ctx.HeroPos.y), Vector2.up, Mathf.Max(1f, y - ctx.HeroPos.y + CeilingMargin), mask);
                if (hu.collider != null) y = Mathf.Min(y, hu.point.y - CeilingMargin);
                y = Mathf.Max(y, ground + MinDropAboveFloor);
                sp.y.Value = y;
            }
            Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': queda do Spawn Antic X {(rf != null ? $"{rf.min.Value:0.##}..{rf.max.Value:0.##}" : "?")} " +
                               $"Y {(sp != null && GruzZoteFixes.IsConst(sp.y) ? sp.y.Value.ToString("0.##") : "?")} (chao {ground:0.##})");
        }
    }

    /// <summary>Acao posta no fim do 'Death' dos Zote Crew: a copia nao volta pro 'Dormant' (nao ha 'Battle Control' pra recicla-la) -
    /// marca isDead (HealthManager.Hit ignora golpes) e destroi a copia (fim do frame, antes de desenhar). A mascara 'Zote Crew Death'
    /// e do pool global (nao e filha) e continua.</summary>
    internal sealed class RemoveSpawnedCopyAction : FsmStateAction
    {
        public GameObject Copy;
        public HealthManager Hm;
        public string Label;

        public override void OnEnter()
        {
            try
            {
                if (Hm != null) Hm.isDead = true;
                if (Copy != null)
                {
                    Modding.Logger.Log($"[CreatureSpawner] '{Label}': morreu ('Death') -> copia removida (sem reciclar pro 'Dormant')");
                    UnityEngine.Object.Destroy(Copy);
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] RemoveSpawnedCopyAction: {ex}");
            }
            Finish();
        }
    }

    // ==================================================================== Gruz Mother
    // FSM 'Big Fly Control' (gruz_c04.txt; bundle identico, BundleProbe 'fsm'). Com o ramo Godhome forcado (ForceBossSceneBranch):
    // Init -> 'GG Boss Wake' (SetInvincible false + WaitForFinishedEnteringScene) -> Wake (anim 'Wake', sobe 2.5) -> Fly -> GG Extra
    // Pause -> Buzz (WAKE pro 'bouncer_control': voa reto e so muda de direcao ao BATER em Terrain) -> Super Choose -> CHARGE ou SLAM.
    // Os dois ataques so terminam batendo em Terrain:
    //  - 'Charge': SetVelocityAsAngle(26) na direcao do heroi UMA vez; as UNICAS saidas sao LEFT/UP/RIGHT/DOWN do CheckCollisionSide
    //    (Enter). Sem Wait/timeout. Na arena original (Crossroads_04: chao y=14, teto y=25..27, paredes ~24 de largura) ela sempre
    //    bate em algo em < 1 s. Aqui a SpawnLeash (RadiusX 27, 9 pra cima) segura ela na borda da elipse ANTES do Terrain e zera a
    //    velocidade pra fora -> fica parada fora da tela, no 'Charge', PRA SEMPRE (sai voando e nunca mais luta).
    //  - 'Flying' (o SLAM): SetVelocityAsAngle(80/100 graus, 50) TODO frame ate bater no teto/chao (Slam Up/Down -> quica) ou estourar
    //    o Slam Time (2.5..3 s). Sala sem teto ao alcance: sobe ate o topo da coleira e fica la presa ate o timer, fora da tela.
    // Mesmo esquema do GodTamerRollFix: se ela parar de andar (presa na coleira) ou o Charge passar do tempo maximo, manda o evento
    // que a parede/teto mandaria no lado pra onde ela ia (Charge Recover L/R/U/D, Slam Up/Down) - a FSM nao e alterada.
    // E 'GG Boss Wake': WaitForFinishedEnteringScene (decompilado) so manda FINISHED na hora se GameManager.HasFinishedEnteringScene;
    // senao espera o PROXIMO OnFinishedEnteringScene (proxima troca de sala) - ela ficaria dormindo (anim 'Sleep', default do
    // animator) flutuando onde nasceu. CORRIGIDO depois: durante o jogo HasFinishedEnteringScene ja e true (GameManager decompilado:
    // so fica false do inicio de uma troca de sala ate o fim da entrada), entao essa espera passava direto e desliga-la nao mudou
    // nada. O que importa e que ela ADIAVA o 'Wake' pra depois dos Start da sala - ver GruzMotherWakeDefer (o adiamento posto no
    // lugar) e o caso 'Wake' do GruzMotherStallWatch (rede de seguranca + rastro de estados no ModLog).
    internal static class GruzMotherFix
    {
        public static void Attach(GameObject clone)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Big Fly Control", "GG Boss Wake", "Charge", "Flying");
                if (fsm == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Gruz Mother: FSM 'Big Fly Control' nao encontrada - sem ajustes");
                    return;
                }
                var ggWake = GruzZoteFixes.GetState(fsm, "GG Boss Wake");
                int off = 0;
                foreach (var a in ggWake.Actions)
                    if (a != null && a.GetType().Name == "WaitForFinishedEnteringScene") { a.Enabled = false; off++; }
                if (off == 0) Modding.Logger.LogWarn("[CreatureSpawner] Gruz Mother: WaitForFinishedEnteringScene do 'GG Boss Wake' nao encontrado");
                // No lugar da espera desligada: segura o 'GG Boss Wake' ate o tk2dSpriteAnimator ja ter feito o Start dele (que
                // toca o clipe padrao 'Sleep', LOOP) - so entao o 'Wake' toca o clipe 'Wake' (Once) que o Tk2dWatchAnimationEvents
                // espera terminar. Ver o comentario do GruzMotherWakeDefer.
                var old = ggWake.Actions;
                var acts = new FsmStateAction[old.Length + 1];
                Array.Copy(old, acts, old.Length);
                acts[old.Length] = new GruzMotherWakeDefer { Animator = clone.GetComponent<tk2dSpriteAnimator>() };
                ggWake.Actions = acts;
                clone.AddComponent<GruzMotherStallWatch>().Fsm = fsm;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GruzMotherFix: {ex}");
            }
        }
    }

    /// <summary>Acao posta no fim do 'GG Boss Wake' da Gruz Mother (substitui o WaitForFinishedEnteringScene desligado).
    /// O 'Wake' faz Tk2dPlayAnimation('Wake', Once) + SetVelocity2d(0, 2.5) e so sai pelo Tk2dWatchAnimationEvents (fim do
    /// clipe). O tk2dSpriteAnimator dela tem playAutomatically=1 e defaultClipId=1 = 'Sleep' (wrap 0 = LOOP; BundleProbe 'mb'
    /// + 'clips sharedassets32.assets 772'), e o Start dele faz Play(DefaultClip) incondicionalmente (decompilado). No Godhome o
    /// 'GG Boss Wake' espera o fim da entrada de cena, ou seja SEMPRE depois de todos os Start da sala. Na copia a espera passa
    /// direto (HasFinishedEnteringScene ja e true) e Init -> GG Boss Wake -> Wake roda inteiro DENTRO do PlayMakerFSM.Start (ou
    /// antes, se algum evento chegar na FSM antes do Start dela: Fsm.ProcessEvent chama Start() se !Started); a ordem dos Start
    /// de um objeto ativado em jogo (SetActive no OnGUI) nao e garantida - se o Start do animator vier depois, o 'Sleep' em loop
    /// substitui o 'Wake', o clipe nunca termina e ela fica no 'Wake' pra sempre: subindo a 2.5/s ate a borda da coleira, com a
    /// animacao de dormir, vulneravel (SetInvincible false ja rodou) e sem nunca chegar no Buzz/ataques.
    /// Aqui o estado so termina 2 frames depois de entrar E com o animator ja tendo tocado algum clipe (Start dele ja rodou).</summary>
    internal sealed class GruzMotherWakeDefer : FsmStateAction
    {
        private const float MaxWait = 0.5f;
        public tk2dSpriteAnimator Animator;
        private int frame;
        private float since;

        public override void OnEnter()
        {
            frame = Time.frameCount;
            since = Time.time;
        }

        public override void OnUpdate()
        {
            if (Time.frameCount < frame + 2) return;
            bool animatorStarted = Animator == null || !Animator.playAutomatically || Animator.CurrentClip != null;
            if (animatorStarted || Time.time - since > MaxWait) Finish();
        }
    }

    internal sealed class GruzMotherStallWatch : MonoBehaviour
    {
        // Charge anda 26/s e o Flying 50/s: numa janela de 0.12 s isso e 3..6 de deslocamento. Presa na coleira: ~0.
        private const float Window = 0.12f, StallDist = 0.3f, MaxCharge = 1.5f;
        // 'Wake' = clipe de 4 quadros a 10 fps (0.4 s). Bem mais que isso = o clipe foi trocado (ex. 'Sleep' em loop) -> preso.
        private const float MaxWake = 1.5f, MaxDormant = 0.75f;
        private const int MaxLogs = 12, MaxTrace = 25;
        public PlayMakerFSM Fsm;
        private string lastState;
        private float enteredAt, sampleAt, spawnedAt = -1f;
        private Vector2 samplePos;
        private int logs, traced;

        private void Update()
        {
            if (Fsm == null) { Destroy(this); return; }
            string st = Fsm.ActiveStateName;
            Vector2 pos = transform.position;
            if (spawnedAt < 0f) spawnedAt = Time.time;
            if (st != lastState)
            {
                // Rastro dos primeiros estados (evidencia pro proximo teste: por onde ela passou depois de nascer).
                if (traced++ < MaxTrace && Time.time - spawnedAt < 20f)
                    Modding.Logger.Log($"[CreatureSpawner] Gruz Mother '{name}': {Time.time - spawnedAt:0.00}s '{lastState}' -> '{st}' em {pos}");
                lastState = st;
                enteredAt = sampleAt = Time.time;
                samplePos = pos;
                return;
            }
            if (st == "Wake" && Time.time - enteredAt > MaxWake)
            {
                var an = GetComponent<tk2dSpriteAnimator>();
                Fire("FINISHED", st, $"preso ha {Time.time - enteredAt:0.0} s (clipe '{(an != null && an.CurrentClip != null ? an.CurrentClip.name : "?")}' " +
                                     $"tocando={(an != null && an.Playing)}; o 'Wake' so sai no fim do clipe 'Wake')");
                return;
            }
            // Ramo da sala (nao o Godhome) por algum motivo: invencivel/dormindo esperando HERO ENTER / golpe. A copia deve lutar.
            if ((st == "Invincible" || st == "Sleep") && Time.time - enteredAt > MaxDormant)
            {
                if (logs++ < MaxLogs)
                    Modding.Logger.Log($"[CreatureSpawner] Gruz Mother '{name}': em '{st}' (ramo da sala, nao o Godhome) -> 'GG Boss Wake'");
                lastState = null;
                Fsm.SetState("GG Boss Wake");
                return;
            }
            bool charge = st == "Charge";
            if (!charge && st != "Flying") return;
            if (charge && Time.time - enteredAt > MaxCharge) { Fire(ChargeEvent(), st, $"investida passou de {MaxCharge} s"); return; }
            if (Time.time - sampleAt < Window) return;
            float moved = (pos - samplePos).magnitude;
            sampleAt = Time.time;
            samplePos = pos;
            if (moved < StallDist) Fire(charge ? ChargeEvent() : SlamEvent(), st, $"parada (moveu {moved:0.###})");
        }

        // Lado em que ela "bateria": eixo dominante da direcao da investida ('Charge Angle', GetAngleToTarget2D em graus).
        private string ChargeEvent()
        {
            var v = Fsm.FsmVariables.FindFsmFloat("Charge Angle");
            float ang = (v != null ? v.Value : 0f) * Mathf.Deg2Rad;
            float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
            if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx >= 0f ? "RIGHT" : "LEFT";
            return dy >= 0f ? "UP" : "DOWN";
        }

        private string SlamEvent()
        {
            var up = Fsm.FsmVariables.FindFsmBool("Slamming Up");
            return up == null || up.Value ? "UP" : "DOWN";
        }

        private void Fire(string evt, string state, string why)
        {
            if (logs++ < MaxLogs)
                Modding.Logger.Log($"[CreatureSpawner] Gruz Mother '{name}': '{state}' {why} em {transform.position} (presa na coleira, sem Terrain) -> {evt}");
            lastState = null;
            Fsm.SendEvent(evt);
        }
    }

    /// <summary>Moscas da Gruz Mother (ver GruzZoteFixes.GruzFlySpawnPath). Host proprio (o corpse pode ser destruido logo depois
    /// do 'Blow' - ramo Godhome forcado nele -> 'Destroy Self'): na morte pega a var 'Burster' da FSM 'corpse'; quando o burster
    /// chega no 'Spawn Flies 2' (o ponto em que o original move o 'Fly Spawn' da sala pra ele), instancia o 'Fly Spawn'
    /// pre-carregado ali e liga. Antes disso zera a var 'Fly Spawn' do burster (o Find por nome pegaria o 'Fly Spawn' de outra
    /// copia ou o da sala, se for o Crossroads_04). Burster no 'Inert' (luta dentro do Godhome) = sem moscas, igual ao original.</summary>
    internal sealed class GruzMotherFlies : MonoBehaviour
    {
        private const float GiveUpAfter = 45f; // corpse 3.5 s + burster ~9.5 s (cai, Gurg 1..3, Burst); folga pra queda longa
        private static readonly FieldInfo FCorpse =
            typeof(EnemyDeathEffects).GetField("corpse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private GameObject gruz;
        private EnemyDeathEffects edf;
        private bool died, bursterFound;
        private float diedAt;
        private FsmGameObject burstVar;
        private PlayMakerFSM burster;

        public static void Attach(GameObject gruz)
        {
            try
            {
                if (GruzZoteFixes.GruzFlySpawnPrefab == null) return; // CacheExtras ja avisou
                var hm = gruz.GetComponent<HealthManager>();
                var edf = gruz.GetComponent<EnemyDeathEffects>();
                if (hm == null || edf == null) return;
                var host = new GameObject("CreatureSpawner Gruz Mother Flies");
                var c = host.AddComponent<GruzMotherFlies>();
                c.gruz = gruz;
                c.edf = edf;
                hm.OnDeath += c.OnDied;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GruzMotherFlies.Attach: {ex}");
            }
        }

        // HealthManager.Die: EnemyDeathEffects.RecieveDeathEvent (emite o corpse) vem ANTES do OnDeath.
        private void OnDied()
        {
            if (died || this == null) return;
            died = true;
            diedAt = Time.time;
            HookCorpse();
        }

        private void HookCorpse()
        {
            try
            {
                var corpse = edf != null && FCorpse != null ? FCorpse.GetValue(edf) as GameObject : null;
                if (corpse == null) return;
                var b = GruzZoteFixes.FindFsm(corpse, "burster"); // (caso o corpse ja seja o burster)
                if (b != null) { burster = b; bursterFound = true; return; }
                var cf = GruzZoteFixes.FindFsm(corpse, "corpse");
                if (cf != null) burstVar = cf.FsmVariables.FindFsmGameObject("Burster");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GruzMotherFlies.HookCorpse: {ex}");
            }
        }

        private void Update()
        {
            try
            {
                if (!died)
                {
                    if (gruz == null) Destroy(gameObject); // removida sem morrer (troca de cena etc.)
                    return;
                }
                if (Time.time - diedAt > GiveUpAfter)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Gruz Mother: o burster nao chegou no 'Spawn Flies 2' a tempo - sem moscas");
                    Destroy(gameObject);
                    return;
                }
                if (!bursterFound)
                {
                    if (burstVar == null) { HookCorpse(); return; }
                    var bgo = burstVar.Value;
                    if (bgo == null) return;
                    burster = GruzZoteFixes.FindFsm(bgo, "burster");
                    bursterFound = true;
                    if (burster == null)
                    {
                        Modding.Logger.LogWarn($"[CreatureSpawner] Gruz Mother: '{bgo.name}' sem FSM 'burster' - sem moscas");
                        Destroy(gameObject);
                    }
                    return;
                }
                if (burster == null) { Destroy(gameObject); return; } // burster sumiu
                string st = burster.ActiveStateName;
                if (st == "Inert") { Destroy(gameObject); return; }
                if (st == "Spawn Flies 2")
                {
                    Release(burster.transform.position);
                    Destroy(gameObject);
                    return;
                }
                if (!string.IsNullOrEmpty(st))
                {
                    var v = burster.FsmVariables.FindFsmGameObject("Fly Spawn");
                    if (v != null && v.Value != null) v.Value = null;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GruzMotherFlies: {ex}");
                Destroy(gameObject);
            }
        }

        private static void Release(Vector3 at)
        {
            var prefab = GruzZoteFixes.GruzFlySpawnPrefab;
            if (prefab == null) return;
            var fs = Instantiate(prefab, new Vector3(at.x, at.y, prefab.transform.position.z), Quaternion.identity);
            fs.name = "Fly Spawn (Gruz Mother spawnada)";
            fs.SetActive(true);
            Modding.Logger.Log($"[CreatureSpawner] Gruz Mother: burster estourou em {at} -> {fs.transform.childCount} moscas soltas");
        }
    }
}
