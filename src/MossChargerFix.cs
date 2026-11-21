using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CreatureSpawner
{
    // Massive Moss Charger ('Mega Moss Charger', Fungus1_29) "fica parado no final" da corrida e do pulo (relatado, com print).
    // Fonte: scratchpad\FsmDumpTool\out\mmc_scene.txt, FSM 'Mossy Control'. Ele ja e relativo ao ponto onde nasce (Start X/Start Y,
    // X Min/X Max = Start X -+ metade do 'Attack Range' + 3), mas:
    //  - 'Charge': SetVelocity2d UMA vez e a unica saida e SUBMERGE, mandado por Collision2dEventLayer (camada 8) ou pelo filho
    //    'Terrain Checker' = bater numa PAREDE. Na arena original tem parede dos dois lados; numa sala aberta ele corre ate a
    //    borda da coleira, que zera a velocidade, e fica la pra sempre.
    //  - 'In Air' (pulo): unica saida = Self Y <= Start Y. Na borda da coleira a elipse segura o Y um fio acima do Start Y.
    // Correcao: na 'Charge', passou da faixa de ataque (X Min - 3 / X Max + 3) no sentido da corrida, ou ficou parado sem avancar
    // -> SUBMERGE (o mesmo evento da parede). No 'In Air', parado no ar -> LAND. So eventos que a FSM ja espera nesses estados.
    internal sealed class MossChargerFix : MonoBehaviour
    {
        public const string Name = "Massive Moss Charger";
        private const float RangeMargin = 3f, StallWindow = 0.3f, StallDist = 0.5f, AirStallDist = 0.3f;

        private PlayMakerFSM fsm;
        private FsmFloat xMin, xMax, speed;
        private string lastState;
        private float windowStart;
        private Vector2 windowPos;
        private int logs;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Mossy Control", "Charge", "In Air", "Submerge 1", "Land");
                if (fsm == null) return;
                var fix = clone.AddComponent<MossChargerFix>();
                fix.fsm = fsm;
                fix.xMin = fsm.FsmVariables.FindFsmFloat("X Min");
                fix.xMax = fsm.FsmVariables.FindFsmFloat("X Max");
                fix.speed = fsm.FsmVariables.FindFsmFloat("Current Charge Speed");
                Modding.Logger.Log("[CreatureSpawner] Massive Moss Charger: vigia da corrida/pulo ligado");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] MossChargerFix: {ex}");
            }
        }

        private void Update()
        {
            if (fsm == null) return;
            string st = fsm.ActiveStateName;
            Vector2 p = transform.position;
            if (st != lastState)
            {
                lastState = st;
                windowStart = Time.time;
                windowPos = p;
                return;
            }
            bool charge = st == "Charge", air = st == "In Air";
            if (!charge && !air) return;
            string why = null;
            if (charge && speed != null && xMin != null && xMax != null && xMax.Value > xMin.Value)
            {
                if (speed.Value > 0f && p.x > xMax.Value + RangeMargin) why = "passou do fim da faixa (direita)";
                else if (speed.Value < 0f && p.x < xMin.Value - RangeMargin) why = "passou do fim da faixa (esquerda)";
            }
            if (why == null && Time.time - windowStart >= StallWindow)
            {
                float moved = charge ? Mathf.Abs(p.x - windowPos.x) : (p - windowPos).magnitude;
                if (moved < (charge ? StallDist : AirStallDist)) why = "parado sem avancar";
                windowStart = Time.time;
                windowPos = p;
            }
            if (why == null) return;
            string evt = charge ? "SUBMERGE" : "LAND";
            if (logs++ < 12) Modding.Logger.Log($"[CreatureSpawner] Massive Moss Charger: '{st}' em ({p.x:0.##},{p.y:0.##}): {why} -> {evt}");
            fsm.SendEvent(evt);
        }
    }
}
