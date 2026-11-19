namespace Badger.Classes
{
    public static class GuildID
    {
        public const ulong TTS = 343867882264068098;
    }
    public static class ChannelID
    {
        public const ulong BOTLOGS = 1144368862507303003;
        public const ulong LTP = 343875614379474957;
    }
    public static class RoleID
    {
        public const ulong INACTIVE = 1267913643807211520;
        public const ulong ACTIVE = 1267913695518785606;
    }
    public static class RaidGuard
    {
#if DEBUG
        public const int ChannelThreshold = 2;
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
#else
        public const int ChannelThreshold = 3;
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
#endif
        public const int BanDeleteMessageDays = 7;
    }
}
