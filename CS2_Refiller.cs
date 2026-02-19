using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json.Serialization;

namespace CS2Refiller;

public class CS2RefillerConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    /// <summary>
    /// Награждать ли ассистента за помощь в убийстве
    /// 1 - ассистент получает такое же вознаграждение, как и убийца
    /// 0 - только убийца получает вознаграждение
    /// </summary>
    [JsonPropertyName("css_refiller_assist")]
    public int AssistRefill { get; set; } = 1;

    /// <summary>
    /// Настройка восстановления здоровья за убийство
    /// "all" - восстановить здоровье до 100 HP
    /// "0" - не восстанавливать здоровье
    /// число - добавить указанное количество HP (например, "20")
    /// Правила: если сумма превышает 100, здоровье устанавливается в 100
    /// </summary>
    [JsonPropertyName("css_refiller_health")]
    public string HealthRefill { get; set; } = "25";

    /// <summary>
    /// Настройка восстановления боеприпасов
    /// "all" - восстановить патроны во всех оружиях в инвентаре
    /// "current" - восстановить патроны только в активном оружии
    /// "off" - боеприпасы не восстанавливаются
    /// </summary>
    [JsonPropertyName("css_refiller_ammo")]
    public string AmmoRefill { get; set; } = "current";

    /// <summary>
    /// Настройка восстановления брони за убийство
    /// "all" - восстановить броню до 100
    /// "0" - не восстанавливать броню
    /// число - добавить указанное количество брони (например, "10")
    /// Правила: если сумма превышает 100, броня устанавливается в 100
    /// </summary>
    [JsonPropertyName("css_refiller_armor")]
    public string ArmorRefill { get; set; } = "15";

    /// <summary>
    /// Автоматически пополнять резервные патроны, когда игрок пытается перезарядиться или стрелять, но резерв пуст
    /// 1 - включено, 0 - выключено
    /// </summary>
    [JsonPropertyName("css_refiller_autorefillclip")]
    public int AutoRefillClip { get; set; } = 1;

    /// <summary>
    /// Уровень логирования: 0-Trace, 1-Debug, 2-Information, 3-Warning, 4-Error, 5-Critical
    /// </summary>
    [JsonPropertyName("css_refiller_loglevel")]
    public int LogLevel { get; set; } = 4;
}

[MinimumApiVersion(362)]
public class CS2Refiller : BasePlugin, IPluginConfig<CS2RefillerConfig>
{
    public override string ModuleName => "CS2 Refiller";
    public override string ModuleAuthor => "Fixed by le1t1337 + AI DeepSeek. Code logic by oscar-wos";
    public override string ModuleVersion => "1.6";

    public required CS2RefillerConfig Config { get; set; }

    // Словари для отслеживания состояний
    private readonly Dictionary<int, bool> _reloadButtonPressed = new();
    private readonly Dictionary<int, bool> _attackButtonPressed = new();
    private readonly Dictionary<int, bool> _ammoRefilledForReload = new();
    private readonly Dictionary<int, bool> _ammoRefilledForAttack = new();

    public void OnConfigParsed(CS2RefillerConfig config)
    {
        config.AssistRefill = Math.Clamp(config.AssistRefill, 0, 1);
        config.AutoRefillClip = Math.Clamp(config.AutoRefillClip, 0, 1);
        config.LogLevel = Math.Clamp(config.LogLevel, 0, 5);
        Config = config;
    }

    public override void Load(bool isReload)
    {
        AddCommand("css_refiller_help", "Показать справку по плагину", OnHelpCommand);
        AddCommand("css_refiller_settings", "Показать текущие настройки", OnSettingsCommand);
        AddCommand("css_refiller_test", "Тестовая команда", OnTestCommand);
        AddCommand("css_refiller_reload", "Перезагрузить конфигурацию", OnReloadCommand);

        AddCommand("css_refiller_setassist", "Включить/выключить награду ассистенту (0/1)", OnSetAssistCommand);
        AddCommand("css_refiller_sethealth", "Установить восстановление здоровья (all / 0 / число)", OnSetHealthCommand);
        AddCommand("css_refiller_setammo", "Установить восстановление патронов (all / current / off)", OnSetAmmoCommand);
        AddCommand("css_refiller_setarmor", "Установить восстановление брони (all / 0 / число)", OnSetArmorCommand);
        AddCommand("css_refiller_setautorefillclip", "Включить/выключить авто-пополнение резерва (0/1)", OnSetAutoRefillClipCommand);
        AddCommand("css_refiller_setloglevel", "Установить уровень логирования (0-5)", OnSetLogLevelCommand);

        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterListener<Listeners.OnTick>(OnGameTick);

        PrintInfo();

        if (isReload)
        {
            Server.NextFrame(() => Log(LogLevel.Information, "Горячая перезагрузка выполнена"));
        }
    }

    private void PrintInfo()
    {
        Log(LogLevel.Information, "===============================================");
        Log(LogLevel.Information, $"Плагин {ModuleName} версии {ModuleVersion} успешно загружен!");
        Log(LogLevel.Information, $"Автор: {ModuleAuthor}");
        Log(LogLevel.Information, "Текущие настройки:");
        Log(LogLevel.Information, $"  css_refiller_assist = {Config.AssistRefill} (0/1)");
        Log(LogLevel.Information, $"  css_refiller_health = {Config.HealthRefill}");
        Log(LogLevel.Information, $"  css_refiller_ammo = {Config.AmmoRefill}");
        Log(LogLevel.Information, $"  css_refiller_armor = {Config.ArmorRefill}");
        Log(LogLevel.Information, $"  css_refiller_autorefillclip = {Config.AutoRefillClip} (0/1)");
        Log(LogLevel.Information, $"  css_refiller_loglevel = {Config.LogLevel} (0-Trace, 1-Debug, 2-Information, 3-Warning, 4-Error, 5-Critical)");
        Log(LogLevel.Information, "===============================================");
    }

    private void Log(LogLevel level, string message)
    {
        if ((int)level >= Config.LogLevel)
            Logger.Log(level, "[Refiller] {Message}", message);
    }

    private bool IsValidPlayer(CCSPlayerController player)
    {
        return player != null &&
               player.IsValid &&
               player.PlayerPawn != null &&
               player.PlayerPawn.IsValid &&
               player.PlayerPawn.Value != null &&
               player.PawnIsAlive;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (victim?.IsValid != true)
            return HookResult.Continue;

        var playersToReward = new List<CCSPlayerController?> { @event.Attacker };
        if (Config.AssistRefill == 1 && @event.Assister?.IsValid == true)
            playersToReward.Add(@event.Assister);

        Server.NextFrame(() =>
        {
            try
            {
                foreach (var player in playersToReward)
                {
                    if (player?.IsValid != true || !player.PawnIsAlive)
                        continue;

                    var pawn = player.PlayerPawn?.Value;
                    if (pawn == null || !pawn.IsValid)
                        continue;

                    // Восстановление боеприпасов
                    if (Config.AmmoRefill != "off")
                    {
                        var weaponService = pawn.WeaponServices;
                        if (weaponService != null)
                        {
                            if (Config.AmmoRefill == "all")
                            {
                                var weapons = weaponService.MyWeapons;
                                if (weapons != null)
                                {
                                    foreach (var weaponHandle in weapons)
                                    {
                                        var weapon = weaponHandle?.Value?.As<CBasePlayerWeapon>();
                                        if (weapon?.IsValid != true)
                                            continue;
                                        RefillWeaponAmmo(weapon);
                                    }
                                }
                            }
                            else if (Config.AmmoRefill == "current")
                            {
                                var activeWeapon = weaponService.ActiveWeapon?.Value?.As<CBasePlayerWeapon>();
                                if (activeWeapon?.IsValid == true)
                                    RefillWeaponAmmo(activeWeapon);
                            }
                        }
                    }

                    // Восстановление здоровья
                    if (Config.HealthRefill != "0")
                    {
                        var currentHealth = pawn.Health;
                        var newHealth = currentHealth;
                        if (Config.HealthRefill == "all")
                            newHealth = 100;
                        else if (int.TryParse(Config.HealthRefill, out int healthAdd) && healthAdd > 0)
                            newHealth = Math.Min(100, currentHealth + healthAdd);

                        if (newHealth != currentHealth)
                        {
                            pawn.Health = newHealth;
                            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                        }
                    }

                    // Восстановление брони
                    if (Config.ArmorRefill != "0")
                    {
                        var currentArmor = pawn.ArmorValue;
                        var newArmor = currentArmor;
                        if (Config.ArmorRefill == "all")
                            newArmor = 100;
                        else if (int.TryParse(Config.ArmorRefill, out int armorAdd) && armorAdd > 0)
                            newArmor = Math.Min(100, currentArmor + armorAdd);

                        if (newArmor != currentArmor)
                        {
                            pawn.ArmorValue = newArmor;
                            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_ArmorValue");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Исключение в OnPlayerDeath: {ex.Message}");
            }
        });

        return HookResult.Continue;
    }

    private void RefillWeaponAmmo(CBasePlayerWeapon weapon)
    {
        try
        {
            var weaponBase = weapon.As<CCSWeaponBase>();
            if (weaponBase?.VData == null)
                return;

            // Восстанавливаем патроны в основном магазине (Clip1)
            weapon.Clip1 = weaponBase.VData.MaxClip1;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");

            // Восстанавливаем запасные патроны (ReserveAmmo)
            if (weapon.ReserveAmmo.Length > 0)
            {
                weapon.ReserveAmmo[0] = weaponBase.VData.PrimaryReserveAmmoMax;
            }

            // Восстанавливаем вторичный магазин (Clip2), если он используется
            if (weapon.Clip2 > 0)
            {
                weapon.Clip2 = weaponBase.VData.MaxClip1;
                Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip2");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка при восстановлении патронов: {ex.Message}");
        }
    }

    private void OnGameTick()
    {
        if (Config.AutoRefillClip == 0)
            return;

        try
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsValidPlayer(player))
                    continue;

                int playerIndex = (int)player.Index;
                bool isReloadPressed = (player.Buttons & PlayerButtons.Reload) != 0;
                bool isAttackPressed = (player.Buttons & PlayerButtons.Attack) != 0;

                // Отслеживаем изменение состояния кнопки перезарядки
                if (isReloadPressed && !_reloadButtonPressed.GetValueOrDefault(playerIndex))
                {
                    _reloadButtonPressed[playerIndex] = true;
                    _ammoRefilledForReload[playerIndex] = false;
                }
                else if (!isReloadPressed && _reloadButtonPressed.GetValueOrDefault(playerIndex))
                {
                    _reloadButtonPressed[playerIndex] = false;
                    _ammoRefilledForReload[playerIndex] = false;
                }

                // Отслеживаем изменение состояния кнопки атаки
                if (isAttackPressed && !_attackButtonPressed.GetValueOrDefault(playerIndex))
                {
                    _attackButtonPressed[playerIndex] = true;
                    _ammoRefilledForAttack[playerIndex] = false;
                }
                else if (!isAttackPressed && _attackButtonPressed.GetValueOrDefault(playerIndex))
                {
                    _attackButtonPressed[playerIndex] = false;
                    _ammoRefilledForAttack[playerIndex] = false;
                }

                var pawn = player.PlayerPawn.Value;
                var weaponService = pawn?.WeaponServices;
                if (weaponService == null)
                    continue;

                var activeWeapon = weaponService.ActiveWeapon?.Value?.As<CBasePlayerWeapon>();
                if (activeWeapon?.IsValid != true)
                    continue;

                var weaponBase = activeWeapon.As<CCSWeaponBase>();
                if (weaponBase?.VData == null)
                    continue;

                bool hasReserve = activeWeapon.ReserveAmmo.Length > 0 && activeWeapon.ReserveAmmo[0] > 0;
                bool clipEmpty = activeWeapon.Clip1 == 0;

                // 1. Обработка нажатия R: если резерв пуст, пополняем его
                if (isReloadPressed && !_ammoRefilledForReload.GetValueOrDefault(playerIndex))
                {
                    if (!hasReserve)
                    {
                        if (activeWeapon.ReserveAmmo.Length > 0)
                        {
                            activeWeapon.ReserveAmmo[0] = weaponBase.VData.PrimaryReserveAmmoMax;
                            Log(LogLevel.Trace, $"Пополнен резерв для {player.PlayerName} (по нажатию R)");
                        }
                        _ammoRefilledForReload[playerIndex] = true;
                    }
                }

                // 2. Обработка нажатия атаки: если магазин пуст и резерв пуст, пополняем резерв
                if (isAttackPressed && !_ammoRefilledForAttack.GetValueOrDefault(playerIndex))
                {
                    if (clipEmpty && !hasReserve)
                    {
                        if (activeWeapon.ReserveAmmo.Length > 0)
                        {
                            activeWeapon.ReserveAmmo[0] = weaponBase.VData.PrimaryReserveAmmoMax;
                            Log(LogLevel.Trace, $"Пополнен резерв для {player.PlayerName} (по нажатию атаки при пустом оружии)");
                        }
                        _ammoRefilledForAttack[playerIndex] = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка в OnGameTick: {ex.Message}");
        }
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsValid == true)
        {
            int index = (int)player.Index;
            _reloadButtonPressed.Remove(index);
            _attackButtonPressed.Remove(index);
            _ammoRefilledForReload.Remove(index);
            _ammoRefilledForAttack.Remove(index);
            Log(LogLevel.Debug, $"Игрок {player.PlayerName} отключился, данные очищены");
        }
        return HookResult.Continue;
    }

    // ---------- Команды ----------

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        string help = $"""
            ================================================
            СПРАВКА ПО ПЛАГИНУ {ModuleName} v{ModuleVersion}
            ================================================
            ОПИСАНИЕ:
              Автоматически восстанавливает здоровье, патроны и броню игроку,
              совершившему убийство (и, опционально, ассистенту).

            ОСОБЕННОСТИ АВТО-ПОПОЛНЕНИЯ:
              Если параметр AutoRefillClip включён (по умолчанию 1), то:
              - При нажатии R (перезарядка), если резерв пуст, он пополняется до максимума.
              - При попытке стрельбы (нажатие атаки), если магазин пуст и резерв пуст,
                резерв также пополняется. После этого игра автоматически начнёт перезарядку.
              Магазин не пополняется мгновенно — сохраняется реализм перезарядки.

            ИСПОЛЬЗОВАНИЕ:
              Настройки задаются в конфиге или через консольные команды.
              Доступные команды:

              css_refiller_help                - показать эту справку
              css_refiller_settings             - показать текущие настройки
              css_refiller_test                  - проверить работу плагина
              css_refiller_reload                - перезагрузить конфигурацию

              css_refiller_setassist <0/1>       - вкл/выкл награду ассистенту
              css_refiller_sethealth <значение>  - здоровье: all / 0 / число
              css_refiller_setammo <значение>    - патроны: all / current / off
              css_refiller_setarmor <значение>   - броня: all / 0 / число
              css_refiller_setautorefillclip <0/1> - авто-пополнение резерва
              css_refiller_setloglevel <0-5>     - уровень логов

            ПРИМЕРЫ:
              css_refiller_sethealth 50
              css_refiller_setammo all
              css_refiller_setassist 1
            ================================================
            """;
        command.ReplyToCommand(help);
        if (player != null)
            player.PrintToChat($" [Refiller] Справка отправлена в консоль.");
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        string status = Config.AssistRefill == 1 ? "Включена" : "Отключена";
        string autoStatus = Config.AutoRefillClip == 1 ? "Включено" : "Отключено";
        int onlineCount = Utilities.GetPlayers().Count(p => IsValidPlayer(p));

        string settings = $"""
            ================================================
            ТЕКУЩИЕ НАСТРОЙКИ {ModuleName} v{ModuleVersion}
            ================================================
            Награда ассистенту: {status}
            Восстановление здоровья: {Config.HealthRefill}
            Восстановление патронов: {Config.AmmoRefill}
            Восстановление брони: {Config.ArmorRefill}
            Авто-пополнение резерва: {autoStatus}
            Уровень логирования: {Config.LogLevel} (0-Trace, 1-Debug, 2-Information, 3-Warning, 4-Error, 5-Critical)

            Активных игроков: {onlineCount}
            ================================================
            """;
        command.ReplyToCommand(settings);
        if (player != null)
            player.PrintToChat($" [Refiller] Настройки отправлены в консоль.");
    }

    private void OnTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            command.ReplyToCommand("[Refiller] Эта команда доступна только игрокам.");
            return;
        }

        player.PrintToChat("=== ТЕСТ ПЛАГИНА REFILLER ===");
        player.PrintToChat("Плагин работает, текущие настройки:");
        player.PrintToChat($"  Assist: {(Config.AssistRefill == 1 ? "Да" : "Нет")}");
        player.PrintToChat($"  Health: {Config.HealthRefill}");
        player.PrintToChat($"  Ammo: {Config.AmmoRefill}");
        player.PrintToChat($"  Armor: {Config.ArmorRefill}");
        player.PrintToChat($"  AutoRefillClip: {(Config.AutoRefillClip == 1 ? "Да" : "Нет")}");
        player.PrintToChat("Совершите убийство, чтобы проверить восстановление.");
        player.PrintToChat("Для проверки авто-пополнения: расстреляйте все патроны, затем нажмите R или попробуйте стрелять — резерв должен пополниться.");
        command.ReplyToCommand("[Refiller] Тестовая информация выведена в чат.");
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            string configPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "CS2Refiller", "CS2Refiller.json");
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var newConfig = System.Text.Json.JsonSerializer.Deserialize<CS2RefillerConfig>(json);
                if (newConfig != null)
                    OnConfigParsed(newConfig);
            }
            else
            {
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка при перезагрузке конфига: {ex.Message}");
            command.ReplyToCommand("[Refiller] Ошибка при перезагрузке конфига.");
            return;
        }

        command.ReplyToCommand("[Refiller] Конфигурация перезагружена.");
        Log(LogLevel.Information, "Конфигурация перезагружена по команде.");
    }

    private void OnSetAssistCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущее значение assist: {Config.AssistRefill} (по умолч. 1). Использование: css_refiller_setassist <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.AssistRefill;
            Config.AssistRefill = value;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] assist изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetHealthCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущее значение health: {Config.HealthRefill} (по умолч. 25). Использование: css_refiller_sethealth <all/0/число>");
            return;
        }

        string arg = command.GetArg(1);
        string old = Config.HealthRefill;

        if (arg == "all" || arg == "0" || (int.TryParse(arg, out _) && int.Parse(arg) > 0))
        {
            Config.HealthRefill = arg;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] health изменён с {old} на {Config.HealthRefill}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Допустимо: all, 0, положительное число.");
        }
    }

    private void OnSetAmmoCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущее значение ammo: {Config.AmmoRefill} (по умолч. current). Использование: css_refiller_setammo <all/current/off>");
            return;
        }

        string arg = command.GetArg(1).ToLower();
        string old = Config.AmmoRefill;

        if (arg == "all" || arg == "current" || arg == "off")
        {
            Config.AmmoRefill = arg;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] ammo изменён с {old} на {Config.AmmoRefill}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Допустимо: all, current, off.");
        }
    }

    private void OnSetArmorCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущее значение armor: {Config.ArmorRefill} (по умолч. 15). Использование: css_refiller_setarmor <all/0/число>");
            return;
        }

        string arg = command.GetArg(1);
        string old = Config.ArmorRefill;

        if (arg == "all" || arg == "0" || (int.TryParse(arg, out _) && int.Parse(arg) > 0))
        {
            Config.ArmorRefill = arg;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] armor изменён с {old} на {Config.ArmorRefill}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Допустимо: all, 0, положительное число.");
        }
    }

    private void OnSetAutoRefillClipCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущее значение autorefillclip: {Config.AutoRefillClip} (по умолч. 1). Использование: css_refiller_setautorefillclip <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.AutoRefillClip;
            Config.AutoRefillClip = value;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] autorefillclip изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetLogLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[Refiller] Текущий уровень логов: {Config.LogLevel} (0-Trace, 1-Debug, 2-Information, 3-Warning, 4-Error, 5-Critical). Использование: css_refiller_setloglevel <0-5>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && value >= 0 && value <= 5)
        {
            int old = Config.LogLevel;
            Config.LogLevel = value;
            SaveConfig();
            command.ReplyToCommand($"[Refiller] Уровень логов изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand("[Refiller] Неверное значение. Используйте число от 0 до 5.");
        }
    }

    private void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "CS2Refiller", "CS2Refiller.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
            Log(LogLevel.Debug, $"Конфигурация сохранена в {configPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Refiller] Ошибка сохранения конфигурации");
        }
    }

    public override void Unload(bool hotReload)
    {
        Log(LogLevel.Information, "Плагин выгружается.");
    }
}