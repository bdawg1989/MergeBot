using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AnimatedGif;
using System.Drawing;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;
using System.Diagnostics;

namespace SysBot.Pokemon.Discord;

public class OwnerModule<T> : SudoModule<T> where T : PKM, new()
{
    [Command("addSudo")]
    [Summary("Adds mentioned user to global sudo")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task SudoUsers([Remainder] string _)
    {
        var users = Context.Message.MentionedUsers;
        var objects = users.Select(GetReference);
        SysCordSettings.Settings.GlobalSudoList.AddIfNew(objects);
        await ReplyAsync("Done.").ConfigureAwait(false);
    }

    [Command("removeSudo")]
    [Summary("Removes mentioned user from global sudo")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task RemoveSudoUsers([Remainder] string _)
    {
        var users = Context.Message.MentionedUsers;
        var objects = users.Select(GetReference);
        SysCordSettings.Settings.GlobalSudoList.RemoveAll(z => objects.Any(o => o.ID == z.ID));
        await ReplyAsync("Done.").ConfigureAwait(false);
    }

    [Command("addChannel")]
    [Summary("Adds a channel to the list of channels that are accepting commands.")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task AddChannel()
    {
        var obj = GetReference(Context.Message.Channel);
        SysCordSettings.Settings.ChannelWhitelist.AddIfNew(new[] { obj });
        await ReplyAsync("Done.").ConfigureAwait(false);
    }

    [Command("syncChannels")]
    [Alias("sch", "syncchannels")]
    [Summary("Copies all channels from ChannelWhitelist to AnnouncementChannel.")]
    [RequireOwner]
    public async Task SyncChannels()
    {
        var whitelist = SysCordSettings.Settings.ChannelWhitelist.List;
        var announcementList = SysCordSettings.Settings.AnnouncementChannels.List;

        bool changesMade = false;

        foreach (var channel in whitelist)
        {
            if (!announcementList.Any(x => x.ID == channel.ID))
            {
                announcementList.Add(channel);
                changesMade = true;
            }
        }

        if (changesMade)
        {
            await ReplyAsync("Channel whitelist has been successfully synchronized with the announcement channels.").ConfigureAwait(false);
        }
        else
        {
            await ReplyAsync("All channels from the whitelist are already in the announcement channels, no changes made.").ConfigureAwait(false);
        }
    }

    [Command("removeChannel")]
    [Summary("Removes a channel from the list of channels that are accepting commands.")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task RemoveChannel()
    {
        var obj = GetReference(Context.Message.Channel);
        SysCordSettings.Settings.ChannelWhitelist.RemoveAll(z => z.ID == obj.ID);
        await ReplyAsync("Done.").ConfigureAwait(false);
    }

    [Command("leave")]
    [Alias("bye")]
    [Summary("Leaves the current server.")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task Leave()
    {
        await ReplyAsync("Goodbye.").ConfigureAwait(false);
        await Context.Guild.LeaveAsync().ConfigureAwait(false);
    }

    [Command("leaveguild")]
    [Alias("lg")]
    [Summary("Leaves guild based on supplied ID.")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task LeaveGuild(string userInput)
    {
        if (!ulong.TryParse(userInput, out ulong id))
        {
            await ReplyAsync("Please provide a valid Guild ID.").ConfigureAwait(false);
            return;
        }

        var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
        if (guild is null)
        {
            await ReplyAsync($"Provided input ({userInput}) is not a valid guild ID or the bot is not in the specified guild.").ConfigureAwait(false);
            return;
        }

        await ReplyAsync($"Leaving {guild}.").ConfigureAwait(false);
        await guild.LeaveAsync().ConfigureAwait(false);
    }

    [Command("leaveall")]
    [Summary("Leaves all servers the bot is currently in.")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task LeaveAll()
    {
        await ReplyAsync("Leaving all servers.").ConfigureAwait(false);
        foreach (var guild in Context.Client.Guilds)
        {
            await guild.LeaveAsync().ConfigureAwait(false);
        }
    }

    [Command("sudoku")]
    [Alias("kill", "shutdown")]
    [Summary("Causes the entire process to end itself!")]
    [RequireOwner]
    // ReSharper disable once UnusedParameter.Global
    public async Task ExitProgram()
    {
        await Context.Channel.EchoAndReply("Shutting down... goodbye! **Bot services are going offline.**").ConfigureAwait(false);
        Environment.Exit(0);
    }

    [Command("repeek")]
    [Alias("peek")]
    [Summary("Take and send a screenshot from the currently configured Switch.")]
    [RequireSudo]
    public async Task RePeek()
    {
        string ip = OwnerModule<T>.GetBotIPFromJsonConfig();
        var source = new CancellationTokenSource();
        var token = source.Token;

        var bot = SysCord<T>.Runner.GetBot(ip);
        if (bot == null)
        {
            await ReplyAsync($"No bot found with the specified IP address ({ip}).").ConfigureAwait(false);
            return;
        }

        _ = Array.Empty<byte>();
        byte[]? bytes;
        try
        {
            bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? [];
        }
        catch (Exception ex)
        {
            await ReplyAsync($"Error while fetching pixels: {ex.Message}");
            return;
        }

        if (bytes.Length == 0)
        {
            await ReplyAsync("No screenshot data received.");
            return;
        }

        using MemoryStream ms = new(bytes);
        var img = "cap.jpg";
        var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = (DiscordColor?)Color.Purple }
            .WithFooter(new EmbedFooterBuilder { Text = $"Here's your screenshot." });

        await Context.Channel.SendFileAsync(ms, img, embed: embed.Build());
    }

    [Command("video")]
    [Alias("video")]
    [Summary("Take and send a GIF from the currently configured Switch.")]
    [RequireSudo]
    public async Task RePeekGIF()
    {
        await Context.Channel.SendMessageAsync("Processing GIF request...").ConfigureAwait(false);

        // Offload processing to a separate task so we dont hold up gateway tasks
        _ = Task.Run(async () =>
        {
            try
            {
                string ip = OwnerModule<T>.GetBotIPFromJsonConfig();
                var source = new CancellationTokenSource();
                var token = source.Token;
                var bot = SysCord<T>.Runner.GetBot(ip);
                if (bot == null)
                {
                    await ReplyAsync($"No bot found with the specified IP address ({ip}).").ConfigureAwait(false);
                    return;
                }
                var screenshotCount = 10;
                var screenshotInterval = TimeSpan.FromSeconds(0.1 / 10);
#pragma warning disable CA1416 // Validate platform compatibility
                var gifFrames = new List<System.Drawing.Image>();
#pragma warning restore CA1416 // Validate platform compatibility
                for (int i = 0; i < screenshotCount; i++)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                    }
                    catch (Exception ex)
                    {
                        await ReplyAsync($"Error while fetching pixels: {ex.Message}").ConfigureAwait(false);
                        return;
                    }
                    if (bytes.Length == 0)
                    {
                        await ReplyAsync("No screenshot data received.").ConfigureAwait(false);
                        return;
                    }
                    using (var ms = new MemoryStream(bytes))
                    {
                        using var bitmap = new Bitmap(ms);
                        var frame = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        gifFrames.Add(frame);
                    }
                    await Task.Delay(screenshotInterval).ConfigureAwait(false);
                }
                using (var ms = new MemoryStream())
                {
                    using (var gif = new AnimatedGifCreator(ms, 200))
                    {
                        foreach (var frame in gifFrames)
                        {
                            gif.AddFrame(frame);
                            frame.Dispose();
                        }
                    }
                    ms.Position = 0;
                    var gifFileName = "screenshot.gif";
                    var embed = new EmbedBuilder { ImageUrl = $"attachment://{gifFileName}", Color = (DiscordColor?)Color.Red }
                        .WithFooter(new EmbedFooterBuilder { Text = "Here's your GIF." });
                    await Context.Channel.SendFileAsync(ms, gifFileName, embed: embed.Build()).ConfigureAwait(false);
                }
                foreach (var frame in gifFrames)
                {
                    frame.Dispose();
                }
                gifFrames.Clear();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error while processing GIF: {ex.Message}").ConfigureAwait(false);
            }
        });
    }

    private static string GetBotIPFromJsonConfig()
    {
        try
        {
            var jsonData = File.ReadAllText(TradeBot.ConfigPath);
            var config = JObject.Parse(jsonData);

            var ip = config["Bots"][0]["Connection"]["IP"].ToString();
            return ip;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading config file: {ex.Message}");
            return "192.168.1.1";
        }
    }

    [Command("dm")]
    [Summary("Sends a direct message to a specified user.")]
    [RequireOwner]
    public async Task DMUserAsync(SocketUser user, [Remainder] string message)
    {
        var attachments = Context.Message.Attachments;
        var hasAttachments = attachments.Count != 0;

        var embed = new EmbedBuilder
        {
            Title = "Private Message from the Bot Owner",
            Description = message,
            Color = (DiscordColor?)Color.Gold,
            Timestamp = DateTimeOffset.Now,
            ThumbnailUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/pikamail.png"
        };

        try
        {
            var dmChannel = await user.CreateDMChannelAsync();

            if (hasAttachments)
            {
                foreach (var attachment in attachments)
                {
                    using var httpClient = new HttpClient();
                    var stream = await httpClient.GetStreamAsync(attachment.Url);
                    var file = new FileAttachment(stream, attachment.Filename);
                    await dmChannel.SendFileAsync(file, embed: embed.Build());
                }
            }
            else
            {
                await dmChannel.SendMessageAsync(embed: embed.Build());
            }

            var confirmationMessage = await ReplyAsync($"Message successfully sent to {user.Username}.");
            await Context.Message.DeleteAsync();
            await Task.Delay(TimeSpan.FromSeconds(10));
            await confirmationMessage.DeleteAsync();
        }
        catch (Exception ex)
        {
            await ReplyAsync($"Failed to send message to {user.Username}. Error: {ex.Message}");
        }
    }

    [Command("say")]
    [Summary("Sends a message to a specified channel.")]
    [RequireSudo]
    public async Task SayAsync([Remainder] string message)
    {
        var attachments = Context.Message.Attachments;
        var hasAttachments = attachments.Count != 0;

        var indexOfChannelMentionStart = message.LastIndexOf('<');
        var indexOfChannelMentionEnd = message.LastIndexOf('>');
        if (indexOfChannelMentionStart == -1 || indexOfChannelMentionEnd == -1)
        {
            await ReplyAsync("Please mention a channel properly using #channel.");
            return;
        }

        var channelMention = message.Substring(indexOfChannelMentionStart, indexOfChannelMentionEnd - indexOfChannelMentionStart + 1);
        var actualMessage = message.Substring(0, indexOfChannelMentionStart).TrimEnd();

        var channel = Context.Guild.Channels.FirstOrDefault(c => $"<#{c.Id}>" == channelMention);

        if (channel == null)
        {
            await ReplyAsync("Channel not found.");
            return;
        }

        if (channel is not IMessageChannel messageChannel)
        {
            await ReplyAsync("The mentioned channel is not a text channel.");
            return;
        }

        // If there are attachments, send them to the channel
        if (hasAttachments)
        {
            foreach (var attachment in attachments)
            {
                using var httpClient = new HttpClient();
                var stream = await httpClient.GetStreamAsync(attachment.Url);
                var file = new FileAttachment(stream, attachment.Filename);
                await messageChannel.SendFileAsync(file, actualMessage);
            }
        }
        else
        {
            await messageChannel.SendMessageAsync(actualMessage);
        }

        // Send confirmation message to the user
        await ReplyAsync($"Message successfully posted in {channelMention}.");
    }

    private RemoteControlAccess GetReference(IUser channel) => new()
    {
        ID = channel.Id,
        Name = channel.Username,
        Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
    };

    private RemoteControlAccess GetReference(IChannel channel) => new()
    {
        ID = channel.Id,
        Name = channel.Name,
        Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
    };


    [Command("startsysdvr")]
    [Alias("dvrstart", "startdvr", "sysdvrstart")]
    [Summary("Makes the bot open SysDVR to stream your Switch on the current PC.")]
    [RequireOwner]
    public async Task StartSysDvr()
    {
        try
        {
            var sysDvrBATPath = Path.Combine("SysDVR.bat");
            if (File.Exists(sysDvrBATPath))
            {
                Process.Start(sysDvrBATPath);
                await ReplyAsync("SysDVR has been initiated. You're now streaming your Switch on PC!");
            }
            else
            {
                await ReplyAsync("**SysDVR.bat** cannot be found at the specified location.");
            }
        }
        catch (Exception ex)
        {
            await ReplyAsync($"**SysDVR Error:** {ex.Message}");
        }
    }

    [Command("sysdvr")]
    [Alias("stream")]
    [Summary("Displays instructions on how to use SysDVR.")]
    [RequireOwner]
    public async Task SysDVRInstructionsAsync()
    {
        var embed0 = new EmbedBuilder()
            .WithTitle("-----------SYSDVR SETUP INSTRUCTIONS-----------");

        embed0.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message0 = await ReplyAsync(embed: embed0.Build());


        var embed1 = new EmbedBuilder()
            .AddField("01) SETTING UP THE SYSBOT WITH SYSDVR",
                      "- [Click here](https://github.com/exelix11/SysDVR/releases) to download **SysDVR-Client-Windows-x64.7z**.\n" +
                      "- Unpack the archive and place the extracted folder anywhere you want.\n" +
                      "- Inside the folder, open **SysDVR-ClientGUI.exe.**\n" +
                      "- Select either *Video* or *Both* under the channels to stream.\n" +
                      "- Select **TCP Bridge** and enter your Switch's IP address.\n" +
                      "- Select **Create quick launch shortcut** to create a **SysDVR Launcher.bat**.\n" +
                      "- Exit the program window that launches.\n" +
                      "- Place the **SysDVR Launcher.bat** in the same folder as your SysBot.\n" +
                      "- Rename the bat file to **SysDVR.bat.**\n" +
                      "- You can then use the `sysdvr start` command once you add SysDVR to your Switch.");

        embed1.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message1 = await ReplyAsync(embed: embed1.Build());


        var embed2 = new EmbedBuilder()
            .AddField("02) SETTING UP SYSDVR ON THE SWITCH",
                      "- [Click here](https://github.com/exelix11/SysDVR/releases) to download **SysDVR.zip**.\n" +
                      "- Unpack the archive and place the extracted folders on the Switch SD card.\n" +
                      "- Reboot your Switch.\n" +
                      "- Open the SysDVR program in the Switch.\n" +
                      "- Select **TCP Bridge.**\n" +
                      "- Select **Save current mode as default.**\n" +
                      "- Select **Save and exit.**\n" +
                      "- As long as you followed Step 01, the `sysdvr start` command can be used.\n");

        embed2.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message2 = await ReplyAsync(embed: embed2.Build());

        _ = Task.Run(async () =>
        {
            await Task.Delay(90_000);
            await message0.DeleteAsync();
            await message1.DeleteAsync();
            await message2.DeleteAsync();
        });
    }
}
