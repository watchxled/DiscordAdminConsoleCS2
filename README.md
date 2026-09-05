# DiscordAdminConsole

DiscordAdminConsole - плагин для CounterStrikeSharp и Counter-Strike 2.

Он подключает CS2-сервер к Discord-боту и позволяет администраторам управлять сервером через удобную панель в Discord. Через панель можно выбрать сервер, посмотреть игроков, выполнить RCON-команду, выдать наказание и посмотреть состояние игровых серверов.

## Что умеет плагин

- Подключаться к Discord-боту через bot token.
- Работать с одним или несколькими CS2-серверами.
- Выполнять RCON-команды из Discord.
- Показывать список игроков с userid и SteamID64.
- Выдавать и снимать наказания:
- Поддерживать обычные CSS-команды и команды Pisex Admin System.
- Переводить время из минут в секунды для команд Pisex `mm_*`.
- Передавать причину наказания в кавычках.
- Проверять SteamID64. Разрешены ровно 17 цифр.
- Показывать статус серверов через A2S:
  - карту;
  - количество игроков;
  - онлайн или офлайн.
- Обновлять статусные сообщения в Discord.
- Вести журнал действий администраторов.
- Управлять доступом через Discord-роли и plugin flags.
- Поддерживать несколько `OwnerRoleIds`.
- Ограничивать работу панели определёнными Discord-каналами.
- Ограничивать частоту административных действий.
- Хранить данные в MySQL или локальных JSON-файлах.
- Использовать общую MySQL для нескольких экземпляров плагина и автоматического выбора контроллера.

## Требования

- Counter-Strike 2.
- CounterStrikeSharp.
- .NET 8 runtime, совместимый с вашей сборкой CounterStrikeSharp.
- Discord-бот.
- RCON-доступ к серверам, которыми нужно управлять.
- MySQL или MariaDB, если нужна общая база данных.

## Установка

1. Соберите проект или скачайте скомпилированный проект под названием `DiscordAdminConsole.7z` или `DiscordAdminConsole.zip`.
2. Перенесите содержимое архива по пути:

```text
addons/counterstrikesharp/plugins/
```

3. Убедитесь, что вместе с плагином присутствует `Newtonsoft.Json.dll`. CounterStrikeSharp требует эту библиотеку при загрузке типов плагина.
4. Запустите сервер. CounterStrikeSharp создаст конфигурацию плагина.
5. Заполните токен бота, ID Discord-сервера и ID ролей владельцев.
6. Перезапустите сервер.
7. В Discord выполните команду настройки панели.

## Discord-бот

Создайте приложение и бота в Discord Developer Portal:

https://discord.com/developers/applications

Минимальные права бота:

- View Channels;
- Send Messages;
- Embed Links;
- Use External Emojis.
- Read Message History;
- Manage Messages.
- Read Message History;
- Manage Messages.

## Конфигурация

Пример конфигурации находится в `examples/DiscordAdminConsole.json`.

Основные параметры:

```json
{
  "Version": 1,
  "Debug": false,
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

### Discord

- `Token` - токен Discord-бота.
- `GuildId` - ID Discord-сервера.
- `OwnerRoleIds` - список ролей владельцев. Наличие любой одной роли даёт полный доступ.
- `HeartbeatIntervalSeconds` - как часто лидер обновляет аренду в общей БД.
- `LeaderTtlSeconds` - через сколько секунд без heartbeat лидер считается отключённым.
- `DisableFailover` - отключает автоматический выбор лидера.

### Database

Если `Host` и `Database` заполнены, плагин использует MySQL или MariaDB. Таблицы создаются автоматически с префиксом `dac_`.

Для нескольких серверов требуется указать одну и ту же базу во всех конфигурациях. Это позволит использовать общие:

- серверы;
- команды;
- роли;
- флаги;
- настройки;
- статусные сообщения;

Если база не настроена, плагин использует JSON-файлы рядом с плагином.

### OwnerRoleIds и SetupRoleIds

`OwnerRoleIds` - роли с полным доступом. Можно указывать несколько ролей:

```json
"OwnerRoleIds": [111111111111111111, 222222222222222222]
```

`SetupRoleIds` - роли, которым разрешено выполнять команды настройки. Владельцы также имеют эти права.

## Slash-команды

Основные команды:

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

После добавления сервера через `/server-add` он появится в выборе серверов в панели.

## Команды и плейсхолдеры

Команды хранятся в базе или JSON-хранилище. Для шаблонов доступны следующие плейсхолдеры:

| Плейсхолдер | Значение |
|---|---|
| `{PLAYER}` | userid игрока в формате `#userid` |
| `{USERID}` | userid игрока без символа `#` |
| `{STEAMID}` | SteamID64 из 17 цифр |
| `{TIME}` | время в минутах |
| `{TIME_SECONDS}` | время в секундах, минуты умножаются на 60 |
| `{REASON}` | причина наказания |
| `{MAP}` | название карты |
| `{ARGUMENTS}` | дополнительные аргументы |

Пример команды CSS:

```text
css_ban {PLAYER} {TIME} "{REASON}"
```

Пример команды Pisex Admin System:

```text
mm_ban {STEAMID} {TIME_SECONDS} "{REASON}"
```

Если администратор вводит `30`, команда Pisex Admin System будет выполнена примерно так:

```text
mm_ban 76561198000000000 1800 "Cheating"
```

## Несколько серверов и общий бот

Для нескольких CS2-серверов рекомендуется такая схема:

1. Установить плагин на каждый CS2-сервер.
2. Настроить одну общую MySQL или MariaDB.
3. Указать одну и ту же базу во всех конфигурациях.
4. Добавить все игровые серверы через `/server-add`.
5. Использовать один Discord-токен.

В каждый момент только один сервер работает как "лидер". Если он отключается, другой сервер после истечения `LeaderTtlSeconds` становится "лидером".

## Сборка

Для сборки нужен .NET SDK 8 или совместимый SDK:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o publish2
```
