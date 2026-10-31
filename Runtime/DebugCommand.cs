#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Based.UI;
using Based.Content.Ming;
using S1API.Console;

namespace Based.Runtime;

public sealed class BasedDebugCommand : BaseConsoleCommand
{
    public override string CommandWord => "baseddiag";
    public override string CommandDescription => "Based construction state and isolated runtime diagnostics.";
    public override string ExampleUsage => "baseddiag status | plan | dock | room";

    public override void ExecuteCommand(List<string> args)
    {
        string action = args.Count == 0 ? "status" : args[0];
        try
        {
            switch (action)
            {
                case "status":
                    Mod.Log("baseddiag: " + BuildPlan.State());
                    break;
                case "plan":
                    Mod.DebugPlan();
                    Mod.Log("baseddiag: plan opened through the Ming conversation.");
                    break;
                case "dock":
                    DockProbe.Execute(args.Skip(1).ToArray());
                    break;
                case "room":
                    RoomProbe.Execute(args.Skip(1).ToArray());
                    break;
                case "workers":
                    MingWorkers.Diagnostics();
                    break;
                case "network":
                    NetworkProbe.Execute();
                    break;
                case "packdoorway":
                    PackTestDoorway();
                    break;
                default:
                    throw new ArgumentException(ExampleUsage);
            }
        }
        catch (Exception error) { Mod.Warn("baseddiag " + action + " failed: " + error); }
    }

    private static void PackTestDoorway()
    {
        string location = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string[] allowed = {
            @"F:\Projects\Mods\Schedule1\_MPTest\Schedule I\Schedule I.exe",
            @"F:\Projects\Mods\Schedule1\_MPTest4\Schedule I\Schedule I.exe"
        };
        if (!NativeBus.IsHost || !allowed.Contains(location, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Doorway packing is restricted to the two isolated Based host test slots.");
        foreach (var peer in Il2CppFishNet.InstanceFinder.ServerManager.Clients)
            if (!peer.Value.IsHost) throw new InvalidOperationException("Doorway packing requires an isolated host without remote players.");
        foreach (var item in Mod.DoorwayItems())
        {
            string name = item.GetManagementName();
            if (!item.CanBePickedUp(out string reason)) throw new InvalidOperationException("Cannot pack " + name + ": " + reason);
            item.PickupItem();
            if (!item.IsDestroyed) throw new InvalidOperationException("Native pickup was not confirmed for " + name);
            Mod.Log("baseddiag: native pickup returned " + name + " to the test player's inventory.");
        }
        if (!Mod.DoorwayClear(out string blocked)) throw new InvalidOperationException(blocked);
        Mod.Log("baseddiag: test doorway is clear; no item was deleted outside native pickup.");
    }
}
#endif
