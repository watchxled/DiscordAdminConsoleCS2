# Discord Admin Console

DiscordAdminConsole is a plugin for CounterStrikeSharp and Counter-Strike 2.

It connects your CS2 server to a Discord bot and lets administrators manage the server through a convenient panel in Discord. From the panel you can pick a server, view players, execute RCON commands, issue punishments, and monitor the state of your game servers.

## Features

- Connects to a Discord bot via bot token.
- Works with one or multiple CS2 servers.
- Executes RCON commands from Discord.
- Shows the player list with userid and SteamID64.
- Issues and removes punishments.
- Supports both plain CSS commands and Pisex Admin System commands.
- Converts time from minutes to seconds for Pisex `mm_*` commands.
- Passes the punishment reason in quotes.
- Shows server status via A2S:
  - map;
  - player count;
  - online or offline.
- Keeps an audit log of administrator actions.
- Supports multiple `OwnerRoleIds`.
- Restricts the panel to specific Discord channels.
- Rate-limits administrative actions.
- Stores data in MySQL or local JSON files.
- Uses a shared MySQL across multiple plugin instances with automatic leader election.
- **Localization:** English by default, Russian via `"Language": "ru"` in the config (`lang/en.json`, `lang/ru.json`, editable without rebuilding).

## Requirements

- Counter-Strike 2.
- CounterStrikeSharp.
- .NET 8 runtime matching your CounterStrikeSharp build.
- A Discord bot.
- RCON access to the servers you want to manage.
- MySQL or MariaDB if you want a shared database.

## Installation

1. Build the project or download the compiled release (`DiscordAdminConsole.7z` or `DiscordAdminConsole.zip`).
2. Copy the archive contents to:

```text
addons/counterstrikesharp/plugins/
```
3. Start the server. CounterStrikeSharp will generate the plugin configuration.
4. Fill in the bot token, the Discord guild ID and the owner role IDs. Set `"Language"` to `"en"` or `"ru"` if needed.
5. Restart the server.
6. Run the panel setup command in Discord.

## Discord bot

Create an application and a bot in the Discord Developer Portal:

https://discord.com/developers/applications

Minimum bot permissions:

- View Channels;
- Send Messages;
- Embed Links;
- Use External Emojis;
- Read Message History;
- Manage Messages.

## Configuration

An example configuration is located in `examples/DiscordAdminConsole.json`.

Main parameters:

```json
{
  "Version": 1,
  "Debug": false,
  "Language": "en",
  "Database": {
    "Host": "",
    "Port": 3306,
    "Database": "",
    "Username": "",
    "Password": ""
  },
  "Discord": {
    "Token": "DISCORD_BOT_TOKEN",
    "GuildId": 123456789012345678,
    "OwnerRoleIds": [
      111111111111111111,
      222222222222222222
    ],
    "HeartbeatIntervalSeconds": 5,
    "LeaderTtlSeconds": 15,
    "DisableFailover": false
  },
  "AllowedChannelIds": [],
  "Security": {
    "SetupRoleIds": [],
    "EnableRawRcon": true,
    "CooldownSeconds": 5,
    "MaxActionsPerMinute": 12,
    "MaxCommandLength": 200,
    "SessionTimeoutMinutes": 10,
    "CommandTimeoutSeconds": 5,
    "CacheTtlMinutes": 5
  },
  "Integrations": {
    "PisexAdminSystem": false
  },
  "Monitoring": {
    "UpdateIntervalSeconds": 60,
    "IgnoreBots": true,
    "OnlineColor": "#2ECC71",
    "OfflineColor": "#E74C3C"
  }
}
```

### Language

- `en` (default) - all bot messages in English.
- `ru` - all bot messages in Russian.

Language files live in `lang/` next to the plugin DLL (`en.json`, `ru.json`). They are extracted automatically on first start and can be edited on the server - no rebuild required. Missing keys fall back to English. `вкл/выкл/навсегда` are accepted as input in both languages.

### Discord

- `Token` - the Discord bot token.
- `GuildId` - the Discord guild ID.
- `OwnerRoleIds` - list of owner roles. Any single one of them grants full access.
- `HeartbeatIntervalSeconds` - how often the leader renews "bot ownership" in the shared database.
- `LeaderTtlSeconds` - after this many seconds without a heartbeat the leader is considered offline.
- `DisableFailover` - disables automatic leader election.

### Database

If `Host` and `Database` are filled in, the plugin uses MySQL or MariaDB. Tables are created automatically with the `dac_` prefix.

For multiple servers, point every installation to the same database. This shares:

- servers;
- commands;
- roles;
- flags;
- settings;
- status messages.

If no database is configured, the plugin uses JSON files stored next to the plugin.

### OwnerRoleIds and SetupRoleIds

`OwnerRoleIds` - roles with full access. Multiple roles are allowed:

```json
"OwnerRoleIds": [111111111111111111, 222222222222222222]
```

`SetupRoleIds` - roles allowed to run setup commands. Owners have these rights as well.

## Slash commands

Main commands:

```text
/setup-admin-console
/server-add
/server-remove
/server-list
/setup-server-status
/server-status-stop
/status-time
/server-image
/setup-audit
/cmd-list
/cmd-add
/cmd-remove
/cmd-toggle
/role-list
/role-add
/role-remove
/role-flag-add
/role-flag-remove
/bind
/unbind
/flag-list
/flag-add
/flag-remove
```

After adding a server via `/server-add` it appears in the server picker in the panel.

## Commands and placeholders

Commands are stored in the database or JSON storage. The following placeholders are available in templates:

| Placeholder | Value |
|---|---|
| `{PLAYER}` | player userid in `#userid` format |
| `{USERID}` | player userid without the `#` symbol |
| `{STEAMID}` | 17-digit SteamID64 |
| `{TIME}` | time in minutes |
| `{TIME_SECONDS}` | time in seconds, minutes multiplied by 60 |
| `{REASON}` | punishment reason |
| `{MAP}` | map name |
| `{ARGUMENTS}` | additional arguments |

CSS command example:

```text
css_ban {PLAYER} {TIME} "{REASON}"
```

Pisex Admin System command example:

```text
mm_ban {STEAMID} {TIME_SECONDS} "{REASON}"
```

If an administrator enters `30`, the Pisex Admin System command is executed roughly like this:

```text
mm_ban 76561198000000000 1800 "Cheating"
```

## Multiple servers and a shared bot

For multiple CS2 servers the recommended setup is:

1. Install the plugin on every CS2 server.
2. Set up one shared MySQL or MariaDB.
3. Point every installation to the same database.
4. Add all game servers via `/server-add`.
5. Use a single Discord token.

At any moment only one server acts as the "leader". If it goes down, another server becomes the leader after `LeaderTtlSeconds` expires.

## Building

You need the .NET 8 SDK or a compatible SDK:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o publish2
```

Build artifacts (`bin/`, `obj/`) are excluded from the repository via `.gitignore` - they are regenerated automatically on build.
