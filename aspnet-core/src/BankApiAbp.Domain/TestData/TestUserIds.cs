using System;

namespace BankApiAbp.TestData;

public static class TestUserIds
{
    public static readonly Guid TestBasicUserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid TestRateLimitUserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid TestConcurrentUserId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid TestBasicCustomerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static readonly Guid TestRateLimitCustomerId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static readonly Guid TestConcurrentCustomerId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
}