using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Monitorian.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        try
        {
            var command = args[0].ToLower();

            switch (command)
            {
                case "list":
                    await ListMonitors();
                    break;
                case "get":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: monitorian-cli get <brightness|contrast> [monitor-id]");
                        return 1;
                    }
                    await GetValue(args[1], args.Length > 2 ? args[2] : null);
                    break;
                case "set":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: monitorian-cli set <brightness|contrast> <value> [brightness|contrast <value>]... [monitor-id]");
                        return 1;
                    }
                    await SetMultipleValues(args.Skip(1).ToArray());
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    static void ShowHelp()
    {
        Console.WriteLine("Monitorian CLI - Command-line tool for controlling monitor brightness and contrast");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  monitorian-cli list                                           - List all monitors");
        Console.WriteLine("  monitorian-cli get <brightness|contrast> [monitor-id]         - Get current values");
        Console.WriteLine("  monitorian-cli set <brightness|contrast> <value> [brightness|contrast <value>]... [monitor-id] - Set values");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  monitorian-cli list");
        Console.WriteLine("  monitorian-cli get brightness");
        Console.WriteLine("  monitorian-cli set brightness 50");
        Console.WriteLine("  monitorian-cli set contrast 75");
        Console.WriteLine("  monitorian-cli set brightness 50 contrast 75");
        Console.WriteLine("  monitorian-cli set brightness 50 contrast 75 <monitor-id>");
    }

    static async Task ListMonitors()
    {
        Console.WriteLine("Scanning for monitors...");

        var monitors = await GetMonitorsAsync();

        if (monitors.Count == 0)
        {
            Console.WriteLine("No monitors found.");
            return;
        }

        Console.WriteLine($"Found {monitors.Count} monitor(s):");
        Console.WriteLine();

        foreach (var monitor in monitors)
        {
            Console.WriteLine($"Device ID: {monitor.DeviceInstanceId}");
            Console.WriteLine($"Name: {monitor.Description}");
            Console.WriteLine($"Brightness: {monitor.Brightness}% (Supported: {(monitor.IsBrightnessSupported ? "Yes" : "No")})");
            Console.WriteLine($"Contrast: {(monitor.IsContrastSupported ? $"{monitor.Contrast}%" : "Not supported")}");
            Console.WriteLine($"Reachable: {(monitor.IsReachable ? "Yes" : "No")}");
            Console.WriteLine();
        }
    }

    static async Task GetValue(string type, string monitorId)
    {
        var monitors = await GetMonitorsAsync();

        if (string.IsNullOrEmpty(monitorId))
        {
            // Get all monitors
            foreach (var monitor in monitors)
            {
                if (type == "brightness" && monitor.IsBrightnessSupported)
                {
                    Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Brightness} B");
                }
                else if (type == "contrast" && monitor.IsContrastSupported)
                {
                    Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Contrast} C");
                }
            }
        }
        else
        {
            // Get specific monitor
            var monitor = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceInstanceId, monitorId, StringComparison.OrdinalIgnoreCase));

            if (monitor == null)
            {
                Console.Error.WriteLine($"Monitor with ID '{monitorId}' not found.");
                return;
            }

            if (type == "brightness" && monitor.IsBrightnessSupported)
            {
                Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Brightness} B");
            }
            else if (type == "contrast" && monitor.IsContrastSupported)
            {
                Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Contrast} C");
            }
            else
            {
                Console.Error.WriteLine($"Monitor does not support {type} control.");
            }
        }
    }

    static async Task SetMultipleValues(string[] args)
    {
        // Parse arguments into type-value pairs and optional monitor-id
        var settings = new List<(string type, int value)>();
        string monitorId = null;

        int i = 0;
        while (i < args.Length)
        {
            string arg = args[i].ToLower();

            // Check if this is a type (brightness or contrast)
            if (arg == "brightness" || arg == "contrast")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine($"Missing value for {arg}.");
                    return;
                }

                if (!int.TryParse(args[i + 1], out int value) || value < 0 || value > 100)
                {
                    Console.Error.WriteLine($"Value for {arg} must be a number between 0 and 100.");
                    return;
                }

                settings.Add((arg, value));
                i += 2;
            }
            else
            {
                // If it's not a type, assume it's the monitor-id (must be last argument)
                if (i == args.Length - 1)
                {
                    monitorId = args[i];
                    break;
                }
                else
                {
                    Console.Error.WriteLine($"Unexpected argument: {args[i]}. Expected 'brightness', 'contrast', or monitor-id.");
                    return;
                }
            }
        }

        if (settings.Count == 0)
        {
            Console.Error.WriteLine("No valid settings specified. Use 'brightness <value>' and/or 'contrast <value>'.");
            return;
        }

        // Execute each setting
        foreach (var (type, value) in settings)
        {
            await SetValue(type, value, monitorId);
        }
    }

    static async Task SetValue(string type, int value, string monitorId)
    {
        var monitors = await GetMonitorsAsync();

        var targetMonitors = string.IsNullOrEmpty(monitorId)
            ? monitors
            : monitors.Where(m => string.Equals(m.DeviceInstanceId, monitorId, StringComparison.OrdinalIgnoreCase));

        var successCount = 0;
        var totalCount = 0;

        foreach (var monitor in targetMonitors)
        {
            if (type == "brightness" && monitor.IsBrightnessSupported && monitor.IsReachable)
            {
                totalCount++;
                var result = monitor.SetBrightness(value);
                if (result.Status == AccessStatus.Succeeded)
                {
                    successCount++;
                    Console.WriteLine($"✓ Set brightness to {value} for {monitor.Description}");
                }
                else
                {
                    Console.Error.WriteLine($"✗ Failed to set brightness for {monitor.Description}: {result.Message}");
                }
            }
            else if (type == "contrast" && monitor.IsContrastSupported && monitor.IsReachable)
            {
                totalCount++;
                var result = monitor.SetContrast(value);
                if (result.Status == AccessStatus.Succeeded)
                {
                    successCount++;
                    Console.WriteLine($"✓ Set contrast to {value} for {monitor.Description}");
                }
                else
                {
                    Console.Error.WriteLine($"✗ Failed to set contrast for {monitor.Description}: {result.Message}");
                }
            }
        }

        if (totalCount == 0)
        {
            Console.Error.WriteLine($"No controllable monitors found for {type}.");
        }
        else
        {
            Console.WriteLine($"Set {type} for {successCount}/{totalCount} monitors");
        }
    }

    static async Task<List<IMonitor>> GetMonitorsAsync()
    {
        var monitors = await MonitorManager.EnumerateMonitorsAsync(TimeSpan.FromSeconds(10));
        return monitors.ToList();
    }
}
