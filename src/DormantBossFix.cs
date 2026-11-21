using UnityEngine;

namespace CreatureSpawner
{
    // Chefes "adormecidos": na sala original eles comecam escondidos e so aparecem quando o controlador da
    // arena (o "Battle Scene" da sala, que NAO vem junto no preload) manda o evento de inicio da luta.
    // Verificado no bundle de preload (scratchpad\BundleProbe):
    //   - False Knight New: MeshRenderer serializado DESLIGADO; FSM 'FalseyControl': Init -> Dormant, que
    //     espera "BATTLE START" -> 'Start Fall', o UNICO estado que liga o MeshRenderer. A transicao global
    //     STUN tira ele do Dormant sem passar pelo 'Start Fall' -> luta invisivel, so a cabeca (filho 'Head',
    //     renderer proprio) aparece no stun. Exatamente o que o usuario viu.
    //   - Hive Knight: FSM 'Control': Init -> Variant -> 'Sleep' (SetMeshRenderer false + SetCollider false),
    //     espera "WAKE" -> Wake -> 'Fall' (liga o renderer, cai ate a variavel 'Ground Y' = chao da arena
    //     ORIGINAL) -> intro -> 'Activate' (liga collider/fisica) -> Idle.
    // Nao da pra simplesmente mandar o evento da intro: a do False Knight termina com um BROADCAST global
    // "KILL ALL ENEMIES" (mataria inimigos da sala atual) e a do Hive Knight so termina quando ele cai ate a
    // altura do chao da arena original. Entao: False Knight -> so liga o renderer (o Dormant nao mexe nele);
    // Hive Knight -> quando a FSM chega no 'Sleep', liga o renderer e pula pro 'Activate' (o estado que a
    // intro usa pra soltar ele pra luta), pulando so a animacao de entrada.
    public class DormantBossFix : MonoBehaviour
    {
        private PlayMakerFSM fsm;
        private string waitState, jumpState;
        private float deadline;

        public static void Apply(GameObject clone)
        {
            DisableProgressGates(clone);
            foreach (var pm in clone.GetComponents<PlayMakerFSM>())
            {
                if (pm.FsmName == "FalseyControl")
                {
                    var mr = clone.GetComponent<MeshRenderer>();
                    if (mr != null) mr.enabled = true;
                }
                else if (pm.FsmName == "Control" && clone.name.StartsWith("Hive Knight") &&
                         HasState(pm, "Sleep") && HasState(pm, "Activate"))
                {
                    var fix = clone.AddComponent<DormantBossFix>();
                    fix.fsm = pm;
                    fix.waitState = "Sleep";
                    fix.jumpState = "Activate";
                }
                // Vengefly King ("Giant Buzzer"): duas FSMs separadas da checagem de progresso da
                // historia do Zote se autodestroem no 1o frame (DestroySelf) se o jogador ja passou
                // por aquela parte: 'Encounter Control' (zoteDeathState>0 OU zoteRescuedBuzzer=true)
                // e 'FSM' (zoteDead=true). Mesmo padrao do DeactivateIfPlayerdataTrue/False acima, so
                // que via FSM propria em vez de componente generico - desligar a FSM impede ela de
                // rodar Awake/Start e se autodestruir.
                else if (clone.name.StartsWith("Giant Buzzer") &&
                         (pm.FsmName == "Encounter Control" || (pm.FsmName == "FSM" && HasState(pm, "Destroy"))))
                {
                    pm.enabled = false;
                }
                // God Tamer (a fera, 'Entry Object/Lobster' em GG_God_Tamer - o "God Tamer" do painel). FSM 'Control'
                // (dump: scratchpad\FsmDumpTool\out\godtamer\gg_god_tamer_all.txt): Init -> 'Dormant' (nenhuma acao),
                // espera o WAKE global -> 'Wake' (SetIsKinematic2d false) -> Idle -> IA. Quem manda o WAKE e o PAI
                // 'Entry Object' (o sprite combinado domadora+fera da entrada), no fim da intro dele ('Fight Start':
                // SetPositionToObject + SetParent null + SendEvent WAKE) - e ele NAO vem no preload. Sem isso a copia
                // fica no Dormant pra sempre, cinematica (Rigidbody2D m_BodyType=1 no bundle -> sem gravidade, parada
                // no ar em heroi+(2,2)) e sem IA. A FSM dela nao tem nenhuma coordenada absoluta (so distancias
                // relativas ao heroi) e o glob cuspido ('Vomit Glob') tambem nao, entao nao precisa de ArenaProfile.
                else if (pm.FsmName == "Control" && clone.name.StartsWith("Lobster") &&
                         HasState(pm, "Dormant") && HasState(pm, "Wake"))
                {
                    FsmEventWhenInState.Add(clone, "Control", "Dormant", "WAKE");
                }
            }
            ApplyWakeTable(clone);
            DisableFsmProgressGates(clone);
        }

        // Mesmo papel do DeactivateIfPlayerdataTrue/False (DisableProgressGates abaixo), mas como FSM-template - so nas variantes
        // que antes ficavam escondidas no painel (ver "Manual rename" em EnemyLocations.cs). Dumps (FsmDumpTool\out\collide\
        // hornet2_scene.txt, FsmDumpTool\out\crystalguardian2_scene.txt) + DormantAudit 'comps' no bundle de preload:
        //  - 'Hornet Boss 2' ("Hornet Sentinel"): FSM 'destroy_if_playerdatabool' (template destroy_if_playerdatabool) Pause ->
        //    Check: PlayerDataBoolTest hornetOutskirtsDefeated -> DESTROY -> DestroySelf. Qualquer save que ja passou dela = a
        //    copia some no 2o frame.
        //  - 'Zombie Beam Miner Rematch' ("Enraged Guardian"): FSM 'FSM' (template deactivate_ifnot_playerdatabool) Pause -> Check:
        //    PlayerDataBoolTest defeatedMegaBeamMiner FALSE -> DEACTIVATE -> ActivateGameObject(Owner, false). Ou seja o
        //    contrario: some em qualquer save que AINDA NAO derrotou o Crystal Guardian. (A outra FSM 'FSM' da raiz dele e o
        //    hp_scaler - por isso a escolha e pelo template, nao pelo nome.)
        //  - 'Mage Lord' ("Soul Master", fase 1): FSM 'Destroy If Defeated' (template destroy_if_playerdatabool, sharedassets19 #106)
        //    Pause -> Check: PlayerDataBoolTest mageLordDefeated -> DESTROY -> DestroySelf (FsmDumpTool\out\soulmaster_p1p2.txt). Em
        //    qualquer save que ja derrotou o Soul Master a copia sumia no 2o frame. ('Mage Lord Phase2' nao tem essa FSM - o
        //    DeactivateIfPlayerdataTrue dela ja e desligado no DisableProgressGates.)
        // Desligar a FSM na copia inativa impede o Start dela (mesmo esquema do Vengefly King acima).
        private static readonly string[] FsmGatePrefixes = { "Hornet Boss 2", "Zombie Beam Miner Rematch", "Mage Lord", "Infected Knight" }; // Infected Knight (Broken Vessel): FSM 'FSM' destroy_if_playerdatabool killedInfectedKnight -> DestroySelf no 1o frame
        private static readonly string[] FsmGateTemplates = { "destroy_if_playerdatabool", "deactivate_ifnot_playerdatabool" };

        private static void DisableFsmProgressGates(GameObject clone)
        {
            bool match = false;
            foreach (var p in FsmGatePrefixes) if (clone.name.StartsWith(p)) { match = true; break; }
            if (!match) return;
            foreach (var pm in clone.GetComponents<PlayMakerFSM>())
            {
                string tmpl = pm.FsmTemplate != null ? pm.FsmTemplate.name : null;
                if (System.Array.IndexOf(FsmGateTemplates, tmpl) < 0 && System.Array.IndexOf(FsmGateTemplates, pm.FsmName) < 0) continue;
                pm.enabled = false;
                Modding.Logger.Log($"[CreatureSpawner] '{clone.name}': FSM '{pm.FsmName}' (template '{tmpl}') desligada - gate de progresso do save");
            }
        }

        // Auditoria em massa do padrao "chefe/inimigo adormecido esperando um evento que so o controlador da sala manda"
        // (scratchpad\DormantAudit, modos 'dormant'/'early'/'state' sobre o bundle de preload repacked_full_v7, com os
        // FsmTemplate externos carregados do sharedassets). Mesmo mecanismo do God Tamer acima: sem ArenaProfile (nenhum
        // destes precisa de deslocamento de arena pra sair do estado de espera), so FsmEventWhenInState no estado exato.
        // Cada entrada: (prefixo do nome da copia, FSM, estado em que ela espera, evento, atraso no estado, timeout).
        //  - Shrumal Ogre ('Mushroom Brawler 2', Fungus2_05): 'Shroom Brawler' Init -> Facing Right? -> 'Sleep' (so
        //    ActivateGameObject Cap Hit=false), unica saida WAKE -> 'Wake' (SetCollider true, gravidade 1, Cap Hit, anim
        //    Wake) -> Change Movement. O WAKE vem do filho 'Wake Region' (hero_detect_region), que no prefab esta
        //    INATIVO (a sala liga). BoxCollider2D serializado desligado + HealthManager invincible=1 -> sem isso ele fica
        //    parado, intocavel e invencivel. Limites de patrulha Min/Max X sao relativos (GetPosition +- Roam Distance).
        //  - Aspid Hatchling ('Hatcher Baby Spawner', Crossroads_35): 'Control' Init -> 'Inert' (SetVelocity2d 0), unica
        //    saida SPAWN (mandado pela Aspid Mother/gaiola) -> 'Chase' (SetParent null + ChaseObject $Hero, relativo).
        //  - Infected Balloon ('Parasite Balloon', Abyss_19): 'Control' Initiate -> 'Spawn Wait' (SetCollider/
        //    SetCircleCollider/SetMeshRenderer FALSE = invisivel e intocavel), unica saida SPAWN (o Broken Vessel/sala
        //    manda) -> Spawn Pause -> Check Saver -> 'Spawn' (liga o renderer, anim) -> Activate -> Chase (ChaseObjectV2
        //    no heroi). Nada absoluto.
        //  - Pale Lurker ('Lurker Control/Pale Lurker', GG_Lurker): 'Lurker Control' Init -> 'Dormant' (sem acoes),
        //    unica saida START (o pai 'Lurker Control', fora do preload) -> Get High (GetRandomChild de $Tele High =
        //    objeto da sala, zerado pelo SceneLinkSanitizer -> null) -> Teleport (SetPositionToObject null = no-op, fica
        //    onde nasceu) -> Dig Out -> Reset (collider on, kinematic off) -> Idle (Range L/R filhos acordam o ataque).
        //    Pendencia (nao e dormencia): 'After Fall' compara X com Left/Right Pos absolutos (122.1/196 - so escolhe o
        //    lado pra virar) e 'Tele Height' compara Hero Y com Mid Y 96.6 - os teleportes de cavar viram no-op.
        //  - Grey Prince Zote ('Grey Prince', Dream_Mighty_Zote): o 'Dormant' JA sai sozinho pelo ramo Godhome forcado
        //    (GGCheckIfBossScene bossSceneEvent = GG BOSS -> GG Pause -> Enter 1, que liga o renderer). Mas 'Roar' (intro)
        //    faz SetFsmGameObject(Hero 'Roar Lock') + ROAR ENTER pro heroi (trava o heroi no rugido) e espera ZOTE TITLE
        //    END, mandado pelo letreiro de titulo da sala (nao vem no preload). So 'Roar End' manda ROAR EXIT -> sem isso
        //    o heroi ficava travado e ele invencivel pra sempre. Atraso ~ duracao do letreiro original.
        //    Pendencia (nao e dormencia): 'Constrain X' (template constrain_x) usa Edge L/R 7.19/45.61 ABSOLUTOS e
        //    'Control' usa Left/Right X absolutos - precisa de ArenaProfile proprio (ver relatorio da auditoria).
        private static readonly (string Prefix, string Fsm, string State, string Event, float Delay, float Timeout)[] WakeTable =
        {
            ("Mushroom Brawler", "Shroom Brawler", "Sleep", "WAKE", 0f, 6f),
            ("Hatcher Baby Spawner", "Control", "Inert", "SPAWN", 0f, 6f),
            ("Parasite Balloon", "Control", "Spawn Wait", "SPAWN", 0f, 6f),
            ("Pale Lurker", "Lurker Control", "Dormant", "START", 0f, 6f),
            ("Grey Prince", "Control", "Roar", "ZOTE TITLE END", 2.5f, 0f),
        };

        private static void ApplyWakeTable(GameObject clone)
        {
            foreach (var w in WakeTable)
            {
                if (!clone.name.StartsWith(w.Prefix)) continue;
                foreach (var pm in clone.GetComponents<PlayMakerFSM>())
                {
                    if (pm.FsmName != w.Fsm || !HasState(pm, w.State)) continue;
                    FsmEventWhenInState.Add(clone, w.Fsm, w.State, w.Event, w.Delay, w.Timeout);
                    break;
                }
            }
        }

        // "Some se o jogador ja passou dessa parte da historia": DeactivateIfPlayerdataTrue/False.OnEnable
        // (decompilado) faz gameObject.SetActive(false) se pd.GetBool(boolName) for true/false. No spawner isso
        // apaga a copia no mesmo frame do SetActive(true), sem erro nenhum (o painel ainda diz "spawnado").
        // Caso real: 'Hornet Boss 1' (o "Hornet" do painel, Fungus1_04_boss) tem um na raiz e outro em
        // 'Needle/Needle Tink', boolName = "hornet1Defeated", que e true no save que o usuario usou (user1.dat).
        // Mesmo componente (bundle de preload): Mega Moss Charger (megaMossChargerDefeated), Mega Jellyfish /
        // Uumuu (defeatedMegaJelly), Mage Lord Phase2 (mageLordDefeated), Mega Zombie Beam Miner (1)
        // (killedMegaBeamMiner), Zombie Myla (DeactivateIfPlayerdataFalse hasSuperDash).
        // Desligar o componente na copia inativa basta: OnEnable de componente desligado nao roda.
        // (Uumuu precisa de MAIS que isso: dorme no estado 'Sleep' esperando BATTLE START, com renderer e
        // colliders desligados, e tem um ConstrainPosition absoluto - ver o perfil "Uumuu" em ArenaProfiles.)
        // Hornet depois disso NAO precisa de WAKE: a FSM 'Control' vai Pause -> Init -> 'Inert', cuja 1a acao e
        // GGCheckIfBossScene; com o ramo Godhome forcado (FsmArenaShift.ForceBossSceneBranch) ela sai por
        // GG BOSS -> 'GG Intro 1' (sobe 16, espera 2s) -> 'GG Fall' (liga renderer/collider, cai ate o chao)
        // -> GG Land -> GG Reset -> GG Music -> Flourish -> Idle. (Sem isso ela ficaria no 'Inert' esperando o
        // WAKE do 'Hornet Infected Knight Encounter' da sala, que nao vem no preload.)
        //
        // Vengefly King ("Giant Buzzer", Fungus1_20_v02): ele mesmo (nao um componente generico tipo
        // DeactivateIfPlayerdataTrue) tem DUAS FSMs que se autodestroem no 1o frame dependendo do
        // progresso da historia do Zote - 'Encounter Control' (DestroySelf se zoteDeathState>0 OU
        // zoteRescuedBuzzer=true) e 'FSM' (DestroySelf se zoteDead=true). Em saves que ja passaram por
        // essa parte da historia, ele sumia no mesmo frame do spawn (sem erro, igual aos casos de
        // DeactivateIfPlayerdata* acima). Fix: desligar as duas FSMs na copia (ver Apply) - elas nao tem
        // nenhum papel na luta em si.
        //
        // PersistentBoolItem ("inimigo que continua morto na sala"): relatado com o Ooma - depois de matar varios
        // na mesma sala, nenhum Ooma novo aparecia mais NAQUELA sala (outras salas ok). Decompilado:
        //  - EnemyDeathEffects.orig_RecieveDeathEvent (tambem o EnemyDeathEffectsBubble do Ooma, que herda dele):
        //    na morte chama GetComponent<PersistentBoolItem>().SaveState() -> SetMyID: id = name (vazio no
        //    prefab, entao vira o nome da copia, "Jellyfish(Clone)(Clone)" - IGUAL pra todo spawn da mesma
        //    criatura), sceneName = cena da copia = a sala atual -> SceneData.SaveMyState(activated = isDead = true).
        //  - Proximo spawn: PersistentBoolItem.Start -> SceneData.FindMyState (compara sceneName + id) acha essa
        //    entrada -> OnSetSaveState(true) -> HealthManager (assinado no Awake): isDead = true +
        //    gameObject.SetActive(false). A copia some antes do 1o frame, sem excecao (o painel diz "spawnado").
        // Nao e so do Ooma: 73 prefabs do bundle de preload tem PersistentBoolItem (scratchpad\BundleProbe\out\persistent_v6.txt,
        // modo 'scripts' no bundle de preload) - Hatcher, Moss Knight, Ruins Sentry, Mawlek Body, Gate Mantis,
        // Royal Gaurd, Zombie Myla, ... O Ooma so passou a ter quando trocou pro de Fungus3_01 (o decorativo antigo
        // nao tinha). Nos semiPersistent a entrada so zera quando o heroi morre/descansa; nos outros e PERMANENTE
        // (e vai pro save). So desligar nao basta: o SaveState da morte e chamado direto, mesmo com o componente
        // desligado. Remover da copia inativa (DestroyImmediate - o Destroy normal so roda no fim do frame, depois
        // do Awake do SetActive(true)): o HealthManager.Awake nao acha o componente (nada assinado), a morte nao
        // grava nada e o Start nao le as entradas que ja ficaram no SceneData/save de spawns anteriores.
        private static void DisableProgressGates(GameObject clone)
        {
            foreach (var c in clone.GetComponentsInChildren<DeactivateIfPlayerdataTrue>(true)) c.enabled = false;
            foreach (var c in clone.GetComponentsInChildren<DeactivateIfPlayerdataFalse>(true)) c.enabled = false;
            foreach (var c in clone.GetComponentsInChildren<PersistentBoolItem>(true)) DestroyImmediate(c);
        }

        private static bool HasState(PlayMakerFSM pm, string name)
        {
            foreach (var s in pm.FsmStates) if (s.Name == name) return true;
            return false;
        }

        private void Start() => deadline = Time.time + 5f;

        private void Update()
        {
            if (fsm == null || Time.time > deadline) { Destroy(this); return; }
            if (fsm.ActiveStateName != waitState) return;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = true;
            fsm.SetState(jumpState);
            Destroy(this);
        }
    }
}
