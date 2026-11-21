using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureSpawner
{
    // "Real Bat" ('Grimm_Main_Tent_boss/Grimm Bats/Real Bat'): o morcego "de verdade" do ataque de morcegos do Grimm (Troupe Master;
    // o do NKG e igual). Relato: invisivel (so um brilho/fumaca vermelha) e imortal. Evidencia (bundle de preload repacked_full_v10,
    // BundleProbe 'fsm'/'mb'/'collide'/'rb' -> out\realbat_fsm_v10.txt; FsmDumpTool\out\nkg.txt p/ a copia do Grimm_Nightmare):
    //  - FSM 'Control' (sem template): Init (GetPosition z, GetOwner) espera BOSS AWAKE -> 'Dormant' (sem acoes) espera BATS OUT ->
    //    'Position' (GetPosition $Grimm -> SetPosition $Grimm Pos; SetMeshRenderer TRUE; anim 'Bat Fly'; SetCollider false; SetHP 0)
    //    -> Face Middle (X Pos vs 87.2) -> 'Get To Middle' (iTweenMoveTo MUNDO (87.2,12.6)) -> 'Choose' (SetCollider true) -> Go*/Fly
    //    (voa dentro de X 84..89 / Y 10..13 ABSOLUTOS). GLOBAL BATS IN -> Return (tween ate $Grimm Pos) -> End (anim 'Bat End') ->
    //    Inactive (renderer off, y=-50) -> Dormant. Quem manda tudo e o proprio Grimm ('Grimm Boss' FSM 'Control', broadcast):
    //    'Init' BOSS AWAKE, 'Explode' BATS OUT, 'Call Bats' BATS IN. Ele nao vem junto no preload -> a copia fica no Init pra sempre.
    //  - INVISIVEL: MeshRenderer serializado DESLIGADO (enabled=0); o UNICO SetMeshRenderer true e o do 'Position' (depois do BATS OUT).
    //    O que aparece e o filho 'white_light' (SpriteRenderer ativo/ligado no prefab, nenhuma FSM mexe nele) + os efeitos de golpe
    //    (EnemyHitEffectsUninfected) quando se acerta o corpo invisivel (BoxCollider2D serializado LIGADO).
    //  - IMORTAL (causa SEPARADA, nao e o mesmo gatilho): HealthManager hp=0, invincible=0, hasSpecialDeath=1, sem EnemyDeathEffects
    //    nem DamageHero. TakeDamage (decompilado): hp = Max(hp - dano, -50); hp <= 0 -> Die -> manda ZERO HP (a FSM nao trata) e, com
    //    hasSpecialDeath, so NonFatalHit e RETORNA - nunca isDead. E de proposito: ele e uma "esponja" de dano do Grimm ('Adjust HP':
    //    GetHP $Real Bat Obj -> IntClamp(Bat Damage Cap -50..0) -> soma no HP do Grimm), e o 'Position' zera o HP a cada saida
    //    (SetHP 0). Mesmo na luta original ele nao morre; acordar a FSM NAO resolve isso.
    //  - SceneLinkSanitizer: var 'Grimm' e o ownerDefault do 'Position' apontam pro 'Grimm Boss' (intacto - 'collide' TOTAL 0 - mas
    //    fora da hierarquia, entao zerados no preload). Com $Grimm null o GetPosition vira no-op e o SetPosition($Grimm Pos = 0,0,0)
    //    jogaria o morcego pra (0,0) do mundo.
    // Correcao (so na copia):
    //  - Perfil: WakeEvents Init/BOSS AWAKE e Dormant/BATS OUT (o que o Grimm mandaria); Ref = (87.2 = 'Face Middle'/'Get To Middle',
    //    5.0 = chao da arena: EdgeCollider2D y=5 de x=32..128 em Grimm_Main_Tent, BundleProbe 'edges level391') -> a caixa de voo
    //    84..89 / 10..13 e as comparacoes do Face Middle sao deslocadas sozinhas pelo FsmArenaShift (GetPosition de mundo em X/Y Pos),
    //    o destino do 'Get To Middle' via WorldTweens. SetHP 0 do 'Position' desligado.
    //  - Var 'Grimm' = a propria copia: 'Position' fica onde nasceu (heroi+(2,2)) e o Return (BATS IN de uma copia do Grimm) volta pra
    //    la, em vez de (0,0).
    //  - HP proprio (StandaloneHp = o teto de dano que o Grimm aceita dele e o piso -50 do TakeDamage) e hasSpecialDeath=false: morre
    //    de verdade (Die marca isDead). Sem EnemyDeathEffects nada some sozinho -> RealBatDeath toca o 'End' (anim 'Bat End', o mesmo
    //    "sumir" do original) e remove a copia.
    internal static class RealBatFix
    {
        public const string Name = "Real Bat";
        public const int StandaloneHp = 50;

        public static void AddProfiles(Dictionary<string, ArenaProfile> d)
        {
            d[Name] = new ArenaProfile
            {
                Ref = new Vector2(87.2f, 5.0f),
                WakeEvents = new[]
                {
                    ("Control", "Init", "BOSS AWAKE"), // 'Grimm Boss' 'Init' (broadcast)
                    ("Control", "Dormant", "BATS OUT"), // 'Grimm Boss' 'Explode' (broadcast)
                },
                DisableActions = new[] { ("Control", "Position", "SetHP") }, // SetHP 0 (esponja de dano do Grimm)
                WorldTweens = new[] { ("Control", "Get To Middle", float.NaN) }, // iTweenMoveTo (87.2,12.6) MUNDO
            };
            // Copia ligada a um Grimm spawnado (ver GrimmBat.cs): mesma arena/voo, mas SEM o BATS OUT forcado (quem manda e o 'Explode' do
            // Grimm de verdade) e sem mexer no SetHP 0 (a "esponja de dano" funciona como no original: o 'Adjust HP' do Grimm soma o dano).
            d[GrimmBatName] = new ArenaProfile
            {
                Ref = new Vector2(87.2f, 5.0f),
                WakeEvents = new[] { ("Control", "Init", "BOSS AWAKE") },
                WorldTweens = new[] { ("Control", "Get To Middle", float.NaN) },
            };
        }

        public const string GrimmBatName = "Real Bat (Grimm)";

        /// <summary>Chamado no fim do SpawnFixups.AfterActivate (FSM ja acordada e deslocada, antes do Start dela/do HealthManager).</summary>
        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            if (ctx == null || clone == null || ctx.Name != Name) return;
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Control", "Init", "Dormant", "Position", "End");
                if (fsm == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Real Bat: FSM 'Control' com Init/Dormant/Position/End nao encontrada - sem ajustes");
                    return;
                }
                var grimm = fsm.FsmVariables.FindFsmGameObject("Grimm");
                if (grimm != null) grimm.Value = clone;
                else Modding.Logger.LogWarn("[CreatureSpawner] Real Bat: variavel 'Grimm' nao encontrada - 'Position' pode mandar ele pra (0,0)");

                var hm = clone.GetComponent<HealthManager>();
                if (hm == null) return;
                hm.hp = StandaloneHp;          // HealthManager.Start ainda roda (hpScale 0/0/0 = mantem)
                hm.hasSpecialDeath = false;    // Die passa a marcar isDead + OnDeath
                var watch = clone.AddComponent<RealBatDeath>();
                watch.Fsm = fsm;
                hm.OnDeath += watch.OnDied;
                Modding.Logger.Log($"[CreatureSpawner] Real Bat: Grimm=copia, hp={StandaloneHp}, hasSpecialDeath=false");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] RealBatFix: {ex}");
            }
        }
    }

    /// <summary>Morte da copia do Real Bat: o original nunca morre (nao ha estado de morte na FSM nem EnemyDeathEffects). No frame
    /// seguinte ao Die: para, desliga o collider e vai pro 'End' (anim 'Bat End' -> FINISHED -> 'Inactive', renderer off); ao chegar
    /// no 'Inactive'/'Dormant' (ou depois de MaxEnd s, se o clipe nao terminar) remove a copia.</summary>
    internal sealed class RealBatDeath : MonoBehaviour
    {
        private const float MaxEnd = 2f;
        public PlayMakerFSM Fsm;
        private bool died, ended;
        private float diedAt;

        public void OnDied()
        {
            if (died) return;
            died = true;
            diedAt = Time.time;
        }

        private void Update()
        {
            if (!died) return;
            try
            {
                if (!ended)
                {
                    ended = true;
                    var rb = GetComponent<Rigidbody2D>();
                    if (rb != null) rb.velocity = Vector2.zero;
                    foreach (var c in GetComponents<Collider2D>()) c.enabled = false;
                    if (Fsm != null) Fsm.SetState("End");
                    Modding.Logger.Log($"[CreatureSpawner] Real Bat '{name}': morreu -> 'End' (anim 'Bat End'), copia removida em seguida");
                    return;
                }
                string st = Fsm != null ? Fsm.ActiveStateName : null;
                if (Fsm == null || st == "Inactive" || st == "Dormant" || Time.time - diedAt > MaxEnd) Destroy(gameObject);
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] RealBatDeath: {ex}");
                Destroy(gameObject);
            }
        }
    }
}
