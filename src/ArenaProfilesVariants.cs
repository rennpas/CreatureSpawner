using System.Collections.Generic;
using UnityEngine;

namespace CreatureSpawner
{
    // Perfis de arena (ver ArenaProfiles) das variantes que antes NUNCA apareciam no painel: o cache do painel e por DisplayName
    // e a primeira entrada vence, entao estas ficavam escondidas atras da versao "normal" com o mesmo nome do diario (ver os
    // comentarios "Manual rename" em EnemyLocations.cs). Nenhuma delas tinha sido testada - tudo abaixo vem de dumps novos:
    // scratchpad\FsmDumpTool\out\collide\*.txt (hornet2_scene, vk_gg, uumuu_gg, sa102 (corpse do Soul Master 2),
    // corpse_sharedassets*.assets) + FsmDumpTool\out\soulmaster.txt / dream_lostkin.txt / corpse_lostkin.txt / nailmasters.txt /
    // crystalguardian2_scene.txt, chao conferido com 'edges' (DormantAudit) em cada cena, e componentes/HealthManager/
    // EnemyDeathEffects via DormantAudit 'mb'/'comps' no bundle de preload repacked_full_v8.
    // Politica de sempre: copia spawnada nao grava progresso (SetPlayerData*/AwardAchievement de corpse desligados), e nada de
    // efeito GLOBAL que so a troca de cena do original desfaria (snapshot de audio 'Silent' #3875, broadcast que acorda/fecha
    // coisas da sala atual).
    internal static class ArenaProfilesVariants
    {
        public static void AddTo(Dictionary<string, ArenaProfile> d)
        {
            // Deepnest_East_Hornet_boss ('Hornet Boss 2'). FSM 'Control' (sem template): Pause -> Init -> 'Inert'
            // (GGCheckIfBossScene; ramo Godhome forcado -> GG BOSS) -> 'GG Pause' (Wait 1) -> 'Refight Ready': kinematic, collider
            // OFF, MeshRenderer continua OFF (serializado off no prefab), e so sai com WAKE (BoolTest everyFrame do filho 'Refight
            // Range' = heroi encostando no trigger dela). Ou seja: invisivel/intocavel ate o heroi achar o trigger por acaso.
            // WakeEvents manda o WAKE -> 'Music (not GG)' (GG forcado: FINISHED) -> 'Refight Wake' (SetPosition y=39.11 ABSOLUTO
            // = cai do teto; liga renderer/collider/fisica) -> In Air -> Land -> Flourish -> luta.
            //  - 'Refight Wake' tambem faz broadcast (BroadcastAll) de WAKE e BG CLOSE pra sala inteira: WAKE acordaria qualquer
            //    coisa da sala atual/outras copias esperando WAKE (Watcher Knights 'Rest', NKG/God Tamer 'Dormant'...) e BG CLOSE
            //    fecha portoes de batalha da sala atual que nunca reabrem (so a sala dela manda BG OPEN) -> desligados.
            //  - Coordenadas absolutas: mesma IA/nomes da Hornet 1 (A Dash: Floor Y 27.55 / Roof Y 38.95 / Wall X 15.13..37.9;
            //    Aim Jump RandomFloat(Left X 16.06 .. Right X 36.53); Can Throw? Throw X L/R 22.51/30.16; In Air Sphere Y 33.8).
            //    Sem ConstrainPosition nesta cena. 'Jump X' NAO entra: vira Jump X - Self X e o FloatInRange -2.5..2.5 dele e relativo.
            //  - Chao: EdgeCollider2D do Deepnest_East_Hornet (cena base) em y=27 de x=3 a 46; ela nasce em y 28.54 = 27 + 1.547
            //    (base do collider Idle, igual a Hornet 1) -> chao 27.0. Centro X = meio das paredes (15.13..37.9) = 26.5.
            //  - Corpse 'Corpse Hornet Outskirts' (sharedassets325 #6), FSM 'Control' start='Set PD': SetPlayerDataBool
            //    hornetOutskirtsDefeated=true + AwardAchievement (sem checagem de Godhome!) -> desligados; 'Blow' snapshot 'Silent'
            //    -> desligado. As constantes absolutas do corpse ('Limit Y' clamp y>=29.2, 'Check Pos' x 27.3, FSM 'Stay In Bounds'
            //    x 17.6..36.2) sao deslocadas sozinhas (SpawnDeathWatch -> PatchHierarchy com este perfil).
            //  - (Gate de progresso FSM 'destroy_if_playerdatabool' hornetOutskirtsDefeated -> DestroySelf: ver DormantBossFix.)
            d["Hornet Sentinel"] = new ArenaProfile
            {
                Ref = new Vector2(26.5f, 27.0f),
                XVars = new[] { "Left X", "Right X", "Throw X L", "Throw X R", "Wall X Left", "Wall X Right" },
                YVars = new[] { "Floor Y", "Roof Y", "Sphere Y" },
                LeashUp = 13f, // teto (Roof Y) 11.95 acima do chao = 8.6 acima da ancora; a queda do 'Refight Wake' sai de 12.1
                WakeEvents = new[] { ("Control", "Refight Ready", "WAKE") },
                DisableActions = new[]
                {
                    ("Control", "Refight Wake", "SendEventByName:WAKE"),
                    ("Control", "Refight Wake", "SendEventByName:BG CLOSE"),
                    // corpse (FSM tambem chamada 'Control'; os estados nao existem na FSM da Hornet viva)
                    ("Control", "Set PD", "SetPlayerDataBool"),
                    ("Control", "Set PD", "CallMethodProper"),            // GameManager.AwardAchievement
                    ("Control", "Blow", "TransitionToAudioSnapshot"),     // 'Silent' (#3875)
                },
            };

            // Dream_03_Infected_Knight ('Lost Kin'). FSM 'IK Control': Pause (WaitForHeroInPosition - termina na hora com o heroi
            // ja na sala, decompilado) -> Init -> Repause -> 'Waiting' (renderer OFF, invencivel), que so sai com BATTLE START
            // (BoolTest everyFrame do filho 'Rewake Range') = mesmo "adormecido ate o heroi achar o trigger". WakeEvents manda ->
            // Close Gates (GG forcado) -> Set X GG -> Intro Fall (SetPosition (21.04,41.82) ABSOLUTO = cai do teto, liga renderer)
            // -> Intro Land -> Roar (ROAR ENTER/EXIT no heroi, sozinho) -> Roar End (START SPAWN pra FSM 'Spawn Balloon') -> luta.
            //  - Coordenadas absolutas: Aim Jump RandomFloat(Left X 16.06 .. Right X 36.53); 'Aim Jump 2' (pulo pro meio antes do
            //    Shake) RandomFloat(25.5..27.5) -> Jump X (Jump X depois vira relativo por FloatSubtract, sem compare nele: entra em
            //    XVars so pra esse RandomFloat ser deslocado); In Air compara Self Y com Min Dstab Height 33.31 / Air Dash Height
            //    31.5; 'Set Height' SetPosition y=Air Dash Height. FSM 'Spawn Balloon': balões em RandomFloat(X Min 17.36 .. X Max
            //    36.46, Y Min 32.16 .. Y Max 37.42). Intro Fall/Set X: constantes de SetPosition, deslocadas sozinhas.
            //  - Chao: EdgeCollider2D em y=27 de x=10 a 50; teto 42..43. Centro X = meio de Left/Right X = 26.3.
            //  - Corpse 'Corpse Infected Knight Dream' (sharedassets397 #2): 'Set Defeated' (infectedKnightDreamDefeated) ja e pulado
            //    pelo ramo Godhome forcado; 'Blow' AwardAchievement -> desligado; 'BG Open' cria 'Dream Orb Get' (sharedassets395 #7 =
            //    a saida do sonho, ChangeToScene dreamReturnScene) se a zona atual for DREAM_WORLD -> desligado.
            d["Lost Kin"] = new ArenaProfile
            {
                Ref = new Vector2(26.3f, 27.0f),
                XVars = new[] { "Left X", "Right X", "Jump X", "X Min", "X Max" },
                YVars = new[] { "Air Dash Height", "Min Dstab Height", "Y Min", "Y Max" },
                LeashUp = 12f, // a intro cai de 14.8 acima do chao (11.4 acima da ancora)
                WakeEvents = new[] { ("IK Control", "Waiting", "BATTLE START") },
                DisableActions = new[]
                {
                    ("corpse", "Blow", "CallMethodProper"),   // GameManager.AwardAchievement
                    ("corpse", "BG Open", "CreateObject"),    // 'Dream Orb Get' (so em DREAM_WORLD)
                },
            };

            // Ruins1_24_boss ('Mage Lord Phase2' = a 2a fase do Soul Master). FSM 'Mage Lord 2': Pause -> 'Set GG' -> 'Init', que so
            // sai com PHASE 2 (FloatCompare everyFrame Hero Y < 11 ABSOLUTO = heroi caiu no andar de baixo depois da fase 1). Renderer
            // OFF e invencivel ate 'Tele Quake'. WakeEvents manda PHASE 2 -> Arrive Pause (2s) -> Music -> Tele Quake -> luta.
            //  - 'Set GG': com o ramo Godhome forcado o GGCheckIfBossScene nao manda evento nenhum e as 10 SetFloatValue com as
            //    constantes da arena GG_Soul_Master (Quake Y 38, Ground Y 31.05, Left/Right X 6.98/34.75, Orb...) rodam. O prefab e o
            //    do Ruins1_24_boss (vars desta arena) -> essas SetFloatValue sao desligadas e o perfil usa a arena Ruins1_24.
            //  - Coordenadas absolutas (vars): Tele Quake/Tele Away/Shift? usam Left X 10.76 / Right X 40.18, Top Y 16.24 / Bot Y
            //    12.24, Quake Y 19.5, Knight Quake Y Max 35.41; Quake Down/Land: Ground Y 11.34; Fireball Pos: Orb Min/Max X 10.1/40.5,
            //    Y 10.4/20.08 (sem deslocar: loop RESET/Reset Orb, igual a Radiance); 'Tele Out' SetPosition y=-10 (esconde ele 19
            //    abaixo do chao - constante, deslocada sozinha). 'CameraLockArea B' (filho) so e desparentado, nunca ativado.
            //  - Chao: EdgeCollider2D do Ruins1_24 (cena base) em y=9 de x=8 a 42 (paredes 8/42); Ground Y 11.34 = pivo dele 2.34
            //    acima; Hero Y < 11 = heroi em pe nesse chao. Centro X = meio de Left/Right X = 25.47.
            //  - Corpse 'Corpse Mage Lord 2' (sharedassets102 #36), FSM 'corpse': 'Init' SetPlayerDataBool mageLordDefeated=true (sem
            //    checagem de Godhome - marcaria o Soul Master como derrotado no save) -> desligado. 'Quake Pickup' (Desolate Dive /
            //    AwardAchievement) ja e pulado pelo ramo Godhome forcado ('Soul Breath' -> End GG Scene).
            //  - (DeactivateIfPlayerdataTrue mageLordDefeated: ja desligado de forma generica no DormantBossFix.)
            //  - Nao e mais botao do painel: este perfil e usado quando a fase 2 nasce sozinha, na morte da fase 1 "Soul Master"
            //    (MageLordPhases / PhasedMageLordEncounter, mesmo caminho Prepare/BeforeActivate/AfterActivate do painel).
            d["Soul Master (Phase 2)"] = new ArenaProfile
            {
                Ref = new Vector2(25.47f, 9.0f),
                XVars = new[] { "Left X", "Right X", "Orb Min X", "Orb Max X", "Hero Mid X" },
                YVars = new[] { "Ground Y", "Top Y", "Bot Y", "Quake Y", "Orb Min Y", "Orb Max Y", "Knight Quake Y Max", "Shockwave Y" },
                LeashDown = 23f, // 'Tele Out' esconde ele 19 abaixo do chao (22.4 abaixo da ancora)
                WakeEvents = new[] { ("Mage Lord 2", "Init", "PHASE 2") },
                DisableActions = new[]
                {
                    ("Mage Lord 2", "Set GG", "SetFloatValue"),
                    ("corpse", "Init", "SetPlayerDataBool"),
                },
            };

            // GG_Uumuu ('Mega Jellyfish GG'). FSM 'Mega Jellyfish' (template gg_uumuu): Init -> 'Sleep', que so sai com BATTLE START
            // (broadcast do 'Battle Scene' da sala, ausente) - renderer/colliders OFF: o mesmo dormente do Uumuu normal. Mesmo corpo
            // e mesma arena do Uumuu normal: posicao inicial (53.21,99.91), ConstrainPosition X 32.7..73.16 / Y max 134.93 e FSM
            // 'Bounds' X 36.46..69.18 / Y 105.73..131.21 IDENTICOS nas duas cenas -> mesmo Ref do perfil "Uumuu" (ja testado): nasce
            // ~16.5 abaixo do chao e sobe 23 no Burst. ('Bounds' so liga com AWAKE, que ninguem manda - deslocado por consistencia.)
            //  - Diferenca do Godhome: sem Quirrel. A vulnerabilidade vem de EXPLODE ('Explode' = SetInvincible false), mandado pela
            //    explosao 'Gas Explosion Uumuu' (FSM 'Uumuu Event', sharedassets156) quando o NUCLEO de um Ooma morto ('Corpse
            //    Jellyfish': Trigger2dEvent tag 'Boss' -> 'Instant Explosion') encosta no filho 'Detonator' (tag Boss) dele. Os Oomas
            //    vem do 'Roar' (SendEventToRegister SPAWN) -> 'Jellyfish Spawner' da sala (ausente) -> ver GodhomeUumuuOomas.
            //  - Corpse 'Corpse Mega Jellyfish' (sharedassets229 #32, o mesmo do Uumuu normal), FSM 'Death' start='PlayerData':
            //    SetPlayerDataBool defeatedMegaJelly=true sem checagem de Godhome -> desligado.
            d["Uumuu (Godhome)"] = new ArenaProfile
            {
                Ref = new Vector2(53.3f, 116.4f),
                XVars = new[] { "X Min", "X Max" },
                YVars = new[] { "Y Min", "Y Max" },
                SpawnAtArenaPosition = true,
                LeashDown = 21f,
                WakeEvents = new[] { ("Mega Jellyfish", "Sleep", "BATTLE START") },
                DisableActions = new[] { ("Death", "PlayerData", "SetPlayerDataBool") },
            };

            // GG_Nailmasters ('Brothers/Oro' = Oro sozinho, a fase 1 da luta dos Nailmasters). FSM 'nailmaster' (template):
            // Init -> (Oro=true) P2 HP Adjust -> Rest (2s) -> Wake (1s) -> Roar (2.8s; ROAR ENTER/EXIT no heroi) -> Roar End
            // (collider on, fisica on) -> luta: NAO e dormente.
            //  - Coordenadas absolutas: Jump/Jump To/Cyclone Up comparam Self Y com Topslash Y 16.1 e 'Dstab Antic' faz SetPosition
            //    y=Topslash Y; Dash Dir compara Hero X com 46.52; Dash L/R escrevem Target X 60/33 e End X 39.77/54 por SetFloatValue
            //    CONSTANTE (-> Aim Jump To pula pra Target X; 'Dashing L/R' so termina quando Self X passa End X; 'D Slash' faz
            //    SetPosition x=End X). Sem deslocar: o dash do Nail Art nunca termina (fica preso dashing contra a coleira). Cyclone
            //    Ready: Self X vs 46 e Target X = 46. Can Evade?/Hero Evade: Self X vs 35.73/57.49. Aim Jump 2 RandomFloat(41..52).
            //    ConstrainPosition: y >= 5.52 (deslocado pelo perfil). As constantes de FloatCompare/RandomFloat/SetPosition sao
            //    deslocadas sozinhas; Topslash Y (variavel) em YVars; as SetFloatValue constantes em SetFloatXVars.
            //  - MORTE: HealthManager hasSpecialDeath=1 -> ZERO HP -> Death Start -> ... -> Death Land -> 'Death Type' (Phase 2 =
            //    false) -> 'Call Mato' (manda ENTER pro Mato - null aqui) e espera LOOK, que so o Mato manda: ficaria deitado,
            //    invencivel e sem dano PRA SEMPRE. Redirect CALL MATO -> 'Defeated' (o final de verdade da luta), que espera BOW do
            //    'Combo Control' do pai 'Brothers' (ausente; no original ele manda ~0.75s depois) -> PhaseEvents manda BOW -> Drum
            //    Roll -> Bow (reverencia, 2s) -> 'End' (EndGGBossScene = nada fora do Godhome) -> FinishStates remove a copia.
            //    'End Music?' e 'Bow' mudam o snapshot de audio pra 'Silent' (#3875) -> desligados.
            //  - Chao: EdgeCollider2D em y=4 de x=3 a 87; Oro nasce em (52, 6.81) = em pe nesse chao (renderer on, collider off,
            //    kinematic durante a intro) -> nasce na posicao da arena deslocada. Centro X = Dash Dir 46.52.
            d["Oro (Solo)"] = new ArenaProfile
            {
                Ref = new Vector2(46.52f, 4.0f),
                YVars = new[] { "Topslash Y" },
                SetFloatXVars = new[] { "Target X", "End X", "Away X" },
                SpawnAtArenaPosition = true,
                LeashUp = 12f, // Dstab Antic sobe ate Topslash Y = 12.1 acima do chao
                Redirects = new[] { ("nailmaster", "Death Type", "CALL MATO", "Defeated") },
                PhaseEvents = new[] { ("nailmaster", "Defeated", "BOW", 1.0f) },
                FinishStates = new[] { ("nailmaster", "End", 0.5f) },
                DisableActions = new[]
                {
                    ("nailmaster", "End Music?", "TransitionToAudioSnapshot"),
                    ("nailmaster", "Bow", "TransitionToAudioSnapshot"),
                },
            };

            // GG_Nailmasters ('Brothers/Mato' = "Mato (Solo)" do painel - antes chamado "Nailmasters Oro & Mato" -, lutando SOZINHO; a luta
            // em dupla e o perfil seguinte, NailmastersPair.Name). Mesma FSM 'nailmaster' (template
            // sharedassets454 #24) do Oro; o que muda sao as variaveis/componentes da instancia (FsmDumpTool\out\nailmasters_brothers.txt,
            // BundleProbe 'mb'/'rb' sobre repacked_full_v10): Oro=False, Brothered=False, Phase 2=False, Topslash Y 16.1; HealthManager
            // invincible=1, DamageHero damageDealt=0, NonBouncer active=1, Rigidbody2D kinematic (o Oro: invincible=0, damage 1) - o Mato
            // so vira "lutavel" no estado 'Activate' (SetInvincible false, SetDamageHeroAmount 1, NonBouncer, gravidade 3).
            //  - TRAVADO HOJE: Init BoolTest $Oro (false) -> MATO -> 'Entry Wait', cuja unica saida e ENTER, mandado so pelo 'Call Mato' do
            //    Oro ($Brother = 'Brothers/Oro' do bundle -> anulado pelo SceneLinkSanitizer). Fica parado, kinematic, invencivel e sem dano
            //    de contato PRA SEMPRE (= o relato). Mandar o ENTER nao resolve: 'Entry Dir' faz SendEventByScale no $Brother (null), 'Pos L/R'
            //    copiam o X do irmao pra Self X e o 'Entry Fall' faz SetPosition(Self X, 22.86) ABSOLUTO; e o 'Entry Wait'/'Activate' poem
            //    Brothered=true -> Idle -> 'Single?' DOUBLE -> 'Wait For Bro' (so sai com o Ready do irmao ou Brothered=false) = parado de novo.
            //  - Correcao: Init MATO -> 'P2 HP Adjust' = a MESMA intro do "Oro (Solo)" (Rest -> Wake -> Roar -> Roar End: collider on,
            //    fisica on; mesma biblioteca de animacao 2788:25 nos dois, entao os clipes existem). 'First Idle' END -> 'Activate' (o que o
            //    torna vulneravel/com dano), com o SetBoolValue Brothered=true do Activate desligado -> Idle -> 'Single?' SINGLE -> 'Oro Choice'
            //    = o Mato sozinho de verdade (o mesmo caminho do jogo quando o Oro morre primeiro na fase 2: 'Defeated' do Oro poe Brothered=false).
            //    O titulo do 'Roar' ('NM_ORO') vira 'TEMP_NM' (o que o proprio Mato mostra no 'Entry Roar') - ver NailmasterMatoFix.
            //  - Ataques do Mato sozinho: COMBO (Dash?/Combo), JUMP (Jump -> Dstab Antic) e D SLASH -> NA -> (Oro=false) CYCLONE -> Cyclone Bro
            //    -> Cyclone Ready (Brothered=false -> CYCLONE) -> NA Charge -> Art Choice (Oro=false -> CYCLONE, nunca o dash-slash do Oro) ->
            //    Cyclone Up, que SO termina com Self Y > Topslash Y (o Wait 1.5 do template esta desligado) -> Cyclone End -> Dstab Antic
            //    SetPosition y=Topslash Y. Topslash Y absoluta = o equivalente do problema do Oro: sem deslocar, o ciclone sobe ate y=16.1 de
            //    MUNDO (nunca termina numa sala mais alta... ou acaba na hora numa sala mais baixa) e o Dstab teleporta pra la -> YVars.
            //    Target X/End X/Away X (dash-slash/pulo pro centro - so com Oro=true ou Brothered=true) deslocados igual ao Oro, por seguranca.
            //    Teto baixo (Self Y nunca passa Topslash Y no ciclone): ver NailmasterMatoFix (vigia do 'Cyclone Up').
            //  - MORTE: Phase 2=false (o 'Entry Wait' que poria true nao roda) -> 'Death Type' CALL MATO -> 'Call Mato' espera LOOK do irmao
            //    pra sempre (deitado, invencivel) -> mesmo Redirect do Oro: -> 'Defeated' -> PhaseEvents BOW -> Drum Roll (Oro=false: FINISHED
            //    direto) -> Bow -> 'End' -> FinishStates remove a copia. Snapshots 'Silent' (#3875) do End Music?/Bow desligados.
            //  - Nasce em heroi+(2,2) (sem SpawnAtArenaPosition: a posicao original (19.26, 26.53) e o ponto de entrada fora da tela, 22.5
            //    acima do chao); kinematic na intro e cai ~0.5 no Roar End. Mesma arena/chao do Oro -> mesmo Ref.
            d[NailmasterMatoFix.Name] = new ArenaProfile
            {
                Ref = new Vector2(46.52f, 4.0f),
                YVars = new[] { "Topslash Y" },
                SetFloatXVars = new[] { "Target X", "End X", "Away X" },
                LeashUp = 12f, // Cyclone Up / Dstab Antic sobem ate Topslash Y = 12.1 acima do chao
                Redirects = new[]
                {
                    ("nailmaster", "Init", "MATO", "P2 HP Adjust"),
                    ("nailmaster", "First Idle", "END", "Activate"),
                    ("nailmaster", "Death Type", "CALL MATO", "Defeated"),
                },
                PhaseEvents = new[] { ("nailmaster", "Defeated", "BOW", 1.0f) },
                FinishStates = new[] { ("nailmaster", "End", 0.5f) },
                DisableActions = new[]
                {
                    ("nailmaster", "Activate", "SetBoolValue"),           // Brothered=true (-> 'Wait For Bro' esperando o Oro)
                    ("nailmaster", "End Music?", "TransitionToAudioSnapshot"),
                    ("nailmaster", "Bow", "TransitionToAudioSnapshot"),
                },
            };

            // GG_Nailmasters, objeto 'Brothers' INTEIRO = "Nailmasters Oro & Mato" do painel, a luta em dupla de verdade (sequencia
            // completa, confirmada no dump, em NailmastersPair.cs). A raiz 'Brothers' esta em (0,0,0) -> SpawnAtArenaPosition poe o grupo
            // em delta e os irmaos nas posicoes originais deslocadas (Oro em pe em (52, 6.81) como no "Oro (Solo)"; Mato esperando
            // escondido em (19.26, 26.53)). Mesmas coordenadas absolutas dos solos (mesma FSM): Topslash Y em YVars; Target X/End X/Away X
            // (SetFloatValue constantes do dash-slash, 'Cyclone Ready' 46 e 'Jump Away L/R' 58.39/34.9) em SetFloatXVars; o resto
            // (Dash Dir 46.52, Can Evade? 35.73/57.49, Aim Jump 2 41..52, e os da entrada do Mato: Pos L/R 32.81/60.35, 'Entry Fall'
            // SetPosition y=22.86 e o pouso y<=6.86) e deslocado sozinho pelo FsmArenaShift.
            //  - SEM os Redirects/PhaseEvents dos solos: aqui o $Brother e o $Controller existem, entao o 'Call Mato' chama o Mato de verdade
            //    e o 'Combo Control' manda o BOW final quando os dois caem.
            //  - FinishStates: 'Combo Control' fica no 'Bow' depois de mandar BOW pros dois (Drum Roll -> Bow 2 s -> End) -> remove o grupo.
            //  - Coleira POR IRMAO (NailmastersPair tira a da raiz): 'Entry Fall' poe o Mato 18.86 acima do chao (15.5 acima da ancora).
            //  - Desligados (efeito GLOBAL/progresso, mesma politica de sempre): snapshots 'Silent' do End Music?/Bow (como nos solos);
            //    'Entry Roar' do Mato ApplyMusicCue (troca a musica da sala atual pra da luta, so a troca de cena desfaria) +
            //    TransitionToAudioSnapshot (#3835); 'Combo Control' 'Journal' (killedNailBros/newDataNailBros/killsNailBros + o aviso
            //    de diario).
            d[NailmastersPair.Name] = new ArenaProfile
            {
                Ref = new Vector2(46.52f, 4.0f),
                YVars = new[] { "Topslash Y" },
                SetFloatXVars = new[] { "Target X", "End X", "Away X" },
                SpawnAtArenaPosition = true,
                LeashUp = 17f,
                FinishStates = new[] { ("Combo Control", "Bow", 2.5f) },
                DisableActions = new[]
                {
                    ("nailmaster", "End Music?", "TransitionToAudioSnapshot"),
                    ("nailmaster", "Bow", "TransitionToAudioSnapshot"),
                    ("nailmaster", "Entry Roar", "ApplyMusicCue"),
                    ("nailmaster", "Entry Roar", "TransitionToAudioSnapshot"),
                    ("Combo Control", "Journal", "SetPlayerDataBool"),
                    ("Combo Control", "Journal", "SetPlayerDataInt"),
                    ("Combo Control", "Journal", "CreateObject"),
                },
            };

            // GG_Vengefly ('Giant Buzzer Col', 450 HP). FSM 'Big Buzzer': Init -> (GG forcado) Wait Frame -> 'Set GG'
            // (PreSpawnGameObjects das Vengeflies, Swoop Height = 14, Wait 2) -> Intro Roar Antic (collider/fisica on, SetInvincible
            // false) -> ... -> Idle: NAO e dormente (fora do Godhome ele iria pro 'Swoop In', que espera SUMMON do Colosseum).
            //  - Coordenadas absolutas: 'Swoop Direction' faz Swoop Target = Swoop Height - Self Y e iTweenMoveBy (0, Swoop Target):
            //    o mergulho desce ate y=14 ABSOLUTO (iTween ignora colisao - atravessaria o chao da sala atual). Swoop Height e
            //    escrita por SetFloatValue constante no 'Set GG' -> SetFloatYVars. 'GG Summon': GetNextPreSpawnedGameObject em
            //    (38,25)/(57,25) de MUNDO -> ShiftPreSpawnPositions (sem isso as Vengeflies nascem na arena original, longe, e o
            //    contador TrackSpawnedEnemies conta elas como vivas pra sempre -> ele para de invocar). 'Summon' (CreateObject em
            //    94.63/109.85 do Colosseum) e inalcancavel com o ramo Godhome forcado.
            //  - Chao: a poeira do mergulho ('Buzzer Dust', y 12.05, x 41.25..53.89) e o EdgeCollider2D y=12 de x=40 a 55; centro X =
            //    meio das duas Vengeflies invocadas (38/57) = 47.5.
            //  - Corpse 'Corpse Giant Buzzer Col' (sharedassets32 #243): 'Music Out 2' snapshot ja pulado pelo ramo Godhome; o
            //    SetPlayerDataInt do 'Init' tem nome vazio (no-op). Nada gravado.
            d["Vengefly King (Godhome)"] = new ArenaProfile
            {
                Ref = new Vector2(47.5f, 12.0f),
                SetFloatYVars = new[] { "Swoop Height" },
                ShiftPreSpawnPositions = true,
            };

            // Mines_32 ('Battle Scene/Zombie Beam Miner Rematch' = "Enraged Guardian"). FSM 'Beam Miner' identica a do Crystal
            // Guardian (Pause Frame -> Init -> GG forcado: GG Wait -> Wake -> Battle Init (SetInvincible false) -> Roar -> luta):
            // NAO e dormente.
            //  - 'Aim Jump': RandomFloat(Jump Min X 40.01 .. Jump Max X 16.72) - Self X = velocidade do pulo: alvo ABSOLUTO (fora da
            //    arena: pulo com velocidade enorme pra longe, coleira puxando) -> XVars. (O Crystal Guardian normal tem os mesmos
            //    valores e NAO tem perfil - nao mexido aqui, fora do escopo.)
            //  - 'Wake' ativa e desparenta o filho 'Cam Lock' (2 CameraLockArea travando a camera em (30.5, 15.36) ABSOLUTO da sala
            //    original - decompilado: OnTriggerEnter2D do heroi -> LockToArea) -> camera iria pra longe do heroi -> desligado.
            //    Tambem manda BG CLOSE (BroadcastAll) -> portoes da sala atual fechariam sem reabrir -> desligado.
            //  - Chao: EdgeCollider2D y=10 de x=4 a 47 (teto 25), igual ao Mines_18 do Crystal Guardian; centro X = meio do alcance
            //    do pulo (16.72..40.01) = 28.37.
            //  - Laser proprio (destroy_if_gameobject_null nos filhos Beam/Beam Ball/Beam Impact, mesma estrutura) e lasers do teto
            //    (4 'Laser Turret Mega' do Mines_32): ver SpawnFixups.AfterActivate / EnragedGuardianTurrets.
            //  - (Gate de progresso FSM 'FSM' deactivate_ifnot_playerdatabool defeatedMegaBeamMiner: ver DormantBossFix.)
            d["Enraged Guardian"] = new ArenaProfile
            {
                Ref = new Vector2(28.37f, 10.0f),
                XVars = new[] { "Jump Min X", "Jump Max X" },
                DisableActions = new[]
                {
                    ("Beam Miner", "Wake", "ActivateGameObject"),         // 'Cam Lock'
                    ("Beam Miner", "Wake", "SendEventByName:BG CLOSE"),
                },
            };
        }
    }
}
