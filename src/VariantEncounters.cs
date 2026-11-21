using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Pecas de encontro das variantes que antes ficavam escondidas no painel (ver "Manual rename" em EnemyLocations.cs e
    // ArenaProfilesVariants.cs). Mesmo esquema do EncounterGroups/CrystalGuardianTurrets/FlukeMotherCage (SpawnFixups.cs), num
    // arquivo proprio pra nao mexer no que ja funciona.
    internal static class VariantEncounters
    {
        // Enraged Guardian (Mines_32, cena propria - nao aditiva): os 4 lasers do teto que ele dispara no rugido sao objetos RAIZ
        // da mesma cena (FsmDumpTool\out\crystalguardian2_scene.txt): 'Laser Turret Mega' (22.01), '(1)' 27.34, '(2)' 32.81,
        // '(3)' 37.89, todos em y=24.62; FSM 'Laser Bug Mega' Idle --LASER SHOOT--> Aim -> Beam Antic -> Beam (igual ao Mines_18).
        // O Mines_32 ja e pre-carregado por causa dele, entao isto nao abre cena nova.
        public const string EnragedTurretScene = "Mines_32";
        public static readonly string[] EnragedTurretPaths =
            { "Laser Turret Mega", "Laser Turret Mega (1)", "Laser Turret Mega (2)", "Laser Turret Mega (3)" };
        public static readonly List<GameObject> EnragedTurretPrefabs = new List<GameObject>();

        // Uumuu (normal E Godhome): o ataque "multizap" (bolas eletricas que aparecem PARADAS em pontos fixos da arena) e o objeto
        // RAIZ 'Mega Jellyfish Multizaps' (tag 'Boss Attack') da sala - Fungus3_archive_02 (cena base do Fungus3_archive_02_boss) e
        // GG_Uumuu tem copias IDENTICAS (mesma raiz (53.21,119.76), mesmos 'Pattern 1' (23 bolas) / 'Pattern 2' (23), mesmas
        // posicoes: FsmDumpTool\out\multizap\archive02_scene.txt x out\collide\uumuu_gg.txt, 141 linhas iguais) -> UM preload
        // serve as duas variantes. O Fungus3_archive_02 ja e pre-carregado ('Jellyfish Baby Inert'): nao abre cena nova.
        public const string MultizapsScene = "Fungus3_archive_02";
        public const string MultizapsPath = "Mega Jellyfish Multizaps";
        public static GameObject MultizapsPrefab;

        // Hornet Sentinel: as farpas ('Hornet Barb', presas no ar em pontos aleatorios de uma area FIXA da arena) vem do objeto RAIZ
        // 'Barb Region' da cena BASE Deepnest_East_Hornet (o chefe vem do Deepnest_East_Hornet_boss aditivo) - ver HornetSentinelBarbs.
        public const string BarbScene = "Deepnest_East_Hornet";
        public const string BarbPath = "Barb Region";
        public static GameObject BarbRegionPrefab;

        public static IEnumerable<(string, string)> ExtraPreloads()
        {
            foreach (var p in EnragedTurretPaths) yield return (EnragedTurretScene, p);
            yield return (MultizapsScene, MultizapsPath);
            yield return (BarbScene, BarbPath);
        }

        public static void CacheExtras(Dictionary<string, Dictionary<string, GameObject>> pre, Action<string> log)
        {
            MultizapsPrefab = CacheOne(pre, MultizapsScene, MultizapsPath, "os dois Uumuus ficam sem o ataque multizap", log);
            BarbRegionPrefab = CacheOne(pre, BarbScene, BarbPath, "Hornet Sentinel fica sem as farpas", log);
            EnragedTurretPrefabs.Clear();
            foreach (var p in EnragedTurretPaths)
            {
                try
                {
                    if (pre.TryGetValue(EnragedTurretScene, out var objs) && objs.TryGetValue(p, out var orig) && orig != null)
                    {
                        var clone = UnityEngine.Object.Instantiate(orig);
                        UnityEngine.Object.DontDestroyOnLoad(clone);
                        clone.SetActive(false);
                        var cleared = SceneLinkSanitizer.Sanitize(clone);
                        if (cleared.Count > 0) log($"'{EnragedTurretScene}/{p}': {cleared.Count} referencia(s) externa(s) zerada(s): {string.Join(", ", cleared.ToArray())}");
                        EnragedTurretPrefabs.Add(clone);
                    }
                    else log($"VariantEncounters: '{EnragedTurretScene}/{p}' nao foi pre-carregado - Enraged Guardian fica sem esse laser do teto");
                }
                catch (Exception ex)
                {
                    log($"VariantEncounters: erro ao pre-carregar '{EnragedTurretScene}/{p}': {ex}");
                }
            }
        }

        private static GameObject CacheOne(Dictionary<string, Dictionary<string, GameObject>> pre, string scene, string path, string missingNote, Action<string> log)
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
                log($"VariantEncounters: '{scene}/{path}' nao foi pre-carregado - {missingNote}");
            }
            catch (Exception ex)
            {
                log($"VariantEncounters: erro ao pre-carregar '{scene}/{path}': {ex}");
            }
            return null;
        }
    }

    /// <summary>Lasers do teto do Enraged Guardian. A FSM 'Beam Miner' dele, a partir do 2o rugido ('Roar Start' -> GG -> 'Laser
    /// Shoot?' -> 'Laser Shoot'), faz SendEventByName('LASER SHOOT', BroadcastAll) - fora do Mines_32 ninguem escuta (ou pior: as
    /// torres de uma copia do Crystal Guardian escutariam). Mesmo esquema do CrystalGuardianTurrets (codigo dele intocado): copias
    /// das 4 torres do Mines_32 num grupo que some com o chefe, e o envio vira so pra esse grupo (+ filhos). Posicao: X da sala
    /// original + o MESMO delta X da arena do perfil (o alcance de pulo dele foi deslocado por esse delta, entao as torres ficam na
    /// mesma relacao com a "arena" que no original); Y = mesma altura acima do chao (24.62 - 10), ou logo abaixo do teto da sala
    /// atual se for mais baixo (senao o raio do 'Beam Extender' nasce dentro do Terrain).</summary>
    internal static class EnragedGuardianTurrets
    {
        private const float OrigFloorY = 10f;     // Mines_32: EdgeCollider2D y=10 de x=4 a 47 (teto 25 de x=19 a 41)
        private const float CeilingGap = 0.38f;   // torres em 24.62 = 0.38 abaixo do teto 25

        public static void Attach(GameObject guardian, SpawnContext ctx)
        {
            try
            {
                if (VariantEncounters.EnragedTurretPrefabs.Count == 0 || ctx.Profile == null) return;
                PlayMakerFSM fsm = null;
                foreach (var f in guardian.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Beam Miner") { fsm = f; break; }
                if (fsm == null) return;
                SendEventByName shoot = null;
                foreach (var st in fsm.FsmStates)
                    if (st.Name == "Laser Shoot")
                        foreach (var a in st.Actions)
                            if (a is SendEventByName s && s.sendEvent != null && s.sendEvent.Value == "LASER SHOOT") shoot = s;
                if (shoot == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Enraged Guardian: 'Laser Shoot'/SendEventByName('LASER SHOOT') nao encontrado - sem lasers do teto");
                    return;
                }

                var group = new GameObject("Laser Turrets (Enraged Guardian spawnado)");
                group.AddComponent<DestroyWithOwner>().Owner = guardian;
                float ground = ctx.GroundY;
                var placed = new List<string>();
                foreach (var prefab in VariantEncounters.EnragedTurretPrefabs)
                {
                    var t = prefab.transform;
                    float x = t.position.x + ctx.Delta.x;
                    float above = t.position.y - OrigFloorY;
                    float y = ground + above;
                    var hit = Physics2D.Raycast(new Vector2(x, ground + 1f), Vector2.up, above + CeilingGap - 1f, SpawnFixups.TerrainMask);
                    if (hit.collider != null && hit.distance > 0f) y = Mathf.Min(y, hit.point.y - CeilingGap);
                    var turret = UnityEngine.Object.Instantiate(prefab, new Vector3(x, y, t.position.z), t.rotation, group.transform);
                    turret.name = prefab.name + " (Enraged Guardian spawnado)";
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
                Modding.Logger.Log($"[CreatureSpawner] Enraged Guardian: {placed.Count} lasers do teto em {string.Join(" ", placed.ToArray())} (chao y={ground:0.##})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] EnragedGuardianTurrets: {ex}");
            }
        }
    }

    /// <summary>Uumuu (Godhome): sem Quirrel, ele so fica vulneravel com EXPLODE ('Explode' = SetInvincible false), que vem da
    /// explosao 'Gas Explosion Uumuu' (FSM 'Uumuu Event', sharedassets156) quando o nucleo de um Ooma morto ('Corpse Jellyfish':
    /// Trigger2dEvent tag 'Boss' -> 'Instant Explosion') encosta no filho 'Detonator' (tag Boss) dele. Os Oomas vem do proprio
    /// Uumuu: estado 'Roar' (escolha SPAWN) faz SendEventToRegister('SPAWN') -> 'Jellyfish Spawner' da sala GG_Uumuu (EventRegister,
    /// FSM 'Spawn': 2 Oomas em x RandomFloat(39.4..65.72), y 98.7/88.7 = DEBAIXO do acido da arena original) - objeto da sala, nao
    /// vem no preload. Sem ele a copia fica invencivel pra sempre.
    /// Aqui: a cada entrada no 'Roar' da copia, 2 Oomas (o mesmo prefab/fluxo do botao "Ooma" do painel, que ja esta corrigido e
    /// testado - o nucleo dele e o mesmo 'Corpse Jellyfish' de sharedassets156) nascem dos dois lados do Uumuu, um pouco acima do
    /// chao (as posicoes originais, debaixo do acido, cairiam dentro do chao da sala atual). Limite de MaxAlive vivos.
    /// (So isso NAO bastava - o nucleo desses Oomas explodia com a explosao COMUM fora do Godhome, sem EXPLODE: ver GodhomeOomaCore.)</summary>
    internal sealed class GodhomeUumuuOomas : MonoBehaviour
    {
        private const string OomaPanelName = "Ooma";
        private const int PerRoar = 2;
        private const int MaxAlive = 6;
        private PlayMakerFSM fsm;
        private SpawnContext ctx;
        private string last;
        private readonly List<GameObject> spawned = new List<GameObject>();

        public static void Attach(GameObject uumuu, SpawnContext ctx)
        {
            foreach (var f in uumuu.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Mega Jellyfish")
                {
                    var c = uumuu.AddComponent<GodhomeUumuuOomas>();
                    c.fsm = f; c.ctx = ctx;
                    return;
                }
            Modding.Logger.LogWarn("[CreatureSpawner] Uumuu (Godhome): FSM 'Mega Jellyfish' nao encontrada - sem Oomas");
        }

        private void Update()
        {
            if (fsm == null) return;
            string st = fsm.ActiveStateName;
            if (st == last) return;
            last = st;
            if (st == "Roar") SpawnOomas();
        }

        private void SpawnOomas()
        {
            try
            {
                var mod = CreatureSpawnerMod.Instance;
                GameObject prefab = null;
                if (mod == null || !mod.CachedPrefabs.TryGetValue(OomaPanelName, out prefab) || prefab == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Uumuu (Godhome): Ooma nao esta no cache do painel - nenhum Ooma invocado");
                    return;
                }
                var hero = HeroController.instance;
                if (hero == null) return;
                spawned.RemoveAll(o => o == null || !o.activeInHierarchy);
                float ground = ctx != null && ctx.GroundFound ? ctx.GroundY : transform.position.y - 6f;
                for (int i = 0; i < PerRoar && spawned.Count < MaxAlive; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    var pos = new Vector3(transform.position.x + side * UnityEngine.Random.Range(5f, 9f), ground + 3.5f, prefab.transform.position.z);
                    // Mesmo fluxo do CreatureSpawnerPanel.SpawnNearPlayer (Ooma nao tem perfil: so DormantBossFix - que remove o
                    // PersistentBoolItem dele - SpawnLeash, SpawnDeathWatch etc.).
                    var c = SpawnFixups.Prepare(OomaPanelName, prefab, hero.transform.position, out _);
                    var o = Instantiate(prefab, pos, Quaternion.identity);
                    o.AddComponent<SpawnLeash>();
                    DormantBossFix.Apply(o);
                    SpawnFixups.BeforeActivate(o, c);
                    o.SetActive(true);
                    SpawnFixups.AfterActivate(o, c);
                    spawned.Add(o);
                    var hm = o.GetComponent<HealthManager>();
                    if (hm != null) { var ooma = o; hm.OnDeath += () => GodhomeOomaCore.OnOomaDied(ooma); }
                }
                Modding.Logger.Log($"[CreatureSpawner] Uumuu (Godhome): 'Roar' -> Oomas vivos {spawned.Count}/{MaxAlive}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GodhomeUumuuOomas: {ex}");
            }
        }
    }

    /// <summary>Por que os Oomas do GodhomeUumuuOomas NUNCA deixavam o Uumuu (Godhome) vulneravel (relatado). A cadeia real
    /// (FsmDumpTool\out\collide\sharedassets156.assets.txt + uumuu_gg.txt, decompilado):
    ///  1. Ooma morre -> EnemyDeathEffectsBubble.EmitCorpse solta o 'Corpse Jellyfish' (filho pre-instanciado no Start).
    ///  2. FSM 'corpse' 'State 1': Trigger2dEvent tag 'Boss' -> EXPLODE -> 'Instant Explosion' (Gas Explosion Uumuu na hora - so se
    ///     o corpo ja nascer encostado no Uumuu); senao qualquer Collision2dEvent -> COLLIDE -> 'Explode' = CreateObject('Lil
    ///     Jellyfish') = o NUCLEO, que persegue o heroi (ChaseObjectV2 speed 80).
    ///  3. 'Lil Jelly' 'Chase': encostou no heroi (HeroBox) ou em qualquer collider (ex. o filho solido 'Detonator' do Uumuu,
    ///     layer 15, raio 2.94) ou levou golpe -> ZERO HP -> 'Die': GGCheckIfBossScene (= BossSceneController.Instance != null).
    ///     Godhome -> GG SCENE -> 'Uumuu Explosion' = SpawnObjectFromGlobalPool('Gas Explosion Uumuu', sharedassets156).
    ///     FORA do Godhome -> nenhum evento -> spawna 'Gas Explosion Recycle L' (resources.assets #5650 - BundleProbe 'scene').
    ///  4. 'Gas Explosion Uumuu' (trigger raio 3 x escala 1.6 = 4.8) tem a FSM 'Uumuu Event': qualquer collider tocado -> HIT ->
    ///     SendEventByName EXPLODE pro GameObject dele -> o Uumuu (FSM 'Mega Jellyfish' Idle/Attack Antic/Zapping/Multizap/...
    ///     --EXPLODE--> 'Explode' = SetInvincible false). A 'Gas Explosion Recycle L' so tem 'Explosion Control' + 'damages_enemy'
    ///     (TAKE DAMAGE/TakeDamage - o Uumuu invencivel ignora): NENHUM EXPLODE.
    /// O elo quebrado e o 3: o nucleo dos Oomas spawnados nasce FORA de uma cena de chefe -> explosao comum -> invencivel pra
    /// sempre. (O caminho 2 'Instant Explosion' funciona em qualquer sala, mas so se o Ooma morrer ja encostado nele.)
    /// O 'Lil Jellyfish' e criado pelo CreateObject do corpo, fora de qualquer hierarquia que o PatchHierarchy ve (o
    /// ForceBossSceneBranch generico nunca chega nele). Correcao: na morte de um Ooma do GodhomeUumuuOomas, o 'Explode' do corpo
    /// DESSE Ooma ganha, logo depois do CreateObject('Lil Jellyfish'), uma acao que forca o ramo Godhome no nucleo recem-criado
    /// (o mesmo ForceBossSceneBranch das copias). So os nucleos dos Oomas invocados pelo Uumuu (Godhome) sao tocados - o "Ooma" do
    /// painel e os Oomas da sala continuam com a explosao comum.</summary>
    internal static class GodhomeOomaCore
    {
        private static readonly System.Reflection.FieldInfo FCorpse = typeof(EnemyDeathEffects).GetField("corpse",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        public static void OnOomaDied(GameObject ooma)
        {
            try
            {
                if (ooma == null) return;
                int hooked = 0;
                foreach (var edf in ooma.GetComponents<EnemyDeathEffects>())
                {
                    // HealthManager.Die emite o corpse (SetParent(null) + SetActive(true)) ANTES do OnDeath (ver SpawnDeathWatch);
                    // a FSM dele ja foi inicializada no Awake (PreInstantiate, com o Ooma ativo) e so comeca no Start.
                    var corpse = FCorpse?.GetValue(edf) as GameObject;
                    if (corpse == null) continue;
                    foreach (var f in corpse.GetComponents<PlayMakerFSM>())
                    {
                        if (f.FsmName != "corpse" || f.Fsm == null) continue;
                        foreach (var st in f.FsmStates)
                        {
                            if (st.Name != "Explode") continue;
                            var old = st.Actions;
                            for (int i = 0; i < old.Length; i++)
                            {
                                if (!(old[i] is CreateObject co) || co.gameObject == null || co.gameObject.Value == null ||
                                    co.gameObject.Value.name != "Lil Jellyfish") continue;
                                var actions = new FsmStateAction[old.Length + 1];
                                Array.Copy(old, 0, actions, 0, i + 1);
                                actions[i + 1] = new LilJellyGodhomeBranch { Source = co };
                                Array.Copy(old, i + 1, actions, i + 2, old.Length - i - 1);
                                st.Actions = actions;
                                hooked++;
                                break;
                            }
                        }
                    }
                }
                if (hooked == 0)
                    Modding.Logger.LogWarn($"[CreatureSpawner] Uumuu (Godhome): corpo do Ooma '{ooma.name}' sem 'corpse'/'Explode'/CreateObject('Lil Jellyfish') - o nucleo dele nao vai deixar o Uumuu vulneravel");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GodhomeOomaCore: {ex}");
            }
        }
    }

    /// <summary>Acao inserida no 'Explode' do corpo de um Ooma do Uumuu (Godhome), logo depois do CreateObject('Lil Jellyfish')
    /// (que grava a instancia criada no proprio storeObject - decompilado: storeObject.Value = Instantiate(...)). O nucleo acabou de
    /// nascer ativo (Awake ja inicializou a FSM 'Lil Jelly'; o Start ainda nao) -> forca o ramo Godhome do 'Die'.</summary>
    internal sealed class LilJellyGodhomeBranch : FsmStateAction
    {
        public CreateObject Source;

        public override void OnEnter()
        {
            try
            {
                var core = Source != null && Source.storeObject != null ? Source.storeObject.Value : null;
                if (core != null)
                {
                    int n = 0;
                    foreach (var f in core.GetComponentsInChildren<PlayMakerFSM>(true))
                        if (f != null && f.Fsm != null) n += FsmArenaShift.ForceBossSceneBranch(f);
                    Modding.Logger.Log($"[CreatureSpawner] Uumuu (Godhome): nucleo de Ooma em {core.transform.position} -> ramo Godhome forcado " +
                                       $"({n} GGCheckIfBossScene; explode como 'Gas Explosion Uumuu' = EXPLODE no Uumuu)");
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] LilJellyGodhomeBranch: {ex}");
            }
            Finish();
        }
    }

    /// <summary>Multizap dos DOIS Uumuus (relatado: as bolas eletricas que aparecem PARADAS em pontos fixos nunca apareciam - as que
    /// perseguem o heroi, 'Zapping'/'Gen' = SpawnObjectFromGlobalPool no heroi, ja funcionavam). FSM 'Mega Jellyfish' (igual nas
    /// duas cenas - FsmDumpTool\out\uumuu_scene.txt e out\collide\uumuu_gg.txt):
    ///  - 'Init': FindGameObject(withTag 'Boss Attack') -> $Multizaps; FindChild($Multizaps, 'Pattern 1'/'Pattern 2').
    ///  - 'Choice' --MULTIZAP--> 'Pattern Choice': GetRandomChild($Multizaps) -> $Current Pattern -> 'Multizap':
    ///    ActivateAllChildren($Current Pattern) + Wait 2 -> Attack Recover.
    ///  - Cada 'Mega Jelly Multizap (N)' (inativo, CircleCollider2D trigger r 1.73 + DamageHero + MegaJellyZap type=MultiZap) se
    ///    desenha sozinho no OnEnable (decompilado MultiZapSequence: 0..0.5 s, 'Zap Antic' 0.8 s, collider/'Zap' 1 s, 'Zap End',
    ///    SetActive(false)) - todas as referencias sao filhos dele (PhysProbe out\multizap\archive02_multizaps_phys.txt).
    /// O 'Mega Jellyfish Multizaps' e um objeto RAIZ da sala (nao vem no preload do Uumuu). Fora dela o FindGameObjectWithTag devolve
    /// null (ou QUALQUER outro 'Boss Attack' da sala atual - ex. uma farpa da Hornet) -> o ataque roda a animacao e nada aparece.
    /// Correcao (padrao FlukeMotherCage/ElderHuRings): cada copia ganha o seu 'Mega Jellyfish Multizaps' pre-carregado na posicao
    /// original + o delta da arena (os pontos fixos ficam na mesma relacao com a arena que no original), SEM a tag (nenhum
    /// FindGameObjectWithTag/GetTagCount/FindClosest('Boss Attack') de outra coisa acha o nosso), o FindGameObject do 'Init' e
    /// desligado e $Multizaps aponta pra copia (o 'Init' so roda no Start, depois deste AfterActivate; os FindChild originais
    /// acham os 'Pattern 1/2' nela). Some com o Uumuu (DestroyWithOwner).</summary>
    internal static class UumuuMultizaps
    {
        public static void Attach(GameObject uumuu, SpawnContext ctx)
        {
            try
            {
                var prefab = VariantEncounters.MultizapsPrefab;
                if (prefab == null || ctx == null) return;
                PlayMakerFSM fsm = null;
                foreach (var f in uumuu.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Mega Jellyfish") { fsm = f; break; }
                var v = fsm != null ? fsm.FsmVariables.FindFsmGameObject("Multizaps") : null;
                if (v == null)
                {
                    Modding.Logger.LogWarn($"[CreatureSpawner] {ctx.Name}: FSM 'Mega Jellyfish' ou a var 'Multizaps' nao encontrada - sem multizap");
                    return;
                }
                int off = 0;
                foreach (var st in fsm.FsmStates)
                    if (st.Name == "Init")
                        foreach (var a in st.Actions)
                            if (a is FindGameObject fg && fg.store != null && fg.store.Name == "Multizaps") { a.Enabled = false; off++; }
                if (off == 0) Modding.Logger.LogWarn($"[CreatureSpawner] {ctx.Name}: FindGameObject('Boss Attack') do 'Init' nao encontrado");

                var t = prefab.transform;
                var group = UnityEngine.Object.Instantiate(prefab, t.position + ctx.Delta3, t.rotation);
                group.name = $"Mega Jellyfish Multizaps ({ctx.Name} spawnado)";
                group.tag = "Untagged";
                group.AddComponent<DestroyWithOwner>().Owner = uumuu;
                group.SetActive(true); // os 'Mega Jelly Multizap (N)' continuam inativos ate o ActivateAllChildren
                v.Value = group;
                Modding.Logger.Log($"[CreatureSpawner] {ctx.Name}: multizaps proprios em {group.transform.position} (original {t.position}, delta {ctx.Delta})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] UumuuMultizaps: {ex}");
            }
        }
    }

    /// <summary>Farpas da Hornet Sentinel (relatado: "espinhos presos em pontos fixos" que nunca apareciam - mesma classe do multizap).
    /// FSM 'Control' (FsmDumpTool\out\collide\hornet2_scene.txt): 'Escalation' (HP &lt;= 480, de 700) liga Can Barb -> 'Barb?'
    /// (GetTagCount 'Boss Attack' &lt; 5, 20%) -> 'Barb Antic' -> 'Barb Throw' = SendEventByName('SPAWN3', BroadcastAll) + Wait 0.54
    /// -> 'Barb Recover'. Quem escuta e o objeto RAIZ 'Barb Region' da cena BASE Deepnest_East_Hornet (out\multizap\
    /// deepnest_east_hornet_scene.txt; o chefe vem do _boss aditivo): FSM 'Spawn Barbs' Init = area do proprio BoxCollider2D
    /// (trigger 18.85 x 5.2 em (26.56,31.18), layer 0) a partir da propria posicao; SPAWN3 -> 3x SpawnObjectFromGlobalPool('Hornet
    /// Barb', sharedassets324 #37) em pontos aleatorios dessa area (>= 3 entre si) -> BARB READY (BroadcastAll) -> as farpas
    /// esticam os espinhos e ficam paradas ('Barb'). As farpas somem com HORNET KILLED (global; o 'Blow' do corpse dela manda).
    /// Fora da sala o SPAWN3 cai no vazio: so a animacao. Correcao (padrao CrystalGuardianTurrets): cada copia ganha a sua 'Barb
    /// Region' na posicao original + delta da arena (area 1.6..6.8 acima do chao, +-9.4 em x, como no original) e o SPAWN3 dela vai
    /// SO pra essa regiao (uma copia nao dispara a regiao de outra). Bundle conferido (BundleProbe repack full v9 'fsm'/'analyze'/
    /// 'collide'): a ref do 'Hornet Barb' continua no arquivo externo sharedassets324, nenhuma ref externa/colisao.</summary>
    internal sealed class HornetSentinelBarbs : MonoBehaviour
    {
        private GameObject owner;
        private HealthManager ownerHm;
        private FsmGameObject barbVar;
        private readonly List<GameObject> barbs = new List<GameObject>();

        public static void Attach(GameObject hornet, SpawnContext ctx)
        {
            try
            {
                var prefab = VariantEncounters.BarbRegionPrefab;
                if (prefab == null || ctx == null) return;
                PlayMakerFSM fsm = null;
                foreach (var f in hornet.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Control") { fsm = f; break; }
                if (fsm == null) return;
                SendEventByName spawn = null;
                foreach (var st in fsm.FsmStates)
                    if (st.Name == "Barb Throw")
                        foreach (var a in st.Actions)
                            if (a is SendEventByName s && s.sendEvent != null && s.sendEvent.Value == "SPAWN3") spawn = s;
                if (spawn == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Hornet Sentinel: 'Barb Throw'/SendEventByName('SPAWN3') nao encontrado - sem farpas");
                    return;
                }

                var t = prefab.transform;
                var region = Instantiate(prefab, t.position + ctx.Delta3, t.rotation);
                region.name = "Barb Region (Hornet Sentinel spawnada)";
                var keeper = region.AddComponent<HornetSentinelBarbs>();
                keeper.owner = hornet;
                keeper.ownerHm = hornet.GetComponent<HealthManager>();
                region.SetActive(true); // 'Spawn Barbs' Init (no Start) le a posicao/BoxCollider2D DESTA copia
                foreach (var f in region.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Spawn Barbs") keeper.barbVar = f.FsmVariables.FindFsmGameObject("Barb Obj");
                spawn.eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObject,
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = region }
                    },
                    sendToChildren = new FsmBool { Value = false },
                    excludeSelf = new FsmBool { Value = false }
                };
                Modding.Logger.Log($"[CreatureSpawner] Hornet Sentinel: Barb Region propria em {region.transform.position} (original {t.position}, delta {ctx.Delta})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] HornetSentinelBarbs: {ex}");
            }
        }

        private void LateUpdate()
        {
            // Farpas DESTA regiao (o pool global reaproveita instancias: uma reciclada sai da lista e nao e mais "nossa").
            barbs.RemoveAll(b => b == null || !b.activeInHierarchy);
            var b0 = barbVar != null ? barbVar.Value : null;
            if (b0 != null && b0.activeInHierarchy && !barbs.Contains(b0)) barbs.Add(b0);
            if (owner != null && owner.activeInHierarchy) return;
            // Hornet morta: o 'Blow' do corpse manda HORNET KILLED (global) e as farpas caem sozinhas, como no original. Copia
            // removida de outro jeito (sem morte): manda HORNET KILLED so pras farpas desta regiao (nao broadcast - acertaria a
            // agulha/farpas de outra Hornet).
            if (ownerHm == null || !ownerHm.isDead)
                foreach (var b in barbs)
                    foreach (var f in b.GetComponents<PlayMakerFSM>())
                        if (f.FsmName == "Control") f.SendEvent("HORNET KILLED");
            Destroy(gameObject);
        }
    }
}
