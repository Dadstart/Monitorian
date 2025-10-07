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
                        Console.WriteLine("Usage: monitorian-cli set <brightness|contrast> <value> [monitor-id]");
                        return 1;
                    }
                    await SetValue(args[1], args[2], args.Length > 3 ? args[3] : null);
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
        Console.WriteLine("  monitorian-cli set <brightness|contrast> <value> [monitor-id] - Set values");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  monitorian-cli list");
        Console.WriteLine("  monitorian-cli get brightness");
        Console.WriteLine("  monitorian-cli set brightness 50");
        Console.WriteLine("  monitorian-cli set contrast 75");
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

    static async Task SetValue(string type, string value, string monitorId)
    {
        var monitors = await GetMonitorsAsync();

        if (!int.TryParse(value, out int intValue) || intValue < 0 || intValue > 100)
        {
            Console.Error.WriteLine("Value must be a number between 0 and 100.");
            return;
        }

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
                var result = monitor.SetBrightness(intValue);
                if (result.Status == AccessStatus.Succeeded)
                {
                    successCount++;
                    Console.WriteLine($"✓ Set brightness to {intValue} for {monitor.Description}");
                }
                else
                {
                    Console.Error.WriteLine($"✗ Failed to set brightness for {monitor.Description}: {result.Message}");
                }
            }
            else if (type == "contrast" && monitor.IsContrastSupported && monitor.IsReachable)
            {
                totalCount++;
                var result = monitor.SetContrast(intValue);
                if (result.Status == AccessStatus.Succeeded)
                {
                    successCount++;
                    Console.WriteLine($"✓ Set contrast to {intValue} for {monitor.Description}");
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
