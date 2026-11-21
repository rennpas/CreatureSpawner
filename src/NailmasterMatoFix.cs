using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // "Mato (Solo)" do painel = 'Brothers/Mato' lutando sozinho. O perfil (ArenaProfilesVariants) faz o grosso: intro do
    // Oro, 'Activate' sem Brothered, Topslash Y deslocada, morte -> Defeated/Bow/End. Aqui ficam dois detalhes que o perfil nao expressa
    // (scratchpad\FsmDumpTool\out\nailmasters_brothers.txt, FSM 'nailmaster' de Brothers/Mato):
    //  - 'Roar' (a intro emprestada do Oro) mostra o titulo SetFsmString 'Area Event' = 'NM_ORO' (o nome do Oro). Troca por 'TEMP_NM',
    //    a mesma chave que o 'Entry Roar' do proprio Mato usa no jogo. So a acao desta copia (a FSM ja inicializada).
    //  - 'Cyclone Up': gravidade 0 + AddForce2d pra cima todo frame; a UNICA saida e FloatCompare Self Y > Topslash Y (o Wait 1.5 do
    //    template esta desligado). Topslash Y = chao + 12.1 (deslocada pelo perfil); numa sala com teto mais baixo que isso ele fica
    //    preso girando contra o teto pra sempre. Vigia: se ficar mais de MaxCyclone s no estado (normal ~1.4 s), baixa a Topslash Y
    //    DESTA copia pra logo abaixo da altura atual -> o proprio FloatCompare do estado manda FINISHED -> Cyclone End -> Dstab Antic
    //    (SetPosition y=Topslash Y = praticamente onde ele ja esta, nada de teleporte pra dentro/acima do teto) -> Dstab -> chao.
    //    O valor baixado vale pro resto da luta (= altura que o teto desta sala permite; o Jump tambem passa a fazer o Dstab dali).
    //    O mesmo vigia e usado no Mato da luta em dupla ("Nailmasters Oro & Mato", ver NailmastersPair.cs) - so o Mato faz o ciclone
    //    ('Art Choice': Oro=true -> dash-slash, Oro=false -> ciclone).
    internal sealed class NailmasterMatoFix : MonoBehaviour
    {
        public const string Name = "Mato (Solo)";
        private const float MaxCyclone = 3f;
        private const int MaxLogs = 6;

        private PlayMakerFSM fsm;
        private FsmFloat topslash;
        private float enteredAt = -1f;
        private int logs;

        public static void AfterActivate(GameObject clone, SpawnContext ctx)
        {
            if (clone == null || ctx == null || ctx.Name != Name) return;
            try
            {
                var f = GruzZoteFixes.FindFsm(clone, "nailmaster", "Roar", "Cyclone Up");
                if (f == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Mato: FSM 'nailmaster' com Roar/Cyclone Up nao encontrada - sem ajustes extras");
                    return;
                }
                foreach (var st in f.FsmStates)
                {
                    if (st.Name != "Roar") continue;
                    foreach (var a in st.Actions)
                        if (a is SetFsmString s && s.setValue != null && s.setValue.Value == "NM_ORO")
                            s.setValue.Value = "TEMP_NM";
                }
                AttachCycloneWatch(clone, f);
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NailmasterMatoFix: {ex}");
            }
        }

        /// <summary>Vigia do 'Cyclone Up' sob teto baixo na FSM 'nailmaster' (ja inicializada) deste Mato.</summary>
        public static void AttachCycloneWatch(GameObject mato, PlayMakerFSM f)
        {
            if (mato == null || f == null || mato.GetComponent<NailmasterMatoFix>() != null) return;
            var w = mato.AddComponent<NailmasterMatoFix>();
            w.fsm = f;
            w.topslash = f.FsmVariables.FindFsmFloat("Topslash Y");
        }

        private void Update()
        {
            if (fsm == null || topslash == null) { Destroy(this); return; }
            if (fsm.ActiveStateName != "Cyclone Up") { enteredAt = -1f; return; }
            if (enteredAt < 0f) { enteredAt = Time.time; return; }
            if (Time.time - enteredAt < MaxCyclone) return;
            float y = transform.position.y;
            if (y <= topslash.Value)
            {
                if (logs++ < MaxLogs)
                    Modding.Logger.Log($"[CreatureSpawner] Mato '{name}': 'Cyclone Up' ha {Time.time - enteredAt:0.##}s sem passar Topslash Y " +
                                       $"{topslash.Value:0.##} (y={y:0.##}, teto baixo?) -> Topslash Y = {y - 0.1f:0.##}");
                topslash.Value = y - 0.1f;
            }
            enteredAt = -1f;
        }
    }
}
