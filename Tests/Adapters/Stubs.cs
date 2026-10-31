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
namespace DooDesch.Localization
{
    // Always answers in German and records keys without a translation, so a check fails on a missing entry.
    internal static class L10n
    {
        private static Dictionary<string, string> _german = new();
        internal static readonly HashSet<string> Missing = new();
        internal static void Register(string language, Dictionary<string, string> table) { if (language == "de") _german = table; }
        internal static string T(string english, params object[] arguments)
        {
            if (_german.TryGetValue(english, out string? german)) return string.Format(german, arguments);
            Missing.Add(english);
            return string.Format(english, arguments);
        }
    }
}
namespace S1API.Money
{
    public static class Money
    {
        public static float Balance = 1000;
        public static int Changes;
        public static bool SuppressMutation;
        public static float RefundBonus;
        public static float Cap = float.MaxValue;
        public static Action? BeforeChange;
        public static float GetCashBalance() => Balance;
        public static void ChangeCashBalance(float amount, bool visualizeChange = true, bool playCashSound = false)
        {
            BeforeChange?.Invoke();
            Changes++;
            if (SuppressMutation) return;
            Balance = Math.Min(Balance + amount + (amount > 0 ? RefundBonus : 0), Cap);
        }
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
        public static bool DropEverything;
        public static bool StartThrows;
        public static int SentToHost;
        public static readonly HashSet<string> ConnectedPlayers = new() { "buyer", "other" };
        public static bool Start() => StartThrows ? throw new ArgumentException("Raw transport failure.") : true;
        public static string PeerPlayerId(int peer) => peer == 1 ? "buyer" : "other";
        public static bool IsPlayerConnected(string playerId) => ConnectedPlayers.Contains(playerId);
        public static void SendToHost(string packet)
        {
            LastClient = packet;
            SentToHost++;
            if (DropEverything) return;
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
