namespace BankApiAbp.HttpApi.Tests.Infrastructure;

public static class TestUsers
{
    public const string BasicUsername = "test_basic";
    public const string RateLimitUsername = "test_ratelimit";
    public const string ConcurrentUsername = "test_concurrent";

    public const string Password = "1q2w3E*";

    public static readonly Guid BasicUserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid RateLimitUserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid ConcurrentUserId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid BasicCustomerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");

    public static readonly Guid RateLimitCustomerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");

    public static readonly Guid ConcurrentCustomerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");

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