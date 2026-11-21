using System.Collections.Generic;
using UnityEngine;

namespace CreatureSpawner
{
    // Varios chefes tem a IA (FSMs do PlayMaker) escrita em COORDENADAS ABSOLUTAS DE MUNDO da arena original:
    // "se meu X < 36 volte pro centro", "teleporte pra (56,41)", "mergulhe ate Y < 8.3 (o chao)". Na sala onde
    // a gente spawna, essas coordenadas nao significam nada -> o chefe teleporta pra longe, fica preso em loop
    // de "tentar de novo", ou briga com a SpawnLeash todo frame. Tudo abaixo foi tirado offline das FSMs
    // reais (scratchpad\FsmDumpTool\out\*.txt) e dos componentes (scratchpad\PhysProbe\res\*.txt).
    //
    // Pra esses chefes a gente desloca as constantes/variaveis de posicao das FSMs por
    //     delta = (heroi.x - Ref.x,  chaoReal - Ref.y)
    // onde Ref.x = centro X da arena ORIGINAL e Ref.y = altura do CHAO (nivel dos pes) da arena original, e
    // chaoReal = Physics2D.Raycast pra baixo a partir do heroi na camada Terrain (mascara 256, a mesma de
    // HeroController.FindGroundPoint) - ver SpawnFixups.FindGroundY.
    //
    // ANTES Ref.y era "onde o pivo do heroi estaria em pe", estimado como chao + ~0.55..0.7, e o delta usava
    // heroi.y. Medido no Knight (resources.assets, BoxCollider2D offset.y=-0.75 size.y=1.28125): o pivo do
    // heroi em pe fica 0.75 + 0.64 = 1.39 ACIMA do chao (= extents.y - offset.y do FindGroundPoint). Ou seja,
    // todo chao/aterrissagem deslocado ficava ~0.7 alto demais (Gorb ~0.85) -> "nunca exatamente no chao",
    // e pior se o heroi estivesse pulando/em cima de um degrau na hora do spawn.
    //
    // O que e deslocado automaticamente esta em FsmArenaShift; aqui ficam so os nomes das variaveis que a FSM
    // ja traz preenchidas com coordenadas absolutas. ConstrainPosition (componente que trava X/Y em limites
    // absolutos da arena) e deslocado pelo mesmo delta em SpawnFixups.BeforeActivate.
    internal sealed class ArenaProfile
    {
        /// <summary>x = centro X da arena original; y = altura do chao (pes) da arena original.</summary>
        public Vector2 Ref;
        public string[] XVars = new string[0];
        public string[] YVars = new string[0];
        /// <summary>Vector3 absolutos: variaveis com esse nome + constantes de SetFsmVector3/SetVector3Value
        /// que escrevem numa variavel com esse nome.</summary>
        public string[] V3Vars = new string[0];

        /// <summary>SetFloatValue com valor CONSTANTE que escreve numa destas variaveis = coordenada X/Y absoluta escrita
        /// DEPOIS do ajuste (ex. Grey Prince 'Level 1..4+': Shockwave Y = 4.9 no 1o frame da FSM, depois do Shift das
        /// variaveis). O valor da acao e deslocado. Opt-in por perfil (so os perfis que listam algo aqui mudam).</summary>
        public string[] SetFloatXVars = new string[0];
        public string[] SetFloatYVars = new string[0];

        /// <summary>Variaveis que NAO sao coordenadas mesmo recebendo um GetPosition de mundo (ex. Grey Prince 'Jump X' =
        /// X do heroi menos o proprio X, depois FloatClamp -17..17 - relativo; sem isto o clamp seria deslocado).</summary>
        public string[] RelativeVars = new string[0];

        /// <summary>true: nasce na posicao original da arena (+delta) em vez de heroi+(2,2).</summary>
        public bool SpawnAtArenaPosition;

        /// <summary>Raios da coleira (null = padrao da SpawnLeash). Ancora = (heroi.x, chao + pivoHeroi + 2).</summary>
        public float? LeashX, LeashUp, LeashDown;

        /// <summary>Eventos que o controlador da sala (ausente) mandaria pra acordar o chefe:
        /// (nome da FSM, estado em que ela espera, evento).</summary>
        public (string Fsm, string State, string Event)[] WakeEvents = new (string, string, string)[0];

        /// <summary>Acoes desligadas na copia: (FSM, estado, nome do tipo da acao). "SendEventByName:EVENTO" desliga
        /// so os SendEventByName daquele estado que mandam EVENTO (ver FsmArenaShift.DisableActions).</summary>
        public (string Fsm, string State, string Action)[] DisableActions = new (string, string, string)[0];

        /// <summary>Transicoes redirecionadas na copia: (FSM, estado, evento, novo estado destino). Usado pra pular
        /// trechos de cutscene que so funcionam na sala/modo original (ver FsmArenaShift.Redirect).</summary>
        public (string Fsm, string State, string Event, string ToState)[] Redirects = new (string, string, string, string)[0];

        /// <summary>Estados finais "sem saida" do final original (a cena trocaria/o Godhome encerraria a luta):
        /// quando a FSM fica Delay s nesse estado, a copia spawnada e removida (ver SpawnedBossFinish).</summary>
        public (string Fsm, string State, float Delay)[] FinishStates = new (string, string, float)[0];

        /// <summary>Estados da cutscene do final original durante os quais o HEROI fica congelado/invisivel (todo o
        /// caminho entre o golpe final e o FinishStates). Ver algum deles ativo = cutscene comecou: o SpawnedBossFinish
        /// restaura o heroi no fim (FinishStates) e liga o vigia (estado parado demais / fora dessa lista / tempo total
        /// estourado -> encerra na marra e restaura).</summary>
        public (string Fsm, string State)[] HeroCutsceneStates = new (string, string)[0];

        /// <summary>Estados da cutscene que esperam INPUT do jogador (ex. ListenForAttack): se o jogador nao apertar,
        /// o evento e mandado sozinho depois de Delay s no estado (a cutscene nunca fica parada esperando).</summary>
        public (string Fsm, string State, string Event, float Delay)[] CutsceneInputs = new (string, string, string, float)[0];

        /// <summary>Tempo maximo (s, tempo de jogo) da cutscene do heroi, do 1o HeroCutsceneStates visto ate o FinishStates.
        /// Estourou -> encerra na marra e restaura o heroi. &lt;= 0: sem limite total (os outros vigias continuam).</summary>
        public float CutsceneTimeout;

        /// <summary>iTweenMoveTo com destino CONSTANTE de mundo nesses estados (ex. o Knight falso da cutscene final) sao
        /// deslocados pelo delta do estado (inclui SubArenas). MinAboveFloor (NaN = nao limita): o Y final fica no minimo
        /// isso acima do chao real (pra nao "cair dentro" do chao numa sala sem o abismo da arena original).</summary>
        public (string Fsm, string State, float MinAboveFloor)[] WorldTweens = new (string, string, float)[0];

        /// <summary>Eventos que a GEOMETRIA da sala (ausente) mandaria NO MEIO da luta (ex. trigger que o heroi toca
        /// ao chegar numa sub-arena): (FSM, estado em que ela espera, evento, segundos no estado antes de mandar).
        /// Sem timeout - valem pra luta inteira.</summary>
        public (string Fsm, string State, string Event, float Delay)[] PhaseEvents = new (string, string, string, float)[0];

        /// <summary>Sub-arenas com chao proprio (outra altura na sala original): as constantes desses estados da
        /// FSM sao deslocadas com o mesmo X mas com Y relativo a FloorY (chao da sub-arena) em vez de Ref.y.</summary>
        public (string Fsm, float FloorY, string[] States)[] SubArenas = new (string, float, string[])[0];

        /// <summary>Ataques de pool global que se posicionam sozinhos em coordenadas absolutas da arena original
        /// (ver PooledAttack / PooledAttackShift).</summary>
        public PooledAttack[] PooledAttacks = new PooledAttack[0];

        /// <summary>Objetos de pool global cuja FSM PROPRIA compara a posicao deles com limites ABSOLUTOS da arena original
        /// guardados em variaveis (ver PooledAttackBounds / PooledBoundsShift).</summary>
        public PooledAttackBounds[] PooledBounds = new PooledAttackBounds[0];

        /// <summary>Opt-in (false = perfis antigos exatamente como antes): o spawnPosition CONSTANTE de GetNextPreSpawnedGameObject
        /// (posicao de MUNDO - decompilado: gameObject.transform.position = spawnPosition.Value) tambem e deslocado pelo delta.
        /// Ex. Vengefly King GG 'GG Summon': Vengeflies em (38,25)/(57,25) da arena GG_Vengefly (ver ArenaProfilesVariants).</summary>
        public bool ShiftPreSpawnPositions;
    }

    /// <summary>Objeto do POOL GLOBAL spawnado pela copia (fora da hierarquia dela - o FsmArenaShift nunca ve a FSM dele) que
    /// tem, numa FSM propria, limites de arena ABSOLUTOS em variaveis float (ex. 'Radiant Orb' FSM 'Final Control': Min/Max X/Y,
    /// fora disso RecycleSelf). Um estado da copia (Hook) recebe, no inicio, uma acao que desloca essas variaveis NA INSTANCIA
    /// guardada em StoreVar pelo delta da arena (Y relativo a FloorY, como SubArenas); o valor original volta quando o objeto
    /// e reciclado (OnDisable) - a instancia do pool volta intacta pra luta real / pra outra copia.</summary>
    internal sealed class PooledAttackBounds
    {
        /// <summary>(FSM da copia, estado) que tem o objeto em StoreVar e, depois do hook, dispara o teste de limites.</summary>
        public (string Fsm, string State) Hook;
        public string StoreVar;
        /// <summary>Bool da FSM Hook que tem que estar true pra deslocar (null = sempre) - o mesmo que decide se o teste dispara.</summary>
        public string OnlyIfBool;
        /// <summary>FSM do objeto spawnado e as variaveis float absolutas dela.</summary>
        public string Fsm;
        public string[] XVars = new string[0];
        public string[] YVars = new string[0];
        /// <summary>Chao da sub-arena a que os limites pertencem (NaN = Ref.y, arena principal).</summary>
        public float FloorY = float.NaN;
    }

    /// <summary>Ataque que a copia spawna do POOL GLOBAL (prefab compartilhado com a luta real, fora da hierarquia da copia,
    /// entao o FsmArenaShift nunca ve a FSM dele) e que, na FSM PROPRIA dele, faz SetPosition de MUNDO com constantes da
    /// arena original. As constantes do objeto nao sao tocadas (a mesma instancia do pool volta pra luta real / pra outra
    /// copia com outro delta): cada spawn feito pela copia ganha um PooledAttackPlacement que, quando o objeto ja se
    /// posicionou (ReadyState) e antes dele comecar a se mover, translada a raiz dele pelo delta da arena.</summary>
    internal sealed class PooledAttack
    {
        /// <summary>(FSM da copia, estado) que spawnam o objeto (SpawnObjectFromGlobalPool -> storeObject = StoreVar).</summary>
        public (string Fsm, string State)[] Spawners = new (string, string)[0];
        public string StoreVar;
        /// <summary>FSM do objeto spawnado e a variavel int dela que escolhe o estado de posicionamento.</summary>
        public string Fsm, TypeVar;
        /// <summary>Estado do objeto em que ele ja esta na posicao absoluta e ainda nao comecou a se mover.</summary>
        public string ReadyState;
        /// <summary>Tipos cujas constantes sao de uma sub-arena: tipo -> chao dela (os outros usam Ref.y), como SubArenas.</summary>
        public Dictionary<int, float> TypeFloorY = new Dictionary<int, float>();
    }

    internal static class ArenaProfiles
    {
        // Perfis das variantes que antes ficavam escondidas pela colisao de DisplayName (Hornet Sentinel, Lost Kin, ...) ficam
        // num arquivo proprio (ArenaProfilesVariants.cs) e entram aqui depois do inicializador de ByName.
        static ArenaProfiles() { ArenaProfilesVariants.AddTo(ByName); GruzZoteFixes.AddProfiles(ByName); RealBatFix.AddProfiles(ByName); BrokenVesselFix.AddProfiles(ByName); MageLordPhases.AddProfiles(ByName); }

        // Chaves = nomes do painel (CachedPrefabs: DisplayName do diario, ou o nome interno quando nao ha).
        public static readonly Dictionary<string, ArenaProfile> ByName = new Dictionary<string, ArenaProfile>
        {
            // Cliffs_02_boss. FSM 'Movement': Hover manda RETURN se Self X<36 | >76 | Self Y<30 e o estado
            // Return faz SetPosition(56,41) -> fora da arena isso dispara TODO frame (o "teleporte travado"
            // + invulneravel que o usuario viu, com a SpawnLeash puxando de volta). Pontos de voo P1..P7 sao
            // Vector3 absolutos. 'Distance Attack' compara Hero X/Y com 35/78/25.
            // Chao: o fantasma-NPC da sala tem o "Base Glow" em y~32.05 (aprox.; Gorb voa).
            ["Gorb"] = new ArenaProfile
            {
                Ref = new Vector2(56.06f, 32.05f),
                V3Vars = new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7" },
                SpawnAtArenaPosition = true,
            },

            // GG_Nosk_Hornet. FSM 'Hornet Nosk': Init -> HP -> 'Dormant', espera START (mandado pelo
            // 'Battle Control' do "Battle Scene" da sala, que nao vem no preload) -> nasce inerte.
            // Coordenadas absolutas: Set Pos (52.37,14.88), Swoop L/R x<38.51 / x>57.93, Shift Down y<19.42,
            // Roof Impact y=39, Roof Return y=26, invocacoes em X Min..X Max / Y Min..Y Max, Swoop Height.
            // Chao: 'Pt Jump' (poeira dos pes) em y~12.0. Centro X da arena ~47.5.
            ["Hornet Nosk"] = new ArenaProfile
            {
                Ref = new Vector2(47.5f, 12.0f),
                XVars = new[] { "X Min", "X Max" },
                YVars = new[] { "Y Min", "Y Max", "Swoop Height" },
                LeashUp = 28f, // o ataque do teto sobe ate y=39 (~26 acima do heroi) - precisa sair da tela
                WakeEvents = new[] { ("Hornet Nosk", "Dormant", "START") },
            },

            // Fungus1_04_boss ('Hornet Boss 1' = o "Hornet" do painel). FSM 'Control' (sem template):
            //  - A Dash: Self Y < Floor Y (27.55) -> 'Land Y' SetPosition y=Floor Y; Self Y > Roof Y (40.54) ->
            //    'Hit Roof' SetPosition y=Roof Y; Self X < Wall X Left (15.13) / > Wall X Right (37.9) ->
            //    'Wall L/R' SetPosition x=Wall X. Fora da arena isso dispara no 1o frame do dash e teleporta ela.
            //  - Aim Jump / Aim Sphere Jump: RandomFloat(Left X 16.06 .. Right X 36.53) - Self X.
            //  - Can Throw?: Self X vs Throw X L/R (22.51 / 30.16). In Air: Self Y < Sphere Y (33.8).
            //  - Componente ConstrainPosition: constrainX 15.07..37.96 (ABSOLUTO). Fora da arena ele joga ela
            //    pra x=15.07/37.96 TODO Update; a SpawnLeash (LateUpdate) projeta de volta na elipse e, com dx
            //    enorme, a projecao puxa o Y dela pro Y do ancora todo frame -> ela "flutua" numa altura fixa
            //    como se tivesse chao (o relato), e a gravidade acumulada no Rigidbody2D deixa o collider
            //    (hitbox) abaixo do sprite. ConstrainPosition agora e deslocado (SpawnFixups).
            // Chao: Floor Y 27.55 e onde o pivo dela fica em pe; o collider Idle ('Box Off Idle' y=-0.2645,
            // 'Box Size Idle' y=2.5647) tem a base 1.547 abaixo do pivo -> chao da arena = 26.0. Centro X =
            // meio das paredes (15.13..37.9) = 26.5.
            ["Hornet"] = new ArenaProfile
            {
                Ref = new Vector2(26.5f, 26.0f),
                XVars = new[] { "Left X", "Right X", "Throw X L", "Throw X R", "Wall X Left", "Wall X Right" },
                YVars = new[] { "Floor Y", "Roof Y", "Sphere Y", "Air Dash Height", "Min Dstab Height" },
                LeashUp = 14f, // o teto deslocado fica ~14.5 acima do chao (~11 acima do ancora)
            },

            // Fungus2_15_boss. FSM 'Mantis Lord' (principal e S1/S2) e 'Start' do 'Battle Sub'.
            // Land Y 8.99 com 'Pt Land' 3.07 abaixo do pivo -> chao ~5.92. Paredes em 18.2 / 41.77 -> centro
            // X ~30. "Away Pos" = posicao inicial (acima da arena, fora da tela).
            ["Mantis Lords"] = new ArenaProfile
            {
                Ref = new Vector2(30f, 5.92f),
                XVars = new[] { "Dash Hero L", "Dash Hero R", "Dash X L", "Dash X R", "Dstab X Max", "Dstab X Min",
                                "Throw Hero L", "Throw Hero R", "Wall X L", "Wall X R",
                                "Pincer Max X", "Pincer Min X", "Centre X" },
                YVars = new[] { "Dash Y", "Dstab Y", "Land Y", "Wall Y Max", "Wall Y Min" },
                SpawnAtArenaPosition = true,
                LeashUp = 25f, // a posicao de espera fica ~22.5 acima do chao
            },

            // GG_Mantis_Lords_V - copia Godhome da mesma sala ("Sisters of Battle"), mesma geometria/FSM do
            // lorde principal que a "Mantis Lords" normal.
            ["Sisters of Battle"] = new ArenaProfile
            {
                Ref = new Vector2(30f, 5.92f),
                XVars = new[] { "Dash Hero L", "Dash Hero R", "Dash X L", "Dash X R", "Dstab X Max", "Dstab X Min",
                                "Throw Hero L", "Throw Hero R", "Wall X L", "Wall X R",
                                "Pincer Max X", "Pincer Min X", "Centre X" },
                YVars = new[] { "Dash Y", "Dstab Y", "Land Y", "Wall Y Max", "Wall Y Min" },
                SpawnAtArenaPosition = true,
                LeashUp = 25f,
            },

            // Grimm_Nightmare. FSM 'Control': Init -> Set Balloon HP -> 'Dormant', espera WAKE (mandado pelo
            // 'Grimm Control', ausente). Toda escolha de posicao de ataque exige HeroX+-offset dentro de
            // Min X..Max X (69.7..100) senao CANCEL -> Retry no proximo frame, pra sempre; 'AD Fire' (mergulho)
            // so termina quando Self Y < Ground Y (8.3) -> fora da arena fica preso mergulhando/soltando fogo
            // em loop (o "ataque repetido" relatado). 'constrain_x' (Edge L/R 69.7/102.23) e 'Constrain Y'
            // (y<6.3 -> y=6.4) puxam ele pra arena original todo frame.
            // Chao: 'Dust Ground' 3.15 abaixo do pivo com Ground Y 8.3 -> chao ~5.15 (pilares nascem em y=5.1;
            // BoxCollider2D dele tem a base 2.95 abaixo do pivo -> 8.3 - 2.95 = 5.35, consistente).
            // Centro X ~85 (Balloon Pos 85.77).
            // (Abertura "travado no inicio ate a fase 2": vigia em NightmareGrimmFix.cs, alem do WakeEvents abaixo.)
            ["Nightmare King"] = new ArenaProfile
            {
                Ref = new Vector2(85f, 5.15f),
                XVars = new[] { "AD Max X", "AD Min X", "Max X", "Min X", "Edge L", "Edge R" },
                YVars = new[] { "Ground Y", "Mid Y" },
                WakeEvents = new[] { ("Control", "Dormant", "WAKE") },
            },

            // Grimm (Grimm_Main_Tent_boss, 'Grimm Boss'): MESMO template 'Control' do Nightmare King (mesma arena: Min X 69.7 / Max X
            // 100, AD Min/Max X 75/99, Ground Y 8.3, Balloon Pos (85.77, 13.5), FSMs 'constrain_x' Edge L/R 69.7/102.23 e 'Constrain Y'
            // y<6.3 -> 6.4). Sem perfil o 'constrain_x' puxava ele todo frame pra x<=102.23 (a copia nasce junto do heroi, longe da arena:
            // "invisivel" = fora da tela) e todos os ataques teleportam pra X/Y absolutos (FB Tele R/L 92..95/77..79, Hero X vs 88, AD y=15,
            // Uppercut y=17.3, Balloon Pos). Sem 'Dormant': Init -> HP Scale -> Set Balloon HP -> Bow -> Tele Out -> ataques, nao precisa de
            // WakeEvents. Mesmo Ref/variaveis do NKG (o FsmArenaShift ja desloca as constantes junto de GetPosition/SetPosition/RandomFloat).
            // Reverencia do inicio ('Bow'): GGCheckIfBossScene manda GG BOSS na hora (ramo Godhome forcado) -> 'GG Bow' esconde o Grimm
            // (SetMeshRenderer false) e espera o objeto 'GG Bow' da sala (ausente) - so saia por TOOK DAMAGE (-> Roar -> ataque do balao).
            // Desligar o GGCheck do 'Bow' deixa o caminho normal: Wait 2 s -> 'Bow Return' -> 'Tele Out' -> luta.
            // Espinhos do chao: ver GrimmSpikes.cs.
            ["Grimm"] = new ArenaProfile
            {
                Ref = new Vector2(85f, 5.15f),
                XVars = new[] { "AD Max X", "AD Min X", "Max X", "Min X", "Edge L", "Edge R" },
                YVars = new[] { "Ground Y", "Mid Y" },
                DisableActions = new[] { ("Control", "Bow", "GGCheckIfBossScene") },
            },

            // GG_Hollow_Knight ("HK Prime"). FSM 'Control' usa template 'hk_prime' (sharedassets447 #160); as
            // acoes vem dele, as variaveis com showInInspector vem da instancia. Coordenadas absolutas:
            //  - TelePos Counter/Slash/Dash/SmallShot/Tendril: Tele X = HeroX +- adder tem que cair em
            //    TeleRange Min..Max (30.03..60.49) senao CANCEL -> TeleLoop (ate 300 tentativas) -> teleporte
            //    cancelado; TelePos Focus: RandomFloat(44.21..47.54) -> Tele In SetPosition x (mundo).
            //  - TelePos Dstab: so se Hero Y < 9.31, clamp 31.36..59.08, SetPosition y=15.38.
            //  - In Air/Dstab Air/Stun Air: aterrissa quando Self Y < Stun Land Y (9.2); Land/Stomp Land fazem
            //    SetPosition y=Stun Land Y (teleporta pro chao da arena ORIGINAL). Plumes: SetPosition y=Plume Y (4.2).
            //  - Pos Check: Self X < 40.21 / > 51.54. Aim Jump: RandomFloat(Left X 25.84 .. Right X 64.47).
            //  - Componente ConstrainPosition: constrainX 30..61 (ABSOLUTO) - deslocado em SpawnFixups.
            // (As constantes junto de GetPosition/SetPosition sao deslocadas sozinhas pelo FsmArenaShift.)
            // Chao: EdgeCollider2D do tilemap em y=5 de x=29 a 62, centro X 45.5. O Rigidbody2D dele e
            // KINEMATIC no prefab (m_BodyType=1) e so vira dinamico em Dstab/Stun/After Tele: ate la ele fica
            // exatamente na altura em que nasceu. O collider 'Colliders/Idle' tem a base 4.125 abaixo do pivo
            // -> em pe o pivo fica em 9.125 = a posicao original (52.33, 9.13) = Stun Land Y. Nasce nessa
            // posicao deslocada (pes no chao real).
            ["Pure Vessel"] = new ArenaProfile
            {
                Ref = new Vector2(45.5f, 5.0f),
                XVars = new[] { "Left X", "Right X", "TeleRange Min", "TeleRange Max" },
                YVars = new[] { "Stun Land Y", "Plume Y" },
                SpawnAtArenaPosition = true,
            },

            // GG_Radiance ('Boss Control/Absolute Radiance'). O congelamento relatado (scratchpad\FsmDumpTool\
            // out\radiance_scene.txt):
            //  - (Correcao posterior: o SendEventByName ORBS do A1 Choice e OFF no asset - o dump antigo nao mostrava acoes
            //    desligadas; ORBS e uma das 8 escolhas do SendRandomEventV3, ver PENTES abaixo. O resto vale quando ORBS sai.)
            //    'Attack Choices' A1 Choice SEMPRE comeca mandando ORBS pra si mesma -> Orb Wait -> Please Cast ->
            //    'Control' Casting manda CAST READY -> 'Attack Choices' Orbs manda ORBS -> 'Attack Commands'
            //    Orb Antic -> Orb Pos: Fireball Pos = RandomFloat(Orb Min X 49 .. Orb Max X 72, Orb Min Y 22 ..
            //    Orb Max Y 29) (ABSOLUTO) e DistanceBetweenPoints(Fireball Pos, Self Pos) < 3 ou > 11 -> RESET ->
            //    'Reset Orb' (sem acoes) -> Orb Pos de novo. Fora da arena a distancia e sempre > 11 -> loop
            //    eterno, nunca manda ATTACK END -> 'Attack Choices' nunca manda CAST END -> 'Control' fica pra
            //    sempre no 'Casting' (pose de cast, sem atacar). 'Phase Control' entao fica parado em 'Set Phase 3'
            //    esperando RAGE START (que so o 'Control' manda no Rage1 Start) -> nada mais reage ao HP.
            //    O HealthManager esta normal (hp 3000, invincible 0, PolygonCollider2D trigger na camada 11):
            //    ela estava tomando dano, so a IA estava travada.
            //  - Teleportes: First Tele (60.63,27) e Rage1 Tele (60.63,28.3) via SetFsmVector3 'Destination'
            //    constante; Set Dest / Set Dest 2: RandomFloat(A1 X Min 49.82 .. A1 X Max 71.78) e
            //    RandomFloat(27 .. 29.17) -> Target Y. Tele 1..10 (arena 2) via SetVector3Value Destination.
            //  - 'Climb Plats1' chama HeroController.SetHazardRespawn(Respawn Pos), com Respawn Pos = GetPosition de
            //    um objeto da sala que nao existe -> (0,0,0): o heroi passaria a renascer de espinhos/acido na
            //    ORIGEM do mundo. Essa acao e desligada.
            // Fase de plataformas ('Phase Control' Check 3: HP < 1850 -> STUN 1). 'Control': Stun1 Start (collider
            // off) -> Tendrils1 -> Stun1 Roar -> Stun1 Out (SetMeshRenderer FALSE: ela some, mas continua ativa no
            // mesmo lugar) -> Climb Plats1 -> 'Abyss Antic' (CameraShake.RumblingMed = TRUE) -> 'Abyss Up', que so
            // ATIVA o 'Boss Control/Phase2 Detector' (irmao dela, nao vem no preload; FSM 'Detect': Trigger2dEvent
            // ENTER -> broadcast PHASE2 RISE quando o heroi chega na plataforma de cima, collider em (61.2,34.9)) e
            // espera PHASE2 RISE. 'Arena 2 Start' (o unico estado depois) e o UNICO que volta RumblingMed = false -
            // a FSM 'CameraShake' do CameraParent fica no estado RumblingMed (ShakePositionV2 IsLooping=True) ate o
            // bool ficar false: o tremor infinito relatado. PhaseEvents manda o PHASE2 RISE (como se o heroi tivesse
            // subido) -> Arena 2 Start desliga o tremor, Tele SFX -> A2 Tele Choice -> 'Teleport' Arrive liga o
            // renderer/collider de volta no ponto Tele 1..10 -> ARENA 2 START pra 'Attack Choices'/'Phase Control'.
            //  - Arena 2 (plataformas): Tele 1..10 (x 40.98..79.5, y 37..49.7) e 'Arena 2 Start' SetFsmFloat Orb
            //    Min/Max X 38.9/78, Y 34.6/50.9 (absolutos; sem deslocar = o mesmo loop eterno de 'Orb Pos' que
            //    congelava a fase 1). Chao = topo da plataforma de chegada 'Plat Sets/Hazard Plat/Radiant Plat Wide
            //    (4)' (y 32.5 + Colliders 0.64 + BoxCollider2D offset -0.372 + size/2 0.492 = 33.26), logo abaixo do
            //    Phase2 Detector.
            //  - Subida final ('Phase Control' Check 4: HP < 1100 -> Ascend Ready): Ascend Tele (62.94,158.65) ->
            //    Plat Setup -> 'Ascend Cast' espera Hero Y > 152 (heroi em cima das plataformas do topo 'Ascend Set/
            //    Radiant Plat Small (10)/(11)', y 151.82 + 0.159 + 0.469 = topo 152.45) -> Scream (orbes 53.8..71.6 /
            //    155.1..158.9) -> Tele 11..13 (y 158.65) -> Final Idle -> Check Pos (x 62.94) -> Death Ready. Com o
            //    chao da sub-arena = 152.45, "Hero Y > 152" vira "heroi em pe no chao" (a subida, que depende das
            //    plataformas da sala, e pulada) e as posicoes finais ficam ~6.2 acima do chao, como na arena 1.
            // Chao da arena 1: os espinhos de chao ('Spike Control/*/Radiant Spike', filho 'Floor Antic') ficam em
            // y=21.18 de x=48.5 a 71.5 -> chao 21.2, centro X ~60.6 (First Tele x=60.63).
            ["Absolute Radiance"] = new ArenaProfile
            {
                Ref = new Vector2(60.6f, 21.2f),
                XVars = new[] { "A1 X Min", "A1 X Max", "Orb Min X", "Orb Max X" },
                YVars = new[] { "Orb Min Y", "Orb Max Y", "Target Y" },
                // "Abyss Pos": destino do vazio no 'Abyss Tween' (SetVector3XYZ 56.5,152.38 - ver SubArenas e AbsoluteRadianceAbyss).
                V3Vars = new[] { "Destination", "Abyss Pos" },
                // 'Final Impact' (golpe final, agora alcancavel) grava no save SetPlayerDataBool killedFinalBoss /
                // newDataFinalBoss e SetPlayerDataInt killsFinalBoss=0 (diario da Radiancia) - mesma politica do
                // ForceBossSceneBranch: copia spawnada nao marca chefe como derrotado.
                //
                // MORTE (softlock depois do golpe final; scratchpad\FsmDumpTool\out\radiance_control_v2.txt, com os
                // FunctionCall do SendMessage decodificados). 'Death Ready' (Trigger2dEvent 'Nail Attack' -> FINAL HIT)
                // -> 'Final Impact' (HUD Canvas OUT, broadcast ALL CHARMS END, ApplyMusicCue 'None' = musica some,
                // golpe/anim 'Death', CameraShake RumblingMed=TRUE; termina no mesmo frame) -> 'Knight Break Antic':
                // FSM CANCEL pro heroi + SendMessage RelinquishControl / StopAnimationControl / AffectedByGravity(false)
                // + SetVelocity2d 0 + SetMeshRenderer(Hero)=false (heroi congelado no ar, invisivel, sem input) e um
                // Knight falso ('Death/Knight Split') voando pra (64.49,145.32) ABSOLUTO -> Knight Break (Knight Ball
                // SetParent null) -> Ball Down -> Abyss Tween -> 'Tendrils 2': GGCheckIfBossSequence falso -> STATUE ->
                // Statue Death 1 -> Statue Death 2 (desliga RumblingMed/Big; snapshot 'at None' = ambiente mudo) ->
                // 'Godseeker Dialogue' (texto preto + StartConversation; CONVO_FINISH so via dialogo) -> 'Return to
                // workshop' = EndGGBossScene, que fora do Godhome nao faz nada. NENHUM estado devolve o controle,
                // o renderer ou o HUD: no Godhome a cena troca. Resultado aqui: camera parada no heroi congelado no ar
                // olhando pra ela, heroi invisivel pra sempre (o noclip do DebugMod move o transform mas o renderer
                // continua desligado - exatamente o relato).
                // 1a correcao (antiga): 'Final Impact' FINISHED -> 'Statue Death 1' (pulava a cutscene do Knight).
                // AGORA (pedido do usuario: final igual ao original, mas sem "zerar"): a cutscene ORIGINAL do Hall of Gods roda
                // inteira - Final Impact -> Knight Break Antic (heroi congelado/invisivel; Knight falso no lugar dele, atirado pra
                // baixo dela) -> Knight Break (Knight Split quebra: Knight Ball + cacos do elmo, AverageShake) -> Ball Down (0.75s;
                // FSM 'Animate Down' do Knight Ball afunda 20) -> Abyss Tween (1.5s; o VAZIO 'Abyss Pit' - irmao dela na sala,
                // pre-carregado a parte e ligado so na cutscene, ver AbsoluteRadianceAbyss - sobe ate o chao real em 1s, engolindo
                // o Knight Ball) -> Tendrils 2 (fora de sequencia de panteao: STATUE) -> Statue Death 1 (3s, raio/tremor) -> Statue
                // Death 2 (4.5s, explosao, desliga RumblingMed/Big) -> 'Godseeker Dialogue' (sem acoes = estado final parado) ->
                // SpawnedBossFinish: devolve o heroi (HeroRestore) e remove a copia + Knight Ball/cacos desparentados.
                //  - Nada disso grava save: os unicos SetPlayerData* do final estao no 'Final Impact' (desligados abaixo). Os
                //    outros desligados mudariam estado GLOBAL que so a troca de cena do original desfaz (HUD fora, efeitos de
                //    amuleto mortos, musica/audio mudos, dialogo/EndGGBossScene do Godhome).
                //  - (SUBSTITUIDO - ver "FINAL DO PANTEAO 5 / SHADE LORD" abaixo: o final agora e o do P5, nao o da estatua. Antes:
                //    Redirect 'Tendrils 2' FINISHED -> 'Statue Death 1' deixava o ramo P5 inalcancavel.)
                //  - 'Knight Break Antic' iTweenMoveTo (64.49,145.32) ABSOLUTO -> WorldTweens (delta da sub-arena final), com o Y
                //    limitado ao chao real (+1.4 = pivo do Knight em pe): no original o Knight cai no abismo 7.1 abaixo das
                //    plataformas; aqui nao ha abismo e ele sumiria dentro do chao, fora da tela.
                //
                // FINAL DO PANTEAO 5 / SHADE LORD (pedido do usuario: o final com o Shade Lord no lugar do da estatua). O ramo e
                // escolhido SO pelo 'Tendrils 2': GGCheckIfBossSequence = BossSequenceController.IsInSequence (decompilado);
                // falso (Hall of Gods) -> STATUE -> estatua; verdadeiro (dentro de um panteao) -> nenhum evento, o estado
                // segue: UP pros Tendrils 2 do vazio, Wait 2.5 -> FINISHED. Ou seja, o P5 e simplesmente "o que acontece quando
                // o STATUE nao dispara". Correcao: o GGCheckIfBossSequence do 'Tendrils 2' e DESLIGADO (o Redirect antigo
                // FINISHED -> Statue Death 1 saiu) e o final do Hall of Gods fica substituido pelo P5. Caminho real (dump
                // FsmDumpTool\out\rad_all_endbg.txt, transicoes conferidas uma a uma):
                //   ... Abyss Tween -> Tendrils 2 (2.5s; SetPosition $Abyss Pit x=56.5 MUNDO = deslocado pelo FsmArenaShift) ->
                //   Abyss Final Burst (1s; FINAL BURST pro 'Final Burst' do Abyss Pit = liga a nevoa 'Final') -> Lord Appear (3s;
                //   ATIVA $Shade Lord = 'Boss Control/Shade_Lord', o Shade Lord gigante) -> Hand Grab (1s; ativa $Shade Lord Hand =
                //   'Boss Control/Shade Lord Hand', anim 'Grab' = a mao agarra ela) -> Break (1s, olho quebra) -> First Idle
                //   (ListenForAttack: o JOGADOR golpeia) -> Kill? (Slashes+1; < 9 -> Attack Idle (ListenForAttack) -> Shade Slash
                //   (0.6s; religa $Shade Slashes = 'Boss Control/radiance_slashes', os cortes de tela cheia) -> Hit (0.15s;
                //   GameManager.FreezeMoment(1) = hit-stop de ~0.1s, efeitos de pool) -> Kill?) ; 9 golpes -> Kill Hit (2s; broadcast
                //   KILL HIT - so o Hollow Shade/Knight Ball dela escutam) -> Final Stream (3s; END pra mao = anim 'Leave' e some) ->
                //   Final Explode (8s; ATIVA $Final Explode = 'Boss Control/Final Explode': black_solid ~52x26 = tela preta) ->
                //   'Ending Scene' (ChangeToScene/SetStaticVariable/EndGGBossScene DESLIGADOS = estado final parado) ->
                //   SpawnedBossFinish devolve o heroi e remove a copia (a tela preta, o Shade Lord etc. vao junto - ver
                //   AbsoluteRadianceAbyss). 'Tendrils 3'/'Yank Down'/'Drag'/'Final Black'/'Slash Recover' NAO tem transicao de
                //   entrada nesta FSM (restos do template da Radiance simples) = inalcancaveis.
                //  - Os 4 objetos acima sao IRMAOS dela (Init: FindChild($Boss Control, ...) = null aqui), pre-carregados a parte e
                //    ligados so na cutscene, no mesmo esquema do Abyss Pit; $Tendrils 2 (FindChild($Abyss Pit) no Init, null) e ligado
                //    ao filho do Abyss Pit da copia.
                //  - Auditoria do ramo P5 (todas as acoes de todos os estados alcancaveis): NENHUM SetPlayerData*/save. Efeito
                //    global/de cena: so 'Final Explode' TransitionToAudioSnapshot ('at None' = ambiente mudo) e 'Ending Scene'
                //    (ChangeToScene, SetStaticVariable ggCinematicEnding, EndGGBossScene) - todos desligados abaixo. O resto e
                //    visual/audio/tremor (RumblingBig liga no Kill Hit e desliga no Final Explode), hit-stop do FreezeMoment
                //    (coroutine do GameManager que volta o timeScale sozinha) e broadcasts KILL HIT/REMINDER ATTACK (so ouvintes
                //    dentro dela). Nenhum iTween/SetPosition de coordenada absoluta alem do 'Tendrils 2' (x 56.5, deslocado);
                //    Shade Lord/mao/cortes/explosao nao sao movidos pela FSM - so nascem nas posicoes da sala original, deslocadas
                //    pelo delta da sub-arena final (mesma relacao com ela que no original).
                DisableActions = new[]
                {
                    ("Control", "Climb Plats1", "CallMethodProper"),
                    ("Control", "Final Impact", "SetPlayerDataBool"),
                    ("Control", "Final Impact", "SetPlayerDataInt"),
                    ("Control", "Final Impact", "SendEventByName:OUT"),            // HUD Canvas 'Slide Out'
                    ("Control", "Final Impact", "SendEventByName:ALL CHARMS END"), // tecelas, grimmchild etc. sumiriam
                    ("Control", "Final Impact", "ApplyMusicCue"),                  // MusicCue 'None' (resources.assets #19414)
                    // Escolha do ramo: sem isto, fora de um panteao -> STATUE (final da estatua). Desligado -> ramo P5 (Shade Lord).
                    ("Control", "Tendrils 2", "GGCheckIfBossSequence"),
                    // ramo P5 / Shade Lord (o caminho usado agora):
                    ("Control", "Final Explode", "TransitionToAudioSnapshot"),     // snapshot 'at None' (#3863)
                    ("Control", "Ending Scene", "CallMethodProper"),               // GameManager.ChangeToScene (final do jogo)
                    ("Control", "Ending Scene", "SetStaticVariable"),              // ggCinematicEnding
                    ("Control", "Ending Scene", "EndGGBossScene"),                 // encerraria uma luta de Godhome de verdade
                    // ramo Hall of Gods (inalcancavel com o GGCheckIfBossSequence desligado) - defesa:
                    ("Control", "Statue Death 2", "TransitionToAudioSnapshot"),    // snapshot 'at None' (#3863)
                    ("Control", "Godseeker Dialogue", "GGCheckIsBossRushMode"),
                    ("Control", "Godseeker Dialogue", "SetTextMeshProColor"),      // deixaria o texto de dialogo preto
                    ("Control", "Godseeker Dialogue", "SetTextMeshProAlignment"),
                    ("Control", "Godseeker Dialogue", "CallMethodProper"),         // DialogueBox.StartConversation
                },
                FinishStates = new[] { ("Control", "Ending Scene", 0.5f) },
                // Se o ramo da estatua for alcancado mesmo assim (ex. o GGCheckIfBossSequence nao foi desligado), ele roda como
                // antes ate 'Godseeker Dialogue', que esta FORA desta lista -> vigia "fora do caminho" (3 s) encerra e restaura.
                HeroCutsceneStates = new[]
                {
                    ("Control", "Final Impact"), ("Control", "Knight Break Antic"), ("Control", "Knight Break"),
                    ("Control", "Ball Down"), ("Control", "Abyss Tween"), ("Control", "Tendrils 2"),
                    ("Control", "Abyss Final Burst"), ("Control", "Lord Appear"), ("Control", "Hand Grab"), ("Control", "Break"),
                    ("Control", "First Idle"), ("Control", "Kill?"), ("Control", "Attack Idle"), ("Control", "Shade Slash"),
                    ("Control", "Hit"), ("Control", "Kill Hit"), ("Control", "Final Stream"), ("Control", "Final Explode"),
                    ("Control", "Statue Death 1"), ("Control", "Statue Death 2"),
                },
                // ListenForAttack: o original espera o jogador golpear 9x (First Idle + 8 Attack Idle). Sem input, SLASH sozinho.
                CutsceneInputs = new[] { ("Control", "First Idle", "SLASH", 3f), ("Control", "Attack Idle", "SLASH", 1.2f) },
                // Natural ~26 s fixos (Knight ~1.5 + 0.75 + 1.5 + 2.5 + 1 + 3 + 1 + 1 + 2 + 3 + 8 + 0.5) + golpes; pior caso so com
                // SLASH automatico: + 3 + 8 x (1.2 + 0.6 + 0.15) = ~45 s. Estado mais longo: Final Explode 8 s (< StallSeconds 10).
                CutsceneTimeout = 75f,
                WorldTweens = new[] { ("Control", "Knight Break Antic", 1.4f) },
                // Abyss Antic (2s de tremor) + 2.5s "subindo" -> PHASE2 RISE.
                PhaseEvents = new[] { ("Control", "Abyss Up", "PHASE2 RISE", 2.5f) },
                SubArenas = new[]
                {
                    ("Control", 33.26f, new[] { "Arena 2 Start", "Tele 1", "Tele 2", "Tele 3", "Tele 4", "Tele 5",
                                                "Tele 6", "Tele 7", "Tele 8", "Tele 9", "Tele 10" }),
                    // 'Abyss Tween': destino do vazio (Abyss Pos, ver V3Vars) y=152.38 = 0.07 abaixo do topo das plataformas finais
                    // -> 0.07 abaixo do chao real (o degrade do vazio vai ate ~1.3 acima do chao, onde o Knight falso fica).
                    ("Control", 152.45f, new[] { "Ascend Tele", "Ascend Cast", "Scream", "Tele 11", "Tele 12", "Tele 13",
                                                 "Knight Break Antic", "Abyss Tween" }),
                },
                // PENTES DE ESPADAS ("espinhos de cima pra baixo" e "de um lado pro outro" que nao apareciam).
                // scratchpad\FsmDumpTool\out\radiance_attack_fsms_v3.txt + radiant_nail_407.txt (dump com as acoes "(off)"
                // agora lidas certo - o dump antigo nunca marcava acao desligada):
                //  - 'Attack Choices' A1 Choice: SendRandomEventV3 escolhe NAIL TOP SWEEP (peso 0.75) / NAIL L SWEEP / NAIL R
                //    SWEEP (0.5 cada) (o SendEventByName ORBS antes dele e OFF no asset - bytes do level459: actionEnabled=[0,1];
                //    entao ela escolhe normalmente, nao "sempre orbes"). Nail Top Sweep manda COMB TOP x4, Nail L/R Sweep manda
                //    COMB L/R x3; arena 2: L or R Choice -> COMB L2/R2. 'Attack Commands' Comb Top/L/R/L 2/R 2 (e 'Control'
                //    Rage Comb na fase de furia, TL/TR na intro): SpawnObjectFromGlobalPool('Radiant Nail Comb' = sharedassets407
                //    #92, o mesmo arquivo do 'Radiant Nail' #91 do Nail Fan e da 'Radiant Orb' #77, que funcionam) -> var
                //    'Attack Obj' -> SetFsmInt Type (1..8).
                //  - FSM 'Control' do pente: Init (NextFrameEvent) -> Type -> TL/TR/Top/L/R/Top 2/L 2/R 2 = SetPosition MUNDO
                //    ABSOLUTO: Top (59.7..61.7, 33.5) rot 180, L (42.46, 26.73) rot 270, R (78.46, 28.73) rot 90, TL (46,43),
                //    TR (74,43), Top 2 (60.7,58), L 2 (35.4,36.73), R 2 (84.8,38.73) -> Spawn Centre/RG*/LG* (as espadas nascem
                //    relativas ao pente e viram filhas de 'Nails') -> Antic (0.4 s) -> Tween (iTweenMoveBy 'Nails' 0,50 local).
                //    Ou seja: o ataque DISPARA, mas o pente aparece na GG_Radiance original, longe da tela. Mesma causa nos dois
                //    ataques. Sem acao desligada/pai/objeto da sala envolvido (o 'Radiant Nail' nao tem FSM de "pai sumiu").
                // Correcao: PooledAttackShift (o pente e do pool global, compartilhado com a luta real - as constantes dele nao
                // sao tocadas): no 'Antic' (ja posicionado, nada se moveu ainda) a raiz do pente e transladada pelo delta da
                // arena 1 (tipos 1..5) ou da arena 2 (6..8: chao 33.26, igual SubArenas acima).
                // ("Espinhos nascendo perto demais dela", investigado: as posicoes do pente sao CONSTANTES da arena no
                // original - Top x 59.7..61.7 qualquer que seja o X dela - e ela teleporta pra constantes da mesma arena
                // (Set Dest x 49.82..71.78 y 27..29.17, Rage 60.63,28.3), deslocadas pelo MESMO ctx.Delta. Pente - Radiancia
                // = exatamente o do original (Top 4.3..6.5 acima do pivo dela; ModLog real: pente y 52.3, chao 40). Ancorar
                // o pente nela seria ERRADO. Diferenca nao-original: a camera - CamLock A1 (PhysProbe res\rad_camlocks.txt)
                // trava y=25 (chao+3.8), topo da tela ~33.3 = o pente nasce na borda de cima; aqui a camera segue o heroi.
                // PooledAttackPlacement loga camera/copia/equivalente original pra medir isso.)
                PooledAttacks = new[]
                {
                    new PooledAttack
                    {
                        Spawners = new[]
                        {
                            ("Attack Commands", "Comb Top"), ("Attack Commands", "Comb L"), ("Attack Commands", "Comb R"),
                            ("Attack Commands", "Comb Top 2"), ("Attack Commands", "Comb L 2"), ("Attack Commands", "Comb R 2"),
                            ("Control", "Rage Comb"), ("Control", "TL"), ("Control", "TR"),
                        },
                        StoreVar = "Attack Obj",
                        Fsm = "Control",
                        TypeVar = "Type",
                        ReadyState = "Antic",
                        TypeFloorY = new Dictionary<int, float> { [6] = 33.26f, [7] = 33.26f, [8] = 33.26f },
                    },
                },
                // ORBES DA FASE FINAL ("a animacao de invocar aparece mas a orbe nunca aparece"). scratchpad\FsmDumpTool\out\
                // rad_attackcmds_final_endbg.txt + radiant_orb_407_endbg.txt (FsmDumpTool recompilado: actionEnabled lido certo, tudo ON):
                //  - 'Control' Scream: SendEventByName FINAL ORBS + SetFsmFloat Orb Min/Max X/Y (53.8/71.6, 155.1/158.9; deslocados
                //    pela sub-arena final 152.45 - ja funcionava). 'Attack Commands' GLOBAL FINAL ORBS -> Set Final Orbs (Final Orbs
                //    = true) -> Orb Pos -> Orb Summon (Shot Charge no Fireball Pos = a "animacao" que o usuario ve) -> Spawn Fireball
                //    (SpawnObjectFromGlobalPool 'Radiant Orb' sharedassets407 #77 em $Fireball Pos -> $Projectile, FIRE) -> 'Final
                //    Event': Final Orbs true -> SendEventByName FINAL pro $Projectile -> Orb Pause -> Orb Check -> FinalOrb Pause ->
                //    Orb Pos... (so orbes ate a morte). Nas arenas 1/2 Final Orbs = false: FINAL nunca e mandado (por isso la funciona).
                //  - A 'Radiant Orb' tem uma 2a FSM, 'Final Control': Idle --FINAL--> Check: GetPosition + 4 FloatCompare everyFrame
                //    contra Min X 45.97 / Max X 79.79 / Min Y 149.96 / Max Y 165.78 (ABSOLUTOS, a caixa da arena final) -> END ->
                //    RecycleSelf. A orbe da copia nasce no Fireball Pos DESLOCADO (fora dessa caixa) -> recicla na hora. E sincrono
                //    (PlayMaker Fsm.DoTransition -> UpdateStateChanges quando o evento vem de outra FSM): tudo acontece dentro do
                //    OnEnter do 'Final Event' da copia, no mesmo frame do spawn - a orbe nunca chega a ser desenhada.
                // Correcao: antes do SendEventByName FINAL, os limites da 'Final Control' DAQUELA instancia sao deslocados pelo delta
                // da sub-arena final (mesmo delta do Scream -> a orbe nasce dentro da caixa, como no original); voltam ao valor
                // original quando a orbe e reciclada (variaveis de FSM nao sao resetadas no OnEnable/OnDisable do PlayMaker).
                PooledBounds = new[]
                {
                    new PooledAttackBounds
                    {
                        Hook = ("Attack Commands", "Final Event"),
                        StoreVar = "Projectile",
                        OnlyIfBool = "Final Orbs", // o BoolTest do proprio 'Final Event': so a fase final manda FINAL
                        Fsm = "Final Control",
                        XVars = new[] { "Min X", "Max X" },
                        YVars = new[] { "Min Y", "Max Y" },
                        FloorY = 152.45f,
                    },
                },
            },

            // Fungus3_archive_02_boss ('Mega Jellyfish' = "Uumuu" do painel). Por que nao aparecia mesmo sem o
            // DeactivateIfPlayerdataTrue (defeatedMegaJelly):
            //  - MeshRenderer serializado DESLIGADO, BoxCollider2D/CircleCollider2D desligados. FSM 'Mega
            //    Jellyfish': Init -> 'Sleep', que espera BATTLE START (mandado por BROADCAST pelo FSM 'Control'
            //    do 'Battle Scene' da sala quando o heroi entra no trigger - nao vem no preload) -> Wake Pause ->
            //    Wake Rumble -> Burst (iTweenMoveBy +23 em y, liga os colliders: ele sobe de dentro do acido) ->
            //    Music -> 'Start' (o UNICO SetMeshRenderer true) -> Idle (persegue o heroi). Sem o evento ele
            //    fica no Sleep pra sempre, invisivel e intocavel.
            //  - Componente ConstrainPosition: X 32.7..73.16, Y 97.94..134.93 (ABSOLUTO) -> todo Update ele era
            //    jogado pra y>=97.94 (dezenas de unidades acima da maioria das salas).
            //  - Quirrel ('Quirrel Land', filho que se desparenta no Init) aparece em pontos absolutos (BL/BR
            //    42.8/62.5,117.82; TL/TR 38.63/66.89,122.92 ...); o lado e escolhido comparando Hero X/Y com
            //    Hero Mid X/Y (53.3, 121.23). O golpe dele (filho 'Quirrel Slash' do Uumuu) e o que deixa o
            //    Uumuu vulneravel (SetInvincible false no estado Slash) - funciona em qualquer sala.
            // Ref: centro X = Hero Mid X 53.3; chao = plataformas de baixo onde o Quirrel pousa (pivo 117.82,
            // ~1.4 acima dos pes como o Knight -> ~116.4; aproximado, so afeta onde o Quirrel aparece). Nasce na
            // posicao original (53.21, 99.91) deslocada = ~16.5 abaixo do chao, dormindo (sem collider) e sobe
            // 23 no Burst ate ~6.5 acima do chao, como na sala original (sai "de baixo").
            ["Uumuu"] = new ArenaProfile
            {
                Ref = new Vector2(53.3f, 116.4f),
                XVars = new[] { "Hero Mid X", "X Min", "X Max" },
                YVars = new[] { "Hero Mid Y", "Y Min", "Y Max" },
                SpawnAtArenaPosition = true,
                LeashDown = 21f, // nasce ~20 abaixo do ancora (embaixo do chao) antes do Burst
                WakeEvents = new[] { ("Mega Jellyfish", "Sleep", "BATTLE START") },
            },

            // Fungus2_32 ('Warrior/Ghost Warrior Hu' = "Elder Hu"). scratchpad\FsmDumpTool\out\hu_scene.txt:
            //  - O ataque dele (os aneis que caem do teto) NAO e filho dele: e o objeto RAIZ 'Ring Holder' da sala
            //    (filhos '1'..'17', inativos, cada um com FSM 'Control' + caixas DamageHero). FSM 'Attacking' Init:
            //    FindGameObject('Ring Holder') + FindChild('1'..'17') -> vars Ring 1..17. Fora do Fungus2_32 o
            //    GameObject.Find devolve null -> todos os ActivateGameObject dos estados '1 1'..'2 6'/'M 1'..'M 9' viram
            //    no-op: ele faz a anim de ataque e bate no chao, mas nenhum anel aparece (o "poder nao funciona").
            //    Correcao: o 'Ring Holder' e pre-carregado a parte (EncounterGroups) e ligado na var 'Ring Holder' da
            //    copia (ElderHuRings); o FindGameObject e desligado e o FindChild original acha os aneis nele.
            //    Idem 'Warrior/Full Dust' (irmao dele, poeira do golpe; no bundle a var aponta pra outro objeto e e
            //    zerada pelo SceneLinkSanitizer).
            //  - Coordenadas absolutas: 'Movement' P1 (35.7,8.3)/P2 (63.87,8.3) (alvos do ChaseObject; P3..P7 sao
            //    restos copiados do Gorb), troca de lado com Self X < 36 / > 63, Set Warp Self X vs 50.15, Choose L/R
            //    Hero X vs 45/55 + SetVector3XYZ Warp Pos (39|60, 9.91); 'Attacking' Mega Warp Out Ring Holder x=50.2,
            //    Choose Pos Hero X vs 42.42/54.89, Set L/R SetPosition (39|60, 9.91); aneis: Check Pos Self X fora de
            //    32.35..66.32 -> CANCEL, Down ate Self Y < 6.01, Land y=5.72, Reset y=13.
            //  - 'Set Ghost PD Int': ZERO HP (HealthManager.Die) -> SetPlayerDataInt elderHuDefeated=1. No save em que o
            //    fantasma ja foi absorvido (=2, 'Ghost Warrior NPC' Collected) isso volta pra "derrotado, essencia nao
            //    coletada" (a sala oferece +100 essencia de novo). Mesma politica do resto: copia nao grava progresso.
            //    (Agora tambem desligado de forma generica pros 7 Dream Warriors em FsmArenaShift.DisableGhostDefeatSave; as
            //    ultimas palavras + essencia da copia vem do 'Ghost Warrior NPC' pre-carregado - ver GhostWarriorReward.)
            // Chao: EdgeCollider2D dos Chunk 0 0/0 1/0 2 em y=2 de x=0 a 66 (parede direita x=66); a 'Box Impact' do anel
            // (escala 1.5, offset -1.97, size 0.98) pousado em 5.72 tem a base em 2.03. Centro X = meio do Check Pos dos
            // aneis (32.35..66.32) ~49.3. Nasce na posicao original (50.15, 9.91) deslocada (~7.9 acima do chao).
            ["Elder Hu"] = new ArenaProfile
            {
                Ref = new Vector2(49.3f, 2.0f),
                V3Vars = new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7", "Warp Pos" },
                SpawnAtArenaPosition = true,
                DisableActions = new[]
                {
                    ("Attacking", "Init", "FindGameObject"),
                    ("Set Ghost PD Int", "Set", "SetPlayerDataInt"),
                },
            },

            // Ruins2_03_boss ('Battle Control/Black Knight 1' = "Watcher Knight"). scratchpad\FsmDumpTool\out\watcher_scene.txt:
            //  - FSM 'Black Knight': Pause -> Init -> Init Facing -> 'Rest' (SEM acoes), que so sai com WAKE (-> Bugs In ->
            //    Cloud Stop -> Bugs In End -> Title? -> Roar Start -> Roar -> Music? -> Roar End -> Set Time -> Idle) ou BATTLE
            //    START (-> Set Battle Inert -> Rest). Quem manda o WAKE e a FSM 'Battle Control' do pai 'Battle Control' (estado
            //    'Knight 1': SendEventByName BK 1 WAKE), que nao vem no preload. Estado inicial serializado do cavaleiro: Rigidbody2D
            //    KINEMATIC (m_BodyType=1), HealthManager invincible=1, layer 19. So o WAKE liga a fisica ('Roar Start':
            //    SetIsKinematic2d false + SetCollider true) e o deixa vulneravel ('Roar End': SetInvincible false, SetLayer 11, tag
            //    'Spell Vulnerable', SetDamageHeroAmount 1). Sem ele: estatua flutuando onde nasceu, invencivel, pra sempre.
            //  - Os outros 5 cavaleiros da sala (Black Knight 2..6, identicos fora o 'Show Title') sao acordados em sequencia pela
            //    mesma 'Battle Control' - ver WatcherKnightsEncounter.
            //  - Nenhuma coordenada absoluta: Aim usa Hero X - Self X (relativo, FloatClamp -22..22); 'Roar Start' SetPosition so
            //    mexe no z ($Random). Nao ha ConstrainPosition. Ref so entra no Delta, que nada usa aqui (x = meio dos 6 cavaleiros
            //    na sala original, y = altura deles); o perfil existe pelo WakeEvents (e pela ancora da coleira no chao real).
            ["Watcher Knight"] = new ArenaProfile
            {
                Ref = new Vector2(43.7f, 71.6f),
                WakeEvents = new[] { ("Black Knight", "Rest", "WAKE") },
            },

            // Dream_Mighty_Zote ('Grey Prince' = "Grey Prince Zote" do painel). O intro/rugido ja e destravado pelo WakeTable do
            // DormantBossFix (ZOTE TITLE END) - intocado. Isto e o RESTO da luta. Dump novo da FSM (FsmDumpTool, com acoes "(off)"):
            // 'Control' + 'Constrain X'; dump do prefab da onda: sharedassets399 #48 'Shockwave Wave Z'.
            //  - ONDA DE CHOQUE ("o ataque que sai do chao quando ele bate no chao" nao funcionava): Land Waves / Charge Slam /
            //    Slash Waves R/L fazem SpawnObjectFromGlobalPool('Shockwave Wave Z') + SetPosition(onda) y = $Shockwave Y (MUNDO);
            //    Ft Waves / Slash Waves usam a constante y = 4.8. A FSM 'shockwave' da onda, no estado 'Move', faz todo frame
            //    RayCast2d de (+-1, +1) local pra baixo, distancia 2, camada 8 (Terrain) -> On Ground; On Ground false -> HIT ->
            //    End Pause -> End Particle -> RecycleSelf (e Trigger2dEventLayer camada 8 -> WALL, idem). Ou seja a onda so vive
            //    com chao entre y-1 e y+1 dela. Na sala atual y = 4.9 absoluto nao tem chao nenhum -> morre no 1o frame (so um
            //    jorro em y=4.9, longe da tela). Chao da arena: EdgeCollider2D 'Chunk 0 0'/'Chunk 0 1' em y=5 de x=6 a 47 -> a
            //    onda nasce 0.1 abaixo do chao (raycast de 0.9 acima -> acerta).
            //    "Shockwave Y nao da pra deslocar uma vez so": a variavel e 0 no prefab e so recebe 4.9 por SetFloatValue
            //    CONSTANTE nos estados 'Level 1'/'Level 2'/'Level 3'/'4+' (Pause -> Init -> Level Check -> Level N -> Dormant), que
            //    rodam no 1o frame da FSM, DEPOIS do FsmArenaShift. Nao muda mais na luta (unicas escritas: essas 4 + nenhum
            //    GetPosition) -> nao precisa de deslocamento por ataque: basta deslocar a constante dessas 4 acoes (SetFloatYVars).
            //    As constantes 4.8 (SetPosition de mundo) ja sao deslocadas pelo FsmArenaShift.
            //  - 'Constrain X' (template constrain_x): GetPosition everyFrame + FloatCompare com Edge L 7.19 / Edge R 45.61
            //    ABSOLUTOS -> SetPosition x = Edge (teleporte todo frame pra arena original, e a coleira puxando de volta).
            //    'Control' Aim Jump: RandomFloat(Left X 7.19 .. Right X 45.61) = alvo do pulo ABSOLUTO.
            //  - Outras constantes absolutas (deslocadas sozinhas pelo FsmArenaShift): FT Fall SetPosition y=22, JS In Air Self Y >
            //    17.5 (ponto do pisao), Shift Dir Self Y 12.7/39.74, To CT / CT Left/Right/In (6.54/46.32, 53.94/3.09, 9.59/8.75 -
            //    ramo inalcancavel: o SendRandomEvent do 'Charge Through?' e OFF no asset).
            //  - 'Jump X' recebe GetPosition(Hero) em Aim Jump / JS Aim (entraria como "X absoluto") mas vira Hero X - Self X e o
            //    Aim Jump faz FloatClamp -17..17 nele: RELATIVO (RelativeVars), senao o clamp do pulo seria deslocado.
            // Chao 5.0 (pivo dele em pe: BoxCollider2D offset.y -1.797, size.y 3.75 -> base 3.67 abaixo; nasce em y 8.76 = 5 + 3.76).
            // Centro X = meio das paredes (6..47) = 26.5. Coleira: o pisao dispara em y > 17.5 = 9.1 acima da ancora (chao + pivo do
            // heroi + 2) e o FT Fall cai de y=22 = 13.6 acima dela -> LeashUp padrao (9) nunca deixaria o pisao acontecer.
            ["Grey Prince Zote"] = new ArenaProfile
            {
                Ref = new Vector2(26.5f, 5.0f),
                XVars = new[] { "Left X", "Right X", "Edge L", "Edge R" },
                SetFloatYVars = new[] { "Shockwave Y" }, // (a variavel em si e 0 ate o 'Level N' - nao vai em YVars)
                RelativeVars = new[] { "Jump X" },
                LeashUp = 18f,
            },

            // Dream_Final_Boss ('Boss Control/Radiance' = a "Radiance" SIMPLES do painel, a do final no sonho - NAO e a
            // "Absolute Radiance" do GG_Radiance acima: objeto/FSMs/cena separados, perfil proprio). Dump novo:
            // scratchpad\FsmDumpTool\out\rad1\boss_control.txt (+ BundleProbe\out\plainrad_fsm_v7.txt, o prefab do bundle de
            // preload: FSMs/vars internas intactas, sem referencia externa corrompida - SceneLinkSanitizer nao precisa).
            //  - NAO e chefe "adormecido": 'Control' Init (Wait 1.25) -> Set Arena 1 -> First Tele -> ... -> Arena 1 Start ->
            //    Arena 1 Idle, sozinha. Nenhum GGCheckIfBossScene / ramo de sonho / DeactivateIfPlayerdata* / PersistentBoolItem.
            //  - O congelamento relatado ("ficou parada"): Player.log do usuario tem 2x "Radiance(Clone)(Clone) : Attack Commands :
            //    Loop count exceeded maximum: 1000". 'Attack Commands' Orb Pos: Fireball Pos = RandomFloat(Orb Min X 49 .. Orb Max X
            //    72, Orb Min Y 22 .. Orb Max Y 29) ABSOLUTO e DistanceBetweenPoints(Fireball Pos, Self Pos) < 3 ou > 11 -> RESET ->
            //    'Reset Orb' (sem acoes) -> Orb Pos no mesmo frame. Perto do heroi a distancia e sempre > 11 -> 1000 voltas ->
            //    PlayMaker Fsm.EnterState (decomp3\Fsm.cs): Owner.enabled = false = a FSM 'Attack Commands' fica DESLIGADA pra
            //    sempre. Nunca manda ATTACK END -> 'Attack Choices' preso em 'Orbs' (nunca manda CAST END) -> 'Control' preso em
            //    'Casting' (pose de cast, parada) e todo ataque seguinte (pentes, raios, leque) morre junto. Mesma IA da Absolute
            //    (mesmo layout de arena: espinhos de chao 'Spike Control/*/Radiant Spike' em y=21.18 de x=48.49 a 72.49, First Tele
            //    (60.63,27), Rage1 Tele (60.63,28.3), Set Dest 49.82..71.78 / 27..29.17, Tele 1..10 e 'Arena 2 Start' Orb 38.9/78/
            //    34.6/50.9, 'Hazard Plat/Radiant Plat Wide (4)' (60.3,32.5) logo abaixo do 'Phase2 Detector' (61.2,34.9)) - valores
            //    conferidos no dump DESTA cena, nao copiados.
            //  - Diferencas da Absolute: limiares do 'Phase Control' 2650/2250/2000/1300; subida final 'Ascend Tele' -> (71.9,142.7)
            //    -> Plat Setup -> 'Ascend Cast' (raio + Trigger2dEvent 'Nail Attack' -> FINAL HIT, SEM checagem de Hero Y). Chao da
            //    sub-arena = topo da plataforma mais alta 'Ascend Set/Radiant Plat Wide (5)' (63.14,136): 136 + Colliders 0.64 +
            //    BoxCollider2D (Radiant Plat Wide) offset -0.372 + size/2 0.492 = 136.76 -> ela fica 5.94 acima do chao (na arena 1
            //    fica 5.8..8), ao alcance do ferrao.
            //  - Fase de plataformas: igual a Absolute ('Abyss Up' espera PHASE2 RISE do 'Phase2 Detector' da sala, ausente) ->
            //    PhaseEvents. 'Climb Plats1' SetHazardRespawn(GetPosition de $P2A Hazard = null -> (0,0,0)) desligado.
            //  - Pentes: mesmo prefab de pool ('Attack Obj' = sharedassets407 #92, o arquivo desta cena) e mesmos estados
            //    spawnadores -> mesmo PooledAttack (tipos 6..8 = arena 2, chao 33.26).
            //  - MORTE (alcancavel agora): 'Final Impact' (HUD OUT, broadcast ALL CHARMS END, SetPlayerDataBool killedFinalBoss/
            //    newDataFinalBoss, SetPlayerDataInt killsFinalBoss=0, musica 'None') -> 'Knight Pull' (FSM CANCEL + RelinquishControl
            //    + StopAnimationControl + gravidade off + SetMeshRenderer(Hero)=false) -> ... cutscene do Knight/Hollow Shade que
            //    exige golpes do heroi ... -> 'Kill Hit' -> Tendrils 3 -> Yank Down -> Drag -> 'Final Black' (snapshot 'at None'
            //    #3863 = audio mudo) -> 'Ending Scene' = GameManager.ChangeToScene (o final do jogo). 1a correcao (antiga):
            //    'Final Impact' FINISHED -> 'Kill Hit' (pulava a cutscene do Knight).
            //  - AGORA (pedido do usuario: final igual ao original, mas sem "zerar"): a cutscene ORIGINAL roda inteira -
            //    Knight Pull (heroi congelado/invisivel, Knight falso no lugar dele puxado pra perto dela) -> Ball Tween (1.5s) ->
            //    Tendrils 2 (2s) (Abyss Pit/Tendrils sao irmaos dela na sala = null aqui, so esperam) -> Shade Rise / Shade Antic /
            //    Shade Pull (Hollow Shade, filho dela, sobe e agarra) -> Break (3s, olho quebra) -> Split Antic -> Knight Split
            //    (Knight Ball sai do casco) -> Form -> First Idle / Knight Idle: ListenForAttack = o JOGADOR golpeia (15x; Set Anim
            //    -> Antic -> Slash -> Kill? -> Slash Recover) - se nao apertar, CutsceneInputs manda SLASH sozinho -> Kill Hit ->
            //    Tendrils 3 -> Yank Down -> Drag -> Final Black (Death Black = tela preta 5s) -> 'Ending Scene' (ChangeToScene
            //    DESLIGADO = estado final parado) -> SpawnedBossFinish: devolve o heroi e remove a copia (a tela preta e filha dela).
            //  - Nada disso grava save (os unicos SetPlayerData* sao do 'Final Impact', desligados) nem troca de cena. Estados novos
            //    auditados: so visual/audio/tremor, broadcasts SHADE HIT / KILL HIT / REMINDER ATTACK (ninguem fora do
            //    Dream_Final_Boss escuta; KILL HIT ja rodava antes), GameManager.FreezeMoment(1) (pausa de 0.1s que se desfaz
            //    sozinha). 'Kill Hit' ALL CHARMS END ja e OFF no asset. Desligados: o que mudaria estado GLOBAL que so a troca de
            //    cena do original desfaria (HUD fora, efeitos de amuleto, musica, snapshot mudo) + o ChangeToScene.
            //  - 'Knight Pull' iTweenMoveTo (64.49,145.32) ABSOLUTO -> WorldTweens (delta da sub-arena final: 8.56 acima do chao,
            //    7.41 a esquerda dela, como no original).
            ["Radiance"] = new ArenaProfile
            {
                Ref = new Vector2(60.6f, 21.2f),
                XVars = new[] { "A1 X Min", "A1 X Max", "Orb Min X", "Orb Max X" },
                YVars = new[] { "Orb Min Y", "Orb Max Y", "Target Y" },
                V3Vars = new[] { "Destination" },
                DisableActions = new[]
                {
                    ("Control", "Climb Plats1", "CallMethodProper"),               // SetHazardRespawn((0,0,0))
                    ("Control", "Final Impact", "SetPlayerDataBool"),
                    ("Control", "Final Impact", "SetPlayerDataInt"),
                    ("Control", "Final Impact", "SendEventByName:OUT"),            // HUD Canvas 'Slide Out'
                    ("Control", "Final Impact", "SendEventByName:ALL CHARMS END"),
                    ("Control", "Final Impact", "ApplyMusicCue"),                  // MusicCue 'None' (resources.assets #19414)
                    ("Control", "Final Black", "TransitionToAudioSnapshot"),       // snapshot 'at None' (#3863)
                    ("Control", "Ending Scene", "CallMethodProper"),               // GameManager.ChangeToScene
                },
                FinishStates = new[] { ("Control", "Ending Scene", 0.5f) },
                HeroCutsceneStates = new[]
                {
                    ("Control", "Final Impact"), ("Control", "Knight Pull"), ("Control", "Ball Tween"), ("Control", "Tendrils 2"),
                    ("Control", "Shade Rise"), ("Control", "Shade Antic"), ("Control", "Shade Pull"), ("Control", "Pulling"),
                    ("Control", "Break"), ("Control", "Split Antic"), ("Control", "Knight Split"), ("Control", "Ball Up"),
                    ("Control", "Form"), ("Control", "Set Anim"), ("Control", "Antic"), ("Control", "Slash"),
                    ("Control", "Kill?"), ("Control", "Slash Recover"), ("Control", "Kill Hit"), ("Control", "Tendrils 3"),
                    ("Control", "Yank Down"), ("Control", "Drag"), ("Control", "Final Black"),
                },
                // ListenForAttack (o original espera o jogador golpear; 'REMINDER ATTACK' sai aos 3s do First Idle).
                CutsceneInputs = new[] { ("Control", "First Idle", "SLASH", 3f), ("Control", "Knight Idle", "SLASH", 1.2f) },
                CutsceneTimeout = 120f, // natural ~35 s + golpes (pior caso, so com SLASH automatico, ~65 s)
                WorldTweens = new[] { ("Control", "Knight Pull", float.NaN) },
                PhaseEvents = new[] { ("Control", "Abyss Up", "PHASE2 RISE", 2.5f) },
                SubArenas = new[]
                {
                    ("Control", 33.26f, new[] { "Arena 2 Start", "Tele 1", "Tele 2", "Tele 3", "Tele 4", "Tele 5",
                                                "Tele 6", "Tele 7", "Tele 8", "Tele 9", "Tele 10" }),
                    ("Control", 136.76f, new[] { "Ascend Tele", "Knight Pull" }),
                },
                PooledAttacks = new[]
                {
                    new PooledAttack
                    {
                        Spawners = new[]
                        {
                            ("Attack Commands", "Comb Top"), ("Attack Commands", "Comb L"), ("Attack Commands", "Comb R"),
                            ("Attack Commands", "Comb Top 2"), ("Attack Commands", "Comb L 2"), ("Attack Commands", "Comb R 2"),
                            ("Control", "Rage Comb"), ("Control", "TL"), ("Control", "TR"),
                        },
                        StoreVar = "Attack Obj",
                        Fsm = "Control",
                        TypeVar = "Type",
                        ReadyState = "Antic",
                        TypeFloorY = new Dictionary<int, float> { [6] = 33.26f, [7] = 33.26f, [8] = 33.26f },
                    },
                },
            },
        };
    }
}
