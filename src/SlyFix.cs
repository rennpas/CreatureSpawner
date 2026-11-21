using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Great Nailsage Sly ('Sly Boss', GG_Sly): dashes errados na fase de furia, "atravessa o chao" e precisa "teletransportar pra baixo
    // primeiro" (relatado). Fonte: scratchpad\FsmDumpTool\out\sly_scene.txt, FSM 'Control' - tudo em coordenadas ABSOLUTAS da arena GG
    // original (centro X 46.45, pivo dele no chao y=4.99, teto 17.47):
    //  - Fase de furia ('Rage Dash'): ele voa a 55 e, ao bater, a FSM TELEPORTA pra borda: 'Bounce L' x=32.26, 'Bounce R' x=60.64,
    //    'Bounce D' y=4.99 (chao), 'Bounce U' y=17.47 (teto) e continua ('Fire *'). Fora da arena isso joga ele pra dentro do chao/no
    //    ar longe do heroi; sem parede perto, so o Wait de 1 s (-> 'Bounce U') o trazia de volta. 'Air Catch' (entrada da furia)
    //    faz SetPosition (46.95, 11.32).
    //  - Ataque de unha DASH ('Jump To R/L'): End X 40.47/52.78 e Target X 60.31/32.57 fixos; 'D Slash S1' faz SetPosition x=End X.
    //  - 'Stomp Antic': SetPosition y=Topslash Y (15.7). Ciclone: 'Cyc Jump Launch' pula pro X 46.82, 'Cyc Down' compara Y < 15.
    // Correcao (sem ArenaProfile): arena VIRTUAL em volta do heroi do spawn - mesmas medidas da original (meia largura 13.9, teto
    // 12.48 acima do chao), limitada pelas paredes/teto reais (raycast Terrain) - e todas as constantes acima apontam pra ela.
    // Como numa sala aberta nao ha parede nas bordas virtuais, o vigia SlyRageBounds manda L/R/LAND/ROOF quando ele passa delas
    // (os mesmos eventos que a colisao mandaria), e os 'Bounce *' fazem o teleporte (pra borda virtual) como na luta original.
    internal sealed class SlyFix : MonoBehaviour
    {
        public const string Name = "Great Nailsage Sly";
        private const float OrigCenterX = 46.45f, HalfWidth = 13.9f, RoofRise = 12.48f;
        private const float DashEndOffsetL = 6.0f, DashEndOffsetR = 6.33f;

        private PlayMakerFSM fsm;
        private float left, right, floorY, roofY;
        private int logs;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Control", "Rage Dash", "Bounce L", "Bounce R", "Bounce D", "Bounce U", "Air Catch",
                    "Jump To R", "Jump To L", "Stomp Antic", "Cyc Jump Launch", "Cyc Down");
                if (fsm == null || ctx == null) return;

                float cx = ctx.HeroPos.x;
                float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out bool found, out _);
                float feet = 1.3f, top = 1.3f, half = 0.9f;
                var col = clone.GetComponent<BoxCollider2D>();
                if (col != null)
                {
                    float sx = Mathf.Abs(clone.transform.lossyScale.x), sy = Mathf.Abs(clone.transform.lossyScale.y);
                    feet = Mathf.Clamp(-(col.offset.y - col.size.y * 0.5f) * sy, 0.3f, 4f);
                    top = Mathf.Clamp((col.offset.y + col.size.y * 0.5f) * sy, 0.3f, 4f);
                    half = Mathf.Clamp(col.size.x * 0.5f * sx, 0.3f, 3f);
                }
                float floor = groundY + feet, mid = groundY + 3f;

                var hl = Physics2D.Raycast(new Vector2(cx, mid), Vector2.left, HalfWidth + 0.6f, SpawnFixups.TerrainMask);
                var hr = Physics2D.Raycast(new Vector2(cx, mid), Vector2.right, HalfWidth + 0.6f, SpawnFixups.TerrainMask);
                var hu = Physics2D.Raycast(new Vector2(cx, floor), Vector2.up, RoofRise + top + 0.3f, SpawnFixups.TerrainMask);
                float left = hl.collider != null ? hl.point.x + half + 0.2f : cx - HalfWidth;
                float right = hr.collider != null ? hr.point.x - half - 0.2f : cx + HalfWidth;
                float roof = hu.collider != null ? hu.point.y - top - 0.15f : floor + RoofRise;
                if (right - left < 6f) { left = cx - 3f; right = cx + 3f; }
                if (roof < floor + 4f) roof = floor + 4f;

                // Ataque de unha DASH: pula pro lado oposto do heroi (Target X) e corre ate End X.
                var jr = GruzZoteFixes.GetState(fsm, "Jump To R");
                SetConst(jr, "End X", Mathf.Clamp(cx - DashEndOffsetL, left + 1f, right - 1f));
                SetConst(jr, "Target X", right - 0.4f);
                var jl = GruzZoteFixes.GetState(fsm, "Jump To L");
                SetConst(jl, "End X", Mathf.Clamp(cx + DashEndOffsetR, left + 1f, right - 1f));
                SetConst(jl, "Target X", left + 0.4f);

                // Furia: os 4 'Bounce' teleportam pra borda; 'Air Catch' e a entrada no ar.
                SetPos(GruzZoteFixes.GetState(fsm, "Bounce L"), left, null);
                SetPos(GruzZoteFixes.GetState(fsm, "Bounce R"), right, null);
                SetPos(GruzZoteFixes.GetState(fsm, "Bounce D"), null, floor);
                SetPos(GruzZoteFixes.GetState(fsm, "Bounce U"), null, roof);
                SetPos(GruzZoteFixes.GetState(fsm, "Air Catch"), cx, Mathf.Min(floor + 6.33f, roof - 0.5f));

                // Stomp (mergulho de cima) e ciclone.
                var topslash = fsm.FsmVariables.FindFsmFloat("Topslash Y");
                if (topslash != null) topslash.Value = Mathf.Min(floor + 10.71f, roof - 0.5f);
                foreach (var a in GruzZoteFixes.GetState(fsm, "Cyc Jump Launch").Actions)
                    if (a is FloatOperator fo && fo.float1 != null && !fo.float1.IsNone && !fo.float1.UsesVariable) fo.float1.Value = cx;
                foreach (var a in GruzZoteFixes.GetState(fsm, "Cyc Down").Actions)
                    if (a is FloatCompare fc && fc.float2 != null && !fc.float2.IsNone && !fc.float2.UsesVariable)
                        fc.float2.Value = Mathf.Clamp(floor + 10.01f, floor + 2f, roof - 1f);

                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null)
                {
                    leash.RadiusX = Mathf.Max(leash.RadiusX, HalfWidth + 6f);
                    leash.RadiusYUp = Mathf.Max(leash.RadiusYUp, roof - groundY);
                }

                var fix = clone.AddComponent<SlyFix>();
                fix.fsm = fsm; fix.left = left; fix.right = right; fix.floorY = floor; fix.roofY = roof;
                Modding.Logger.Log($"[CreatureSpawner] Sly: arena virtual X {left:0.##}..{right:0.##}, chao(pivo) y={floor:0.##}, teto y={roof:0.##} " +
                                   $"(chao {(found ? "raycast" : "pes do heroi")}, parede E {(hl.collider != null)}, D {(hr.collider != null)}, teto {(hu.collider != null)})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] SlyFix: {ex}");
            }
        }

        // 'Rage Dash': so sai por colisao (L/R/LAND/ROOF) ou pelo Wait de 1 s. Nas bordas virtuais (sem parede de verdade) manda o
        // mesmo evento; o 'Bounce *' correspondente teleporta pra borda virtual (ja deslocada) e dispara pro outro lado.
        // Objetos da SALA que a FSM espera e que nao vem no prefab (ver sly_scene.txt):
        //  - 'Stun Nail' (prego que ele joga quando atordoado): 'Stun Wait' so sai quando o FSM do prego poe 'Nail Landed'=true e
        //    'Grabbing' so sai com o GRABBED que o prego manda. Sem o prego ele ficava preso no atordoamento ate a vida zerar
        //    (ZERO HP e global). Aqui: 'Nail Landed' liga depois de StunWaitSeconds e o GRABBED sai depois de GrabSeconds.
        //  - Fim: 'Bow' -> 'Battle End' (EndGGBossScene, so age dentro do Godhome) e a FSM fica parada pra sempre, ele curvado.
        //    Aqui a copia some BowEndSeconds depois de chegar em 'Battle End'.
        private const float StunWaitSeconds = 1.0f, GrabSeconds = 0.5f, BowEndSeconds = 1.5f;
        private string lastState;
        private float stateSince;
        private bool handled;

        private void Update()
        {
            if (fsm == null) return;
            string st = fsm.ActiveStateName;
            if (st != lastState)
            {
                lastState = st;
                stateSince = Time.time;
                handled = false;
            }
            if (!handled)
            {
                float t = Time.time - stateSince;
                if (st == "Stun Wait" && t >= StunWaitSeconds)
                {
                    handled = true;
                    var landed = fsm.FsmVariables.FindFsmBool("Nail Landed");
                    if (landed != null) landed.Value = true;
                    Modding.Logger.Log("[CreatureSpawner] Sly: 'Stun Wait' sem o prego da sala -> Nail Landed");
                }
                else if (st == "Grabbing" && t >= GrabSeconds)
                {
                    handled = true;
                    Modding.Logger.Log("[CreatureSpawner] Sly: 'Grabbing' sem o prego da sala -> GRABBED");
                    fsm.SendEvent("GRABBED");
                }
                else if (st == "Battle End" && t >= BowEndSeconds)
                {
                    handled = true;
                    Modding.Logger.Log("[CreatureSpawner] Sly: fim da luta ('Battle End') -> removendo a copia");
                    Destroy(gameObject);
                    return;
                }
            }
            if (st != "Rage Dash") return;
            Vector3 p = transform.position;
            string evt = null;
            if (p.x < left - 0.05f) evt = "L";
            else if (p.x > right + 0.05f) evt = "R";
            else if (p.y < floorY - 0.05f) evt = "LAND";
            else if (p.y > roofY + 0.05f) evt = "ROOF";
            if (evt == null) return;
            if (logs++ < 12) Modding.Logger.Log($"[CreatureSpawner] Sly: furia passou da borda virtual em ({p.x:0.##},{p.y:0.##}) -> {evt}");
            fsm.SendEvent(evt);
        }

        private static void SetConst(FsmState state, string varName, float value)
        {
            foreach (var a in state.Actions)
                if (a is SetFloatValue sf && sf.floatVariable != null && sf.floatVariable.Name == varName && sf.floatValue != null)
                    sf.floatValue.Value = value;
        }

        private static void SetPos(FsmState state, float? x, float? y)
        {
            foreach (var a in state.Actions)
            {
                if (!(a is SetPosition sp)) continue;
                if (x.HasValue && sp.x != null && !sp.x.IsNone && !sp.x.UsesVariable) sp.x.Value = x.Value;
                if (y.HasValue && sp.y != null && !sp.y.IsNone && !sp.y.UsesVariable) sp.y.Value = y.Value;
            }
        }
    }
}
