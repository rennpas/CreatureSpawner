using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Ajustes por-spawn de comportamento/IA (ver ArenaProfiles e FsmArenaShift). Uso, no SpawnNearPlayer:
    //   var ctx = SpawnFixups.Prepare(name, prefab, heroPos, out pos);   // antes do Instantiate
    //   clone = Instantiate(prefab, pos, ...); SpawnFixups.BeforeActivate(clone, ctx);
    //   clone.SetActive(true);                 SpawnFixups.AfterActivate(clone, ctx);
    internal static class SpawnFixups
    {
        public static readonly Vector3 DefaultOffset = new Vector3(2f, 2f, 0f);

        // Camada "Terrain" (8): a mesma mascara que HeroController.FindGroundPoint/CheckTouchingGround usam
        // (Physics2D.Raycast(..., Vector2.down, dist, 256)).
        public const int TerrainMask = 1 << 8;
        public const float GroundProbeDistance = 60f;
        // Pivo do Knight em pe acima do chao: BoxCollider2D do prefab 'Knight' (resources.assets) tem
        // offset.y=-0.75, size.y=1.28125 -> 0.75 + 0.640625 = 1.39 (FindGroundPoint soma extents.y - offset.y).
        // So usado como fallback se nao der pra ler o collider do heroi em tempo de execucao.
        public const float HeroPivotAboveFeet = 1.39f;
        // Ancora da coleira = 2 acima do pivo do heroi em pe (o mesmo que o antigo heroi+(0,2) quando ele
        // estava parado no chao), mas calculado a partir do chao real.
        public const float LeashAboveHeroPivot = 2f;

        public static SpawnContext Prepare(string name, GameObject prefab, Vector3 heroPos, out Vector3 spawnPos)
        {
            var ctx = new SpawnContext { Name = name, HeroPos = heroPos, Prefab = prefab };
            spawnPos = heroPos + DefaultOffset;
            if (ArenaProfiles.ByName.TryGetValue(name, out var p))
            {
                ctx.Profile = p;
                // Ref.y = chao da arena original; o deslocamento vertical e chao real - chao original. Assim as
                // variaveis de "aterrissou quando Self Y < X" / SetPosition y=X ficam exatamente na altura do chao
                // da sala atual, independente do heroi estar pulando, agachado no ar ou do tamanho do pivo dele.
                float pivot;
                ctx.GroundY = FindGroundY(heroPos, out ctx.GroundFound, out pivot);
                ctx.Delta = new Vector2(heroPos.x - p.Ref.x, ctx.GroundY - p.Ref.y);
                ctx.LeashAnchor = new Vector3(heroPos.x, ctx.GroundY + pivot + LeashAboveHeroPivot, heroPos.z);
                if (p.SpawnAtArenaPosition)
                {
                    // O prefab em cache guarda a posicao de MUNDO original (Instantiate sem pai preserva).
                    var orig = prefab.transform.position;
                    spawnPos = new Vector3(orig.x + ctx.Delta.x, orig.y + ctx.Delta.y, orig.z);
                }
                Modding.Logger.Log($"[CreatureSpawner] '{name}': chao={(ctx.GroundFound ? "raycast" : "pes do heroi (raycast falhou)")} " +
                                   $"y={ctx.GroundY:0.###} heroi={heroPos} delta={ctx.Delta} spawn={spawnPos}");
            }
            return ctx;
        }

        /// <summary>Altura do chao real embaixo do heroi (topo do collider Terrain), via raycast pra baixo a partir
        /// do pivo do heroi. pivotAboveFeet = altura do pivo do heroi acima dos pes (medida no collider dele).
        /// Se o raycast nao achar nada (heroi em cima de um abismo), usa os pes do heroi.</summary>
        public static float FindGroundY(Vector3 heroPos, out bool found, out float pivotAboveFeet)
        {
            float feet = heroPos.y - HeroPivotAboveFeet;
            pivotAboveFeet = HeroPivotAboveFeet;
            var hc = HeroController.instance;
            var col = hc != null ? hc.GetComponent<Collider2D>() : null;
            if (col != null)
            {
                feet = col.bounds.min.y;
                float measured = hc.transform.position.y - feet;
                if (measured > 0.2f && measured < 4f) pivotAboveFeet = measured;
            }
            var hit = Physics2D.Raycast(new Vector2(heroPos.x, heroPos.y), Vector2.down, GroundProbeDistance, TerrainMask);
            if (hit.collider != null)
            {
                found = true;
                return hit.point.y;
            }
            found = false;
            return feet;
        }

        public static void ConfigureLeash(SpawnLeash leash, SpawnContext ctx)
        {
            if (leash == null || ctx.Profile == null) return;
            var p = ctx.Profile;
            leash.SetAnchor(ctx.LeashAnchor);
            if (p.LeashX.HasValue) leash.RadiusX = p.LeashX.Value;
            if (p.LeashUp.HasValue) leash.RadiusYUp = p.LeashUp.Value;
            if (p.LeashDown.HasValue) leash.RadiusYDown = p.LeashDown.Value;
        }

        // ConstrainPosition.Update (decompilado) clampa transform.position em xMin..xMax / yMin..yMax ABSOLUTOS
        // da arena original, todo frame. Achados no bundle de preload real (scratchpad\PhysProbe\res\
        // constrain_all.txt, 'repack full'): Hornet 1 X 15.07..37.96, HK Prime X 30..61, Oro/Mato Y>=5.52,
        // Uumuu X 32.7..73.16 Y 97.94..134.93, Zote Crew Tall X 87.6..118.56. Fora da arena isso arrasta o
        // chefe pra longe todo Update e a SpawnLeash (LateUpdate) puxa de volta -> ele fica parado numa borda
        // da elipse da coleira, "flutuando como se tivesse chao" (o Y converge pro Y do ancora), ou preso num
        // chao invisivel em y=5.52 (Oro/Mato). Com perfil: desloca pelo mesmo delta da arena. Sem perfil: os
        // limites nao significam nada aqui -> desliga (a coleira cuida de manter perto).
        public static int FixConstrainPosition(GameObject root, SpawnContext ctx)
        {
            int n = 0;
            foreach (var cp in root.GetComponentsInChildren<ConstrainPosition>(true))
            {
                if (ctx.Profile != null)
                {
                    cp.xMin += ctx.Delta.x; cp.xMax += ctx.Delta.x;
                    cp.yMin += ctx.Delta.y; cp.yMax += ctx.Delta.y;
                }
                else cp.enabled = false;
                n++;
            }
            return n;
        }

        public static void BeforeActivate(GameObject clone, SpawnContext ctx)
        {
            // "Nailmasters Oro & Mato" (grupo 'Brothers'): tira a coleira da raiz (antes do ConfigureLeash abaixo), poe uma por irmao,
            // remove os filhos da sala e esconde o Mato ate o ENTER - ver NailmastersPair.cs.
            NailmastersPair.BeforeActivate(clone, ctx);
            FixConstrainPosition(clone, ctx);
            ConfigureLeash(clone.GetComponent<SpawnLeash>(), ctx);
            var dw = clone.AddComponent<SpawnDeathWatch>();
            dw.Ctx = ctx;
            if (ctx.Profile != null)
            {
                foreach (var w in ctx.Profile.WakeEvents)
                    FsmEventWhenInState.Add(clone, w.Fsm, w.State, w.Event);
                foreach (var e in ctx.Profile.PhaseEvents)
                    FsmEventWhenInState.Add(clone, e.Fsm, e.State, e.Event, e.Delay, timeout: 0f);
                foreach (var f in ctx.Profile.FinishStates)
                    SpawnedBossFinish.Add(clone, f.Fsm, f.State, f.Delay, ctx.Profile);
            }
        }

        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            ctx.PatchHierarchy(clone);
            if (ctx.Profile != null && ctx.Profile.PooledAttacks.Length > 0) PooledAttackShift.Attach(clone, ctx);
            if (ctx.Profile != null && ctx.Profile.PooledBounds.Length > 0) PooledBoundsShift.Attach(clone, ctx);
            if (ctx.Name == "Mantis Lords") MantisLordsEncounter.Begin(clone, ctx, EncounterGroups.MantisSubPrefab);
            if (ctx.Name == "Sisters of Battle") MantisLordsEncounter.Begin(clone, ctx, EncounterGroups.SistersSubPrefab);
            if (ctx.Name == "Elder Hu") ElderHuRings.Attach(clone, ctx);
            if (ctx.Name == "Watcher Knight") WatcherKnightsEncounter.Begin(clone, ctx);
            if (ctx.Name == "Flukemarm") FlukeMotherCage.Attach(clone, ctx);
            if (ctx.Name == "Absolute Radiance") AbsoluteRadianceAbyss.Attach(clone, ctx);
            // Laser que varre a arena (Beam Sweeper, irmao delas na sala) - ver RadianceBeamSweeper. Independente do vazio/cutscene acima.
            if (ctx.Name == "Absolute Radiance") RadianceBeamSweeper.Attach(clone, ctx, EncounterGroups.AbsRadianceBeamSweeperPrefab);
            if (ctx.Name == "Radiance") RadianceBeamSweeper.Attach(clone, ctx, EncounterGroups.PlainRadianceBeamSweeperPrefab);
            if (clone.name.StartsWith("Lobster")) GodTamerRollFix.Attach(clone); // God Tamer (a fera) - ver GodTamerRollFix
            if (ctx.Name == "Crystal Guardian")
            {
                CrystalGuardianBeamFix.Apply(clone); // antes do Start das FSMs (ver CrystalGuardianBeamFix)
                CrystalGuardianTurrets.Attach(clone, ctx);
            }
            // Variantes que antes ficavam escondidas no painel - ver VariantEncounters.cs.
            if (ctx.Name == "Enraged Guardian")
            {
                CrystalGuardianBeamFix.Apply(clone); // mesmos filhos Beam/Beam Ball/Beam Impact (destroy_if_gameobject_null)
                EnragedGuardianTurrets.Attach(clone, ctx);
            }
            if (ctx.Name == "Uumuu (Godhome)") GodhomeUumuuOomas.Attach(clone, ctx);
            // Multizap (bolas fixas da sala) dos dois Uumuus e farpas da Hornet Sentinel - ver VariantEncounters.cs.
            if (ctx.Name == "Uumuu" || ctx.Name == "Uumuu (Godhome)") UumuuMultizaps.Attach(clone, ctx);
            if (ctx.Name == "Hornet Sentinel") HornetSentinelBarbs.Attach(clone, ctx);
            // Gruz Mother (ataques presos na coleira, espera de entrada de cena, moscas do burster) e Zote Crew Fat/Normal/Tall
            // (morte especial reciclada, queda dentro da sala) - ver GruzZoteFixes.cs.
            GruzZoteFixes.AfterActivate(clone, ctx);
            // Real Bat (morcego-esponja do Grimm: acorda com BOSS AWAKE/BATS OUT, HP/morte proprios) - ver RealBatFix.cs.
            RealBatFix.AfterActivate(clone, ctx);
            // Soul Master / Soul Tyrant: fase 1 -> (morte, corpse) -> fase 2 escondida, como uma luta so - ver MageLordPhases.cs.
            MageLordPhases.AfterActivate(clone, ctx);
            // Nightmare King Grimm: vigia da abertura (Init/Dormant/Tele Out parados = "travado no inicio ate a fase 2") - ver NightmareGrimmFix.cs.
            NightmareGrimmOpeningWatch.AfterActivate(clone, ctx);
            // "Mato (Solo)": titulo do Roar + vigia do 'Cyclone Up' sob teto baixo - ver NailmasterMatoFix.cs.
            NailmasterMatoFix.AfterActivate(clone, ctx);
            // "Nailmasters Oro & Mato" (luta em dupla, grupo 'Brothers'): confere/religa $Brother/$Controller, vigia do ciclone do Mato
            // e o "aparecer" do Mato no ENTER - ver NailmastersPair.cs.
            NailmastersPair.AfterActivate(clone, ctx);
            // Hive Knight: bolas de espinhos, abelhas caindo e som do enxame (objetos irmaos na sala) - ver HiveKnightAttacks.cs.
            if (ctx.Name == "Hive Knight") HiveKnightAttacks.Attach(clone, ctx);
            // The Collector: WAKE da sala, entrada pelo teto e jarros com as alturas da sala atual - ver CollectorFix.cs.
            if (ctx.Name == CollectorFix.Name) CollectorFix.Attach(clone, ctx);
            // Nosk: arena deslocada em X, corrida que termina em parede/parada, cuspe do teto pela sala atual - ver NoskFix.cs.
            if (ctx.Name == NoskFix.Name) NoskFix.Attach(clone, ctx);
            // Dung Defender: alturas/bordas do tunel pela sala atual (ficava enterrado e invencivel) - ver DungDefenderFix.cs.
            if (ctx.Name == DungDefenderFix.Name) DungDefenderFix.Attach(clone, ctx);
            // White Defender: so o monte de terra (visual) acima do chao - ver WhiteDefenderMound.cs.
            if (ctx.Name == WhiteDefenderMound.Name) WhiteDefenderMound.Attach(clone, ctx);
            // False Knight: pulos pela sala atual e barris do teto (summoner da sala) - ver FalseKnightFix.cs.
            if (ctx.Name == FalseKnightFix.Name) FalseKnightFix.Attach(clone, ctx);
            // Massive Moss Charger: corrida/pulo que so terminavam numa parede da arena original - ver MossChargerFix.cs.
            if (ctx.Name == MossChargerFix.Name) MossChargerFix.Attach(clone, ctx);
            // Great Nailsage Sly: furia/dash/stomp/ciclone em coordenadas absolutas da arena GG - ver SlyFix.cs.
            if (ctx.Name == SlyFix.Name) SlyFix.Attach(clone, ctx);
            // Grimm: fileira de espinhos do chao (objeto da sala) - ver GrimmSpikes.cs.
            if (ctx.Name == GrimmSpikes.Name) GrimmSpikes.Attach(clone, ctx);
            // Grimm: morcego de verdade da fase de morcegos (objeto da sala) - ver GrimmBat.cs.
            if (ctx.Name == GrimmBat.Name) GrimmBat.Attach(clone, ctx);
            // Traitor Lord: pulo (DSlash) e entrada com Y/X absolutos da arena - ver TraitorLordFix.cs.
            if (ctx.Name == TraitorLordFix.Name) TraitorLordFix.Attach(clone, ctx);
            // Broken Vessel: intro do corpo + bichinhos (Scuttler Spawner da sala) - ver BrokenVesselFix.cs.
            if (ctx.Name == BrokenVesselFix.Name) BrokenVesselFix.Attach(clone, ctx);
        }
    }

    // God Tamer ('Lobster' = a fera do "God Tamer" do painel; FSM 'Control' - scratchpad\FsmDumpTool\out\godtamer\gg_god_tamer_all.txt).
    // "Travou no ataque bola infinitamente" = o ataque de ROLAR em bola ('Attack Choice' ROLL CHARGE): RC Antic (anim 'Lobster Roll
    // Up', liga 'Hit Ball', SetInvincible TRUE) -> RC Charge (SetVelocity2d x = Charge Speed -25 * escala, UMA vez) -> 'RC Charging',
    // cuja UNICA saida e WALL, mandado so por CheckCollisionSide/CheckCollisionSideEnter (decompilado: raycasts de 0.08 a partir das
    // laterais do collider, so camada 8 = Terrain). Nenhum Wait/timeout. Na arena original (Colosseum, parede dos dois lados a poucos
    // metros) ela sempre bate numa parede. Aqui: a SpawnLeash (RadiusX 27 da ancora) segura ela na borda e zera a velocidade pra fora,
    // e o atrito do chao freia a bola (Rigidbody2D sem PhysicsMaterial, gravidade 2 - PhysProbe GG_God_Tamer). Sem parede Terrain ao
    // alcance (sala larga, borda da coleira, obstaculo que nao e Terrain) o WALL nunca vem -> ela fica PRA SEMPRE enrolada em bola,
    // parada e INVENCIVEL (so 'RC Land' faz SetInvincible false). O cuspe NAO tem relacao: 'Fire Globs' = 3 FlingObjectsFromGlobalPoolVel
    // + FINISHED, e a FSM do 'Vomit Glob' (sharedassets34_all.txt) nao tem coordenada absoluta nem manda evento de volta.
    // Correcao: no 'RC Charging', se ela parar de avancar (|dX| < StallDist numa janela de StallWindow s) ou rolar mais de MaxRoll s,
    // manda WALL = o que a parede faria: RC Launch (salto na direcao do heroi) -> RC Air -> RC Land -> Idle. Mesmo esquema no 'RC Air'
    // (unica saida LAND, mesmo CheckCollisionSide no chao): parada no ar por AirStallWindows janelas seguidas (coleira segurando ela
    // sobre um buraco) -> LAND. A FSM nao e alterada; so eventos que ela ja espera nesses estados.
    internal sealed class GodTamerRollFix : MonoBehaviour
    {
        private const float StallWindow = 0.25f, StallDist = 0.4f, MaxRoll = 3f;
        private const float AirWindow = 0.75f, AirStallDist = 0.05f;
        private const int AirStallWindows = 2, MaxLogs = 10;
        private PlayMakerFSM fsm;
        private string lastState;
        private float enteredAt, sampleAt;
        private Vector2 samplePos;
        private int stalledWindows, logs;

        public static void Attach(GameObject clone)
        {
            foreach (var f in clone.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Control" && HasState(f, "RC Charging") && HasState(f, "RC Air"))
                {
                    clone.AddComponent<GodTamerRollFix>().fsm = f;
                    return;
                }
        }

        private static bool HasState(PlayMakerFSM pm, string name)
        {
            foreach (var s in pm.FsmStates) if (s.Name == name) return true;
            return false;
        }

        private void Update()
        {
            if (fsm == null) { Destroy(this); return; }
            string st = fsm.ActiveStateName;
            Vector2 pos = transform.position;
            if (st != lastState)
            {
                lastState = st;
                enteredAt = sampleAt = Time.time;
                samplePos = pos;
                stalledWindows = 0;
                return;
            }
            bool charging = st == "RC Charging";
            if (!charging && st != "RC Air") return;
            if (charging && Time.time - enteredAt > MaxRoll) { Fire("WALL", st, $"rolando ha mais de {MaxRoll} s"); return; }
            if (Time.time - sampleAt < (charging ? StallWindow : AirWindow)) return;
            // Rolando: so o avanco horizontal liquido (na borda da coleira o corpo treme mas nao sai do lugar). No ar: deslocamento
            // liquido, e por 2 janelas seguidas (no apice do salto uma janela simetrica pode dar ~0).
            float moved = charging ? Mathf.Abs(pos.x - samplePos.x) : (pos - samplePos).magnitude;
            stalledWindows = moved < (charging ? StallDist : AirStallDist) ? stalledWindows + 1 : 0;
            sampleAt = Time.time;
            samplePos = pos;
            if (stalledWindows >= (charging ? 1 : AirStallWindows))
                Fire(charging ? "WALL" : "LAND", st, $"parada (moveu {moved:0.###} na ultima janela)");
        }

        private void Fire(string evt, string state, string why)
        {
            if (logs++ < MaxLogs)
                Modding.Logger.Log($"[CreatureSpawner] God Tamer '{name}': '{state}' {why} em {transform.position} -> {evt}");
            lastState = null; // reinicia a medicao no proximo estado
            fsm.SendEvent(evt);
        }
    }

    /// <summary>Na morte de qualquer HealthManager da copia, pega o corpse emitido pelo EnemyDeathEffects
    /// e aplica nele os mesmos ajustes de FSM (ramo Godhome etc.) + a rede de seguranca de queda.</summary>
    internal sealed class SpawnDeathWatch : MonoBehaviour
    {
        public SpawnContext Ctx;
        private static readonly FieldInfo FCorpse =
            typeof(EnemyDeathEffects).GetField("corpse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private readonly List<HealthManager> hooked = new List<HealthManager>();
        private bool rewardStarted;

        private void Start()
        {
            foreach (var hm in GetComponentsInChildren<HealthManager>(true))
            {
                var h = hm;
                h.OnDeath += () => OnDied(h);
                hooked.Add(h);
            }
        }

        private void OnDied(HealthManager hm)
        {
            if (hm == null || Ctx == null) return;
            try
            {
                foreach (var edf in hm.GetComponents<EnemyDeathEffects>())
                {
                    // HealthManager.Die chama EnemyDeathEffects.RecieveDeathEvent (que emite o corpse:
                    // SetParent(null) + SetActive(true) -> Awake ja rodou) ANTES do OnDeath, e o Start da
                    // FSM do corpse so vem depois -> da tempo de ajustar antes dela comecar.
                    var corpse = FCorpse?.GetValue(edf) as GameObject;
                    if (corpse == null) continue;
                    Ctx.PatchHierarchy(corpse);
                    if (corpse.GetComponent<CorpseFallGuard>() == null)
                        corpse.AddComponent<CorpseFallGuard>().Anchor = Ctx.HeroPos;
                    if (!rewardStarted && Ctx.Name != null &&
                        EncounterGroups.GhostNpcPrefabs.TryGetValue(Ctx.Name, out var npcPrefab) && npcPrefab != null)
                    {
                        rewardStarted = true;
                        GhostWarriorReward.Begin(Ctx.Name, npcPrefab, corpse, hm.transform.position, Ctx);
                    }
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] SpawnDeathWatch: {ex}");
            }
        }
    }

    /// <summary>Ataques de pool global que se posicionam em coordenadas absolutas da arena original (ArenaProfile.PooledAttacks;
    /// ex. os pentes de espadas da Absolute Radiance). Fica na copia e, todo LateUpdate, olha a variavel onde as FSMs dela
    /// guardam o objeto spawnado: se a FSM esta num estado que spawna e o objeto acabou de nascer (FSM dele ainda nao comecou
    /// ou esta no estado inicial - o 'Init' do pente tem NextFrameEvent, entao isso so e verdade no frame do spawn), pendura
    /// um PooledAttackPlacement nele. So objetos spawnados por ESTA copia sao tocados, e so durante esse uso.</summary>
    internal sealed class PooledAttackShift : MonoBehaviour
    {
        // Diagnostico do "pente perto demais da Radiancia": cada colocacao loga 2 linhas (ver PooledAttackPlacement).
        // Rage Comb repete a cada 0.85 s, entao o limite fica por copia.
        private const int MaxLogs = 30;
        private sealed class Watch
        {
            public PlayMakerFSM Owner;
            public FsmGameObject Var;
            public HashSet<string> States;
            public PooledAttack Attack;
        }

        private readonly List<Watch> watches = new List<Watch>();
        public SpawnContext Ctx;
        public int Placed;

        public static void Attach(GameObject boss, SpawnContext ctx)
        {
            try
            {
                var c = boss.AddComponent<PooledAttackShift>();
                c.Ctx = ctx;
                var desc = new List<string>();
                foreach (var pa in ctx.Profile.PooledAttacks)
                {
                    var byFsm = new Dictionary<string, HashSet<string>>();
                    foreach (var (fsmName, state) in pa.Spawners)
                    {
                        if (!byFsm.TryGetValue(fsmName, out var set)) byFsm[fsmName] = set = new HashSet<string>();
                        set.Add(state);
                    }
                    foreach (var kv in byFsm)
                    {
                        PlayMakerFSM owner = null;
                        foreach (var f in boss.GetComponents<PlayMakerFSM>()) if (f.FsmName == kv.Key) { owner = f; break; }
                        var v = owner != null ? owner.FsmVariables.FindFsmGameObject(pa.StoreVar) : null;
                        if (v == null)
                        {
                            Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': FSM '{kv.Key}'/var '{pa.StoreVar}' nao encontrada - ataque de pool nao sera reposicionado");
                            continue;
                        }
                        c.watches.Add(new Watch { Owner = owner, Var = v, States = kv.Value, Attack = pa });
                        desc.Add($"{kv.Key}.{pa.StoreVar} ({kv.Value.Count} estados)");
                    }
                }
                Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': ataques de pool reposicionados pelo delta da arena: {string.Join(", ", desc.ToArray())}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] PooledAttackShift.Attach: {ex}");
            }
        }

        private void LateUpdate()
        {
            foreach (var w in watches)
            {
                if (w.Owner == null || !w.States.Contains(w.Owner.ActiveStateName)) continue;
                var go = w.Var.Value;
                if (go == null || !go.activeInHierarchy || go.GetComponent<PooledAttackPlacement>() != null) continue;
                PlayMakerFSM target = null;
                foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == w.Attack.Fsm) { target = f; break; }
                // (o Nail Fan guarda o 'Radiant Nail' na mesma var 'Attack Obj': FSM 'Control' sem 'Type' -> ignorado)
                if (target == null || target.Fsm == null || target.FsmVariables.FindFsmInt(w.Attack.TypeVar) == null) continue;
                if (target.Fsm.Started && target.ActiveStateName != target.Fsm.StartState) continue; // nao e deste frame
                var pl = go.AddComponent<PooledAttackPlacement>();
                pl.Begin(this, target, w.Attack, w.Owner.FsmName + "/" + w.Owner.ActiveStateName);
            }
        }

        public bool ShouldLog() => Placed++ < MaxLogs;
    }

    /// <summary>Num objeto de pool spawnado pela copia (ver PooledAttackShift): quando a FSM dele chega no ReadyState (ja fez o
    /// SetPosition absoluto e ainda nao se moveu), translada a raiz pelo delta da arena (os filhos - ex. as espadas de 'Nails' -
    /// vao junto) e se remove. Roda em LateUpdate: o objeto nunca e desenhado na posicao absoluta. Se o objeto for reciclado
    /// antes (OnDisable) tambem se remove - a proxima vez que o pool entregar essa instancia ela volta intacta.</summary>
    internal sealed class PooledAttackPlacement : MonoBehaviour
    {
        private const float Timeout = 5f;
        private PooledAttackShift owner;
        private PlayMakerFSM fsm;
        private PooledAttack attack;
        private SpawnContext ctx;
        private string source;
        private float startedAt;

        public void Begin(PooledAttackShift o, PlayMakerFSM target, PooledAttack pa, string src)
        {
            owner = o; fsm = target; attack = pa; ctx = o.Ctx; source = src;
            startedAt = Time.time;
        }

        // Diagnostico: depois de deslocar, fica ate a FSM sair do ReadyState (fim do Antic, inicio do movimento) e loga
        // de novo, pra confirmar que nada moveu o pente/espadas de volta e onde ela estava no momento em que elas saem.
        private bool shifted, logThis;
        private Vector3 shiftDelta;
        private int shiftedType;

        private void LateUpdate()
        {
            if (fsm == null || ctx == null || ctx.Profile == null) { Destroy(this); return; }
            if (shifted)
            {
                if (fsm.ActiveStateName == attack.ReadyState && Time.time - startedAt <= Timeout) return;
                if (logThis)
                    Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': '{name}' tipo {shiftedType} SAINDO de '{attack.ReadyState}' -> '{fsm.ActiveStateName}': " +
                                       Describe(shiftDelta));
                Destroy(this);
                return;
            }
            if (fsm.ActiveStateName != attack.ReadyState)
            {
                if (Time.time - startedAt > Timeout)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': '{name}' (de {source}) nao chegou em '{attack.ReadyState}' em {Timeout} s (estado '{fsm.ActiveStateName}') - nao reposicionado");
                    Destroy(this);
                }
                return;
            }
            var tv = fsm.FsmVariables.FindFsmInt(attack.TypeVar);
            int type = tv != null ? tv.Value : 0;
            var p = ctx.Profile;
            float floor = attack.TypeFloorY.TryGetValue(type, out var fy) ? fy : p.Ref.y;
            var d = new Vector3(ctx.Delta.x, ctx.Delta.y + p.Ref.y - floor, 0f);
            var before = transform.position;
            transform.position = before + d;
            shifted = true; shiftDelta = d; shiftedType = type; startedAt = Time.time;
            logThis = owner == null || owner.ShouldLog();
            if (logThis)
                Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': '{name}' tipo {type} (de {source}) {before} -> {transform.position} " +
                                   $"(delta {d}, chao ref {floor:0.##}): " + Describe(d));
        }

        /// <summary>Tudo que precisa pra comparar com o jogo original: posicao do pente e das espadas, posicao REAL da copia,
        /// o "equivalente na arena original" dela (posicao - delta do pente; no original a Radiancia fica em x 49.82..71.78,
        /// y 27..29.17 na arena 1 / Rage 60.63,28.3 / Tele 1..10 na arena 2, e o pente Top em y 33.5), o destino que a FSM
        /// de teleporte pediu (diferenca = coleira puxando), a ancora da coleira e o enquadramento da camera (no original,
        /// CamLock A1 trava a camera em y=25: topo da tela ~33.3, ou seja o pente Top nasce na borda de cima).</summary>
        private string Describe(Vector3 d)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var comb = transform.position;
                sb.Append($"pente={V(comb)} rot={transform.eulerAngles.z:0}");
                var nails = transform.Find("Nails");
                if (nails != null && nails.childCount > 0)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                    foreach (Transform c in nails)
                    {
                        var q = c.position;
                        x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y);
                    }
                    sb.Append($" espadas={nails.childCount} x {x0:0.##}..{x1:0.##} y {y0:0.##}..{y1:0.##}");
                }
                else sb.Append(" espadas=0");
                if (owner != null)
                {
                    var boss = owner.transform.position;
                    sb.Append($" | copia={V(boss)} equivOriginal={V(boss - d)} pente-copia={V(comb - boss)}");
                    foreach (var f in owner.GetComponents<PlayMakerFSM>())
                        foreach (var vn in ctx.Profile.V3Vars)
                        {
                            var v = f.FsmVariables.FindFsmVector3(vn);
                            if (v != null) sb.Append($" {f.FsmName}.{vn}={V(v.Value)}");
                        }
                    var leash = owner.GetComponent<SpawnLeash>();
                    if (leash != null) sb.Append($" ancora={V(leash.Anchor)}");
                    foreach (var f in owner.GetComponents<PlayMakerFSM>())
                        if (f.FsmName == "Control") { sb.Append($" Control@'{f.ActiveStateName}'"); break; }
                }
                var hero = HeroController.instance;
                if (hero != null) sb.Append($" heroi={V(hero.transform.position)}");
                Camera cam = GameCameras.instance != null ? GameCameras.instance.mainCamera : null;
                if (cam == null) cam = Camera.main;
                if (cam != null)
                {
                    float depth = -cam.transform.position.z; // plano z=0
                    var top = cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, depth));
                    var bottom = cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, depth));
                    var left = cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, depth));
                    var right = cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth));
                    sb.Append($" | camera={V(cam.transform.position)} tela x {left.x:0.##}..{right.x:0.##} y {bottom.y:0.##}..{top.y:0.##}" +
                              $" (pente {comb.y - top.y:+0.##;-0.##} do topo)");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "diag falhou: " + ex.Message;
            }
        }

        private static string V(Vector3 v) => $"({v.x:0.##},{v.y:0.##})";

        private void OnDisable() => Destroy(this);
    }

    /// <summary>ArenaProfile.PooledBounds (ex. orbes da fase final da Absolute Radiance - ver o perfil): poe, no INICIO do estado
    /// Hook da FSM da copia, uma acao que desloca as variaveis de limite absoluto da FSM do objeto de pool guardado em StoreVar.
    /// Tem que ser uma acao DENTRO do estado (nao um LateUpdate como o PooledAttackShift): o teste de limites roda e recicla o
    /// objeto de forma sincrona no mesmo OnEnter que manda o evento. So a copia recebe a acao (a FSM da luta real nao e tocada).</summary>
    internal static class PooledBoundsShift
    {
        public static void Attach(GameObject boss, SpawnContext ctx)
        {
            var p = ctx.Profile;
            foreach (var pb in p.PooledBounds)
            {
                try
                {
                    PlayMakerFSM owner = null;
                    foreach (var f in boss.GetComponents<PlayMakerFSM>()) if (f.FsmName == pb.Hook.Fsm) { owner = f; break; }
                    var store = owner != null ? owner.FsmVariables.FindFsmGameObject(pb.StoreVar) : null;
                    var cond = owner != null && pb.OnlyIfBool != null ? owner.FsmVariables.FindFsmBool(pb.OnlyIfBool) : null;
                    FsmState state = null;
                    if (owner != null && owner.Fsm != null)
                        foreach (var st in owner.FsmStates) if (st.Name == pb.Hook.State) { state = st; break; }
                    if (store == null || state == null || (pb.OnlyIfBool != null && cond == null))
                    {
                        Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': PooledBounds '{pb.Hook.Fsm}'/'{pb.Hook.State}'/var '{pb.StoreVar}' nao encontrado - limites de '{pb.Fsm}' nao deslocados");
                        continue;
                    }
                    float floor = float.IsNaN(pb.FloorY) ? p.Ref.y : pb.FloorY;
                    var d = new Vector2(ctx.Delta.x, ctx.Delta.y + p.Ref.y - floor);
                    var old = state.Actions;
                    var actions = new FsmStateAction[old.Length + 1];
                    actions[0] = new PooledBoundsAction { Store = store, Condition = cond, Bounds = pb, Delta = d, BossName = ctx.Name };
                    Array.Copy(old, 0, actions, 1, old.Length);
                    state.Actions = actions;
                    Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': '{pb.Hook.Fsm}'/'{pb.Hook.State}' desloca os limites de '{pb.Fsm}' " +
                                       $"(${pb.StoreVar}) por {d} (chao ref {floor:0.##})");
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] PooledBoundsShift.Attach: {ex}");
                }
            }
        }
    }

    /// <summary>Acao inserida pela PooledBoundsShift: desloca os limites na instancia em Store (se houver) e termina na hora.</summary>
    internal sealed class PooledBoundsAction : FsmStateAction
    {
        private const int MaxLogs = 5;
        public FsmGameObject Store;
        public FsmBool Condition; // null = sempre
        public PooledAttackBounds Bounds;
        public Vector2 Delta;
        public string BossName;
        private int logs;

        public override void OnEnter()
        {
            try
            {
                var go = Store != null ? Store.Value : null;
                if (go != null && (Condition == null || Condition.Value))
                {
                    int n = PooledFsmVarsRestore.Apply(go, Bounds.Fsm, Bounds.XVars, Bounds.YVars, Delta);
                    if (n < 0 || logs++ < MaxLogs)
                        Modding.Logger.Log($"[CreatureSpawner] '{BossName}': '{go.name}' em {go.transform.position}: " +
                                           (n < 0 ? $"FSM '{Bounds.Fsm}' nao encontrada" : $"{n} limites de '{Bounds.Fsm}' deslocados por {Delta}"));
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] PooledBoundsAction: {ex}");
            }
            Finish();
        }
    }

    /// <summary>Fica (inerte) no objeto de pool: guarda os valores originais das variaveis deslocadas e os devolve quando o objeto
    /// e reciclado (ObjectPool.Recycle -> SetActive(false)) - o PlayMaker NAO reseta variaveis no OnDisable/OnEnable, entao sem isso
    /// a instancia voltaria pro pool com limites de outra sala. Nunca se destroi (Destroy e adiado pro fim do frame: um re-spawn no
    /// mesmo frame perderia os originais); um novo Apply sempre restaura antes, entao deslocamentos nunca se acumulam.</summary>
    internal sealed class PooledFsmVarsRestore : MonoBehaviour
    {
        private readonly List<KeyValuePair<FsmFloat, float>> saved = new List<KeyValuePair<FsmFloat, float>>();

        /// <summary>Numero de variaveis deslocadas; -1 se a FSM nao existe no objeto.</summary>
        public static int Apply(GameObject go, string fsmName, string[] xVars, string[] yVars, Vector2 d)
        {
            PlayMakerFSM fsm = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == fsmName) { fsm = f; break; }
            if (fsm == null) return -1;
            var r = go.GetComponent<PooledFsmVarsRestore>();
            if (r == null) r = go.AddComponent<PooledFsmVarsRestore>();
            r.Restore();
            int n = 0;
            foreach (var name in xVars) n += r.Shift(fsm.FsmVariables.FindFsmFloat(name), d.x);
            foreach (var name in yVars) n += r.Shift(fsm.FsmVariables.FindFsmFloat(name), d.y);
            return n;
        }

        private int Shift(FsmFloat v, float k)
        {
            if (v == null) return 0;
            saved.Add(new KeyValuePair<FsmFloat, float>(v, v.Value));
            v.Value += k;
            return 1;
        }

        public void Restore()
        {
            foreach (var s in saved) s.Key.Value = s.Value;
            saved.Clear();
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }

    /// <summary>Final de luta cuja cutscene ORIGINAL roda inteira na copia mas que so "termina" na sala/modo original
    /// (Radiancias: a cena trocaria / o Godhome encerraria - ver ArenaProfiles). Essas cutscenes congelam o HEROI
    /// (RelinquishControl, StopAnimationControl, gravidade off, renderer off) e nada as desfaz fora do contexto original.
    ///  - Caminho PRINCIPAL: a FSM fica Delay s no estado final (ArenaProfile.FinishStates) -> devolve o heroi
    ///    (HeroRestore) e remove a copia + os objetos que a cutscene desparentou (Knight Ball, cacos do Knight Split).
    ///  - Vigias (a partir do 1o ArenaProfile.HeroCutsceneStates visto = heroi congelado): estado de cutscene parado ha
    ///    mais de StallSeconds (o mais longo do original e 5 s), estado fora da lista (FSM saiu do caminho esperado) ha
    ///    mais de OffPathSeconds, ou CutsceneTimeout total estourado -> encerra NA MARRA (restaura heroi, para o tremor
    ///    de camera, remove a copia). Estados que esperam input (CutsceneInputs) recebem o evento sozinhos.
    ///  - Copia desligada/destruida no meio (outra coisa a removeu, troca de cena) -> HeroRestore.Deferred.
    /// Tempos em Time.time (jogo pausado nao conta).</summary>
    internal sealed class SpawnedBossFinish : MonoBehaviour
    {
        public const float StallSeconds = 10f;
        public const float OffPathSeconds = 3f;

        public string FsmName, StateName;
        public float Delay;
        public (string Fsm, string State)[] HeroCutsceneStates = new (string, string)[0];
        public (string Fsm, string State, string Event, float Delay)[] Inputs = new (string, string, string, float)[0];
        public float CutsceneTimeout;
        private PlayMakerFSM target;
        private float enteredAt = -1f;
        private bool heroTouched, finished;
        private float cutsceneStart, stateSince = -1f;
        private string lastState;
        private bool inputSent;
        private readonly List<GameObject> orphans = new List<GameObject>();

        public static SpawnedBossFinish Add(GameObject go, string fsmName, string state, float delay, ArenaProfile p)
        {
            var c = go.AddComponent<SpawnedBossFinish>();
            c.FsmName = fsmName; c.StateName = state; c.Delay = delay;
            if (p != null)
            {
                c.HeroCutsceneStates = p.HeroCutsceneStates;
                c.Inputs = p.CutsceneInputs;
                c.CutsceneTimeout = p.CutsceneTimeout;
            }
            return c;
        }

        private void Start()
        {
            // A cutscene desparenta coisas da copia que sobreviveriam ao Destroy dela: 'Knight Break'/'Knight Split' faz
            // SetParent(null) no Knight Ball, e o FSM 'Split' do Knight Split faz DetachChildren (cacos do elmo com
            // Rigidbody2D sem collider = caem pra sempre, Death Glow, Particle Wave). Guarda tudo agora, ainda filho.
            var ks = transform.Find("Death/Knight Split");
            if (ks != null)
                foreach (Transform child in ks) orphans.Add(child.gameObject);
        }

        private bool IsCutsceneState(string st)
        {
            foreach (var (fsmName, state) in HeroCutsceneStates)
                if (fsmName == FsmName && state == st) return true;
            return false;
        }

        private void Update()
        {
            if (finished) return;
            if (target == null)
                foreach (var f in GetComponents<PlayMakerFSM>()) if (f.FsmName == FsmName) { target = f; break; }
            if (target == null) return;
            string st = target.ActiveStateName;
            float now = Time.time;
            if (st != lastState) { lastState = st; stateSince = now; inputSent = false; }

            if (!heroTouched && IsCutsceneState(st))
            {
                heroTouched = true;
                cutsceneStart = now;
                Modding.Logger.Log($"[CreatureSpawner] '{name}': FSM '{FsmName}' entrou em '{st}' - cutscene final original rodando " +
                                   $"(heroi congelado ate '{StateName}'; vigia: {StallSeconds:0}s parado / {CutsceneTimeout:0}s total)");
            }

            if (st == StateName)
            {
                if (enteredAt < 0f) enteredAt = now;
                if (now - enteredAt >= Delay) Finish(false, null);
                return;
            }
            enteredAt = -1f;
            if (!heroTouched) return;

            // Vigias - so depois que a cutscene congelou o heroi.
            float inState = now - stateSince;
            if (CutsceneTimeout > 0f && now - cutsceneStart > CutsceneTimeout)
            {
                Finish(true, $"cutscene passou de {CutsceneTimeout:0}s (estado '{st}')");
                return;
            }
            bool isInput = false;
            foreach (var (fsmName, state, evt, delay) in Inputs)
            {
                if (fsmName != FsmName || state != st) continue;
                isInput = true;
                if (!inputSent && inState >= delay)
                {
                    inputSent = true; // um por entrada no estado (o estado volta a ser ativo a cada golpe)
                    target.SendEvent(evt);
                }
                if (inState > delay + StallSeconds) { Finish(true, $"estado de input '{st}' nao andou nem com {evt} automatico"); return; }
            }
            if (isInput) return;
            if (!IsCutsceneState(st))
            {
                if (inState > OffPathSeconds) Finish(true, $"FSM saiu do caminho da cutscene (estado '{st}')");
                return;
            }
            if (inState > StallSeconds) Finish(true, $"estado '{st}' parado ha {inState:0.#}s" + (target.enabled ? "" : " (FSM desligada)"));
        }

        private void Finish(bool forced, string why)
        {
            if (finished) return;
            finished = true;
            bool restore = heroTouched || HeroRestore.HeroLooksFrozen();
            if (forced)
                Modding.Logger.LogWarn($"[CreatureSpawner] '{name}': encerrando a cutscene na marra ({why}) -> restaurando o heroi e removendo a copia");
            else
                Modding.Logger.Log($"[CreatureSpawner] '{name}': FSM '{FsmName}' chegou no final '{StateName}' -> removendo a copia" +
                                   (restore ? " e restaurando o heroi" : ""));
            if (restore) HeroRestore.AfterBossCutscene();
            if (forced) HeroRestore.StopCutsceneRumble();
            foreach (var o in orphans) if (o != null && o.transform.root != transform) Destroy(o);
            Destroy(gameObject);
        }

        // Copia desligada/destruida por fora no meio da cutscene (heroi ainda congelado): o Update daqui nao roda mais.
        private void OnDisable()
        {
            if (finished || !heroTouched) return;
            finished = true;
            Modding.Logger.LogWarn($"[CreatureSpawner] '{name}': copia desligada/removida no meio da cutscene final (estado '{lastState}') -> restauracao adiada do heroi");
            foreach (var o in orphans) if (o != null && o.transform.root != transform) Destroy(o);
            HeroRestore.Deferred(rumble: true);
        }
    }

    /// <summary>Desfaz o que cutscenes de fim de chefe fazem no heroi quando a cena NAO troca depois (fora do
    /// Godhome). Espelha o 'Knight Break Antic'/'Knight Pull' das Radiancias: SendMessage RelinquishControl /
    /// StopAnimationControl / AffectedByGravity(false), SetVelocity2d 0, SetMeshRenderer(Hero)=false (+ HUD Canvas OUT,
    /// desligado nas copias). Os inversos (HeroController decompilado): RegainControl (AcceptInput + gravidade de volta +
    /// controlReqlinquished=false + SetStartingMotionState), StartAnimationControl, AffectedByGravity(true) (no-op se o
    /// RegainControl ja devolveu), MeshRenderer.enabled=true (= HeroController.EnableRenderer) e IN pra FSM 'Slide Out'
    /// do Hud Canvas (estado 'Out' -> 'Come In'; nos outros estados IN e ignorado).
    /// O FSM CANCEL mandado ao heroi so leva Spell Control/Nail Arts/Dream Nail/Superdash/Map Control pros estados
    /// de cancelamento deles (que voltam a esperar input) - nada a desfazer. Nenhum estado das cutscenes mexe em
    /// PlayerData alem dos SetPlayerData* do 'Final Impact' (desligados em DisableActions).</summary>
    internal static class HeroRestore
    {
        public static void AfterBossCutscene()
        {
            try
            {
                var hc = HeroController.instance;
                if (hc != null)
                {
                    hc.RegainControl();
                    hc.StartAnimationControl();
                    hc.AffectedByGravity(true);
                    var mr = hc.GetComponent<MeshRenderer>();
                    if (mr != null) mr.enabled = true;
                }
                var gc = GameCameras.instance;
                if (gc != null && gc.hudCanvas != null)
                    foreach (var f in gc.hudCanvas.GetComponents<PlayMakerFSM>())
                        if (f.FsmName == "Slide Out") f.SendEvent("IN");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] HeroRestore: {ex}");
            }
        }

        /// <summary>Heroi do jeito que a cutscene deixa: sem controle ou invisivel.</summary>
        public static bool HeroLooksFrozen()
        {
            try
            {
                var hc = HeroController.instance;
                if (hc == null) return false;
                var mr = hc.GetComponent<MeshRenderer>();
                return hc.controlReqlinquished || (mr != null && !mr.enabled);
            }
            catch { return false; }
        }

        /// <summary>A cutscene liga CameraShake.RumblingMed ('Final Impact') e RumblingBig ('Kill Hit') e so o fim dela
        /// desliga (ShakePositionV2 em loop enquanto o bool for true = tremor infinito). Encerramento na marra: desliga.</summary>
        public static void StopCutsceneRumble()
        {
            try
            {
                var gc = GameCameras.instance;
                if (gc == null) return;
                PlayMakerFSM shake = null;
                if (gc.cameraParent != null)
                    foreach (var f in gc.cameraParent.GetComponents<PlayMakerFSM>())
                        if (f.FsmName == "CameraShake") { shake = f; break; }
                if (shake == null) shake = gc.cameraShakeFSM;
                if (shake == null) return;
                foreach (var n in new[] { "RumblingMed", "RumblingBig" })
                {
                    var b = shake.FsmVariables.FindFsmBool(n);
                    if (b != null) b.Value = false;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] StopCutsceneRumble: {ex}");
            }
        }

        /// <summary>Restauracao quando a copia some no meio da cutscene (o componente dela nao roda mais). Espera o
        /// MESMO heroi estar fora de transicao de cena e vivo, e so entao restaura - se ele ainda estiver congelado.
        /// Desiste se o heroi morrer (a morte/respawn cuida dele; restaurar ali estragaria o banco) ou for trocado
        /// (save recarregado), ou depois de 60 s.</summary>
        public static void Deferred(bool rumble)
        {
            try
            {
                var hc = HeroController.instance;
                if (hc == null || hc.cState.dead) return;
                var go = new GameObject("CreatureSpawner HeroRestore");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var r = go.AddComponent<DeferredHeroRestore>();
                r.Hero = hc; r.Rumble = rumble;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] HeroRestore.Deferred: {ex}");
                AfterBossCutscene();
            }
        }
    }

    internal sealed class DeferredHeroRestore : MonoBehaviour
    {
        public HeroController Hero;
        public bool Rumble;
        private float giveUpAt;

        private void Start() => giveUpAt = Time.realtimeSinceStartup + 60f;

        private void Update()
        {
            try
            {
                var hc = HeroController.instance;
                if (Hero == null || hc != Hero || hc.cState.dead || Time.realtimeSinceStartup > giveUpAt) { Destroy(gameObject); return; }
                var gm = GameManager.instance;
                if (gm == null || gm.IsInSceneTransition || hc.cState.transitioning ||
                    hc.transitionState != GlobalEnums.HeroTransitionState.WAITING_TO_TRANSITION) return;
                if (HeroRestore.HeroLooksFrozen())
                {
                    Modding.Logger.Log("[CreatureSpawner] restauracao adiada do heroi (cutscene final interrompida)");
                    HeroRestore.AfterBossCutscene();
                }
                if (Rumble) HeroRestore.StopCutsceneRumble();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] DeferredHeroRestore: {ex}");
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Morte do HEROI numa sala de sonho/Godhome sem um "lugar pra acordar" valido (softlock relatado: jogador
    /// morto pela Absolute Radiance spawnada num save Godseeker). Decompilado/dumps:
    ///  - HeroController.Die: se GetCurrentMapZone() for DREAM_WORLD ou GODS_GLORY, NAO chama gm.PlayerDead (respawn no
    ///    banco): RelinquishControl, StopAnimationControl, gravidade off, isInvincible, renderer off e so ativa o
    ///    'Hero Death' do Knight.
    ///  - FSM 'Hero Death Anim' (knight_fsms.txt): 'Map Zone' -> DREAM -> 'Death Dream' -> 'Bursting' (Dream Burst Pt +
    ///    Dream Impact = a explosao rosa/branca, HUD Blanker White FADE IN) -> 'Dream Return': GetPlayerDataString
    ///    dreamReturnScene -> BeginSceneTransition(dreamReturnScene, 'door_dreamReturn').
    ///  - Toda sala do Godhome e GODS_GLORY. No jogo normal so se morre la dentro de uma luta de estatua/panteao, que
    ///    grava dreamReturnScene antes; num quarto comum do Godhome (GG_Workshop, GG_Atrium...) so da pra morrer por
    ///    causa de criatura spawnada, e dreamReturnScene fica com o que estiver no save. No save do usuario (Godseeker,
    ///    bossRushMode=true) e "none": Player.log "Scene 'none' couldn't be loaded" + NullReferenceException em
    ///    SceneLoad.BeginRoutine (LoadSceneAsync devolveu null). O sceneLoad do GameManager nunca e limpo (so no
    ///    Finish), toda transicao seguinte e recusada ("while a scene transition is in progress"), o heroi fica
    ///    invisivel/sem controle e a tela parada no branco/explosao = SOFTLOCK, nao so efeito visual.
    /// Correcao: no inicio do Die() (evento HeroController.OnDeath, antes do ramo de map zone), se a zona for de sonho e
    /// dreamReturnScene nao for uma cena carregavel, aponta pra cena ATUAL (o heroi "acorda" na mesma sala, a copia
    /// spawnada some com o reload). O 'Dream Return' do Knight cuida do resto como no jogo normal (Prostrate -> Get Up
    /// -> Regain Control; sem 'door_dreamReturn' na sala o EnterHero falha - ver AfterWakeTransition). Quando a cena nova
    /// ativa, o valor original volta (o save fica igual). dreamReturnScene valido (sonho/estatua de verdade) nao e
    /// tocado. WHITE_PALACE nao entra aqui: la o Die() segue o caminho normal (gm.PlayerDead).</summary>
    internal static class DreamDeathGuard
    {
        private static HeroController hooked;
        private static bool restorePending, enterFixPending;
        private static string originalValue, patchedValue;
        private static Vector3 deathPos;

        /// <summary>Chamado a cada spawn: assina o OnDeath do HeroController atual (um novo e criado a cada save carregado).</summary>
        public static void Ensure(HeroController hc)
        {
            if (hc == null || ReferenceEquals(hc, hooked)) return;
            hooked = hc;
            hc.OnDeath += OnHeroDeath;
        }

        private static void OnHeroDeath()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm == null || gm.sm == null || gm.playerData == null) return;
                var zone = gm.sm.mapZone;
                if (zone != GlobalEnums.MapZone.DREAM_WORLD && zone != GlobalEnums.MapZone.GODS_GLORY) return;
                string ret = gm.playerData.GetString("dreamReturnScene");
                if (!string.IsNullOrEmpty(ret) && Application.CanStreamedLevelBeLoaded(ret)) return;
                string here = gm.sceneName;
                if (string.IsNullOrEmpty(here) || !Application.CanStreamedLevelBeLoaded(here))
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] heroi morreu em {zone} com dreamReturnScene='{ret}' invalido e a cena atual '{here}' tambem nao e carregavel - nada a fazer");
                    return;
                }
                gm.playerData.SetString("dreamReturnScene", here);
                if (!restorePending)
                {
                    restorePending = true;
                    originalValue = ret;
                    UnityEngine.SceneManagement.SceneManager.activeSceneChanged += Restore;
                }
                patchedValue = here;
                var hc = HeroController.instance;
                if (hc != null) deathPos = hc.transform.position;
                if (!enterFixPending)
                {
                    enterFixPending = true;
                    gm.OnFinishedSceneTransition += AfterWakeTransition;
                }
                Modding.Logger.Log($"[CreatureSpawner] heroi morreu em {zone} ('{here}') com dreamReturnScene='{ret}' (cena inexistente -> " +
                                   $"softlock na volta do sonho) -> acordando em '{here}'");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] DreamDeathGuard: {ex}");
            }
        }

        private static void Restore(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= Restore;
            restorePending = false;
            try
            {
                var pd = GameManager.instance != null ? GameManager.instance.playerData : null;
                // So desfaz se ninguem mais mexeu no valor desde entao (ex. o jogo gravou um retorno de verdade).
                if (pd != null && pd.GetString("dreamReturnScene") == patchedValue)
                    pd.SetString("dreamReturnScene", originalValue);
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] DreamDeathGuard.Restore: {ex}");
            }
        }

        /// <summary>Pausa quebrada depois de acordar (relatado). O 'Dream Return' entra com o portao 'door_dreamReturn',
        /// que salas comuns do Godhome nao tem. Decompilado: o fim da transicao (sceneLoad.Finish) chama
        /// GameManager.OnNextLevelReady -> SetState(ENTERING_LEVEL) + EnterHero(additiveGateSearch: true); nessa busca, sem
        /// achar o portao, o jogo so loga "Searching in next scene for TransitionGate failed." (visto no Player.log, logo
        /// apos 'acordando em GG_Atrium') e RETORNA - HeroController.EnterScene nunca roda, entao nem o
        /// HeroController.FinishedEnteringScene (transitioning=false, DamageMode FULL, hazard respawn) nem o
        /// GameManager.FinishedEnteringScene (unico SetState(PLAYING) de uma entrada normal) acontecem. gameState fica
        /// ENTERING_LEVEL pra sempre; InputHandler.Update so abre o pause com gameState PLAYING/PAUSED (e
        /// TransitionPoint.OnTriggerEnter2D tambem exige PLAYING). O 'Dream Return' do Knight devolve controle/animacao,
        /// mas nao mexe no gameState. Correcao: logo depois do OnNextLevelReady (evento OnFinishedSceneTransition, mesmo
        /// callback Finish), se a entrada falhou (gameState ENTERING_LEVEL e heroi ainda em EXITING_SCENE do LeaveScene),
        /// faz o que o EnterHero faria ao achar o portao: StartCoroutine(hero.EnterScene(portao, 0)) - com um portao
        /// que EXISTE na sala (o tipo 'door' mais perto de onde o heroi morreu, como o door_dreamReturn original; senao o
        /// mais perto de qualquer tipo). Se a sala nao tiver nenhum, EnterSceneDreamGate no ponto da morte (o mesmo
        /// "last ditch" do EnterHero nao-aditivo). Se o jogo achou o portao sozinho, nao faz nada.</summary>
        private static void AfterWakeTransition()
        {
            var gm = GameManager.instance;
            if (gm != null) gm.OnFinishedSceneTransition -= AfterWakeTransition;
            enterFixPending = false;
            try
            {
                var hc = HeroController.instance;
                if (gm == null || hc == null) return;
                if (gm.gameState != GlobalEnums.GameState.ENTERING_LEVEL ||
                    hc.transitionState != GlobalEnums.HeroTransitionState.EXITING_SCENE)
                    return; // o EnterHero achou um portao e ja esta entrando normalmente
                if (gm.sceneName != patchedValue) return; // nao e a volta do sonho que este guard redirecionou
                var gate = FindWakeGate(gm.sceneName, deathPos);
                if (gate != null)
                {
                    Modding.Logger.Log($"[CreatureSpawner] '{gm.sceneName}' nao tem o portao '{gm.entryGateName}' -> entrando pelo '{gate.name}' " +
                                       "(sem isso o gameState fica ENTERING_LEVEL e o pause nao abre)");
                    gm.StartCoroutine(hc.EnterScene(gate, 0f));
                }
                else
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{gm.sceneName}' sem nenhum TransitionPoint ativo -> EnterSceneDreamGate em {deathPos}");
                    hc.transform.position = new Vector3(deathPos.x, deathPos.y, hc.transform.position.z);
                    hc.EnterSceneDreamGate();
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] DreamDeathGuard.AfterWakeTransition: {ex}");
            }
        }

        private static TransitionPoint FindWakeGate(string sceneName, Vector3 near)
        {
            TransitionPoint bestDoor = null, bestAny = null;
            float dDoor = float.MaxValue, dAny = float.MaxValue;
            var list = TransitionPoint.TransitionPoints;
            if (list == null) return null;
            foreach (var tp in list)
            {
                if (tp == null || !tp.gameObject.activeInHierarchy || tp.gameObject.scene.name != sceneName) continue;
                var gp = tp.GetGatePosition();
                if (gp == GlobalEnums.GatePosition.unknown) continue;
                float d = ((Vector2)tp.transform.position - (Vector2)near).sqrMagnitude;
                if (d < dAny) { dAny = d; bestAny = tp; }
                if (gp == GlobalEnums.GatePosition.door && d < dDoor) { dDoor = d; bestDoor = tp; }
            }
            return bestDoor ?? bestAny;
        }
    }

    /// <summary>Rede de seguranca: se o corpse cair muito abaixo de onde o heroi estava no spawn (que tem
    /// chao garantido), ele volta pra cima desse ponto sem velocidade e cai de novo no chao certo. Depois de
    /// algumas tentativas sem sucesso o corpse e escondido em vez de cair pra sempre.</summary>
    internal sealed class CorpseFallGuard : MonoBehaviour
    {
        public Vector3 Anchor;
        public float MaxDrop = 14f;
        public float Lifetime = 40f;
        private int rescues;
        private float age;

        private void Update()
        {
            age += Time.deltaTime;
            if (age > Lifetime) { Destroy(this); return; }
            if (transform.position.y >= Anchor.y - MaxDrop) return;
            if (++rescues > 3)
            {
                gameObject.SetActive(false);
                Destroy(this);
                return;
            }
            transform.position = new Vector3(Anchor.x, Anchor.y + 1f, transform.position.z);
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
        }
    }

    // Objetos extras pre-carregados que nao aparecem no painel (partes de encontros com varios chefes).
    internal static class EncounterGroups
    {
        public const string MantisScene = "Fungus2_15_boss";
        public const string MantisSubPath = "Mantis Battle/Battle Sub"; // contem 'Mantis Lord S1' e 'S2' + FSM 'Start'
        public static GameObject MantisSubPrefab;

        // GG_Mantis_Lords_V = copia Godhome da mesma sala ("Sisters of Battle"): mesmo caminho "Battle Sub",
        // so que com as TRES irmas (S1+S2+S3, ver "Mantis Lord S3" em EnemyLocations) em vez de so S1+S2.
        public const string SistersScene = "GG_Mantis_Lords_V";
        public const string SistersSubPath = "Mantis Battle/Battle Sub";
        public static GameObject SistersSubPrefab;

        // Elder Hu: os aneis do ataque ('Ring Holder', raiz da sala) e a poeira do golpe ('Warrior/Full Dust', irmao
        // dele) nao sao filhos dele - ver o perfil "Elder Hu" em ArenaProfiles e ElderHuRings.
        public const string HuScene = "Fungus2_32";
        public const string HuRingsPath = "Ring Holder";
        public const string HuDustPath = "Warrior/Full Dust";
        public static GameObject HuRingsPrefab, HuDustPrefab;

        // Dream Warriors: o fantasma ferido que da as ultimas palavras e a essencia e o objeto RAIZ 'Ghost Warrior NPC'
        // da sala, nao o guerreiro (ver GhostWarriorReward). Chave = nome do painel; valor = cena nativa (a mesma do
        // EnemyLocations). Estrutura identica nas 7 salas (FSMs 'Conversation Control'/'Appear'/'npc_control'/...).
        public const string GhostNpcPath = "Ghost Warrior NPC";
        public static readonly Dictionary<string, string> GhostNpcScenes = new Dictionary<string, string>
        {
            ["Elder Hu"] = "Fungus2_32",
            ["Xero"] = "RestingGrounds_02_boss",
            ["Gorb"] = "Cliffs_02_boss",
            ["Marmu"] = "Fungus3_40_boss",
            ["No Eyes"] = "Fungus1_35",
            ["Galien"] = "Deepnest_40",
            ["Markoth"] = "Deepnest_East_10",
        };
        public static readonly Dictionary<string, GameObject> GhostNpcPrefabs = new Dictionary<string, GameObject>();

        // Flukemarm ('Fluke Mother'): as Flukefeys que ela cospe nao sao filhas dela - moram no objeto RAIZ 'Hatcher Cage (2)'
        // da sala (tag 'Extra Tag'), achado por FindGameObject(withTag) no Init - ver FlukeMotherCage.
        public const string FlukeScene = "Waterways_12_boss";
        public const string FlukeCagePath = "Hatcher Cage (2)";
        public static GameObject FlukeCagePrefab;

        // Crystal Guardian (Mines_18_boss, cena ADITIVA): os 4 lasers do teto que disparam no rugido sao objetos RAIZ da
        // cena base Mines_18 ('Laser Turret Mega (1..4)') - ver CrystalGuardianTurrets.
        public const string CrystalTurretScene = "Mines_18";
        public static readonly string[] CrystalTurretPaths =
            { "Laser Turret Mega (1)", "Laser Turret Mega (2)", "Laser Turret Mega (3)", "Laser Turret Mega (4)" };
        public static readonly List<GameObject> CrystalTurretPrefabs = new List<GameObject>();

        // Absolute Radiance: o VAZIO que sobe na cutscene final ('Abyss Tween') e o objeto 'Boss Control/Abyss Pit', IRMAO
        // dela no GG_Radiance (nao vem no preload dela) - ver AbsoluteRadianceAbyss.
        public const string RadianceScene = "GG_Radiance";
        public const string RadianceAbyssPath = "Boss Control/Abyss Pit";
        public static GameObject RadianceAbyssPrefab;
        // Absolute Radiance, final do P5 (Shade Lord): outros irmaos dela no GG_Radiance, achados no Init do 'Control' por
        // FindChild($Boss Control, nome) -> var. (var da FSM, caminho na sala) - ver AbsoluteRadianceAbyss.
        public static readonly (string Var, string Path)[] RadianceEndingParts =
        {
            ("Shade Lord", "Boss Control/Shade_Lord"),           // o Shade Lord gigante ('Lord Appear')
            ("Shade Lord Hand", "Boss Control/Shade Lord Hand"), // a mao que agarra ela ('Hand Grab'/'Break'/'Final Stream')
            ("Shade Slashes", "Boss Control/radiance_slashes"),  // os cortes de tela cheia de cada golpe ('Shade Slash')
            ("Final Explode", "Boss Control/Final Explode"),     // explosao + tela preta antes do 'Ending Scene'
        };
        public static readonly Dictionary<string, GameObject> RadianceEndingPrefabs = new Dictionary<string, GameObject>();
        // Absolute Radiance (GG_Radiance) e Radiance (Dream_Final_Boss): o LASER QUE VARRE a arena e o objeto 'Boss Control/Beam
        // Sweeper', IRMAO delas nas duas salas (nao vem no preload delas) - ver RadianceBeamSweeper. Um por cena (mesma FSM nas duas).
        public const string PlainRadianceScene = "Dream_Final_Boss";
        public const string RadianceBeamSweeperPath = "Boss Control/Beam Sweeper";
        public static GameObject AbsRadianceBeamSweeperPrefab, PlainRadianceBeamSweeperPrefab;

        // "Nailmasters Oro & Mato" (luta em dupla): o objeto 'Brothers' INTEIRO do GG_Nailmasters (Oro + Mato + FSM 'Combo Control' do
        // pai, com as referencias cruzadas internas intactas) - mesmo esquema pai+filhos pre-carregados do 'Mantis Battle/Battle Sub'.
        // Vira o botao do painel em CreatureSpawnerMod.Initialize - ver NailmastersPair.
        public const string NailBrosScene = "GG_Nailmasters";
        public const string NailBrosPath = "Brothers";
        public static GameObject NailBrosPrefab;

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            yield return (NailBrosScene, NailBrosPath);
            yield return (RadianceScene, RadianceAbyssPath);
            foreach (var part in RadianceEndingParts) yield return (RadianceScene, part.Path);
            yield return (RadianceScene, RadianceBeamSweeperPath);
            yield return (PlainRadianceScene, RadianceBeamSweeperPath);
            yield return (MantisScene, MantisSubPath);
            yield return (SistersScene, SistersSubPath);
            yield return (HuScene, HuRingsPath);
            yield return (HuScene, HuDustPath);
            foreach (var scene in GhostNpcScenes.Values) yield return (scene, GhostNpcPath);
            yield return (FlukeScene, FlukeCagePath);
            foreach (var p in CrystalTurretPaths) yield return (CrystalTurretScene, p);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            MantisSubPrefab = CacheOne(pre, MantisScene, MantisSubPath, "Mantis Lords vai lutar so com 1", log);
            SistersSubPrefab = CacheOne(pre, SistersScene, SistersSubPath, "Sisters of Battle vai lutar so com 1", log);
            NailBrosPrefab = CacheOne(pre, NailBrosScene, NailBrosPath, "\"Nailmasters Oro & Mato\" (luta em dupla) fica fora do painel", log);
            HuRingsPrefab = CacheOne(pre, HuScene, HuRingsPath, "Elder Hu fica sem os aneis", log);
            HuDustPrefab = CacheOne(pre, HuScene, HuDustPath, "Elder Hu fica sem a poeira do golpe", log);
            foreach (var kv in GhostNpcScenes)
                GhostNpcPrefabs[kv.Key] = CacheOne(pre, kv.Value, GhostNpcPath, $"{kv.Key} morre sem ultimas palavras/essencia", log);
            FlukeCagePrefab = CacheOne(pre, FlukeScene, FlukeCagePath, "Flukemarm fica sem as Flukefeys", log);
            CrystalTurretPrefabs.Clear();
            foreach (var p in CrystalTurretPaths)
            {
                var t = CacheOne(pre, CrystalTurretScene, p, "Crystal Guardian fica sem esse laser do teto", log);
                if (t != null) CrystalTurretPrefabs.Add(t);
            }
            RadianceAbyssPrefab = CacheOne(pre, RadianceScene, RadianceAbyssPath, "Absolute Radiance morre sem o vazio subindo na cutscene final", log);
            RadianceEndingPrefabs.Clear();
            foreach (var part in RadianceEndingParts)
            {
                var go = CacheOne(pre, RadianceScene, part.Path, $"final do Shade Lord da Absolute Radiance sem '{part.Var}' (so falta o visual)", log);
                if (go != null) RadianceEndingPrefabs[part.Var] = go;
            }
            AbsRadianceBeamSweeperPrefab = CacheOne(pre, RadianceScene, RadianceBeamSweeperPath, "Absolute Radiance fica sem o laser que varre a arena", log);
            PlainRadianceBeamSweeperPrefab = CacheOne(pre, PlainRadianceScene, RadianceBeamSweeperPath, "Radiance fica sem o laser que varre a arena", log);
        }

        internal static GameObject CacheOne(Dictionary<string, Dictionary<string, GameObject>> pre, string scene, string path, string missingNote, Action<string> log)
        {
            try
            {
                if (pre.TryGetValue(scene, out var objs) && objs.TryGetValue(path, out var orig) && orig != null)
                {
                    var clone = UnityEngine.Object.Instantiate(orig);
                    UnityEngine.Object.DontDestroyOnLoad(clone);
                    clone.SetActive(false);
                    var cleared = SceneLinkSanitizer.Sanitize(clone);
                    if (cleared.Count > 0) log($"'{scene}/{path}': {cleared.Count} referencia(s) externa(s) zerada(s): {string.Join(", ", cleared.ToArray())}");
                    return clone;
                }
                log($"EncounterGroups: '{scene}/{path}' nao foi pre-carregado - {missingNote}");
            }
            catch (Exception ex)
            {
                log($"EncounterGroups: erro ao pre-carregar '{scene}/{path}': {ex}");
            }
            return null;
        }
    }

    // A luta real (FSMs do Fungus2_15_boss): 'Mantis Throne Main' manda MLORD START MAIN -> o lorde principal
    // ('Battle Main/Mantis Lord') luta sozinho -> quando ele morre o trono recebe MANTIS DEFEATED, espera ~1.25s
    // (Pause 0.25 + Defeated 1.0) e manda MLORD START SUB -> o FSM 'Start' do 'Battle Sub' ativa S1+S2
    // (Sub=True: so atacam sob comando) e coordena os ataques em dupla; quando um morre, KILLED -> o outro
    // passa a lutar sozinho (Sub=False). Os tronos/'Battle Control' nao vem no preload, entao este
    // componente faz o papel do trono: observa o principal e, quando ele morre, spawna o 'Battle Sub'.
    // Mesmo mecanismo serve pra "Mantis Lords" (sub = S1+S2) e pra "Sisters of Battle" (sub = S1+S2+S3,
    // ver EncounterGroups.SistersSubPrefab) - so muda qual prefab de grupo e spawnado no final.
    internal sealed class MantisLordsEncounter : MonoBehaviour
    {
        private GameObject main;
        private HealthManager mainHm;
        private SpawnContext ctx;
        private GameObject subPrefab;
        private float deadAt = -1f;

        public static void Begin(GameObject mainLord, SpawnContext ctx, GameObject subPrefab)
        {
            if (subPrefab == null) return;
            var host = new GameObject("CreatureSpawner Mantis Lords Encounter");
            var enc = host.AddComponent<MantisLordsEncounter>();
            enc.main = mainLord;
            enc.mainHm = mainLord.GetComponent<HealthManager>();
            enc.ctx = ctx;
            enc.subPrefab = subPrefab;
        }

        private void Update()
        {
            if (deadAt < 0f)
            {
                bool dead = main == null || !main.activeInHierarchy || (mainHm != null && mainHm.isDead);
                if (dead) deadAt = Time.time;
                return;
            }
            if (Time.time - deadAt < 1.25f) return;
            SpawnSubs();
            Destroy(gameObject);
        }

        private void SpawnSubs()
        {
            try
            {
                var prefab = subPrefab;
                var sub = Instantiate(prefab, prefab.transform.position + ctx.Delta3, Quaternion.identity);
                SpawnFixups.FixConstrainPosition(sub, ctx);
                foreach (var hm in sub.GetComponentsInChildren<HealthManager>(true))
                {
                    var lord = hm.gameObject;
                    if (lord == sub || lord.GetComponent<SpawnLeash>() != null) continue;
                    SpawnFixups.ConfigureLeash(lord.AddComponent<SpawnLeash>(), ctx);
                    lord.AddComponent<SpawnDeathWatch>().Ctx = ctx;
                }
                FsmEventWhenInState.Add(sub, "Start", "Init", "MLORD START SUB");
                sub.SetActive(true);
                ctx.PatchHierarchy(sub); // 'Start' agora; S1/S2 (inativos ate o ActivateAllChildren) via FsmPatchOnEnable
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] MantisLordsEncounter: {ex}");
            }
        }
    }

    /// <summary>Watcher Knights: a luta real tem SEIS cavaleiros (Ruins2_03_boss 'Battle Control/Black Knight 1'..'6', mesmo FSM,
    /// so 'Show Title' = True no 1), acordados um a um pela FSM 'Battle Control' do pai (scratchpad\FsmDumpTool\out\
    /// watcher_scene.txt), com 'Battle Enemies' = 6 decrementado pelo HealthManager.Die de cada um (campo battleScene):
    ///   Knight 1: WAKE BK 1; Battle Enemies &lt;= 5 -> Pause 6 (2 s) -> Knight 2; ou Wait 20 s -> INSTANT -> Knight 2 (sem pausa).
    ///   Knight 2: WAKE BK 2; &lt;= 5 -> 2 s -> Knight 3.   Knight 3: WAKE BK 3; &lt;= 4 -> 2 s -> Knight 4.
    ///   Knight 4: &lt;= 3 -> 2 s -> Knight 5.   Knight 5: &lt;= 2 -> 2 s -> Knight 6.   Knight 6: == 0 -> Journal/fim.
    /// (Knight 3 pula pro 4 se o lustre ja caiu em cima dele - watcherChandelier; aqui sempre sao os 6.) Ou seja, no maximo dois
    /// lutando ao mesmo tempo; cada um e independente (so o GetTagCount('Spell Vulnerable') do 'Set Time' muda o ritmo/ataques
    /// quando ha mais de um). A 'Battle Control' nao vem no preload (e faria coisas da SALA: fecha portoes, trava camera, desliga
    /// o '_Enemies' da sala atual, grava killedBlackKnight), entao este componente faz o papel dela com o PROPRIO prefab do
    /// cavaleiro 1 em cache (os 2..6 so diferem no 'Show Title' e na posicao). Cada cavaleiro novo nasce perto do heroi naquele
    /// momento, do lado em que estaria em relacao ao 1 na sala original, e acorda na hora (WakeEvents do perfil).</summary>
    internal sealed class WatcherKnightsEncounter : MonoBehaviour
    {
        private const int TotalKnights = 6;
        private const float SoloTimeout = 20f; // 'Knight 1' Wait 20 -> INSTANT
        private const float WakePause = 2f;    // 'Pause 1'..'Pause 4'/'Pause 6'
        // Indice = cavaleiros ja acordados: quando 'Battle Enemies' (vivos + ainda nao acordados) cai pra esse valor, acorda o proximo.
        private static readonly int[] NextWhenRemaining = { 0, 5, 5, 4, 3, 2 };
        // Lado do cavaleiro N em relacao ao 1 na sala (x local: 1=-7.41, 2=9.59, 3=-13.41, 4=4.11, 5=-20.41, 6=-1.62).
        private static readonly float[] Side = { 0f, 0f, 1f, -1f, 1f, -1f, 1f };
        private const float SideOffset = 3.5f;

        private string panelName;
        private GameObject prefab;
        private readonly List<HealthManager> knights = new List<HealthManager>();
        private int woken = 1;
        private float stageAt, readyAt = -1f;

        public static void Begin(GameObject firstKnight, SpawnContext ctx)
        {
            if (ctx == null || ctx.Prefab == null) return;
            var host = new GameObject("CreatureSpawner Watcher Knights Encounter");
            var enc = host.AddComponent<WatcherKnightsEncounter>();
            enc.panelName = ctx.Name;
            enc.prefab = ctx.Prefab;
            enc.stageAt = Time.time;
            enc.knights.Add(firstKnight.GetComponent<HealthManager>());
        }

        private int Dead()
        {
            int n = 0;
            foreach (var hm in knights) if (hm == null || hm.isDead) n++;
            return n;
        }

        private void Update()
        {
            try
            {
                int dead = Dead();
                if (woken >= TotalKnights)
                {
                    if (dead >= knights.Count) Destroy(gameObject); // 'Knight 6': Battle Enemies == 0 -> fim
                    return;
                }
                // (Troca de cena: este host e os cavaleiros sao objetos da cena atual e somem juntos.)
                if (readyAt < 0f)
                {
                    int remaining = TotalKnights - dead;
                    if (remaining <= NextWhenRemaining[woken]) readyAt = Time.time + WakePause;
                    else if (woken == 1 && Time.time - stageAt >= SoloTimeout) SpawnNext(); // INSTANT
                    return;
                }
                if (Time.time < readyAt) return;
                readyAt = -1f;
                SpawnNext();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] WatcherKnightsEncounter: {ex}");
                Destroy(gameObject);
            }
        }

        private void SpawnNext()
        {
            var hero = HeroController.instance;
            if (hero == null || prefab == null) { Destroy(gameObject); return; }
            woken++;
            stageAt = Time.time;
            Vector3 heroPos = hero.transform.position;
            var ctx = SpawnFixups.Prepare(panelName, prefab, heroPos, out var defaultPos);
            var pos = ChoosePos(heroPos, Side[woken], defaultPos);
            var k = Instantiate(prefab, pos, Quaternion.identity);
            k.AddComponent<SpawnLeash>();
            DormantBossFix.Apply(k);
            SpawnFixups.BeforeActivate(k, ctx); // coleira, SpawnDeathWatch e o WAKE do perfil
            k.SetActive(true);
            foreach (var f in k.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Black Knight")
                {
                    var title = f.FsmVariables.FindFsmBool("Show Title");
                    if (title != null) title.Value = false; // so o 1 mostra o titulo (e passa pelo 'Music?')
                }
            ctx.PatchHierarchy(k);
            knights.Add(k.GetComponent<HealthManager>());
            Modding.Logger.Log($"[CreatureSpawner] Watcher Knights: cavaleiro {woken}/{TotalKnights} acordando em {pos} ({Dead()} derrotado(s))");
        }

        // Do lado original (Side), SideOffset do heroi; se tiver parede (Terrain) no caminho, o outro lado; senao o padrao heroi+(2,2).
        private static Vector3 ChoosePos(Vector3 heroPos, float side, Vector3 fallback)
        {
            var origin = new Vector2(heroPos.x, heroPos.y);
            foreach (var s in new[] { side, -side })
            {
                if (s == 0f) continue;
                if (Physics2D.Raycast(origin, new Vector2(s, 0f), SideOffset + 1f, SpawnFixups.TerrainMask).collider != null) continue;
                return new Vector3(heroPos.x + s * SideOffset, heroPos.y + SpawnFixups.DefaultOffset.y, heroPos.z);
            }
            return fallback;
        }
    }

    // Elder Hu: o ataque dele ativa os aneis de um objeto da SALA ('Ring Holder', achado por GameObject.Find no
    // 'Attacking' Init - ver o perfil "Elder Hu" em ArenaProfiles). Cada copia ganha o seu proprio 'Ring Holder' (e a
    // 'Full Dust'), na posicao original + delta da arena, com as FSMs dos aneis deslocadas pelo mesmo SpawnContext
    // (Check Pos/Down/Land/Reset sao absolutos). O FindGameObject do Init esta desligado (DisableActions do perfil);
    // com a var 'Ring Holder' ja preenchida, os FindChild('1'..'17') originais acham os aneis da copia quando a FSM
    // comeca (Start, depois deste AfterActivate). Nome proprio pra que o GameObject.Find de um Elder Hu de verdade
    // (na sala dele) nao pegue o nosso.
    internal static class ElderHuRings
    {
        public static void Attach(GameObject hu, SpawnContext ctx)
        {
            try
            {
                PlayMakerFSM attacking = null;
                foreach (var f in hu.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Attacking") { attacking = f; break; }
                if (attacking == null || ctx.Profile == null) return;
                Link(attacking, "Ring Holder", SpawnExtra(EncounterGroups.HuRingsPrefab, hu, ctx, "Ring Holder (Elder Hu spawnado)"));
                Link(attacking, "Full Dust", SpawnExtra(EncounterGroups.HuDustPrefab, hu, ctx, "Full Dust (Elder Hu spawnado)"));
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] ElderHuRings: {ex}");
            }
        }

        private static void Link(PlayMakerFSM fsm, string varName, GameObject go)
        {
            if (go == null) return;
            var v = fsm.FsmVariables.FindFsmGameObject(varName);
            if (v != null) v.Value = go;
            else Modding.Logger.LogWarn($"[CreatureSpawner] Elder Hu: FSM '{fsm.FsmName}' sem a var '{varName}'");
        }

        private static GameObject SpawnExtra(GameObject prefab, GameObject owner, SpawnContext ctx, string name)
        {
            if (prefab == null) return null;
            var t = prefab.transform;
            var go = UnityEngine.Object.Instantiate(prefab, t.position + ctx.Delta3, t.rotation);
            go.name = name;
            go.AddComponent<DestroyWithOwner>().Owner = owner;
            go.SetActive(true);
            ctx.PatchHierarchy(go); // aneis (inativos) via FsmPatchOnEnable quando o ataque liga cada um
            return go;
        }
    }

    /// <summary>Flukemarm spawnada sem as Flukefeys (relatado). FSM 'Fluke Mother' (scratchpad\FsmDumpTool\out\flukemother_scene.txt):
    ///  - 'Init': FindGameObject(objectName '', withTag 'Extra Tag') -> var 'Cage'. Em Waterways_12_boss (e GG_Flukemarm) o unico
    ///    objeto com essa tag e a RAIZ 'Hatcher Cage (2)' em (-100.2, 101.1) - um deposito fora da sala com 6 'Fluke Fly Spawner'
    ///    (FSM 'Fluke Fly': Init GetParent -> Cage; Inert espera SPAWN; Spawn = SetParent(null) + liga renderer/collider -> Chase;
    ///    na morte/CENTIPEDE DEATH volta pra gaiola e espera de novo).
    ///  - 'Spawn'/'Spawn 2': GetRandomChild(Cage) -> var 'Fluke Fly' -> GameObjectIsNull? -> SetPosition no Spawn Point -> SPAWN.
    /// Fora da sala o FindGameObjectWithTag devolve null (ou a gaiola de OUTRO chefe com 'Extra Tag' na sala atual):
    /// GetRandomChild(null) nao escreve nada, 'Fluke Fly' fica null (o 'Select Point' zera) -> CANCEL -> nenhuma mosca, so a
    /// animacao/som do cuspe. Correcao (mesmo padrao do ElderHuRings): cada copia ganha a sua 'Hatcher Cage (2)' pre-carregada
    /// (na posicao original, que e so um deposito), sem a tag (pra nenhum outro FindGameObjectWithTag achar a nossa), o
    /// FindGameObject do Init e desligado e a var 'Cage' aponta pra copia. Quando a Flukemarm some, a gaiola (com as moscas que
    /// ainda estao nela), as moscas mortas (inativas) e o 'Flukeflys' vazio que o Init desparenta sao removidos. Moscas vivas
    /// soltas continuam, como na sala original (nenhuma delas tem EventRegister, entao o SendEventToRegister('CENTIPEDE DEATH')
    /// do 'Dead' nao chega nelas nem no jogo normal).</summary>
    internal sealed class FlukeMotherCage : MonoBehaviour
    {
        private GameObject owner, holder;
        private readonly List<GameObject> flies = new List<GameObject>();

        public static void Attach(GameObject mother, SpawnContext ctx)
        {
            try
            {
                var prefab = EncounterGroups.FlukeCagePrefab;
                if (prefab == null) return;
                PlayMakerFSM fsm = null;
                foreach (var f in mother.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Fluke Mother") { fsm = f; break; }
                var cageVar = fsm != null ? fsm.FsmVariables.FindFsmGameObject("Cage") : null;
                if (cageVar == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Flukemarm: FSM 'Fluke Mother' ou a var 'Cage' nao encontrada - sem Flukefeys");
                    return;
                }
                int off = 0;
                foreach (var st in fsm.FsmStates)
                    if (st.Name == "Init")
                        foreach (var a in st.Actions)
                            if (a is FindGameObject fg && fg.store != null && fg.store.Name == "Cage") { a.Enabled = false; off++; }
                if (off == 0) Modding.Logger.LogWarn("[CreatureSpawner] Flukemarm: FindGameObject('Extra Tag') do 'Init' nao encontrado");

                var cage = Instantiate(prefab); // posicao do prefab = a original (deposito longe de qualquer sala)
                cage.name = "Hatcher Cage (Flukemarm spawnada)";
                cage.tag = "Untagged";
                var keeper = cage.AddComponent<FlukeMotherCage>();
                keeper.owner = mother;
                foreach (Transform child in cage.transform) keeper.flies.Add(child.gameObject); // saem da gaiola no SPAWN
                keeper.holder = fsm.FsmVariables.FindFsmGameObject("Flukeflys")?.Value;
                cage.SetActive(true);
                ctx.PatchHierarchy(cage);
                cageVar.Value = cage; // a FSM so entra no 'Init' no Start (depois deste AfterActivate)
                Modding.Logger.Log($"[CreatureSpawner] Flukemarm: gaiola propria com {keeper.flies.Count} Flukefeys em {cage.transform.position}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] FlukeMotherCage: {ex}");
            }
        }

        private void Update()
        {
            if (owner != null && owner.activeInHierarchy) return;
            foreach (var go in flies)
                if (go != null && !go.activeInHierarchy && go.transform.parent != transform) Destroy(go); // mortas, fora da gaiola
            if (holder != null && holder.transform.parent == null) Destroy(holder);
            Destroy(gameObject); // + as que ainda estao na gaiola
        }
    }

    /// <summary>Crystal Guardian sem laser (relatado: "nem o da sala nem o proprio dele"). Dois mecanismos de verdade
    /// (scratchpad\FsmDumpTool\out\crystalguardian_scene.txt, mines_18_scene.txt):
    ///  1. O laser DELE ('Range Check' -> Aim/Aim Left/Aim Right -> Shoot Antic -> Beam Antic -> Beam): filhos proprios
    ///     ('Beam', 'Beam Ball', 'Beam Impact', 'Beam Origin', 'Beam Point L/R'), todos dentro do preload e intactos no bundle
    ///     (BundleProbe 'fsm'), sem coordenada absoluta - nenhuma causa estatica achada; CrystalGuardianBeamProbe loga o estado
    ///     real do 'Beam' quando a FSM entra no 'Beam' (ou avisa se nunca entrar) pra fechar o diagnostico no jogo.
    ///  2. O laser DA SALA: no rugido, 'Roar Start' -> 'Lasers' (no ramo Godhome: 'Shoot Lasers?', a partir do 2o rugido) =
    ///     SendEventByName('LASER SHOOT', BroadcastAll). Quem escuta sao os 4 'Laser Turret Mega' no teto da cena BASE Mines_18
    ///     (o chefe vem do Mines_18_boss aditivo): FSM 'Laser Bug Mega' Idle --LASER SHOOT--> Aim (gira 160..200 graus) -> Beam
    ///     Antic -> Beam (o 'Beam Extender' do filho 'Beam' estica o raio ate o Terrain). Fora da sala nao existe nenhuma torre ->
    ///     o broadcast cai no vazio. Correcao (padrao ElderHuRings): copias pre-carregadas das 4 torres, na mesma distancia
    ///     horizontal do chefe que na sala e na mesma altura acima do chao (ou logo abaixo do teto da sala atual, se for mais
    ///     baixo), num grupo que some com o chefe; o broadcast da copia vira um envio so pra esse grupo (+ filhos), pra uma copia
    ///     nao disparar as torres de outra.</summary>
    internal static class CrystalGuardianTurrets
    {
        // Mines_18 (BundleProbe 'edges level260 10 50'): chao y=10 em x 0..64, teto y=25 em x 15..44; torres em y=24.62
        // (x 20.2 / 26.93 / 33.06 / 38.35), chefe (Mines_18_boss) em (29.69, 12.33).
        private const float OrigFloorY = 10f;
        private const float CeilingGap = 0.38f;

        public static void Attach(GameObject guardian, SpawnContext ctx)
        {
            try
            {
                PlayMakerFSM fsm = null;
                foreach (var f in guardian.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Beam Miner") { fsm = f; break; }
                if (fsm == null) return;
                guardian.AddComponent<CrystalGuardianBeamProbe>().Target = fsm;
                if (EncounterGroups.CrystalTurretPrefabs.Count == 0 || ctx.Prefab == null) return;
                SendEventByName shoot = null;
                foreach (var st in fsm.FsmStates)
                    if (st.Name == "Lasers")
                        foreach (var a in st.Actions)
                            if (a is SendEventByName s && s.sendEvent != null && s.sendEvent.Value == "LASER SHOOT") shoot = s;
                if (shoot == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Crystal Guardian: 'Lasers'/SendEventByName('LASER SHOOT') nao encontrado - sem lasers do teto");
                    return;
                }

                var group = new GameObject("Laser Turrets (Crystal Guardian spawnado)");
                group.AddComponent<DestroyWithOwner>().Owner = guardian;
                Vector3 origBoss = ctx.Prefab.transform.position; // clone em cache guarda a posicao de mundo original
                float ground = SpawnFixups.FindGroundY(ctx.HeroPos, out _, out _);
                float bossX = guardian.transform.position.x;
                var placed = new List<string>();
                foreach (var prefab in EncounterGroups.CrystalTurretPrefabs)
                {
                    var t = prefab.transform;
                    float x = bossX + (t.position.x - origBoss.x);
                    float above = t.position.y - OrigFloorY;
                    float y = ground + above;
                    // Teto da sala atual mais baixo: logo abaixo dele, como na sala original (senao a torre nasce dentro do
                    // Terrain e o raio do 'Beam Extender' bate em distancia 0).
                    var hit = Physics2D.Raycast(new Vector2(x, ground + 1f), Vector2.up, above + CeilingGap - 1f, SpawnFixups.TerrainMask);
                    if (hit.collider != null && hit.distance > 0f) y = Mathf.Min(y, hit.point.y - CeilingGap);
                    var turret = UnityEngine.Object.Instantiate(prefab, new Vector3(x, y, t.position.z), t.rotation, group.transform);
                    turret.name = prefab.name + " (Crystal Guardian spawnado)";
                    turret.SetActive(true);
                    placed.Add($"({x:0.#}, {y:0.#})");
                }
                shoot.eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = group }
                    },
                    sendToChildren = new FsmBool { Value = true },
                    excludeSelf = new FsmBool { Value = false }
                };
                Modding.Logger.Log($"[CreatureSpawner] Crystal Guardian: {placed.Count} lasers do teto em {string.Join(" ", placed.ToArray())} (chao y={ground:0.##})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CrystalGuardianTurrets: {ex}");
            }
        }
    }

    /// <summary>DIAGNOSTICO (so log): estado real do laser do proprio Crystal Guardian quando a FSM 'Beam Miner' entra no 'Beam'
    /// (as 3 primeiras vezes), ou um aviso se em 60 s ela nunca chegar la. Pode sair quando o laser dele for confirmado no jogo.</summary>
    internal sealed class CrystalGuardianBeamProbe : MonoBehaviour
    {
        public PlayMakerFSM Target;
        private string last;
        private int beams;
        private float startedAt;
        private bool warned;
        private readonly HashSet<string> visited = new HashSet<string>();

        private void Start() => startedAt = Time.time;

        private void Update()
        {
            if (Target == null) { Destroy(this); return; }
            if (beams == 0 && !warned && Time.time - startedAt > 60f)
            {
                warned = true;
                Modding.Logger.LogWarn($"[CreatureSpawner] Crystal Guardian: 60 s sem entrar no estado 'Beam' - estado atual '{Target.ActiveStateName}', visitados: {string.Join(", ", new List<string>(visited).ToArray())}");
            }
            string st = Target.ActiveStateName;
            if (st == last) return;
            last = st;
            if (st != null) visited.Add(st);
            if (st != "Beam") return;
            var beam = Target.FsmVariables.FindFsmGameObject("Beam")?.Value;
            var mr = beam != null ? beam.GetComponent<MeshRenderer>() : null;
            Modding.Logger.Log($"[CreatureSpawner] Crystal Guardian laser #{beams + 1}: Beam={(beam == null ? "NULL/destruido" : $"ativo={beam.activeInHierarchy} renderer={(mr != null && mr.enabled)} pos={beam.transform.position} escala={beam.transform.lossyScale} pai={(beam.transform.parent == null ? "-" : beam.transform.parent.name)}")} chefe={transform.position}");
            if (++beams >= 3) Destroy(this);
        }
    }

    /// <summary>Laser do PROPRIO Crystal Guardian (relatado: nao dispara; CrystalGuardianBeamProbe no jogo: "laser #1: Beam=NULL/destruido"
    /// com o chefe vivo). Causa (scratchpad\FsmDumpTool\out\crystalguardian_scene.txt + bundle de preload, BundleProbe 'fsm'):
    ///  - 'Beam', 'Beam Ball' e 'Beam Impact' tem a FSM-template 'destroy_if_gameobject_null': 'Check' = GetOwner -> GetParent
    ///    (UMA vez, na entrada do estado) -> var 'Parent' -> GameObjectIsNull($Parent, everyFrame) -> DESTROY -> DestroySelf.
    ///    Ou seja: "guarde quem e meu pai AGORA e se autodestrua quando ele sumir" - limpeza das pecas desparentadas quando o
    ///    chefe e destruido. E o unico Destroy que alcanca o 'Beam' (nada no mod destroi/desliga ele).
    ///  - A FSM do chefe ('Beam Miner') comeca em 'Pause Frame' (NextFrameEvent) -> 'Init' (SetParent null no Beam Impact/Beam
    ///    Ball/...) -> 'Deparents' (SetParent null no Beam). O 'Pause Frame' existe pra dar tempo das FSMs dos filhos entrarem no
    ///    'Check' (e guardarem o chefe como pai) ANTES do desparentamento. Na sala isso vale porque o Unity chama o Start de todos
    ///    os objetos da cena antes do 1o Update; num objeto ativado em jogo (nosso SetActive(true), chamado do OnGUI) o Unity nao
    ///    garante essa ordem (doc. de ordem de execucao: "this cannot be enforced when you instantiate an object during
    ///    gameplay"). Se o Update do chefe roda antes do Start da FSM do filho, o 'Check' ja encontra o filho na raiz:
    ///    GetParent = null -> GameObjectIsNull -> DestroySelf no 1o frame. O mesmo acontece se a peca, ja desparentada, for
    ///    desligada/religada (RestartOnEnable da FSM refaz o 'Check'). Resultado: var 'Beam' da 'Beam Miner' = null, os
    ///    SetPosition/ActivateGameObject/SetMeshRenderer/Tk2dPlayAnimation do ataque viram no-op -> sem laser (e sem Beam Ball/
    ///    Impact, que tem a mesma FSM). A 'Beam Extender' do 'Beam' tem o mesmo problema (GetParent -> FindChild('Beam Impact')
    ///    no 'Init'): pai null -> o impacto nao acompanha o raio a cada frame.
    /// Correcao (so nesta copia, antes do Start): nessas FSMs o GetParent/FindChild do estado inicial e desligado e as variaveis
    /// ja nascem com o que eles guardariam na ordem certa ('Parent' = o pai atual = o chefe; 'Beam Impact' = o filho do chefe).
    /// Comportamento identico ao da sala, independente da ordem dos Start: as pecas so se autodestroem quando o chefe e destruido.
    /// As torres do teto (CrystalGuardianTurrets) ficam em outro grupo, fora da hierarquia do chefe - nao sao tocadas.</summary>
    internal static class CrystalGuardianBeamFix
    {
        public static void Apply(GameObject guardian)
        {
            try
            {
                var done = new List<string>();
                foreach (var pm in guardian.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (pm == null || pm.gameObject == guardian || pm.transform.parent == null) continue;
                    if (pm.FsmName != "destroy_if_gameobject_null" && pm.FsmName != "Beam Extender") continue;
                    if (pm.Fsm == null || !pm.Fsm.Initialized)
                    {
                        Modding.Logger.LogWarn($"[CreatureSpawner] Crystal Guardian: FSM '{pm.FsmName}' de '{pm.name}' ainda nao inicializada - nao corrigida");
                        continue;
                    }
                    var parent = pm.transform.parent.gameObject;
                    var vars = pm.FsmVariables;
                    if (pm.FsmName == "destroy_if_gameobject_null")
                    {
                        var pv = vars.FindFsmGameObject("Parent");
                        int off = 0;
                        foreach (var st in pm.FsmStates)
                            if (st.Name == "Check")
                                foreach (var a in st.Actions)
                                    if (a is GetParent gp && gp.storeResult != null && gp.storeResult.Name == "Parent") { a.Enabled = false; off++; }
                        if (pv == null || off == 0)
                        {
                            Modding.Logger.LogWarn($"[CreatureSpawner] Crystal Guardian: '{pm.name}'/destroy_if_gameobject_null sem 'Check'/GetParent->Parent - nao corrigida");
                            continue;
                        }
                        pv.Value = parent;
                        done.Add($"{pm.name}.Parent={parent.name}");
                    }
                    else
                    {
                        var impact = parent.transform.Find("Beam Impact");
                        var pv = vars.FindFsmGameObject("Parent");
                        var iv = vars.FindFsmGameObject("Beam Impact");
                        if (impact == null || pv == null || iv == null) continue; // (Beam Extender de outro objeto)
                        int off = 0;
                        foreach (var st in pm.FsmStates)
                            if (st.Name == "Init")
                                foreach (var a in st.Actions)
                                {
                                    if (a is GetParent gp && gp.storeResult != null && gp.storeResult.Name == "Parent") { a.Enabled = false; off++; }
                                    else if (a is FindChild fc && fc.storeResult != null && fc.storeResult.Name == "Beam Impact") { a.Enabled = false; off++; }
                                }
                        if (off != 2)
                        {
                            Modding.Logger.LogWarn($"[CreatureSpawner] Crystal Guardian: '{pm.name}'/Beam Extender: {off} de 2 acoes do 'Init' achadas");
                            if (off == 0) continue;
                        }
                        pv.Value = parent;
                        iv.Value = impact.gameObject;
                        done.Add($"{pm.name}.BeamExtender(Parent={parent.name}, Beam Impact)");
                    }
                    if (pm.GetComponent<CrystalGuardianBeamWatch>() == null)
                        pm.gameObject.AddComponent<CrystalGuardianBeamWatch>().Owner = guardian;
                }
                Modding.Logger.Log($"[CreatureSpawner] Crystal Guardian: laser proprio protegido ({done.Count}): {string.Join(", ", done.ToArray())}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CrystalGuardianBeamFix: {ex}");
            }
        }
    }

    /// <summary>DIAGNOSTICO (so log): avisa se uma peca do laser do Crystal Guardian (Beam / Beam Ball / Beam Impact) for destruida
    /// com o chefe ainda vivo e ativo - nao deveria mais acontecer depois do CrystalGuardianBeamFix.</summary>
    internal sealed class CrystalGuardianBeamWatch : MonoBehaviour
    {
        public GameObject Owner;
        private bool logged;

        private void OnDestroy()
        {
            if (logged || Owner == null || !Owner.activeInHierarchy) return;
            logged = true;
            string st = "?";
            foreach (var f in Owner.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Beam Miner") { st = f.ActiveStateName; break; }
            Modding.Logger.LogWarn($"[CreatureSpawner] Crystal Guardian: '{name}' destruido com o chefe vivo (estado '{st}', t={Time.time:0.00})");
        }
    }

    /// <summary>Morte de um Dream Warrior spawnado: ultimas palavras + essencia (relatado com o Elder Hu: "nao funciona").
    /// O fluxo original (scratchpad\FsmDumpTool\out\hu_scene.txt, hu_corpse.txt, ghost_defeat_obj.txt; igual nos 7):
    ///  1. Morte -> corpse 'Ghost Death &lt;nome&gt;', FSM 'Control': Music -> Pause (0.75 s) -> Blow (2.5 s) -> 'End':
    ///     CreateObject('Ghost Death') = o orbe branco.
    ///  2. Orbe, FSM 'Shake': broadcast GHOST FIGHT END / DREAM GATE OPEN / DREAM AREA DISABLE, 2 s -> 'Tween':
    ///     FindGameObject(withTag 'Ghost Warrior NPC') -> GetPosition -> iTweenMoveTo (1 s) -> 'End': broadcast GHOST DEFEAT.
    ///  3. O 'Ghost Warrior NPC' (objeto RAIZ da sala, nao filho do guerreiro): 'Appear' GHOST DEFEAT -> Reappear
    ///     (fantasma ferido visivel, collider de conversa ligado); 'Conversation Control' -> Idle. Conversar ("Listen")
    ///     -> Convo Choice le &lt;nome&gt;Defeated == 1 -> 'Defeated' = DialogueBox &lt;GHOST&gt;_DEFEAT (as ultimas palavras)
    ///     -> Vanish -> 'Get' PlayerDataIntAdd dreamOrbs += Dream Orbs (Hu 100) -> 'Collected' SetPlayerDataInt = 2.
    ///     Dream Nail no fantasma ferido: DREAMNAIL -> Defeated? -> To Defeat -> Vanish -> mesma essencia.
    /// Fora da sala nativa o NPC nao existe: FindGameObjectWithTag devolve null, GetPosition(null) nao escreve nada, o orbe
    /// voa pra (0,0,0) do mundo (sai da tela) e o GHOST DEFEAT nao acha ninguem -> sem fantasma ferido, sem fala, sem
    /// essencia. (E mesmo com o NPC, ele dependia de &lt;nome&gt;Defeated == 1 no save, que a copia nao grava.)
    /// Correcao: o 'Ghost Warrior NPC' da sala nativa e pre-carregado (EncounterGroups.GhostNpcPrefabs); na morte, uma copia
    /// dele (inativa) fica no chao embaixo de onde o guerreiro morreu. O CreateObject do corpse e desligado e o orbe e criado
    /// aqui (mesmo prefab/posicao), ajustado ANTES do Start: alvo = a copia, broadcasts pra sala desligados. Quando o orbe
    /// chega, GHOST DEFEAT vai so pra copia. Na copia: leituras do &lt;nome&gt;Defeated viram "1" (var PD Int) e nenhuma escrita de
    /// progresso (=2, &lt;GHOST&gt;_encountered, mapa) acontece; a essencia (dreamOrbs) e a recompensa e e dada normalmente.</summary>
    internal sealed class GhostWarriorReward : MonoBehaviour
    {
        private const float CorpseTimeout = 10f;  // morte -> 'End' do corpse (original ~3.25 s)
        private const float NoCorpseDelay = 3.5f; // sem FSM do corpse: mais ou menos o mesmo tempo
        private const float OrbTimeout = 8f;      // orbe criado -> chegou (original 2 + 1 s)
        private const float NpcInitTimeout = 2f;
        private const float CleanupDelay = 3f;

        private string warrior;
        private GameObject npc, orb, orbPrefab;
        private PlayMakerFSM corpseFsm, orbFsm, npcConvo, npcAppear;
        private float startedAt, orbAt = -1f, npcAt = -1f, doneAt = -1f;
        private bool orbStarted, arrived, defeatSent;

        public static void Begin(string warriorName, GameObject npcPrefab, GameObject corpse, Vector3 deathPos, SpawnContext ctx)
        {
            try
            {
                var host = new GameObject($"CreatureSpawner Ghost Reward ({warriorName})");
                var r = host.AddComponent<GhostWarriorReward>();
                r.warrior = warriorName;
                r.startedAt = Time.time;
                if (corpse != null)
                    foreach (var f in corpse.GetComponents<PlayMakerFSM>())
                    {
                        if (f.FsmName != "Control" || f.Fsm == null || !f.Fsm.Initialized) continue;
                        foreach (var st in f.FsmStates)
                        {
                            if (st.Name != "End") continue;
                            foreach (var a in st.Actions)
                                if (a is CreateObject co && co.gameObject != null && co.gameObject.Value != null)
                                {
                                    r.orbPrefab = co.gameObject.Value;
                                    co.Enabled = false; // o orbe e criado (e ajustado) pelo GhostWarriorReward
                                    r.corpseFsm = f;
                                }
                        }
                    }
                r.npc = Instantiate(npcPrefab); // prefab em cache inativo -> copia inativa ate o orbe chegar
                r.npc.name = $"Ghost Warrior NPC ({warriorName} spawnado)";
                r.npc.transform.position = NpcPosition(r.npc, deathPos, ctx);
                Modding.Logger.Log($"[CreatureSpawner] '{warriorName}' morreu em {deathPos}: fantasma ferido (ultimas palavras/essencia) em " +
                                   $"{r.npc.transform.position}" + (r.orbPrefab == null ? " (corpse sem o orbe 'Ghost Death' - sem animacao do orbe)" : ""));
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GhostWarriorReward.Begin: {ex}");
            }
        }

        // No chao embaixo do ponto da morte (o guerreiro costuma morrer no ar). Pivo do NPC = altura do 'Base Glow' (o
        // brilho no chao, local y=-1.32 nos 7) acima do chao - no Fungus2_32 o NPC fica em y=3.26 com o chao em y=2.
        private static Vector3 NpcPosition(GameObject npc, Vector3 deathPos, SpawnContext ctx)
        {
            float above = 1.32f;
            var glow = npc.transform.Find("Base Glow");
            if (glow != null && -glow.localPosition.y > 0.2f && -glow.localPosition.y < 4f) above = -glow.localPosition.y;
            float z = npc.transform.position.z;
            var hit = Physics2D.Raycast(new Vector2(deathPos.x, deathPos.y), Vector2.down, SpawnFixups.GroundProbeDistance, SpawnFixups.TerrainMask);
            if (hit.collider != null) return new Vector3(deathPos.x, hit.point.y + above, z);
            float ground = SpawnFixups.FindGroundY(ctx.HeroPos, out _, out _);
            return new Vector3(ctx.HeroPos.x, ground + above, z);
        }

        private void Update()
        {
            if (npc == null) { Destroy(gameObject); return; } // troca de cena
            try { Step(); }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GhostWarriorReward ('{warrior}'): {ex}");
                Destroy(gameObject);
            }
        }

        private void Step()
        {
            float now = Time.time;
            if (!orbStarted)
            {
                if (orbPrefab != null && corpseFsm != null && corpseFsm.ActiveStateName == "End") SpawnOrb();
                else if (now - startedAt > (corpseFsm != null && orbPrefab != null ? CorpseTimeout : NoCorpseDelay))
                {
                    orbStarted = arrived = true; // sem orbe: o fantasma aparece direto
                    if (orbPrefab != null)
                        Modding.Logger.LogWarn($"[CreatureSpawner] '{warrior}': corpse nao chegou no 'End' em {CorpseTimeout}s - fantasma ferido sem o orbe");
                }
                else return;
            }
            if (!arrived)
            {
                string st = orb != null && orbFsm != null ? orbFsm.ActiveStateName : null;
                if (orb == null || st == "End" || now - orbAt > OrbTimeout) arrived = true;
                else if (st == "Tween") ActivateNpc(); // 1 s de voo: tempo pras FSMs da copia inicializarem
                if (!arrived) return;
            }
            ActivateNpc();
            if (!defeatSent)
            {
                // Init -> Set Convos (monta HU_DEFEAT etc.) -> Wounded? -> Idle tem que ter rodado antes do GHOST DEFEAT
                // (transicao global pro Idle; antes disso as falas ficariam vazias).
                bool ready = (npcConvo == null || npcConvo.ActiveStateName == "Idle") &&
                             (npcAppear == null || (npcAppear.ActiveStateName != "Pause" && !string.IsNullOrEmpty(npcAppear.ActiveStateName)));
                if (!ready && now - npcAt < NpcInitTimeout) return;
                if (npcAppear != null) npcAppear.SendEvent("GHOST DEFEAT");
                if (npcConvo != null) npcConvo.SendEvent("GHOST DEFEAT");
                defeatSent = true;
                Modding.Logger.Log($"[CreatureSpawner] '{warrior}': fantasma ferido pronto em {npc.transform.position} (conversar/Dream Nail = ultimas palavras + essencia)");
                return;
            }
            if (doneAt < 0f)
            {
                if (npcConvo != null && npcConvo.ActiveStateName == "End DN Kill") doneAt = now;
                return;
            }
            if (now - doneAt > CleanupDelay)
            {
                Destroy(npc);
                Destroy(gameObject);
            }
        }

        private void SpawnOrb()
        {
            orbStarted = true;
            orbAt = Time.time;
            // = CreateObject(spawnPoint $Self, position 0, rotation 0) do 'End' do corpse
            orb = Instantiate(orbPrefab, corpseFsm.transform.position, Quaternion.identity);
            foreach (var f in orb.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Shake") { orbFsm = f; break; }
            if (orbFsm == null || orbFsm.Fsm == null || !orbFsm.Fsm.Initialized)
            {
                Modding.Logger.LogWarn($"[CreatureSpawner] '{warrior}': orbe '{orbPrefab.name}' sem FSM 'Shake' - removido");
                Destroy(orb);
                orb = null;
                return;
            }
            // Ainda dentro do Instantiate -> Awake ja rodou (FSM inicializada), Start nao: nada disso executou ainda.
            foreach (var st in orbFsm.FsmStates)
                foreach (var a in st.Actions)
                {
                    if (a is SendEventByName s && s.eventTarget != null && s.eventTarget.target == FsmEventTarget.EventTarget.BroadcastAll)
                        a.Enabled = false; // GHOST FIGHT END / DREAM GATE OPEN / DREAM AREA DISABLE / GHOST DEFEAT pra sala inteira
                    else if (a is GGCheckIfBossScene)
                        a.Enabled = false; // ramo Godhome ('Disable particles') para ali e o fantasma nunca apareceria
                    else if (a is FindGameObject && st.Name == "Tween")
                        a.Enabled = false; // FindGameObjectWithTag('Ghost Warrior NPC'): null fora da sala -> voo pra (0,0,0)
                }
            var target = orbFsm.FsmVariables.FindFsmGameObject("Target");
            if (target != null) target.Value = npc; // GetPosition funciona com a copia ainda inativa
        }

        private void ActivateNpc()
        {
            if (npcAt >= 0f) return;
            npcAt = Time.time;
            npc.SetActive(true);
            PatchNpc();
            // Collider de conversa desligado ate o Reappear (SetCollider Owner true), senao o 'npc_control' mostraria o
            // "Listen" de um fantasma ainda invisivel.
            var col = npc.GetComponent<BoxCollider2D>();
            if (col != null) col.enabled = false;
        }

        // Logo depois do SetActive(true): Awake das FSMs ativas ja rodou, Start ainda nao.
        private void PatchNpc()
        {
            foreach (var f in npc.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f == null || f.Fsm == null || !f.Fsm.Initialized) continue; // filhos inativos (Wounded, Warp...): nada a mudar
                switch (f.FsmName)
                {
                    case "Conversation Control":
                        npcConvo = f;
                        foreach (var st in f.FsmStates)
                            foreach (var a in st.Actions)
                            {
                                if (a is GetPlayerDataInt || a is SetPlayerDataInt) a.Enabled = false; // <nome>Defeated: le via var, nunca grava
                                else if (a is PlayerDataBoolTest pt && pt.boolName != null && pt.boolName.Value == "hasDreamNail") a.Enabled = false;
                                else if (a is FindGameObject fg && fg.store != null && (fg.store.Name == "Warrior Folder" || fg.store.Name == "Inspect Region"))
                                    a.Enabled = false; // objetos DA SALA ('Warrior', 'Inspect Region Ghost'): achariam os da sala atual
                                else if (a is SetCollider sc && sc.gameObject != null && sc.gameObject.OwnerOption == OwnerDefaultOption.SpecifyGameObject &&
                                         sc.gameObject.GameObject != null && sc.gameObject.GameObject.Name == "Inspect Region")
                                    a.Enabled = false; // com a var null, SetCollider da NullReferenceException
                                else if (a is SetPlayerDataBool sb && sb.boolName != null && sb.boolName.UsesVariable && sb.boolName.Name == "Encountered Bool")
                                    a.Enabled = false; // <GHOST>_encountered (so no ramo de desafio, que nao e alcancado)
                            }
                        // Init (==2 -> INERT), Convo Choice/Defeated? (==1 -> DEFEATED) leem esta var; 'Wounded?' le 'Current State'
                        // (fica 0: o fantasma ferido so aparece no GHOST DEFEAT/Reappear, quando o orbe chega).
                        var pd = f.FsmVariables.FindFsmInt("PD Int");
                        if (pd != null) pd.Value = 1;
                        break;
                    case "Appear":
                        npcAppear = f;
                        foreach (var st in f.FsmStates)
                            foreach (var a in st.Actions)
                                if ((a is BoolTest && st.Name == "Away") || a is PlayerDataBoolTest)
                                    a.Enabled = false; // nunca aparece "de pe" (desafio) / sem Dream Nail -> Inert
                        break;
                    case "Set Pin":
                    case "FSM":
                        foreach (var st in f.FsmStates)
                            foreach (var a in st.Actions)
                                if (a is SetPlayerDataBool || a is PlayerDataBoolTest)
                                    a.Enabled = false; // '<nome>Pinned' (mapa) / No Eyes: desativa sem hasLantern
                        break;
                }
            }
        }
    }

    /// <summary>Absolute Radiance: o VAZIO da cutscene final nao aparecia (relatado: "nao aparece o vazio envolta dela").
    /// scratchpad\FsmDumpTool\out\radiance_control_v2.txt / rad_all_endbg.txt + PhysProbe\res\rad_abysspit.txt:
    ///  - 'Control' Init: GetParent(Owner) -> $Boss Control; FindChild($Boss Control, 'Abyss Pit') -> $Abyss Pit. A copia e RAIZ
    ///    (Boss Control = null) e o 'Abyss Pit' e IRMAO dela no GG_Radiance (fora do preload) -> var fica null.
    ///  - 'Abyss Tween' (depois de Knight Break / Ball Down): GetPosition($Abyss Pit) -> Abyss Pos; SetVector3XYZ x=56.5 y=152.38;
    ///    iTweenMoveTo($Abyss Pit, Abyss Pos, 1s) = o vazio SOBE ate a altura das plataformas finais (152.45), engolindo o Knight Ball
    ///    que afunda. Com a var null as 3 acoes viram no-op: o estado so espera 1.5s.
    ///  - O 'Abyss Pit' e o vazio em si: black_solid (sprite 0.531 u x escala 199x67 = ~106 x 35.5, topo 2.8 abaixo do pivo),
    ///    msk_generic_soft (degrade ate 1.4 acima do pivo), Pt Mist / Pt Surface (nevoa em loop) - todos na sorting layer 'Over'
    ///    (acima de Tiles/FG) - + 'Spike Collider' (DamageHero hazardType 2 = espinho -> hazard respawn) e Tendrils 1..3.
    /// Correcao: cada copia ganha o seu 'Abyss Pit' pre-carregado (EncounterGroups), INATIVO durante a luta - a var so e ligada
    /// quando a cutscene final comeca. Motivo: a luta tambem usa $Abyss Pit ('Rage1 Start' iTweenMoveTo (61.77,18.7), 'Tendrils1'
    /// UP, 'Abyss Antic'/'Abyss Up' iTweenMoveTo (61.77,30) = o vazio subindo atras do heroi na escalada que aqui e pulada), tudo com
    /// var null hoje = luta confirmada OK; ligar antes mudaria a luta (vazio na camada 'Over' cobrindo o heroi). O Spike Collider e
    /// desligado (so serve pra matar quem cai no vazio). O destino do 'Abyss Tween' (Abyss Pos) e deslocado pelo perfil (V3Vars +
    /// SubArenas da arena final): a superficie fica no chao real (-0.07), na mesma relacao com ela/chao que no original.
    ///
    /// FINAL DO P5 / SHADE LORD (ver o perfil "Absolute Radiance" em ArenaProfiles): na mesma hora (inicio da cutscene) tambem
    /// liga $Tendrils 2 (filho do Abyss Pit da copia - no Init o FindChild($Abyss Pit) deu null) e os outros irmaos dela que o
    /// ramo P5 usa (EncounterGroups.RadianceEndingParts: Shade_Lord, Shade Lord Hand, radiance_slashes, Final Explode). Esses 4
    /// sao instanciados INATIVOS - quem liga cada um e a propria FSM no estado certo (ActivateGameObject), como no original - e
    /// nunca sao movidos pela FSM. Cada um fica dentro de um "suporte" vazio na posicao = delta da sub-arena final (chao 152.45,
    /// a mesma das posicoes finais dela - SubArenas), com a posicao local = posicao de mundo original: assim ficam na mesma
    /// relacao com ela que na sala original e, se o Animator do Shade_Lord/radiance_slashes animar a posicao da raiz (valores
    /// absolutos da sala), a animacao fica relativa ao suporte deslocado em vez de jogar o objeto pra arena original. Os
    /// suportes (ativos) tem DestroyWithOwner + este OnDestroy: somem com a copia em qualquer saida (fim, encerramento na marra,
    /// copia desligada/destruida) - em especial a tela preta do 'Final Explode' nunca sobra.</summary>
    internal sealed class AbsoluteRadianceAbyss : MonoBehaviour
    {
        // Estados do 'Control' entre o golpe final e o 'Abyss Tween' ('Knight Break Antic' >= 0.25s, 'Ball Down' 0.75s).
        private static readonly string[] CutsceneStart = { "Final Impact", "Knight Break Antic", "Knight Break", "Ball Down" };
        private PlayMakerFSM control;
        private FsmGameObject abyssVar;
        private GameObject abyss;
        private SpawnContext ctx;
        private bool linked;
        private readonly List<GameObject> endingHolders = new List<GameObject>();

        public static void Attach(GameObject rad, SpawnContext ctx)
        {
            try
            {
                PlayMakerFSM control = null;
                foreach (var f in rad.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Control") { control = f; break; }
                if (control == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Absolute Radiance: FSM 'Control' nao encontrada - sem o vazio/Shade Lord na cutscene final");
                    return;
                }
                var w = rad.AddComponent<AbsoluteRadianceAbyss>();
                w.control = control; w.ctx = ctx;
                var prefab = EncounterGroups.RadianceAbyssPrefab;
                if (prefab == null) return;
                var v = control.FsmVariables.FindFsmGameObject("Abyss Pit");
                if (v == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Absolute Radiance: var 'Abyss Pit' nao encontrada - sem o vazio na cutscene final");
                    return;
                }
                var t = prefab.transform;
                // Inativo (o clone em cache e inativo): nada aparece nem roda durante a luta.
                var go = Instantiate(prefab, t.position + ctx.Delta3, t.rotation);
                go.name = "Abyss Pit (Absolute Radiance spawnada)";
                var spikes = go.transform.Find("Spike Collider");
                if (spikes != null) spikes.gameObject.SetActive(false);
                w.abyssVar = v; w.abyss = go;
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] AbsoluteRadianceAbyss: {ex}");
            }
        }

        private void Update()
        {
            if (linked || control == null) return;
            if (Array.IndexOf(CutsceneStart, control.ActiveStateName) < 0) return;
            linked = true;
            if (abyss != null)
            {
                try
                {
                    abyss.AddComponent<DestroyWithOwner>().Owner = gameObject;
                    abyss.SetActive(true);
                    // 'Ascend' Idle: SetPosition (61.77,12.1) space Self na raiz = mundo -> deslocado pelo delta (fica ~9 abaixo do chao,
                    // fora da tela, como na arena original); o 'Abyss Tween' sobe ele dali.
                    ctx.PatchHierarchy(abyss);
                    abyssVar.Value = abyss;
                    Modding.Logger.Log($"[CreatureSpawner] '{name}': cutscene final ('{control.ActiveStateName}') -> vazio (Abyss Pit) ligado em {abyss.transform.position}");
                    // 'Tendrils 2' (estado): SendEventByName UP pra $Tendrils 2 = os tentaculos do vazio sobem (so visual/audio).
                    var t2 = abyss.transform.Find("Tendrils 2");
                    var t2Var = control.FsmVariables.FindFsmGameObject("Tendrils 2");
                    if (t2 != null && t2Var != null) t2Var.Value = t2.gameObject;
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] AbsoluteRadianceAbyss.Update (vazio): {ex}");
                }
            }
            LinkShadeLordEnding();
        }

        /// <summary>Delta dos estados finais dela (sub-arena que contem 'Abyss Tween' no perfil): mesmo X, Y relativo ao chao 152.45.</summary>
        private Vector2 FinalArenaDelta()
        {
            var p = ctx.Profile;
            if (p != null)
                foreach (var (fsmName, floorY, states) in p.SubArenas)
                    if (fsmName == "Control" && Array.IndexOf(states, "Abyss Tween") >= 0)
                        return new Vector2(ctx.Delta.x, ctx.Delta.y + p.Ref.y - floorY);
            return ctx.Delta;
        }

        private void LinkShadeLordEnding()
        {
            var sd = FinalArenaDelta();
            foreach (var (varName, path) in EncounterGroups.RadianceEndingParts)
            {
                try
                {
                    if (!EncounterGroups.RadianceEndingPrefabs.TryGetValue(varName, out var prefab) || prefab == null) continue;
                    var v = control.FsmVariables.FindFsmGameObject(varName);
                    if (v == null)
                    {
                        Modding.Logger.LogWarn($"[CreatureSpawner] Absolute Radiance: var '{varName}' nao encontrada no 'Control' - '{path}' nao ligado");
                        continue;
                    }
                    var holder = new GameObject($"{varName} (Absolute Radiance spawnada)");
                    holder.transform.position = new Vector3(sd.x, sd.y, 0f);
                    holder.AddComponent<DestroyWithOwner>().Owner = gameObject;
                    endingHolders.Add(holder);
                    // Clone em cache e raiz inativa: posicao local = posicao de mundo original; continua INATIVO (a FSM liga).
                    var go = Instantiate(prefab, holder.transform, false);
                    go.name = path.Substring(path.LastIndexOf('/') + 1);
                    ctx.PatchHierarchy(go);
                    v.Value = go;
                    Modding.Logger.Log($"[CreatureSpawner] '{name}': final do Shade Lord -> '{varName}' ligado (inativo) em {go.transform.position}");
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] AbsoluteRadianceAbyss ('{varName}'): {ex}");
                }
            }
        }

        // Copia removida (fim da cutscene, encerramento na marra, morte, troca de cena): o vazio e os objetos do final do Shade
        // Lord (incluindo a tela preta do 'Final Explode') vao junto, usados ou nao.
        private void OnDestroy()
        {
            if (abyss != null) Destroy(abyss);
            foreach (var h in endingHolders) if (h != null) Destroy(h);
        }
    }

    /// <summary>Absolute Radiance / Radiance: o LASER QUE VARRE a arena de um lado ao outro nunca aparecia (relatado: "nao dispara").
    /// scratchpad\FsmDumpTool\out\rad_all_endbg.txt (GG_Radiance) e rad1\scene_all.txt (Dream_Final_Boss) - identico nas duas:
    ///  - 'Attack Choices' A1 Choice / A2 Choice (SendRandomEventV3, pesos 0.75 cada) -> BEAM SWEEP L/R -> estados 'Beam Sweep L'/'Beam
    ///    Sweep R' (arena 1) ou 'Beam Sweep L 2'/'Beam Sweep R 2' (arena 2) = Wait 2 + SendEventByName('BEAM SWEEP L'|'R'|'L2'|'R2')
    ///    com eventTarget target4 = FsmEventTarget.EventTarget.BroadcastAll (decomp_pm\Fsm.cs Event(FsmEventTarget,...): 0 Self,
    ///    1 GameObject, 2 GameObjectFSM, 3 FSMComponent, 4 BroadcastAll).
    ///  - Quem escuta e o 'Boss Control/Beam Sweeper', IRMAO dela na sala (fora do preload): FSM 'Control' Idle --BEAM SWEEP*--> 'Beam
    ///    Sweep L'/'R'/'L 2'/'R 2' = SetPosition MUNDO (79,16)/(43,16)/(89,16)/(32.6,16) -> SpawnObjectFromGlobalPoolOverTime (segmento
    ///    do raio, rot 0,0,90 = vertical pra cima, a cada 0.075 s na posicao de $Self) -> iTweenMoveBy (-39|+39|-50|+50, 0) em 4|4|5|5 s
    ///    (RELATIVO) -> FINISHED -> Idle. Fora da sala original nao existe Beam Sweeper nenhum -> o broadcast cai no vazio: ela faz a
    ///    pausa de 2 s do estado e volta pra escolha, sem laser (o ChildOfLight tem o dele, mas tira as transicoes do Idle).
    /// Correcao (padrao CrystalGuardianTurrets/ElderHuRings): cada copia ganha o SEU Beam Sweeper pre-carregado (EncounterGroups),
    /// como objeto RAIZ (nao filho dela - o raio fica em coordenadas da arena enquanto ela teleporta, como no original) que some com
    /// ela (DestroyWithOwner). Os SetPosition de mundo do sweeper da copia sao deslocados pelo delta da arena: 'Beam Sweep L/R' pelo da
    /// arena 1 (Ref.y), 'Beam Sweep L 2/R 2' pelo da arena 2 (chao da sub-arena do perfil que contem 'Arena 2 Start' = 33.26, o mesmo
    /// dos Tele 1..10 / pentes tipo 6..8) - mesma relacao com ela e com o chao que no original (raio nasce 5.2 / 17.26 abaixo do chao
    /// e sobe). Os SendEventByName desses 4 estados da copia viram envio SO pro sweeper dela (em vez de broadcast): uma copia nao
    /// dispara o sweeper de outra, nem o de uma luta real, nem o do ChildOfLight.
    /// Nada do perfil (DisableActions/Redirects/SubArenas/PhaseEvents/HeroCutsceneStates/...) e alterado e nenhum estado da cutscene
    /// final usa o laser ('Attack Choices' para em A2 End no 'Ascend Tele' - ARENA 2 END - bem antes do golpe final).</summary>
    internal static class RadianceBeamSweeper
    {
        private static readonly string[] ChoiceStates = { "Beam Sweep L", "Beam Sweep R", "Beam Sweep L 2", "Beam Sweep R 2" };
        private static readonly string[] Arena2SweepStates = { "Beam Sweep L 2", "Beam Sweep R 2" };

        public static void Attach(GameObject rad, SpawnContext ctx, GameObject prefab)
        {
            try
            {
                if (prefab == null || ctx == null || ctx.Profile == null) return; // (CacheExtras ja avisou se faltou o preload)
                PlayMakerFSM choices = null;
                foreach (var f in rad.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Attack Choices") { choices = f; break; }
                if (choices == null || choices.Fsm == null)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': FSM 'Attack Choices' nao encontrada - sem o laser que varre a arena");
                    return;
                }
                var sends = new List<SendEventByName>();
                foreach (var st in choices.FsmStates)
                    if (Array.IndexOf(ChoiceStates, st.Name) >= 0)
                        foreach (var a in st.Actions)
                            if (a is SendEventByName s && s.sendEvent != null && s.sendEvent.Value != null &&
                                s.sendEvent.Value.StartsWith("BEAM SWEEP", StringComparison.Ordinal))
                                sends.Add(s);
                if (sends.Count == 0)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': SendEventByName('BEAM SWEEP*') nao encontrado em 'Attack Choices' - sem o laser que varre a arena");
                    return;
                }

                var t = prefab.transform;
                var go = UnityEngine.Object.Instantiate(prefab, t.position + ctx.Delta3, t.rotation);
                go.name = $"Beam Sweeper ({ctx.Name} spawnada)";
                go.AddComponent<DestroyWithOwner>().Owner = rad;
                go.SetActive(true); // Awake = FSM inicializada; o 'Init' dela so roda no Start, depois do ajuste abaixo
                PlayMakerFSM ctrl = null;
                foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Control") { ctrl = f; break; }
                if (ctrl == null || ctrl.Fsm == null || !ctrl.Fsm.Initialized)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': FSM 'Control' do Beam Sweeper nao encontrada/inicializada - sem o laser que varre a arena");
                    UnityEngine.Object.Destroy(go);
                    return; // (os SendEventByName continuam broadcast, como antes)
                }

                Vector2 d1 = ctx.Delta, d2 = Arena2Delta(ctx);
                var placed = new List<string>();
                foreach (var st in ctrl.FsmStates)
                {
                    var d = Array.IndexOf(Arena2SweepStates, st.Name) >= 0 ? d2 : d1;
                    foreach (var a in st.Actions)
                    {
                        if (!(a is SetPosition sp) || sp.space != Space.World || sp.gameObject == null ||
                            sp.gameObject.OwnerOption != OwnerDefaultOption.UseOwner) continue;
                        if (IsConst(sp.x)) sp.x.Value += d.x;
                        if (IsConst(sp.y)) sp.y.Value += d.y;
                        if (sp.vector != null && !sp.vector.IsNone && !sp.vector.UsesVariable) sp.vector.Value += new Vector3(d.x, d.y, 0f);
                        placed.Add($"'{st.Name}' ({(IsConst(sp.x) ? sp.x.Value : float.NaN):0.##},{(IsConst(sp.y) ? sp.y.Value : float.NaN):0.##})");
                    }
                }

                foreach (var s in sends)
                    s.eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            GameObject = new FsmGameObject { Value = go }
                        },
                        sendToChildren = new FsmBool { Value = false },
                        excludeSelf = new FsmBool { Value = false }
                    };
                Modding.Logger.Log($"[CreatureSpawner] '{ctx.Name}': Beam Sweeper proprio ({sends.Count} envios BEAM SWEEP redirecionados pra ele); " +
                                   $"inicio das varreduras (arena 1 delta {d1}, arena 2 delta {d2}): {string.Join(", ", placed.ToArray())}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] RadianceBeamSweeper: {ex}");
            }
        }

        private static bool IsConst(FsmFloat f) => f != null && !f.IsNone && !f.UsesVariable;

        /// <summary>Delta da arena 2 (plataformas): mesmo X, Y relativo ao chao da sub-arena do perfil que contem 'Arena 2 Start'
        /// (o mesmo que o FsmArenaShift usa nos Tele 1..10 dela). So le o perfil.</summary>
        private static Vector2 Arena2Delta(SpawnContext ctx)
        {
            var p = ctx.Profile;
            foreach (var (fsmName, floorY, states) in p.SubArenas)
                if (fsmName == "Control" && Array.IndexOf(states, "Arena 2 Start") >= 0)
                    return new Vector2(ctx.Delta.x, ctx.Delta.y + p.Ref.y - floorY);
            Modding.Logger.LogWarn($"[CreatureSpawner] '{ctx.Name}': sub-arena com 'Arena 2 Start' nao achada no perfil - laser da arena 2 usa o delta da arena 1");
            return ctx.Delta;
        }
    }

    /// <summary>Some junto com o dono (copia spawnada destruida/desativada - morte, troca de cena).</summary>
    internal sealed class DestroyWithOwner : MonoBehaviour
    {
        public GameObject Owner;

        private void Update()
        {
            if (Owner == null || !Owner.activeInHierarchy) Destroy(gameObject);
        }
    }
}
