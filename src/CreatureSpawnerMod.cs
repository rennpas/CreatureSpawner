using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HKEnemySpawner.Data;
using Modding;
using UnityEngine;

namespace CreatureSpawner
{
    // ITogglableMod: sem isso o mod fica carregado e funcionando mas NAO aparece na tela
    // "Mods" do menu principal (so mods ITogglableMod ganham a linha com liga/desliga la -
    // confirmado comparando com ChildOfLight, que tambem implementa essa interface).
    // IMenuMod: adiciona a aba "Creature Spawner Opcoes" com a escolha da tecla do painel.
    public class CreatureSpawnerMod : Mod, ITogglableMod, IMenuMod
    {
        // Nome bonito pro mod aparecer certo na lista de mods do jogo (Mods, no menu
        // principal) - sem isso o Modding API usa o nome da classe (CreatureSpawnerMod).
        public CreatureSpawnerMod() : base("Creature Spawner") { Instance = this; }

        // Acesso ao cache do painel fora do painel (ex. GodhomeUumuuOomas invoca o mesmo "Ooma" do botao do painel).
        internal static CreatureSpawnerMod Instance { get; private set; }

        private GameObject panelHost;

        // Indice em HotkeyOptions.All da tecla que abre/fecha o painel (ver CreatureSpawnerPanel.Update).
        internal int HotkeyIndex = 0; // padrao: Ctrl

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "..", "AppData", "LocalLow", "Team Cherry", "Hollow Knight", "CreatureSpawner_settings.txt");

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    if (parts[0].Trim() == "HotkeyLabel")
                    {
                        HotkeyIndex = HotkeyOptions.IndexOfLabel(parts[1].Trim());
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(SettingsPath, new[] { "HotkeyLabel=" + HotkeyOptions.All[HotkeyIndex].Label });
            }
            catch { }
        }

        public bool ToggleButtonInsideMenu => false;

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry
                {
                    Name = "Tecla do Menu",
                    Description = "Tecla que abre/fecha o painel de spawnar criaturas",
                    Values = HotkeyOptions.Labels(),
                    Saver = (index) => { HotkeyIndex = index; SaveSettings(); },
                    Loader = () => HotkeyIndex
                }
            };
        }

        // Nome pra mostrar no painel -> clone ja pronto (inativo) pra instanciar.
        // Chave e o DisplayName (nome em ingles do bestiario) quando existe, senao
        // o nome interno do objeto na cena.
        internal readonly Dictionary<string, GameObject> CachedPrefabs = new Dictionary<string, GameObject>();

        // DisplayName (ingles, o que aparece no painel) -> nome traduzido pro idioma ATUAL do jogo (ex:
        // portugues, se for o idioma selecionado). So usado pra BUSCA no painel - o botao continua mostrando
        // o nome em ingles, mas digitar o nome em outro idioma tambem encontra a criatura. Ver JournalConvoKeys.
        internal readonly Dictionary<string, string> LocalizedNames = new Dictionary<string, string>();

        public override List<(string, string)> GetPreloadNames()
        {
            // Carrega TODAS as 206 criaturas conhecidas (pedido explicito do
            // usuario - o painel precisa ter tudo disponivel desde o inicio, sem
            // depender de ter encontrado a criatura jogando antes). Isso deixa o
            // carregamento inicial do jogo mais lento (~148 cenas sendo abertas em
            // segundo plano), mas so acontece uma vez por sessao de jogo.
            var list = EnemyLocations.GetPreloadNames();
            list.AddRange(EncounterGroups.ExtraPreloads()); // partes de encontros (ex. Mantis Lords S1+S2)
            list.AddRange(VariantEncounters.ExtraPreloads()); // idem (lasers do Enraged Guardian, multizaps dos Uumuus, farpas da Hornet Sentinel)
            list.AddRange(GruzZoteFixes.ExtraPreloads()); // moscas da Gruz Mother ('_Enemies/Fly Spawn' do Crossroads_04)
            list.AddRange(HiveKnightAttacks.ExtraPreloads()); // bolas de espinhos, abelhas e som do enxame do Hive Knight (Hive_05)
            list.AddRange(FalseKnightFix.ExtraPreloads()); // summoner dos barris do teto do False Knight (Crossroads_10_boss)
            list.AddRange(GrimmSpikes.ExtraPreloads()); // fileira de espinhos do chao do Grimm (Grimm_Main_Tent_boss)
            list.AddRange(BrokenVesselFix.ExtraPreloads()); // 'Scuttler Spawner' da intro do Broken Vessel (Abyss_19)
            return list;
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            LoadSettings();

            // ITogglableMod: ao religar depois de desligado, o Modding API chama Initialize de
            // novo mas com preloadedObjects=null (nao pre-carrega tudo outra vez). Nesse caso so
            // recria o painel - os clones em CachedPrefabs de antes continuam validos (DontDestroyOnLoad).
            if (preloadedObjects != null)
            {
                int ok = 0, fail = 0;
                try
                {
                    // Nomes dos assets como o Modding API registra no bundle (modo RepackAssets): "<cena>/<caminho>.prefab"
                    Log("SceneLinkSanitizer: " + SceneLinkSanitizer.BuildBundleIndex(
                        EnemyLocations.All.Values.Select(l => l.Scene + "/" + l.Path + ".prefab")));
                }
                catch (Exception ex)
                {
                    LogError($"SceneLinkSanitizer: falha ao indexar o bundle de preload: {ex}");
                }
                foreach (var kv in EnemyLocations.All)
                {
                    string key = kv.Key;
                    var loc = kv.Value;
                    string displayName = !string.IsNullOrEmpty(loc.DisplayName) ? loc.DisplayName : key;
                    try
                    {
                        if (preloadedObjects.TryGetValue(loc.Scene, out var sceneObjs) &&
                            sceneObjs.TryGetValue(loc.Path, out var original) && original != null)
                        {
                            var clone = UnityEngine.Object.Instantiate(original);
                            UnityEngine.Object.DontDestroyOnLoad(clone);
                            clone.SetActive(false);
                            // Zera referencias pra objetos da sala original (fora da
                            // hierarquia do inimigo) - ver SceneLinkSanitizer. Feito no
                            // clone em cache, entao todo spawn copiado dele ja nasce limpo.
                            var cleared = SceneLinkSanitizer.Sanitize(clone);
                            if (cleared.Count > 0)
                            {
                                Log($"'{displayName}': {cleared.Count} referencia(s) externa(s) zerada(s): {string.Join(", ", cleared.ToArray())}");
                            }
                            // Fases 2 do Soul Master / Soul Tyrant: pre-carregadas, mas fora do painel - so nascem
                            // quando a fase 1 morre (ver MageLordPhases).
                            if (MageLordPhases.CachePhase2(key, clone))
                            {
                                ok++;
                                continue;
                            }
                            // Evita sobrescrever se dois objetos diferentes acabarem
                            // mapeando pro mesmo nome de exibicao (ex: variantes)
                            if (!CachedPrefabs.ContainsKey(displayName))
                            {
                                CachedPrefabs[displayName] = clone;
                                TryCacheLocalizedName(displayName, loc.JournalKey);
                            }
                            ok++;
                        }
                        else
                        {
                            fail++;
                        }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        LogError($"Erro ao pre-carregar '{key}': {ex}");
                    }
                }
                Log($"CreatureSpawner: {ok} criaturas carregadas, {fail} falharam");
                EncounterGroups.CacheExtras(preloadedObjects, s => Log(s));
                // Luta em dupla dos Nailmasters: o botao spawna o grupo 'Brothers' inteiro (Oro + Mato + 'Combo Control') - ver
                // NailmastersPair. (Os solos "Oro (Solo)"/"Mato (Solo)" continuam vindo do EnemyLocations, cada um o seu prefab.)
                if (EncounterGroups.NailBrosPrefab != null && !CachedPrefabs.ContainsKey(NailmastersPair.Name))
                    CachedPrefabs[NailmastersPair.Name] = EncounterGroups.NailBrosPrefab;
                VariantEncounters.CacheExtras(preloadedObjects, s => Log(s));
                GruzZoteFixes.CacheExtras(preloadedObjects, s => Log(s));
                HiveKnightAttacks.CacheExtras(preloadedObjects, s => Log(s));
                FalseKnightFix.CacheExtras(preloadedObjects, s => Log(s));
                GrimmSpikes.CacheExtras(preloadedObjects, s => Log(s));
                BrokenVesselFix.CacheExtras(preloadedObjects, s => Log(s));

                // Apelidos de busca em portugues escritos a mao (ver ManualLocalizedNames) - sobrescrevem
                // qualquer nome automatico resolvido acima pra essas entradas especificas.
                foreach (var kv2 in ManualLocalizedNames.ByDisplayName)
                {
                    LocalizedNames[kv2.Key] = kv2.Value;
                }
            }

            if (panelHost == null)
            {
                panelHost = new GameObject("CreatureSpawnerPanelHost");
                UnityEngine.Object.DontDestroyOnLoad(panelHost);
                var panel = panelHost.AddComponent<CreatureSpawnerPanel>();
                panel.Mod = this;
            }
        }

        // ITogglableMod.Unload: chamado quando o jogador desliga o mod pela tela "Mods". So
        // precisa fechar o painel - os clones em cache ficam guardados (DontDestroyOnLoad) pra
        // religar na hora, sem precisar pre-carregar tudo de novo.
        public void Unload()
        {
            if (panelHost != null)
            {
                UnityEngine.Object.Destroy(panelHost);
                panelHost = null;
            }
        }

        // Resolve "NAME_<convoName>" (sheet "Journal") no idioma atual do jogo via JournalKey (= playerDataName,
        // ja temos esse valor pronto em EnemyLocation). Falha silenciosa: se nao achar a chave convo, ou se
        // Language.Get devolver o marcador de erro "#!#chave#!#", so nao entra no dicionario - a busca continua
        // funcionando normal em ingles.
        private void TryCacheLocalizedName(string displayName, string journalKey)
        {
            try
            {
                if (string.IsNullOrEmpty(journalKey)) return;
                if (!JournalConvoKeys.ByJournalKey.TryGetValue(journalKey, out var nameConvo)) return;
                string localized = global::Language.Language.Get(nameConvo, "Journal");
                if (string.IsNullOrEmpty(localized) || localized.StartsWith("#!#")) return;
                LocalizedNames[displayName] = localized;
            }
            catch (Exception ex)
            {
                LogError($"TryCacheLocalizedName('{displayName}'): {ex}");
            }
        }

        public override string GetVersion() => "1.0.0";
    }
}
