using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class ReusableActions
{
    public static async Task SendPKMAsync(this IMessageChannel channel, PKM pkm, string msg = "")
    {
        var tmp = Path.Combine(Path.GetTempPath(), Util.CleanFileName(pkm.FileName));
        await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);
        await channel.SendFileAsync(tmp, msg).ConfigureAwait(false);
        File.Delete(tmp);
    }

    public static async Task SendPKMAsync(this IUser user, PKM pkm, string msg = "")
    {
        var tmp = Path.Combine(Path.GetTempPath(), Util.CleanFileName(pkm.FileName));
        await File.WriteAllBytesAsync(tmp, pkm.DecryptedPartyData);
        await user.SendFileAsync(tmp, msg).ConfigureAwait(false);
        File.Delete(tmp);
    }

    public static async Task RepostPKMAsShowdownAsync(this ISocketMessageChannel channel, IAttachment att, SocketUserMessage userMessage)
    {
        if (!EntityDetection.IsSizePlausible(att.Size))
            return;
        var result = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!result.Success)
            return;

        var pkm = result.Data!;
        await channel.SendPKMAsShowdownSetAsync(pkm, userMessage).ConfigureAwait(false);
    }

    public static RequestSignificance GetFavor(this IUser user)
    {
        var mgr = SysCordSettings.Manager;
        if (user.Id == mgr.Owner)
            return RequestSignificance.Owner;
        if (mgr.CanUseSudo(user.Id))
            return RequestSignificance.Favored;
        if (user is SocketGuildUser g)
            return mgr.GetSignificance(g.Roles.Select(z => z.Name));
        return RequestSignificance.None;
    }

    public static async Task EchoAndReply(this ISocketMessageChannel channel, string msg)
    {
        // Announce it in the channel the command was entered only if it's not already an echo channel.
        EchoUtil.Echo(msg);
        if (!EchoModule.IsEchoChannel(channel))
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
    }

    public static async Task SendPKMAsShowdownSetAsync(this ISocketMessageChannel channel, PKM pkm, SocketUserMessage userMessage)
    {
        var txt = GetFormattedShowdownText(pkm);
        bool canGmax = pkm is PK8 pk8 && pk8.CanGigantamax;
        var speciesImageUrl = AbstractTrade<PK9>.PokeImg(pkm, canGmax, false);

        var embed = new EmbedBuilder()
            .WithTitle("Pokémon Showdown Set")
            .WithDescription(txt)
            .WithColor(Color.Blue)
            .WithThumbnailUrl(speciesImageUrl)
            .Build();

        //var botMessage = await channel.SendMessageAsync(embed: embed).ConfigureAwait(false); // Send the embed
        //var warningMessage = await channel.SendMessageAsync("This message will self-destruct in 15 seconds. Please copy your data.").ConfigureAwait(false);
        //await Task.Delay(2000).ConfigureAwait(false);
        //await userMessage.DeleteAsync().ConfigureAwait(false);
        //await Task.Delay(20000).ConfigureAwait(false);
        //await botMessage.DeleteAsync().ConfigureAwait(false);
        //await warningMessage.DeleteAsync().ConfigureAwait(false);
    }


    public static string GetFormattedShowdownText(PKM pkm)
    {
        var newShowdown = new List<string>();
        var showdown = ShowdownParsing.GetShowdownText(pkm);
        foreach (var line in showdown.Split('\n'))
            newShowdown.Add(line);

        if (pkm.IsEgg)
            newShowdown.Add("\nPokémon is an egg");
        if (pkm.Ball > (int)Ball.None)
            newShowdown.Insert(newShowdown.FindIndex(z => z.Contains("Nature")), $"Ball: {(Ball)pkm.Ball} Ball");
        if (pkm.IsShiny)
        {
            var index = newShowdown.FindIndex(x => x.Contains("Shiny: Yes"));
            if (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                newShowdown[index] = "Shiny: Square\r";
            else newShowdown[index] = "Shiny: Star\r";
        }

        newShowdown.InsertRange(1, new string[] {
            $"OT: {pkm.OriginalTrainerName}",
            $"TID: {pkm.DisplayTID}",
            $"SID: {pkm.DisplaySID}",
            $"OTGender: {(Gender)pkm.OriginalTrainerGender}",
            $"Language: {(LanguageID)pkm.Language}",
            $".FatefulEncounter={pkm.FatefulEncounter}",
            $".MetLevel={pkm.MetLevel}",
            $".MetDate=20{pkm.MetYear}{(pkm.MetMonth < 10 ? "0" : "")}{pkm.MetMonth}{(pkm.MetDay < 10 ? "0" : "")}{pkm.MetDay}",
            $"{(pkm.IsEgg ? "\nIsEgg: Yes" : "")}"}
            );
        return Format.Code(string.Join("\n", newShowdown).TrimEnd());
    }

    private static readonly string[] separator = [ ",", ", ", " " ];

    public static IReadOnlyList<string> GetListFromString(string str)
    {
        // Extract comma separated list
        return str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
