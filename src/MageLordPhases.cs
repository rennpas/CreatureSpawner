using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CreatureSpawner
{
    // Soul Master ('Mage Lord' + 'Mage Lord Phase2', Ruins1_24_boss) e Soul Tyrant ('Dream Mage Lord' + 'Dream Mage Lord Phase2',
    // Dream_02_Mage_Lord) como UMA luta so, com a troca de fase do jogo. Evidencia: FsmDumpTool (endbg) -> out\soulmaster_p1p2.txt,
    // soultyrant_scene.txt, gg_soul_master_all.txt, sa102_full.txt (corpses da fase 1 + 'Corpse Mage Lord 2'), sa396_full.txt
    // ('Corpse Dream Mage Lord 2'); renderers = DormantAudit 'renderers' no bundle repacked_full_v10; chao = DormantAudit 'edges'.
    //
    // Como a troca de fase funciona no jogo (NAO e limite de HP - e a MORTE da fase 1):
    //  - A fase 1 morre de verdade (HealthManager zero HP -> EnemyDeathEffects emite o corpse). Nenhuma FSM da fase 1 referencia a
    //    fase 2. Quem chama a fase 2 e o CORPSE, por BROADCAST (SendEventByName target4 = BroadcastAll) do evento PHASE 2, que a fase 2
    //    (ja presente na sala, escondida: MeshRenderer serializado off, invencivel) espera no estado 'Init' da FSM 'Mage Lord 2'.
    //  - Soul Master, corpse 'Corpse Mage Lord 1' (sharedassets102 #49), FSM 'Corpse': Init (DISSIPATE broadcast, snapshot 'Silent')
    //    -> Godseeker Set -> Quick Death? (GG -> Stun Init) -> Stun -> Encountered? -> loop 'Get Dest'/'Valid?'/'Teleport'/'Pause'
    //    (o "chorando e teleportando" famoso; ~25 teleportes com espera caindo de 0.6 a 0.1 s, dentro de X Min..X Max 8.34..33.8 /
    //    Y Min..Y Max 31.61..36.99 ABSOLUTOS) -> 'End' (SetPosition $End Pos (20.64,33.39) ABSOLUTO, flash, RumblingMed off) ->
    //    GGCheckIfBossScene: Godhome -> 'Phase 2' (PHASE 2 broadcast); historia -> 'Fake Quake' (QUAKE FAKE APPEAR pro chao quebravel
    //    da sala, o heroi cai pro andar de baixo e o 'Init' da fase 2 acorda sozinho com Hero Y < 11).
    //  - Soul Tyrant, corpse 'Corpse Dream Mage Lord 1' (sharedassets102 #56), FSM 'Get Quake': Start (ApplyMusicCue 'None',
    //    SetPosition (22,32.97) ABSOLUTO) -> Get -> GGCheckIfBossScene: Godhome -> 'GG Death' (1.25 s, anim de morte) ->
    //    'Start Phase 2' (PHASE 2 broadcast) -> End -> Event; historia -> Mage Lord Up/Down/Break (cutscene do chao quebrando, mexe no
    //    heroi: FreezeMoment/RegainControl). No Dream_02 o PHASE 2 da historia vem do trigger 'Stat5 Phase 2' (heroi caiu la embaixo).
    //  - As copias usam o ramo Godhome (FsmArenaShift.ForceBossSceneBranch), entao o corpse da fase 1 JA chega no broadcast de PHASE 2
    //    hoje - so que nao existe fase 2 nenhuma na sala (era outro botao do painel). Resultado: a luta acabava no corpse.
    //  - Por que as fases 2 sozinhas nao bastavam: 'Mage Lord Phase2' tinha perfil proprio (ArenaProfilesVariants, WakeEvents PHASE 2)
    //    mas era um botao separado. 'Dream Mage Lord Phase2' NAO tinha nada: o 'Init' dela so sai com PHASE 2 (sem o teste de Hero Y
    //    da versao normal) -> invisivel e invencivel pra sempre, e com coordenadas absolutas do andar de baixo do Dream_02.
    //
    // Correcao:
    //  - As fases 2 continuam pre-carregadas (mesmas entradas de EnemyLocations), mas NAO entram no painel (CachePhase2): ficam aqui,
    //    escondidas. O painel mostra so "Soul Master" e "Soul Tyrant" (rename manual de 'Dream Mage Lord').
    //  - Perfis das fases 1 (antes NAO tinham - teleportavam pras coordenadas da arena original e a coleira puxava de volta) e do corpse
    //    delas (mesmo SpawnContext, via SpawnDeathWatch), perfil da fase 2 do Soul Tyrant (a do Soul Master reaproveita o perfil
    //    "Soul Master (Phase 2)" que ja existia, sem mudanca nenhuma).
    //  - O broadcast PHASE 2 do corpse e desligado (acordaria qualquer coisa da sala atual que espere PHASE 2, ex. outra copia). No lugar,
    //    PhasedMageLordEncounter observa o corpse e, quando a FSM dele chega no estado do broadcast, spawna a fase 2 pelo MESMO caminho do
    //    painel (Prepare/BeforeActivate/AfterActivate, como WatcherKnightsEncounter.SpawnNext) -> perfil, coleira, WakeEvents PHASE 2,
    //    SpawnDeathWatch (corpse da fase 2 sem gravar mageLordDefeated) valem igual. A partir dai e a sequencia original da fase 2:
    //    Soul Master: Arrive Pause 2 s -> Music -> Tele Quake; Soul Tyrant: Wait 3 s -> Music -> Tele Quake.
    //  - Nenhuma etapa mexe no controle do heroi (o ramo Godhome dos corpses nao toca no heroi). Rede de seguranca: se o corpse sumir ou
    //    nao chegar no estado em CorpseTimeout s, a fase 2 nasce mesmo assim (e o RumblingMed que o 'Get Dest' liga e desligado).
    internal static class MageLordPhases
    {
        internal sealed class Pair
        {
            public string Phase1Name;      // nome do painel da fase 1 (ctx.Name)
            public string Phase2Key;       // chave da fase 2 em EnemyLocations.All (pre-carregada, fora do painel)
            public string Phase2Name;      // DisplayName da fase 2 = chave do ArenaProfile dela
            public string CorpseFsm;       // FSM do corpse da fase 1 que faz o broadcast PHASE 2
            public string[] CorpseTriggerStates; // estado do broadcast (e os seguintes, caso o polling pule um estado de 1 frame)
            public bool HideSilhouette;    // a fase 2 tem um filho 'Silhouette' ATIVO e visivel (Dream) que ninguem desliga
        }

        public static readonly Pair[] Pairs =
        {
            new Pair
            {
                Phase1Name = "Soul Master", Phase2Key = "Mage Lord Phase2", Phase2Name = "Soul Master (Phase 2)",
                CorpseFsm = "Corpse", CorpseTriggerStates = new[] { "Phase 2" },
            },
            new Pair
            {
                Phase1Name = "Soul Tyrant", Phase2Key = "Dream Mage Lord Phase2", Phase2Name = "Soul Tyrant (Phase 2)",
                CorpseFsm = "Get Quake", CorpseTriggerStates = new[] { "Start Phase 2", "End", "Event" },
                HideSilhouette = true,
            },
        };

        /// <summary>Clones em cache das fases 2 (inativos, DontDestroyOnLoad), por Phase2Name. Nao aparecem no painel.</summary>
        public static readonly Dictionary<string, GameObject> Phase2Prefabs = new Dictionary<string, GameObject>();

        /// <summary>Chamado pelo CreatureSpawnerMod.Initialize pra cada entrada pre-carregada: se for uma fase 2, guarda aqui (e nao no
        /// painel) e devolve true.</summary>
        public static bool CachePhase2(string key, GameObject clone)
        {
            foreach (var p in Pairs)
            {
                if (p.Phase2Key != key) continue;
                if (!Phase2Prefabs.ContainsKey(p.Phase2Name)) Phase2Prefabs[p.Phase2Name] = clone;
                return true;
            }
            return false;
        }

        public static void AddProfiles(Dictionary<string, ArenaProfile> d)
        {
            // Fase 1 (Soul Master e Soul Tyrant: MESMA FSM 'Mage Lord' e as mesmas variaveis de arena - o andar de CIMA do Ruins1_24 /
            // Dream_02, que e tambem a arena do GG_Soul_Master/GG_Soul_Tyrant). Init -> GGCheckIfBossScene (Godhome forcado) ->
            // Soul Master: GG Pause (0.75 s) -> 'Set Entry Point GG' (Tele X = 20 CONSTANTE, Tele Y = $Top Y); Soul Tyrant: Sleep ->
            // 'GG Enter' (Tele X = 25 CONSTANTE) -> Teleport In (SetPosition absoluto) -> Music (pulado pelo ramo GG) -> Roar -> luta.
            //  - Coordenadas absolutas (vars): Left/Right X 6.98/34.75 (Tele Away/Shoot RandomFloat, HS/Charge param em Self X > Right X,
            //    FloatClamp do Tele Quake), Hero Mid X 20.18, Top/Bot Y 35.85/31.07, Quake Y 38, Ground Y 31.05 (Quake Down/Land),
            //    Knight Quake Y Max 35.41, Shockwave Y 27.82/27.77 (SetPosition y da onda). 'Tele X' das entradas = SetFloatXVars.
            //  - Corpse (mesmo SpawnContext): Soul Master 'Corpse' X/Y Min/Max + End Pos (V3); Soul Tyrant 'Get Quake' Start SetPosition
            //    (22,32.97) constante (deslocada sozinha).
            //  - Chao: EdgeCollider2D y=28 (GG_Soul_Master/GG_Soul_Tyrant x=5..37; Ruins1_24/Dream_02 andar de cima), onda em 27.8.
            //    Centro X = meio de Left/Right X = 20.87.
            //  - Quake: Tele Quake sobe ate Quake Y (10 acima do chao) e o Quake Antic mais 2 -> LeashUp 12.
            ArenaProfile Phase1(params (string, string, string)[] corpseOff) => new ArenaProfile
            {
                Ref = new Vector2(20.87f, 28.0f),
                XVars = new[] { "Left X", "Right X", "Hero Mid X", "X Min", "X Max" },
                YVars = new[] { "Ground Y", "Top Y", "Bot Y", "Quake Y", "Knight Quake Y Max", "Shockwave Y", "Y Min", "Y Max" },
                V3Vars = new[] { "End Pos" },
                SetFloatXVars = new[] { "Tele X" },
                LeashUp = 12f,
                DisableActions = corpseOff,
            };

            d["Soul Master"] = Phase1(
                ("Corpse", "Init", "TransitionToAudioSnapshot"),      // 'Silent' (#3875, resources.assets) - efeito global
                ("Corpse", "Phase 2", "SendEventByName:PHASE 2"));    // broadcast -> PhasedMageLordEncounter spawna a fase 2

            d["Soul Tyrant"] = Phase1(
                ("Get Quake", "Start", "ApplyMusicCue"),              // MusicCue 'None' (#19414) - cortaria a musica da sala
                ("Get Quake", "Start Phase 2", "SendEventByName:PHASE 2"));

            // Dream_02_Mage_Lord ('Dream Mage Lord Phase2'). FSM 'Mage Lord 2': Pause -> 'Set GG' -> 'Init', que so sai com PHASE 2 (sem o
            // FloatCompare de Hero Y da versao do Soul Master) -> Wait 3 s -> Music -> Tele Quake -> luta. Mesma estrutura/arena do
            // perfil "Soul Master (Phase 2)" (ArenaProfilesVariants): o andar de BAIXO (EdgeCollider2D y=9 de x=8 a 42 no Dream_02,
            // DormantAudit 'edges level396'), Left/Right X 10.76/40.18, Ground Y 11.34, Top/Bot Y 16.24/12.24, Quake Y 19.5, Orb
            // 10.1..40.5 / 10.4..20.08; 'Set GG' (constantes da arena GG_Soul_Tyrant, com o ramo Godhome forcado) desligado; 'Tele Out'
            // SetPosition y=-10 constante (deslocada sozinha) -> LeashDown 23.
            //  - Corpse 'Corpse Dream Mage Lord 2' (sharedassets396 #6), FSM 'corpse': 'Music' (snapshot 'Silent') e 'Soul Breath'
            //    (mageLordDreamDefeated, AwardAchievement, 'Dream Orb Get') ja sao pulados pelo ramo Godhome; 'Init' SetPlayerDataBool
            //    mageLordDefeated=true SEM checagem -> desligado. (StoryRecord_defeated e vazio no decompilado.)
            d["Soul Tyrant (Phase 2)"] = new ArenaProfile
            {
                Ref = new Vector2(25.47f, 9.0f),
                XVars = new[] { "Left X", "Right X", "Orb Min X", "Orb Max X", "Hero Mid X" },
                YVars = new[] { "Ground Y", "Top Y", "Bot Y", "Quake Y", "Orb Min Y", "Orb Max Y", "Knight Quake Y Max", "Shockwave Y" },
                LeashDown = 23f,
                WakeEvents = new[] { ("Mage Lord 2", "Init", "PHASE 2") },
                DisableActions = new[]
                {
                    ("Mage Lord 2", "Set GG", "SetFloatValue"),
                    ("corpse", "Init", "SetPlayerDataBool"),
                },
            };
        }

        /// <summary>SpawnFixups.AfterActivate: fase 1 recem-ativada (FSMs acordadas e deslocadas, antes do Start delas).</summary>
        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            if (clone == null || ctx == null) return;
            foreach (var p in Pairs)
            {
                if (p.Phase1Name != ctx.Name) continue;
                try
                {
                    // 'Set Entry Point GG' (Soul Master) / 'Quick Start' liga o filho 'CameraLockArea B' (desparentado no Init): uma
                    // CameraLockArea com os limites de camera ABSOLUTOS da sala original -> a camera iria pra longe ao encostar nela.
                    // Remove so o componente (o estado tambem desliga a Silhouette, que precisa continuar).
                    foreach (var cla in clone.GetComponentsInChildren<CameraLockArea>(true))
                    {
                        foreach (var col in cla.GetComponents<Collider2D>()) col.enabled = false;
                        UnityEngine.Object.Destroy(cla);
                    }
                    PhasedMageLordEncounter.Begin(clone, ctx, p);
                }
                catch (Exception ex)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] MageLordPhases '{ctx.Name}': {ex}");
                }
                return;
            }
        }
    }

    /// <summary>Faz o papel da fase 2 "ja presente na sala" do jogo: quando a fase 1 morre, acompanha o corpse dela e, no momento em que
    /// a FSM do corpse chega no estado que faria o broadcast PHASE 2 (desligado no perfil), spawna a fase 2 perto do heroi pelo caminho
    /// normal do painel. Objeto da cena atual (troca de cena = some junto com tudo). Nunca toca no heroi.</summary>
    internal sealed class PhasedMageLordEncounter : MonoBehaviour
    {
        private const float CorpseTimeout = 30f;      // Soul Master leva ~11 s do zero HP ate o 'Phase 2'; Soul Tyrant ~1.3 s
        private const float MissingCorpseDelay = 2f;  // sem corpse (ou corpse sumiu): espera isso e spawna a fase 2 mesmo assim

        private static readonly FieldInfo FCorpse =
            typeof(EnemyDeathEffects).GetField("corpse", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private MageLordPhases.Pair pair;
        private SpawnContext ctx;
        private GameObject phase1;
        private HealthManager hm;
        private bool died, done, hadCorpse;
        private float diedAt;
        private GameObject corpse;
        private PlayMakerFSM corpseFsm;

        public static void Begin(GameObject phase1, SpawnContext ctx, MageLordPhases.Pair pair)
        {
            var hm = phase1.GetComponent<HealthManager>();
            if (hm == null)
            {
                Modding.Logger.LogWarn($"[CreatureSpawner] {pair.Phase1Name}: sem HealthManager - sem troca de fase");
                return;
            }
            if (!MageLordPhases.Phase2Prefabs.TryGetValue(pair.Phase2Name, out var p2) || p2 == null)
                Modding.Logger.LogWarn($"[CreatureSpawner] {pair.Phase1Name}: '{pair.Phase2Key}' nao foi pre-carregado - a luta acaba na fase 1");
            var host = new GameObject($"CreatureSpawner {pair.Phase1Name} Phases");
            var enc = host.AddComponent<PhasedMageLordEncounter>();
            enc.pair = pair;
            enc.ctx = ctx;
            enc.phase1 = phase1;
            enc.hm = hm;
            hm.OnDeath += enc.OnPhase1Died;
        }

        // HealthManager.Die: EnemyDeathEffects ja emitiu o corpse (ativo, FSM ainda sem Start) quando o OnDeath chega.
        private void OnPhase1Died()
        {
            if (died || this == null) return;
            died = true;
            diedAt = Time.time;
            try
            {
                if (hm != null)
                    foreach (var edf in hm.GetComponents<EnemyDeathEffects>())
                    {
                        var c = FCorpse?.GetValue(edf) as GameObject;
                        if (c != null) { corpse = c; break; }
                    }
                if (corpse != null)
                {
                    hadCorpse = true;
                    ctx.PatchHierarchy(corpse); // ja feito pelo SpawnDeathWatch (idempotente) - garante a ordem
                    foreach (var f in corpse.GetComponentsInChildren<PlayMakerFSM>(true))
                        if (f.FsmName == pair.CorpseFsm) { corpseFsm = f; break; }
                }
                Modding.Logger.Log($"[CreatureSpawner] {pair.Phase1Name}: fase 1 derrotada -> corpse '{(corpse != null ? corpse.name : "null")}' " +
                                   $"FSM '{pair.CorpseFsm}' {(corpseFsm != null ? "ok" : "NAO encontrada")}; fase 2 quando ela chegar em '{pair.CorpseTriggerStates[0]}'");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] {pair.Phase1Name} (morte da fase 1): {ex}");
            }
        }

        private void OnDestroy()
        {
            if (hm != null) hm.OnDeath -= OnPhase1Died;
            // Troca de cena / morte do heroi no meio do choro-teleporte do corpse: o 'End' (que desliga o RumblingMed ligado a cada
            // 'Get Dest') nunca roda e a camera (persistente) tremeria pra sempre na sala seguinte.
            if (died && !done) HeroRestore.StopCutsceneRumble();
        }

        private void Update()
        {
            if (done) return;
            try
            {
                if (!died)
                {
                    // OnDeath perdido: HealthManager.Die com AttackTypes.Splatter destroi o objeto e RETORNA antes do SendDeathEvent
                    // (decompilado) - sem corpse. Troca de cena nao cai aqui (este objeto e da cena e some junto).
                    if (phase1 == null || hm == null || hm.isDead) OnPhase1Died();
                    return;
                }
                float since = Time.time - diedAt;
                bool corpseAlive = corpseFsm != null && corpseFsm.gameObject != null && corpseFsm.gameObject.activeInHierarchy;
                if (corpseAlive)
                {
                    if (Array.IndexOf(pair.CorpseTriggerStates, corpseFsm.ActiveStateName) >= 0)
                    {
                        SpawnPhase2(null);
                        return;
                    }
                    if (since > CorpseTimeout)
                        SpawnPhase2($"FSM '{pair.CorpseFsm}' do corpse parada em '{corpseFsm.ActiveStateName}' ha {since:0.#} s");
                    return;
                }
                if (since > MissingCorpseDelay)
                    SpawnPhase2(hadCorpse ? "o corpse da fase 1 sumiu antes do fim" : "fase 1 morreu sem corpse/FSM do corpse");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] {pair?.Phase1Name} (troca de fase): {ex}");
                done = true;
                Destroy(gameObject);
            }
        }

        private void SpawnPhase2(string forcedWhy)
        {
            done = true;
            try
            {
                if (forcedWhy != null)
                {
                    // O loop 'Get Dest' do corpse do Soul Master liga CameraShake.RumblingMed a cada teleporte e so o 'End' desliga.
                    HeroRestore.StopCutsceneRumble();
                    Modding.Logger.LogWarn($"[CreatureSpawner] {pair.Phase1Name}: {forcedWhy} -> fase 2 spawnada mesmo assim");
                }
                if (!MageLordPhases.Phase2Prefabs.TryGetValue(pair.Phase2Name, out var prefab) || prefab == null) return;
                var hero = HeroController.instance;
                if (hero == null) return;

                // Mesmo caminho do botao do painel (CreatureSpawnerPanel.SpawnNearPlayer / WatcherKnightsEncounter.SpawnNext).
                var ctx2 = SpawnFixups.Prepare(pair.Phase2Name, prefab, hero.transform.position, out var pos);
                var p2 = Instantiate(prefab, pos, Quaternion.identity);
                if (pair.HideSilhouette)
                {
                    var s = p2.transform.Find("Silhouette");
                    if (s != null) s.gameObject.SetActive(false);
                }
                p2.AddComponent<SpawnLeash>();
                DormantBossFix.Apply(p2);
                SpawnFixups.BeforeActivate(p2, ctx2); // coleira, SpawnDeathWatch, WakeEvents PHASE 2 no 'Init'
                p2.SetActive(true);
                SpawnFixups.AfterActivate(p2, ctx2);
                Modding.Logger.Log($"[CreatureSpawner] {pair.Phase1Name}: fase 2 ('{pair.Phase2Name}') spawnada em {pos}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] {pair.Phase1Name} (spawn da fase 2): {ex}");
            }
            finally
            {
                Destroy(gameObject);
            }
        }
    }
}
