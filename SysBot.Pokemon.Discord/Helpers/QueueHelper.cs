using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using SysBot.Pokemon.Discord.Commands.Bots;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;

    // A dictionary to hold batch trade file paths and their deletion status
    private static readonly Dictionary<int, List<string>> batchTradeFiles = new Dictionary<int, List<string>>();
    private static readonly Dictionary<ulong, int> userBatchTradeMaxDetailId = [];

    private static DiscordColor embedColor = DiscordColor.Gold;
    private static GameVersion gameVersion;


    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
            return;
        }

        try
        {
            string imageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dm-tradecode.gif";
            EmbedBuilder embedBuilder = new EmbedBuilder()
                .WithTitle("Here's your Trade Code!")
                .WithDescription($"# **{code:0000 0000}**\n*I'll notify you when your trade starts*")
                .WithColor(embedColor)
                .WithThumbnailUrl(imageUrl);
            Embed embed = embedBuilder.Build(); // Build the Embed object

            // Assuming Trader is a DiscordSocketClient object

            if (!isBatchTrade || batchTradeNumber == 1)
            {
                if (trade is PB7 && lgcode != null)
                {
                    var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(lgcode);
                    await trader.SendFileAsync(thefile, null, embed: lgcodeembed).ConfigureAwait(false);
                }
                else
                {
                    await trader.SendMessageAsync(embed: embed).ConfigureAwait(false); // Use the built Embed object
                }
            }

            var result = await AddToTradeQueue(context, trade, code, trainer, sig, routine, isBatchTrade ? PokeTradeType.Batch : type, trader, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg, lgcode, ignoreAutoOT, setEdited).ConfigureAwait(false);

        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, bool ignoreAutoOT = false)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User, ignoreAutoOT: ignoreAutoOT);
    }

    private static string GetLanguageText(int languageValue)
    {
        switch (languageValue)
        {
            case 1:
                return "Japanese";
            case 2:
                return "English";
            case 3:
                return "French";
            case 4:
                return "Italian";
            case 5:
                return "German";
            case 7:
                return "Spanish";
            case 8:
                return "Korean";
            case 9:
                return "ChineseS";
            case 10:
                return "ChineseT";
            default:
                return "Unknown";
        }
    }

    private static async Task<TradeQueueResult> AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, bool isBatchTrade, int batchTradeNumber, int totalBatchTrades, bool isHiddenTrade, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, string[] markTitles = null)
    {

        var user = trader;
        var userID = user.Id;
        var name = user.Username;
        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, trader, batchTradeNumber, totalBatchTrades, isMysteryEgg, lgcode);
        var uniqueTradeID = GenerateUniqueTradeID();
        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored, lgcode, batchTradeNumber, totalBatchTrades, isMysteryEgg, uniqueTradeID, ignoreAutoOT, setEdited);
        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, name, uniqueTradeID);
        var strings = GameInfo.GetStrings(1);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var canAddMultiple = isBatchTrade || sig == RequestSignificance.None;
        var added = Info.AddToTradeQueue(trade, userID, canAddMultiple);
        bool useTypeEmojis = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.MoveTypeEmojis;
        bool useGenderIcons = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.GenderEmojis;
        bool showScale = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowScale;
        bool showTeraType = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTeraType;
        bool useTeraTypeEmoji = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTeraTypeEmoji;
        bool showLevel = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowLevel;
        bool showAbility = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowAbility;
        bool showNature = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowNature;
        bool showIVs = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowIVs;
        bool showEVs = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowEVs;
        bool showAlphaMark = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.AlphaMarkEmoji;
        bool showMightiestMark = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.MightiestMarkEmoji;
        bool showMysteryGift = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.MysteryGiftEmoji;
        bool showOT = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowOT;
        bool showTID = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowTID;
        bool showSID = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowSID;
        bool showMetLevel = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowMetLevel;
        bool showFatefulEncounter = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowFatefulEncounter;
        bool showWasEgg = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowWasEgg;
        bool showMetDate = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowMetDate;
        bool showLanguage = SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.ShowLanguage;
        string markTitle = string.Empty;
        var tradeCodeStorage = new TradeCodeStorage();
        int totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
        var tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        string otText = tradeDetails?.OT ?? "";
        string tidText = tradeDetails?.TID != null ? $"TID: {tradeDetails.TID}" : "";
        string sidText = tradeDetails?.SID != null ? $"SID: {tradeDetails.SID}" : "";
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            return new TradeQueueResult(false);
        }
    
        if (pk is IRibbonIndex ribbonIndex)
        {
            AbstractTrade<T>.HasMark(ribbonIndex, out RibbonIndex result, out markTitle);
        }

var typeEmojis = new Dictionary<MoveType, string>
        {
            [MoveType.Bug] = "<:bug_type:1218977628246245427>",
            [MoveType.Fire] = "<:fire_type:1218977624093626399>",
            [MoveType.Flying] = "<:flying_type:1218977867602596013>",
            [MoveType.Ground] = "<:ground_type:1231420390048272385>",
            [MoveType.Water] = "<:water_type:1218977621178585230>",
            [MoveType.Grass] = "<:grass_type:1218977614081949747>",
            [MoveType.Ice] = "<:ice_type:1218977634885570560>",
            [MoveType.Rock] = "<:rock_type:1218977617835851846>",
            [MoveType.Ghost] = "<:ghost_type:1218977631291314298>",
            [MoveType.Steel] = "<:steel_type:1218977869825572896>",
            [MoveType.Fighting] = "<:fighting_type:1218977613410996315>",
            [MoveType.Electric] = "<:electric_type:1218977615239581936>",
            [MoveType.Dragon] = "<:dragon_type:1218977868688916554>",
            [MoveType.Psychic] = "<:psychic_type:1218977870639272106>",
            [MoveType.Dark] = "<:dark_type:1218977616644538478>",
            [MoveType.Normal] = "<:normal_type:1218977610051092540>",
            [MoveType.Poison] = "<:poison_type:1218977611275833445>",
            [MoveType.Fairy] = "<:fairy_type:1218977872488693880>",
        };

        var teraTypeEmojis = new Dictionary<string, string>
        {
            ["Bug"] = "<:bug_tera:1231413366510190623>",
            ["Fire"] = "<:fire_tera:1231413423703592980>",
            ["Dark"] = "<:dark_tera:1231413367894446120>",
            ["Dragon"] = "<:dragon_tera:1231413373661483108>",
            ["Electric"] = "<:electric_tera:1231413396851789875>",
            ["Fairy"] = "<:fairy_tera:1231413397992636507>",
            ["Fighting"] = "<:fighting_tera:1231413399259189308>",
            ["Flying"] = "<:flying_tera:1231413424731324457>",
            ["Ghost"] = "<:ghost_tera:1231413425880436846>",
            ["Ground"] = "<:ground_tera:1231413459371950131>",
            ["Normal"] = "<:normal_tera:1231413494310637670>",
            ["Psychic"] = "<:psychic_tera:1231413497162891265>",
            ["Steel"] = "<:steel_tera:1231413520613249064>",
            ["Water"] = "<:water_tera:1231413521905094686>",
            ["Grass"] = "<:grass_tera:1231413457732239401>",
            ["Ice"] = "<:ice_tera:1231413460487901224>",
            ["Poison"] = "<:poison_tera:1231413495153561651>",
            ["Rock"] = "<:rock_tera:1231413518960431166>",
            ["Stellar"] = "<:stellar_tera:1227229314660044850>",

        };

        // Basic Pokémon details
        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE];
        ushort[] moves = new ushort[4];
        pk.GetMoves(moves.AsSpan());
        int[] movePPs = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
        List<string> moveNames = [""];
        for (int i = 0; i < moves.Length; i++)
        {
            if (moves[i] == 0) continue; // Skip if no move is assigned
            string moveName = GameInfo.MoveDataSource.FirstOrDefault(m => m.Value == moves[i])?.Text ?? "";
            byte moveTypeId = MoveInfo.GetType(moves[i], default);
            MoveType moveType = (MoveType)moveTypeId;
            string formattedMove = $"*{moveName}* ({movePPs[i]} PP)";
            if (useTypeEmojis)
            {
                string typeEmoji = typeEmojis.TryGetValue(moveType, out var moveEmoji) ? moveEmoji : string.Empty;
                formattedMove = $"{typeEmoji} {formattedMove}";
            }
            moveNames.Add($"\u200B {formattedMove}");
        }
        int level = pk.CurrentLevel;
        string originalTrainerName = pk.OriginalTrainerName;
        uint tidDisplay = pk.TrainerTID7;
        uint sidDisplay = pk.TrainerSID7;
        byte metLevelDisplay = pk.MetLevel;
        Boolean fatefulEncounterDisplay = pk.FatefulEncounter;
        Boolean wasEggDisplay = pk.WasEgg;

        // Pokémon appearance and type details
        string teraTypeString = "", scaleText = "", abilityName, natureName, speciesName, formName, speciesAndForm, heldItemName, ballName, formDecoration = "";
        byte scaleNumber = 0;
        if (pk is PK9 pk9)
        {
            teraTypeString = GetTeraTypeString(pk9);
            scaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pk9.Scale)}";
            scaleNumber = pk9.Scale;
        }

        // Pokémon identity and special attributes
        abilityName = GameInfo.AbilityDataSource.FirstOrDefault(a => a.Value == pk.Ability)?.Text ?? "";
        natureName = GameInfo.NatureDataSource.FirstOrDefault(n => n.Value == (int)pk.Nature)?.Text ?? "";
        speciesName = GameInfo.GetStrings(1).Species[pk.Species];
        string alphaMarkSymbol = pk is IRibbonSetMark9 && (pk as IRibbonSetMark9).RibbonMarkAlpha && showAlphaMark ? "<:alpha_mark:1218977873805967533> " : string.Empty;
        string mightyMarkSymbol = pk is IRibbonSetMark9 && (pk as IRibbonSetMark9).RibbonMarkMightiest && showMightiestMark ? "<:MightiestMark:1218977612580261908> " : string.Empty;        
        string alphaSymbol = pk is IAlpha alpha && alpha.IsAlpha ? "<:alpha:1218977646269038672> " : string.Empty;
        string shinySymbol = pk.ShinyXor == 0 ? "◼ " : pk.IsShiny ? "★ " : string.Empty;
        string genderSymbol = GameInfo.GenderSymbolASCII[pk.Gender];
        string mysteryGiftEmoji = pk.FatefulEncounter && showMysteryGift ? "<:Mystery_Gift_mark:1218977638509576232> " : "";
        string displayGender = (genderSymbol == "M" ? (useGenderIcons ? "<:male_mark:1218977652610957443>" : "(M)") :
               genderSymbol == "F" ? (useGenderIcons ? "<:female_mark:1218977655010099221>" : "(F)") : "") +
               alphaSymbol + mightyMarkSymbol + alphaMarkSymbol + mysteryGiftEmoji;
        formName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
        string toppingName = "";
        if (pk.Species == (int)Species.Alcremie && pk is IFormArgument formArgument)
        {
            AlcremieDecoration topping = (AlcremieDecoration)formArgument.FormArgument;
            toppingName = $"-{topping}";
            formName += toppingName;
        }
        speciesAndForm = $"**{shinySymbol}{speciesName}{(string.IsNullOrEmpty(formName) ? "" : $"-{formName}")}{(!string.IsNullOrEmpty(markTitle) ? markTitle : "")} {displayGender}**";
        heldItemName = strings.itemlist[pk.HeldItem];
        ballName = strings.balllist[pk.Ball];


        // Request type flags
        bool isCloneRequest = type == PokeRoutineType.Clone;
        bool isDumpRequest = type == PokeRoutineType.Dump;
        bool FixOT = type == PokeRoutineType.FixOT;
        bool isSpecialRequest = type == PokeRoutineType.SeedCheck;

        // Display elements
        string ivsDisplay = ivs.All(iv => iv == 31) ? "6IV" : $"" +
            $"{ivs[0]}/{ivs[1]}/{ivs[2]}/{ivs[3]}/{ivs[4]}/{ivs[5]}";
        string evsDisplay = string.Join(" / ", new[]{
               (evs[0] != 0 ? $"{evs[0]} HP" : ""),
               (evs[1] != 0 ? $"{evs[1]} Atk" : ""),
               (evs[2] != 0 ? $"{evs[2]} Def" : ""),
               (evs[3] != 0 ? $"{evs[3]} SpA" : ""),
               (evs[4] != 0 ? $"{evs[4]} SpD" : ""),
               (evs[5] != 0 ? $"{evs[5]} Spe" : "")
               }.Where(s => !string.IsNullOrEmpty(s)));
        string MetDate = $"{pk.MetDate}";
        string movesDisplay = string.Join("\n", moveNames);
        int languageName = pk.Language;
        string languageText = GetLanguageText(languageName);
        string shinyEmoji = pk.IsShiny ? "✨ " : "";
        string pokemonDisplayName = pk.IsNicknamed ? pk.Nickname : GameInfo.GetStrings(1).Species[pk.Species];

        // Queue position and ETA calculation
        var position = Info.CheckPosition(userID, uniqueTradeID, type);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
        var adjustedEta = baseEta + (batchTradeNumber - 1); // Increment ETA by 1 minute for each batch trade
        var etaMessage = $"Estimated Wait Time: {baseEta:F1} min(s)\nCurrent Batch Trade: {batchTradeNumber} of {totalBatchTrades}";

        // Determining trade title based on trade type
        string tradeTitle;
        tradeTitle = isMysteryEgg ? "✨ Shiny Mystery Egg ✨" :
                     isBatchTrade ? $"Batch Trade #{batchTradeNumber} - {shinyEmoji}{pokemonDisplayName}" :
                     FixOT ? "FixOT Request" :
                     isSpecialRequest ? "Special Request" :
                     isCloneRequest ? "Clone Request" :
                     isDumpRequest ? "Dump Request" :
                     "";

        // Prepare embed details for Discord message
        (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);

        // Adjust image URL based on request type
        if (isMysteryEgg)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg1.png?raw=true&width=200&height=200";
        }
        else if (isDumpRequest)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/dumpball.png?raw=true&width=200&height=200";
        }
        else if (isCloneRequest)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/cloneball.png?raw=true&width=200&height=200";
        }
        else if (isSpecialRequest)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/seedball.png?raw=true&width=200&height=200";
        }
        else if (FixOT)
        {
            embedImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/fixotball.png?raw=true&width=200&height=200";
        }


        // Prepare held item image URL if available
        string heldItemUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(heldItemName))
        {
            heldItemName = heldItemName.ToLower().Replace(" ", "");
            heldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
        }

        // Checking if the image URL points to a local file
        bool isLocalFile = File.Exists(embedImageUrl);
        string userName = user.Username;
        string isPkmShiny = pk.IsShiny ? "Shiny " : "";

        // Building the embed author name based on the type of trade
        string authorName = isMysteryEgg || FixOT || isCloneRequest || isDumpRequest || isSpecialRequest || isBatchTrade ?
                            $"{userName}'s {tradeTitle}" :
                            $"{userName}'s {isPkmShiny}{pokemonDisplayName}";

        // Build footer
        string footerText = $"Current Queue Position: {position.Position}";

        TradeCodeStorage.TradeCodeDetails userDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        string userDetailsText = $"User's Total Trades: {totalTradeCount}";

        footerText += $"\n{userDetailsText}\n{etaMessage}";


        // Initializing the embed builder with general settings
        var embedBuilder = new EmbedBuilder()
            .WithColor(embedColor)
            .WithImageUrl(isLocalFile ? $"attachment://{Path.GetFileName(embedImageUrl)}" : embedImageUrl)
            .WithFooter(footerText)
            .WithAuthor(new EmbedAuthorBuilder()
                .WithName(authorName)
                .WithIconUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithUrl("https://play.pokemonshowdown.com/teambuilder"));

        // Adding additional text to the embed, if any
        string additionalText = string.Join("\n", SysCordSettings.Settings.AdditionalEmbedText);
        if (!string.IsNullOrEmpty(additionalText))
        {
            embedBuilder.AddField("\u200B", additionalText, inline: false);
        }

        // Determine if any EVs are greater than 0
        bool hasEVs = evs.Any(ev => ev > 0);
        // Constructing the content of the embed based on the trade type
        if (!isMysteryEgg && !isCloneRequest && !isDumpRequest && !FixOT && !isSpecialRequest)
        {

            // Preparing content for normal trades
            string leftSideContent = $"**User:** {user.Mention}\n";

            // Add OT, TID, and SID only if they are available
            // Also removes SID from displaying in SWSH only
            if (gameVersion == GameVersion.SWSH)
            {
                if (!string.IsNullOrEmpty(userDetails?.OT))
                    leftSideContent += $"**OT:** {userDetails.OT}\n";

                if (userDetails.TID != 0)
                    leftSideContent += $"**TID:** {userDetails.TID:D6}\n";
            }
            else
            {
                if (!string.IsNullOrEmpty(userDetails?.OT))
                    leftSideContent += $"**OT:** {userDetails.OT}\n";

                if (userDetails.TID != 0)
                    leftSideContent += $"**TID:** {userDetails.TID:D6}\n";

                if (userDetails.SID != 0)
                    leftSideContent += $"**SID:** {userDetails.SID:D4}\n";
            }

            string teraTypeEmoji = teraTypeEmojis.ContainsKey(teraTypeString) ? teraTypeEmojis[teraTypeString] : "";
            string teraTypeWithEmoji = $"{teraTypeEmoji} {teraTypeString}";


            leftSideContent +=
                (showLevel ? $"**Level:** {level}\n" : "") +
                (showMetLevel ? $"**Met Level:** {metLevelDisplay}\n" : "") +
                (showMetDate ? $"**Met Date:** {MetDate}\n" : "") +
                (showAbility ? $"**Ability:** {abilityName}\n" : "") +
                (showNature ? $"**Nature**: {natureName}\n" : "") +
                (showLanguage ? $"**Language:** {languageText}\n" : "") +
                (showIVs ? $"**IVs**: {ivsDisplay}\n" : "") +
                (hasEVs ? $"**EVs**: {evsDisplay}\n" : "") +
                (pk.Version is GameVersion.SL or GameVersion.VL && showTeraType ? $"**Tera Type:** {teraTypeEmoji}\n" : "") +
                (pk.Version is GameVersion.SL or GameVersion.VL && showScale ? $"**Scale:** {scaleText} ({scaleNumber})\n" : "") +
                (showFatefulEncounter ? $"**Encounter:** {fatefulEncounterDisplay}\n" : "") +
                (showWasEgg && wasEggDisplay ? "**Encounter:** Egg\n" : "");


            leftSideContent = leftSideContent.TrimEnd('\n');
            if (!string.IsNullOrEmpty(leftSideContent))
            {
                embedBuilder.AddField($"{speciesAndForm}", leftSideContent, inline: true);
            }
            embedBuilder.AddField("\u200B", "\u200B", inline: true); // Spacer
            embedBuilder.AddField("**__MOVES__**", movesDisplay, inline: true);
        }
        else
        {
            // Preparing content for special types of trades
            string specialDescription = $"**User:** {user.Mention}\n" +
                                        (isMysteryEgg ? "Shiny 6IV Mystery Egg Coming Right Up!" : isSpecialRequest ? "Special Request Module Initiated..." : isCloneRequest ? "Cloning has Begun..." : FixOT ? "Fixing OT!" : "Dumping Pokemon Files...");
            embedBuilder.AddField("\u200B", specialDescription, inline: false);
        }

        // Adding thumbnails for clone and special requests, or held items
        if (isCloneRequest || isSpecialRequest)
        {

        }
        else if (!string.IsNullOrEmpty(heldItemUrl))
        {
            embedBuilder.WithThumbnailUrl(heldItemUrl);
        }

        if (setEdited && Info.Hub.Config.Trade.AutoCorrectConfig.AutoCorrectEmbedIndicator)
        {
            embedBuilder.AddField("Genned Closest Legal Set", "Enjoy your new Pokémon!");
        }

        if (!isHiddenTrade)
        {
            try
            {
                if (!isHiddenTrade && SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
                {
                    var embed = embedBuilder.Build();
                    if (embed == null)
                    {
                        Console.WriteLine("Error: Embed is null.");
                        await context.Channel.SendMessageAsync("An error occurred while preparing the trade details.");
                        return new TradeQueueResult(false);
                    }

                    if (isLocalFile)
                    {
                        await context.Channel.SendFileAsync(embedImageUrl, embed: embed);
                        if (isBatchTrade)
                        {
                            userBatchTradeMaxDetailId[userID] = Math.Max(userBatchTradeMaxDetailId.GetValueOrDefault(userID), detail.ID);
                            await ScheduleFileDeletion(embedImageUrl, 0, detail.ID);
                            if (detail.ID == userBatchTradeMaxDetailId[userID] && batchTradeNumber == totalBatchTrades)
                            {
                                DeleteBatchTradeFiles(detail.ID);
                            }
                        }
                        else
                        {
                            await ScheduleFileDeletion(embedImageUrl, 0);
                        }
                    }
                    else
                    {
                        await context.Channel.SendMessageAsync(embed: embed);
                    }
                }
                else
                {
                    var message = $"▹𝗦𝗨𝗖𝗖𝗘𝗦𝗦𝗙𝗨𝗟𝗟𝗬 𝗔𝗗𝗗𝗘𝗗◃\n" +
                                 $"//【𝐔𝐒𝐄𝐑: {trader.Mention}】\n" +
                                 $"//【𝐐𝐔𝐄𝐔𝐄: LinkTrade】\n" +
                                 $"//【𝐏𝐎𝐒𝐈𝐓𝐈𝐎𝐍: {position.Position}】\n";

                    if (speciesName != "---")
                    {
                        message += $"//【𝐏𝐎𝐊𝐄𝐌𝐎𝐍: {speciesName}】\n";
                    }

                    message += $"//【𝐄𝐓𝐀: {baseEta:F1}】";
                    await context.Channel.SendMessageAsync(message);
                }
            }
            catch (HttpException ex)
            {
                await HandleDiscordExceptionAsync(context, trader, ex);
                return new TradeQueueResult(false);
            }

            return new TradeQueueResult(true);
        }
        else
        {
            var message = $"▹𝗦𝗨𝗖𝗖𝗘𝗦𝗦𝗙𝗨𝗟𝗟𝗬 𝗔𝗗𝗗𝗘𝗗◃\n" +
                                 $"//【𝐔𝐒𝐄𝐑: {trader.Mention}】\n" +
                                 $"//【𝐐𝐔𝐄𝐔𝐄: Hidden Trade】\n" +
                                 $"//【𝐏𝐎𝐒𝐈𝐓𝐈𝐎𝐍: {position.Position}】\n";

            if (speciesName != "---")
            {
                message += $"//【𝐏𝐎𝐊𝐄𝐌𝐎𝐍: {speciesName}】\n";
            }

            message += $"//【𝐄𝐓𝐀: {baseEta:F1}】";

            await context.Channel.SendMessageAsync(message);

            return new TradeQueueResult(true);
        }

        static int GenerateUniqueTradeID()
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int randomValue = new Random().Next(1000);
            int uniqueTradeID = (int)(timestamp % int.MaxValue) * 1000 + randomValue;
            return uniqueTradeID;
        }


        static string GetTeraTypeString(PK9 pk9)
        {
            if (pk9.TeraTypeOverride == (MoveType)TeraTypeUtil.Stellar)
            {
                return "Stellar";
            }
            else if ((int)pk9.TeraType == 99) // Terapagos
            {
                return "Stellar";
            }
            // Fallback to default TeraType string representation if not Stellar
            else
            {
                return pk9.TeraType.ToString();
            }
        }


        static string GetImageFolderPath()
        {
            // Get the base directory where the executable is located
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // Define the path for the images subfolder
            string imagesFolder = Path.Combine(baseDirectory, "Images");

            // Check if the folder exists, if not, create it
            if (!Directory.Exists(imagesFolder))
            {
                Directory.CreateDirectory(imagesFolder);
            }

            return imagesFolder;
        }

        static string SaveImageLocally(System.Drawing.Image image)
        {
            // Get the path to the images folder
            string imagesFolderPath = GetImageFolderPath();

            // Create a unique filename for the image
            string filePath = Path.Combine(imagesFolderPath, $"image_{Guid.NewGuid()}.png");

            // Save the image to the specified path
            image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            return filePath;
        }

        static async Task<(string, DiscordColor)> PrepareEmbedDetails(T pk)
        {
            string embedImageUrl;
            string speciesImageUrl;

            if (pk.IsEgg)
            {
                string eggImageUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/egg2.png";
                speciesImageUrl = AbstractTrade<T>.PokeImg(pk, false, true);
                System.Drawing.Image combinedImage = await OverlaySpeciesOnEgg(eggImageUrl, speciesImageUrl);
                embedImageUrl = SaveImageLocally(combinedImage);
            }
            else
            {
                bool canGmax = pk is PK8 pk8 && pk8.CanGigantamax;
                speciesImageUrl = AbstractTrade<T>.PokeImg(pk, canGmax, false);
                embedImageUrl = speciesImageUrl;
            }

            // Determine ball image URL
            var strings = GameInfo.GetStrings(1);
            string ballName = strings.balllist[pk.Ball];

            // Check for "(LA)" in the ball name
            if (ballName.Contains("(LA)"))
            {
                ballName = "la" + ballName.Replace(" ", "").Replace("(LA)", "").ToLower();
            }
            else
            {
                ballName = ballName.Replace(" ", "").ToLower();
            }

            string ballImgUrl = $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/AltBallImg/28x28/{ballName}.png";

            // Check if embedImageUrl is a local file or a web URL
            if (Uri.TryCreate(embedImageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
            {
                // Load local image directly
                using (var localImage = System.Drawing.Image.FromFile(uri.LocalPath))
                using (var ballImage = await LoadImageFromUrl(ballImgUrl))
                {
                    if (ballImage != null)
                    {
                        using (var graphics = Graphics.FromImage(localImage))
                        {
                            var ballPosition = new Point(localImage.Width - ballImage.Width, localImage.Height - ballImage.Height);
                            graphics.DrawImage(ballImage, ballPosition);
                        }
                        embedImageUrl = SaveImageLocally(localImage);
                    }
                }
            }
            else
            {
                // Load web image and overlay ball
                (System.Drawing.Image finalCombinedImage, bool ballImageLoaded) = await OverlayBallOnSpecies(speciesImageUrl, ballImgUrl);
                embedImageUrl = SaveImageLocally(finalCombinedImage);

                if (!ballImageLoaded)
                {
                    Console.WriteLine($"Ball image could not be loaded: {ballImgUrl}");
                    // await context.Channel.SendMessageAsync($"Ball image could not be loaded: {ballImgUrl}");
                }
            }

            (int R, int G, int B) = await GetDominantColorAsync(embedImageUrl);
            return (embedImageUrl, new DiscordColor(R, G, B));
        }

        static async Task<(System.Drawing.Image, bool)> OverlayBallOnSpecies(string speciesImageUrl, string ballImageUrl)
        {
            using (var speciesImage = await LoadImageFromUrl(speciesImageUrl))
            {
                if (speciesImage == null)
                {
                    Console.WriteLine("Species image could not be loaded.");
                    return (null, false);
                }

                var ballImage = await LoadImageFromUrl(ballImageUrl);
                if (ballImage == null)
                {
                    Console.WriteLine($"Ball image could not be loaded: {ballImageUrl}");
                    return ((System.Drawing.Image)speciesImage.Clone(), false); // Return false indicating failure
                }

                using (ballImage)
                {
                    using (var graphics = Graphics.FromImage(speciesImage))
                    {
                        var ballPosition = new Point(speciesImage.Width - ballImage.Width, speciesImage.Height - ballImage.Height);
                        graphics.DrawImage(ballImage, ballPosition);
                    }

                    return ((System.Drawing.Image)speciesImage.Clone(), true); // Return true indicating success
                }
            }
        }
        static async Task<System.Drawing.Image> OverlaySpeciesOnEgg(string eggImageUrl, string speciesImageUrl)
        {
            // Load both images
            System.Drawing.Image eggImage = await LoadImageFromUrl(eggImageUrl);
            System.Drawing.Image speciesImage = await LoadImageFromUrl(speciesImageUrl);

            // Calculate the ratio to scale the species image to fit within the egg image size
            double scaleRatio = Math.Min((double)eggImage.Width / speciesImage.Width, (double)eggImage.Height / speciesImage.Height);

            // Create a new size for the species image, ensuring it does not exceed the egg dimensions
            Size newSize = new Size((int)(speciesImage.Width * scaleRatio), (int)(speciesImage.Height * scaleRatio));

            // Resize species image
            System.Drawing.Image resizedSpeciesImage = new Bitmap(speciesImage, newSize);

            // Create a graphics object for the egg image
            using (Graphics g = Graphics.FromImage(eggImage))
            {
                // Calculate the position to center the species image on the egg image
                int speciesX = (eggImage.Width - resizedSpeciesImage.Width) / 2;
                int speciesY = (eggImage.Height - resizedSpeciesImage.Height) / 2;

                // Draw the resized and centered species image over the egg image
                g.DrawImage(resizedSpeciesImage, speciesX, speciesY, resizedSpeciesImage.Width, resizedSpeciesImage.Height);
            }

            // Dispose of the species image and the resized species image if they're no longer needed
            speciesImage.Dispose();
            resizedSpeciesImage.Dispose();

            // Calculate scale factor for resizing while maintaining aspect ratio
            double scale = Math.Min(128.0 / eggImage.Width, 128.0 / eggImage.Height);

            // Calculate new dimensions
            int newWidth = (int)(eggImage.Width * scale);
            int newHeight = (int)(eggImage.Height * scale);

            // Create a new 128x128 bitmap
            Bitmap finalImage = new Bitmap(128, 128);

            // Draw the resized egg image onto the new bitmap, centered
            using (Graphics g = Graphics.FromImage(finalImage))
            {
                // Calculate centering position
                int x = (128 - newWidth) / 2;
                int y = (128 - newHeight) / 2;

                // Draw the image
                g.DrawImage(eggImage, x, y, newWidth, newHeight);
            }

            // Dispose of the original egg image if it's no longer needed
            eggImage.Dispose();

            // The finalImage now contains the overlay, is resized, and maintains aspect ratio
            return finalImage;
        }

        static async Task<System.Drawing.Image> LoadImageFromUrl(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to load image from {url}. Status code: {response.StatusCode}");
                    return null;
                }

                Stream stream = await response.Content.ReadAsStreamAsync();
                if (stream == null || stream.Length == 0)
                {
                    Console.WriteLine($"No data or empty stream received from {url}");
                    return null;
                }

                try
                {
                    return System.Drawing.Image.FromStream(stream);
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Failed to create image from stream. URL: {url}, Exception: {ex}");
                    return null;
                }
            }
        }

        static async Task ScheduleFileDeletion(string filePath, int delayInMilliseconds, int batchTradeId = -1)
        {
            if (batchTradeId != -1)
            {
                // If this is part of a batch trade, add the file path to the dictionary
                if (!batchTradeFiles.ContainsKey(batchTradeId))
                {
                    batchTradeFiles[batchTradeId] = new List<string>();
                }

                batchTradeFiles[batchTradeId].Add(filePath);
            }
            else
            {
                // If this is not part of a batch trade, delete the file after the delay
                await Task.Delay(delayInMilliseconds);
                DeleteFile(filePath);
            }
        }

        static void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error deleting file: {ex.Message}");
                }
            }
        }

        // Call this method after the last trade in a batch is completed
        static void DeleteBatchTradeFiles(int batchTradeId)
        {
            if (batchTradeFiles.TryGetValue(batchTradeId, out var files))
            {
                foreach (var filePath in files)
                {
                    DeleteFile(filePath);
                }
                batchTradeFiles.Remove(batchTradeId);
            }
        }
    }
    public enum AlcremieDecoration
    {
        Strawberry = 0,
        Berry = 1,
        Love = 2,
        Star = 3,
        Clover = 4,
        Flower = 5,
        Ribbon = 6,
    }

    public static async Task<(int R, int G, int B)> GetDominantColorAsync(string imagePath)
    {
        try
        {
            Bitmap image = await LoadImageAsync(imagePath);

            var colorCount = new Dictionary<Color, int>();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixelColor = image.GetPixel(x, y);

                    if (pixelColor.A < 128 || pixelColor.GetBrightness() > 0.9) continue;

                    var brightnessFactor = (int)(pixelColor.GetBrightness() * 100);
                    var saturationFactor = (int)(pixelColor.GetSaturation() * 100);
                    var combinedFactor = brightnessFactor + saturationFactor;

                    var quantizedColor = Color.FromArgb(
                        pixelColor.R / 10 * 10,
                        pixelColor.G / 10 * 10,
                        pixelColor.B / 10 * 10
                    );

                    if (colorCount.ContainsKey(quantizedColor))
                    {
                        colorCount[quantizedColor] += combinedFactor;
                    }
                    else
                    {
                        colorCount[quantizedColor] = combinedFactor;
                    }
                }
            }

            image.Dispose();

            if (colorCount.Count == 0)
                return (255, 255, 255);

            var dominantColor = colorCount.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            return (dominantColor.R, dominantColor.G, dominantColor.B);
        }
        catch (Exception ex)
        {
            // Log or handle exceptions as needed
            Console.WriteLine($"Error processing image from {imagePath}. Error: {ex.Message}");
            return (255, 255, 255);  // Default to white if an exception occurs
        }
    }

    private static async Task<Bitmap> LoadImageAsync(string imagePath)
    {
        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(imagePath);
            using var stream = await response.Content.ReadAsStreamAsync();
            return new Bitmap(stream);
        }
        else
        {
            return new Bitmap(imagePath);
        }
    }

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        string message = string.Empty;
        switch (ex.DiscordCode)
        {
            case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
                {
                    // Check if the exception was raised due to missing "Send Messages" or "Manage Messages" permissions. Nag the bot owner if so.
                    var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                    if (!permissions.SendMessages)
                    {
                        // Nag the owner in logs.
                        message = "You must grant me \"Send Messages\" permissions!";
                        Base.LogUtil.LogError(message, "QueueHelper");
                        return;
                    }
                    if (!permissions.ManageMessages)
                    {
                        var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                        var owner = app.Owner.Id;
                        message = $"<@{owner}> You must grant me \"Manage Messages\" permissions!";
                    }
                }
                break;
            case DiscordErrorCode.CannotSendMessageToUser:
                {
                    // The user either has DMs turned off, or Discord thinks they do.
                    message = context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
                }
                break;
            default:
                {
                    // Send a generic error message.
                    message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                }
                break;
        }
        await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    public static (string, Embed) CreateLGLinkCodeSpriteEmbed(List<Pictocodes> lgcode)
    {
        int codecount = 0;
        List<System.Drawing.Image> spritearray = new();
        foreach (Pictocodes cd in lgcode)
        {


            var showdown = new ShowdownSet(cd.ToString());
            var sav = SaveUtil.GetBlankSAV(EntityContext.Gen7b, "pip");
            PKM pk = sav.GetLegalFromSet(showdown).Created;
            System.Drawing.Image png = pk.Sprite();
            var destRect = new Rectangle(-40, -65, 137, 130);
            var destImage = new Bitmap(137, 130);

            destImage.SetResolution(png.HorizontalResolution, png.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(png, destRect, 0, 0, png.Width, png.Height, GraphicsUnit.Pixel);

            }
            png = destImage;
            spritearray.Add(png);
            codecount++;
        }
        int outputImageWidth = spritearray[0].Width + 20;

        int outputImageHeight = spritearray[0].Height - 65;

        Bitmap outputImage = new Bitmap(outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(outputImage))
        {
            graphics.DrawImage(spritearray[0], new Rectangle(0, 0, spritearray[0].Width, spritearray[0].Height),
                new Rectangle(new Point(), spritearray[0].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[1], new Rectangle(50, 0, spritearray[1].Width, spritearray[1].Height),
                new Rectangle(new Point(), spritearray[1].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[2], new Rectangle(100, 0, spritearray[2].Width, spritearray[2].Height),
                new Rectangle(new Point(), spritearray[2].Size), GraphicsUnit.Pixel);
        }
        System.Drawing.Image finalembedpic = outputImage;
        var filename = $"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
        filename = System.IO.Path.GetFileName($"{System.IO.Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }

}
