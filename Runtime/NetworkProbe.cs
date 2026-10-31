#if DEBUG
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.PlayerScripts;
using System.Security.Cryptography;
using System.Text;

namespace Based.Runtime;

public static class NetworkProbe
{
    public static void Execute()
    {
        var manager = InstanceFinder.NetworkManager;
        if (manager == null) { Mod.Log("network probe manager absent"); return; }
        Mod.Log($"network probe pid={Environment.ProcessId} server={manager.IsServer} client={manager.IsClient} connection={Connection(manager.ClientManager?.Connection)} localPlayer={Player.Local != null} playerList={Player.PlayerList?.Count ?? 0}");
        int peers = 0;
        foreach (var peer in manager.ServerManager.Clients)
        {
            if (peers++ < 8) Mod.Log($"network peer key={peer.Key} {Connection(peer.Value)}");
        }
        Mod.Log($"network server peers={peers} shown={Math.Min(peers, 8)}");
        var players = UnityEngine.Object.FindObjectsOfType<Player>(true);
        Mod.Log($"network player objects={players.Length}");
        for (int i = 0; i < Math.Min(players.Length, 8); i++)
        {
            var player = players[i];
            var obj = player.NetworkObject;
            Mod.Log($"network player[{i}] name={Clean(player.name)} active={player.gameObject.activeInHierarchy} local={(Player.Local != null && Player.Local.Pointer == player.Pointer)} isOwner={player.IsOwner} connection={Connection(player.Connection)} owner={Connection(player.Owner)} objectId={obj?.ObjectId ?? -1} prefabId={(obj == null ? -1 : obj.PrefabId)} spawned={obj?.IsSpawned ?? false}");
        }

        var prefabs = manager.SpawnablePrefabs;
        if (prefabs == null) throw new InvalidOperationException("Network probe SpawnablePrefabs absent.");
        int count = prefabs.GetObjectCount();
        using var manifest = new MemoryStream();
        using var writer = new BinaryWriter(manifest, Encoding.UTF8, leaveOpen: true);
        writer.Write("Based.NetworkPrefabManifest.v1");
        writer.Write((int)prefabs.CollectionId);
        writer.Write(count);
        int custom = 0, playerPrefabs = 0, missing = 0;
        for (int i = 0; i < count; i++)
        {
            var prefab = prefabs.GetObject(manager.IsServer, i);
            writer.Write(i);
            writer.Write(prefab != null);
            if (prefab == null) { missing++; continue; }
            string name = prefab.name ?? "";
            writer.Write(name);
            writer.Write((int)prefab.PrefabId);
            var behaviours = prefab.NetworkBehaviours;
            int behaviourCount = behaviours?.Length ?? -1;
            writer.Write(behaviourCount);
            for (int b = 0; b < behaviourCount; b++)
            {
                var behaviour = behaviours![b];
                writer.Write(behaviour != null);
                if (behaviour == null) continue;
                writer.Write(behaviour.GetIl2CppType().FullName ?? "");
                writer.Write(RelativePath(prefab.transform, behaviour.transform));
            }
            bool isPlayer = prefab.GetComponent<Player>() != null;
            bool isCustom = name.StartsWith("S1API_", StringComparison.Ordinal);
            if (isPlayer) playerPrefabs++;
            if (isCustom) custom++;
            if (isPlayer || (isCustom && custom <= 16))
                Mod.Log($"network prefab index={i} id={prefab.PrefabId} player={isPlayer} name={Clean(name)} behaviours={behaviourCount}");
        }
        writer.Flush();
        string hash = Convert.ToHexString(SHA256.HashData(manifest.ToArray())).ToLowerInvariant();
        Mod.Log($"network manifest version=1 collection={prefabs.CollectionId} total={count} missing={missing} playerPrefabs={playerPrefabs} s1apiPrefabs={custom} customShown={Math.Min(custom, 16)} sha256={hash}");
    }

    private static string Connection(NetworkConnection? value) => value == null ? "absent" :
        $"id:{value.ClientId},valid:{value.IsValid},authenticated:{value.Authenticated},local:{value.IsLocalClient},host:{value.IsHost}";

    private static string RelativePath(UnityEngine.Transform root, UnityEngine.Transform child)
    {
        var parts = new Stack<string>();
        for (var current = child; current != null && current.Pointer != root.Pointer; current = current.parent)
            parts.Push(current.GetSiblingIndex() + ":" + current.name);
        return string.Join("/", parts);
    }

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
#endif
