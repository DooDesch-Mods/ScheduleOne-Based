using Il2CppFishNet;
using Il2CppFishNet.Broadcast;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using System.Reflection;

namespace Based.Runtime;

public static class NativeBus
{
    private const int MaximumPacketLength = 65536;
    private static NetworkManager? _manager;
    private static bool _serializersReady;
    private static Il2CppSystem.Action<NetworkConnection, BasedClientPacket>? _serverReceiver;
    private static Il2CppSystem.Action<BasedHostPacket>? _clientReceiver;
    private static Il2CppSystem.Action<Writer, BasedClientPacket>? _clientWriter;
    private static Il2CppSystem.Func<Reader, BasedClientPacket>? _clientReader;
    private static Il2CppSystem.Action<Writer, BasedHostPacket>? _hostWriter;
    private static Il2CppSystem.Func<Reader, BasedHostPacket>? _hostReader;
    private static string? _outgoingClient;
    private static string? _outgoingHost;
    private static readonly Queue<string> IncomingClient = new();
    private static readonly Queue<string> IncomingHost = new();
    private static MethodInfo? _sendToConnection;

    public static event Action<int, string>? HostReceived;
    public static event Action<string>? ClientReceived;
    public static bool IsHost => InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServer;
    public static bool IsConnected => InstanceFinder.NetworkManager != null &&
        (InstanceFinder.NetworkManager.IsServer || InstanceFinder.NetworkManager.IsClient);
    public static int LocalPeerId => Player.Local?.Connection?.ClientId ?? -1;
    public static string LocalPlayerId => Player.Local?.PlayerCode ?? "";

    public static string PeerPlayerId(int peerId)
    {
        var players = Player.PlayerList;
        for (int i = 0; players != null && i < players.Count; i++)
        {
            var player = players[i];
            if (player != null && player.Connection != null && player.Connection.ClientId == peerId)
                return player.PlayerCode ?? "";
        }
        return "";
    }

    public static bool Start()
    {
        var manager = InstanceFinder.NetworkManager;
        if (manager == null) return false;
        if (_manager != null && _manager.Pointer == manager.Pointer) return true;
        Stop();
        ConfigureSerializers();
        _serverReceiver ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action<NetworkConnection, BasedClientPacket>>(
            new Action<NetworkConnection, BasedClientPacket>(ReceiveClient));
        _clientReceiver ??= DelegateSupport.ConvertDelegate<Il2CppSystem.Action<BasedHostPacket>>(
            new Action<BasedHostPacket>(ReceiveHost));
        manager.ServerManager.RegisterBroadcast<BasedClientPacket>(_serverReceiver, requireAuthentication: true);
        try { manager.ClientManager.RegisterBroadcast<BasedHostPacket>(_clientReceiver); }
        catch
        {
            manager.ServerManager.UnregisterBroadcast<BasedClientPacket>(_serverReceiver);
            throw;
        }
        _manager = manager;
        MelonLogger.Msg("[Based/Net] authenticated broadcast handlers registered.");
        return true;
    }

    public static void Stop()
    {
        if (_manager == null) return;
        var manager = _manager;
        _manager = null;
        try { manager.ServerManager.UnregisterBroadcast<BasedClientPacket>(_serverReceiver); }
        catch (Exception e) { MelonLogger.Warning("[Based/Net] server unregister failed: " + e); }
        try { manager.ClientManager.UnregisterBroadcast<BasedHostPacket>(_clientReceiver); }
        catch (Exception e) { MelonLogger.Warning("[Based/Net] client unregister failed: " + e); }
        IncomingClient.Clear(); IncomingHost.Clear();
        _outgoingClient = null; _outgoingHost = null;
    }

    public static void SendToHost(string packet)
    {
        CheckPacket(packet);
        if (!Start() || !IsConnected) throw new InvalidOperationException("Based host transport is not connected.");
        if (IsHost) { DispatchHost(LocalPeerId, packet); return; }
        if (_outgoingClient != null) throw new InvalidOperationException("Overlapping Based client serialization.");
        _outgoingClient = packet;
        try { _manager!.ClientManager.Broadcast(new BasedClientPacket(), Channel.Reliable); }
        finally { _outgoingClient = null; }
    }

    public static void SendToPeer(int peerId, string packet)
    {
        CheckPacket(packet);
        if (!Start() || !IsHost) throw new InvalidOperationException("Only the Based host can send peer state.");
        if (peerId == LocalPeerId) { DispatchClient(packet); return; }
        if (!_manager!.ServerManager.Clients.TryGetValue(peerId, out NetworkConnection connection) || connection == null || !connection.Authenticated)
            throw new InvalidOperationException("Based peer is absent or unauthenticated: " + peerId);
        if (_outgoingHost != null) throw new InvalidOperationException("Overlapping Based host serialization.");
        _outgoingHost = packet;
        try
        {
            _sendToConnection ??= _manager.ServerManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Single(method =>
            {
                var parameters = method.GetParameters();
                return method.Name == "Broadcast" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 &&
                    parameters.Length == 4 && parameters[0].ParameterType == typeof(NetworkConnection) &&
                    parameters[1].ParameterType.IsGenericParameter && parameters[2].ParameterType == typeof(bool) && parameters[3].ParameterType == typeof(Channel);
            });
            _sendToConnection.MakeGenericMethod(typeof(BasedHostPacket)).Invoke(_manager.ServerManager,
                new object[] { connection, new BasedHostPacket(), true, Channel.Reliable });
        }
        finally { _outgoingHost = null; }
    }

    public static void Broadcast(string packet)
    {
        CheckPacket(packet);
        if (!Start() || !IsHost) throw new InvalidOperationException("Only the Based host can broadcast state.");
        foreach (var pair in _manager!.ServerManager.Clients)
            if (pair.Value != null && pair.Value.Authenticated && pair.Key != LocalPeerId) SendToPeer(pair.Key, packet);
        DispatchClient(packet);
    }

    private static void ConfigureSerializers()
    {
        if (_serializersReady) return;
        _clientWriter = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Writer, BasedClientPacket>>(
            new Action<Writer, BasedClientPacket>((writer, _) => writer.WriteString(TakeOutgoing(ref _outgoingClient))));
        _clientReader = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Reader, BasedClientPacket>>(
            new Func<Reader, BasedClientPacket>(reader => { IncomingClient.Enqueue(ReadPacket(reader)); return new BasedClientPacket(); }));
        _hostWriter = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Writer, BasedHostPacket>>(
            new Action<Writer, BasedHostPacket>((writer, _) => writer.WriteString(TakeOutgoing(ref _outgoingHost))));
        _hostReader = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Reader, BasedHostPacket>>(
            new Func<Reader, BasedHostPacket>(reader => { IncomingHost.Enqueue(ReadPacket(reader)); return new BasedHostPacket(); }));
        GenericWriter<BasedClientPacket>.Write = _clientWriter;
        GenericReader<BasedClientPacket>.Read = _clientReader;
        GenericWriter<BasedHostPacket>.Write = _hostWriter;
        GenericReader<BasedHostPacket>.Read = _hostReader;
        _serializersReady = true;
    }

    private static string TakeOutgoing(ref string? slot)
    {
        string packet = slot ?? throw new InvalidOperationException("Based serialization has no prepared packet.");
        slot = null;
        return packet;
    }
    private static string ReadPacket(Reader reader) { string packet = reader.ReadString(); CheckPacket(packet); return packet; }
    private static void CheckPacket(string packet)
    {
        if (packet == null || packet.Length == 0 || packet.Length > MaximumPacketLength)
            throw new ArgumentException("Invalid Based packet length.");
    }
    private static void ReceiveClient(NetworkConnection connection, BasedClientPacket _)
    {
        if (IncomingClient.Count == 0) { MelonLogger.Error("[Based/Net] client packet has no managed payload."); return; }
        string packet = IncomingClient.Dequeue();
        if (!IsHost || connection == null || !connection.Authenticated)
        { MelonLogger.Warning("[Based/Net] rejected unauthenticated client packet."); return; }
        DispatchHost(connection.ClientId, packet);
    }
    private static void ReceiveHost(BasedHostPacket _)
    {
        if (IncomingHost.Count == 0) { MelonLogger.Error("[Based/Net] host packet has no managed payload."); return; }
        DispatchClient(IncomingHost.Dequeue());
    }
    private static void DispatchHost(int peerId, string packet)
    {
        foreach (Action<int, string> handler in HostReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { handler(peerId, packet); }
            catch (Exception e) { MelonLogger.Error("[Based/Net] host handler failed: " + e); }
    }
    private static void DispatchClient(string packet)
    {
        foreach (Action<string> handler in ClientReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { handler(packet); }
            catch (Exception e) { MelonLogger.Error("[Based/Net] client handler failed: " + e); }
    }
}

[RegisterTypeInIl2Cpp]
[Il2CppImplements(typeof(IBroadcast))]
public sealed class BasedClientPacket : Il2CppSystem.Object
{
    public BasedClientPacket(IntPtr pointer) : base(pointer) { }
    public BasedClientPacket() : base(ClassInjector.DerivedConstructorPointer<BasedClientPacket>()) => ClassInjector.DerivedConstructorBody(this);
}

[RegisterTypeInIl2Cpp]
[Il2CppImplements(typeof(IBroadcast))]
public sealed class BasedHostPacket : Il2CppSystem.Object
{
    public BasedHostPacket(IntPtr pointer) : base(pointer) { }
    public BasedHostPacket() : base(ClassInjector.DerivedConstructorPointer<BasedHostPacket>()) => ClassInjector.DerivedConstructorBody(this);
}
