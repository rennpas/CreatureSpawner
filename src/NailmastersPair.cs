using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CreatureSpawner
{
    // "Nailmasters Oro & Mato" do painel = a luta EM DUPLA de verdade: o objeto 'Brothers' INTEIRO do GG_Nailmasters (pre-carregado
    // como grupo, mesmo esquema do 'Mantis Battle/Battle Sub' - ver EncounterGroups.NailBrosPrefab), nao dois clones soltos.
    // Fonte: scratchpad\FsmDumpTool\out\nailmasters_brothers.txt (FSM 'nailmaster' identica nos dois - template sharedassets454 #24 -,
    // so muda Oro=True/False e as referencias $Brother; FSM 'Combo Control' no pai 'Brothers').
    //
    // Sequencia original (confirmada no dump):
    //  1. Oro: Init (Oro=true) -> P2 HP Adjust -> Rest -> Wake -> Roar -> Roar End -> First Idle -> luta SOZINHO ('Single?' com
    //     Brothered=false -> 'Oro Choice'). Mato: Init (Oro=false) -> 'Entry Wait' (Brothered=true, Phase 2=true), escondido fora da
    //     tela (19.26, 26.53), kinematic/invencivel/sem dano, unica saida ENTER. 'Combo Control': Init -> 'Oro Battle' (espera NEXT).
    //  2. HP do Oro zera (HealthManager hasSpecialDeath -> ZERO HP): Death Start -> Unbrother -> End Music? -> Explode -> Death Launch
    //     -> Death Air -> Death Land (2 s) -> 'Death Type' (Phase 2=false) CALL MATO -> 'Call Mato': Brothered=true, Phase 2=true,
    //     SendEventByName($Brother, ENTER), espera LOOK.
    //  3. Mato: Entry Dir (SendEventByScale no $Brother) -> Pos L/R (X = X do Oro -/+ 6; fora de 32.81..60.35 -> FlipScale no Oro e
    //     tenta o outro lado) -> 'Entry Fall' (SetPosition y=22.86, cai a -50 ate y<=6.86) -> Entry Land (2 s) -> 'Look Antic' (LOOK
    //     pro Oro) -> Look -> 'Entry To Roar' (END pro Oro) -> Entry Roar -> 'Roar End 2' (END pro Oro, NEXT pro $Controller) ->
    //     Activate (vulneravel, Brothered=true) -> Idle -> 'Single?' DOUBLE -> 'Wait For Bro'.
    //     Oro, em paralelo: LOOK -> Look Antic 2 -> Look 2 (espera END) -> Getup -> Recovery Roar (espera END) -> Recover Roar End ->
    //     'Reactivate' (SetHP P2 HP = 600, volta a lutar) -> Idle -> 'Wait For Bro'. = FASE 2 (os dois juntos): o Oro (BoolAllTrue
    //     Bro Ready + Oro) escolhe os golpes em dupla ('Check Hero Pos' -> 2 Slash/2 Stomp/Stomp Slash/Do Combo/Cross...: manda DO
    //     SLASH/DO STOMP/JUMP AWAY/EVADE AWAY - globais - pro $Brother); o 'Combo Control' (NEXT -> Reset -> Idle, conta ATTACK) liga
    //     'Do Dash Slash'/'Do Cyclone' nos dois -> combos coordenados (Oro dash-slash + Mato ciclone, D SLASH RELEASE/CYCLONE RELEASE).
    //  4. Um cai na fase 2 (Phase 2=true): Death Start -> 'Unbrother' (Brothered=false no outro -> ele sai do 'Wait For Bro' por CANCEL
    //     e luta SOZINHO) -> ... -> 'Death Type' DEFEATED -> 'Defeated' (manda DEFEATED pro $Controller, fica caido esperando BOW).
    //  5. O outro cai: 'Combo Control' 2o DEFEATED -> Journal -> Defeated 2 (0.75 s) -> 'Bow' (BOW pros dois) -> cada um: Drum Roll ->
    //     Bow (reverencia 2 s) -> End (EndGGBossScene = nada fora do Godhome). NAO existe estado "final cooperativo" depois disso: o
    //     final da luta e a reverencia dos dois.
    //
    // Por que o grupo inteiro: as referencias cruzadas ($Brother de cada um, $Controller, as $Mato/$Oro do 'Combo Control') sao todas
    // INTERNAS ao 'Brothers'. O Instantiate do grupo remapeia elas pros clones novos e o SceneLinkSanitizer nao mexe (sao "Own"); com
    // dois prefabs separados o sanitizer zera o $Brother (aponta pro bundle) e o 'Combo Control' nem existe (o pai no bundle e um toco
    // so com Transform/TinkEffect - BundleProbe\out\mato_analyze.txt). Mesmo assim o AfterActivate abaixo CONFERE e religa as
    // referencias (loga se precisou mudar algo).
    //
    // Ajustes desta copia (o resto - coordenadas, coleira por irmao etc. - vem do perfil em ArenaProfilesVariants):
    //  - Filhos da SALA dentro do 'Brothers' sao removidos antes de ativar: 'CamLock Intro' (CameraLockArea ATIVA - travaria a camera
    //    nos limites absolutos da arena GG), 'CamLockRoar' (idem, ligada no 'Entry Fall' com X min/max = Roar X) e 'Godseeker Crowd'
    //    (plateia de fundo do Godhome). As vars $CamLock Intro/$CamLockRoar/$Godseeker ficam null (acoes viram no-op - CallMethodProper
    //    e ActivateGameObject checam null, decompilado).
    //  - A SpawnLeash que o painel poe na RAIZ arrastaria o grupo todo (a raiz fica em delta, longe da ancora) -> removida; cada irmao
    //    ganha a sua.
    //  - Mato escondido (renderer + collider desligados) enquanto espera no 'Entry Wait': a posicao original dele fica 27 a esquerda e
    //    22.5 acima do centro da arena e a coleira dele puxaria ele pra borda dela, possivelmente dentro da tela. Reaparece assim que
    //    sai do 'Entry Wait' (ENTER -> 'Entry Fall' ja teleportou ele pra cima do Oro).
    //  - 'Entry Fall': o lado (Oro +-6) so respeita as paredes da arena ORIGINAL (32.81..60.35, deslocadas). Raycast Terrain do Oro ate
    //    o X escolhido; parede no caminho -> Mato desce encostado nela (ele cai kinematic, atravessaria a parede da sala atual).
    //  - Os dois 'Cyclone Tink' (FSM 'Follow' faz SetParent(null) e segue o pai) sairiam da hierarquia e sobrariam quando a copia some
    //    -> DestroyWithOwner(raiz).
    //  - Mato faz o ciclone tambem na dupla ('Cyclone Ready' com Brothered=true pula pro centro e faz o ciclone) -> o mesmo vigia de teto
    //    baixo do "Mato (Solo)" (NailmasterMatoFix.AttachCycloneWatch).
    internal sealed class NailmastersPair : MonoBehaviour
    {
        public const string Name = "Nailmasters Oro & Mato";
        private static readonly string[] RoomChildren = { "CamLock Intro", "CamLockRoar", "Godseeker Crowd" };
        private static readonly string[] RoomVars = { "CamLock Intro", "CamLockRoar", "Godseeker" };
        private const float WallMargin = 1.5f;

        private GameObject oro, mato;
        private PlayMakerFSM matoFsm;
        private Renderer matoRenderer;
        private Collider2D matoCollider;
        private bool matoColliderWasEnabled, revealed;

        // DIAGNOSTICO (so a dupla): relato "nao apareceu nada" sem nenhuma linha de log depois do AfterActivate. Loga as trocas de
        // estado das 3 FSMs (Oro/Mato 'nailmaster' + 'Combo Control') e um retrato do Oro em 0.5/2/5 s (ativo, renderer, escala,
        // posicao, clipe) e se o grupo for desligado/destruido. Nao muda comportamento nenhum.
        private const int MaxDiagLogs = 40;
        private static readonly float[] Snapshots = { 0.5f, 2f, 5f };
        private PlayMakerFSM oroFsm, comboFsm;
        private string lastOro, lastMato, lastCombo;
        private float spawnedAt = -1f;
        private int diagLogs, nextSnapshot;

        public static void BeforeActivate(GameObject root, SpawnContext ctx)
        {
            if (root == null || ctx == null || ctx.Name != Name) return;
            try
            {
                var rootLeash = root.GetComponent<SpawnLeash>();
                if (rootLeash != null) DestroyImmediate(rootLeash);
                foreach (var n in RoomChildren)
                {
                    var t = root.transform.Find(n);
                    if (t != null) DestroyImmediate(t.gameObject);
                }
                var oroT = root.transform.Find("Oro");
                var matoT = root.transform.Find("Mato");
                if (oroT == null || matoT == null)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] Nailmasters: 'Brothers' sem Oro/Mato (Oro={oroT != null}, Mato={matoT != null})");
                    return;
                }
                foreach (var b in new[] { oroT.gameObject, matoT.gameObject })
                {
                    // Os dois vem com activeSelf=false dentro do 'Brothers' pre-carregado (ModLog: ativo=False, FSM sem estado).
                    if (!b.activeSelf) b.SetActive(true);
                    if (b.GetComponent<SpawnLeash>() == null) SpawnFixups.ConfigureLeash(b.AddComponent<SpawnLeash>(), ctx);
                    var tink = b.transform.Find("Cyclone Tink");
                    if (tink != null && tink.GetComponent<DestroyWithOwner>() == null) tink.gameObject.AddComponent<DestroyWithOwner>().Owner = root;
                }
                var pair = root.AddComponent<NailmastersPair>();
                pair.oro = oroT.gameObject;
                pair.mato = matoT.gameObject;
                pair.matoRenderer = matoT.GetComponent<MeshRenderer>();
                pair.matoCollider = matoT.GetComponent<BoxCollider2D>();
                if (pair.matoRenderer != null) pair.matoRenderer.enabled = false;
                if (pair.matoCollider != null)
                {
                    pair.matoColliderWasEnabled = pair.matoCollider.enabled;
                    pair.matoCollider.enabled = false;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NailmastersPair.BeforeActivate: {ex}");
            }
        }

        public static void AfterActivate(GameObject root, SpawnContext ctx)
        {
            if (root == null || ctx == null || ctx.Name != Name) return;
            try
            {
                var pair = root.GetComponent<NailmastersPair>();
                if (pair == null || pair.oro == null || pair.mato == null) return;
                var oroF = GruzZoteFixes.FindFsm(pair.oro, "nailmaster", "Call Mato", "Wait For Bro");
                var matoF = GruzZoteFixes.FindFsm(pair.mato, "nailmaster", "Entry Wait", "Wait For Bro");
                var combo = GruzZoteFixes.FindFsm(root, "Combo Control", "Oro Battle", "Bow");
                if (oroF == null || matoF == null)
                {
                    Modding.Logger.LogError($"[CreatureSpawner] Nailmasters: FSM 'nailmaster' nao encontrada (Oro={oroF != null}, Mato={matoF != null})");
                    return;
                }
                bool? oroFlag = oroF.FsmVariables.FindFsmBool("Oro")?.Value, matoFlag = matoF.FsmVariables.FindFsmBool("Oro")?.Value;
                if (oroFlag != true || matoFlag != false)
                    Modding.Logger.LogWarn($"[CreatureSpawner] Nailmasters: var 'Oro' inesperada (Oro={oroFlag}, Mato={matoFlag})");

                var changed = new List<string>();
                Link(oroF, "Brother", pair.mato, changed);
                Link(matoF, "Brother", pair.oro, changed);
                Link(oroF, "Controller", root, changed);
                Link(matoF, "Controller", root, changed);
                if (combo != null)
                {
                    Link(combo, "Oro", pair.oro, changed);
                    Link(combo, "Mato", pair.mato, changed);
                }
                else Modding.Logger.LogWarn("[CreatureSpawner] Nailmasters: FSM 'Combo Control' nao encontrada na raiz - sem combos em dupla nem BOW final");
                foreach (var f in new[] { oroF, matoF })
                    foreach (var n in RoomVars)
                    {
                        var v = f.FsmVariables.FindFsmGameObject(n);
                        if (v != null) v.Value = null;
                    }

                NailmasterMatoFix.AttachCycloneWatch(pair.mato, matoF);
                pair.matoFsm = matoF;
                pair.oroFsm = oroF;
                pair.comboFsm = combo;
                pair.spawnedAt = Time.time;
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters: dupla em Oro={pair.oro.transform.position} Mato={pair.mato.transform.position} (escondido ate o ENTER); " +
                                   (changed.Count == 0 ? "referencias cruzadas ja corretas" : "religado: " + string.Join(", ", changed.ToArray())));
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters diag: raiz '{root.name}' ativa={root.activeInHierarchy} escala={root.transform.lossyScale}; " +
                                   $"{pair.Describe(pair.oro, oroF)}; {pair.Describe(pair.mato, matoF)}; " +
                                   $"Combo={(combo == null ? "null" : $"'{combo.ActiveStateName}' on={combo.enabled}")}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NailmastersPair.AfterActivate: {ex}");
            }
        }

        private static void Link(PlayMakerFSM f, string varName, GameObject value, List<string> changed)
        {
            var v = f.FsmVariables.FindFsmGameObject(varName);
            if (v == null)
            {
                Modding.Logger.LogWarn($"[CreatureSpawner] Nailmasters: FSM '{f.FsmName}' de '{f.name}' sem a var '{varName}'");
                return;
            }
            if (v.Value == value) return;
            changed.Add($"{f.name}.{varName} {(v.Value == null ? "null" : v.Value.name)} -> {value.name}");
            v.Value = value;
        }

        private string Describe(GameObject b, PlayMakerFSM f)
        {
            if (b == null) return "irmao null (destruido?)";
            var r = b.GetComponent<MeshRenderer>();
            var anim = b.GetComponent<tk2dSpriteAnimator>();
            var col = b.GetComponent<Collider2D>();
            return $"{b.name} ativo={b.activeInHierarchy} renderer={(r == null ? "null" : r.enabled.ToString())} " +
                   $"collider={(col == null ? "null" : col.enabled.ToString())} pos={b.transform.position} escala={b.transform.lossyScale} " +
                   $"camada={b.layer} clipe='{(anim != null && anim.CurrentClip != null ? anim.CurrentClip.name : "-")}' " +
                   $"fsm='{(f == null ? "null" : f.ActiveStateName)}' on={(f != null && f.enabled)}";
        }

        private void Diag()
        {
            if (spawnedAt < 0f || diagLogs >= MaxDiagLogs) return;
            float t = Time.time - spawnedAt;
            void Track(PlayMakerFSM f, string who, ref string last)
            {
                if (f == null || diagLogs >= MaxDiagLogs) return;
                string st = f.ActiveStateName;
                if (st == last) return;
                diagLogs++;
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters diag t={t:0.00}s: {who} '{last ?? "-"}' -> '{st}'");
                last = st;
            }
            Track(oroFsm, "Oro", ref lastOro);
            Track(matoFsm, "Mato", ref lastMato);
            Track(comboFsm, "Combo Control", ref lastCombo);
            if (nextSnapshot < Snapshots.Length && t >= Snapshots[nextSnapshot] && diagLogs < MaxDiagLogs)
            {
                nextSnapshot++;
                diagLogs++;
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters diag t={t:0.00}s: {Describe(oro, oroFsm)}; {Describe(mato, matoFsm)}");
            }
        }

        // (OnDisable tambem roda quando o proprio Update faz enabled=false - so loga se o GameObject saiu da hierarquia ativa.)
        private void OnDisable()
        {
            if (spawnedAt >= 0f && !gameObject.activeInHierarchy)
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters diag: grupo '{name}' DESLIGADO apos {Time.time - spawnedAt:0.00}s; {Describe(oro, oroFsm)}");
        }

        private void OnDestroy()
        {
            if (spawnedAt >= 0f)
                Modding.Logger.Log($"[CreatureSpawner] Nailmasters diag: grupo '{name}' DESTRUIDO apos {Time.time - spawnedAt:0.00}s");
        }

        private void Update()
        {
            Diag();
            if (revealed) { if (diagLogs >= MaxDiagLogs) enabled = false; return; }
            if (mato == null || matoFsm == null) return;
            string st = matoFsm.ActiveStateName;
            if (string.IsNullOrEmpty(st) || st == "Init" || st == "Entry Wait") return;
            revealed = true;
            if (matoRenderer != null) matoRenderer.enabled = true;
            if (matoCollider != null) matoCollider.enabled = matoColliderWasEnabled;
            string wall = st == "Entry Fall" ? KeepOffWalls() : "";
            Modding.Logger.Log($"[CreatureSpawner] Nailmasters: Mato entrando ('{st}') em {mato.transform.position}{wall}");
        }

        // 'Entry Fall' ja fez SetPosition(X do Oro +-6, alto) e ele cai kinematic: sem isto atravessaria uma parede da sala atual
        // que esteja entre ele e o Oro e aterrissaria dentro/atras dela.
        private string KeepOffWalls()
        {
            if (oro == null) return "";
            Vector3 o = oro.transform.position, m = mato.transform.position;
            float dx = m.x - o.x;
            if (Mathf.Abs(dx) < 0.01f) return "";
            float dir = Mathf.Sign(dx);
            var hit = Physics2D.Raycast(new Vector2(o.x, o.y), new Vector2(dir, 0f), Mathf.Abs(dx) + WallMargin, SpawnFixups.TerrainMask);
            if (hit.collider == null) return "";
            float nx = hit.point.x - dir * WallMargin;
            if ((nx - o.x) * dir < 0f) nx = o.x; // parede colada no Oro (raro): cai no X dele
            mato.transform.position = new Vector3(nx, m.y, m.z);
            return $" - parede em x={hit.point.x:0.##} entre ele e o Oro: X {m.x:0.##} -> {nx:0.##}";
        }
    }
}
