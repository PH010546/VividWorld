namespace VividWorld.Core.Memory
{
    public enum DormancyKind
    {
        AllAtMaxHop,
        LifetimeExpired,
        Stale,
        AllForgotten,
        AllOutdated
    }

    public sealed class DormancyReason
    {
        public DormancyKind Kind { get; }
        public int Count { get; }
        public int MaxHop { get; }
        public double Age { get; }
        public double Lifetime { get; }
        public double Idle { get; }
        public double StaleThreshold { get; }
        public int OutdatedCount { get; }
        public int ForgottenCount { get; }

        private DormancyReason(
            DormancyKind kind,
            int count = 0,
            int maxHop = 0,
            double age = 0,
            double lifetime = 0,
            double idle = 0,
            double staleThreshold = 0,
            int outdatedCount = 0,
            int forgottenCount = 0)
        {
            Kind = kind;
            Count = count;
            MaxHop = maxHop;
            Age = age;
            Lifetime = lifetime;
            Idle = idle;
            StaleThreshold = staleThreshold;
            OutdatedCount = outdatedCount;
            ForgottenCount = forgottenCount;
        }

        public static DormancyReason AllAtMaxHop(int count, int maxHop) =>
            new(DormancyKind.AllAtMaxHop, count: count, maxHop: maxHop);

        public static DormancyReason ForMaxHop(int count, int maxHop) => AllAtMaxHop(count, maxHop);

        public static DormancyReason LifetimeExpired(double age, double lifetime) =>
            new(DormancyKind.LifetimeExpired, age: age, lifetime: lifetime);

        public static DormancyReason ForLifetime(double age, double lifetime) => LifetimeExpired(age, lifetime);

        public static DormancyReason Stale(double idle, double staleThreshold) =>
            new(DormancyKind.Stale, idle: idle, staleThreshold: staleThreshold);

        public static DormancyReason ForStale(double idle, double staleThreshold) => Stale(idle, staleThreshold);

        public static DormancyReason AllForgotten(int count) =>
            new(DormancyKind.AllForgotten, count: count);

        public static DormancyReason ForAllForgotten(int count) => AllForgotten(count);

        public static DormancyReason AllOutdated(int count, int outdatedCount, int forgottenCount) =>
            new(DormancyKind.AllOutdated, count: count, outdatedCount: outdatedCount, forgottenCount: forgottenCount);

        public static DormancyReason ForAllOutdated(int count, int outdatedCount, int forgottenCount) =>
            AllOutdated(count, outdatedCount, forgottenCount);
    }
}
