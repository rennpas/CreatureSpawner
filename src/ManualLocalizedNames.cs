using System.Collections.Generic;

namespace CreatureSpawner
{
    // Apelidos de busca em portugues ESCRITOS A MAO, pra criaturas cujo DisplayName no painel nao tem
    // (ou nao bate certinho com) um nome oficial traduzido pelo jogo via Language.Get/JournalConvoKeys -
    // normalmente porque sao entradas ADICIONADAS A MAO em EnemyLocations.cs (ver comentarios "manual
    // addition" la), sem uma entrada propria no bestiario do jogo (ex: "Sisters of Battle" e um apelido da
    // comunidade pra variante Godhome dos Mantis Lords, o jogo nao tem um nome oficial PT-BR so pra ela).
    //
    // IMPORTANTE: toda vez que uma NOVA criatura for adicionada manualmente ao painel (uma entrada que nao
    // vem do scan automatico do EnemyScanTool, ou cujo JournalKey nao resolve num nome em portugues que
    // faca sentido pra ela especificamente), adiciona uma entrada aqui tambem - pedido explicito do
    // usuario. Isso SOBRESCREVE qualquer nome automatico que o JournalKey ja tivesse resolvido (ver
    // CreatureSpawnerMod.TryCacheLocalizedName), entao tambem serve pra corrigir uma traducao automatica
    // que saia errada/enganosa pra alguma criatura especifica.
    internal static class ManualLocalizedNames
    {
        public static readonly Dictionary<string, string> ByDisplayName = new Dictionary<string, string>
        {
            ["Sisters of Battle"] = "Irmãs de Batalha",
            // Sem JournalKey -> sem nome PT automatico. Antes, buscar "radiância" so achava "Radiance" (a da
            // HISTORIA, Dream_Final_Boss, journal FinalBoss = "Radiância"), que NAO tem ArenaProfile: teleporta pras
            // coordenadas absolutas da arena (a coleira puxa de volta -> "teleporta quase pro mesmo lugar"), trava no
            // loop de 'Orb Pos' e nunca recebe PHASE2 RISE. A versao corrigida e a "Absolute Radiance".
            ["Absolute Radiance"] = "Radiância Absoluta",
            // Variantes que antes ficavam escondidas pela colisao de DisplayName (ver os comentarios "Manual rename" em
            // EnemyLocations.cs). O JournalKey delas resolve pro nome da versao BASE (ex. "Receptáculo Quebrado" pro Lost Kin),
            // entao o apelido manual corrige. Textos PT oficiais tirados dos sheets PT_CP3/PT_Titles/PT_Journal do jogo
            // (resources.assets, mesma decriptacao do EnemyScanTool): NAME_HORNET_2 "Hornet Sentinela", NAME_LOST_KIN "Parente
            // Perdido", NAME_MEGA_BEAM_MINER_2 "Guardião Enfurecido", NM_ORO "Mestre do Ferrão" + "Oro", NAME_BIGBUZZER "Rei
            // Vengemosca", NAME_MAGE_LORD "Mestre das Almas", GODHOME_MAIN "Lar dos Deuses".
            ["Vengefly King (Godhome)"] = "Rei Vengemosca (Lar dos Deuses)",
            ["Uumuu (Godhome)"] = "Uumuu (Lar dos Deuses)",
            // (A antiga entrada "Soul Master (Phase 2)" saiu: a fase 2 nao e mais botao do painel - nasce sozinha quando a fase 1 do
            // "Soul Master" morre, ver MageLordPhases.) Soul Tyrant = 'Dream Mage Lord' (DisplayName manual, sem JournalKey):
            // PT_CP3 NAME_SOUL_TYRANT "Tirano das Almas".
            ["Soul Tyrant"] = "Tirano das Almas",
            ["Enraged Guardian"] = "Guardião Enfurecido",
            ["Lost Kin"] = "Parente Perdido",
            ["Oro (Solo)"] = "Mestre do Ferrão Oro (Sozinho)",
            ["Mato (Solo)"] = "Mestre do Ferrão Mato (Sozinho)",
            // A luta em dupla (grupo 'Brothers', ver NailmastersPair). JournalKey NailBros resolve (JournalConvoKeys) pra NAME_HOLLOW_KNIGHT,
            // que nao e o nome deles -> apelido manual.
            ["Nailmasters Oro & Mato"] = "Mestres do Ferrão Oro e Mato",
            ["Hornet Sentinel"] = "Hornet Sentinela",
            // JournalKey HiveKnight resolve (JournalConvoKeys) pra NAME_SPITTER, que nao e o nome dele -> apelido manual.
            ["Hive Knight"] = "Cavaleiro da Colmeia",
        };
    }
}
