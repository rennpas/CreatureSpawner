using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace CreatureSpawner
{
    // Dung Defender (Waterways_05_boss) ficava "cavando" pra sempre, imortal, com o monte de terra flutuando (relatado, com print).
    // Fonte: scratchpad\FsmDumpTool\out\dung_defender_scene.txt. FSM 'Dung Defender', coordenadas ABSOLUTAS da arena original
    // (pivo dele em pe = 8.97, o Y que o 'Quaked Out' usa; X 61.78..90.57, centro 76.63):
    //  - Sleep -> (ramo Godhome) -> Wake -> 'Underground': SetPosition y = Buried Y (-3), cinematico, SetInvincible TRUE ->
    //    'Tunneling L/R' entre Min X/Max X ('Set Min X'/'Set Max X' fazem SetPosition na borda) -> 'Erupt Out': y = Erupt Y (3.97),
    //    sobe a 40 ate Self Y > Erupt Peak Y (12) -> cai. Fora da arena ele ia pra y=-3 e pro X da sala original (a coleira puxava
    //    de volta) e o Erupt Peak Y nunca era o da sala atual -> enterrado e invencivel pra sempre.
    //  - 'Burrow Effect' (o monte com os chifres): FSM 'Keep Y' sai do pai no 2o frame e guarda o Y de MUNDO que tinha (pai+4.55)
    //    -> flutuava 4.55 acima de onde ele nasceu.
    //  - 'Dolph Dir'/'Repeat?': Centre X, Dolphin Min X/Max X.
    // Correcao (sem ArenaProfile): X deslocado pro heroi do spawn, Y pro chao real embaixo dele (pivo em pe = chao + pes do
    // collider); ele nasce enterrado como na sala (3.9 - 8.97 abaixo do pivo em pe) e o monte fica na altura do chao. Coleira com
    // folga pra baixo (o Buried Y fica 12 abaixo do chao e ela encolheria o X do tunel). Enquanto esta enterrado o renderer dele
    // fica desligado: na sala original o chao esconde, numa sala com espaco embaixo ele apareceria nadando la.
    internal sealed class DungDefenderFix : MonoBehaviour
    {
        public const string Name = "Dung Defender";
        private const float OrigCentreX = 76.63f, OrigStandY = 8.97f, OrigStartY = 3.9f, OrigMoundY = 8.45f, DefaultFeet = 2.6f;
        private static readonly HashSet<string> BuriedStates = new HashSet<string>
        {
            "Underground", "Erupt Tunneling", "Tunneling L", "Tunneling R", "Set Min X", "Set Max X", "Erupt Antic", "Erupt Antic R", "First?"
        };

        private PlayMakerFSM fsm;
        private MeshRenderer mesh;
        private bool hidden;

        public static void Attach(GameObject clone, SpawnContext ctx)
        {
            try
            {
                var fsm = GruzZoteFixes.FindFsm(clone, "Dung Defender", "Underground", "Erupt Out", "Quaked Out", "Tunneling L");
                if (fsm == null || ctx == null) return;
                float groundY = SpawnFixups.FindGroundY(ctx.HeroPos, out bool found, out _);
                float feet = FeetBelowPivot(clone);
                float dx = ctx.HeroPos.x - OrigCentreX, dy = groundY + feet - OrigStandY;

                foreach (var n in new[] { "Centre X", "Min X", "Max X", "Dolphin Min X", "Dolphin Max X" }) AddFloat(fsm, n, dx);
                foreach (var n in new[] { "Buried Y", "Erupt Y", "Erupt Peak Y" }) AddFloat(fsm, n, dy);
                foreach (var a in GruzZoteFixes.GetState(fsm, "Quaked Out").Actions)
                    if (a is SetPosition sp && sp.y != null && !sp.y.IsNone && !sp.y.UsesVariable) sp.y.Value += dy;

                var t = clone.transform;
                t.position = new Vector3(t.position.x, OrigStartY + dy, t.position.z);
                var mound = t.Find("Burrow Effect");
                if (mound != null) mound.position = new Vector3(mound.position.x, OrigMoundY + dy, mound.position.z);

                var leash = clone.GetComponent<SpawnLeash>();
                if (leash != null)
                {
                    leash.SetAnchor(new Vector3(ctx.HeroPos.x, groundY + 3.4f, t.position.z));
                    leash.RadiusYDown = 16f;
                }
                var fix = clone.AddComponent<DungDefenderFix>();
                fix.fsm = fsm;
                fix.mesh = clone.GetComponent<MeshRenderer>();
                Modding.Logger.Log($"[CreatureSpawner] Dung Defender: arena deslocada por ({dx:0.##},{dy:0.##}), chao={(found ? "raycast" : "pes do heroi")} y={groundY:0.##}, " +
                                   $"pes {feet:0.##} abaixo do pivo, monte={(mound != null)}");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] DungDefenderFix: {ex}");
            }
        }

        private void Update()
        {
            if (fsm == null || mesh == null) return;
            bool buried = BuriedStates.Contains(fsm.ActiveStateName);
            if (buried && mesh.enabled)
            {
                mesh.enabled = false;
                hidden = true;
            }
            else if (!buried && hidden)
            {
                mesh.enabled = true;
                hidden = false;
            }
        }

        private static float FeetBelowPivot(GameObject go)
        {
            float sy = Mathf.Abs(go.transform.lossyScale.y);
            var col = go.GetComponent<Collider2D>();
            float f = DefaultFeet;
            if (col is BoxCollider2D b) f = -(b.offset.y - b.size.y * 0.5f) * sy;
            else if (col is CircleCollider2D c) f = -(c.offset.y - c.radius) * sy;
            else if (col is CapsuleCollider2D k) f = -(k.offset.y - k.size.y * 0.5f) * sy;
            return f > 0.5f && f < 6f ? f : DefaultFeet;
        }

        private static void AddFloat(PlayMakerFSM fsm, string name, float add)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value += add;
            else Modding.Logger.LogWarn($"[CreatureSpawner] Dung Defender: FSM '{fsm.FsmName}' sem a var '{name}'");
        }
    }
}
