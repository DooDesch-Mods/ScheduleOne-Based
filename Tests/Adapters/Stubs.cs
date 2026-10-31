using System.Text.Json;

namespace MelonLoader
{
    public static class MelonLogger
    {
        public static void Msg(string text) => Console.WriteLine(text);
        public static void Warning(string text) => Console.WriteLine("warn " + text);
        public static void Error(string text) => Console.WriteLine("error " + text);
    }
}
namespace MelonLoader.Utils
{
    public static class MelonEnvironment
    {
        public static string UserDataDirectory => Environment.GetEnvironmentVariable("BASED_TEST_USERDATA")
            ?? throw new InvalidOperationException("Adapter checks require an isolated journal directory.");
    }
}
namespace S1API.Money
{
    public static class Money
    {
        public static float Balance = 1000;
        public static int Changes;
        public static bool SuppressMutation;
        public static float GetCashBalance() => Balance;
        public static void ChangeCashBalance(float amount, bool visualizeChange = true, bool playCashSound = false)
        { Changes++; if (!SuppressMutation) Balance += amount; }
    }
}
namespace Based.Runtime
{
    public static class NativeBus
    {
        public static event Action<int, string>? HostReceived;
        public static event Action<string>? ClientReceived;
        public static bool IsHost = true;
        public static bool IsConnected = true;
        public static int LocalPeerId = 1;
        public static string LocalPlayerId = "buyer";
        public static string LastClient = "";
        public static string LastAuthorization = "";
        public static bool DropAcknowledgements;
        public static bool Start() => true;
        public static string PeerPlayerId(int peer) => peer == 1 ? "buyer" : "other";
        public static void SendToHost(string packet)
        {
            LastClient = packet;
            if (DropAcknowledgements && JsonDocument.Parse(packet[11..]).RootElement.GetProperty("Op").GetString() == "ack") return;
            HostReceived?.Invoke(1, packet);
        }
        public static void SendToPeer(int peer, string packet)
        {
            if (JsonDocument.Parse(packet[11..]).RootElement.GetProperty("Op").GetString() == "authorize") LastAuthorization = packet;
            ClientReceived?.Invoke(packet);
        }
        public static void ReplayClient(string packet, int peer = 1) => HostReceived?.Invoke(peer, packet);
        public static void ReplayHost(string packet) => ClientReceived?.Invoke(packet);
    }
}
