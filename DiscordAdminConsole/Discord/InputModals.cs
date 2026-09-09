using Discord;
using DiscordAdminConsole.Commands;
using DiscordAdminConsole.Discord;
using DiscordAdminConsole.Localization;

namespace DiscordAdminConsole.Discord.Components;

public static class InputModals
{
    private const int ModalTitleLimit = 45;

    public static Modal BuildArgumentsModal(CommandDefinition definition, string sessionId, bool askSteamId, Localizer loc)
    {
        var builder = new ModalBuilder(
            definition.Name.Length > ModalTitleLimit ? definition.Name[..ModalTitleLimit] : definition.Name,
            CustomIds.ModalArgs + sessionId);

        var hasInput = false;

        if (askSteamId && definition.RequiresPlayer)
        {
            builder.AddTextInput(
                loc.Get("modal.steamId"),
                CustomIds.InputSteamId,
                TextInputStyle.Short,
                "76561198000000000",
                minLength: 17,
                maxLength: 17,
                required: true);
            hasInput = true;
        }

        if (definition.HasTime)
        {
            builder.AddTextInput(
                loc.Get("modal.time"),
                CustomIds.InputTime,
                TextInputStyle.Short,
                "30",
                minLength: 1,
                maxLength: 7,
                required: true);
        }

        if (definition.HasMap)
        {
            builder.AddTextInput(
                loc.Get("modal.map"),
                CustomIds.InputMap,
                TextInputStyle.Short,
                "de_mirage",
                minLength: 2,
                maxLength: 64,
                required: true);
        }

        if (definition.HasArguments)
        {
            builder.AddTextInput(
                loc.Get("modal.arguments"),
                CustomIds.InputArguments,
                TextInputStyle.Short,
                placeholder: null,
                minLength: 1,
                maxLength: 200,
                required: true);
        }

        if (definition.HasReason)
        {
            builder.AddTextInput(
                loc.Get("modal.reason"),
                CustomIds.InputReason,
                TextInputStyle.Paragraph,
                "Cheating",
                minLength: 0,
                maxLength: 200,
                required: false);
            hasInput = true;
        }

        if (!hasInput)
        {
            builder.AddTextInput(
                loc.Get("modal.comment"),
                CustomIds.InputReason,
                TextInputStyle.Short,
                placeholder: null,
                minLength: 0,
                maxLength: 100,
                required: false);
        }

        return builder.Build();
    }

    public static Modal BuildRawRconModal(string sessionId, Localizer loc)
    {
        var builder = new ModalBuilder("RCON", CustomIds.ModalRaw + sessionId);
        builder.AddTextInput(
            loc.Get("modal.rcon"),
            CustomIds.InputRawCommand,
            TextInputStyle.Short,
            "mp_restartgame 1",
            minLength: 1,
            maxLength: 200,
            required: true);
        return builder.Build();
    }
}
