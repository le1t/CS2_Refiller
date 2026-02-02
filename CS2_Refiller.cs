using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json.Serialization;

namespace CS2Refiller;

public class CS2RefillerConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;
    
    /// <summary>
    /// Награждать ли ассистента за помощь в убийстве
    /// true - ассистент получает такое же вознаграждение, как и убийца
    /// false - только убийца получает вознаграждение
    /// </summary>
    [JsonPropertyName("AssistRefill")] 
    public bool AssistRefill { get; set; } = true;
    
    /// <summary>
    /// Настройка восстановления здоровья за убийство
    /// "all" - восстановить здоровье до 100 HP
    /// "0" - не восстанавливать здоровье
    /// "20" - добавить 20 HP (или любое другое число)
    /// Правила: если сумма превышает 100, здоровье устанавливается в 100
    /// </summary>
    [JsonPropertyName("HealthRefill")] 
    public string HealthRefill { get; set; } = "25";
    
    /// <summary>
    /// Настройка восстановления боеприпасов
    /// "all" - восстановить патроны во всех оружиях в инвентаре
    /// "current" - восстановить патроны только в активном оружии
    /// "off" - боеприпасы не восстанавливаются
    /// </summary>
    [JsonPropertyName("AmmoRefill")] 
    public string AmmoRefill { get; set; } = "current";
    
    /// <summary>
    /// Настройка восстановления брони за убийство
    /// "all" - восстановить броню до 100
    /// "0" - не восстанавливать броню
    /// "10" - добавить 10 единиц брони (или любое другое число)
    /// Правила: если сумма превышает 100, броня устанавливается в 100
    /// </summary>
    [JsonPropertyName("ArmorRefill")] 
    public string ArmorRefill { get; set; } = "15";
}

public class CS2Refiller : BasePlugin, IPluginConfig<CS2RefillerConfig>
{
    public override string ModuleName => "CS2 Refiller";
    public override string ModuleAuthor => "Fixed by le1t1337 + AI DeepSeek. Code logic by oscar-wos";
    public override string ModuleVersion => "1.2";

    public CS2RefillerConfig Config { get; set; } = new();

    public void OnConfigParsed(CS2RefillerConfig config)
    {
        Config = config;
    }

    public override void Load(bool isReload)
    {
        // Выводим информацию о конфигурации
        PrintConVarInfo();
        
        // Регистрируем команды
        AddCommand("css_refiller_help", "Show Refiller help", OnHelpCommand);
        AddCommand("css_refiller_settings", "Show current Refiller settings", OnSettingsCommand);
        
        // Регистрируем обработчик события смерти
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
    }
    
    private void PrintConVarInfo()
    {
        Console.WriteLine("===============================================");
        Console.WriteLine("[CS2Refiller] Plugin successfully loaded!");
        Console.WriteLine($"[CS2Refiller] Version: {ModuleVersion}");
        Console.WriteLine("[CS2Refiller] Current settings:");
        Console.WriteLine($"[CS2Refiller]   AssistRefill = {Config.AssistRefill}");
        Console.WriteLine($"[CS2Refiller]   HealthRefill = {Config.HealthRefill}");
        Console.WriteLine($"[CS2Refiller]   AmmoRefill = {Config.AmmoRefill}");
        Console.WriteLine($"[CS2Refiller]   ArmorRefill = {Config.ArmorRefill}");
        Console.WriteLine("===============================================");
    }
    
    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;

        if (victim?.IsValid != true)
            return HookResult.Continue;

        // Собираем список игроков для награды
        var playersToReward = new List<CCSPlayerController?>
        {
            @event.Attacker
        };
        
        if (Config.AssistRefill && @event.Assister?.IsValid == true)
        {
            playersToReward.Add(@event.Assister);
        }

        // Выполняем в следующем кадре для безопасности
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
                                {
                                    RefillWeaponAmmo(activeWeapon);
                                }
                            }
                        }
                    }

                    // Восстановление здоровья
                    if (Config.HealthRefill != "0")
                    {
                        var currentHealth = pawn.Health;
                        var newHealth = currentHealth;

                        if (Config.HealthRefill == "all")
                        {
                            newHealth = 100;
                        }
                        else if (int.TryParse(Config.HealthRefill, out int healthAdd) && healthAdd > 0)
                        {
                            newHealth = Math.Min(100, currentHealth + healthAdd);
                        }

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
                        {
                            newArmor = 100;
                        }
                        else if (int.TryParse(Config.ArmorRefill, out int armorAdd) && armorAdd > 0)
                        {
                            newArmor = Math.Min(100, currentArmor + armorAdd);
                        }

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
                Console.WriteLine($"[CS2Refiller ERROR] Exception in refill: {ex.Message}");
            }
        });

        return HookResult.Continue;
    }
    
    private void RefillWeaponAmmo(CBasePlayerWeapon weapon)
    {
        try
        {
            var weaponBase = weapon.As<CCSWeaponBase>();
            if (weaponBase == null)
                return;

            var weaponData = weaponBase.VData;
            if (weaponData == null)
                return;

            // Восстанавливаем патроны в магазине
            weaponBase.Clip1 = weaponData.MaxClip1;
            
            // Восстанавливаем запасы патронов (исправлено)
            // ReserveAmmo это свойство, а не метод, нужно проверить его наличие
            var reserveAmmoProperty = weaponBase.GetType().GetProperty("ReserveAmmo");
            if (reserveAmmoProperty != null)
            {
                try
                {
                    // Получаем значение ReserveAmmo
                    var reserveAmmoValue = reserveAmmoProperty.GetValue(weaponBase);
                    
                    // Проверяем, это NetworkedVector или что-то подобное
                    if (reserveAmmoValue is System.Collections.IList reserveList && reserveList.Count > 0)
                    {
                        reserveList[0] = weaponData.PrimaryReserveAmmoMax;
                    }
                }
                catch
                {
                    // Если не получается установить резервные патроны, это не критично
                    Console.WriteLine($"[CS2Refiller WARNING] Could not set reserve ammo for weapon");
                }
            }
            
            // Обновляем состояние оружия
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CS2Refiller ERROR] Failed to refill weapon ammo: {ex.Message}");
        }
    }
    
    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        string helpMessage = """
            ===============================================
            CS2 REFILLER PLUGIN HELP
            ===============================================
            DESCRIPTION:
              Automatically refills health, ammo, and armor when getting a kill.
              Can also reward assistants with the same refill.

            CONFIGURATION:
              AssistRefill - Reward assistants with same refill as killer
              HealthRefill - "all", "0", or number (e.g., "20")
              AmmoRefill - "all", "current", or "off"
              ArmorRefill - "all", "0", or number (e.g., "10")

            COMMANDS:
              css_refiller_help - Show this help message
              css_refiller_settings - Show current plugin settings
              css_plugins reload CS2Refiller - Reload configuration file
            ===============================================
            """;
        
        if (player != null)
        {
            player.PrintToConsole(helpMessage);
            player.PrintToChat($"CS2Refiller v{ModuleVersion}: Check console for help");
        }
        else
        {
            Console.WriteLine(helpMessage);
        }
    }
    
    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        string settingsMessage = $"""
            ===============================================
            CS2 REFILLER CURRENT SETTINGS
            ===============================================
            Assist Refill: {Config.AssistRefill}
            Health Refill: {Config.HealthRefill}
            Ammo Refill: {Config.AmmoRefill}
            Armor Refill: {Config.ArmorRefill}
            ===============================================
            """;
        
        if (player != null)
        {
            player.PrintToConsole(settingsMessage);
            player.PrintToChat($"CS2Refiller: Assist={Config.AssistRefill}, Health={Config.HealthRefill}");
        }
        else
        {
            Console.WriteLine(settingsMessage);
        }
    }
}