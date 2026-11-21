using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // "The Collector" ('Jar Collector', Ruins2_11_boss) nem aparecia (relatado). Fonte: scratchpad\FsmDumpTool\out\collector_scene.txt
    // + SpawnJarControl decompilado.
    //  - FSM 'Control': Init -> 'Sleep' (collider desligado no prefab, renderer so liga no 'Start Fall'), unica saida = WAKE global,
    //    mandado pelo pai 'Battle Scene' (trigger da sala, nao vem no preload) -> invisivel pra sempre.
    //  - Alturas ABSOLUTAS da arena original (chao ~94): 'Jump' sobe ate Self Y > Roof Y (109); 'Return Antic' faz SetPosition
    //    (aleatorio em Roof X L..R = 44.28..64.23, Return Y = 106.88); 'Spawn' poe o jarro em X aleatorio de Bottle XL..XR
    //    (42.94..65.98); o jarro (pool global, SpawnJarControl) vai sozinho pra spawnY = 106.52 e quebra em breakY = 94.55;
    //    'Death Start' faz FloatClamp do Y em 0..100.
    // Correcao (sem ArenaProfile; tudo calculado aqui):
    //  - WAKE no 'Sleep'; ele nasce no alto (como na sala: cai do teto), numa coluna livre perto do heroi.
    //  - Faixas X deslocadas pro heroi do spawn (centro original 54.4). Alturas recalculadas A CADA uso pela sala atual (acoes
    //    inseridas so nesta copia): teto do pulo, ponto de volta e jarro ficam na mesma altura acima do chao que no original,
    //    limitados pelo teto real (raycast Terrain), numa coluna com linha livre ate o heroi.
    //  - Jarro: spawnY/breakY ajustados na instancia do pool e devolvidos quando ela e reciclada (CollectorJarRestore).
    //  - Coleira: ancora no chao do spawn e folga pra cima (o pulo passa de 9); vigia: mais de 1 s subindo no 'Jump' -> END.
    //  - 'Kill Enemies' (ramo Godhome forcado pelo ForceBossSceneBranch): o KILL ALL ENEMIES mataria inimigos da sala atual.
    internal sealed class CollectorFix : MonoBehaviour
    {
        public const string Name = "The Collector";
        private const float OrigCenterX = 54.4f, OrigFloorY = 94f;
        internal const float EntranceHeight = 9.75f;   // 103.75 - 94
        internal const float ReturnHeight = 12.88f;    // Return Y 106.88 - 94
        internal const float JarHeight = 12.52f;       // spawnY 106.52 - 94
        internal const float JarBreakHeight = 0.6f;    // breakY 94.55 - 94 (+ folga)
        internal const float RoofRise = 12.2f;         // Roof Y 109 - pivo dele em pe (~96.8)
        private const float JumpTimeout = 1f;

        private PlayMakerFSM fsm;
        private float jumpSince = -1f;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Control", "Sleep", "Jump", "Return Antic", "Spawn", "Death Start");
                if (fsm == null || ctx == null) return;
                float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out _, out _);
                float dx = ctx.HeroPos.x - OrigCenterX, dy = groundY - OrigFloorY;

                foreach (var n in new[] { "Bottle XL", "Bottle XR", "Roof X L", "Roof X R" }) AddFloat(fsm, n, dx);
                var roofVar = fsm.FsmVariables.FindFsmFloat("Roof Y");
                var jarVar = fsm.FsmVariables.FindFsmGameObject("Spawn Jar");

                Insert(GruzZoteFixes.GetState(fsm, "Jump"), 0, new CollectorRoofAction { Boss = clone, RoofY = roofVar });
                InsertAfter<SetPosition>(GruzZoteFixes.GetState(fsm, "Return Antic"), new CollectorReturnAction { Boss = clone });
                InsertAfter<SetPosition>(GruzZoteFixes.GetState(fsm, "Spawn"), new CollectorJarAction { Jar = jarVar });
                foreach (var a in GruzZoteFixes.GetState(fsm, "Death Start").Actions)
                    if (a is FloatClamp fc)
                    {
                        if (fc.minValue != null && !fc.minValue.UsesVariable) fc.minValue.Value += dy;
                        if (fc.maxValue != null && !fc.maxValue.UsesVariable) fc.maxValue.Value += dy;
                    }
                var death = GruzZoteFixes.FindFsm(clone, "Death", "Kill Enemies");
                if (death != null)
                    foreach (var a in GruzZoteFixes.GetState(death, "Kill Enemies").Actions)
                        if (a.GetType().Name == "SendEventToRegister") a.Enabled = false;

                Column(ctx.HeroPos.x + 4f, EntranceHeight, 0.3f, out float x, out float y, out _);
                clone.transform.position = new Vector3(x, y, clone.transform.position.z);
                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null)
                {
                    leash.SetAnchor(new Vector3(ctx.HeroPos.x, groundY + 3f, clone.transform.position.z));
                    leash.RadiusYUp = 18f;
                }
                clone.AddComponent<CollectorFix>().fsm = fsm;
                FsmEventWhenInState.Add(clone, "Control", "Sleep", "WAKE");
                Modding.Logger.Log($"[CreatureSpawner] The Collector: WAKE agendado, entrada em ({x:0.##},{y:0.##}), chao y={groundY:0.##}, delta=({dx:0.##},{dy:0.##})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CollectorFix: {ex}");
            }
        }

        // 'Jump' (cinematico, sobe a 40) so sai com Self Y > Roof Y; se a coleira segurar ele antes disso, nunca sai.
        private void Update()
        {
            if (fsm == null) return;
            if (fsm.ActiveStateName != "Jump") { jumpSince = -1f; return; }
            if (jumpSince < 0f) { jumpSince = Time.time; return; }
            if (Time.time - jumpSince < JumpTimeout) return;
            jumpSince = -1f;
            Modding.Logger.Log($"[CreatureSpawner] The Collector: {JumpTimeout:0.#}s no 'Jump' sem passar do teto em {transform.position} -> END");
            fsm.SendEvent("END");
        }

        /// <summary>Coluna livre pra algo "vir do teto": X pedido (encurtado se tiver parede entre ele e o heroi), Y = chao dessa
        /// coluna + height, limitado pelo teto real acima dela.</summary>
        internal static void Column(float wantX, float height, float margin, out float x, out float y, out float ground)
        {
            var hc = HeroController.instance;
            Vector2 h = hc != null ? (Vector2)hc.transform.position : new Vector2(wantX, 0f);
            x = wantX;
            if (Mathf.Abs(wantX - h.x) > 0.05f)
            {
                float dir = Mathf.Sign(wantX - h.x);
                var wall = Physics2D.Linecast(h, new Vector2(wantX, h.y), SpawnFixups.TerrainMask);
                if (wall.collider != null)
                {
                    x = wall.point.x - dir * 1.5f;
                    if ((x - h.x) * dir < 0f) x = h.x;
                }
            }
            var down = Physics2D.Raycast(new Vector2(x, h.y), Vector2.down, SpawnFixups.GroundProbeDistance, SpawnFixups.TerrainMask);
            ground = down.collider != null ? down.point.y : h.y - SpawnFixups.HeroPivotAboveFeet;
            y = ground + height;
            if (y > h.y)
            {
                var up = Physics2D.Raycast(new Vector2(x, h.y), Vector2.up, y - h.y + margin, SpawnFixups.TerrainMask);
                if (up.collider != null) y = up.point.y - margin;
            }
        }

        private static void AddFloat(PlayMakerFSM fsm, string name, float add)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value += add;
            else Modding.Logger.LogWarn($"[CreatureSpawner] The Collector: FSM '{fsm.FsmName}' sem a var '{name}'");
        }

        private static void Insert(FsmState state, int index, FsmStateAction action)
        {
            var old = state.Actions;
            var actions = new FsmStateAction[old.Length + 1];
            Array.Copy(old, 0, actions, 0, index);
            actions[index] = action;
            Array.Copy(old, index, actions, index + 1, old.Length - index);
            state.Actions = actions;
        }

        private static void InsertAfter<T>(FsmState state, FsmStateAction action) where T : FsmStateAction
        {
            var old = state.Actions;
            for (int i = 0; i < old.Length; i++)
                if (old[i] is T) { Insert(state, i + 1, action); return; }
            Modding.Logger.LogWarn($"[CreatureSpawner] The Collector: estado '{state.Name}' sem {typeof(T).Name} - ajuste nao inserido");
        }
    }

    /// <summary>'Jump': teto do pulo = a mesma subida do original, ou logo depois de entrar no teto real se ele for mais baixo.</summary>
    internal sealed class CollectorRoofAction : FsmStateAction
    {
        public GameObject Boss;
        public FsmFloat RoofY;

        public override void OnEnter()
        {
            try
            {
                if (Boss != null && RoofY != null)
                {
                    Vector2 p = Boss.transform.position;
                    var up = Physics2D.Raycast(p, Vector2.up, CollectorFix.RoofRise, SpawnFixups.TerrainMask);
                    RoofY.Value = up.collider != null ? up.point.y + 0.5f : p.y + CollectorFix.RoofRise;
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CollectorRoofAction: {ex}");
            }
            Finish();
        }
    }

    /// <summary>'Return Antic', depois do SetPosition original: mantem o X sorteado (se der) e poe a altura da sala atual.</summary>
    internal sealed class CollectorReturnAction : FsmStateAction
    {
        public GameObject Boss;

        public override void OnEnter()
        {
            try
            {
                if (Boss != null)
                {
                    var t = Boss.transform;
                    CollectorFix.Column(t.position.x, CollectorFix.ReturnHeight, 0.3f, out float x, out float y, out _);
                    t.position = new Vector3(x, y, t.position.z);
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CollectorReturnAction: {ex}");
            }
            Finish();
        }
    }

    /// <summary>'Spawn', depois do SetPosition do jarro: spawnY/breakY desta instancia pela sala atual.</summary>
    internal sealed class CollectorJarAction : FsmStateAction
    {
        public FsmGameObject Jar;
        private int logs;

        public override void OnEnter()
        {
            try
            {
                var jar = Jar != null ? Jar.Value : null;
                var ctl = jar != null ? jar.GetComponent<SpawnJarControl>() : null;
                var sr = jar != null ? jar.GetComponent<SpriteRenderer>() : null;
                // Sprite ligado = jarro ja caindo (nao e o recem-criado): nao mexe.
                if (ctl != null && (sr == null || !sr.enabled))
                {
                    if (jar.GetComponent<CollectorJarRestore>() == null) jar.AddComponent<CollectorJarRestore>().Capture(ctl);
                    CollectorFix.Column(jar.transform.position.x, CollectorFix.JarHeight, 1f, out float x, out float y, out float ground);
                    ctl.spawnY = y;
                    ctl.breakY = ground + CollectorFix.JarBreakHeight;
                    jar.transform.position = new Vector3(x, y, jar.transform.position.z);
                    if (logs++ < 5)
                        Modding.Logger.Log($"[CreatureSpawner] The Collector: jarro em ({x:0.##},{y:0.##}), quebra em y={ctl.breakY:0.##}");
                }
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] CollectorJarAction: {ex}");
            }
            Finish();
        }
    }

    /// <summary>Fica no jarro do pool global: devolve spawnY/breakY originais quando ele e reciclado (um Collector de verdade,
    /// na sala dele, pode pegar a mesma instancia depois).</summary>
    internal sealed class CollectorJarRestore : MonoBehaviour
    {
        private SpawnJarControl ctl;
        private float spawnY, breakY;

        public void Capture(SpawnJarControl c)
        {
            ctl = c;
            spawnY = c.spawnY;
            breakY = c.breakY;
        }

        private void OnDisable()
        {
            if (ctl == null) return;
            ctl.spawnY = spawnY;
            ctl.breakY = breakY;
        }
    }
}
