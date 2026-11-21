using System;
using UnityEngine;

namespace CreatureSpawner
{
    // Grimm: sem o momento em que vira morcego e da pra bater (relatado). Na luta o 'Control' dele faz: Stun -> 'Explode' (FakeBat.SendAllOut,
    // broadcast 'BATS OUT') -> 'Exploded' (3.25 s, o Grimm some) -> 'Call Bats' ('BATS IN') -> 'Reformed' -> 'Adjust HP': GetHP($Real Bat Obj)
    // -> IntClamp(Bat Damage Cap -50..0) -> soma no HP do Grimm. Quem apanha nesse intervalo e o 'Real Bat' ('Grimm Bats/Real Bat', objeto da
    // SALA: ver RealBatFix.cs), que nao vem no prefab -> nesses 3 s nao havia nada pra bater (var 'Real Bat Obj' zerada pelo SceneLinkSanitizer).
    // Correcao: cada Grimm spawnado ganha o SEU Real Bat (a copia em cache da criatura "Real Bat", com o perfil 'Real Bat (Grimm)': mesma arena,
    // acorda com o BOSS AWAKE do Init do Grimm e espera o BATS OUT de verdade); 'Grimm' do morcego = o Grimm, 'Real Bat Obj' do Grimm = o
    // morcego, e o morcego some junto com o Grimm. Os morcegos falsos (enxame de enfeite) nao vem junto.
    internal static class GrimmBat
    {
        public const string Name = "Grimm";

        public static void Attach(GameObject grimm, SpawnContext ctx)
        {
            try
            {
                var mod = CreatureSpawnerMod.Instance;
                if (mod == null || ctx == null || !mod.CachedPrefabs.TryGetValue(RealBatFix.Name, out var prefab) || prefab == null)
                {
                    Modding.Logger.LogWarn("[CreatureSpawner] Grimm: copia em cache do 'Real Bat' nao encontrada - sem a fase de morcego");
                    return;
                }
                var control = GruzZoteFixes.FindFsm(grimm, "Control", "Explode", "Exploded", "Adjust HP");
                if (control == null) return;

                var bctx = SpawnFixups.Prepare(RealBatFix.GrimmBatName, prefab, ctx.HeroPos, out var pos);
                var bat = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                bat.AddComponent<SpawnLeash>();
                DormantBossFix.Apply(bat);
                SpawnFixups.BeforeActivate(bat, bctx);
                bat.SetActive(true);
                SpawnFixups.AfterActivate(bat, bctx);

                var batFsm = GruzZoteFixes.FindFsm(bat, "Control", "Init", "Dormant", "Position", "End");
                var grimmVar = batFsm != null ? batFsm.FsmVariables.FindFsmGameObject("Grimm") : null;
                if (grimmVar != null) grimmVar.Value = grimm;
                var batVar = control.FsmVariables.FindFsmGameObject("Real Bat Obj");
                if (batVar != null) batVar.Value = bat;
                bat.AddComponent<DestroyWithOwner>().Owner = grimm;
                Modding.Logger.Log($"[CreatureSpawner] Grimm: Real Bat proprio ligado (FSM do morcego={(batFsm != null)}, 'Grimm'={(grimmVar != null)}, 'Real Bat Obj'={(batVar != null)})");
            }
            catch (Exception ex)
            {
                Modding.Logger.LogError($"[CreatureSpawner] GrimmBat: {ex}");
            }
        }
    }
}
