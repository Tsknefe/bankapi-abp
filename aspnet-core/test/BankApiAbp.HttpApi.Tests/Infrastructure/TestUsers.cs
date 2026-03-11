namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestUsers
{
    public const string BasicUsername = "test_basic";
    public const string RateLimitUsername = "test_ratelimit";
    public const string ConcurrentUsername = "test_concurrent";

    public const string Password = "Qwe123!";

    public static readonly Guid BasicAccountA =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid BasicAccountB =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static readonly Guid RateLimitAccountA =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid RateLimitAccountB =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static readonly Guid ConcurrentAccountA =
        Guid.Parse("30000000-0000-0000-0000-000000000001");

    public static readonly Guid ConcurrentAccountB =
        Guid.Parse("30000000-0000-0000-0000-000000000002");
}