using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;

namespace Monitorian.CLI;

class Program
{
    private enum ControlType
    {
        Brightness,
        Contrast
    }

    static Task<int> Main(string[] args)
    {
        var rootCommand = BuildCommandLine();
        return rootCommand.InvokeAsync(args);
    }

    private static RootCommand BuildCommandLine()
    {
        var rootCommand = new RootCommand("Monitorian CLI - Control monitor brightness and contrast.");

        rootCommand.AddCommand(BuildListCommand());
        rootCommand.AddCommand(BuildGetCommand());
        rootCommand.AddCommand(BuildSetCommand());

        return rootCommand;
    }

    private static Command BuildListCommand()
    {
        var command = new Command("list", "List available monitors and their capabilities.");
        command.SetHandler(async () => await ListMonitors());
        return command;
    }

    private static Command BuildGetCommand()
    {
        var command = new Command("get", "Get brightness and/or contrast values.");

        var brightnessOption = new Option<bool>(aliases: new[] { "--brightness", "-b" }, description: "Get brightness values.");
        var contrastOption = new Option<bool>(aliases: new[] { "--contrast", "-c" }, description: "Get contrast values.");
        var monitorOption = CreateMonitorOption();

        command.AddOption(brightnessOption);
        command.AddOption(contrastOption);
        command.AddOption(monitorOption);

        command.AddValidator(result =>
        {
            var brightness = result.GetValueForOption(brightnessOption);
            var contrast = result.GetValueForOption(contrastOption);

            if (!brightness && !contrast)
            {
                result.ErrorMessage = "Specify at least one of --brightness or --contrast.";
            }
        });

        command.SetHandler(async (bool brightness, bool contrast, string? monitorId) =>
        {
            var types = new List<ControlType>();
            if (brightness)
            {
                types.Add(ControlType.Brightness);
            }

            if (contrast)
            {
                types.Add(ControlType.Contrast);
            }

            foreach (var type in types)
            {
                await GetValue(type, monitorId);
            }
        }, brightnessOption, contrastOption, monitorOption);

        return command;
    }

    private static Command BuildSetCommand()
    {
        var command = new Command("set", "Set brightness and/or contrast values.");

        var brightnessOption = new Option<int?>(aliases: new[] { "--brightness", "-b" }, description: "Set brightness to a value between 0 and 100.");
        var contrastOption = new Option<int?>(aliases: new[] { "--contrast", "-c" }, description: "Set contrast to a value between 0 and 100.");
        var monitorOption = CreateMonitorOption();

        AddPercentageValidator(brightnessOption, "Brightness");
        AddPercentageValidator(contrastOption, "Contrast");

        command.AddOption(brightnessOption);
        command.AddOption(contrastOption);
        command.AddOption(monitorOption);

        command.AddValidator(result =>
        {
            var brightness = result.GetValueForOption(brightnessOption);
            var contrast = result.GetValueForOption(contrastOption);

            if (brightness is null && contrast is null)
            {
                result.ErrorMessage = "Specify at least one of --brightness or --contrast.";
            }
        });

        command.SetHandler(async (int? brightness, int? contrast, string? monitorId) =>
        {
            var requests = new List<(ControlType type, int value)>();

            if (brightness is int brightnessValue)
            {
                requests.Add((ControlType.Brightness, brightnessValue));
            }

            if (contrast is int contrastValue)
            {
                requests.Add((ControlType.Contrast, contrastValue));
            }

            foreach (var (type, value) in requests)
            {
                await SetValue(type, value, monitorId);
            }
        }, brightnessOption, contrastOption, monitorOption);

        return command;
    }

    private static Option<string?> CreateMonitorOption()
    {
        return new Option<string?>(aliases: new[] { "--monitor", "-m" }, description: "Target monitor device instance ID.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
    }

    private static void AddPercentageValidator(Option<int?> option, string label)
    {
        option.AddValidator(result =>
        {
            var value = result.GetValueOrDefault<int?>();
            if (value is { } typedValue && (typedValue < 0 || typedValue > 100))
            {
                result.ErrorMessage = $"{label} value must be between 0 and 100.";
            }
        });
    }

    private static async Task ListMonitors()
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

    private static async Task GetValue(ControlType type, string? monitorId)
    {
        var monitors = await GetMonitorsAsync();

        if (string.IsNullOrWhiteSpace(monitorId))
        {
            foreach (var monitor in monitors)
            {
                if (type == ControlType.Brightness && monitor.IsBrightnessSupported)
                {
                    Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Brightness} B");
                }
                else if (type == ControlType.Contrast && monitor.IsContrastSupported)
                {
                    Console.WriteLine($"{monitor.DeviceInstanceId} {monitor.Description} {monitor.Contrast} C");
                }
            }
            return;
        }

        var monitorMatch = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceInstanceId, monitorId, StringComparison.OrdinalIgnoreCase));

        if (monitorMatch is null)
        {
            Console.Error.WriteLine($"Monitor with ID '{monitorId}' not found.");
            return;
        }

        if (type == ControlType.Brightness && monitorMatch.IsBrightnessSupported)
        {
            Console.WriteLine($"{monitorMatch.DeviceInstanceId} {monitorMatch.Description} {monitorMatch.Brightness} B");
        }
        else if (type == ControlType.Contrast && monitorMatch.IsContrastSupported)
        {
            Console.WriteLine($"{monitorMatch.DeviceInstanceId} {monitorMatch.Description} {monitorMatch.Contrast} C");
        }
        else
        {
            Console.Error.WriteLine($"Monitor does not support {GetLabel(type)} control.");
        }
    }

    private static async Task SetValue(ControlType type, int value, string? monitorId)
    {
        var monitors = await GetMonitorsAsync();

        var targetMonitors = string.IsNullOrWhiteSpace(monitorId)
            ? monitors
            : monitors.Where(m => string.Equals(m.DeviceInstanceId, monitorId, StringComparison.OrdinalIgnoreCase));

        var successCount = 0;
        var totalCount = 0;

        foreach (var monitor in targetMonitors)
        {
            if (type == ControlType.Brightness && monitor.IsBrightnessSupported && monitor.IsReachable)
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
            else if (type == ControlType.Contrast && monitor.IsContrastSupported && monitor.IsReachable)
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
            Console.Error.WriteLine($"No controllable monitors found for {GetLabel(type)}.");
        }
        else
        {
            Console.WriteLine($"Set {GetLabel(type)} for {successCount}/{totalCount} monitors");
        }
    }

    private static string GetLabel(ControlType type) =>
        type switch
        {
            ControlType.Brightness => "brightness",
            ControlType.Contrast => "contrast",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

    private static async Task<List<IMonitor>> GetMonitorsAsync()
    {
        var monitors = await MonitorManager.EnumerateMonitorsAsync(TimeSpan.FromSeconds(10));
        return monitors.ToList();
    }
}
