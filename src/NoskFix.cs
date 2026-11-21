using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Nosk ('Mimic Spider', Deepnest_32) "corre pra sempre" no ataque de ir de um lado ao outro (relatado). Fonte:
    // scratchpad\FsmDumpTool\out\nosk_scene.txt. FSM 'Mimic Spider', tudo em X/Y ABSOLUTOS da arena original (X 73.39..115.55):
    //  - 'Charge L'/'Charge R': SetVelocity2d todo frame; unica saida = Self X passar de Jump Min X / Jump Max X (ou o cuspe no
    //    meio). Fora da arena a borda nunca chega: ele corre contra a coleira/parede pra sempre (e a FSM 'Climb' sobe a parede).
    //  - 'Aim Jump' (alvo aleatorio em Jump Min X..Jump Max X), 'Idle' (FloatInRange 88.32..103.78), 'Set Spit' (Spit X em
    //    92.7..99.6), FSM 'constrain_x' (Edge L/R 73.35/115.703: SetPosition na borda) e 'Spit Antic' (SetPosition y=8.84).
    //  - 'RS Jump' (cuspe do teto): gravidade 0, sobe a 55 ate Self Y > Roof Y (21.3); corpo dinamico -> um teto mais baixo (ou a
    //    coleira, RadiusYUp 9) segura ele antes e o estado nunca sai.
    // Correcao (sem ArenaProfile):
    //  - Os X acima deslocados pro heroi do spawn (centro original 94.47); 'Spit Antic' mantem o Y em que ele esta (no chao).
    //  - Corrida: parede Terrain na frente (raycast) ou parado sem avancar (coleira, degrau) -> FINISHED = o mesmo fim da borda
    //    ('Wait' -> CHARGE END -> Idle). Pedido do usuario.
    //  - 'RS Jump': Roof Y = a mesma subida do original (12.46) a partir de onde ele esta, ou logo abaixo do teto real; coleira
    //    com folga pra cima; vigia: mais de 1.5 s subindo -> LAND.
    internal sealed class NoskFix : MonoBehaviour
    {
        public const string Name = "Nosk";
        private const float OrigCenterX = 94.47f;
        internal const float RoofRise = 12.46f; // Roof Y 21.3 - pivo dele em pe 8.84
        private const float WallAhead = 0.6f, StallWindow = 0.3f, StallDist = 0.5f, RoofTimeout = 1.5f;

        private PlayMakerFSM fsm;
        private BoxCollider2D box;
        private string lastState;
        private float stateSince, windowStart, windowX;
        private int logs;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Mimic Spider", "Charge L", "Charge R", "RS Jump", "Spit Antic", "Set Spit", "Idle");
                if (fsm == null || ctx == null) return;
                float dx = ctx.HeroPos.x - OrigCenterX;

                AddFloat(fsm, "Jump Min X", dx);
                AddFloat(fsm, "Jump Max X", dx);
                foreach (var a in GruzZoteFixes.GetState(fsm, "Idle").Actions)
                    if (a is FloatInRange r)
                    {
                        if (IsConst(r.lowerValue)) r.lowerValue.Value += dx;
                        if (IsConst(r.upperValue)) r.upperValue.Value += dx;
                    }
                foreach (var a in GruzZoteFixes.GetState(fsm, "Set Spit").Actions)
                    if (a is RandomFloat rf)
                    {
                        if (IsConst(rf.min)) rf.min.Value += dx;
                        if (IsConst(rf.max)) rf.max.Value += dx;
                    }
                foreach (var a in GruzZoteFixes.GetState(fsm, "Spit Antic").Actions)
                    if (a is SetPosition sp) sp.y = new FsmFloat { UseVariable = true };
                var cx = GruzZoteFixes.FindFsm(clone, "constrain_x", "Constrain L", "Constrain R");
                if (cx != null)
                {
                    AddFloat(cx, "Edge L", dx);
                    AddFloat(cx, "Edge R", dx);
                }

                var fix = clone.AddComponent<NoskFix>();
                fix.fsm = fsm;
                fix.box = clone.GetComponent<BoxCollider2D>();
                var rs = GruzZoteFixes.GetState(fsm, "RS Jump");
                var old = rs.Actions;
                var actions = new FsmStateAction[old.Length + 1];
                actions[0] = new NoskRoofAction { Boss = clone, Box = fix.box, RoofY = fsm.FsmVariables.FindFsmFloat("Roof Y") };
                Array.Copy(old, 0, actions, 1, old.Length);
                rs.Actions = actions;

                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null) leash.RadiusYUp = 16f;
                Modding.Logger.Log($"[CreatureSpawner] Nosk: arena deslocada em X por {dx:0.##} (constrain_x={(cx != null)}), corrida com fim por parede/parada");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NoskFix: {ex}");
            }
        }

        private void Update()
        {
            if (fsm == null) return;
            string st = fsm.ActiveStateName;
            float x = transform.position.x;
            if (st != lastState)
            {
                lastState = st;
                stateSince = windowStart = Time.time;
                windowX = x;
                return;
            }
            if (st == "RS Jump")
            {
                if (Time.time - stateSince < RoofTimeout) return;
                stateSince = Time.time;
                Log($"{RoofTimeout:0.#}s no 'RS Jump' sem chegar no teto em {transform.position} -> LAND");
                fsm.SendEvent("LAND");
                return;
            }
            if (st != "Charge L" && st != "Charge R") return;
            float dir = st == "Charge R" ? 1f : -1f;
            string why = null;
            if (box != null)
            {
                Vector2 c = transform.TransformPoint(box.offset);
                float half = box.size.x * 0.5f * Mathf.Abs(transform.lossyScale.x);
                var hit = Physics2D.Raycast(c, new Vector2(dir, 0f), half + WallAhead, SpawnFixups.TerrainMask);
                if (hit.collider != null) why = $"parede em x={hit.point.x:0.##}";
            }
            if (why == null && Time.time - windowStart >= StallWindow)
            {
                if (Mathf.Abs(x - windowX) < StallDist) why = "parado sem avancar";
                windowStart = Time.time;
                windowX = x;
            }
            if (why == null) return;
            Log($"'{st}' em x={x:0.##}: {why} -> FINISHED");
            fsm.SendEvent("FINISHED");
        }

        private void Log(string msg)
        {
            if (logs++ < 12) Modding.Logger.Log("[CreatureSpawner] Nosk: " + msg);
        }

        private static bool IsConst(FsmFloat f) => f != null && !f.IsNone && !f.UsesVariable;

        private static void AddFloat(PlayMakerFSM fsm, string name, float add)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value += add;
            else Modding.Logger.LogWarn($"[CreatureSpawner] Nosk: FSM '{fsm.FsmName}' sem a var '{name}'");
        }
    }

    /// <summary>'RS Jump': Roof Y relativo a onde ele esta, limitado pelo teto real (corpo dinamico: nao atravessa).</summary>
    internal sealed class NoskRoofAction : FsmStateAction
    {
        public GameObject Boss;
        public BoxCollider2D Box;
        public FsmFloat RoofY;

        public override void OnEnter()
        {
            try
            {
                if (Boss != null && RoofY != null)
                {
                    Vector2 p = Boss.transform.position;
                    float top = Box != null ? Box.offset.y + Box.size.y * 0.5f : 0f;
                    float roof = p.y + NoskFix.RoofRise;
                    var up = Physics2D.Raycast(new Vector2(p.x, p.y + top), Vector2.up, NoskFix.RoofRise + 0.3f, SpawnFixups.TerrainMask);
                    if (up.collider != null) roof = Mathf.Min(roof, up.point.y - top - 0.3f);
                    RoofY.Value = roof;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] NoskRoofAction: {ex}");
            }
            Finish();
        }
    }
}
