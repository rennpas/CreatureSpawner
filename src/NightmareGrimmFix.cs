using System;
using UnityEngine;

namespace CreatureSpawner
{
    // Nightmare King Grimm ('Grimm_Nightmare/Grimm Control/Nightmare Grimm Boss' = "Nightmare King" do painel): "fica parado/travado
    // num ataque no comeco e so volta ao normal depois da transicao de fase" (= o STUN/explosao em morcegos). Evidencia
    // (scratchpad\FsmDumpTool\out\nkg_full.txt, nkg_tmpl126.txt, nkg_gc_control.txt, nkg_stun_tmpl144.txt; BundleProbe 'mb'/'clips'
    // sobre repacked_full_v10.bundle):
    //  - A cutscene de intro (3-6 s) NAO e dele: e da FSM 'Control' do pai 'Grimm Control' (nao vem no preload): Pan Over (3 s,
    //    CameraLock Heart, HUD OUT) -> Eye 1/Eye 2 (1 s cada) -> Silhouette (4.25 s, 'Grimm Appear') -> Fader Wipe -> Title Up (2.5 s,
    //    musica) -> Title Down -> Return Pause -> Fade Back (RegainControl do heroi) -> 'Fight Start', cujo UNICO efeito nele e
    //    SendEventByName($Nightmare Grimm, WAKE). Toda a intro mexe no heroi/camera/HUD/musica - nada dela e necessario pra ele lutar.
    //  - A FSM dele ('Control', template grimm_nightmare_control sharedassets393 #126): Init -> Set Balloon HP -> 'Dormant' (SEM acoes;
    //    unica saida WAKE -> 'Tele Out'). 'Tele Out' (unica saida FINISHED = animationCompleteEvent do Tk2dPlayAnimationWithEvents
    //    'Tele Out', clip Once 6 frames @30fps = 0.2 s) -> 'Out Pause' (Wait 0.6) -> 'Balloon?' -> 'Move Choice' -> ataques.
    //    Na sala original ele espera o Dormant em (74.3, 44.9), fora da tela.
    //  - O animator da raiz tem playAutomatically=1, defaultClipId=2 = 'Slash Antic' (wrap LoopSection = nunca termina): enquanto
    //    nenhum estado tocar outro clipe ele fica "num ataque" (pose de Slash Antic em loop) - exatamente o relato/screenshot. Ou seja
    //    o ultimo comando de animacao que ele recebeu foi o autoplay do Start: a FSM ficou presa ANTES de qualquer estado que toca clipe
    //    (Init/Set Balloon HP/Dormant), ou no 'Tele Out' com o clipe dele sobrescrito (sem o FINISHED do fim do clipe nao ha saida).
    //  - Por que a "fase 2" destrava: a FSM 'Stun' (template stun_control) comeca em Idle (nao precisa de STUN CONTROL START), conta
    //    os golpes e manda STUN -> transicao GLOBAL STUN -> Stun Reset -> ... -> Adjust HP -> 'Balloon?' -> luta normal. E a UNICA
    //    saida que nao depende do WAKE/Tele Out - por isso so ela destrava.
    //  - O perfil ja manda WAKE no Dormant (FsmEventWhenInState, 6 s, no 1o frame em que a FSM estiver no Dormant). Nao ha erro no
    //    Player.log do teste de 09/11 (so o "Unsupported type: null" do CallStaticMethod FakeBat.NotifyAllBossAwake do Init, que tambem
    //    sai no jogo original), entao o ponto exato onde o WAKE se perde nao da pra provar offline.
    // Correcao (vigia de abertura, no estilo GodTamerRollFix/GruzMotherStallWatch - a FSM nao e alterada): ate ele chegar em 'Balloon?'
    // pela 1a vez (abertura concluida) ou levar o 1o STUN ('Stun Reset'), olha o estado da 'Control':
    //  - Init / Set Balloon HP parados > Grace: SetState('Set Balloon HP') (recalcula os limiares Rage HP; FINISHED -> Dormant).
    //  - Dormant > Grace: manda WAKE de novo (como o 'Fight Start'); se mesmo assim continuar no Dormant, SetState('Tele Out').
    //  - Tele Out > TeleOutMax (clipe de 0.2 s): manda FINISHED (o que o fim do clipe mandaria); se nao sair, SetState('Out Pause').
    // Cada acao loga o estado/clipe atual no ModLog ("[CreatureSpawner] NKG abertura: ...") - se o vigia disparar, o log mostra
    // qual dos casos era. Nada disso roda depois da abertura: os ataques, o crash do rage (SceneLinkSanitizer) e o loop de ataque
    // (ArenaProfiles "Nightmare King") ficam exatamente como estavam.
    internal sealed class NightmareGrimmOpeningWatch : MonoBehaviour
    {
        public const string Name = "Nightmare King";
        private const float Grace = 0.5f;       // o WAKE do perfil chega no 1o frame do Dormant
        private const float TeleOutMax = 1.0f;  // 'Tele Out' = 6 frames @30fps (0.2 s)
        private const float MaxWatch = 30f;     // a abertura normal leva < 1 s (Tele Out 0.2 + Out Pause 0.6)
        private const int MaxLogs = 12;

        private PlayMakerFSM fsm;
        private tk2dSpriteAnimator anim;
        private string lastState;
        private float enteredAt, startedAt;
        private int tries, logs;

        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            if (clone == null || ctx == null || ctx.Name != Name) return;
            try
            {
                var f = GruzZoteFixes.FindFsm(clone, "Control", "Init", "Set Balloon HP", "Dormant", "Tele Out", "Out Pause", "Balloon?");
                if (f == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] NKG: FSM 'Control' com Dormant/Tele Out/Out Pause/Balloon? nao encontrada - sem vigia de abertura");
                    return;
                }
                var w = clone.AddComponent<NightmareGrimmOpeningWatch>();
                w.fsm = f;
                w.anim = clone.GetComponent<tk2dSpriteAnimator>();
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NightmareGrimmOpeningWatch: {ex}");
            }
        }

        private void Start() => startedAt = Time.time;

        private void Update()
        {
            try
            {
                if (fsm == null || Time.time - startedAt > MaxWatch) { Destroy(this); return; }
                string st = fsm.ActiveStateName;
                // Abertura concluida (chegou nos ataques) ou STUN global (a fase que ja funcionava): vigia sai.
                if (st == "Balloon?" || st == "Move Choice" || st == "Stun Reset" || st == "Stun")
                {
                    if (tries > 0) Log($"abertura concluida em '{st}' depois de {tries} intervencao(oes)");
                    Destroy(this);
                    return;
                }
                if (st != lastState)
                {
                    lastState = st;
                    enteredAt = Time.time;
                    tries = 0;
                    return;
                }
                float inState = Time.time - enteredAt;
                switch (st)
                {
                    case "Init":
                    case "Set Balloon HP":
                        if (inState > Grace) Force("Set Balloon HP", $"parado em '{st}' ha {inState:0.##}s");
                        break;
                    case "Dormant":
                        if (inState < Grace * (tries + 1)) break;
                        if (tries < 2) Send("WAKE", $"no Dormant ha {inState:0.##}s");
                        else Force("Tele Out", $"WAKE nao tirou do Dormant ({tries}x)");
                        break;
                    case "Tele Out":
                        if (inState < TeleOutMax + Grace * tries) break;
                        if (tries < 1) Send("FINISHED", $"no 'Tele Out' ha {inState:0.##}s (clipe de 0.2 s nao terminou)");
                        else Force("Out Pause", "FINISHED nao tirou do 'Tele Out'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NightmareGrimmOpeningWatch: {ex}");
                Destroy(this);
            }
        }

        private string Diag()
        {
            string clip = anim != null && anim.CurrentClip != null ? anim.CurrentClip.name : "?";
            bool playing = anim != null && anim.Playing;
            return $"estado='{fsm.ActiveStateName}' fsm.enabled={fsm.enabled} started={fsm.Fsm?.Started} clipe='{clip}' tocando={playing} pos={transform.position}";
        }

        private void Send(string evt, string why)
        {
            Log($"{why} -> {evt} [{Diag()}]");
            tries++;
            fsm.SendEvent(evt);
        }

        private void Force(string state, string why)
        {
            Log($"{why} -> SetState('{state}') [{Diag()}]");
            tries++;
            lastState = null; // reinicia a medicao no estado novo
            fsm.SetState(state);
        }

        private void Log(string msg)
        {
            if (logs++ < MaxLogs) Modding.Logger.Log($"[CreatureSpawner] NKG abertura '{name}': {msg}");
        }
    }
}
